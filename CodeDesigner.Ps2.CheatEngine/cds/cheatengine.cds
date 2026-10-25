////////////////////////////////////////////////////////////////////////////////
//
//  Code Designer — Assembly-Level Cheat Engine
//
//  This MIPS routine is injected into unused PS2 EE RAM and hooked into the
//  game's execution path (an instruction that runs every frame). Each time the
//  hook fires the engine walks a table of rows and re-applies them, so cheat
//  values hold against the running game.
//
//  Placeholders ({{...}}) are substituted by CdsCheatEngine before compiling.
//
//  Memory layout:
//      Base + 0x0000   Engine code (this file)
//      Base + 0x0400   Engine data:
//                        +0x00  original instruction (hook point)
//                        +0x04  original instruction (hook point + 4)
//                        +0x08  return address (hook point + 8)
//                        +0x0C  frame counter (incremented every run)
//      Base + 0x0500   Table rows (16 bytes each), terminated by a zero control word
//
//  Row format (words):  +0 control   +4 address   +8 value   +C extra
//      control bits 7..0 = opcode (see RawCodeTranslator.EngineOp)
//        01/02/03  write 8/16/32           [address] = value
//        04/05/06  add 8/16/32             [address] += value (two's-complement for decrement)
//        07        serial 32-bit write     count = control>>16, stride (words) = (control>>8)&FF,
//                                          value increment = extra
//        08        compare                 size = (control>>12)&F (1/2/4), compare = (control>>8)&F
//                                          (0 ==, 1 !=, 2 <, 3 > unsigned); when false skip
//                                          control>>16 rows
//        09        timer                   when frame % value != 0 (or value == 0) skip
//                                          control>>16 rows
//        00        end of table
//
//  Register use: at, v0, v1, t0-t4 and s0 are saved as full 128-bit registers
//  (sq/lq) together with HI/LO, so the game's state is untouched. The engine
//  returns through a patched `j`, not through a register.
//
////////////////////////////////////////////////////////////////////////////////

address ${{BASE}}

scope CheatEngine {

    entry:
        addiu sp, sp, $FF60
        sq at, $0000(sp)
        sq v0, $0010(sp)
        sq v1, $0020(sp)
        sq t0, $0030(sp)
        sq t1, $0040(sp)
        sq t2, $0050(sp)
        sq t3, $0060(sp)
        sq t4, $0070(sp)
        sq s0, $0080(sp)
        mfhi t0
        mflo t1
        sd t0, $0090(sp)
        sd t1, $0098(sp)

        // s0 = first table row
        lui s0, ${{TABLE_HI}}
        ori s0, s0, ${{TABLE_LO}}

        // Frame counter lives at data + 0x0C = table - 0xF4. Keep the new count in `at`.
        addiu t1, s0, $FF0C
        lw at, $0000(t1)
        addiu at, at, $0001
        sw at, $0000(t1)

    processLoop:
        lw t0, $0000(s0)
        lw t1, $0004(s0)
        lw t2, $0008(s0)
        andi t3, t0, $00FF
        beq t3, zero, :done
        nop
        addiu t4, zero, $0001
        beq t3, t4, :write8
        nop
        addiu t4, zero, $0002
        beq t3, t4, :write16
        nop
        addiu t4, zero, $0003
        beq t3, t4, :write32
        nop
        addiu t4, zero, $0004
        beq t3, t4, :add8
        nop
        addiu t4, zero, $0005
        beq t3, t4, :add16
        nop
        addiu t4, zero, $0006
        beq t3, t4, :add32
        nop
        addiu t4, zero, $0007
        beq t3, t4, :serial32
        nop
        addiu t4, zero, $0008
        beq t3, t4, :compare
        nop
        addiu t4, zero, $0009
        beq t3, t4, :timer
        nop
        // Unknown opcode: ignore the row
        j :nextEntry
        nop

    write8:
        sb t2, $0000(t1)
        j :nextEntry
        nop

    write16:
        sh t2, $0000(t1)
        j :nextEntry
        nop

    write32:
        sw t2, $0000(t1)
        j :nextEntry
        nop

    add8:
        lbu t4, $0000(t1)
        addu t4, t4, t2
        sb t4, $0000(t1)
        j :nextEntry
        nop

    add16:
        lhu t4, $0000(t1)
        addu t4, t4, t2
        sh t4, $0000(t1)
        j :nextEntry
        nop

    add32:
        lw t4, $0000(t1)
        addu t4, t4, t2
        sw t4, $0000(t1)
        j :nextEntry
        nop

    serial32:
        srl t4, t0, $10
        srl t3, t0, $08
        andi t3, t3, $00FF
        sll t3, t3, $02
        lw v0, $000C(s0)
    serialLoop:
        beq t4, zero, :nextEntry
        nop
        sw t2, $0000(t1)
        addu t1, t1, t3
        addu t2, t2, v0
        addiu t4, t4, $FFFF
        j :serialLoop
        nop

    compare:
        srl t4, t0, $0C
        andi t4, t4, $000F
        addiu v0, zero, $0001
        beq t4, v0, :compareLoad8
        nop
        addiu v0, zero, $0002
        beq t4, v0, :compareLoad16
        nop
        lw v1, $0000(t1)
        j :compareTest
        nop
    compareLoad8:
        lbu v1, $0000(t1)
        andi t2, t2, $00FF
        j :compareTest
        nop
    compareLoad16:
        lhu v1, $0000(t1)
        andi t2, t2, $FFFF
    compareTest:
        srl t4, t0, $08
        andi t4, t4, $000F
        beq t4, zero, :compareEqual
        nop
        addiu v0, zero, $0001
        beq t4, v0, :compareNotEqual
        nop
        addiu v0, zero, $0002
        beq t4, v0, :compareLess
        nop
        // 3: greater (unsigned)
        sltu v0, t2, v1
        bne v0, zero, :nextEntry
        nop
        j :skipRows
        nop
    compareEqual:
        beq v1, t2, :nextEntry
        nop
        j :skipRows
        nop
    compareNotEqual:
        bne v1, t2, :nextEntry
        nop
        j :skipRows
        nop
    compareLess:
        sltu v0, v1, t2
        bne v0, zero, :nextEntry
        nop
        j :skipRows
        nop

    timer:
        beq t2, zero, :skipRows
        nop
        divu at, t2
        mfhi v0
        beq v0, zero, :nextEntry
        nop
    skipRows:
        srl t4, t0, $10
        sll t4, t4, $04
        addu s0, s0, t4

    nextEntry:
        addiu s0, s0, $0010
        j :processLoop
        nop

    done:
        ld t0, $0090(sp)
        ld t1, $0098(sp)
        mthi t0
        mtlo t1
        lq at, $0000(sp)
        lq v0, $0010(sp)
        lq v1, $0020(sp)
        lq t0, $0030(sp)
        lq t1, $0040(sp)
        lq t2, $0050(sp)
        lq t3, $0060(sp)
        lq t4, $0070(sp)
        lq s0, $0080(sp)
        addiu sp, sp, $00A0

        // Patched by CdsCheatEngine: the two hooked instructions, then `j hook+8`.
    origInstr0:
        hexcode $C0DE0001
    origInstr1:
        hexcode $C0DE0002
    returnJump:
        hexcode $C0DE0003
        nop
}

////////////////////////////////////////////////////////////////////////////////
//  Engine data area: base + 0x0400
////////////////////////////////////////////////////////////////////////////////
address ${{DATA}}
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
