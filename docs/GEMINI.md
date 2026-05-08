# Project Instruction: NeuroMemoryStudy (Agent Guidelines)

You are an AI developer assisting in the NeuroMemoryStudy, a neurofeedback and EEG research project. This document serves as your operational manual.

## 1. Core Architecture (Mental Model)
*   **Frontend (C# WPF, .NET 10.0):** "The General". Handles UI, Participant Management, Data Logging, and high-level logic. Located in `src/Frontend/App/`.
*   **Backend (Python 3.11+):** "The Worker". Handles LSL stream acquisition, Digital Signal Processing (DSP), and audio triggers. Located in `src/Backend/PyApp/`.
*   **IPC Bridge:** WebSockets (Port 8765). Python emits JSON telemetry; C# subscribes.

## 2. Technical Commandments
*   **Sampling Rate:** Hardware is 256Hz.
*   **DSP Chain:** 
    *   1. Artifact Rejection (>100uV peak-to-peak on AF7/AF8).
    *   2. FIR Filtering (513-tap Blackman-window kernel).
    *   3. Bandpass (1Hz-40Hz) + 60Hz Notch.
    *   4. FFT: `scipy.signal.welch` (2s rolling window).
*   **Coding Style:**
    *   **C#:** Use `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`. Use `ScottPlot 5` for charts.
    *   **Python:** Unbuffered stdout (`-u`) for console integration. 
*   **Concurrency:** Use `SemaphoreSlim` in C# for socket safety. Never block the UI thread with math.

## 3. Operational Commands
*   **Build C#:** `dotnet build "src/Frontend/App/App.csproj"`
*   **Run C#:** `dotnet run --project "src/Frontend/App/App.csproj"`
*   **Run Python:** `python -u "src/Backend/PyApp/PyApp.py"` (Note: Usually spawned by the C# app).

## 4. Documentation Protocol
*   **Startup:** Always read `docs/MEMORY.md` first.
*   **Daily Logs:** When requested, create/update a folder in `docs/Documentation/MM-DD/`.
*   **State Tracking:** Update `docs/MEMORY.md` at the end of a session to hand off state to the next agent.
