# Project Memory: NeuroMemoryStudy
**Last Updated:** 2026-05-07 23:30

## I. System Constants (Long-Term)
*   **Hardware:** Muse 2/S via BlueMuse (LSL).
*   **Channels:** TP9, AF7, AF8, TP10 (Average used for PSD).
*   **Ports:** 8765 (Telemetry), 8766 (Emulator Control), 22345 (LabRecorder TCP).
*   **Status:** Stable. Using 513-tap FIR kernels for filtering.

## II. Current Project State (Short-Term)
*   **Current Milestone:** Debug Pipeline Implementation & Bug Squashing.
*   **Recent Changes (2026-05-07):**
    *   Reorganized root folder to use `src/Frontend` and `src/Backend`.
    *   Updated `.slnx` solution file and configurations with correct relative paths.
    *   Fixed external python tool launching from the C# UI so windows are persistent and visible.
    *   Implemented "Debug Pipeline" tab with 4 real-time ScottPlot stages (Raw, Filtered, Denoised, FFT).
    *   Enforced "Honesty Policy" in `engine.py` (explicit zeroing of outputs on stream drop/artifact to prevent UI ghosting).
    *   Expanded UI slider ranges (Threshold down to 5uV, Noise up to 100uV, zeroed default noise).
*   **Known Issues:**
    *   Root folder `Neurofeedback App` is currently locked by a system process; needs manual deletion after restart.
    *   **OVERLOADING BUG**: When subjected to extreme noise/input, the noise toggle stops working and the engine enters a "phantom data" loop where it broadcasts fake live data even after the emulator/inputs are shut off. The only known fix currently is to use the "Kill All Python" button and restart the processes.

## III. Active Todo List
- [ ] Investigate "Overloading" bug (phantom data loops when pipeline is overwhelmed).
- [ ] Manually delete `Neurofeedback App` root folder (Blocked).
- [ ] Verify telemetry visualization with BlueMuse active.
