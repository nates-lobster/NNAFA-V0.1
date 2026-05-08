# Project Manifest: NeuroMemoryStudy (EEG Neurofeedback & Memory Research)

## 1. Project Overview
* **User:** Nathan Chen (Student, Engineering/Neuroscience focus).
* **Objective:** Develop a custom Windows application to investigate the correlation between neurofeedback-assisted meditation and performance on memory tests.
* **Hardware:** Muse 2/S Headset.
* **Primary Metrics:** Alpha/Beta/Theta power ratios and user-specific baseline thresholds.

## 2. Technical Architecture (Hybrid Build)
The project utilizes a "Twin-Engine" strategy to balance UI performance with signal processing efficiency.

### A. The "General" (Frontend - C# WPF)
* **Environment:** Visual Studio (.NET 8.0/Desktop Development).
* **Role:** User interface, participant management, memory test display, and external process control.
* **External Control:**
    * **BlueMuse:** Triggered via URI (`bluemuse://start?streamfirst=true`).
    * **LabRecorder:** Controlled via TCP (Port 22345) for automated start/stop and metadata injection.

### B. The "Worker" (Backend - Python)
* **Environment:** VS Code (Python 3.11+, `pylsl`, `scipy`, `numpy`, `pygame`).
* **Role:** Real-time LSL stream handling, signal processing, and audio modulation.
* **Inter-Process Communication (IPC):** WebSockets or ZeroMQ to send live data to the C# UI.

## 3. Core Functional Modules

### Monitoring Tab
* **Live EEG:** 4-channel view (TP9, AF7, AF8, TP10).
* **Signal Integrity:** Visual indicators (R/Y/G) based on sensor contact quality.
* **Power Spectrum:** Real-time bar graph of Delta, Theta, Alpha, Beta, and Gamma bands.

### Neurofeedback Tab
* **Protocol Selector:** Dropdown to select ratios (e.g., Alpha:Beta).
* **Calibration:** Automated 60-second baseline recording to calculate personalized Mean and Standard Deviation.
* **Proportional Feedback (Rainforest):** * **Indicator:** `rain.wav` volume inversely proportional to "Success Ratio."
    * **Reward:** Random `bird_chirp.wav` triggers when ratio exceeds `Mean + 1 StdDev`.

### Research & Data Tab
* **Participant Management:** ID, Session, and Metadata entry.
* **Memory Test:** Integrated module to run recall tests post-meditation.
* **Data Export:** Automatic sync of "Meditation Scores" and "Test Scores" to a unified CSV/JSON.

## 4. Signal Processing Pipeline
1.  **Ingestion:** Pull 256Hz LSL chunk.
2.  **Preprocessing:** * Notch Filter (60Hz) to remove electrical hum.
    * Bandpass Filter (1Hz–40Hz) for drift/muscle noise removal.
3.  **Analysis:** FFT via Welch's method to extract band power.
4.  **Artifact Rejection:** Logic to "gate" or pause feedback during blinks/jaw clenches.

## 5. Master Structure Tree (For AI Agents)
* **Root: NeuroMemoryStudyApp**
    * **/Frontend (C# WPF)**
        * `MainWindow.xaml` (TabControl: Monitoring, Neurofeedback, Research).
        * `LSLClient.cs` (Handles WebSocket/ZMQ reception).
        * `ExternalProcessManager.cs` (BlueMuse/LabRecorder automation).
    * **/Backend (Python)**
        * `engine.py` (Main loop).
        * `dsp.py` (Filtering and FFT logic).
        * `audio_manager.py` (Pygame volume/trigger logic).
    * **/Assets**
        * `/Audio/Rain/` (Loops).
        * `/Audio/Birds/` (Reward triggers).
        * `/Data/` (XDF and CSV output).

## 6. Development Status
* **Environment Setup:** Visual Studio (.NET Desktop & Python Workloads) installation in progress.
* **LSL Connectivity:** Verified via BlueMuse and raw Python scripts.
* **Next Steps:** Implement the Python "Calibration" loop and establish the C# UI boilerplate.
