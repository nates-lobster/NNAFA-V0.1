# NeuroMemoryStudy - April 30, 2026 Dev Journal (Final Wrap-up)

## Objective
The goal was to transform the prototype into a clinical-grade neurofeedback suite with custom audio, stable metrics, and integrated external tool management.

## Progress & Achievements

### 1. The "Muse-Quality" Engine
**Achievement:** Upgraded Python processing to 10Hz refresh rate.
**Math implementation:** I applied a heavy **Exponential Moving Average (EMA)** with a factor of 0.1 to the raw Power Spectral Density (PSD) array. 
*   **Significance:** This creates a ~3-second "glide" window, which is the industry standard for commercial neurofeedback (like Muse). It eliminates the jitter that makes auditory feedback distracting.

### 2. Custom C# Audio Architecture (NAudio)
**Achievement:** Successfully moved all audio logic to C# to eliminate WebSocket latency and support custom files.
*   **Volume Lerping:** To ensure a "high-end" feel, volume changes are linear-interpolated over 50ms intervals. This creates seamless fading rather than clicking or popping sounds.
*   **Custom MP3/WAV Support:** Built a file-picker and an infinite looping provider to allow the user to use any background meditation track.

### 3. Integrated Control Center
**Achievement:** Redesigned the UI into 5 distinct tabs (Signals, Trends, Neurofeedback, Settings, External Tools).
*   **Process Redirection:** The Python engine now runs as a hidden background process, with its console output piped into a dark-themed integrated terminal inside the C# app.

## Hardships & Persistent Challenges

### 1. The "Invisible" LSL Stream (CRITICAL - UNSOLVED)
**Problem:** Despite the Python engine calculating all math perfectly (verified via the WebSocket UI and internal console), the **LSL metrics stream remains invisible to LabRecorder.**
**Attempts to fix:** Moved LSL Outlets to global scope and forced a nominal sampling rate (10.24Hz).

### 2. UI Data Binding & Streamer Failures (UNSOLVED)
**Problem:** Several visual elements are currently non-functional:
*   **4 Small EEG Graphs:** The 2x2 grid for TP9, TP10, AF7, and AF8 sensors remains blank. Despite implementing mean-centering to remove DC offsets, the ScottPlot DataStreamers are not displaying the incoming arrays.
*   **Metric Indicators:** The Delta, Theta, and Gamma readouts in the top header bar are stuck at 0.00. 
**Status:** These issues likely stem from a mismatch in JSON property naming between the Python dictionary and the C# `UpdateUI` logic, or a race condition during the initialization of the ScottPlot objects.

## Future Research Questions
*   **Fixing the data pipeline:** Priority #1 for tomorrow is auditing the JSON bridge to ensure every brainwave band and sensor array is being correctly deserialized and rendered. Fixing the LSL metrics broadcast remains essential for session recording synchronization.
