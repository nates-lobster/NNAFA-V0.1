import numpy as np
from pylsl import StreamInlet, resolve_byprop, StreamInfo, StreamOutlet
from scipy.signal import butter, iirnotch, lfilter, welch
import time
import asyncio
import websockets
import json
import sys

# Force unbuffered output for the C# console
def log(message):
    print(message)
    sys.stdout.flush()

# Constants based on GEMINI.md
FS = 256
BUFFER_DURATION = 2  # seconds
BUFFER_SAMPLES = FS * BUFFER_DURATION
CHANNELS = 4
AF7_INDEX = 1 # Typical Muse 2 LSL mapping: TP9, AF7, AF8, TP10
AF8_INDEX = 2

# Global state
last_valid_metrics = None
connected_clients = set()
ARTIFACT_THRESHOLD = 150.0  # Default value
LOW_CUT = 1.0
HIGH_CUT = 40.0
CONFIG_CHANGED = False

# --- LSL SETUP (Global to ensure visibility) ---
metrics_info = StreamInfo(name='NeuroMemory_Metrics', type='EEG_Metrics', channel_count=6, 
                  nominal_srate=10.0, channel_format='float32', source_id='neuromemory_metrics_01')
chns = metrics_info.desc().append_child("channels")
for label in ["Delta", "Theta", "Alpha", "Beta", "Gamma", "Ratio"]:
    ch = chns.append_child("channel")
    ch.append_child_value("label", label)
    ch.append_child_value("unit", "power")

metrics_outlet = StreamOutlet(metrics_info)

marker_info = StreamInfo(name='NeuroMemory_Status', type='Markers', channel_count=1, 
                         nominal_srate=0, channel_format='string', source_id='neuromemory_status_01')
marker_outlet = StreamOutlet(marker_info)

def create_filters(fs):
    b_notch, a_notch = iirnotch(60, 30, fs)
    nyq = 0.5 * fs
    low = LOW_CUT / nyq
    high = HIGH_CUT / nyq
    b_band, a_band = butter(4, [low, high], btype='band')
    return (b_notch, a_notch), (b_band, a_band)

def is_artifact_free(data, threshold=150.0):
    # Only scan the newest 0.5s chunk (128 samples) so a single blink doesn't freeze the system for 2 seconds
    chunk = data[-128:]
    for idx in [AF7_INDEX, AF8_INDEX]:
        ptp = np.ptp(chunk[:, idx])
        if ptp > threshold:
            return False, ptp
    return True, 0.0

# EMA State for smoothing
smoothed_bands = { "delta": 0, "theta": 0, "alpha": 0, "beta": 0, "gamma": 0 }
SMOOTHING_FACTOR = 0.1  # 10% new data, 90% history. Creates a ~3 second glide (Industry standard for neurofeedback)

def calculate_bands(freqs, psd):
    """Calculate band powers from PSD with EMA smoothing."""
    global smoothed_bands
    bands = {}
    
    # Delta: 1-4 Hz
    delta_idx = np.logical_and(freqs >= 1, freqs <= 4)
    bands["delta"] = float(np.trapezoid(psd[delta_idx], freqs[delta_idx]))
    
    # Theta: 4-8 Hz
    theta_idx = np.logical_and(freqs >= 4, freqs <= 8)
    bands["theta"] = float(np.trapezoid(psd[theta_idx], freqs[theta_idx]))
    
    # Alpha: 8-12 Hz
    alpha_idx = np.logical_and(freqs >= 8, freqs <= 12)
    bands["alpha"] = float(np.trapezoid(psd[alpha_idx], freqs[alpha_idx]))
    
    # Beta: 12-30 Hz (Fixed gap)
    beta_idx = np.logical_and(freqs >= 12, freqs <= 30)
    bands["beta"] = float(np.trapezoid(psd[beta_idx], freqs[beta_idx]))
    
    # Gamma: 30-40 Hz
    gamma_idx = np.logical_and(freqs >= 30, freqs <= 40)
    bands["gamma"] = float(np.trapezoid(psd[gamma_idx], freqs[gamma_idx]))
    
    # Apply Exponential Moving Average (EMA)
    for band in bands:
        if smoothed_bands[band] == 0:
            smoothed_bands[band] = bands[band] # Initialize
        else:
            smoothed_bands[band] = (bands[band] * SMOOTHING_FACTOR) + (smoothed_bands[band] * (1.0 - SMOOTHING_FACTOR))
            
    return dict(smoothed_bands)

async def websocket_handler(websocket):
    """Handle new websocket connections and incoming config messages."""
    global ARTIFACT_THRESHOLD, LOW_CUT, HIGH_CUT, CONFIG_CHANGED
    connected_clients.add(websocket)
    try:
        async for message in websocket:
            try:
                data = json.loads(message)
                if data.get("type") == "config":
                    updated = False
                    if "threshold" in data and float(data["threshold"]) != ARTIFACT_THRESHOLD:
                        ARTIFACT_THRESHOLD = float(data["threshold"])
                        log(f"[{time.strftime('%H:%M:%S')}] [CONFIG] Artifact threshold updated to: {ARTIFACT_THRESHOLD}uV")
                    
                    if "low_cut" in data and float(data["low_cut"]) != LOW_CUT:
                        LOW_CUT = float(data["low_cut"])
                        updated = True
                    
                    if "high_cut" in data and float(data["high_cut"]) != HIGH_CUT:
                        HIGH_CUT = float(data["high_cut"])
                        updated = True
                        
                    if updated:
                        CONFIG_CHANGED = True
                        log(f"[{time.strftime('%H:%M:%S')}] [CONFIG] Bandpass filter updated to: {LOW_CUT}Hz - {HIGH_CUT}Hz")
            except Exception:
                pass
    finally:
        connected_clients.remove(websocket)

async def broadcast(message):
    """Send message to all connected C# clients."""
    if connected_clients:
        websockets.broadcast(connected_clients, json.dumps(message))

async def acquisition_loop():
    global last_valid_metrics, CONFIG_CHANGED
    log("Looking for an EEG stream...")
    
    try:
        streams = resolve_byprop('type', 'EEG', timeout=5)
        if not streams:
            log("Error: No EEG stream found.")
            return
    except Exception as e:
        log(f"LSL Error: {e}")
        return

    inlet = StreamInlet(streams[0])
    log(f"Connected to: {streams[0].name()} ({streams[0].nominal_srate()} Hz)")

    data_buffer = np.zeros((BUFFER_SAMPLES, CHANNELS))
    notch_coeffs, bandpass_coeffs = create_filters(FS)
    
    total_samples_collected = 0
    signal_stride = 25 # ~10.24 Hz refresh rate
    
    log("Acquisition loop started. Buffering raw data...")

    smoothed_psd = None

    while True:
        # Check if filters need to be recreated from UI slider
        if CONFIG_CHANGED:
            notch_coeffs, bandpass_coeffs = create_filters(FS)
            CONFIG_CHANGED = False
            
        # Non-blocking pull
        sample, timestamp = inlet.pull_sample(timeout=0.0)
        
        if sample:
            data_buffer = np.roll(data_buffer, -1, axis=0)
            data_buffer[-1, :] = sample[:CHANNELS]
            total_samples_collected += 1

            if total_samples_collected >= BUFFER_SAMPLES and total_samples_collected % signal_stride == 0:
                filtered = lfilter(notch_coeffs[0], notch_coeffs[1], data_buffer, axis=0)
                filtered = lfilter(bandpass_coeffs[0], bandpass_coeffs[1], filtered, axis=0)
                
                is_clean, max_ptp = is_artifact_free(filtered, threshold=ARTIFACT_THRESHOLD)
                
                # We analyze AF7 for the FFT
                af7_filtered = filtered[:, AF7_INDEX]
                
                # Perform FFT using Welch's method
                freqs, psd = welch(af7_filtered, FS, nperseg=FS*2)
                
                if smoothed_psd is None:
                    smoothed_psd = psd
                else:
                    smoothed_psd = (psd * SMOOTHING_FACTOR) + (smoothed_psd * (1.0 - SMOOTHING_FACTOR))

                bands = calculate_bands(freqs, smoothed_psd)
                ratio = bands["alpha"] / bands["beta"] if bands["beta"] > 0 else 0
                
                # Broadcast metrics to LSL
                metrics_outlet.push_sample([bands["delta"], bands["theta"], bands["alpha"], bands["beta"], bands["gamma"], ratio])
                
                # Construct payload
                payload = {
                    "type": "telemetry",
                    "status": "OK" if is_clean else "HOLD" if last_valid_metrics else "DIRTY",
                    "ptp": max_ptp,
                    "delta": bands["delta"],
                    "theta": bands["theta"],
                    "alpha": bands["alpha"],
                    "beta": bands["beta"],
                    "gamma": bands["gamma"],
                    "ratio": ratio,
                    "new_raw_tp9": data_buffer[-signal_stride:, 0].tolist(), 
                    "new_filt_tp9": filtered[-signal_stride:, 0].tolist(),
                    "new_raw_af7": data_buffer[-signal_stride:, 1].tolist(), 
                    "new_filt_af7": filtered[-signal_stride:, 1].tolist(),
                    "new_raw_af8": data_buffer[-signal_stride:, 2].tolist(), 
                    "new_filt_af8": filtered[-signal_stride:, 2].tolist(),
                    "new_raw_tp10": data_buffer[-signal_stride:, 3].tolist(), 
                    "new_filt_tp10": filtered[-signal_stride:, 3].tolist(),
                    "fft_freqs": freqs[freqs <= 40].tolist(),
                    "fft_psd": smoothed_psd[freqs <= 40].tolist()
                }

                if is_clean:
                    marker_outlet.push_sample(["OK"])
                    log(f"[{time.strftime('%H:%M:%S')}] [OK] Clean chunk. Alpha: {bands['alpha']:.2f}, Beta: {bands['beta']:.2f}")
                    last_valid_metrics = payload
                    await broadcast(payload)
                elif last_valid_metrics:
                    marker_outlet.push_sample(["HOLD"])
                    log(f"[{time.strftime('%H:%M:%S')}] [HOLD] Artifact ({max_ptp:.1f}uV). Holding last state.")
                    hold_payload = dict(last_valid_metrics)
                    hold_payload["status"] = "HOLD"
                    hold_payload["new_raw_tp9"] = payload["new_raw_tp9"]
                    hold_payload["new_filt_tp9"] = payload["new_filt_tp9"]
                    hold_payload["new_raw_af7"] = payload["new_raw_af7"]
                    hold_payload["new_filt_af7"] = payload["new_filt_af7"]
                    hold_payload["new_raw_af8"] = payload["new_raw_af8"]
                    hold_payload["new_filt_af8"] = payload["new_filt_af8"]
                    hold_payload["new_raw_tp10"] = payload["new_raw_tp10"]
                    hold_payload["new_filt_tp10"] = payload["new_filt_tp10"]
                    await broadcast(hold_payload)
                else:
                    marker_outlet.push_sample(["DIRTY"])
                    log(f"[{time.strftime('%H:%M:%S')}] [DIRTY] Initial buffer noisy ({max_ptp:.1f}uV). Waiting for clean signal.")
                    await broadcast(payload)

        # Yield to asyncio event loop
        await asyncio.sleep(0.001)

async def main():
    log("--- NeuroMemoryStudy Backend Engine ---")
    server = await websockets.serve(websocket_handler, "localhost", 8765)
    log("WebSocket Server started on ws://localhost:8765")
    await acquisition_loop()

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        log("\nEngine shutting down.")
