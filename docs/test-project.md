[← Back to Documentation Index](README.md)

# Test Project

`CodeDesigner.Ps2.TestProject` is the main test suite: about 1,375 **MSTest** tests covering the instruction set, the emulator, CDS, cheats, file formats and parts of the Bridge. The MCP server has its own suite in `CodeDesigner.AI.Tests` (about 120 tests).

---

## Running

```powershell
dotnet test CodeDesigner.Ps2.TestProject/
dotnet test CodeDesigner.Ps2.TestProject/ --filter "FullyQualifiedName~TestCheatPorter"
dotnet test CodeDesigner.AI.Tests/
```

Building the test project builds `CodeDesigner.Ps2.Database` too, which migrates LocalDB. Add `-p:SkipDatabaseInit=true` if you don't want that. CI runs the main suite in Release on every push (`.github/workflows/build.yml`).

---

## Layout

| Folder | Tests | Covers |
|---|---|---|
| `Common/` | ~385 | Encoding/decoding, every instruction's disassembly and execution, VU0 macro mode, the exhaustive disassemble → assemble round trip, comment-hint and pointer-chain analysis |
| `Emulator/` | ~600 | EE and IOP CPUs, CPU accuracy, TLB, exceptions, memory buses, timers, INTC, DMAC (EE and IOP), GIF/GS, VIF, VU0 micro mode, VU1, SIF, CDVD, SIO2, SPU2, the dual-CPU scheduler, ELF/IRX and game loading |
| `CdsLanguage/` | ~220 | Preprocessor, parser, compiler, diagnostics and regression fixes |
| `Cheats/` | ~70 | Cheat file format, one-shot engine, frame-loop engine, converter ciphers, porter |
| `Bridge/` | ~55 | Packet labelling (every Medius / DME / SOCOM II message has a name) and the value scanner |
| `Recompiler/` | ~25 | ELF parsing and instruction translation |
| `Compression/` | ~13 | APACHE / ZDB crypto and readers, ZED containers, Zipper round trips on real files |
| `MemoryCardManager/`, `HddManager/` | ~10 | Memory card and HDD image handling |

Some `Compression/` tests read real game files (an EE dump, a retail ZDB) and report *Inconclusive* when those files aren't present.

---

## Writing tests

- Put new tests in the folder for the project they test, one `Test<Thing>.cs` class each.
- For a new instruction, add both a disassembly test and an execution test, following `Common/TestFullInstructionCoverage.cs` and `Common/TestPS2Instructions.cs`. The round-trip test (`Common/TestDisasmRoundTrip.cs`) then checks that its printed form assembles back.
- Check instruction semantics against PCSX2 (`pcsx2/pcsx2/R5900OpcodeImpl.cpp`, `VUops.cpp`, `FPU.cpp`).
- `SYSCALL` tests expect a `SystemCallException` from a direct `Exec()` call.
