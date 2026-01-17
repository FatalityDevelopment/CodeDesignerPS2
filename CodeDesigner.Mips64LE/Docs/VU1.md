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
- Feeding transformed vertices to the GS

VU1 executes **microprograms** uploaded by the EE CPU and runs fully
independently once started.

---

## VU1 Architecture

- **Vector Registers:** 32 × 128-bit (`vf0–vf31`)
- **Integer Registers:** 16 × 16-bit (`vi0–vi15`)
- **Accumulator:** 1 × 128-bit (`ACC`)
- **Micro Memory:** 16 KB
- **Data Memory:** 16 KB
- **Instruction Width:** 32-bit
- **Pipeline:** Vector + Integer (dual-issue)
- **Execution Model:** SIMD

Each vector register holds **4 × 32-bit floats** (`x, y, z, w`).

---

## Program Execution Model

1. EE uploads microprogram to VU1 micro memory
2. EE sets VU1 start address
3. VU1 executes independently
4. VU1 outputs data via DMA to GS
5. Program halts or loops

---

## VU1 Vector Arithmetic Instructions

| Instruction | Description |
|-------------|-------------|
| ADD | Vector add |
| SUB | Vector subtract |
| MUL | Vector multiply |
| MAD | Multiply and add |
| MADD | Multiply add (ACC) |
| MSUB | Multiply subtract |
| MAX | Vector maximum |
| MIN | Vector minimum |
| ABS | Absolute value |
| NEG | Negate |
| CLIP | Clipping test |

---

## VU1 Accumulator Instructions

| Instruction | Description |
|-------------|-------------|
| ADDA | Add to accumulator |
| MULA | Multiply to accumulator |
| MADD | Multiply add using ACC |
| MSUB | Multiply subtract using ACC |

---

## VU1 Reciprocal & Square Root

| Instruction | Description |
|-------------|-------------|
| DIV | Vector divide |
| RCP | Reciprocal |
| RSQRT | Reciprocal square root |
| SQRT | Square root |

---

## VU1 Conversion Instructions

| Instruction | Description |
|-------------|-------------|
| FTOI | Float to integer |
| ITOF | Integer to float |
| ITOR | Integer to reciprocal |
| RTOI | Reciprocal to integer |

---

## VU1 Integer Instructions

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
| IBLTZAL | Branch and link if less than zero |
| IBGTZAL | Branch and link if greater than zero |

---

## VU1 Load & Store Instructions

| Instruction | Description |
|-------------|-------------|
| LQ | Load quadword |
| SQ | Store quadword |
| ILW | Load integer word |
| ISW | Store integer word |

---

## VU1 Control & Flow Instructions

| Instruction | Description |
|-------------|-------------|
| MOVE | Register move |
| MR32 | Rotate vector components |
| WAITQ | Wait for division result |
| SYNC | Pipeline synchronization |
| NOP | No operation |
| END | End microprogram |

---

## Component Masks

VU1 instructions support per-component execution:

- `.x`, `.y`, `.z`, `.w`
- Combined masks: `.xy`, `.xyz`, `.xyzw`

Example:
```asm
MUL.xyz vf4, vf1, vf2
```