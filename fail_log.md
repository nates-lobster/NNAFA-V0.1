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

### 12.8 Undefined system state transitions
- Do NOT allow state changes without explicit state machine definition
- Do NOT infer user state from UI behavior

---

## 13. Final Principle

If a change cannot be:
- validated by schema
- reproduced via replay
- isolated to a single layer

→ It is invalid system design and must be rejected.
