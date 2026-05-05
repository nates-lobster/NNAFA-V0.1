# Project Memory: NeuroMemoryStudy

## Long-Term Memory (Persistent Rules & Setup)
* **Hardware:** Muse 2/S Headset via BlueMuse (LSL). 256Hz sampling. 4 Channels (TP9, AF7, AF8, TP10).
* **Frequencies:** Bandpass 1Hz - 40Hz. 60Hz Notch.
* **IPC:** WebSocket on 127.0.0.1:8765 (Engine) and 127.0.0.1:8766 (Emulator).
* **Engine Stability:** Refactored `engine.py` to keep LSL outlets alive indefinitely even when searching for EEG. Prevents LabRecorder discovery failures.
* **NumPy 2.x Compatibility:** Switched all band power logic from `np.trapz` to `np.trapezoid`.
* **High-Precision Timing:** Updated Engine to use `pylsl.local_clock()` for sub-millisecond XDF timestamping. Fixed "rounded session duration" bug.
* **UI Improvements:** 
  - Locked EEG axes to -150/+150 uV and disabled auto-management to prevent plot "disappearance".
  - Fixed live Alpha/Beta ratio indicator on Neurofeedback tab.
  - Corrected engine launch path resolution (now searches 4 potential relative paths).
* **Software Setup:** Installed R, RStudio, and Rtools to `M:\Muse Project\Software\` for research-grade data analysis.
* **Data Flow:** Verified scrolling EEG waves and live FFT updates are functioning at ~10Hz refresh.

## Short-Term Memory (Current Task State)
* **Status:** System stabilized as of 5-4.
* **Emergency Recovery:** Added a **"Kill All Python"** button in the Tools tab to clear orphaned processes and release locked ports (8765/8766).
* **Fixed Issues:**
    *   **Port Conflict (WinError 10048):** Resolved via "quit" command and graceful shutdown sequence.
    *   **WPF Threading:** Resolved via `SemaphoreSlim` for WebSocket sends and moving math to background threads in `UpdateUI`.
    *   **Startup Crashes:** Fixed with `IsLoaded` checks on UI components.
    *   **Telemetry Regression:** Restored multi-sensor data (TP9, AF7, AF8, TP10) and PSD band shading.
* **Emulator:** Added `PyApp/emulator.py` with WebSocket control.
* **DSP:** Engine now provides isolated time-series for all 5 brainwave bands via `waves` payload.
