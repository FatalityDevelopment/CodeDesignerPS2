# Emotion Engine VU1 (Vector Unit 1)

## Overview

**VU1 (Vector Unit 1)** is the second vector processor in the
PlayStation 2 Emotion Engine.  
Unlike VU0, **VU1 operates only in Micro Mode** and is tightly coupled
to the **Graphics Synthesizer (GS)** pipeline.

VU1 is primarily responsible for:
- Geometry transformation
- Lighting calculations
- Vertex skinning
- Feeding transformed vertices to the GS via `XGKICK`

VU1 executes **microprograms** uploaded by VIF1 (`MPG` packets) and runs fully
independently once started by VIF1 `MSCAL`/`MSCNT`.

---

## VU1 Architecture

- **Vector Registers:** 32 × 128-bit (`vf0–vf31`); each holds 4 × 32-bit floats (`x, y, z, w`)
- **Integer Registers:** 16 × 16-bit (`vi0–vi15`); `vi0` is always 0
- **Accumulator:** 1 × 128-bit (`ACC`)
- **Q Register:** scalar result of `DIV`/`SQRT`/`RSQRT`
- **P Register:** scalar result of EFU operations (ESQRT, ERSQRT, etc.)
- **R Register (RNG):** 23-bit LFSR random number register, stored as `0x3F800000 | mantissa`
- **Micro Memory:** 16 KB (VU1 microprogram storage; VIF1 writes via `MPG`)
- **Data Memory:** 16 KB (VIF1 writes via UNPACK; read/written by microcode via `LQ`/`SQ`)
- **Instruction Width:** 64-bit dual-issue (lower word = memory/integer/branch, upper word = float arithmetic)
- **Execution Model:** SIMD; per-component destination masks and broadcast variants
- Reference: PCSX2 `pcsx2/VUops.cpp`

---

## Program Execution Model

1. VIF1 uploads microprogram to VU1 micro memory via `MPG` packets (EE DMAC CH1 feeds VIF1)
2. VIF1 `MSCAL` or `MSCNT` starts execution at a given micro-memory address
3. VU1 executes independently (synchronous in this emulator)
4. VU1 outputs transformed vertex data to the GS via `XGKICK` → GIF PATH1
5. Program halts at `E` (end-of-microprogram) bit

---

## Upper-Word Instructions (Float Arithmetic)

### Vector Arithmetic

| Instruction | Description |
|-------------|-------------|
| `ADD[bc]` | Vector add (broadcast variants: `ADDx`, `ADDy`, `ADDz`, `ADDw`) |
| `SUB[bc]` | Vector subtract (broadcast variants) |
| `MUL[bc]` | Vector multiply (broadcast variants) |
| `MADD[bc]` | Multiply-add using ACC (broadcast variants) |
| `MSUB[bc]` | Multiply-subtract using ACC (broadcast variants) |
| `MAX[bc]` | Component-wise maximum (broadcast variants) |
| `MINI[bc]` | Component-wise minimum (broadcast variants) |
| `OPMULA` | Outer product multiply to ACC |
| `OPMSUB` | Outer product multiply-subtract from ACC |
| `ABS` | Absolute value |
| `CLIP` | Clip test against ±w; updates clip flags |

### Q-Register Variants

| Instruction | Description |
|-------------|-------------|
| `ADDq` | Add Q to each component |
| `SUBq` | Subtract Q from each component |
| `MULq` | Multiply each component by Q |
| `MADDq` | Multiply-add using Q |
| `MSUBq` | Multiply-subtract using Q |

### I-Register (Immediate) Variants

| Instruction | Description |
|-------------|-------------|
| `ADDi` | Add immediate (I register) |
| `SUBi` | Subtract immediate |
| `MULi` | Multiply by immediate |
| `MADDi` | Multiply-add using I register |
| `MSUBi` | Multiply-subtract using I register |

### Accumulator

| Instruction | Description |
|-------------|-------------|
| `ADDA[bc]` | Add to accumulator (broadcast variants) |
| `SUBA[bc]` | Subtract from accumulator (broadcast variants) |
| `MULA[bc]` | Multiply to accumulator (broadcast variants) |
| `MADDA[bc]` | Multiply-add to accumulator (broadcast variants) |
| `MSUBA[bc]` | Multiply-subtract to accumulator (broadcast variants) |
| `MAXA[bc]` | Max into accumulator |
| `MINIA[bc]` | Min into accumulator |

### Conversion

| Instruction | Description |
|-------------|-------------|
| `FTOI0` | Float → integer (×1.0) |
| `FTOI4` | Float → integer (×16.0) |
| `FTOI12` | Float → integer (×4096.0) |
| `FTOI15` | Float → integer (×32768.0) |
| `ITOF0` | Integer → float (÷1.0) |
| `ITOF4` | Integer → float (÷16.0) |
| `ITOF12` | Integer → float (÷4096.0) |
| `ITOF15` | Integer → float (÷32768.0) |

### Special Upper-Word

| Instruction | Description |
|-------------|-------------|
| `LOI` | Load immediate float into I register (from lower word) |
| `NOP` (upper) | No operation |
| `E` bit | End-of-microprogram flag (bit 30 of upper word) |

---

## Lower-Word Instructions (Memory / Integer / Branch)

### Load and Store

| Instruction | Description |
|-------------|-------------|
| `LQ` | Load quadword from data memory |
| `SQ` | Store quadword to data memory |
| `ILW` | Load integer from data memory (one 16-bit component) |
| `ISW` | Store integer to data memory (one 16-bit component) |

### Integer Arithmetic

| Instruction | Description |
|-------------|-------------|
| `IADD` | Integer add |
| `ISUB` | Integer subtract |
| `IADDI` | Integer add immediate |
| `IAND` | Integer AND |
| `IOR` | Integer OR |

### Integer Move

| Instruction | Description |
|-------------|-------------|
| `MOVE` | Copy vector register (float) |
| `MR32` | Rotate components: x←w, y←x, z←y, w←z |

### Branches

| Instruction | Description |
|-------------|-------------|
| `B` | Unconditional branch |
| `BAL` | Branch and link (save PC to vi register) |
| `IBEQ` | Branch if vi_is == vi_it |
| `IBNE` | Branch if vi_is != vi_it |
| `IBLTZ` | Branch if vi_is < 0 |
| `IBGTZ` | Branch if vi_is > 0 |
| `IBLEZ` | Branch if vi_is ≤ 0 |
| `IBGEZ` | Branch if vi_is ≥ 0 |
| `JR` | Jump to address in vi register |
| `JALR` | Jump and link to address in vi register |

### Flag Instructions

| Instruction | Description |
|-------------|-------------|
| `FCEQ` | Read clip flag = immediate, store 0 or 1 in vi_id |
| `FCAND` | vi_id = 1 if ClipFlag & imm != 0 |
| `FCOR` | vi_id = 1 if (ClipFlag | imm) == 0xFFFFFF |
| `FCSET` | Set clip flag bits to immediate |
| `FSEQ` | Status flag == immediate |
| `FSAND` | Status flag & immediate |
| `FSOR` | Status flag | immediate |
| `FSSET` | Set status flag bits |
| `FMEQ` | MAC flag == immediate |
| `FMAND` | MAC flag & immediate |
| `FMOR` | MAC flag | immediate |

### Q-Register (Scalar Division)

| Instruction | Description |
|-------------|-------------|
| `DIV` | `Q = fs[field] / ft[field]` |
| `SQRT` | `Q = sqrt(ft[field])` |
| `RSQRT` | `Q = fs[field] / sqrt(ft[field])` |
| `WAITQ` | Stall until Q is ready (no-op in synchronous emulation) |

### P-Register (EFU — Elementary Function Unit)

| Instruction | Description |
|-------------|-------------|
| `ESQRT` | `P = sqrt(fs.x)` |
| `ERSQRT` | `P = 1 / sqrt(fs.x)` |
| `ERCPR` | `P = 1 / fs.x` (reciprocal) |
| `ESUM` | `P = fs.x + fs.y + fs.z + fs.w` |
| `EACOS` | `P = acos(fs.x)` |
| `EATAN` | `P = atan(fs.x)` |
| `ELENG` | `P = sqrt(fs.x² + fs.y² + fs.z²)` |
| `ESADD` | `P = fs.x² + fs.y² + fs.z²` |
| `ERSADD` | `P = 1 / (fs.x² + fs.y² + fs.z²)` |
| `ERLENG` | `P = 1 / sqrt(fs.x² + fs.y² + fs.z²)` |
| `WAITP` | Stall until P is ready (no-op in synchronous emulation) |

### Random Number Generator (LFSR)

| Instruction | Description |
|-------------|-------------|
| `RNEXT` | Advance LFSR, write result to vfd component |
| `RGET` | Read current LFSR to vfd component (no advance) |
| `RINIT` | Initialise LFSR from fs component |
| `RXOR` | XOR LFSR with fs component |

### GIF Output

| Instruction | Description |
|-------------|-------------|
| `XGKICK` | Send data memory block (from vi_is address) to GIF PATH1 |

---

## Component Masks and Broadcast

VU1 instructions support per-component destination masks and source broadcast:

- **Destination mask:** `.x`, `.y`, `.z`, `.w`, `.xy`, `.xyz`, `.xyzw`, etc.
- **Broadcast:** `ADDx vf1, vf2, vf3` adds `vf3.x` to each selected component of `vf2`

```asm
MUL.xyz   vf4, vf1, vf2        # multiply only x/y/z components
ADDx.xyzw vf5, vf6, vf7       # add vf7.x (broadcast) to all of vf6
XGKICK    vi1                  # kick vertex buffer at vi1 to GIF
```

---

## Disclaimer

This documentation is for **educational and research purposes only**.  
PlayStation and Emotion Engine are trademarks of Sony Interactive Entertainment.
