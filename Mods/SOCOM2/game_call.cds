// SOCOM II (SCUS-97275, r0004) - call game functions once, from the game's own main loop
//
// Used by socom_bots.py (bomb, ...): the script fills the call table, sets the flag and points the main-loop
// hook (0x001E70E4 `jalr t9` -> `jal :call_stub`) at this stub. On the next frame the stub runs every entry,
// clears the flag and tail-calls the game object's update like the other stubs; the script then restores
// the original hook word. Same mechanism as set_uivars.cds.
//
// Call table at 0x000D0700: { func, a0, a1, a2, a3 } x up to 6 (20 bytes each), terminated by func = 0.
// Each call's v0 is written back over its func word, so the script can read the results.
// 0x000D0780-0x000D07FF: free argument buffer for the script (vectors, strings).

address $000D06F0
call_flag:
    hexcode $00000000

address $000D0800
call_stub:
    lui   $t0, $000D
    lw    $t1, $06F0($t0)
    beq   $t1, $zero, :call_done
    nop
    addiu $sp, $sp, -64
    sd    $ra, $0000($sp)
    sd    $a0, $0008($sp)
    hexcode $E7AC0010            // swc1 $f12, 16($sp)
    sd    $s0, $0018($sp)
    lui   $s0, $000D
    addiu $s0, $s0, $0700
call_loop:
    lw    $t9, $0000($s0)
    beq   $t9, $zero, :call_end
    nop
    lw    $a0, $0004($s0)
    lw    $a1, $0008($s0)
    lw    $a2, $000C($s0)
    jalr  $ra, $t9
    lw    $a3, $0010($s0)
    sw    $v0, $0000($s0)
    beq   $zero, $zero, :call_loop
    addiu $s0, $s0, 20
call_end:
    lui   $t0, $000D
    sw    $zero, $06F0($t0)
    ld    $s0, $0018($sp)
    hexcode $C7AC0010            // lwc1 $f12, 16($sp)
    ld    $a0, $0008($sp)
    ld    $ra, $0000($sp)
    addiu $sp, $sp, 64
call_done:
    lw    $t9, $0000($a0)
    lw    $t9, $001C($t9)
    jr    $t9
    nop
