# Emotion Engine COP1 (Floating Point Unit)

## Overview

**COP1** is the **Floating Point Unit (FPU)** of the PlayStation 2 Emotion Engine.

> ⚠️ **PS2 EE COP1 is single-precision (32-bit) only.**  There is no double-precision
> support on the EE FPU.  All FPU registers hold 32-bit IEEE 754 values, and the
> `fmt` field in COP1 instruction encodings is always `0x10` (S) — it is **not** a
> source register.  Register fields: `fd` = bits[10:6], `fs` = bits[15:11], `ft` = bits[20:16].

While the PS2 relies heavily on its **Vector Units (VU0/VU1)** for performance-critical math,
COP1 is still used for:
- Game logic
- Non-vectorized math
- Legacy or compiler-generated floating-point code

---

## PS2 EE FPU Behaviour

| Property | Value |
|----------|-------|
| Precision | Single (32-bit) only |
| Denormals | **Flushed to zero** — not IEEE 754 compliant |
| Compare functs | `C.F`=0x30, `C.EQ`=0x32, `C.LT`=0x34, `C.LE`=0x36 (differ from standard MIPS) |
| `fmt` field | Always `0x10` in instruction words — not a register index |

---

## COP1 Register File

- **32 Floating-Point Registers (FPRs)** — `f0`–`f31`, each 32 bits
- **FPU Accumulator (ACC / FACC)** — 32-bit accumulator for `ADDA.S`, `MADD.S`, etc.
- **FCSR** (Floating-Point Control/Status Register at FCR31) — holds the FCC condition flag and rounding mode

---

## COP1 Data Transfer Instructions

| Instruction | Description |
|-------------|-------------|
| MFC1 | Move from FPU register to GPR |
| MTC1 | Move from GPR to FPU register |
| DMFC1 | Move 64-bit from FPU register to GPR (upper 32 bits zero-extended) |
| DMTC1 | Move 64-bit from GPR to FPU register (lower 32 bits used) |
| CFC1 | Move control from FCSR to GPR |
| CTC1 | Move control from GPR to FCSR |

---

## Floating-Point Arithmetic Instructions

| Instruction | Description |
|-------------|-------------|
| ADD.S | Add single-precision |
| SUB.S | Subtract single-precision |
| MUL.S | Multiply single-precision |
| DIV.S | Divide single-precision |
| SQRT.S | Square root |
| ABS.S | Absolute value |
| MOV.S | Move |
| NEG.S | Negate |
| RSQRT.S | Reciprocal square root |
| ADDA.S | Add to accumulator |
| SUBA.S | Subtract from accumulator |
| MULA.S | Multiply to accumulator |
| MADD.S | Multiply-add using accumulator |
| MSUB.S | Multiply-subtract using accumulator |
| MADDA.S | Multiply-add, result to accumulator |
| MSUBA.S | Multiply-subtract, result to accumulator |
| MAX.S | Maximum of two floats |
| MIN.S | Minimum of two floats |

---

## Conversion Instructions

| Instruction | Description |
|-------------|-------------|
| CVT.S.W | Convert word integer to single-precision |
| CVT.W.S | Convert single-precision to word integer |
| CEIL.W.S | Ceiling towards +∞ |
| FLOOR.W.S | Floor towards −∞ |
| ROUND.W.S | Round to nearest integer |
| TRUNC.W.S | Truncate towards zero |

---

## Comparison Instructions

> PS2 EE compare functs differ from standard MIPS R4000.

| Instruction | Funct | Description |
|-------------|-------|-------------|
| C.F.S | 0x30 | Always false |
| C.EQ.S | 0x32 | Compare equal |
| C.LT.S | 0x34 | Compare less than |
| C.LE.S | 0x36 | Compare less than or equal |

Comparison results set the **FPU condition flag (FCC)** in FCSR, used by branch instructions.

---

## Floating-Point Branch Instructions

| Instruction | Description |
|-------------|-------------|
| BC1F | Branch if FPU condition false |
| BC1T | Branch if FPU condition true |
| BC1FL | Branch if FPU condition false (likely) |
| BC1TL | Branch if FPU condition true (likely) |

---

## FPU Load/Store Instructions

| Instruction | Description |
|-------------|-------------|
| LWC1 | Load word to FPU register |
| SWC1 | Store word from FPU register |
| LDC1 | Load doubleword to FPU register pair (lower 32 bits used) |
| SDC1 | Store doubleword from FPU register pair (upper 32 bits zero) |

---

## Emulation Considerations

- Denormal inputs and outputs must be **flushed to zero** before and after every operation.
- The `fmt` field in COP1 encodings is always `0x10` (single) — never treat it as a register.
- Compare functs 0x34 (`C.LT`) and 0x36 (`C.LE`) differ from standard MIPS 0x3C/0x3E.
- The FPU accumulator (`FACC`) is a persistent register used across MADD/MSUB chains.

---

## Disclaimer

This documentation is for **educational and research purposes only**.
PlayStation and Emotion Engine are trademarks of Sony Interactive Entertainment.
