# Science Fair Logbook: May 4, 2026

## 1. Project Milestone: System Stabilization & High-Precision FIR Transition
**Status:** STABLE.
**Goal:** Transition to FIR filtering and resolve critical IPC vulnerabilities.

### 2. Technical Implementation Notes
#### A. Transition to FIR Filtering (WOLA Strategy)
*   **Implementation:** Replaced Butterworth IIR filters with a **513-tap Blackman-window FIR kernel**. 
*   **Reasoning:** FIR filters provide zero-phase distortion and sharper frequency cutoffs (1Hz - 40Hz), essential for isolating overlapping brainwave bands without signal drift.
*   **Buffer Strategy:** Implemented a **1024-sample rolling buffer** to accommodate the FIR kernel length and ensure continuous convolution.
*   **Windowing:** Added a **Hann window** application prior to FFT (Weighted Overlap-Add prep) to suppress spectral leakage.

#### B. Global Signal Scaling & Gain
*   **Feature:** Moved the "Signal Gain" tuner to the **Settings** tab.
*   **Logic:** Gain is now applied in the Python engine directly to the cleaned signal *after* artifact rejection. This ensures that amplification does not erroneously trigger "DIRTY" signal status while still scaling the UI visualizations and power metrics.

#### C. Multi-Channel PSD Fusion
*   **Change:** Refactored the engine to compute Power Spectral Density across all 4 sensors (**TP9, AF7, AF8, TP10**) instead of just AF7.
*   **Result:** The PSD graph and power metrics now reflect a fused average of the entire head, providing a more robust research metric.

#### D. Trends UI Enhancements
*   **Visibility Toggles:** Added checkboxes to selectively show/hide Delta, Theta, Alpha, Beta, and Gamma trends.
*   **Aesthetics:** Increased line thickness to **3.0** for all charts to improve readability and visual impact.

### 3. Verification & Resolution
*   **Emulator Connectivity:** **RESOLVED.** Stream prioritization (name='Muse') and 1024-sample buffer management have stabilized the end-to-end signal path. The software now reliably detects the emulator over idle background streams.
*   **UI Stability:** **RESOLVED.** Moving math to background threads and implementing batch ScottPlot refreshes has eliminated UI freezing during high-speed emulator updates.

### 4. Mandatory Deployment Rule
**PUSH TO GITHUB APPROVED.** The signal path for the emulator is confirmed stable. This build serves as the new baseline for high-precision FIR-based neurofeedback.
