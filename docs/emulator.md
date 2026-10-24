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
- FPU with PS2-specific denormal handling (flush to zero)
- TLB and COP0 exception handling

#### I/O Processor (IOP)
- **MIPS32 R3000A** at 36 MHz
- Handles I/O: CD/DVD, controllers, memory cards, SPU2, DEV9, USB, i.LINK
- Loads IRX modules (`IopModuleLoader`)

### Memory System

#### Main RAM
- 32 MB at physical `0x00000000`
- KSEG0/KSEG1 mirrors handled by `PS2MemoryBus`

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
- `INTC_STAT` (write-1-to-clear) and `INTC_MASK` (write-1-to-toggle)
- Timer, DMAC, VBLANK and peripheral interrupt bits

#### Serial I/O (SIO2)
- Controller communication
- Pad state simulation
- INTC bit 17 signaling

#### CD/DVD Drive
- ISO-9660 filesystem support
- Sector reading and caching
- DMAC integration for data transfer

#### Memory Card
- Card protocol over SIO2 (`MemoryCard`)
- Card images are managed separately by [Memory Card Manager](memory-card-manager.md)

#### Sound Processing Unit (SPU2)
- Two cores × 24 voices (48 channels)
- Voice and core register banks at IOP `0x1F900000`

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

## Running it

The emulator runs in its own process, `CodeDesigner.Ps2.Emulator.exe`, which hosts `PS2Emulator` behind `InternalPineSocket`: a server that speaks PCSX2's PINE wire format on port `28012`. The Bridge drives it with the same client code it uses for PCSX2, so the disassembler, breakpoints, stepping and memory tools work the same on both. In VS Code, pick the built-in emulator in the sidebar and click **Run**.

```
CodeDesigner.Ps2.Emulator.exe --bios <bios.bin> [--disc <game.iso|zip>]
CodeDesigner.Ps2.Emulator.exe --file <game.elf|bin> [--load-address 0xN]
```

See [Command-line tools](console-app.md).

## Limits

- Built for debugging, not speed, and not a replacement for PCSX2: commercial games generally need PCSX2.
- No save states; use PCSX2 for those.
- Step Over / Step Into and the PCSX2-only PINE extensions (symbols, GS stream, DEV9 capture, audio) are not available.

## Testing

About 600 tests in `CodeDesigner.Ps2.TestProject/Emulator/` cover the CPUs, TLB, exceptions, memory buses, timers, INTC, DMAC, GIF/GS, VIF, VU0/VU1, SIF, CDVD, SIO2, SPU2 and the loaders. See [Test Project](test-project.md).

## Usage

```csharp
using CodeDesigner.Ps2.EmulatorCore;

var emulator = new PS2Emulator();
emulator.Reset();                                         // PC = 0xBFC00000 (BIOS)

uint entry = ElfLoader.LoadIntoEmulator(File.ReadAllBytes("game.elf"), emulator);
emulator.SyscallRaised += (_, pc) => Console.WriteLine($"syscall at {pc:X8}");

for (int i = 0; i < 1_000_000; i++)
    emulator.Step();                                      // one EE instruction, with delay slots
```
