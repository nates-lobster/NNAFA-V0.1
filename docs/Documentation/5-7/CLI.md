# Daily Log: 5-7

## Summary of Work
Significant structural improvements to the codebase, bug fixes regarding inter-process communication (IPC), and the implementation of a new "Debug Pipeline" interface for transparent signal processing visualization.

## Progress
*   **Path Migration:** Successfully refactored paths throughout the WPF frontend to locate the Python backend scripts in the new `src/Backend/` hierarchy.
*   **Launch Improvements:** `run.bat` added to automatically build C# and manage package dependencies. Updated `MainWindow.xaml.cs` so python tools (Engine/Emulator) open in visible console windows for easier debugging.
*   **Debug Pipeline UI:** Added a new tab with a 2x2 grid displaying exactly how the signal is mutated at each stage:
    1.  Stage 1: Raw Signal (uV)
    2.  Stage 2: Filtered (1-40Hz + Notch)
    3.  Stage 3: Denoised (Artifact Rejection)
    4.  Stage 4: FFT (Power Spectrum)
*   **Settings Overhaul:** Lowered threshold slider minimum to 5uV to catch precision emulator signals. Made processing gain and filter sliders update dynamically.

## Bugs Encountered & Solved
*   **Bug:** Ghost data/Phantom graphs when streams stopped.
    *   **Solution:** The C# graph clock (`_emuTime`) was advancing without verifying stream health. We implemented a strict "Honesty Policy." `engine.py` now enforces a 0.7-second watchdog. If the stream stops, it broadcasts a `DISCONNECTED` state and zeros out all telemetry payloads before shutting down. The UI listens for `DISCONNECTED` to explicitly freeze trend charts and zero out metrics.
*   **Bug:** Emulator signals dropping/not passing to software.
    *   **Solution:** Emulator signals (pure sine waves) had peak-to-peak amplitudes double their defined amplitude. The old 50uV threshold cut them off. Expanded threshold slider to 5uV to allow fine-grained gating. Removed artificial `jitter` from the emulator's sine generation.
*   **Bug:** Stage 3 Graph blanking out.
    *   **Solution:** Shared resource conflict in `ScottPlot.Plottables.DataStreamer` initialization. Re-initialized Stage 1, 2, and 3 as completely independent streamer objects mapped to discrete keys in the payload (`new_raw_af7`, `new_filt_af7`, `new_denoised_af7`).

## Unresolved Issues (For Future Agents)
*   **The Overload Bug:** When flooded with maximum noise or overwhelming signals, the engine enters a broken state. The noise toggle stops functioning, and the system continues to broadcast "phantom data" as if it is live, even after inputs are shut off. The current workaround is a hard restart of the Python engine. Needs deeper investigation into pylsl buffer overflowing or asyncio WebSocket queue freezing.

## Technical Implementation Notes
*   **DSP Chain Tracking:** `engine.py` was modified to output 25-sample arrays (`signal_stride`) for every stage of the pipeline via JSON. The PSD array was interpolated down from its native FFT bin size to exactly 25 points using `numpy.interp` to ensure the frontend can plot all data synchronously in one UI loop tick without array length mismatches.