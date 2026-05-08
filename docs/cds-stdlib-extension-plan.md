# CDS Standard Libraries — Extension Plan & Ideas

Files added:
- `StdLib/mem.cds` — memcpy, memset, memcmp, memchr
- `StdLib/str_ext.cds` — find, substr, startswith, endswith, atoi, itoa

Next recommended libraries and features to implement (prioritised):
1. Heap / Allocator
   - Minimal bump allocator (malloc/free stub) to allow functions that return allocated strings.
   - Integration with host/injector to reserve a heap block.

2. Format / Print
   - `vsnprintf`-like minimal implementation: `%d`, `%x`, `%s` support.
   - `lib_printf` that writes into a buffer or a serial console.

3. Numeric utils
   - `itoa` variants for hex, zero-pad, base conversions.
   - Floating-point -> ascii helpers if FP available.

4. Encoding
   - UTF-8 aware utilities (validate, strlen in codepoints, conversion to ASCII-safe).
   - Base64 encode/decode.

5. Hashing / CRC
   - CRC32, FNV-1a — useful for checksums, quick symbol lookup.

6. Tokeniser & StringBuilder macros
   - `str_split` that returns pointer-array (requires heap).
   - Higher-level `join` macro that concatenates an array of pointers.

7. IO / Host interop helpers (sidecar)
   - `host_log` / `host_assert` macros that emit `log`/`print` sidecar directives for easier debugging.
   - `save_map` helper to emit map-writing sidecar (integrates with host bridge).

8. Data structure helpers
   - Simple linked-list, vector (dynamic array) implementations (needs allocator).

9. Math
   - Integer division/mod helpers for platforms without convenient immediate division support in assembler emit.

10. Tests & Examples
   - CDS unit examples exercising each library function.
   - Example cheat/snippets that reuse libraries to demonstrate real-world usage.

If you want, I can:
- Implement the bump allocator and a small `str_split` that returns pointer array (requires choosing a heap area).
- Produce unit CDS examples exercising each function.
- Add host-side wiring (C# bridge) to reserve memory/heap when injecting CDS-built code into a process.

Which of the above should I implement next?