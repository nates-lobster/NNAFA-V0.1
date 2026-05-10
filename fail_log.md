# Neurofeedback System Rebuild Spec (Strict)

## 1. System Goal
Build a deterministic neurofeedback pipeline using Muse EEG + Python backend + C# frontend.

No implicit behavior. No cross-layer logic leakage. All interfaces strictly defined.

---

## 2. Architecture

Muse EEG
→ Ingestion Layer
→ Python Processing Service
→ Feature Extraction
→ State Engine
→ C# Frontend

---

## 3. Hard Rules

- No shared state between Python and C#
- No hidden assumptions between layers
- No logic duplication across layers
- All communication via explicit API contracts
- All outputs must be reproducible from inputs

---

## 4. Contracts (Mandatory)

All inter-layer communication MUST use versioned schemas.

### Requirements:
- Defined input schema
- Defined output schema
- Strict validation (reject unknown fields)
- Versioned endpoints (/v1, /v2)

### Example:
Request:
```json
{
  "signal_window_ms": 1000,
  "features": ["alpha_power", "beta_power"]
}
```

Response:
```json
{
  "alpha_power": 0.72,
  "beta_power": 0.31
}
```

---

## 5. Layers

### 5.1 Ingestion Layer
- Receive EEG stream
- Forward raw data only
- No processing

### 5.2 Python Processing Layer
- Filtering
- Band power extraction
- Feature computation
- Stateless functions preferred

Output:
- numeric feature vector

### 5.3 Feature Layer
- Convert signals → metrics
- Examples: alpha_ratio, focus_score, relaxation_index
- Config-driven where possible

### 5.4 State Engine
- Converts features → system state
- States:
  - CALIBRATING
  - BASELINE
  - TRAINING_RELAXATION
  - TRAINING_FOCUS
- Explicit transitions only

### 5.5 C# Frontend
- Visualization only
- Feedback rendering only
- No signal interpretation

---

## 6. API Design

- REST or gRPC only
- Strict schema validation required
- No optional behavior inference
- Fail fast on invalid input

---

## 7. Observability

Mandatory logging:
- raw EEG
- processed signals
- features
- state transitions
- UI output

All sessions must be replayable exactly.

---

## 8. Configuration

No hardcoded feature logic.

Example:
```yaml
features:
  - type: band_power
    band: alpha
    range: [8, 13]
```

---

## 9. Testing

Required:
- unit tests (signal processing)
- contract tests (API validation)
- replay tests (deterministic session reproduction)

---

## 10. Build Strategy

Phase 1: Freeze current system behavior (record + document)
Phase 2: Build clean pipeline (ingest → process → features → state → UI)
Phase 3: Reintroduce features one at a time with tests

---

## 11. Constraint Rule

If a component cannot be fully specified as:
- input schema
- output schema
- deterministic transformation

→ It is not allowed in system core


## 12. Failure Log / Anti-Patterns (DO NOT DO)

This section records observed failure modes from previous iterations. These are explicitly forbidden in the rebuilt system.

### 12.1 No contract enforcement
- Do NOT allow free-form JSON between layers
- Do NOT accept implicit or optional fields without schema validation

### 12.2 Mixed responsibility across layers
- Do NOT implement EEG signal processing in C#
- Do NOT implement UI logic in Python
- Do NOT duplicate feature computation across layers

### 12.3 Agent-driven uncontrolled edits
- Do NOT allow agents to modify multiple layers simultaneously without tests
- Do NOT accept "best-effort fixes" without schema compliance
- Do NOT merge changes that are not replay-test verified

### 12.4 Non-deterministic system behavior
- Do NOT allow outputs that change for identical inputs
- Do NOT rely on runtime state without explicit logging
- Do NOT allow hidden state transitions

### 12.5 Debugging without replay capability
- Do NOT debug without full session logs
- Do NOT remove raw EEG + feature + state logs

### 12.6 Ad-hoc feature logic
- Do NOT hardcode feature thresholds in application logic
- Do NOT implement unversioned feature definitions

### 12.7 Tight coupling between C# and Python
- Do NOT share memory, globals, or implicit state
- Do NOT call Python functions directly from C# without API boundary

### 12.9 Folder Sprawl & Repository Hygiene (CRITICAL)
- **Do NOT allow directory duplication:** In V0.1, files existed in both root (`/PyApp`) and nested (`/src/Backend/PyApp`) paths. This causes agents to fix the "wrong" file, leading to phantom bugs.
- **Do NOT keep "Scratchpad" clutter:** Root-level scripts like `test_filter2.py` and `engine_output.txt` should be in a `/temp` or `/scratch` folder, never in the core source tree.
- **Do NOT allow untracked sub-repos:** The `temp_repo/` folder inside the project created recursive git confusion.

### 12.10 Documentation & Context Fragmentation
- **Do NOT scatter GEMINI.md files:** Having different instructions in `/`, `/docs`, and `/src` leads to conflicting agent behavior.
- **Do NOT rely on "Shadow Backups":** The folder `backup_20260508` contained a "better" version of the Stage 3 logic than the main `src` folder. This is a failure of version control.

---

## 13. V0.2 Structural Recommendation

To implement the "Strict Spec," the file layout must mirror the logic:

```text
/project_root
├── protocols/             # YAML/JSON definitions of NFB sessions
├── schemas/               # VERSIONED Protobuf/JSON schemas (The Source of Truth)
├── src/
│   ├── 01_ingestion/      # LSL / Hardware abstraction
│   ├── 02_processing/     # Pure Python DSP (Stateless, unit-tested)
│   ├── 03_bridge/         # The WebSocket/IPC Server
│   └── 04_frontend/       # C# WPF (Visualization ONLY)
├── tests/
│   ├── data/              # Recorded .XDF or .CSV EEG for replays
│   ├── unit/              # Testing 02_processing
│   └── contract/          # Validating 01 -> 04 schema compliance
└── GEMINI.md              # SINGLE source of truth for AI instructions
```

---

## 14. Final Principle

If a change cannot be:
- validated by schema
- reproduced via replay
- isolated to a single layer
- correctly located in a non-redundant file path

→ It is invalid system design and must be rejected.

