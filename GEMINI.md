# Project Context: NeuroMemoryStudy (EEG Research)

**CRITICAL STARTUP RULE:** Always read [MEMORY.md](./MEMORY.md) at the start of every chat to load Long-Term and Short-Term persistent project facts.

**DOCUMENTATION RULE:** Upon request, find `../Documentation/` and create a subfolder for the current day formatted as `MM-DD` (no leading zeroes, e.g., `4-30`). Inside, create/edit `CLI.md` summarizing the session's work, including progress, bugs encountered, how they were solved, and detailed technical implementation notes suitable for a science fair logbook.

## 1. System Architecture
* **Frontend (App/):** C# WPF (.NET 10.0) - "The General". Primary UI and participant management.
* **Backend (PyApp/):** Python 3.11+ - "The Worker". Handles LSL stream, DSP, and audio triggers.
* **IPC Bridge:** WebSockets/JSON (Port 8765). Python broadcasts telemetry; C# subscribes.
* **Hardware:** Muse 2/S Headset via BlueMuse (LSL)[cite: 1, 2].

## 2. Technical Constraints (CRITICAL)
* **Sampling Rate:** 256Hz (Hardware Standard)[cite: 2].
* **Filtering:** MUST apply 60Hz Notch and 1Hz–40Hz Bandpass to raw data before FFT[cite: 1, 2].
* **FFT Method:** `scipy.signal.welch` using a 2-second rolling ring buffer[cite: 2].
* **Artifact Rejection:** Peak-to-peak amplitude gating (>100uV) specifically on AF7/AF8.
* **C# Conventions:** <Nullable>enable</Nullable> and <ImplicitUsings>enable</ImplicitUsings>.

## 3. Core Modules (Reference: Structure Tree.docx)
1. **Data Acquisition:** pylsl Stream Listener -> 2s Ring Buffer -> Preprocessing[cite: 2].
2. **Signal Engine:** FFT -> Band Power Extractor -> Ratio Logic (e.g., Alpha/Beta)[cite: 1, 2].
3. **Audio Engine:** `pygame.mixer`. Rain volume = 1.0 - SuccessRatio. Reward = Bird chirps[cite: 1, 2].
4. **UI Tabs:** 
    - **Monitoring:** Raw EEG (4 channels) + Signal Integrity Lights (R/Y/G)[cite: 1, 2].
    - **Neurofeedback:** Calibration (60s baseline) + Protocol Selector[cite: 1, 2].
    - **Research:** Participant Metadata + LabRecorder TCP Control (Port 22345)[cite: 1, 2].

## 4. Operational Commands
* **Build C#:** `dotnet build App/App.csproj`.
* **Run C#:** `dotnet run --project App/App.csproj`.
* **Run Python:** `python PyApp/engine.py`.