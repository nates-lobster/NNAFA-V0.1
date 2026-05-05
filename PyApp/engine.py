import numpy as np
from pylsl import StreamInlet, resolve_byprop, StreamInfo, StreamOutlet, local_clock
from scipy.signal import firwin, iirnotch, lfilter, welch
from scipy.signal.windows import hann
import time
import asyncio
import websockets
import json
import sys

# Force unbuffered output
def log(message):
    print(message)
    sys.stdout.flush()

# Constants
FS = 256
FIR_TAPS = 513
BUFFER_SAMPLES = 1024
CHANNELS = 4

TP9_INDEX = 0
AF7_INDEX = 1
AF8_INDEX = 2
TP10_INDEX = 3
INDEX_MAP = {"tp9": 0, "af7": 1, "af8": 2, "tp10": 3}

# Global state
last_valid_metrics = None
connected_clients = set()
ARTIFACT_THRESHOLD = 150.0
LOW_CUT = 1.0
HIGH_CUT = 40.0
GLOBAL_GAIN = 1.0
CONFIG_CHANGED = False

BAND_LIMITS = {
    "delta": (1.0, 4.0),
    "theta": (4.0, 8.0),
    "alpha": (8.0, 12.0),
    "beta": (12.0, 30.0),
    "gamma": (30.0, 40.0)
}

# --- LSL SETUP ---
metrics_info = StreamInfo(name='NeuroMemory_Metrics', type='EEG_Metrics', channel_count=6, 
                  nominal_srate=10.0, channel_format='float32', source_id='neuromemory_metrics_01')
metrics_outlet = StreamOutlet(metrics_info)

marker_info = StreamInfo(name='NeuroMemory_Status', type='Markers', channel_count=1, 
                         nominal_srate=0, channel_format='string', source_id='neuromemory_status_01')
marker_outlet = StreamOutlet(marker_info)

def create_fir_filters(fs):
    nyq = 0.5 * fs
    b_notch, a_notch = iirnotch(60, 30, fs)
    taps_main = firwin(FIR_TAPS, [LOW_CUT, HIGH_CUT], pass_zero=False, fs=fs, window='blackman')
    band_taps = {}
    for name, (l, h) in BAND_LIMITS.items():
        band_taps[name] = firwin(FIR_TAPS, [l, h], pass_zero=False, fs=fs, window='blackman')
    return (b_notch, a_notch), taps_main, band_taps

def is_artifact_free(data, threshold=150.0):
    # Scan only the newest 0.5s chunk
    chunk = data[-128:]
    for idx in [AF7_INDEX, AF8_INDEX]:
        ptp = np.ptp(chunk[:, idx])
        if ptp > threshold:
            return False, ptp
    return True, 0.0

smoothed_bands = { "delta": 0, "theta": 0, "alpha": 0, "beta": 0, "gamma": 0 }
SMOOTHING_FACTOR = 0.1 

def calculate_bands(freqs, psd):
    global smoothed_bands
    bands = {}
    for band, (low, high) in BAND_LIMITS.items():
        idx = np.logical_and(freqs >= low, freqs < high)
        bands[band] = float(np.trapezoid(psd[idx], freqs[idx])) if any(idx) else 0.0
    for band in bands:
        if smoothed_bands[band] == 0:
            smoothed_bands[band] = bands[band]
        else:
            smoothed_bands[band] = (bands[band] * SMOOTHING_FACTOR) + (smoothed_bands[band] * (1.0 - SMOOTHING_FACTOR))
    return dict(smoothed_bands)

async def websocket_handler(websocket):
    global ARTIFACT_THRESHOLD, LOW_CUT, HIGH_CUT, GLOBAL_GAIN, CONFIG_CHANGED
    connected_clients.add(websocket)
    try:
        async for message in websocket:
            try:
                data = json.loads(message)
                if data.get("type") == "config":
                    if "threshold" in data: ARTIFACT_THRESHOLD = float(data["threshold"])
                    if "low_cut" in data: LOW_CUT = float(data["low_cut"]); CONFIG_CHANGED = True
                    if "high_cut" in data: HIGH_CUT = float(data["high_cut"]); CONFIG_CHANGED = True
                if data.get("command") == "set_gain":
                    GLOBAL_GAIN = float(data.get("value", 1.0))
                    log(f"Global Gain set to: {GLOBAL_GAIN}")
                if data.get("type") == "quit":
                    log("Quit command received.")
                    sys.exit(0)
            except Exception as e: log(f"Config error: {e}")
    finally:
        connected_clients.remove(websocket)

async def broadcast(message):
    if connected_clients:
        websockets.broadcast(connected_clients, json.dumps(message))

def clean_dict(d):
    for k, v in d.items():
        if isinstance(v, list): d[k] = [x if np.isfinite(x) else 0.0 for x in v]
        elif isinstance(v, dict): clean_dict(v)
        elif isinstance(v, float):
            if not np.isfinite(v): d[k] = 0.0
    return d

async def acquisition_loop():
    global last_valid_metrics, CONFIG_CHANGED
    while True:
        log("Searching for LSL stream (Prioritizing name='Muse')...")
        # Prioritize 'Muse' name to ensure we hit the Emulator over any idle BlueMuse background streams
        streams = resolve_byprop('name', 'Muse', timeout=2)
        if not streams:
            streams = resolve_byprop('type', 'EEG', timeout=1)
        
        if not streams:
            await asyncio.sleep(1)
            continue

        try:
            inlet = StreamInlet(streams[0])
            log(f"SUCCESS: Connected to LSL Stream '{streams[0].name()}' ({streams[0].type()})")
            
            data_buffer = np.zeros((BUFFER_SAMPLES, CHANNELS))
            notch_coeffs, fir_kernel, band_kernels = create_fir_filters(FS)
            
            total_samples_collected = 0
            signal_stride = 25 
            smoothed_psd = None

            while True:
                if CONFIG_CHANGED:
                    notch_coeffs, fir_kernel, band_kernels = create_fir_filters(FS)
                    CONFIG_CHANGED = False
                
                sample, timestamp = inlet.pull_sample(timeout=0.01)
                if sample:
                    data_buffer = np.roll(data_buffer, -1, axis=0)
                    data_buffer[-1, :] = sample[:CHANNELS]
                    total_samples_collected += 1

                    # Wait for full buffer to avoid FIR warm-up transients with zero-padding
                    if total_samples_collected >= BUFFER_SAMPLES and total_samples_collected % signal_stride == 0:
                        raw_chunk = data_buffer[-BUFFER_SAMPLES:, :]
                        normalized = raw_chunk - np.mean(raw_chunk, axis=0)
                        
                        notched = lfilter(notch_coeffs[0], notch_coeffs[1], normalized, axis=0)
                        filtered = np.zeros_like(notched)
                        for i in range(CHANNELS):
                            filtered[:, i] = lfilter(fir_kernel, 1.0, notched[:, i])
                        
                        # IMPORTANT: Check artifacts on signal WITHOUT gain so gain doesn't trigger "DIRTY" status
                        is_clean, max_ptp = is_artifact_free(filtered, threshold=ARTIFACT_THRESHOLD)
                        
                        # Apply Gain for visualization and metrics
                        cleaned_signal = filtered * GLOBAL_GAIN
                        
                        # Multi-Channel PSD with Hann Windowing
                        all_psds = []
                        h_win = hann(BUFFER_SAMPLES)
                        for i in range(CHANNELS):
                            windowed_data = cleaned_signal[:, i] * h_win
                            freqs, psd = welch(windowed_data, FS, nperseg=FS*2)
                            all_psds.append(psd)
                        avg_psd = np.mean(all_psds, axis=0)
                        
                        if smoothed_psd is None: smoothed_psd = avg_psd
                        else: smoothed_psd = (avg_psd * SMOOTHING_FACTOR) + (smoothed_psd * (1.0 - SMOOTHING_FACTOR))

                        bands = calculate_bands(freqs, smoothed_psd)
                        ratio = bands["alpha"] / bands["beta"] if bands["beta"] > 0 else 0
                        now = local_clock()
                        metrics_outlet.push_sample([bands["delta"], bands["theta"], bands["alpha"], bands["beta"], bands["gamma"], ratio], timestamp=now)
                        
                        isolated_waves = {}
                        for name, taps in band_kernels.items():
                            iso = lfilter(taps, 1.0, normalized[:, AF7_INDEX])
                            isolated_waves[name] = (iso[-signal_stride:] * GLOBAL_GAIN).tolist()

                        payload = {
                            "type": "telemetry",
                            "status": "OK" if is_clean else "HOLD" if last_valid_metrics else "DIRTY",
                            "ptp": max_ptp,
                            "delta": bands["delta"], "theta": bands["theta"], "alpha": bands["alpha"], "beta": bands["beta"], "gamma": bands["gamma"],
                            "ratio": ratio,
                            "new_raw_tp9": normalized[-signal_stride:, TP9_INDEX].tolist(),
                            "new_filt_tp9": cleaned_signal[-signal_stride:, TP9_INDEX].tolist(),
                            "new_raw_af7": normalized[-signal_stride:, AF7_INDEX].tolist(),
                            "new_filt_af7": cleaned_signal[-signal_stride:, AF7_INDEX].tolist(),
                            "new_raw_af8": normalized[-signal_stride:, AF8_INDEX].tolist(),
                            "new_filt_af8": cleaned_signal[-signal_stride:, AF8_INDEX].tolist(),
                            "new_raw_tp10": normalized[-signal_stride:, TP10_INDEX].tolist(),
                            "new_filt_tp10": cleaned_signal[-signal_stride:, TP10_INDEX].tolist(),
                            "waves": isolated_waves,
                            "fft_freqs": freqs[freqs <= 40].tolist(),
                            "fft_psd": smoothed_psd[freqs <= 40].tolist()
                        }

                        if is_clean:
                            marker_outlet.push_sample(["OK"], timestamp=now)
                            last_valid_metrics = payload
                            await broadcast(clean_dict(payload))
                        elif last_valid_metrics:
                            marker_outlet.push_sample(["HOLD"], timestamp=now)
                            hold_payload = dict(last_valid_metrics)
                            hold_payload["status"] = "HOLD"
                            for ch_name, ch_idx in INDEX_MAP.items():
                                hold_payload[f"new_raw_{ch_name}"] = normalized[-signal_stride:, ch_idx].tolist()
                                hold_payload[f"new_filt_{ch_name}"] = cleaned_signal[-signal_stride:, ch_idx].tolist()
                            await broadcast(clean_dict(hold_payload))
                        else:
                            marker_outlet.push_sample(["DIRTY"], timestamp=now)
                            await broadcast(clean_dict(payload))

                await asyncio.sleep(0.001)
        except Exception as e:
            log(f"Acquisition Error: {e}. Retrying search...")
            await asyncio.sleep(2)

async def main():
    log("--- NeuroMemoryStudy Backend Engine (FIR/WOLA) ---")
    server = await websockets.serve(websocket_handler, "127.0.0.1", 8765)
    await acquisition_loop()

if __name__ == "__main__":
    try: asyncio.run(main())
    except KeyboardInterrupt: log("\nEngine shutting down.")
