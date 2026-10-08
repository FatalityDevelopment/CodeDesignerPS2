// SOCOM II (SCUS-97275, r0004) - TEST ONLY: simulate players joining the host's game
//
// Write (dmeIndex + 1) to the mailbox at 0x000D0100 and the next frame the host runs its real
// DME client-connect callback (0x002BE260) for that index: team assignment, team slot index,
// flags-byte packing, game-full check. 0x000D0104 = info flags (bit 0 = join as spectator).
// No network traffic is involved, so other consoles never see these players.
//
// Hook: the main loop (0x001E70D0..0x001E70EC, inside 0x001E7040) calls the game object's per-frame
// update through `jalr t9` at 0x001E70E4 (a0 = 0x00434E40, $f12 = frame time). That jalr becomes
// `jal fake_join_stub`; the stub runs any pending fake join, then reloads the vtable entry and
// tail-calls it (ra already points back into the loop).

address $000D0100
mailbox:
    hexcode $00000000
mailbox_flags:
    hexcode $00000000

// 24-byte client-connect info struct handed to 0x002BE260:
// +0 event (0; 1 = script-only ON_CLIENT_CONNECT), +4 DME index, +0x10 flags
address $000D0120
info:
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

address $000D0140
fake_join_stub:
    lui   $t0, $000D
    lw    $t1, $0100($t0)
    beq   $t1, $zero, :fake_done
    nop
    addiu $sp, $sp, -64
    sd    $ra, $0000($sp)
    sd    $a0, $0008($sp)
    hexcode $E7AC0010            // swc1 $f12, 16($sp)
    sw    $zero, $0100($t0)
    addiu $t1, $t1, -1
    sw    $zero, $0120($t0)
    sw    $t1, $0124($t0)
    lw    $t2, $0104($t0)
    sw    $t2, $0130($t0)
    jal   $002BE260
    addiu $a0, $t0, $0120
    ld    $ra, $0000($sp)
    ld    $a0, $0008($sp)
    hexcode $C7AC0010            // lwc1 $f12, 16($sp)
    addiu $sp, $sp, 64
fake_done:
    lw    $t9, $0000($a0)
    lw    $t9, $001C($t9)
    jr    $t9
    nop

address $001E70E4
    jal   :fake_join_stub
