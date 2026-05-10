# NeuroMemoryStudy: The Expanded Bug-tionary

| ENTRY_ID | Component | Bug Type | Trigger Condition | Confidence | Urgency | Agent Fix Guidance |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **[ENGINE]-[001]** | Py | **The Overload Bug** | Maximum noise or extreme signal input. | 9 | **Crit** | Engine enters "phantom data" loop. Likely `pylsl` buffer overflow or `asyncio` deadlock. Add buffer size limits and watchdog reset. |
| **[ENGINE]-[002]** | Py | **NaN Propagation** | FFT of flatline or invalid data. | 10 | Med | `clean_dict` masks the issue by replacing `inf/nan` with `0.0`. Root cause is lack of input validation before FFT. |
| **[ENGINE]-[003]** | Py | **Filter Initialization Pop** | Changing filter settings (Low/High cut) live. | 8 | Low | `lfilter` state is not preserved or cleared correctly when `CONFIG_CHANGED` triggers re-creation of filters, causing a DC spike in plots. |
| **[ENGINE]-[004]** | Py | **LSL Race Condition** | Resolving streams with multiple EEG devices. | 9 | Med | `resolve_byprop` takes the first result `streams[0]`. If both a real Muse and an Emulator are active, connection is non-deterministic. |
| **[ENGINE]-[005]** | Py | **WS Timeout Dropout** | High CPU load or UI lag. | 9 | Med | `broadcast` has `timeout=0.05`. On slow machines, telemetry frames are dropped silently, leading to jittery UI updates. |
| **[EMU]-[001]** | Py | **Thread-Unsafe List** | Adding/Removing frequencies rapidly via C#. | 8 | Med | `active_oscillators` is iterated in one thread and modified in another. Use a deep copy or more robust locking for the generator loop. |
| **[EMU]-[002]** | Py | **Phase Discontinuity** | Removing and re-adding a frequency. | 7 | Low | Frequency generator uses `osc["phase"]` which resets or jumps when frequencies are toggled, causing signal "clicks." |
| **[EMU]-[003]** | Py | **Unbounded Noise** | Setting noise slider to 100 via C#. | 9 | Low | `np.random.normal` with high scale can produce values exceeding 10,000uV, potentially crashing the C# ScottPlot axis auto-scaler. |
| **[EMU]-[004]** | Py | **Ghost Process** | Closing C# App without "Quit" command. | 10 | Med | If WebSocket disconnects abruptly, `emulator.py` might stay alive as a background process since `sys.exit(0)` is only in the handler. |
| **[BUILD]-[001]** | R | **Hardcoded User Path** | Running `visualize_session.R` on a new machine. | 10 | **Crit** | `file_path` is hardcoded to `C:/Users/Nate/...`. Script will crash on any other machine. Use `file.choose()` or relative paths. |
| **[BUILD]-[002]** | R | **Reticulate Dependency** | Running script without Python/pyxdf installed. | 9 | Med | `py_install("pyxdf")` might fail without admin rights or in restricted environments, breaking the whole R pipeline. |
| **[BUILD]-[003]** | R | **Column Index Fragility** | Loading XDF with unexpected channel count. | 8 | Med | Uses `time_series[,1]` etc. If the LSL stream order changes or extra channels are added, bands (Delta/Theta) will be misaligned. |
| **[WINDOW]-[001]** | C# | **Global Kill Command** | Clicking "Kill All Python". | 10 | **Crit** | Kills *every* python.exe on the OS. Change to `_engineProc.Kill()` and `_emuProc.Kill()`. |
| **[WINDOW]-[002]** | C# | **Async Leak** | Repeated WebSocket connection attempts. | 9 | Low | `new ClientWebSocket()` is called in a loop without `Dispose()` on the old instance if a connection fails or is reset. |
| **[WINDOW]-[003]** | C# | **Zombie Threads** | Closing Window during calibration. | 8 | Med | `_calibrationTimer` is not stopped in `Window_Closing`. The timer will continue to fire in the background until the process fully exits. |
| **[WINDOW]-[004]** | C# | **Division by Zero** | Setting "Target Ratio" slider to 0 (if allowed). | 9 | Med | `UpdateNeurofeedbackVolume` divides by `_targetRatio`. If slider minimum is 0, this will throw an exception in the UI update loop. |
| **[WINDOW]-[005]** | C# | **Path Mapping Hell** | Launching from Visual Studio vs Build folder. | 10 | Low | `LaunchPythonScript` tries 4 different relative paths. This is fragile and makes the "Tools" tab unreliable across different dev setups. |
| **[WINDOW]-[006]** | C# | **Audio Buffer Underflow** | Loading a very short/corrupt MP3. | 7 | Low | `LoopingSampleProvider` assumes `AudioFileReader` always returns data. If a file is 0 bytes, it enters an infinite loop of `Position = 0`. |
| **[WINDOW]-[007]** | C# | **UI Thread Saturation** | 256Hz raw data updates. | 10 | **Crit** | `UpdateUI` refreshes 18 ScottPlot controls (including hidden tabs) per telemetry chunk. Causes severe lag. Implement tab-aware visibility and throttling. |
| **[WINDOW]-[008]** | C# | **Missing Null Checks** | Accessing `_volumeProvider` before audio init. | 8 | Low | `UpdateNeurofeedbackVolume` checks for null, but `AudioEnable_Checked` does not, potentially causing a crash if audio fails to setup. |
| **[WINDOW]-[009]** | C# | **Handle Leak** | Browsing for new audio files repeatedly. | 9 | Med | `BrowseAudio_Click` stops `_waveOut` but never calls `.Dispose()` on the previous `AudioFileReader`. Accumulates file handles and memory. |
| **[WINDOW]-[010]** | C# | **Incomplete Shutdown** | Closing window during active IPC. | 9 | Med | `Window_Closing` fires an unawaited `Task.Run` for 'quit' signals. App process often ends before the backend can shut down cleanly. |
| **[WINDOW]-[011]** | C# | **UI Thread DSP** | High JSON iteration in `UpdateUI`. | 7 | Low | `UpdateUI` performs array enumeration and band preparation on the dispatcher thread. Should be moved to the background before `Invoke`. |
| **[PROJECT]-[001]** | IPC | **Port Conflict** | Port 8765 occupied by another app. | 10 | Med | Neither C# nor Python handles "Port in Use" gracefully. C# just shows "Searching..." and Python crashes silently or logs to a hidden console. |
| **[PROJECT]-[002]** | IPC | **JSON Type Mismatch** | Python receiving string instead of float. | 9 | Med | `float(data.get("threshold"))` will throw if C# sends a non-numeric string. Need better try/except blocks in `websocket_handler`. |
| **[PROJECT]-[003]** | DSP | **Notch Filter Ringing** | Filtering 60Hz on a noisy signal. | 7 | Low | 60Hz Notch with `Q=30` is quite sharp. On signal start or artifact, this can cause significant "ringing" (oscillations) that looks like brainwaves. |
| **[PROJECT]-[004]** | UI | **Calibration Honesty** | Calibrating while signal status is "ARTIFACT". | 8 | Med | `CalibrateButton_Click` doesn't check `StatusText`. Users can calibrate on muscle noise, setting a baseline for the whole session. |

**Total Entries:** 27
**Exclusion Zone:** `src/Backend/BlueMuse_2.3.0.0` and `src/Backend/Tools/LabRecorder` were skipped as per directive.
