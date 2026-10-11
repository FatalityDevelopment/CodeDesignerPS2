// SOCOM II (SCUS-97275, r0004) - set UI string variables the game's own way (one shot)
//
// Used by socom_bots.py login: puts the account into PLAYERNAMEVAR / PLAYERPASSWORD exactly as if they
// had been typed, so the "Connect to SOCOM II" screen enables CONNECT (CheckConnectEnable / TestUIVar
// read the real variables) and LobbyLogin (0x0027EEA0) / LobbyRegister (0x0027EF60) send them.
//
//   0x003A5C10(table 0x00444890, name)  find variable  -> var* (0 if missing)
//   0x003A65B0(var, text)               set string: frees the old value, type = 3, copies text into the
//                                       string pool, notifies the UI (what ClearUIVar uses for strings)
//
// Runs once from the main-loop hook (0x001E70E4 `jalr t9` -> `jal :uivar_stub`) while the flag at 0x000D0480
// is set, clears the flag, then tail-calls the game object's update like the other stubs. The script puts
// the hook in, waits for the flag to clear and restores the original 0x001E70E4 word.
//
// Pairs table at 0x000D0490: { name*, value* } ..., terminated by name* = 0. Strings are filled in by the
// script: names at 0x000D04B0 / 0x000D04C0, values at 0x000D0440 / 0x000D0460 (auto_login.cds buffers).

address $000D0480
uivar_flag:
    hexcode $00000000

address $000D0600
uivar_stub:
    lui   $t0, $000D
    lw    $t1, $0480($t0)
    beq   $t1, $zero, :uivar_done
    nop
    addiu $sp, $sp, -64
    sd    $ra, $0000($sp)
    sd    $a0, $0008($sp)
    hexcode $E7AC0010            // swc1 $f12, 16($sp)
    sd    $s0, $0018($sp)
    sd    $s1, $0020($sp)
    lui   $s1, $000D
    addiu $s1, $s1, $0490
uivar_loop:
    lw    $a1, $0000($s1)
    beq   $a1, $zero, :uivar_end
    nop
    lui   $a0, $0044
    jal   $003A5C10
    addiu $a0, $a0, $4890
    beq   $v0, $zero, :uivar_next
    nop
    daddu $a0, $v0, $zero
    jal   $003A65B0
    lw    $a1, $0004($s1)
uivar_next:
    beq   $zero, $zero, :uivar_loop
    addiu $s1, $s1, 8
uivar_end:
    lui   $t0, $000D
    sw    $zero, $0480($t0)
    ld    $s1, $0020($sp)
    ld    $s0, $0018($sp)
    hexcode $C7AC0010            // lwc1 $f12, 16($sp)
    ld    $a0, $0008($sp)
    ld    $ra, $0000($sp)
    addiu $sp, $sp, 64
uivar_done:
    lw    $t9, $0000($a0)
    lw    $t9, $001C($t9)
    jr    $t9
    nop
