# Emotion Engine COP0 (System Control Coprocessor)

## Overview

**COP0** is the **System Control Coprocessor** of the PlayStation 2 Emotion Engine.  
It manages low-level CPU control, exceptions, interrupts, virtual memory, and cache behavior.

COP0 is **not a general-purpose coprocessor** — its instructions interact directly with
privileged CPU state and are typically used by the kernel, runtime, or emulator.

---

## COP0 Instructions

| Instruction | Description |
|-------------|-------------|
| MFC0 | Move from COP0 register to GPR |
| MTC0 | Move from GPR to COP0 register |
| DMFC0 | Move from COP0 register to GPR (64-bit) |
| DMTC0 | Move from GPR to COP0 register (64-bit) |
| CFC0 | Move control from COP0 |
| CTC0 | Move control to COP0 |
| ERET | Return from exception |
| TLBP | Probe TLB for matching entry |
| TLBR | Read indexed TLB entry |
| TLBWI | Write indexed TLB entry |
| TLBWR | Write random TLB entry |
| CACHE | Cache operation (no-op in emulation) |
| EI | Enable interrupts — sets Status.EIE (bit 16) |
| DI | Disable interrupts — clears Status.EIE (bit 16) |

---

## COP0 Registers (Emotion Engine)

| Register | Name | Description |
|---------:|------|-------------|
| 0 | Index | TLB index register |
| 1 | Random | Random TLB index |
| 2 | EntryLo0 | Low half of TLB entry 0 |
| 3 | EntryLo1 | Low half of TLB entry 1 |
| 4 | Context | Virtual memory context |
| 5 | PageMask | TLB page size mask |
| 6 | Wired | Wired TLB entries |
| 8 | BadVAddr | Faulting virtual address |
| 9 | Count | Timer count register |
| 10 | EntryHi | High half of TLB entry |
| 11 | Compare | Timer compare register |
| 12 | Status | Processor status and control |
| 13 | Cause | Exception cause |
| 14 | EPC | Exception program counter |
| 15 | PRId | Processor ID |
| 16 | Config | System configuration |
| 18 | WatchLo | Watchpoint address (low) |
| 19 | WatchHi | Watchpoint address (high) |
| 22 | Debug | Debug control |
| 23 | DEPC | Debug exception PC |
| 24 | Perf | Performance counter (legacy) |
| 25 | PCCR | Performance counter control (sel=0); PCR0 (sel=1); PCR1 (sel=2) |
| 26 | ECC | Error correction code |
| 27 | CacheErr | Cache error status |
| 28 | TagLo | Cache tag low |
| 29 | TagHi | Cache tag high |
| 30 | ErrorEPC | Error exception PC (used by ERET when ERL=1) |

> ⚠️ Some registers are **EE-specific** or behave differently compared to standard MIPS CPUs.

---

## Exception Handling Flow

1. Exception occurs (interrupt, syscall, fault, break)
2. CPU saves PC → `EPC`
3. Cause stored in `Cause`
4. Status flags updated in `Status`
5. Control jumps to exception vector
6. `ERET` restores execution

---

## Privilege Levels

- **Kernel Mode** – Full COP0 access
- **User Mode** – COP0 access restricted
- Status register controls mode switching

---

## Cache Control

The `CACHE` instruction is used for:
- Instruction cache invalidation
- Data cache writeback
- Cache line locking and flushing

Cache behavior is **critical for EE performance and correctness**.

---

## Notes for Emulation & Tooling

- COP0 behavior is **timing-sensitive**
- Incorrect Status/Cause handling breaks games
- TLB is fully implemented; `HasAnyValidEntry` gate preserves identity-mapping for pre-TLB unit tests
- Count/Compare registers are used for timing and interrupts; `IncrementCount()` sets Cause.IP7 when Count reaches Compare; Status.IM7 gates delivery
- `ERET` checks `ERL` first — if set it clears `ERL` and returns via `ErrorEPC` (reg **30**); otherwise it clears `EXL` and returns via `EPC` (reg 14)
- `EI`/`DI` are EE-specific extensions encoded in the COP0 sub-opcode space; they are **not** present on standard MIPS CPUs
- Default Status after reset is `0x00400000` (BEV=1, interrupts disabled)
- Register 25 is used for performance counters via the `sel` field in `MFC0`/`MTC0` instructions: sel=0 → PCCR, sel=1 → PCR0, sel=2 → PCR1

---

## Disclaimer

This documentation is for **educational and research purposes only**.  
PlayStation and Emotion Engine are trademarks of Sony Interactive Entertainment.
