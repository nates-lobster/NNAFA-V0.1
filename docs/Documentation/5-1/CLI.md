# Session Log: May 1, 2026 (5-1)
*Project: NeuroMemoryStudy (EEG Research)*

## 1. Accomplishments
*   **GitHub Integration:** Initialized local Git repository, configured `.gitignore` for Visual Studio/Python/LSL, and successfully pushed the codebase to: `https://github.com/nates-lobster/neurofeedback-app`.
*   **Data Integrity (WebSocket Fix):** Identified and resolved a critical bug where ~5KB telemetry payloads were being fragmented. Implemented a buffered `StringBuilder` loop in the C# `ReceiveDataLoopAsync` to wait for `EndOfMessage` before parsing. This fixed the "0.00 indicators" issue.
*   **Scientific Precision:** Updated `engine.py` to use `pylsl.local_clock()` for sub-millisecond XDF timestamping. This ensures session recordings are accurate to the hardware clock rather than best-guess estimates.
*   **Software Environment:** Installed **R (4.6.0)**, **RStudio**, and **Rtools 4.5** into `M:\Muse Project\Software\` to establish a dedicated research analysis pipeline.
*   **Visualization Workflow:** Developed `visualize_session.R` using `reticulate` + `pyxdf`. Implemented high-resolution axis scaling (10s breaks) to provide accurate session duration displays.

## 2. Technical Fixes & Bug Solutions
*   **NumPy 2.x Compatibility:** Encountered `AttributeError: module 'numpy' has no attribute 'trapz'`. Fixed by switching to `np.trapezoid`, consistent with NumPy 2.x requirements.
*   **ScottPlot 5 Stability:** Disabled `ManageAxisLimits` on EEG streamers. Set static Y-axis limits (-150 to +150 uV) to prevent erratic "axis fighting" when Raw and Filtered signals share a plot.
*   **Engine Launch Path:** Implemented a robust 4-stage search algorithm in C# to find `engine.py` across different execution contexts (CLI vs. IDE).
*   **LSL Discovery:** Refactored engine initialization to create LSL Outlets globally at startup. This prevents the outlets from "vanishing" if the EEG stream is briefly lost, ensuring LabRecorder can always find the metrics.

## 3. Implementation Notes
*   **R Implementation:** Switched from niche GitHub R packages to a `reticulate`-based Python bridge for reading XDF. This provides a more stable, industry-standard interface for EEG data.
*   **Smooth Volume Lerp:** (Verified) The volume provider in C# is smoothly transitioning at 10% per 50ms, providing a calm audio feedback experience without abrupt volume jumps.

## 4. Hardware Verification
*   Sampling Rate: 256Hz (Confirmed).
*   Channel Map: TP9, AF7, AF8, TP10 (Muse Hardware Standard).
*   Artifact Threshold: Set to 150uV on AF7/AF8 for peak-to-peak gating.

**Status:** ALL CORE FUNCTIONS OPERATIONAL. System is research-ready.
