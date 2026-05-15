# CodeDesigner Bridge Review (SignalR ignored): what needs work next

This review focuses on **performance, code quality, architecture, and code debt** in the bridge + VS Code extension path, explicitly **excluding SignalR discussion**.

## Top priorities (ranked)

1. **Live label polling amplification + duplicate memory reads**
2. **Command channel head-of-line blocking from long-running operations**
3. **Overgrown extension provider/client classes (maintainability risk)**
4. **Protocol ambiguity (response/event multiplexing by heuristics)**
5. **Insufficient observability (no built-in per-command latency/volume metrics)**
6. **Potential over-fetching in hot UI loops (disasm/status polling cadence)**
7. **Back-compat/dead-path cleanup debt in command aliases + legacy switches**

---

## 1) Live label polling amplification (highest ROI)

## What I see

- Label manager polls every 500ms for dynamic values.
- Provider performs per-entry `readMemory(...)` calls sequentially for root entries and member-path dereferences.
- Member resolution path can multiply reads by depth and count.

## Why it matters

- Spikes command count and increases contention with stepping/breakpoint/runtime controls.
- Produces avoidable latency and jitter in UI responsiveness.

## What to do

- Add `READMEMBATCH` in bridge `MemoryService`.
- Add `RESOLVEMEMBERPATHSBATCH` (or equivalent) so pointer-chain traversal happens server-side.
- In the extension, swap per-address loops for a single request per tick per panel.
- Add per-panel byte/request budgets so dynamic label sets can’t overwhelm command throughput.

---

## 2) Head-of-line blocking on one command socket

## What I see

- Most extension commands share one JSON-over-TCP command channel.
- Some commands are long-running (scan/recompile/import/export style operations).
- Even with async calls, user-perceived responsiveness can degrade when queue pressure rises.

## What to do

- Introduce explicit command classes/priority lanes in client scheduler:
  - **Interactive**: step/pause/breakpoint/read-small-memory
  - **Background**: scans/import/export/analysis
- Add cancellation tokens and user-cancel hooks for long operations.
- Add bounded in-flight concurrency and queue backpressure handling.

---

## 3) Monolithic extension modules (code quality debt)

## What I see

- `socketClient.ts` and disasm provider logic are very large and multi-responsibility.
- Transport, protocol parsing, domain APIs, and UI orchestration are tightly mixed.

## Why it matters

- Harder testing.
- Higher regression risk for unrelated feature changes.
- Slower onboarding and review cycles.

## What to do

- Split into layered modules:
  - `transport/` (TCP framing/connect/retry)
  - `protocol/` (request-response-event envelope)
  - `bridgeApi/` (memory/labels/net/pad/etc domain clients)
  - `uiOrchestration/` (provider-specific orchestration)
- Add strict interfaces and dependency injection to make provider logic testable.

---

## 4) Protocol ambiguity / heuristic parsing

## What I see

- Client distinguishes pushed events vs responses using shape heuristics.

## Risk

- Edge-case misrouting under malformed/partial payload scenarios.
- Harder forward compatibility when payload shapes evolve.

## What to do

- Adopt explicit envelope (`kind`, `id`, `method`, `payload`, `ok/error`, `version`).
- Keep adapter/shim compatibility for legacy messages during migration.

---

## 5) Observability gaps

## What I see

- No first-class built-in metrics for command rate, latency percentile, payload bytes, drop/retry counts.

## What to do

- Add low-overhead metrics (rolling window):
  - requests/sec by command
  - p50/p95 latency by command
  - bytes in/out per second
  - queue depth and wait time
  - timeout/error counts
- Add a debug command (`GETBRIDGEMETRICS`) and optional VS Code diagnostics pane.

---

## 6) Polling cadence and over-fetching in UI paths

## What I see

- Fixed poll intervals in some UI features regardless of visibility/focus/runtime state.

## What to do

- Adaptive polling policy:
  - running + focused: faster
  - paused/unfocused: slower
  - hidden panel: stop or low-frequency heartbeat
- Debounce/coalesce repeated requests (same address range within tick window).

---

## 7) Command alias/legacy switch cleanup debt

## What I see

- Command handling includes compatibility aliases and fallback logic from migration.

## Risk

- Harder reasoning about canonical behavior.
- Increased maintenance cost and inconsistent error handling.

## What to do

- Generate a canonical command map and mark aliases as deprecated.
- Add one place for validation/error-shaping.
- Remove dead aliases after one release cycle with telemetry confirmation.

---

## 8) Data-format and binary payload efficiency

## What I see

- Some paths use base64/hex payloads for memory data over JSON.

## Tradeoff

- Simpler compatibility but increased payload overhead and encode/decode cost.

## What to do

- Keep JSON control channel, but optimize data paths:
  - For repeated memory polling, prefer compact binary-safe framing payloads or chunked base64 maps.
  - Ensure chunk-size defaults are tuned and measurable.

---

## 9) Testability gaps

## What I see

- Hot-path behavior (polling, batching, queue fairness, fallback logic) is hard to unit test in current structure.

## What to do

- Add contract tests at protocol layer (request/response/event ordering).
- Add extension-side scheduler tests (priority fairness, cancellation, timeout).
- Add bridge memory-batch tests (correct mapping, partial failure behavior).

---

## Concrete execution plan (no SignalR)

## Phase 1 (immediate)

1. Implement `READMEMBATCH` + extension integration for live labels.
2. Add request budgeting + adaptive polling for label manager.
3. Add command latency/rate metrics and a quick diagnostics view.

## Phase 2 (short-term)

1. Introduce explicit protocol envelope with compatibility shim.
2. Split `socketClient` and provider orchestration into layers.
3. Add priority scheduler + cancellation for long-running commands.

## Phase 3 (stabilization)

1. Remove deprecated aliases/fallback paths validated as unused.
2. Expand automated tests for batching/scheduling/protocol ordering.
3. Tune defaults using real metrics (not assumptions).

---

## Bottom line

Ignoring SignalR for now is the right call. The biggest wins are in:

- **batching live memory reads**,
- **reducing command-channel contention**,
- **decomposing monolithic extension modules**,
- **formalizing protocol semantics**, and
- **adding observability + test coverage for the hot paths**.

These changes directly improve responsiveness, reliability, and maintainability without requiring a transport rewrite.
