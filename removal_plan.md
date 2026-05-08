# Plan: Removal of Fabricated Data and Hold Logic

This plan outlines the steps required to implement a strict "No Input = No Data" policy across the Neurofeedback App.

## 1. Backend Changes (`src/Backend/PyApp/engine.py`)

### A. Remove Dead Code
- Delete `last_valid_metrics = None` (line 29).
- Remove `global last_valid_metrics` from `acquisition_loop` (line 126).

### B. Redefine Status Logic
- Replace the `HOLD` status with `INITIALIZING`.
- **Logic:** `status = "OK" if (v_len == BUFFER_SAMPLES and is_clean) else ("ARTIFACT" if not is_clean else "INITIALIZING")`.

### C. Zero-out on Artifact
- If `is_clean` is `False`:
  - Set `bands` values to `0.0`.
  - Set `ratio` to `0.0`.
  - Set `psd_list` to a list of zeros.
  - Set `waves` values to lists of zeros.
- This ensures the UI receives explicit "no data" values during artifact events rather than the last calculated values being misinterpreted.

### D. Watchdog Broadcast
- When `WATCHDOG: Stream timed out` occurs (line 224), explicitly `await broadcast({"type": "status", "status": "DISCONNECTED"})` before breaking the loop.

## 2. Frontend Changes (`src/Frontend/App/MainWindow.xaml.cs`)

### A. Freeze Wave Generation
- Modify the `_emuTime` increment condition (line 373).
- **New Logic:** Only increment `_emuTime` if `status == "OK"`. If `status` is `INITIALIZING`, `ARTIFACT`, or `SEARCHING`, the "emulator" time must freeze.

### B. Strict UI Data Clearing
- Update the `Dispatcher.Invoke` block in `UpdateUI`:
  - If `status != "OK"`:
    - Set `_currentDelta`, `_currentTheta`, etc., to `0`.
    - Clear `fftFreqs` and `fftPsd` (or set to empty arrays).
    - Skip `ProcessEegData`, `ApplyBandData`, and trend updates.
  - This ensures that if the backend stops sending valid data or sends "zeroed" data, the UI immediately reflects this by flatlining or clearing.

### C. Visual Feedback
- Update the `StatusIndicator` logic:
  - `OK` -> `LimeGreen`
  - `INITIALIZING` -> `Yellow`
  - `ARTIFACT` -> `OrangeRed`
  - `SEARCHING` / `DISCONNECTED` -> `Red`

## 3. Verification Steps
1. **Startup Test:** Verify UI stays at zero/frozen while the buffer fills (`INITIALIZING`).
2. **Artifact Test:** Introduce an artifact (e.g., blink) and verify metrics immediately drop to zero and graphs freeze.
3. **Disconnect Test:** Kill the LSL stream and verify the UI immediately switches to "SEARCHING/DISCONNECTED" and all data clears.
