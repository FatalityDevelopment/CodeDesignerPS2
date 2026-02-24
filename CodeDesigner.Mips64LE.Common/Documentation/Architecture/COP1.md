# Emotion Engine COP1 (Floating Point Unit)

## Overview

**COP1** is the **Floating Point Unit (FPU)** of the PlayStation 2 Emotion Engine.  
It is responsible for IEEE-754 floating-point arithmetic and supports both
single-precision and double-precision operations.

While the PS2 relies heavily on its **Vector Units (VU0/VU1)** for performance-critical math,
COP1 is still used for:
- Game logic
- Non-vectorized math
- Legacy or compiler-generated floating-point code

---

## COP1 Register File

### Floating-Point Registers

- **32 Floating-Point Registers (FPRs)**
- Each register is **32-bit**
- Can be paired for **64-bit double-precision** operations

| Register Range | Usage |
|----------------|------|
| `f0 – f31` | Single-precision floats |
| Paired (`f0/f1`, `f2/f3`, …) | Double-precision floats |

---

### FPU Control & Status Register (FCSR)

| Field | Description |
|------|-------------|
| FCC | Floating-point condition codes |
| RM | Rounding mode |
| Flags | Exception flags |
| Enables | Exception enables |

---

## COP1 Data Transfer Instructions

| Instruction | Description |
|-------------|-------------|
| MFC1 | Move from FPU register to GPR |
| MTC1 | Move from GPR to FPU register |
| DMFC1 | Move from FPU register to GPR (64-bit) |
| DMTC1 | Move from GPR to FPU register (64-bit) |
| CFC1 | Move control from FPU |
| CTC1 | Move control to FPU |

---

## Floating-Point Arithmetic Instructions

### Single-Precision

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

---

### Double-Precision

| Instruction | Description |
|-------------|-------------|
| ADD.D | Add double-precision |
| SUB.D | Subtract double-precision |
| MUL.D | Multiply double-precision |
| DIV.D | Divide double-precision |
| SQRT.D | Square root |
| ABS.D | Absolute value |
| MOV.D | Move |
| NEG.D | Negate |

---

## Conversion Instructions

| Instruction | Description |
|-------------|-------------|
| CVT.S.W | Convert word to single |
| CVT.D.W | Convert word to double |
| CVT.S.D | Convert double to single |
| CVT.D.S | Convert single to double |
| CVT.W.S | Convert single to word |
| CVT.W.D | Convert double to word |

---

## Comparison Instructions

### Single-Precision

| Instruction | Description |
|-------------|-------------|
| C.EQ.S | Compare equal |
| C.LT.S | Compare less than |
| C.LE.S | Compare less than or equal |

---

### Double-Precision

| Instruction | Description |
|-------------|-------------|
| C.EQ.D | Compare equal |
| C.LT.D | Compare less than |
| C.LE.D | Compare less than or equal |

Comparison results update the **FPU condition flag**, used by branch instructions.

---

## Floating-Point Branch Instructions

| Instruction | Description |
|-------------|-------------|
| BC1T | Branch if FP condition true |
| BC1F | Branch if FP condition false |

---

## Exception Behavior

COP1 can raise exceptions for:
- Invalid operations
- Divide by zero
- Overflow / underflow
- Inexact results

Exception handling is controlled via **FCSR** and routed through COP0.

---

## Emotion Engine Notes

- COP1 is **in-order** and slower than VU units
- Not commonly used for heavy vector math
- Precision and rounding behavior must be accurate for emulation
- Some games rely on exact IEEE-754 edge cases

---

## Emulation Considerations

- Correct NaN handling is critical
- Paired-register behavior for doubles must be preserved
- FCSR rounding modes affect results
- Timing differences can break physics or animation code

---

## Disclaimer

This documentation is for **educational and research purposes only**.  
PlayStation and Emotion Engine are trademarks of Sony Interactive Entertainment.
