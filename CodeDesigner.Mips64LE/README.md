# MIPS64 – PlayStation 2 Emotion Engine

## Overview

The **Emotion Engine (EE)** is the primary CPU of the **Sony PlayStation 2**, designed jointly by **Sony** and **Toshiba**.  
It is a **custom 64-bit MIPS-based processor** engineered to deliver extremely high throughput through **parallelism**, **vector processing**, and **DMA-driven data flow**.

Rather than relying on a single powerful scalar core, the EE combines:
- A **superscalar MIPS64 CPU**
- Two **high-performance Vector Units (VU0 and VU1)**
- A **bandwidth-focused memory and DMA architecture**

This repository/project focuses on **understanding, documenting, and working with the MIPS64 architecture as implemented by the PlayStation 2 Emotion Engine**, including its many non-standard extensions.

---

## Emotion Engine Architecture

### Core CPU

- **Architecture:** MIPS III / MIPS64 (custom Sony extensions)
- **Word Size:** 64-bit
- **Clock Speed:** ~294.912 MHz
- **Endianness:** Little-endian
- **Pipeline:** Superscalar, dual-issue
- **Execution:** In-order with instruction pairing rules

#### Registers
- **32 × 128-bit General Purpose Registers (GPRs)** — integer operations use the lower 64-bit lane; the upper 64 bits are sign-extended from bit 63
- **HI / LO registers** for multiply/divide results
- **Program Counter (PC)**
- **Special-purpose system registers** accessed via COP0

The EE core supports both **32-bit and 64-bit integer operations**, allowing legacy MIPS code and new 64-bit code to coexist.

---

## Vector Units (Key Feature)

The Emotion Engine’s defining feature is its **two programmable vector coprocessors**, which offload most math-heavy workloads.

### VU0 (Vector Unit 0)

- Dual-mode operation:
  - **Macro Mode** – Acts as a COP2-style coprocessor controlled by the EE CPU
  - **Micro Mode** – Executes its own microprograms independently
- Typically used for:
  - Physics calculations
  - Animation blending
  - Collision detection
  - Assisting the CPU with SIMD math

### VU1 (Vector Unit 1)

- Fully independent processor
- Executes **microcode only** (no macro mode)
- Optimized for:
  - Geometry transformations
  - Lighting calculations
  - Vertex skinning
  - Feeding data to the Graphics Synthesizer (GS)
- Closely coupled to the **graphics pipeline**

### Vector Capabilities

- **32 × 128-bit vector registers per VU**
- Each register contains **4 × 32-bit floating-point components**
- SIMD-style execution with per-component masking
- Dedicated accumulator register (`ACC`)

---

## Floating Point & Coprocessors

### COP1 – Floating Point Unit (FPU)
- **Single-precision only** (PS2 EE does not have double-precision FPU)
- **Denormals flushed to zero** (not IEEE 754 compliant for denormal inputs/outputs)
- Mostly used for:
  - Game logic
  - Non-vectorized math
  - Compiler-generated floating-point code

### COP0 – System Control
- Handles:
  - Exceptions and interrupts
  - Privilege levels
  - Timers and counters
  - Cache and TLB management

---

## Memory System

- **Main Memory:** 32 MB RDRAM
- **Memory Bandwidth:** Extremely high for its time
- **Scratchpad RAM:** 16 KB on-chip, low latency
- **Cache:**
  - Instruction Cache
  - Data Cache
- **DMA Controllers:**
  - Multiple DMA channels
  - Core to EE ↔ VUs ↔ GS data flow
  - Essential for performance

The PS2 relies heavily on **DMA-driven parallelism**, allowing the CPU, VUs, and GS to operate concurrently.

---

## Instruction Set Notes

The Emotion Engine extends standard MIPS with:

- Full **64-bit integer arithmetic**
- **128-bit quadword load/store instructions** (`LQ`, `SQ`)
- Multimedia-style operations
- Tight integration with vector and DMA subsystems
- Non-standard behavior in certain corner cases

These extensions make the EE **not fully compatible with desktop MIPS64 CPUs**, requiring **custom handling in emulators, assemblers, and compilers**.

---

## Execution Model

- Scalar CPU, VU0, VU1, and GS run **in parallel**
- Explicit synchronization via:
  - DMA completion
  - Pipeline waits
  - Cache control instructions
- Performance depends heavily on:
  - Instruction scheduling
  - Memory alignment
  - Data locality

---

## Common Use Cases

This project or documentation is useful for:

- 🎮 **PlayStation 2 emulation**
- 🧪 **Low-level PS2 homebrew development**
- 🛠 **Writing assemblers or compilers targeting the EE**
- 🔍 **Reverse engineering PS2 executables**
- 📐 **Studying SIMD, parallelism, and heterogeneous CPU design**

---

## Challenges

- Non-standard MIPS extensions
- Complex instruction pairing rules
- Heavy reliance on DMA timing
- Tight synchronization between:
  - EE Core
  - VU0
  - VU1
  - Graphics Synthesizer
- Performance-sensitive code paths with minimal margin for error

---

## References

- Sony PlayStation 2 Technical Documentation  
- MIPS64 Architecture Manuals  
- PS2 Linux and Homebrew Documentation  
- PS2 Reverse Engineering & Emulation Communities  

---

## Disclaimer

This project is for **educational and research purposes only**.  
PlayStation, Emotion Engine, and related trademarks are the property of  
**Sony Interactive Entertainment**.
