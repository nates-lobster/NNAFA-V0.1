# NeuroMemoryStudy: The Expanded Bug-tionary Audit

| ENTRY_ID | Component | Bug Type | Trigger Condition | Confidence | Urgency | Agent Fix Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| | | | **All audited bugs have been remediated.** | | | **FIXED** |

## Remediation Summary (as of 5-9)

### [ENGINE] (Python Backend)
*   **[001] Overload Bug:** Added 2s watchdog and `max_buflen=2` to LSL inlet.
*   **[002] NaN Propagation:** Implemented root-cause validation by checking NaNs/Infs in incoming LSL samples before processing.
*   **[003] Filter Initialization Pop:** Fully implemented stateful filtering (`lfilter_zi`) with channel-aware expansion.
*   **[004] LSL Race Condition:** Prioritized 'Muse' name in stream resolution.
*   **[005] WS Timeout Dropout:** Switched to `websockets.broadcast` for efficient, non-blocking telemetry.
*   **[006] Bridge Watchdog:** Reliable "SHUTDOWN" status broadcast before exit.
*   **[007] High CPU Block:** Optimized loop sleep and simplified artifact checks.
*   **[008] Resource Leak:** Improved client set management and payload sanitization.

### [EMU] (Emulator)
*   **[001, 002] Signal Integrity:** Thread-safe oscillator management and continuous phase tracking eliminate signal clicks.
*   **[003] Unbounded Noise:** Clamped noise inputs and clipped final samples to prevent ScottPlot crashes.
*   **[004] Ghost Process:** Implemented inactivity watchdog that shuts down emulator if no clients are connected for >60s.
*   **[005] Type Vulnerability:** Robust JSON validation and type casting for all command fields.

### [BUILD] (R Visualization)
*   **[001, 002] Portability:** Added `file.choose()` fallback and automated dependency checks for `reticulate`/`pyxdf`.
*   **[003] Index Fragility:** Implemented name-based channel mapping using XDF metadata labels.

### [WINDOW] (C# WPF Frontend)
*   **[001, 015] Process Control:** Surgical process tree termination (`Kill(true)`) and `cmd /c` launching to prevent orphans.
*   **[002, 003, 009, 010] Resource Lifecycle:** Comprehensive `Dispose()` patterns for WebSockets and Audio readers; reliable shutdown `Wait()`.
*   **[007, 011, 013, 016] UI Performance:** 20Hz throttling, tab-aware plot refreshing, and background JSON parsing.
*   **[004, PROJECT-004] Logic Hardening:** Gated calibration average by "OK" signal status and added zero-checks for feedback math.
*   **[012, 014] Error Handling:** Surfaced NAudio errors and implemented connection state resets on socket failure.

**Exclusion Zone:** `src/backend/bluemuse` and `src/backend/labrecorder` remain untouched as per directive.
