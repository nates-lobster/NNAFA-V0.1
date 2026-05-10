# NeuroMemoryStudy: Bug Fixes & Patches Log

| ENTRY_ID | Fix Description | Technical Reasoning / Safety Justification |
| :--- | :--- | :--- |
| **[ENGINE]-[001]** | Added 2s watchdog and `max_buflen=2` to LSL inlet. | Prevents "phantom data" loops and buffer overflows by resetting if stream hangs. |
| **[ENGINE]-[002]** | Added NaN/Inf checks to incoming LSL samples. | Prevents propagation of invalid numerical values into DSP, FFT, and UI. |
| **[ENGINE]-[003]** | Implemented `lfilter_zi` with multi-channel expansion. | Ensures smooth transitions during live filter changes by tracking state for all 4 EEG channels. |
| **[ENGINE]-[004]** | Prioritized 'name'='Muse' in LSL resolution. | Fixes non-deterministic connection when multiple EEG streams are active. |
| **[ENGINE]-[005]** | Added error handling for broadcast timeouts. | Prevents engine stalls during high UI lag or slow networking. |
| **[ENGINE]-[006]** | Added "SHUTDOWN" status broadcast before exit. | Improves IPC synchronization and ensures frontend knows when backend is closing. |
| **[ENGINE]-[007]** | Optimized loop sleep and simplified artifact check. | Reduces CPU usage while maintaining signal integrity monitoring. |
| **[ENGINE]-[008]** | Improved WebSocket client tracking and removal. | Prevents resource leaks from improper or multiple client connections. |
| **[EMU]-[001]** | Added `osc_lock` (threading.Lock) for oscillator list. | Ensures thread-safety when adding/removing frequencies from C#. |
| **[EMU]-[002]** | Persistent phase tracking in generator loop. | Eliminates signal clicks/pops when frequencies are toggled. |
| **[EMU]-[003]** | Clamped noise level to 100uV and clipped final sample. | Prevents extreme values from crashing ScottPlot auto-scalers. |
| **[EMU]-[004]** | Added 60s inactivity watchdog. | Ensures emulator process dies if C# app disconnects abruptly. |
| **[EMU]-[005]** | Added robust type casting and input validation for JSON commands. | Prevents TypeError crashes from malformed frontend payloads. |
| **[BUILD]-[001]** | Added `file.choose()` fallback in R script. | Fixes crash on new machines where hardcoded paths don't exist. |
| **[BUILD]-[002]** | Added `pyxdf` dependency check. | Provides clearer errors if the Python environment is not configured correctly. |
| **[BUILD]-[003]** | Implemented label-based channel mapping. | Prevents data misalignment if XDF stream order changes. |
| **[WINDOW]-[001]** | Changed "Kill All" to target only managed processes (`_engineProc`, `_emuProc`). | Prevents accidentally killing unrelated python processes on the OS. |
| **[WINDOW]-[002]** | Added `Dispose()` for `ClientWebSocket` on reconnect. | Prevents memory and socket handle leaks. |
| **[WINDOW]-[003]** | Stopped `_calibrationTimer` and `_volumeLerpTimer` on close. | Prevents zombie threads and background firing after window is closed. |
| **[WINDOW]-[004]** | Added zero-check for `_targetRatio` in volume math. | Prevents DivisionByZero exceptions in the audio update loop. |
| **[WINDOW]-[005]** | Improved `LaunchPythonScript` path resolution. | Ensures "Tools" tab works reliably across different dev/build setups. |
| **[WINDOW]-[006]** | Added check for 0-byte audio files. | Prevents `LoopingSampleProvider` from entering an infinite loop. |
| **[WINDOW]-[007]** | Implemented 20Hz UI throttling and tab-aware plot refreshing. | Massively reduces Dispatcher load and eliminates UI lag. |
| **[WINDOW]-[008]** | Added null checks for `_waveOut` and audio providers. | Prevents crashes during startup or if hardware audio init fails. |
| **[WINDOW]-[009]** | Disposed old `AudioFileReader` before loading new files. | Fixes file handle and memory accumulation in the Research tab. |
| **[WINDOW]-[010]** | Added `Wait(2000)` for clean IPC shutdown on closing. | Ensures 'quit' signals reach backends before process termination. |
| **[WINDOW]-[011]** | Simplified UI loop and moved plot refreshes inside vis-checks. | Optimized dispatcher thread usage for high-refresh telemetry. |
| **[WINDOW]-[012]** | Improved error surfacing for NAudio init failures. | Prevents silent failures in the audio engine. |
| **[WINDOW]-[013]** | ScottPlot updates now gated by `Dispatcher.Invoke`. | Ensures thread-safe access to UI plotting components. |
| **[WINDOW]-[014]** | Added connection state reset on socket errors. | Prevents reconnect loops from locking the UI thread. |
| **[WINDOW]-[015]** | Switched to `cmd /c` with `CreateNoWindow=true`. | Prevents detached console orphans and keeps processes managed. |
| **[WINDOW]-[016]** | Implemented `ResetUIState()` on disconnect. | Prevents UI from displaying "frozen" last-known data after backend crash. |
| **[PROJECT]-[001]** | Added status broadcasts for "SEARCHING" vs "CONNECTED". | Provides better user feedback when ports are locked or streams missing. |
| **[PROJECT]-[002]** | Added `try-except` (ValueError, TypeError) for config parsing. | Ensures backend stability against malformed frontend JSON. |
| **[PROJECT]-[003]** | Verified Notch Q=30 stability with stateful filters. | Ringing is minimized by maintaining filter state across chunks. |
| **[PROJECT]-[004]** | Gated calibration average by status monitoring. | Prevents baseline calculation during artifact-heavy periods. |
| **[NEW]-[001]** | Moved emulator clock increment outside UI throttle. | Prevents emulator signal drift/lag when UI refresh is throttled. |
| **[NEW]-[002]** | Reset watchdog timer on client activity in emulator. | Ensures 60s shutdown countdown only starts after the last client disconnects. |
| **[NEW]-[003]** | Standardized `trapezoid` integration logic. | Ensures NumPy 2.x compatibility for brainwave power calculations. |
| **[NEW]-[004]** | Verified ScottPlot 5 transparency hex strings. | Ensures correct rendering of PSD band shading in the UI. |
