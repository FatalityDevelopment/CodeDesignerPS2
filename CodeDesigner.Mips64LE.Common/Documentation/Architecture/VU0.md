# Emotion Engine VU0 (Vector Unit 0)

## Overview

**VU0 (Vector Unit 0)** is a programmable SIMD coprocessor inside the
PlayStation 2 Emotion Engine.  
It is unique in that it can operate in **two distinct modes**:

- **Macro Mode** – Acts as a COP2-style coprocessor controlled by the EE CPU via COP2 instructions
- **Micro Mode** – Executes independent microprograms launched by `VCALLMS`/`VCALLMSR` or by VIF0 `MSCAL`/`MSCNT`

VU0 is typically used for:
- Physics calculations
- Animation blending
- Matrix and vector math
- Assisting the main CPU with math-heavy workloads

---

## VU0 Architecture

- **Vector Registers:** 32 × 128-bit (`vf0–vf31`); each holds 4 × 32-bit floats (`x, y, z, w`)
- **Integer Registers:** 16 × 16-bit (`vi0–vi15`); `vi0` is always 0
- **Accumulator:** 1 × 128-bit (`ACC`)
- **Q Register:** scalar result of `DIV`/`SQRT`/`RSQRT`
- **P Register:** scalar result of EFU operations (`ESQRT`, `ERSQRT`, etc.)
- **R Register (RNG):** 23-bit LFSR random number register, stored as `0x3F800000 | mantissa`
- **Micro Memory:** 4 KB (VU0 microprogram storage)
- **Data Memory:** 4 KB (shared with EE via COP2 `VLQ`/`VSQ`)
- **Instruction Width:** 64-bit dual-issue (lower word = memory/integer/branch, upper word = float arithmetic)
- **Execution Model:** SIMD; per-component destination masks and broadcast variants

---

## VU0 Modes

### Macro Mode
- Controlled directly by EE CPU via COP2 (`rs=0x10`) instructions
- Accessed as `VADD`, `VSUB`, `VMUL`, `VMADD`, etc. in EE assembly
- `VWAITQ` is a no-op in macro mode (Q is always ready)
- No `XGKICK` (VU1-only feature)

### Micro Mode
- Launched by `VCALLMS`/`VCALLMSR` EE instructions or by VIF0 `MSCAL`/`MSCNT`
- Executes synchronously: EE pipeline waits until the microprogram completes
- Full VU instruction set available (same encoding as VU1)
- Reference: PCSX2 `pcsx2/VU0.cpp`

---

## Macro-Mode Instructions (COP2 / EE Assembly)

### Vector Arithmetic

| Instruction | Description |
|-------------|-------------|
| `VADD[bc]` | Vector add (scalar broadcast: `VADDx`, `VADDy`, `VADDz`, `VADDw`) |
| `VSUB[bc]` | Vector subtract (broadcast variants) |
| `VMUL[bc]` | Vector multiply (broadcast variants) |
| `VMADD[bc]` | Multiply-add using ACC (broadcast variants) |
| `VMSUB[bc]` | Multiply-subtract using ACC (broadcast variants) |
| `VMAX[bc]` | Component-wise maximum (broadcast variants) |
| `VMIN[bc]` | Component-wise minimum (broadcast variants) |
| `VABS` | Absolute value |
| `VOPMULA` | Outer product multiply to ACC |
| `VOPMSUB` | Outer product multiply-subtract from ACC |
| `VCLIPW` | Clip test against ±w |

### Accumulator

| Instruction | Description |
|-------------|-------------|
| `VADDA[bc]` | Add to accumulator (broadcast variants) |
| `VSUBA[bc]` | Subtract from accumulator (broadcast variants) |
| `VMULA[bc]` | Multiply to accumulator (broadcast variants) |

### Conversion

| Instruction | Description |
|-------------|-------------|
| `VFTOI0` | Float to signed integer (multiply by 1.0) |
| `VFTOI4` | Float to signed integer (multiply by 16.0) |
| `VITOF0` | Signed integer to float (divide by 1.0) |
| `VITOF4` | Signed integer to float (divide by 16.0) |

### Q-Register (Scalar Division)

| Instruction | Description |
|-------------|-------------|
| `VDIV` | `Q = fs[field] / ft[field]` |
| `VSQRT` | `Q = sqrt(ft[field])` |
| `VRSQRT` | `Q = fs[field] / sqrt(ft[field])` |
| `VWAITQ` | Wait for Q to be ready (no-op in macro mode) |

### Register Transfer

| Instruction | Description |
|-------------|-------------|
| `VMOVE` | Copy vector register |
| `VMR32` | Rotate components: x←w, y←x, z←y, w←z |
| `VLQ` | Load quadword (128-bit) from memory |
| `VSQ` | Store quadword (128-bit) to memory |
| `QMFC2` | Move 128-bit VU0 register to EE GPR pair |
| `QMTC2` | Move EE GPR pair to 128-bit VU0 register |
| `CFC2` | Move VU0 control register to EE GPR |
| `CTC2` | Move EE GPR to VU0 control register |

### Random Number Generator (LFSR)

| Instruction | Description |
|-------------|-------------|
| `VRNEXT` | Advance LFSR, write result to vfd |
| `VRGET` | Read current LFSR value to vfd |
| `VRINIT` | Initialise LFSR from fs[field] |
| `VRXOR` | XOR LFSR with fs[field] |

### Micro Mode Control

| Instruction | Description |
|-------------|-------------|
| `VCALLMS` | Start VU0 microprogram at immediate address, then execute synchronously |
| `VCALLMSR` | Start VU0 microprogram at address in `vi15`, then execute synchronously |
| `VNOP` | No operation |

---

## Micro-Mode Instructions

All VU1 micro-mode instructions are available in VU0 micro mode with the following exception:

- **`XGKICK`** is a no-op in VU0 (VU1-only feature — feeds data to GIF PATH1)

Upper-word and lower-word instruction sets are identical to VU1; see the VU1 documentation for the full micro-mode instruction reference.

---

## Component Masks and Broadcast

VU instructions support per-component destination masks and source broadcast:

- **Destination mask:** `.x`, `.y`, `.z`, `.w`, `.xy`, `.xyz`, `.xyzw`, etc.
- **Broadcast:** `VADDx vf1, vf2, vf3` adds `vf3.x` to each component of `vf2`

```asm
VMUL.xyz  vf1, vf2, vf3        # multiply only x/y/z components
VADDx.xyzw vf4, vf5, vf6      # add vf6.x (broadcast) to all of vf5
```

---

## Disclaimer

This documentation is for **educational and research purposes only**.  
PlayStation and Emotion Engine are trademarks of Sony Interactive Entertainment.