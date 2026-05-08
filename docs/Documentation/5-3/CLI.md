# Science Fair Logbook: May 3, 2026

## 1. Project Milestone: Integrated Signal Emulator (BETA / UNSTABLE)
**Status:** IMPLEMENTED BUT BUGGY.
**Goal:** Create a closed-loop testing environment to verify DSP pipeline accuracy.

### 2. Technical Implementation Notes
#### A. Architecture
*   **Bidirectional IPC:** C# App (Client) <-> `emulator.py` (WebSocket Server, Port 8766).
*   **Dynamic Oscillators:** Implemented a real-time frequency builder. Frequencies use phase accumulation to prevent spectral leakage.
*   **Band Isolation:** `engine.py` now applies five parallel 4th-order Butterworth filters to isolate Delta, Theta, Alpha, Beta, and Gamma waves in the time domain.

#### B. The "Emulator" Tab
*   **Side-by-Side Plots:** Added 10 ScottPlot charts.
    *   **Ideal (Green):** Mathematical sine wave sum.
    *   **Filtered (Red):** Signal after LSL transmission and engine-side bandpass filtering.

### 3. Stability Report (CRITICAL BUGS)
The system is currently unstable under the following conditions:
*   **Rapid Interaction:** Clicking "Add" or "Remove" frequency too quickly can trigger WebSocket aborted connection errors or WPF threading race conditions.
*   **Port Locking:** If the C# App crashes, Python processes often remain orphaned, holding ports 8765/8766. This prevents relaunch until `taskkill /F /IM python.exe` is run.
*   **Startup:** Race conditions exist between XAML property initialization and code-behind object instantiation (partially mitigated with `IsLoaded` checks).

### 4. Technical Fixes Applied Today
*   **Phase Sync:** Fixed visualization "drift" by using a shared master clock for all band oscillators.
*   **Summation Math:** Corrected power scaling formula to $Amplitude / \sqrt{N}$ oscillators.
*   **Snapshotting:** Implemented a thread-safe snapshot of the frequency list before each UI draw frame.
*   **Network:** Forced all connections to `127.0.0.1` to bypass Windows IPv6 resolution issues.

### 5. Mandatory Deployment Rule
**DO NOT PUSH TO GITHUB.** The current codebase contains experimental IPC logic that requires significant hardening before it is stable enough for main-branch integration.
