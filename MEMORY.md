# Project Memory: NeuroMemoryStudy

## Long-Term Memory (Persistent Rules & Setup)
* **Hardware:** Muse 2/S Headset via BlueMuse (LSL). 256Hz sampling. 4 Channels (TP9, AF7, AF8, TP10).
* **Frequencies:** Bandpass 1Hz - 40Hz. 60Hz Notch. Artifact rejection >150uV (AF7/AF8).
* **Dependencies:** Python 3.11+, NumPy 2.x (uses `np.trapezoid`), .NET 10.0 WPF.
* **Architecture:** Python Engine (Worker) -> WebSockets -> C# App (General).
* **LSL Streams:** 
  - `NeuroMemory_Metrics`: Delta, Theta, Alpha, Beta, Gamma, Ratio (10Hz).
  - `NeuroMemory_Status`: Marker stream (OK, HOLD, DIRTY).
* **Visualization:** R script (`visualize_session.R`) using `reticulate` + `pyxdf`. Detailed 10s axis resolution.

## Short-Term Memory (Active Fixes & Recent Work)
* **WebSocket Fix:** Implemented buffered `StringBuilder` loop in C# to handle fragmented JSON packets (~5KB telemetry chunks). Prevents 0.00 UI freezes.
* **Engine Stability:** Refactored `engine.py` to keep LSL outlets alive indefinitely even when searching for EEG. Prevents LabRecorder discovery failures.
* **NumPy 2.x Compatibility:** Switched all band power logic from `np.trapz` to `np.trapezoid`.
* **High-Precision Timing:** Updated Engine to use `pylsl.local_clock()` for sub-millisecond XDF timestamping. Fixed "rounded session duration" bug.
* **UI Improvements:** 
  - Locked EEG axes to -150/+150 uV and disabled auto-management to prevent plot "disappearance".
  - Fixed live Alpha/Beta ratio indicator on Neurofeedback tab.
  - Corrected engine launch path resolution (now searches 4 potential relative paths).
* **Software Setup:** Installed R, RStudio, and Rtools to `M:\Muse Project\Software\` for research-grade data analysis.
* **Data Flow:** Verified scrolling EEG waves and live FFT updates are functioning at ~10Hz refresh.
