# Zipper backend fix plan: load full map package into backend memory

## Decision
Yes — for map preview scenarios we should load the **entire map package** into backend memory, recursively extracting `.zdb`, `.zar`, and `.zed` containers and flattening all addressable resources into one merged lookup for the webview.

This directly solves the `common/` + map-specific dependency problem where weapon/model textures are missing at preview time because the frontend resolves files before all related resources are visible.

## Why this is the right tradeoff
- **Fixes correctness first:** the renderer can only choose the right texture if all candidate resources are available up front.
- **Keeps logic centralized:** decode/resolve stays in `CodeDesigner.Ps2.FileFormats/Zipper` instead of duplicating special cases in TS.
- **Avoids full platform rewrite:** we do not need to move every viewer workflow at once; only map-package preload path needs recursive behavior.
- **Predictable performance:** we can bound memory with cache policy (LRU + size caps) and optional temp-file spill.

## Scope (what to build now)

### 1) Backend package preloader (C#)
Implement in `CodeDesigner.Ps2.FileFormats/Zipper`:
- Recursive extractor for `.zdb`, `.zar`, and `.zed` nodes found inside a package.
- Build a `ResourceBundle` in memory:
  - `Dictionary<string, ResourceBlob>` keyed by normalized virtual path.
  - secondary index by basename and extension for legacy references.
  - provenance metadata (source container, depth, original path) for debugging.

Supported traversal:
1. Root `.zdb` / `.zar` / `.zed` entries
2. Nested `.zar` and `.zed` entries
3. Nested `.zdb` entries discovered inside container nodes (same recursion guard)

Safety controls:
- max recursion depth (default 6)
- max total decompressed bytes (configurable)
- cycle guard (hash + logical path)

### 2) Deterministic merge/precedence rules
When duplicate logical resources exist, resolve with deterministic order:
1. Map-local path wins over `common/`
2. Shallower path wins over deeper nested fallback
3. Latest container in explicit traversal order wins as final tie-break

Return chosen source in diagnostics so we can explain why a texture was selected.

### 3) Bridge API for frontend
In `CodeDesigner.Ps2.Bridge` expose commands that let the frontend stop decoding archives itself:
- `ZIPPER_PRELOAD_MAP_PACKAGE`
  - input: map package path
  - output: preload token, stats (resource count, bytes, build ms)
- `ZIPPER_GET_PRELOADED_RESOURCE`
  - input: preload token + resource key/query
  - output: bytes/base64 + content metadata
- `ZIPPER_RELEASE_PRELOAD`
  - explicit disposal endpoint


### Command contract details
- `ZIPPER_PRELOAD_MAP_PACKAGE`
  - request: `InputPath`
  - response: `preloadToken`, `resourceCount`, `totalBytes`, `cacheHit`
- `ZIPPER_GET_PRELOADED_RESOURCE`
  - request: `ZipperPreloadToken`, `Query`
  - compatibility: `ZipperSection` is still accepted as fallback token for older callers
- `ZIPPER_RELEASE_PRELOAD`
  - request: `ZipperPreloadToken`
  - compatibility: `ZipperSection` fallback remains supported during migration

### 4) In-memory cache
Add a process-local cache keyed by package fingerprint:
- key: `sha256(file path + mtime + size)` (or archive header digest)
- value: `ResourceBundle`
- policy:
  - max bundles (e.g., 3)
  - max memory budget (e.g., 1.5 GB default, configurable)
  - LRU eviction
  - TTL (e.g., 10 minutes idle)

## Rollout sequence

### Phase A (low-risk implementation)
- Add preloader + merge resolver in backend only.
- Keep existing extraction commands untouched.
- Hide behind feature flag: `ZipperBackendPreloadV1`.

### Phase B (frontend wiring)
- Webview requests preload token when opening map package.
- Texture/model lookups go through backend token endpoints.
- Keep old frontend parser path as fallback toggle.

### Phase C (stabilization)
- Add telemetry counters:
  - preload duration
  - cache hit rate
  - memory used
  - unresolved texture count
- Validate known failing packages containing `common/` weapon assets.

### Phase D (cleanup)
- Remove frontend archive-decoding branches for map preview once parity is reached.

## Regression controls
- Do **not** change save/APACHE/RDR flows.
- Keep existing `ZIPPER_EXTRACT_ALL` behavior stable.
- Gate everything behind feature flag until preview parity is verified.

## Test plan

### Unit tests (FileFormats)
- recursive `.zdb` -> `.zar` resource discovery
- duplicate resolution precedence (`common/` vs map-local)
- recursion/cycle guards
- path normalization and basename fallback lookups

### Integration tests (Bridge)
- preload returns token + expected stats
- lookup returns expected bytes for known texture references
- cache hit/miss + eviction behavior

### Manual validation
- Open known failing map package with weapon in `common/` and map-local dependent refs.
- Verify weapons + map textures render without missing-material fallback.
- Reopen same package and verify cache-hit startup improvement.

## Performance expectations
- First open: slower due to full preload (acceptable).
- Subsequent opens: near-instant with cache hit.
- Better UX than ad-hoc per-texture extraction failures and retries.

## Recommendation
Proceed with the full-package backend preload for map preview use cases. It is the fastest path to correctness with controlled regression risk, and it lets us retire brittle frontend archive edge-case logic over time.
