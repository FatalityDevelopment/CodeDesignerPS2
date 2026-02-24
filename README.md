# Mips64LE — PlayStation 2 Emotion Engine MIPS Toolkit

A C# library and toolset for working with the **Sony PlayStation 2 Emotion Engine (EE)** MIPS64 architecture. Provides a complete **assembler**, **disassembler**, **instruction decoder/encoder**, and **execution engine** for the PS2 EE, verified against [PCSX2](https://pcsx2.net/) reference behaviour.

---

## Project Structure

```
Mips64LE/
├── CodeDesigner.Mips64LE.Common/     # Core library (assembler, disassembler, instructions, emulator)
├── CodeDesigner.Mips64LE/           # Console application + socket server
├── CodeDesigner.Mips64LE.TestProject/ # Unit-test suite (511 tests)
└── vscode/codedesigner/             # VSCode extension frontend
```

---

## Quick Start

### Requirements
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Node.js + npm (for the optional VSCode extension)

### Build

```bash
dotnet build CodeDesigner.Mips64LE.sln
```

### Run Tests

```bash
dotnet test CodeDesigner.Mips64LE.TestProject/
```

All 511 tests should pass.

### Console Application

```bash
cd CodeDesigner.Mips64LE
dotnet run
```

To start the socket server for the VSCode extension:

```bash
dotnet run -- --server
```

---

## Features

| Feature | Status |
|---------|--------|
| MIPS EE core instruction decode/encode | ✅ |
| Assembler (text → machine code) | ✅ |
| Disassembler (machine code → text) | ✅ |
| Instruction execution / emulation | ✅ |
| Static analysis / annotation | ✅ |
| COP0 (TLB, ERET, exception registers) | ✅ |
| COP1 FPU (single-precision float) | ✅ |
| COP2/VU0 macro-mode vector instructions | ✅ |
| MMI multimedia extensions | ✅ |
| 64-bit EE-specific behaviour (HI/LO sign-ext) | ✅ |
| FPU flush-to-zero for denormals | ✅ |
| Branch delay slots + branch-likely | ✅ |
| COP0 exception dispatch (SYSCALL, BREAK, overflow, trap) | ✅ |
| ELF32 loader (EE `.elf` into RAM) | ✅ |
| EE timers T0–T3 + INTC | ✅ |
| EE DMAC (10 channels, normal mode) | ✅ |
| SIF (EE↔IOP sub-bus interface) | ✅ |
| IOP (R3000A) emulator + memory bus | ✅ |
| IOP timers T0–T5 | ✅ |
| IOP DMAC (13 channels) | ✅ |
| CDVD subsystem (ISO-9660) | ✅ |
| SIO2 + DualShock 2 pad input | ✅ |
| IOP module loader (IRX/ELF32) | ✅ |
| VSCode extension frontend | ✅ |

---

## PS2 Emotion Engine Architecture

### Core CPU
- **Architecture:** MIPS III / MIPS64 with Sony/Toshiba extensions
- **Clock:** ~294.912 MHz
- **Endianness:** Little-endian
- **Pipeline:** Superscalar, dual-issue, in-order
- **Registers:** 32 × 128-bit GPRs (64-bit integer lane + 64-bit extension), PC, HI, HI1, LO, LO1, SA

### EE-Specific Behaviours (vs standard MIPS64)
- `MULT`/`MULTU`/`DIV`/`DIVU` — 32-bit results **sign-extended to 64 bits** when written to HI/LO
- `MFHI`/`MFLO` — upper 64 bits of destination GPR sign-extended from bit 63
- All 64-bit integer instructions sign-extend result into upper 64-bit lane of 128-bit GPR
- `ERET` — checks ERL bit first (uses ErrorEPC/reg 30), then falls back to EXL/EPC
- FPU (COP1) — **no IEEE 754 denormal support**; all denormal inputs/outputs are flushed to zero
- FPU instruction encoding — `fmt` field (bits\[25:21\] = 0x10) is **not a source register**; `fd`=bits\[10:6\], `fs`=bits\[15:11\], `ft`=bits\[20:16\]
- Unaligned load/store (`LWL`/`LWR`/`SWL`/`SWR`/`LDL`/`LDR`/`SDL`/`SDR`) implemented for little-endian memory layout

---

## Instruction Support

### Core Integer (MIPS III / MIPS64)

| Category | Instructions |
|----------|-------------|
| Arithmetic (32-bit) | `ADD` `ADDU` `SUB` `SUBU` `ADDI` `ADDIU` |
| Arithmetic (64-bit) | `DADD` `DADDU` `DSUB` `DSUBU` `DADDI` `DADDIU` |
| Multiply / Divide (32-bit) | `MULT` `MULTU` `DIV` `DIVU` |
| Multiply / Divide (64-bit) | `DMULT` `DMULTU` `DDIV` `DDIVU` |
| HI/LO Move | `MFHI` `MTHI` `MFLO` `MTLO` |
| Shift (32-bit) | `SLL` `SRL` `SRA` `SLLV` `SRLV` `SRAV` |
| Shift (64-bit) | `DSLL` `DSRL` `DSRA` `DSLLV` `DSRLV` `DSRAV` `DSLL32` `DSRL32` `DSRA32` |
| Logic | `AND` `OR` `NOR` `XOR` `ANDI` `ORI` `XORI` |
| Compare/Set | `SLT` `SLTU` `SLTI` `SLTIU` |
| Conditional Move | `MOVN` `MOVZ` |
| Load Upper | `LUI` |
| Branch | `BEQ` `BNE` `BGEZ` `BGTZ` `BLEZ` `BLTZ` `BEQL` `BNEL` `BGEZL` `BGTZL` `BLEZL` `BLTZL` |
| Branch-and-Link | `BGEZAL` `BLTZAL` `BGEZALL` `BLTZALL` |
| Jump | `J` `JAL` `JR` `JALR` |
| Load | `LB` `LBU` `LH` `LHU` `LW` `LWU` `LWL` `LWR` `LD` `LDL` `LDR` `LQ` `LUI` |
| Store | `SB` `SH` `SW` `SWL` `SWR` `SD` `SDL` `SDR` `SQ` |
| Trap | `SYSCALL` `BREAK` `SYNC` `TEQ` `TNE` `TGE` `TGEU` `TLT` `TLTU` `TEQI` `TNEI` `TGEI` `TGEIU` `TLTI` `TLTIU` |
| Special | `NOP` |
| EE SA register | `MFSA` `MTSA` `MTSAB` `MTSAH` |

### COP0 — System Control

| Instruction | Description |
|-------------|-------------|
| `MFC0` `DMFC0` `CFC0` | Move from COP0 register |
| `MTC0` `DMTC0` `CTC0` | Move to COP0 register |
| `ERET` | Return from exception (checks ERL/EXL, uses ErrorEPC/EPC) |
| `TLBR` | Read indexed TLB entry |
| `TLBWI` | Write indexed TLB entry |
| `TLBWR` | Write random TLB entry |
| `TLBP` | Probe TLB for matching entry |
| `CACHE` | Cache operation |
| `EI` `DI` | Enable / Disable interrupts (EE extension) |

### COP1 — Floating-Point Unit (FPU)

> All FPU operations use **single precision (32-bit)** only. Denormals are flushed to zero (PS2 EE behaviour, differs from IEEE 754 default).

| Instruction | Description |
|-------------|-------------|
| `ADD.S` `SUB.S` `MUL.S` `DIV.S` | Basic arithmetic |
| `SQRT.S` `RSQRT.S` | Square root, reciprocal square root |
| `ABS.S` `NEG.S` `MOV.S` | Unary operations |
| `MADD.S` `MSUB.S` `MADDA.S` `MSUBA.S` | Multiply-accumulate |
| `ADDA.S` `SUBA.S` `MULA.S` | Accumulate variants |
| `MAX.S` `MIN.S` | Min/max |
| `CVT.W.S` `CVT.S.W` | Float/integer conversion |
| `CEIL.W.S` `FLOOR.W.S` `ROUND.W.S` `TRUNC.W.S` | Rounding |
| `C.F.S` `C.EQ.S` `C.LT.S` `C.LE.S` | Comparisons (PS2 EE functs 0x30/0x32/0x34/0x36) |
| `BC1F` `BC1T` `BC1FL` `BC1TL` | Branch on FPU condition code |
| `MFC1` `DMFC1` `CFC1` `MTC1` `DMTC1` `CTC1` | Register moves |
| `LWC1` `SWC1` `LDC1` `SDC1` | FPU load/store |

### COP2 / VU0 Macro-Mode

> 128-bit vector operations. Each register holds 4 × 32-bit float components (x, y, z, w).

| Instruction | Description |
|-------------|-------------|
| `VADD` `VSUB` `VMUL` | Vector add/sub/mul |
| `VMADD` `VMSUB` | Vector multiply-accumulate |
| `VADDA` `VSUBA` `VMULA` | Vector accumulate |
| `VABS` | Vector absolute value |
| `VMAX` `VMIN` | Vector max/min |
| `VDIV` `VSQRT` `VRSQRT` | Scalar division/sqrt |
| `VFTOI0` `VFTOI4` | Float to integer conversion |
| `VITOF0` `VITOF4` | Integer to float conversion |
| `VMOVE` `VMR32` | Vector register move |
| `VOPMULA` `VOPMSUB` | Outer product multiply |
| `VCLIPW` | Clip test |
| `VNOP` | No operation |
| `VLQ` `VSQ` | Quadword load/store (128-bit) |
| `VCALLMS` `VCALLMSR` | Call VU0 microsubroutine |
| `QMFC2` `QMTC2` | Quadword moves between EE and VU0 |
| `CFC2` `CTC2` | COP2 control register moves |
| `VRNEXT` `VRGET` `VRINIT` `VRXOR` | VU0 LFSR random number generator |
| VADDBc, VSUBBc, VMULBc, ... | Per-component broadcast variants (x/y/z/w) for VADD/VSUB/VMUL/VMADD/VMSUB/VADDA/VSUBA/VMULA |

### MMI — Multimedia Instructions (PS2 EE Extension)

| Category | Instructions |
|----------|-------------|
| Multiply-Accumulate | `MADD` `MADDU` `MADD1` `MADDU1` `MULT1` `MULTU1` `DIV1` `DIVU1` |
| HI1/LO1 Pipeline | `MFHI1` `MTHI1` `MFLO1` `MTLO1` |
| Halfword/Word Operations | `PMFHI` `PMTHI` `PMFLO` `PMTLO` `PMFHL` `PMTHL` |
| SIMD Integer Arithmetic | `PADDW` `PADDH` `PADDB` `PSUBW` `PSUBH` `PSUBB` |
| SIMD Unsigned Saturate | `PADDUW` `PADDUH` `PADDUB` `PSUBUW` `PSUBUH` `PSUBUB` |
| SIMD Signed Saturate | `PADDSW` `PADDSH` `PADDSB` `PSUBSW` `PSUBSH` `PSUBSB` |
| SIMD Compare | `PCEQW` `PCEQH` `PCEQB` `PCGTW` `PCGTH` `PCGTB` |
| SIMD Min/Max | `PMAXW` `PMINW` `PMAXH` `PMINH` |
| SIMD Shift | `PSLLW` `PSRLW` `PSRAW` `PSLLH` `PSRLH` `PSRAH` `PSLLVW` `PSRLVW` `PSRAVW` |
| SIMD Multiply | `PMULTW` `PMULTUW` `PMULTH` `PMADDW` `PMADDUW` `PMADDH` `PMSUBW` `PMSUBH` |
| SIMD Divide | `PDIVW` `PDIVUW` `PDIVBW` |
| Bitwise | `PAND` `POR` `PNOR` `PXOR` |
| Pack/Extend | `PEXTLW` `PEXTUW` `PEXTLH` `PEXTUH` `PEXTLB` `PEXTUB` |
| Pack | `PPACW` `PPACH` `PPACB` `PPAC5` `PEXT5` |
| Shuffle/Rearrange | `PEXCH` `PEXCW` `PEXEH` `PEXEW` `PCPYH` `PCPYLD` `PCPYUD` `PREVH` `PROT3W` `PINTEH` `PINTH` |
| Absolute Value | `PABSW` `PABSH` |
| Halfword Multiply | `PHMADH` `PHMSBH` |
| Signed-to-Byte | `PADSBH` |
| Misc | `PLZCW` `QFSRV` |

---

## PS2 EE vs Standard MIPS — Key Differences

| Aspect | Standard MIPS64 | PS2 EE |
|--------|-----------------|--------|
| GPR width | 64 bits | 128 bits (lower 64 used for int) |
| `MULT`/`DIV` HI/LO | 64-bit raw | **32-bit result sign-extended to 64 bits** |
| FPU compare functs | C.LT=0x3C, C.LE=0x3E | C.LT=0x34, C.LE=0x36 |
| FPU denormals | IEEE 754 (supported) | **Flushed to zero** |
| FPU register encoding | fd=bits\[10:6\], fs=bits\[15:11\] | Same — **args.Rs is format (0x10), not a register** |
| `ERET` | Clears EXL | **Checks ERL first, uses ErrorEPC (reg 30) if set** |
| Shift 64-bit RegsHi | N/A | **Sign-extended from result bit 63** |
| VU0 | Not present | COP2 macro-mode + micro-mode |
| MMI instructions | Not present | Full SIMD multimedia extension |

---

## Implementation Notes

### Verified Against PCSX2
All instruction semantics have been cross-referenced with [PCSX2](https://pcsx2.net/) source code, particularly:
- `pcsx2/R5900OpcodeImpl.cpp` — EE core instruction execution
- `pcsx2/R5900OpcodeTables.cpp` — EE opcode tables and encoding
- `pcsx2/VUops.cpp` — VU0/VU1 vector operations
- `pcsx2/FPU.cpp` — COP1 floating-point unit

### Test Coverage
- **511 unit tests** covering core, FPU, COP0, COP2/VU0, MMI, EE hardware, IOP, CDVD, SIO2, and IOP module loader
- Tests verify both disassembly output and execution correctness
- Special cases: long.MinValue in DMULT, negative DIV sign-extension, ERET ERL handling

---

## Documentation

| File | Purpose |
|------|---------|
| `README.md` (this file) | Project overview and instruction reference |
| `QUICKSTART.md` | Step-by-step guide to using the assembler/disassembler |
| `ASSEMBLER_DISASSEMBLER_INTEGRATION.md` | Technical details of the assembler and disassembler |
| `SOCKET_COMMUNICATION.md` | VSCode extension ↔ C# server protocol |
| `ROADMAP.md` | Development roadmap tracking completed and upcoming phases |
| `CodeDesigner.Mips64LE.Common/Documentation/` | Architecture deep-dives (EE, COP0, COP1, VU0, VU1) |

---

## References

- Sony PlayStation 2 EE Core User's Manual
- MIPS64 Architecture Specification (MIPS Technologies)
- [PCSX2 Emulator Source Code](https://github.com/PCSX2/pcsx2)
- PS2 Linux Developer Tools Documentation
- PS2 Reverse Engineering Community Resources

---

## Disclaimer

This project is for **educational and research purposes** only.  
PlayStation, Emotion Engine, and related trademarks are the property of **Sony Interactive Entertainment**.  
This project is not affiliated with or endorsed by Sony Interactive Entertainment.
