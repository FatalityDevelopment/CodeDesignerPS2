# Design Doc: PCSX2 `PINE.cpp` support for `READMEMBATCH`

## Context

CodeDesigner bridge now supports a `READMEMBATCH` command on its local bridge socket to reduce per-address polling amplification. The current bridge implementation still performs N individual reads against the PINE client API. To fully realize the performance win, PCSX2 PINE should expose a native batch-memory read operation in `PINE.cpp` so one PINE request can return many address slices.

This document describes the proposed PCSX2-side changes so implementation can continue in the PCSX2 repo.

---

## Goals

1. Add a **native batch read** opcode/handler in PINE (`PINE.cpp`) for fixed-size reads from multiple addresses.
2. Preserve compatibility with existing single-read commands.
3. Keep validation/limits explicit and deterministic.
4. Provide response encoding that is cheap to parse and robust under partial failures.

---

## Non-goals

- No changes to debugger semantics beyond read batching.
- No write-batch support in this pass.
- No transport rewrite (keep existing PINE transport framing).

---

## Proposed wire contract (PINE-level)

## Request

- `Opcode`: `READ_MEM_BATCH` (new)
- `CpuSel`: EE/IOP (reuse existing selector behavior)
- `LengthPerAddress`: `u16` (or existing field width) — fixed length for each address
- `Count`: number of addresses (`u16`/`u32`, depending on existing PINE conventions)
- `Addresses[]`: packed list of virtual addresses (`u32` each)

## Response

- `Status`: success / partial / error
- `CountReturned`
- Repeated entries:
  - `Address` (`u32`)
  - `ResultCode` (`ok`, `invalid_addr`, `unmapped`, `fault`, etc.)
  - `ByteCount` (`u16`)
  - `Bytes[ByteCount]`

This allows partial success while preserving per-address error visibility.

---

## Validation and limits

Add hard limits in `PINE.cpp`:

- `MAX_BATCH_COUNT` (initial suggestion: 1024)
- `MAX_LENGTH_PER_ITEM` (initial suggestion: 256 for live polling path)
- `MAX_TOTAL_BYTES` (initial suggestion: 1 MiB serialized payload cap)

Reject request with explicit error when limits exceeded.

Address normalization and CPU-specific validation should mirror existing single-read behavior:

- EE read path: existing address translation/mapping rules.
- IOP read path: existing mirror/clamp behavior.

---

## Execution model in `PINE.cpp`

1. Decode request envelope and validate fields.
2. Pre-size response buffer conservatively to reduce reallocations.
3. Iterate addresses:
   - Attempt read via existing memory access primitive used by single-read opcode.
   - Append per-address result entry.
   - Stop early if `MAX_TOTAL_BYTES` would be exceeded.
4. Return `partial` if any entry fails or early stop occurs.

### Error policy

- Do **not** fail whole batch for one bad address.
- Fail whole request only for malformed packet / invalid parameters / impossible allocation.

---

## Bridge integration changes after PINE update

Once PCSX2-side batch opcode exists:

1. Add `PineClient.ReadBytesBatch(addresses, length, cpuSel)` in CodeDesigner bridge.
2. Update bridge `MemoryService.HandleReadMemBatch(...)` to call one PINE batch command when in PINE mode.
3. Keep existing fallback loop for:
   - older PCSX2 versions without new opcode,
   - internal emulator backend.
4. Add capability detection at connect-time (`GetVersion` or feature bit query).

---

## Backward compatibility strategy

- Version-gate on PINE feature support.
- If unsupported, bridge keeps current per-address read loop.
- No frontend protocol changes required.

---

## Observability additions

Add debug counters/log entries in PCSX2 PINE path:

- `batch_requests`
- `batch_items_total`
- `batch_items_failed`
- `batch_bytes_total`
- `batch_duration_us` (avg/p95 if available)

These are useful for validating real gains in label polling workload.

---

## Test plan (PCSX2 repo)

1. **Unit-ish parser tests** for malformed request decoding:
   - zero count, too-large count, too-large length, truncated payload.
2. **Functional**:
   - all-valid addresses (expect full success).
   - mixed valid/invalid addresses (expect partial + per-entry status).
   - IOP mode and EE mode coverage.
3. **Performance smoke**:
   - compare 128 single reads vs 1 batch request over local transport.
   - confirm lower total latency and fewer syscalls/messages.
4. **Compatibility**:
   - existing single READ_MEM command unchanged.

---

## Rollout checklist

- [ ] Add opcode enum and docs/comments in PINE protocol definitions.
- [ ] Implement handler in `PINE.cpp`.
- [ ] Add any serializer helpers for repeated result entries.
- [ ] Add tests.
- [ ] Expose feature/version marker.
- [ ] Bridge-side capability detection and fallback.
- [ ] End-to-end verify with CodeDesigner label live polling.

---

## Risks and mitigations

- **Large payload risk**: enforce `MAX_TOTAL_BYTES` and return partial.
- **Regression risk in memory read path**: reuse existing single-read primitives.
- **Version skew**: capability gate + fallback loop in bridge.

---

## Suggested first implementation scope

Start with **fixed-size batch reads only** (all items same byte length).

That matches current hot path (4-byte scalar polls) and is enough to drastically reduce command count. Variable-length batches can be added later if needed.
