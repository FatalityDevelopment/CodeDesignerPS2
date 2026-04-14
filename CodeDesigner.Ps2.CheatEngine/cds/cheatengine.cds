////////////////////////////////////////////////////////////////////////////////
//
//  Code Designer — Assembly-Level Cheat Engine
//
//  This MIPS routine is injected into unused PS2 EE RAM and hooked into the
//  game's execution path (typically a VBlank handler or main-loop instruction).
//  Every time the hook fires, the engine iterates a cheat table in memory and
//  applies constant writes so cheat values persist against the running CPU.
//
//  Memory layout (base address is configurable via CdsCheatEngine):
//
//      Base + 0x0000   Engine code (this file)
//      Base + 0x0200   Engine data:
//                        +0x00  original instruction word (from hook point)
//                        +0x04  original instruction word (hook point + 4)
//                        +0x08  return address (hook point + 8)
//      Base + 0x0300   Cheat table entries (8 bytes each, terminated by FFFFFFFF)
//
//  Cheat table entry format (matches GameShark/Action Replay):
//      Word 0:  [type_byte << 24 | 24-bit address offset]
//      Word 1:  [32-bit value]
//      End marker:  FFFFFFFF FFFFFFFF
//
//  Supported cheat types:
//      0x00 - 8-bit write        (sb)
//      0x10 - 16-bit write       (sh)
//      0x20 - 32-bit write       (sw)
//      0xB0 - timer gate         (body count in low 24 bits, frame interval in word 1)
//      0xD0 - 16-bit conditional (skip next entry if mem != value)
//      0xE0 - 32-bit conditional (skip next entry if mem != value)
//
////////////////////////////////////////////////////////////////////////////////

// Engine base address (placeholder; patched by CdsCheatEngine.cs)
address $000C0000

scope CheatEngine {

    ////////////////////////////////////////////////////////////////////////////
    //  Entry point
    ////////////////////////////////////////////////////////////////////////////
    entry:
        // Save registers to stack (sp -= 48)
        addiu sp, sp, $FFD0
        sw ra, $002C(sp)
        sw s0, $0028(sp)
        sw s1, $0024(sp)
        sw s2, $0020(sp)
        sw s3, $001C(sp)
        sw s4, $0018(sp)
        sw at, $0014(sp)
        sw v0, $0010(sp)
        sw v1, $000C(sp)

        // Load cheat table pointer: s0 = base + 0x0300
        // This lui+ori pair is patched by CdsCheatEngine to match the base.
    loadTableBase:
        lui s0, $000C
        ori s0, s0, $0300
        addu t0, s0, zero

        // Increment persistent frame counter at EngineData.frameCounter.
        // Relative to the cheat table base, frameCounter lives at -0x00F4 (0xFF0C as a signed immediate).
        addiu v0, t0, $FF0C
        lw v1, $0000(v0)
        addiu v1, v1, $0001
        sw v1, $0000(v0)

    ////////////////////////////////////////////////////////////////////////////
    //  Main processing loop
    ////////////////////////////////////////////////////////////////////////////
    processLoop:
        // Load cheat entry pair
        lw s1, $0000(s0)
        lw s2, $0004(s0)

        // Check for end marker (0xFFFFFFFF via sign-extended $FFFF)
        addiu s3, zero, $FFFF
        beq s1, s3, :done
        nop

        // Extract type byte (upper 8 bits of s1)
        srl s3, s1, $18

        // Extract payload (lower 24 bits of s1):
        // - write/cond entries: address offset
        // - timer entries: guarded body-entry count
        lui at, $00FF
        ori at, at, $FFFF
        and s4, s1, at

        // Dispatch: type 0x00 = 8-bit write
        beq s3, zero, :write8bit
        nop

        // Dispatch: type 0x10 = 16-bit write
        addiu at, zero, $0010
        beq s3, at, :write16bit
        nop

        // Dispatch: type 0x20 = 32-bit write
        addiu at, zero, $0020
        beq s3, at, :write32bit
        nop

        // Dispatch: type 0xB0 = timer gate
        addiu at, zero, $00B0
        beq s3, at, :timerGate
        nop

        // Dispatch: type 0xD0 = 16-bit conditional
        addiu at, zero, $00D0
        beq s3, at, :cond16bit
        nop

        // Dispatch: type 0xE0 = 32-bit conditional
        addiu at, zero, $00E0
        beq s3, at, :cond32bit
        nop

        // Unknown type: skip this entry
        j :nextEntry
        nop

    ////////////////////////////////////////////////////////////////////////////
    //  Write handlers
    ////////////////////////////////////////////////////////////////////////////

    write8bit:
        lui at, $2000
        or s4, s4, at
        sb s2, $0000(s4)
        j :nextEntry
        nop

    write16bit:
        lui at, $2000
        or s4, s4, at
        sh s2, $0000(s4)
        j :nextEntry
        nop

    write32bit:
        lui at, $2000
        or s4, s4, at
        sw s2, $0000(s4)
        j :nextEntry
        nop

    ////////////////////////////////////////////////////////////////////////////
    //  Conditional handlers
    ////////////////////////////////////////////////////////////////////////////

    timerGate:
        // s4 = guarded body-entry count, s2 = frame interval
        beq s2, zero, :nextEntry
        nop
        addiu v0, t0, $FF0C
        lw v1, $0000(v0)
        divu v1, s2
        mfhi v0
        beq v0, zero, :nextEntry
        nop
        // Interval not met: skip the guarded body entries
        sll s4, s4, $03
        addu s0, s0, s4
        j :nextEntry
        nop

    cond16bit:
        lui at, $2000
        or s4, s4, at
        lhu v0, $0000(s4)
        andi v1, s2, $FFFF
        // Condition met: process next entry normally
        beq v0, v1, :nextEntry
        nop
        // Not met: skip the next entry (extra 8 bytes)
        addiu s0, s0, $0008
        j :nextEntry
        nop

    cond32bit:
        lui at, $2000
        or s4, s4, at
        lw v0, $0000(s4)
        // Condition met: process next entry normally
        beq v0, s2, :nextEntry
        nop
        // Not met: skip the next entry
        addiu s0, s0, $0008
        j :nextEntry
        nop

    ////////////////////////////////////////////////////////////////////////////
    //  Advance to next entry (each entry = 8 bytes)
    ////////////////////////////////////////////////////////////////////////////
    nextEntry:
        addiu s0, s0, $0008
        j :processLoop
        nop

    ////////////////////////////////////////////////////////////////////////////
    //  Done: restore registers, replay original instructions, return
    ////////////////////////////////////////////////////////////////////////////
    done:
        lw v1, $000C(sp)
        lw v0, $0010(sp)
        lw at, $0014(sp)
        lw s4, $0018(sp)
        lw s3, $001C(sp)
        lw s2, $0020(sp)
        lw s1, $0024(sp)
        lw s0, $0028(sp)
        lw ra, $002C(sp)
        addiu sp, sp, $0030

        // Two NOPs patched at runtime with original hooked instructions
    origInstr0:
        nop
    origInstr1:
        nop

        // Return address loaded via lui+ori, patched at runtime
    loadReturn:
        lui v0, $0000
        ori v0, v0, $0000
        jr v0
        nop
}

////////////////////////////////////////////////////////////////////////////////
//  Engine data area: base + 0x0200
////////////////////////////////////////////////////////////////////////////////
address $000C0200
scope EngineData {
    origWord0:
        hexcode $00000000
    origWord1:
        hexcode $00000000
    returnAddr:
        hexcode $00000000
    frameCounter:
        hexcode $00000000
}

////////////////////////////////////////////////////////////////////////////////
//  Cheat table area: base + 0x0300
////////////////////////////////////////////////////////////////////////////////
address $000C0300
scope CheatTable {
    endMarker:
        hexcode $FFFFFFFF
        hexcode $FFFFFFFF
}
