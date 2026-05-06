[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.EmulatorCore — PS2 Hardware Emulation

The `CodeDesigner.Ps2.EmulatorCore` project provides a complete software emulation of the Sony PlayStation 2 console, including the Emotion Engine (EE) CPU, I/O Processor (IOP), and all major hardware peripherals.

## Overview

This emulator accurately simulates PS2 hardware to run commercial games and homebrew software. It integrates with the core library for CPU execution and adds comprehensive peripheral emulation.

## Architecture

### CPU Cores

#### Emotion Engine (EE)
- **MIPS64 R5900** at 294 MHz
- 128-bit SIMD multimedia instructions (MMI)
- Vector Units (VU0/VU1) for 3D graphics acceleration
- FPU with PS2-specific denormal handling
- Dual-issue pipeline simulation

#### I/O Processor (IOP)
- **MIPS32 R3000A** at 36 MHz
- Handles I/O operations and legacy PS1 compatibility
- Integrated SPU for audio processing

### Memory System

#### Main RAM
- 32 MB RDRAM at 294 MHz
- Dual-channel interleaved access
- Proper latency and bandwidth simulation

#### BIOS ROM
- 4 MB flash memory
- KSEG1 mirror at 0xBFC00000
- Contains PS2 system software

#### Scratchpad
- 16 KB fast RAM for EE
- Uncached access for performance-critical code

### Hardware Peripherals

#### Graphics Synthesizer (GS)
- 128×64-bit internal registers
- Handles rendering commands from EE via GIF
- Simulates frame buffer and texture memory

#### DMA Controller
- 10 EE channels + 13 IOP channels
- Supports all transfer modes (normal, chain, interleaved)
- Accurate interrupt generation

#### Vector Interface (VIF0/VIF1)
- Unpacks vertex data for VU processing
- Supports all 14 UNPACK formats
- MPG, MSCAL, STROW/STCOL, STMASK operations

#### Graphics Interface (GIF)
- Receives display lists from EE
- PACKED, REGLIST, IMAGE modes
- PATH1/2/3 routing to GS/DMAC/VIF

#### Timer Units
- 4×16-bit timers (EE T0-T3)
- Configurable clock sources and modes
- Interrupt generation on compare/overflow

#### Interrupt Controller (INTC)
- 32 interrupt sources
- Write-1-to-clear and mask registers
- Proper priority handling

#### Serial I/O (SIO2)
- Controller communication
- Pad state simulation
- INTC bit 17 signaling

#### CD/DVD Drive
- ISO-9660 filesystem support
- Sector reading and caching
- DMAC integration for data transfer

#### Memory Card
- 8 MB per card (2 slots)
- Save file management
- PS1/PS2 format support

#### Sound Processing Unit (SPU2)
- 48-channel ADPCM audio
- Reverb effects processing
- SPU RAM simulation

## Execution Model

### Step-by-Step Emulation
- `PS2Emulator.Step()` advances by one EE instruction
- Handles branch delay slots correctly
- Processes hardware events between instructions

### Event System
- `UnknownInstruction`: Invalid opcode encountered
- `SyscallRaised`: SYSCALL instruction executed
- `InterruptPending`: Hardware interrupt ready
- `DmaChannelComplete`: DMA transfer finished

### ELF Loading
- `ElfLoader.Load()` parses ELF32 MIPS images
- Loads PT_LOAD segments into memory
- Sets entry point for execution

## Integration

### With PCSX2
- Compatible with PINE debugger protocol
- Can load PCSX2 save states
- Memory layout matches PCSX2 exactly

### With VS Code Extension
- Socket server for real-time debugging
- Breakpoint and watchpoint support
- Live memory inspection

## Performance

- Cycle-accurate where possible
- Optimized for debugging (not speed)
- Supports save/load state for quick iteration

## Testing

Extensive test suite covers:
- Hardware register accuracy
- DMA transfer correctness
- Interrupt timing
- Game compatibility verification

## Usage

```csharp
var emulator = new PS2Emulator();
emulator.Reset(); // Initialize to BIOS entry point
emulator.LoadElf("game.elf");
while (!emulator.Halted) {
    emulator.Step();
}
```</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\emulator.md