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
    b_notch, a_notch = iirnotch(60, 30, fs)
    taps_main = firwin(FIR_TAPS, [LOW_CUT, HIGH_CUT], pass_zero=False, fs=fs, window='blackman')
    band_taps = {}
    for name, (l, h) in BAND_LIMITS.items():
        band_taps[name] = firwin(FIR_TAPS, [l, h], pass_zero=False, fs=fs, window='blackman')
    return (b_notch, a_notch), taps_main, band_taps

def is_artifact_free(data, threshold=150.0):
    chunk = data[-128:]
    for idx in range(CHANNELS):
        ptp = np.ptp(chunk[:, idx])
        if ptp < 0.1: return False, ptp, "FLATLINE"
    
    for idx in [AF7_INDEX, AF8_INDEX]:
        ptp = np.ptp(chunk[:, idx])
        if ptp > threshold: return False, ptp, "ARTIFACT"
            
    return True, 0.0, "OK"

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
                    if "global_gain" in data: GLOBAL_GAIN = float(data["global_gain"])
                if data.get("type") == "quit":
                    log("Quit command received.")
                    sys.exit(0)
            except Exception as e: log(f"Config error: {e}")
    except: pass
    finally:
        connected_clients.remove(websocket)

async def broadcast(message):
    if connected_clients:
        msg = json.dumps(message)
        tasks = [asyncio.create_task(ws.send(msg)) for ws in connected_clients]
        if tasks: await asyncio.wait(tasks, timeout=0.05)

def clean_dict(d):
    for k, v in d.items():
        if isinstance(v, list): d[k] = [x if np.isfinite(x) else 0.0 for x in v]
        elif isinstance(v, dict): clean_dict(v)
        elif isinstance(v, float):
            if not np.isfinite(v): d[k] = 0.0
    return d

async def acquisition_loop():
    global CONFIG_CHANGED
    while True:
        log("Searching for LSL stream...")
        await broadcast({"type": "status", "status": "SEARCHING"})
        
        streams = resolve_byprop('name', 'Muse', timeout=1)
        if not streams: streams = resolve_byprop('type', 'EEG', timeout=1)
        
        if not streams:
            await asyncio.sleep(0.5)
            continue

        try:
            inlet = StreamInlet(streams[0], recover=False)
            log(f"Connected to {streams[0].name()}")
            await broadcast({"type": "status", "status": "CONNECTED"})
            
            data_buffer = np.zeros((BUFFER_SAMPLES, CHANNELS))
            notch_coeffs, fir_kernel, band_kernels = create_fir_filters(FS)
            total_samples = 0
            stride = 25 
            smoothed_psd = None
            last_sample_time = time.time()

            while True:
                if CONFIG_CHANGED:
                    notch_coeffs, fir_kernel, band_kernels = create_fir_filters(FS)
                    CONFIG_CHANGED = False
                
                sample, timestamp = inlet.pull_sample(timeout=0.005)
                if sample:
                    last_sample_time = time.time()
                    data_buffer = np.roll(data_buffer, -1, axis=0)
                    data_buffer[-1, :] = sample[:CHANNELS]
                    total_samples += 1

                    if total_samples % stride == 0:
                        v_len = min(total_samples, BUFFER_SAMPLES)
                        
                        # Stage 1: Raw (DC Removed)
                        raw = data_buffer[-v_len:, :] - np.mean(data_buffer[-v_len:, :], axis=0)
                        
                        # Stage 2: Filtered
                        notched = lfilter(notch_coeffs[0], notch_coeffs[1], raw, axis=0)
                        filt = np.zeros_like(notched)
                        for i in range(CHANNELS): filt[:, i] = lfilter(fir_kernel, 1.0, notched[:, i])
                        
                        # Apply GLOBAL_GAIN
                        filt_g = filt * GLOBAL_GAIN
                        
                        # Status Logic & Honesty Check
                        is_clean, ptp, quality = is_artifact_free(filt_g, threshold=ARTIFACT_THRESHOLD)
                        
                        # Determine current status
                        if v_len < BUFFER_SAMPLES:
                            current_status = "INITIALIZING"
                        else:
                            current_status = quality # "OK", "ARTIFACT", or "FLATLINE"

                        # Build telemetry basics
                        payload = {
                            "type": "telemetry", 
                            "status": current_status,
                            "ptp": ptp,
                            "new_raw_af7": raw[-stride:, AF7_INDEX].tolist(),
                            "new_filt_af7": filt_g[-stride:, AF7_INDEX].tolist()
                        }

                        # HONESTY: If not OK, explicitly clear processed metrics
                        if current_status == "OK":
                            # PSD / Bands
                            h_win = hann(v_len)
                            all_psds = []
                            for i in range(CHANNELS):
                                f_tmp, p_tmp = welch(filt_g[:, i] * h_win, FS, nperseg=v_len)
                                all_psds.append(p_tmp)
                            avg_psd = np.mean(all_psds, axis=0)
                            if smoothed_psd is None: smoothed_psd = avg_psd
                            else: smoothed_psd = (avg_psd * SMOOTHING_FACTOR) + (smoothed_psd * (1.0 - SMOOTHING_FACTOR))
                            
                            bands = calculate_bands(f_tmp, smoothed_psd)
                            ratio = bands["alpha"] / bands["beta"] if bands["beta"] > 0 else 0
                            
                            target_f = np.linspace(0, 40, stride)
                            psd_list = np.interp(target_f, f_tmp, smoothed_psd).tolist()
                            freqs_list = target_f.tolist()

                            payload.update({
                                "ratio": ratio, **bands,
                                "fft_freqs": freqs_list, "fft_psd": psd_list,
                                "new_denoised_af7": filt_g[-stride:, AF7_INDEX].tolist()
                            })
                            
                            waves = {}
                            for name, kernel in band_kernels.items():
                                iso = lfilter(kernel, 1.0, filt_g[:, AF7_INDEX])
                                waves[name] = (iso[-stride:] if len(iso) >= stride else np.pad(iso, (stride-len(iso), 0))).tolist()
                            payload["waves"] = waves

                        else:
                            # Send zeros for metrics if signal is bad
                            payload.update({
                                "ratio": 0, "delta":0, "theta":0, "alpha":0, "beta":0, "gamma":0,
                                "fft_freqs": [0.0]*stride, "fft_psd": [0.0]*stride,
                                "new_denoised_af7": [0.0]*stride,
                                "waves": {b: [0.0]*stride for b in BAND_LIMITS}
                            })

                        # Other channels for raw view
                        for n, idx in INDEX_MAP.items():
                            if n != "af7":
                                payload[f"new_raw_{n}"] = raw[-stride:, idx].tolist()
                                payload[f"new_filt_{n}"] = filt_g[-stride:, idx].tolist()

                        await broadcast(clean_dict(payload))
                else:
                    if time.time() - last_sample_time > 0.7: 
                        log("WATCHDOG: Stream timed out.")
                        await broadcast({"type": "status", "status": "DISCONNECTED"})
                        break
                await asyncio.sleep(0.001)
        except Exception as e: log(f"Err: {e}"); await asyncio.sleep(1)

async def main():
    async with websockets.serve(websocket_handler, "127.0.0.1", 8765):
        await acquisition_loop()

if __name__ == "__main__":
    try: asyncio.run(main())
    except: pass