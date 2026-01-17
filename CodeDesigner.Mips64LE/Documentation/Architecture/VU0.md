# Emotion Engine VU0 (Vector Unit 0)

## Overview

**VU0 (Vector Unit 0)** is a programmable SIMD coprocessor inside the
PlayStation 2 Emotion Engine.  
It is unique in that it can operate in **two distinct modes**:

- **Macro Mode** – Acts as a COP2-style coprocessor controlled by the EE CPU
- **Micro Mode** – Executes independent microprograms, similar to VU1

VU0 is typically used for:
- Physics calculations
- Animation blending
- Matrix and vector math
- Assisting the main CPU with math-heavy workloads

---

## VU0 Architecture

- **Vector Registers:** 32 × 128-bit (`vf0–vf31`)
- **Integer Registers:** 16 × 16-bit (`vi0–vi15`)
- **Accumulator:** 1 × 128-bit (`ACC`)
- **Pipeline:** Vector + Integer pipelines
- **Instruction Width:** 32-bit
- **Execution Model:** SIMD

Each vector register contains **4 × 32-bit floats** (`x, y, z, w`).

---

## VU0 Modes

### Macro Mode
- Controlled directly by EE CPU instructions
- Limited instruction set
- Used via `COP2` instructions

### Micro Mode
- Executes VU microprograms
- Independent instruction stream
- Full VU instruction set available

---

## VU0 Vector Arithmetic Instructions

| Instruction | Description |
|-------------|-------------|
| ADD | Vector add |
| SUB | Vector subtract |
| MUL | Vector multiply |
| MAD | Multiply and add |
| MADD | Multiply add (accumulator) |
| MSUB | Multiply subtract |
| MAX | Vector max |
| MIN | Vector min |
| ABS | Absolute value |
| NEG | Negate |
| CLIP | Clipping test |

---

## VU0 Accumulator Instructions

| Instruction | Description |
|-------------|-------------|
| ADDA | Add to accumulator |
| MULA | Multiply to accumulator |
| MADD | Multiply add using ACC |
| MSUB | Multiply subtract using ACC |

---

## VU0 Conversion Instructions

| Instruction | Description |
|-------------|-------------|
| FTOI | Float to integer |
| ITOF | Integer to float |
| ITOR | Integer to reciprocal |
| RTOI | Reciprocal to integer |

---

## VU0 Reciprocal & Square Root

| Instruction | Description |
|-------------|-------------|
| DIV | Vector divide |
| RSQRT | Reciprocal square root |
| SQRT | Square root |
| RCP | Reciprocal |

---

## VU0 Integer Instructions

| Instruction | Description |
|-------------|-------------|
| IADD | Integer add |
| ISUB | Integer subtract |
| IADDI | Integer add immediate |
| IAND | Integer AND |
| IOR | Integer OR |
| IBEQ | Branch if equal |
| IBNE | Branch if not equal |
| IBLTZ | Branch if less than zero |
| IBGTZ | Branch if greater than zero |

---

## VU0 Load & Store Instructions

| Instruction | Description |
|-------------|-------------|
| LQ | Load quadword |
| SQ | Store quadword |
| ILW | Load integer word |
| ISW | Store integer word |

---

## VU0 Control Instructions

| Instruction | Description |
|-------------|-------------|
| MOVE | Register move |
| MR32 | Rotate vector components |
| WAITQ | Wait for division result |
| SYNC | Pipeline synchronization |
| ERET | Return from microprogram |

---

## Component Masks

VU instructions can selectively operate on vector components:

- `.x`, `.y`, `.z`, `.w`
- Example:
  ```asm
  MUL.xyz vf1, vf2, vf3
```