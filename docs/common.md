[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.Common — Core Library

The `CodeDesigner.Ps2.Common` project is the foundational library providing PS2 MIPS64 R5900 instruction set architecture (ISA) support, including assembler, disassembler, instruction decoder/encoder, and core emulation primitives.

## Overview

This library implements the complete PS2 Emotion Engine (EE) MIPS64 instruction set, verified against PCSX2 reference behavior. It serves as the core for all PS2-related functionality in CodeDesigner.

## Key Components

### Instruction Set Architecture

- **Complete MIPS64 R5900 coverage**: All standard MIPS III instructions plus PS2-specific extensions (MMI, VU0 macro mode, COP1 FPU, etc.)
- **Accurate behavior**: Matches PCSX2's R5900OpcodeImpl.cpp execution semantics
- **PS2 differences**: Implements critical deviations from standard MIPS (HI/LO sign-extension, FPU denormals, ERET behavior)

### Core Classes

- `MipsInstruction`: Represents a decoded MIPS instruction with all fields (Rs, Rt, Rd, Shamt, Funct, Immediate, Target)
- `InstructionDecoder`: Decodes raw 32-bit opcodes into `MipsInstruction` objects
- `InstructionEncoder`: Encodes `MipsInstruction` back to binary
- `Assembler`: Converts assembly text to machine code
- `Disassembler`: Converts machine code to human-readable assembly

### Instruction Categories

#### Core Instructions
Standard MIPS III arithmetic, logic, load/store, branch, and jump operations.

#### Coprocessor Instructions
- **COP0 (System Control)**: TLB management, exception handling, status registers
- **COP1 (FPU)**: Single-precision floating-point operations (no denormal support, matches PS2)
- **COP2 (VU0 Macro Mode)**: Vector unit operations with special argument decoding

#### Multimedia Instructions (MMI)
PS2-specific SIMD-like operations for enhanced performance.

#### Vector Unit (VU0)
Full VU0 microcode support with accurate pipeline simulation.

## Usage Examples

### Decoding an Instruction

```csharp
var decoder = new InstructionDecoder();
var instr = decoder.Decode(0x00000000); // NOP
Console.WriteLine(instr.Disassemble()); // "nop"
```

### Assembling Code

```csharp
var assembler = new Assembler();
var bytes = assembler.Assemble("add $v0, $a0, $a1");
```

### Instruction Execution

Each instruction class implements `Exec()` method for emulation:

```csharp
var add = new AddInstruction();
add.Exec(cpuState);
```

## Architecture Notes

### Register Model
- **GPRs**: 128-bit registers (lower 64 bits accessible, upper 64 bits sign-extended)
- **HI/LO**: Accumulator registers for multiplication/division
- **PC**: Program counter with branch delay slot handling
- **SA**: PS2-specific shift amount register

### Memory Model
- Big-endian byte ordering
- KSEG0/KSEG1 address translation
- Proper handling of uncached/cached regions

### Exception Handling
- COP0 register management
- Exception vector dispatch (BEV=0: 0x80000080, BEV=1: 0xBFC00180)
- ERET implementation with ERL/EXL priority

## Testing

The library includes comprehensive unit tests in `CodeDesigner.Ps2.TestProject` covering:
- Instruction encoding/decoding accuracy
- Execution correctness against PCSX2
- Edge cases and PS2-specific behaviors

## Dependencies

- .NET 6+ runtime
- No external dependencies (pure C# implementation)</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\common.md