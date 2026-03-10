# PINE Protocol Extension Proposal — Debugger Commands

**Target repository:** https://github.com/PCSX2/pcsx2  
**Related files:** `pcsx2/PINE.h`, `pcsx2/PINE.cpp`

## Motivation

The existing PINE protocol covers memory read/write and basic emulator metadata, but
external tools that need a full debugger experience (breakpoints, single-step, register
inspection) currently have no way to do this without a separate custom socket.

This proposal adds **8 new opcodes** (0x10–0x17) that expose EE CPU debugger state.
All packets follow the existing PINE framing and endianness conventions exactly, so
existing clients are unaffected.

---

## Protocol Background

All PINE packets share the same framing:

```
Request:  [total_size : u32 LE] [opcode : u8] [args…]
Reply:    [total_size : u32 LE] [status : u8] [data…]
```

- `total_size` includes the 4-byte size field itself.
- `status` is `IPC_OK (0x00)` or `IPC_FAIL (0xFF)`.

---

## New Opcodes

| Value | Name                  | Description                                   |
|-------|-----------------------|-----------------------------------------------|
| 0x10  | `MsgGetProgramCounter`| Read EE PC and paused/running state           |
| 0x11  | `MsgPause`            | Pause EE execution                            |
| 0x12  | `MsgResume`           | Resume EE execution                           |
| 0x13  | `MsgStep`             | Single-step one EE CPU instruction            |
| 0x14  | `MsgSetBreakpoint`    | Add a PC breakpoint at a given EE address     |
| 0x15  | `MsgClearBreakpoint`  | Remove the breakpoint at a given EE address   |
| 0x16  | `MsgClearAllBreakpoints` | Remove all PC breakpoints                  |
| 0x17  | `MsgGetRegisters`     | Read EE GPRs, HI/LO, and PC                  |

---

## Packet Specifications

### MsgGetProgramCounter (0x10)

Returns the current EE program counter and a flag indicating whether emulation is
paused.

```
Request:  [5 : u32LE] [0x10 : u8]
Reply:    [10 : u32LE] [IPC_OK : u8] [PC : u32LE] [IsPaused : u8]
```

- **PC** — lower 32 bits of the EE instruction pointer, KSEG0-normalised  
  (i.e. KSEG1 addresses 0xA0000000–0xBFFFFFFF are mapped to 0x80000000–0x9FFFFFFF).
- **IsPaused** — `1` if emulation is currently paused, `0` if running.

---

### MsgPause (0x11)

Halts EE execution at the next instruction boundary.

```
Request:  [5 : u32LE] [0x11 : u8]
Reply OK: [5 : u32LE] [IPC_OK : u8]
```

Returns `IPC_OK` whether or not execution was already paused.

---

### MsgResume (0x12)

Resumes EE execution.

```
Request:  [5 : u32LE] [0x12 : u8]
Reply OK: [5 : u32LE] [IPC_OK : u8]
```

Returns `IPC_OK` whether or not execution was already running.  
If execution is paused at a breakpoint address, the breakpoint instruction is executed
once before the run gate is released (so the same breakpoint does not immediately
re-fire).

---

### MsgStep (0x13)

Executes exactly one EE instruction while keeping emulation paused.

```
Request:      [5 : u32LE] [0x13 : u8]
Reply OK:     [9 : u32LE] [IPC_OK : u8] [NewPC : u32LE]
Reply error:  [5 : u32LE] [IPC_FAIL : u8]   (if emulation is not paused)
```

- **NewPC** — EE PC after the instruction was executed, KSEG0-normalised.

---

### MsgSetBreakpoint (0x14)

Registers a PC-execution breakpoint. PCSX2 will pause and emit `IPC_OK` on
`MsgGetProgramCounter` / `MsgStep` the next time the EE reaches that address.

```
Request:  [9 : u32LE] [0x14 : u8] [Address : u32LE]
Reply OK: [5 : u32LE] [IPC_OK : u8]
```

- **Address** — EE virtual address (KSEG0 or KSEG1 — internally normalised).
- Duplicate `Address` values are ignored.
- When execution reaches a registered breakpoint address, PCSX2 pauses.
  Subsequent `MsgGetProgramCounter` calls will report `IsPaused == 1` and return
  the breakpoint address.

---

### MsgClearBreakpoint (0x15)

Removes the breakpoint registered at `Address`.

```
Request:      [9 : u32LE] [0x15 : u8] [Address : u32LE]
Reply OK:     [5 : u32LE] [IPC_OK : u8]
Reply error:  [5 : u32LE] [IPC_FAIL : u8]   (address was not a breakpoint)
```

---

### MsgClearAllBreakpoints (0x16)

Removes every PC breakpoint that was set via `MsgSetBreakpoint`.

```
Request:  [5 : u32LE] [0x16 : u8]
Reply OK: [5 : u32LE] [IPC_OK : u8]
```

---

### MsgGetRegisters (0x17)

Returns a snapshot of the full EE integer register state: all 32 GPRs (lower 32-bit
halves), PC, HI, and LO.

```
Request:  [5   : u32LE] [0x17 : u8]
Reply OK: [145 : u32LE] [IPC_OK : u8]
          [r0..r31 : 32 × u32LE]   (128 bytes — lower 32 bits of each 64-bit GPR)
          [PC      : u32LE]         (KSEG0-normalised)
          [HI      : u32LE]
          [LO      : u32LE]
```

Total reply size: 4 (size) + 1 (status) + 128 (GPR) + 4 (PC) + 4 (HI) + 4 (LO) = **145 bytes**.

---

## Reference C++ Implementation

### `pcsx2/PINE.h` — diff

```diff
     enum IPCCommand : unsigned char {
         MsgRead8 = 0,
         MsgRead16 = 1,
         MsgRead32 = 2,
         MsgRead64 = 3,
         MsgWrite8 = 4,
         MsgWrite16 = 5,
         MsgWrite32 = 6,
         MsgWrite64 = 7,
         MsgVersion = 8,
         MsgSaveState = 9,
         MsgLoadState = 0xA,
         MsgTitle = 0xB,
         MsgID = 0xC,
         MsgUUID = 0xD,
         MsgGameVersion = 0xE,
         MsgStatus = 0xF,
+        MsgGetProgramCounter   = 0x10,
+        MsgPause               = 0x11,
+        MsgResume              = 0x12,
+        MsgStep                = 0x13,
+        MsgSetBreakpoint       = 0x14,
+        MsgClearBreakpoint     = 0x15,
+        MsgClearAllBreakpoints = 0x16,
+        MsgGetRegisters        = 0x17,
         MsgUnimplemented = 0xFF
     };
```

Add a breakpoint set to the PINE server class (or reuse the existing PCSX2 CBreakPoints
infrastructure where already present):

```diff
 class Pcsx2PINE {
 private:
     // … existing fields …
+    std::unordered_set<u32> m_breakpoints;
+    std::mutex              m_bp_mutex;
```

### `pcsx2/PINE.cpp` — new cases in the dispatch switch

Add the following cases to the main command-dispatch `switch` statement in
`PINE::ProcessMessage()` (or equivalent function):

```cpp
case MsgGetProgramCounter: {
    // Reply: [size(4)][IPC_OK(1)][PC(4)][IsPaused(1)]
    const u32 pc = NormalizePcKseg0(cpuRegs.pc);
    const u8 paused = (VMManager::GetState() == VMState::Paused) ? 1 : 0;
    ret_buffer[4] = IPC_OK;
    ToArray(ret_buffer, pc, 5);
    ret_buffer[9] = paused;
    SendReply(10);
    break;
}

case MsgPause: {
    VMManager::SetPaused(true);
    ret_buffer[4] = IPC_OK;
    SendReply(5);
    break;
}

case MsgResume: {
    VMManager::SetPaused(false);
    ret_buffer[4] = IPC_OK;
    SendReply(5);
    break;
}

case MsgStep: {
    if (VMManager::GetState() != VMState::Paused) {
        ret_buffer[4] = IPC_FAIL;
        SendReply(5);
        break;
    }
    // Execute one EE instruction via the existing PCSX2 step mechanism.
    // VMManager::Step() / Cpu->Step() advances PC by one instruction.
    Cpu->Step();
    const u32 newPc = NormalizePcKseg0(cpuRegs.pc);
    ret_buffer[4] = IPC_OK;
    ToArray(ret_buffer, newPc, 5);
    SendReply(9);
    break;
}

case MsgSetBreakpoint: {
    const u32 addr = NormalizePcKseg0(FromArray<u32>(ipc_buffer, 5));
    {
        std::lock_guard<std::mutex> lock(m_bp_mutex);
        m_breakpoints.insert(addr);
    }
    // Optionally wire into CBreakPoints for GUI consistency:
    // CBreakPoints::AddBreakPoint(BREAKPOINT_EE, addr, false);
    ret_buffer[4] = IPC_OK;
    SendReply(5);
    break;
}

case MsgClearBreakpoint: {
    const u32 addr = NormalizePcKseg0(FromArray<u32>(ipc_buffer, 5));
    bool removed = false;
    {
        std::lock_guard<std::mutex> lock(m_bp_mutex);
        removed = m_breakpoints.erase(addr) > 0;
    }
    ret_buffer[4] = removed ? IPC_OK : IPC_FAIL;
    SendReply(5);
    break;
}

case MsgClearAllBreakpoints: {
    {
        std::lock_guard<std::mutex> lock(m_bp_mutex);
        m_breakpoints.clear();
    }
    ret_buffer[4] = IPC_OK;
    SendReply(5);
    break;
}

case MsgGetRegisters: {
    // Reply: [size(4)][IPC_OK(1)][r0..r31(128)][PC(4)][HI(4)][LO(4)]
    ret_buffer[4] = IPC_OK;
    int pos = 5;
    for (int i = 0; i < 32; i++) {
        // cpuRegs.GPR.r[i].UL[0] = lower 32 bits of the 128-bit GPR
        ToArray(ret_buffer, cpuRegs.GPR.r[i].UL[0], pos);
        pos += 4;
    }
    ToArray(ret_buffer, NormalizePcKseg0(cpuRegs.pc), pos); pos += 4;
    ToArray(ret_buffer, cpuRegs.HI.UL[0],              pos); pos += 4;
    ToArray(ret_buffer, cpuRegs.LO.UL[0],              pos);
    SendReply(145);
    break;
}
```

### Breakpoint check in the EE step path

In the EE CPU step loop (e.g. `Interpreter::Execute()` or the recompiler exit check),
add a hook to pause and notify when a PINE breakpoint is hit:

```cpp
// After each instruction executes, before fetching the next:
if (!m_pine_breakpoints.empty()) {
    std::lock_guard<std::mutex> lock(m_bp_mutex);
    if (m_pine_breakpoints.count(NormalizePcKseg0(cpuRegs.pc))) {
        VMManager::SetPaused(true);
    }
}
```

### Helper — KSEG0 normalisation

```cpp
static inline u32 NormalizePcKseg0(u32 vaddr) {
    // Map KSEG1 (0xA0000000–0xBFFFFFFF) to KSEG0 (0x80000000–0x9FFFFFFF)
    if ((vaddr & 0xE0000000u) == 0xA0000000u)
        return (vaddr & 0x1FFFFFFFu) | 0x80000000u;
    return vaddr;
}
```

---

## Batch Mode Compatibility

`MsgGetProgramCounter`, `MsgPause`, `MsgResume`, `MsgStep`,
`MsgSetBreakpoint`, `MsgClearBreakpoint`, `MsgClearAllBreakpoints`, and
`MsgGetRegisters` are **single-command only** — they are not batchable because they
affect mutable CPU state and ordering guarantees must be preserved.  The existing
`BatchSafetyChecks` path can return `IPC_FAIL` for these opcodes when encountered
inside a batch request.

---

## Backwards Compatibility

- All new opcodes are above 0x0F — no existing opcode values are changed.
- Clients that do not send the new opcodes are unaffected.
- PCSX2 builds that do not include this patch will return `IPC_FAIL`
  (`MsgUnimplemented = 0xFF`) for any unrecognised opcode, which is already
  the documented behaviour.

---

## Testing Checklist

- [ ] `MsgGetProgramCounter` returns consistent PC while a game is running
- [ ] `MsgPause` halts execution; subsequent `MsgGetProgramCounter.IsPaused == 1`
- [ ] `MsgResume` restarts execution; subsequent `MsgGetProgramCounter.IsPaused == 0`
- [ ] `MsgStep` while paused advances PC to the next instruction (PC + 4 for normal
      instructions, PC + 8 for instructions with a branch delay slot)
- [ ] `MsgStep` while running returns `IPC_FAIL`
- [ ] `MsgSetBreakpoint` + `MsgResume` pauses at the correct PC
- [ ] `MsgClearBreakpoint` removes the breakpoint; execution no longer pauses
- [ ] `MsgClearAllBreakpoints` removes all breakpoints
- [ ] `MsgGetRegisters` returns 145-byte reply; `r0 == 0` (hardwired zero)
- [ ] All new opcodes return `IPC_FAIL` gracefully when called out of context
