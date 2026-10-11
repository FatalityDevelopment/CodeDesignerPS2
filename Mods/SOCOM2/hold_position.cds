// SOCOM II (SCUS-97275, r0004) - hold the host's soldier at a position every frame
//
// Per-frame hook: the main loop (0x001E70D0..0x001E70EC inside 0x001E7040) calls the game object's
// update through `jalr t9` at 0x001E70E4 (a0 = 0x00434E40). That jalr becomes `jal hold_stub`; the
// stub writes the target position into the soldier entity, then reloads the vtable entry and
// tail-calls it (ra already points back into the loop). Running before the game's own update lets
// it beat pad input and physics.
//
// Mailbox (written by socom_bots.py hold / unhold):
//   0x000D0500  enable (0 = off)
//   0x000D0504  seal entity pointer (SealObject data +0x8C)
//   0x000D0508  x, 0x000D050C y, 0x000D0510 z (floats)
// Writes seal+0x1C and seal+0xB0 (position copies the game reads back each frame).

address $000D0500
hold_enable:
    hexcode $00000000
hold_seal:
    hexcode $00000000
hold_x:
    hexcode $00000000
hold_y:
    hexcode $00000000
hold_z:
    hexcode $00000000

address $000D0520
hold_stub:
    lui   $t0, $000D
    lw    $t1, $0500($t0)
    beq   $t1, $zero, :hold_done
    nop
    lw    $t1, $0504($t0)
    beq   $t1, $zero, :hold_done
    nop
    lw    $t2, $0508($t0)
    lw    $t3, $050C($t0)
    lw    $t4, $0510($t0)
    sw    $t2, $001C($t1)
    sw    $t3, $0020($t1)
    sw    $t4, $0024($t1)
    sw    $t2, $00B0($t1)
    sw    $t3, $00B4($t1)
    sw    $t4, $00B8($t1)
hold_done:
    lw    $t9, $0000($a0)
    lw    $t9, $001C($t9)
    jr    $t9
    nop

address $001E70E4
    jal   :hold_stub
