# Nate's Neuroanalysis and Neurofeedback App (NNAFA) V0.1 - Project Journey & Reflection

## 1. Project Overview
NeuroMemoryStudy is a deterministic neurofeedback platform designed to pair Muse EEG headsets with a real-time signal processing engine and a WPF-based monitoring dashboard. The goal of V0.1 was to establish a functional "Gold Path" from raw EEG ingestion to audio reward modulation.

### Core Stack:
- **Frontend:** C# / WPF (.NET 10.0) with ScottPlot 5.
- **Backend:** Python 3.11+ (asyncio, websockets, scipy).
- **IPC:** WebSockets (JSON payloads).
- **Hardware:** Muse 2/S via LSL (BlueMuse).

---

## 2. The Journey: V0.1 Milestones
- **Phase 1: Ingestion:** Successfully implemented LSL stream resolution and multi-channel buffering.
- **Phase 2: DSP Engine:** Developed a stateful FIR/IIR filtering pipeline with real-time Welch PSD estimation.
- **Phase 3: IPC Bridge:** Established a bidirectional WebSocket bridge for telemetry and configuration.
- **Phase 4: Feedback Loop:** Integrated `NAudio` for real-time volume modulation based on brainwave ratios (Alpha/Beta).

---

## 3. Post-Mortem: What Went Wrong?
As the project progressed, a "Development Wall" was hit where simple bug fixes became exponentially difficult and token-expensive. The following factors contributed to this friction:

### 3.1 Folder Sprawl & Version Desync
The project suffered from "directory duplication" (e.g., `PyApp/` vs `src/Backend/PyApp/`). Agentic edits frequently targeted the wrong files or applied fixes inconsistently across identical-looking paths. This led to "phantom bugs" where a fix was implemented but not observed in the active runtime.

### 3.2 Implicit IPC Contracts
The telemetry payload between Python and C# was defined implicitly. Changes to the backend (like the "Stage 3" denoising data) would break the frontend silently or cause rendering failures. Without a strict schema (e.g., Protobuf or JSON Schema), the two layers drifted apart.

### 3.3 Threading & Shutdown Deadlocks
The WPF UI thread and the asynchronous IPC bridge were tightly coupled. Shutdown attempts often resulted in deadlocks because the UI thread was waiting for a task that was itself waiting for a UI Dispatcher call. This manifested as the "Crash on Exit" behavior.

### 3.4 Token Burn vs. Value Gain
The lack of unit tests for individual signal processing components forced the agent to debug "in production" using live streams. This resulted in high-volume tool usage to diagnose issues that should have been caught by local test suites.

---

## 4. The `fail_log.md` Validation
The root file `fail_log.md` serves as an accurate diagnostic of the V0.1 failure modes. Its critique of **"uncontrolled agent edits"** and **"mixed responsibility"** matches the empirical evidence gathered during the final stabilization phase.

**V0.1 Key Failure:** We prioritized *behavior* over *structure*, leading to a system that works but is too fragile to maintain.

---

## 5. Roadmap for V0.2: The "Clean Pipeline"
The next version should be built on the principles of **Determinism** and **Strict Isolation**.

### 5.1 Architectural Shift
- **Contract-First Design:** All IPC must use versioned, strictly-validated schemas (Protobuf preferred).
- **Single Source of Truth:** Flatten the folder structure. Remove all shadow directories.
- **Stateless DSP:** Move signal processing into pure, stateless Python functions that can be unit-tested with recorded EEG playback.

### 5.2 Agentic Approach
- **Modular Delegation:** Assign specific sub-agents to distinct layers (e.g., a "DSP Agent" and a "WPF UI Agent") to prevent cross-layer context pollution.
- **Test-Driven Implementation:** No feature is "complete" until a replay-test verifies it against a gold-standard EEG dataset.

### 5.3 Technical Improvements
- **Decoupled Shutdown:** Implement a more robust "Zombie Process" management strategy to ensure Python processes die when the WPF window is closed, regardless of thread state.
- **Shared Schema Library:** A central JSON/Proto file shared by both FE and BE projects.

---

## 6. Conclusion
V0.1 succeeded in proving the concept but failed in scalability. V0.2 will focus on building a "Hardened Pipeline" that treats the EEG stream as a data-integrity challenge rather than just a visualization task.
