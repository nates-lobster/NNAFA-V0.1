import numpy as np
import time
import asyncio
import websockets
import json
from pylsl import StreamInfo, StreamOutlet, local_clock
import threading
import sys

# Constants
FS = 256
CHANNELS = 4
CHANNEL_NAMES = ["TP9", "AF7", "AF8", "TP10"]

# Global state for oscillators
active_oscillators = []
noise_level = 5.0
emu_gain = 1.0
osc_lock = threading.Lock()
connected_clients = 0

def log(msg):
    print(f"[EMULATOR] {msg}")
    sys.stdout.flush()

def setup_lsl():
    # Force type to 'EEG' so engine.py resolve_byprop('type', 'EEG') finds it
    info = StreamInfo(name='Muse', type='EEG', channel_count=CHANNELS, 
                      nominal_srate=FS, channel_format='float32', source_id='muse_emulator_01')
    desc = info.desc()
    chns = desc.append_child("channels")
    for name in CHANNEL_NAMES:
        ch = chns.append_child("channel")
        ch.append_child_value("label", name)
        ch.append_child_value("unit", "microvolts")
        ch.append_child_value("type", "EEG")
    return StreamOutlet(info)

async def control_handler(websocket):
    global active_oscillators, noise_level, emu_gain, connected_clients
    log("C# App connected.")
    connected_clients += 1
    try:
        async for message in websocket:
            try:
                data = json.loads(message)
                cmd = data.get("command")
                with osc_lock:
                    if cmd == "add_freq":
                        hz = float(data.get("hz", 10.0))
                        amp = float(data.get("amp", 10.0))
                        # Clamp amp to safe range [EMU]-[003]
                        amp = min(max(amp, 0.0), 200.0)
                        # Check if already exists to prevent duplicates
                        if not any(np.isclose(o["hz"], hz) for o in active_oscillators):
                            active_oscillators.append({"hz": hz, "amp": amp, "phase": np.random.rand() * 2 * np.pi})
                            log(f"Added {hz}Hz @ {amp}uV")
                    elif cmd == "remove_freq":
                        hz = float(data.get("hz"))
                        active_oscillators = [o for o in active_oscillators if not np.isclose(o["hz"], hz)]
                        log(f"Removed {hz}Hz")
                    elif cmd == "set_noise":
                        val = float(data.get("value", data.get("amp", 0.0)))
                        noise_level = min(max(val, 0.0), 100.0)
                    elif cmd == "set_gain":
                        val = float(data.get("value", 1.0))
                        emu_gain = min(max(val, 0.1), 10.0)
                        log(f"Gain set to {emu_gain}")
                    elif cmd == "clear":
                        active_oscillators = []
                        log("Cleared all")
                    elif cmd == "quit":
                        log("Quit command received.")
                        sys.exit(0)
            except Exception as e:
                log(f"Command Error: {e}")
    except websockets.exceptions.ConnectionClosed:
        log("C# App disconnected.")
    finally:
        connected_clients -= 1

async def start_ws():
    global connected_clients
    async with websockets.serve(control_handler, "127.0.0.1", 8766):
        log("WebSocket server started on port 8766")
        last_active_time = time.time()
        while True:
            # Ghost Process Watchdog [EMU]-[004] / [NEW]-[002]
            # Shutdown if no client connected for > 60s
            if connected_clients == 0:
                if (time.time() - last_active_time) > 60.0:
                    log("WATCHDOG: No active connections for 60s. Shutting down.")
                    sys.exit(0)
            else:
                last_active_time = time.time() # Keep timer fresh while clients are connected
            
            await asyncio.sleep(5)

def run_lsl_loop(outlet):
    dt = 1.0 / FS
    start_time = local_clock()
    sent_samples = 0
    while True:
        elapsed = local_clock() - start_time
        required = int(elapsed * FS)
        while sent_samples < required:
            sample = np.zeros(CHANNELS)
            t = sent_samples * dt
            with osc_lock:
                # [EMU]-[001, 002] Thread-safe iteration and continuous phase
                for osc in active_oscillators:
                    osc["phase"] = (osc["phase"] + 2 * np.pi * osc["hz"] * dt) % (2 * np.pi)
                    # Add some jitter to make it look realistic
                    jitter = 0.05 * np.sin(2 * np.pi * 0.1 * t)
                    sample += (osc["amp"] * emu_gain) * np.sin(osc["phase"] + jitter)
                
                if noise_level > 0:
                    # [EMU]-[003] Clamped noise
                    noise = np.random.normal(0, noise_level, CHANNELS)
                    sample += np.clip(noise, -500, 500)
            
            outlet.push_sample(sample)
            sent_samples += 1
        time.sleep(0.005)

if __name__ == "__main__":
    try:
        outlet = setup_lsl()
        log(f"Streaming 'Muse' at {FS}Hz...")
        threading.Thread(target=run_lsl_loop, args=(outlet,), daemon=True).start()
        asyncio.run(start_ws())
    except KeyboardInterrupt:
        log("Shutting down...")
    except Exception as e:
        log(f"FATAL: {e}")
