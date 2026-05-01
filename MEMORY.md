# Project Memory: NeuroMemoryStudy

## Long-Term Memory (Persistent Rules & Setup)
* **Hardware:** Muse 2/S Headset via BlueMuse (LSL). 256Hz sampling. 4 Channels (TP9, AF7, AF8, TP10).
* **Frequencies:** Bandpass 1Hz - 40Hz. 60Hz Notch. Beta Band is 12-30Hz (no gap).
* **UI/Visuals:** 
  - Update rate is ~10Hz (25 sample stride) for professional responsiveness.
  - Neurofeedback audio uses NAudio in C# with a custom `LoopingSampleProvider` to play continuous tones/MP3s.
  - Smooth volume fading implemented via a 50ms DispatcherTimer in C#.
  - DataStreamers are used for both EEG (250 points) and Trend plotting (1000 points) to allow infinite scrolling.
* **Math/DSP:**
  - Artifact Threshold is set to 150.0 uV.
  - Exponential Moving Average (EMA) (alpha=0.2) applied directly to the raw PSD array in Python *before* band power extraction to ensure buttery smooth FFT graphs and stable neurofeedback ratios.

## Short-Term Memory (Active Bugs & Notes)
* **UNRESOLVED (April 30):** 
  - **4 Scrolling EEG Graphs:** Still blank/not drawing data in the 2x2 grid. Need to investigate why mean-centering didn't bring them into view or if the streamers aren't receiving the arrays properly.
  - **Delta, Theta, Gamma Indicators:** Still showing 0.00 at the top of the app. Need to verify JSON property naming between engine.py and C# UpdateUI logic.
  - **LSL Broadcast:** LabRecorder still cannot see the `NeuroMemory_Metrics` stream.
* **Resolved (April 30):** 
  - Integrated Console: Added `sys.stdout.flush()` to fix silent C# integrated terminal.
  - Smoothing: EMA (alpha=0.1) on PSD array emulates Muse's 3s glide perfectly.
  - Audio: NAudio integrated with custom MP3 looping and smooth volume lerping.
  - Calibration: 60s baseline auto-tuning for Target Ratio implemented.
