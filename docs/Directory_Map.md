# Project Directory Map

## Overview
This document provides a map of the current project structure, organized for practicality.

## Core Structure
- `src/`: All source code and project binaries.
    - `Backend/`: Server-side and data processing logic.
        - `PyApp/`: Main Python engine and LSL interface.
        - `Tools/`: Supporting third-party software.
            - `BlueMuse/`: Bluetooth to LSL bridge.
            - `LabRecorder/`: Recording tool for LSL streams.
        - `test_lsl.py`: Script for testing LSL connectivity.
    - `Frontend/`: User interface and client-side code.
        - `App/`: Main WPF/C# Application source.
        - `assets/`: UI resources and multimedia.
    - `Neurofeedback_App.slnx`: Visual Studio solution entry point.
- `docs/`: Documentation and project history.
    - `Documentation/`: Logged progress organized by date (e.g., `4-30`, `5-1`).
    - `GEMINI.md`: AI-specific system architecture notes.
    - `MEMORY.md`: Project history and technical context.
    - `Changelog.md`: Record of major changes.
- `logs/`: Application run logs and historical data.
    - `Archive/`: Backup of previous session logs.

## Maintenance & Backups
- `.backups/`: Contains the `Software/` folder with RStudio/R/Rtools installations (isolated to reduce clutter) and original zip files.
- `temp_repo/`: A clean backup of the latest project state used for recovery.

---
*Last updated: 2026-05-07*
