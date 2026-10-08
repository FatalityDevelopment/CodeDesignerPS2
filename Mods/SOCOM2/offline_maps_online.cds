// SOCOM II (SCUS-97275, r0004) - Offline maps online
//
// Campaign maps sit in the normal Create Game map list next to the online maps and play as
// Suppression with respawn. Every player in the room needs this code.
//
// Map ids: campaign map + 100 (m51 -> 151 ... m83 -> 183), so they never collide with the online
// remakes that share the base id (mp51 Vigilance = 51).
//
// Built from an earlier SP-maps-online mod (dedicated "SINGLE PLAYER MAPS" room, replaced map list)
// with these changes for a mixed list:
//   - campaign entries are appended to the real list (FNC_GetAvailableMPMissions hook) instead of
//     replacing it; the room check / forced reboot and spawn warning are gone
//   - the campaign path no longer patches MP map loading to reboot the game (0x002B1328)
//   - MP path / reset restore AI spawn, weapon-exchange code and 0x002B1328
//   - 0x002682D4 is restored to this build's original (3C010044, was 3C010041)
//   - the default-armory hook keeps the original s4 = v0 / skip-if-null logic on MP maps
//   - all three "mp%d" level-name sites go through _checkMapID_createGame
//
// Game addresses (live build):
//   0x001988D0 sprintf, 0x00199060 strcpy, 0x004417C0 g_LevelName
//   0x002CBAE0 vector<MPMission>::insert(vec, pos, n, &value)  (32-byte elements)
//   0x0044E070 g_MPMissionVector {+4 count, +8 data}            (built from missionlist.rdr)
//   0x00275850 FNC_GetAvailableMPMissions (Create Game map list)
//   0x003158F0 / 0x00321244 / 0x0027A620  sprintf(buf, "mp%d", mapId) level-name sites
//   0x002AEA08 "online check" branch: 10000005 on SP maps, 10400005 on MP maps


////////////////////////////////////////////////////////////////////////////////
//                 Map list: append the campaign entries once
////////////////////////////////////////////////////////////////////////////////
// Entered by a `j` written over the first instruction of FNC_GetAvailableMPMissions.
address $000A0080
list_hook:
    addiu $sp, $sp, -64
    sd    $ra, $0000($sp)
    sd    $a0, $0008($sp)
    sq    $s0, $0010($sp)
    sq    $s1, $0020($sp)
    lui   $t0, $0045
    addiu $t0, $t0, $E070
    lw    $t1, $0004($t0)
    beq   $t1, $zero, :list_out
    lw    $t2, $0008($t0)
    sll   $t1, $t1, 5
    addu  $t1, $t2, $t1
list_chk:
    lw    $t3, $0010($t2)
    addiu $t3, $t3, -151
    beq   $t3, $zero, :list_out
    addiu $t2, $t2, 32
    bne   $t2, $t1, :list_chk
    nop
    lui   $s0, $000A
    addiu $s0, $s0, $0200
    addiu $s1, $zero, 12
list_add:
    lui   $a0, $0045
    addiu $a0, $a0, $E070
    lw    $v0, $0004($a0)
    lw    $v1, $0008($a0)
    sll   $v0, $v0, 5
    addu  $a1, $v1, $v0
    addiu $a2, $zero, 1
    jal   $002CBAE0
    daddu $a3, $s0, $zero
    addiu $s1, $s1, -1
    bne   $s1, $zero, :list_add
    addiu $s0, $s0, 32
list_out:
    ld    $ra, $0000($sp)
    ld    $a0, $0008($sp)
    lq    $s0, $0010($sp)
    lq    $s1, $0020($sp)
    addiu $sp, $sp, 64
    addiu $sp, $sp, -176
    j     $00275854
    nop

// Entry templates (32 bytes): name{0x21, len, ptr} | multiplayer=1 | id | type 5 (Suppression) | 0 | 0
address $000A0200
// CHAOS = m51
    hexcode $00000021
    hexcode $00000005
    hexcode $000A0400
    hexcode $00000001
    hexcode $00000097
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// TERMINAL = m52
    hexcode $00000021
    hexcode $00000008
    hexcode $000A0420
    hexcode $00000001
    hexcode $00000098
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// UPLAND = m53
    hexcode $00000021
    hexcode $00000006
    hexcode $000A0440
    hexcode $00000001
    hexcode $00000099
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// URBAN = m61
    hexcode $00000021
    hexcode $00000005
    hexcode $000A0460
    hexcode $00000001
    hexcode $000000A1
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// STRANGLE = m62
    hexcode $00000021
    hexcode $00000008
    hexcode $000A0480
    hexcode $00000001
    hexcode $000000A2
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// HYDRO = m63
    hexcode $00000021
    hexcode $00000005
    hexcode $000A04A0
    hexcode $00000001
    hexcode $000000A3
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// GUARDIAN = m71
    hexcode $00000021
    hexcode $00000008
    hexcode $000A04C0
    hexcode $00000001
    hexcode $000000AB
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// SERVE = m72
    hexcode $00000021
    hexcode $00000005
    hexcode $000A04E0
    hexcode $00000001
    hexcode $000000AC
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// TIDE = m73
    hexcode $00000021
    hexcode $00000004
    hexcode $000A0500
    hexcode $00000001
    hexcode $000000AD
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// LOCKDOWN = m81
    hexcode $00000021
    hexcode $00000008
    hexcode $000A0520
    hexcode $00000001
    hexcode $000000B5
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// GUIDED = m82
    hexcode $00000021
    hexcode $00000006
    hexcode $000A0540
    hexcode $00000001
    hexcode $000000B6
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000
// DELIVERY = m83
    hexcode $00000021
    hexcode $00000008
    hexcode $000A0560
    hexcode $00000001
    hexcode $000000B7
    hexcode $00000005
    hexcode $00000000
    hexcode $00000000

// Names (32-byte slots)
address $000A0400
    string "CHAOS"
address $000A0420
    string "TERMINAL"
address $000A0440
    string "UPLAND"
address $000A0460
    string "URBAN"
address $000A0480
    string "STRANGLE"
address $000A04A0
    string "HYDRO"
address $000A04C0
    string "GUARDIAN"
address $000A04E0
    string "SERVE"
address $000A0500
    string "TIDE"
address $000A0520
    string "LOCKDOWN"
address $000A0540
    string "GUIDED"
address $000A0560
    string "DELIVERY"

////////////////////////////////////////////////////////////////////////////////
//                 Gameplay support (spawns, models, armory, mode switching)
////////////////////////////////////////////////////////////////////////////////
address $000C0000



//-------------------- Check armory for null weapons, set to m67 if null
_CheckWeaponID:
// check if SP map
la t0, :SP_enabled_BOOL
lbu t0, $0000(t0)
beq t0, zero, :_CheckWeaponID_end
nop

// edit avaiable weapons list to 5
addiu t0, zero, $5
sb t0, $02EC(s1)

// primary: 3b0
// secondary: 3bc

// equip 1: 3c8
_CheckWeaponID_e1:
lbu t0, $03C8(s1)
bne t0, zero, :_CheckWeaponID_e2
nop
// reset equip slot to m67
addiu t0, zero, $79
sb t0, $03C8(s1)

// equip 2: 3d4
_CheckWeaponID_e2:
lbu t0, $03D4(s1)
bne t0, zero, :_CheckWeaponID_e3
nop
// reset equip slot to m67
addiu t0, zero, $79
sb t0, $03D4(s1)

// equip 3: 3e0
_CheckWeaponID_e3:
lbu t0, $03E0(s1)
bne t0, zero, :_CheckWeaponID_end
nop
// reset equip slot to m67
addiu t0, zero, $79
sb t0, $03E0(s1)

_CheckWeaponID_end:
// original code
j $005437A0
nop
//-------------------- 



//-------------------- Reset SP variables

_ResetSP:
addiu sp, sp, $fff0
sw ra, $0000(sp)
// reset: disable AI spawn
lui t0, $002B
setreg t1, $104000dd
sw t1, $4A78(t0)
// reset: map events
lui t0, $002B
setreg t1, $1200000a
sw t1, $01E4(t0)
// SP BOOL
la t0, :SP_enabled_BOOL
sw zero, $0000(t0)
// set MP models
lui t0, $002B
setreg t1, $10400005
sw t1, $EA08(t0)
// mission event audio
lui t0, $0026
setreg t1, $1040000c
sw t1, $67F4(t0)
// mission event messages
lui t0, $0026
setreg t1, $27bdffe0
sw t1, $82D0(t0)
setreg t1, $3C010044
sw t1, $82D4(t0)
jal :_RestoreMPCode
nop
// original terrorist models 5-8
lui t0, $0042
setreg t1, $0041B4A0
sw t1, $B670(t0)
setreg t1, $0041B4B0
sw t1, $B674(t0)
setreg t1, $0041B4C0
sw t1, $B678(t0)
setreg t1, $0041B4D0
sw t1, $B67C(t0)

lw ra, $0000(sp)
addiu sp, sp, $10
// reset map models to default as SP map models corrupt the MP models
// set map models index to default so game loads correct player models for all maps
//lui t0, $0045
//addiu t1, zero, $1d4
//sw t1, $CD4C(t0)

jr ra
nop
//--------------------


//-------------------- player ARMOR
_editArmor:
// check if SP map
la t0, :SP_enabled_BOOL
lbu t0, $0000(t0)
beq t0, zero, :_editArmor_MP
nop

// s0 = pptr
// increment to player armor
addiu t1, s0, $0FFC
// edit player armor
lui t0, $4100 // 8.0
sw t0, $0000(t1)
lui t0, $41F0 // 30.0
sw t0, $0004(t1)
sw t0, $0008(t1)
lui t0, $4248 // 50.0
sw t0, $000C(t1)
lui t0, $41F0 // 30.0
sw t0, $0010(t1)
sw t0, $0014(t1)
lui t0, $4100 // 8.0
sw t0, $0018(t1)
lui t0, $41F0 // 30.0
sw t0, $001C(t1)
sw t0, $0020(t1)
lui t0, $4248 // 50.0
sw t0, $0024(t1)
lui t0, $41F0 // 30.0
sw t0, $0028(t1)
sw t0, $002C(t1)
sw zero, $0030(t1)
lui t0, $41C8 // 25.0
sw t0, $0034(t1)
sw t0, $0038(t1)
sw t0, $003C(t1)
sw t0, $0040(t1)
sw t0, $0044(t1)
// return
jr ra
nop

// original code
_editArmor_MP:
j $00195800
nop



//-------------------- 
// Returns default loadout string in v0 based on the map
_defaultArmory:
addiu sp, sp, $fff0
sw ra, $0000(sp)
// original code replaced by the hook
daddu s4, v0, zero

// check if SP map
la t0, :SP_enabled_BOOL
lbu t0, $0000(t0)
beq t0, zero, :_defaultArmory_end
nop

// enable armory weapons
jal :_EnableWeapons
nop

// set starting position of default armory to position 1 for all players
lw s4, $0008(s7)

_defaultArmory_end:
lw ra, $0000(sp)
// original "beq s4, zero, $002B1560" (its delay slot computes s2 = s4 + 0x20)
bne s4, zero, :_defaultArmory_ret
addiu s2, s4, $0020
lui ra, $002B
ori ra, ra, $1560
_defaultArmory_ret:
jr ra
addiu sp, sp, $10

_RestoreMPCode:
lui t0, $002B
setreg t1, $104000DD
sw t1, $4A78(t0)
lui t0, $0024
setreg t1, $24844DE8
sw t1, $BC00(t0)
setreg t1, $00123080
sw t1, $CB38(t0)
lui t0, $002B
setreg t1, $12400027
sw t1, $1328(t0)
jr ra
nop


_EnableWeapons:
// created by antix
// a0 string to send
addiu sp, sp, $fff0
sw ra, $0000(sp)
sw s0, $0004(sp)
sw v0, $0008(sp)
la s0, :_EnableWeapons_pointers
startCode:
lw a0, $0000(s0)
beq a0, zero, :endCode
nop
jal $0036F520
nop
beq v0, zero, :startCode
addiu s0, s0, $0004
addiu a0, zero, $0009
sw a0, $0004(v0)
beq zero, zero, :startCode
nop
endCode:
lw ra, $0000(sp)
lw s0, $0004(sp)
lw v0, $0008(sp)
jr ra
addiu sp, sp, $0010



_EnableWeapons_pointers:
hexcode $00428E68 //fnp
hexcode $00428E78 //m16
hexcode $00428EB8 //mp5sd
hexcode $00428EC8 //mp5
hexcode $00428ED8 //ak47
hexcode $00428EE8 //aks74
hexcode $00428F08 //spas
hexcode $00428F18 //m14
hexcode $00428F48 //uzi
hexcode $00428F60 //remington700
hexcode $00428F78 //m60
hexcode $00428F88 //m63a
hexcode $00428FA0 //sr25
hexcode $00428FC0 //m82
hexcode $00428FE0 //870
hexcode $00428FF0 //m87
hexcode $00429010 //m4a1
hexcode $00429028 //m4a1sd
hexcode $00429040 //sr25sd
hexcode $00429098 //552sd
hexcode $004290B0 //sasr
hexcode $004290C0 //iw80
hexcode $004290D0 //ak105
hexcode $004290F0 //mark23
hexcode $00429100 //mark23sd
hexcode $00429110 //226
hexcode $00429120 //hk
hexcode $00429130 //glock
hexcode $00429140 //57
hexcode $00429160 //m9
hexcode $00429180 //de
hexcode $004291C8 //mp5k
hexcode $004291D8 //2xammo
hexcode $004291F0 //therm_scope
hexcode $00429208 //frag
hexcode $00429218 //smoke
hexcode $00429260 //claymore
hexcode $00429270 //pmn
hexcode $00429280 //c4
hexcode $004292D0 //he frag
nop
//-------------------- 



//-------------------- check if SP map and apply respawn
_respawnCheckForSP:
// check if SP_enabled_bool is set
la t0, :SP_enabled_BOOL
lbu t0, $0000(t0)
// check SP bool
beq t0, zero, :_respawnCheckForSP_end
nop

// game stats pointer
lui t0, $0044
lw t0, $46F8(t0)
addiu t1, zero, $1
// store 1 to respawn bool
sb t1, $00E0(t0)

_respawnCheckForSP_end:
jr ra
nop

//-------------------- 



//-------------------- respawn points
_respawnPoints:
// 1. Check if SP map
// 2. If SP map, get spawn points index based on map ID
// 3. Load first by of map time(timer counts in ms) and use it to randomly
//    load 1 out of 10 spawn points.

//-- check if SP map
// get map string
lui t0, $0044
lw t0, $17C0(t0)



// check if m51
la t1, :_m51
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m51
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m52
la t1, :_m52
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m52
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m53
la t1, :_m53
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m53
beq t0, t1, :_respawnPoints_findRespawnPoint
nop


// check if m61
la t1, :_m61
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m61
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m62
la t1, :_m62
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m62
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m63
la t1, :_m63
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m63
beq t0, t1, :_respawnPoints_findRespawnPoint
nop


// check if m71
la t1, :_m71
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m71
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m72
la t1, :_m72
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m72
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m73
la t1, :_m73
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m73
beq t0, t1, :_respawnPoints_findRespawnPoint
nop


// check if m81
la t1, :_m81
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m81
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m82
la t1, :_m82
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m82
beq t0, t1, :_respawnPoints_findRespawnPoint
nop
// check if m83
la t1, :_m83
lw t1, $0000(t1)
// spawn index
la t2, :respawnPoints_m83
beq t0, t1, :_respawnPoints_findRespawnPoint
nop



// ELSE, MP MAP
beq zero, zero, :_respawnPoints_end
nop


_respawnPoints_findRespawnPoint:
// t2 = respawnPoint index for map
// load current map time
lui t3, $0044
// game stats pointer
lw t3, $46F8(t3)
// offset to game time address
lbu t3, $00EC(t3)
// check each index offset (note: time will almost never be 0, start at offset 10)
// make offset 0 the default spawn
// 10
addiu t4, zero, $10
// subtract offset from gameTime, if offset < 0 then use spawn point
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 20
addiu t4, zero, $20
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 30
addiu t4, zero, $30
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 40
addiu t4, zero, $40
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 50
addiu t4, zero, $50
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 60
addiu t4, zero, $60
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 70
addiu t4, zero, $70
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 80
addiu t4, zero, $80
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// 90
addiu t4, zero, $90
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// a0
addiu t4, zero, $a0
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// b0
addiu t4, zero, $b0
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// c0
addiu t4, zero, $c0
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// d0
addiu t4, zero, $d0
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// e0
addiu t4, zero, $e0
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop
// f0
addiu t4, zero, $f0
sub t5, t3, t4
bltz t5, :findRespawnPoint_applyIndex
nop

// else, set index to default position 0x0
daddu t4, zero, zero

findRespawnPoint_applyIndex:
// add t2 to t4 offset to get index
addu t4, t4, t2
// load XYZ and apply to stack pointer for respawn
lw t5, $0000(t4)
sw t5, $0030(a1)
lw t5, $0004(t4)
sw t5, $0034(a1)
lw t5, $0008(t4)
sw t5, $0038(a1)


_respawnPoints_end:
// original code
jr t9
nop



//-------------------- Terrorist model replacements
_TerrModel_check:
// get current map
lui t0, $0044
lw t0, $17C0(t0)
// set terrorist player model strings
lui t1, $0042


// m51
la t2, :_m51
lw t2, $0000(t2)
la t4, :m51_terror1
la t5, :m51_terror2
la t6, :m51_terror3
la t7, :m51_terror4
beq t0, t2, :_TerrModel_SP
nop
// m52
la t2, :_m52
lw t2, $0000(t2)
la t4, :m52_terror1
la t5, :m52_terror2
la t6, :m52_terror3
la t7, :m52_terror4
beq t0, t2, :_TerrModel_SP
nop
// m53
la t2, :_m53
lw t2, $0000(t2)
la t4, :m53_terror1
la t5, :m53_terror2
la t6, :m53_terror3
la t7, :m53_terror4
beq t0, t2, :_TerrModel_SP
nop

// m61
la t2, :_m61
lw t2, $0000(t2)
la t4, :m61_terror1
la t5, :m61_terror2
la t6, :m61_terror3
la t7, :m61_terror4
beq t0, t2, :_TerrModel_SP
nop
// m62
la t2, :_m62
lw t2, $0000(t2)
la t4, :m62_terror1
la t5, :m62_terror2
la t6, :m62_terror3
la t7, :m62_terror4
beq t0, t2, :_TerrModel_SP
nop
// m63
la t2, :_m63
lw t2, $0000(t2)
la t4, :m63_terror1
la t5, :m63_terror2
la t6, :m63_terror3
la t7, :m63_terror4
beq t0, t2, :_TerrModel_SP
nop

// m71
la t2, :_m71
lw t2, $0000(t2)
la t4, :m71_terror1
la t5, :m71_terror2
la t6, :m71_terror3
la t7, :m71_terror4
beq t0, t2, :_TerrModel_SP
nop
// m72
la t2, :_m72
lw t2, $0000(t2)
la t4, :m72_terror1
la t5, :m72_terror2
la t6, :m72_terror3
la t7, :m72_terror4
beq t0, t2, :_TerrModel_SP
nop
// m73
la t2, :_m73
lw t2, $0000(t2)
la t4, :m73_terror1
la t5, :m73_terror2
la t6, :m73_terror3
la t7, :m73_terror4
beq t0, t2, :_TerrModel_SP
nop

// m81
la t2, :_m81
lw t2, $0000(t2)
la t4, :m81_terror1
la t5, :m81_terror2
la t6, :m81_terror3
la t7, :m81_terror4
beq t0, t2, :_TerrModel_SP
nop
// m82
la t2, :_m82
lw t2, $0000(t2)
la t4, :m82_terror1
la t5, :m82_terror2
la t6, :m82_terror3
la t7, :m82_terror4
beq t0, t2, :_TerrModel_SP
nop
// m83
la t2, :_m83
lw t2, $0000(t2)
la t4, :m83_terror1
la t5, :m83_terror2
la t6, :m83_terror3
la t7, :m83_terror4
beq t0, t2, :_TerrModel_SP
nop

// more maps go here

// else, not a SP map
_TerrModel_MP:
// original models 5-8
setreg t4, $0041B4A0
sw t4, $B670(t1)
setreg t4, $0041B4B0
sw t4, $B674(t1)
setreg t4, $0041B4C0
sw t4, $B678(t1)
setreg t4, $0041B4D0
sw t4, $B67C(t1)
// disable SP map bool
la t0, :SP_enabled_BOOL
sw zero, $0000(t0)
// set MP models
lui t0, $002B
setreg t1, $10400005
sw t1, $EA08(t0)
// mission event audio
lui t0, $0026
setreg t1, $1040000c
sw t1, $67F4(t0)
// mission event messages
lui t0, $0026
setreg t1, $27bdffe0
sw t1, $82D0(t0)
setreg t1, $3C010044
sw t1, $82D4(t0)
// reset: map events
lui t0, $002B
setreg t1, $1200000a
sw t1, $01E4(t0)
// restore AI spawn, weapon-exchange code and MP map loading
lui t0, $002B
setreg t1, $104000DD
sw t1, $4A78(t0)
lui t0, $0024
setreg t1, $24844DE8
sw t1, $BC00(t0)
setreg t1, $00123080
sw t1, $CB38(t0)
lui t0, $002B
setreg t1, $12400027
sw t1, $1328(t0)

// end
beq zero, zero, :_TerrModel_end
nop

_TerrModel_SP:
// models 5-8
//-- test
//setreg t4, $003F00D0 // seal 3
//setreg t5, $003F00E0 // seal 4
//-- end test
sw t4, $B670(t1)
sw t5, $B674(t1)
sw t6, $B678(t1)
sw t7, $B67C(t1)
// enable SP map bool
la t0, :SP_enabled_BOOL
addiu t1, zero, $1
sb t1, $0000(t0)
// set MP models
lui t0, $002B
setreg t1, $10000005
sw t1, $EA08(t0)
// mission event audio
lui t0, $0026
sw zero, $67F4(t0)
// mission event messages
lui t0, $0026
setreg t1, $03E00008
sw t1, $82D0(t0)
setreg t1, $24020001
sw t1, $82D4(t0)
// disable AI spawn
lui t0, $002B
setreg t1, $100000dd
sw t1, $4A78(t0)
// disable map events
lui t0, $002B
setreg t1, $1000000a
sw t1, $01E4(t0)
// weapon exchange loop correction (workaround for weapon select freeze)
lui t0, $0024
sw zero, $BC00(t0)
//weapon exchange change column freeze correction
lui t0, $0024
setreg t1, $10000007
sw t1, $CB38(t0)
// disable help window
lui t0, $0043
lw t0, $57F0(t0)
sh zero, $0004(t0)


_TerrModel_end:
jr ra
// OG code
lbu v0, $D481(at)
//--------------------


//-------------------- check map ID CREATE GAME
//
// NOTE: mapID = SP_map_ID + 100
// ex: m53 = 153 (99 hex)
// Do this to seperate SP from MP maps as some have identical IDs.
_checkMapID_createGame:

// online check (must be forced for offline, not for online)
setreg t1, $002AEA08
// online check default data
setreg t2, $10400005


// m51
addiu t0, zero, $97 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $33 // real ID
// m52
addiu t0, zero, $98 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $34 // real ID
// m53
addiu t0, zero, $99 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $35 // real ID

// m61
addiu t0, zero, $A1 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $3D // real ID
// m62
addiu t0, zero, $A2 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $3E // real ID
// m63
addiu t0, zero, $A3 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $3F // real ID

// m71
addiu t0, zero, $AB // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $47 // real ID
// m72
addiu t0, zero, $AC // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $48 // real ID
// m73
addiu t0, zero, $AD // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $49 // real ID

// m81
addiu t0, zero, $b5 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $51 // real ID
// m82
addiu t0, zero, $b6 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $52 // real ID
// m83
addiu t0, zero, $b7 // ID used to identify SP map (real ID + 100)
beq a2, t0, :_checkMapID_SP_init
addiu t3, zero, $53 // real ID


// ELSE, MP MAP
_checkMapID_MP_init:
beq zero, zero, :_checkMapID_end
nop


_checkMapID_SP_init:
// move actual ID from t3 to a2
daddu a2, t3, zero
// SP map selected, set string type to "m%d"
la a1, :_checkMapID_SP_SETUP
// online check
setreg t2, $10000005
beq zero, zero, :_checkMapID_end
nop


_checkMapID_end:
// online check save
sw t2, $0000(t1)
// original jump
j $001988D0
nop
_checkMapID_SP_SETUP:
string "m%d"
nop
//-------------------- 



//-------------------- check map string on JoinGame
_checkMapType:
// online check (must be forced for offline, not for online)
setreg t3, $002AEA08
// a1 = map string
lw t1, $0000(a1)


// m51
la t2, :_m51
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m52
la t2, :_m52
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m53
la t2, :_m53
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop

// m61
la t2, :_m61
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m62
la t2, :_m62
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m63
la t2, :_m63
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop

// m71
la t2, :_m71
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m72
la t2, :_m72
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m73
la t2, :_m73
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop

// m81
la t2, :_m81
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m82
la t2, :_m82
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop
// m83
la t2, :_m83
lw t2, $0000(t2)
beq t1, t2, :_checkMapType_SP
nop

//--- ELSE, MP MAP
_checkMapType_MP:
// online check
setreg t2, $10400005
beq zero, zero, :_checkMapType_end
nop


_checkMapType_SP:
// online check
setreg t2, $10000005
beq zero, zero, :_checkMapType_end
nop

_checkMapType_end:
// save online check
sw t2, $0000(t3)


// original code
j $00199060
nop

//map_m53:
//print "m53"
//---------------------



//--------------------- edit player coords on spawn
_replaceXYZcoords:

// get map ID
lui t0, $0044
lw t0, $17C0(t0)

// check m51
la t1, :_m51
lw t1, $0000(t1)
beq t0, t1, :set_m51
nop
// check m52
la t1, :_m52
lw t1, $0000(t1)
beq t0, t1, :set_m52
nop
// check m53
la t1, :_m53
lw t1, $0000(t1)
beq t0, t1, :set_m53
nop


// check m61
la t1, :_m61
lw t1, $0000(t1)
beq t0, t1, :set_m61
nop
// check m62
la t1, :_m62
lw t1, $0000(t1)
beq t0, t1, :set_m62
nop
// check m63
la t1, :_m63
lw t1, $0000(t1)
beq t0, t1, :set_m63
nop


// check m71
la t1, :_m71
lw t1, $0000(t1)
beq t0, t1, :set_m71
nop
// check m72
la t1, :_m72
lw t1, $0000(t1)
beq t0, t1, :set_m72
nop
// check m73
la t1, :_m73
lw t1, $0000(t1)
beq t0, t1, :set_m73
nop


// check m81
la t1, :_m81
lw t1, $0000(t1)
beq t0, t1, :set_m81
nop
// check m82
la t1, :_m82
lw t1, $0000(t1)
beq t0, t1, :set_m82
nop
// check m83
la t1, :_m83
lw t1, $0000(t1)
beq t0, t1, :set_m83
nop

// else, no SP map
// reset respawn to default

// enable victory dance
lui t0, $022
addiu t1, zero, $40
sb t1, $FD66(t0)
// goto end
beq zero, zero, :_replaceXYZcoords_END
nop

//--- SET SP MAPS START

set_m51:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m51
nop
// SEAL
// x
setreg t0, $45A38413
sw t0, $0030(a1)
// y
setreg t0, $C36AFECC
sw t0, $0034(a1)
// z
setreg t0, $455FA1A4
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m51:
// x
setreg t0, $45AE7759
sw t0, $0030(a1)
// y
setreg t0, $C2A93399
sw t0, $0034(a1)
// z
setreg t0, $450A08F5
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m52:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m52
nop
// SEAL
// x
setreg t0, $456B17F4
sw t0, $0030(a1)
// y
setreg t0, $C1147000
sw t0, $0034(a1)
// z
setreg t0, $457F0139
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m52:
// x
setreg t0, $458B4B2D
sw t0, $0030(a1)
// y
setreg t0, $41E46000
sw t0, $0034(a1)
// z
setreg t0, $4534410C
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m53:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m53
nop
// SEAL
// x
setreg t0, $44C86922
sw t0, $0030(a1)
// y
setreg t0, $43F0F280
sw t0, $0034(a1)
// z
setreg t0, $4519B82F
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m53:
// x
setreg t0, $4517525E
sw t0, $0030(a1)
// y
setreg t0, $442F5F00
sw t0, $0034(a1)
// z
setreg t0, $448876BD
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m61:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m61
nop
// SEAL
// x
setreg t0, $43E8D407
sw t0, $0030(a1)
// y
setreg t0, $416E7000
sw t0, $0034(a1)
// z
setreg t0, $44531D14
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m61:
// x
setreg t0, $44115843
sw t0, $0030(a1)
// y
setreg t0, $C24FA800
sw t0, $0034(a1)
// z
setreg t0, $44F44618
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m62:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m62
nop
// SEAL
// x
setreg t0, $44A6216D
sw t0, $0030(a1)
// y
setreg t0, $42DCD000
sw t0, $0034(a1)
// z
setreg t0, $44AD29E2
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m62:
// x
setreg t0, $453C9C0A
sw t0, $0030(a1)
// y
setreg t0, $42D6A866
sw t0, $0034(a1)
// z
setreg t0, $45305218
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m63:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m63
nop
// SEAL
// x
setreg t0, $44A4022C
sw t0, $0030(a1)
// y
setreg t0, $3CE00000
sw t0, $0034(a1)
// z
setreg t0, $44FF839F
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m63:
// x
setreg t0, $45098C58
sw t0, $0030(a1)
// y
setreg t0, $C247FC00
sw t0, $0034(a1)
// z
setreg t0, $44CFB925
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m71:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m71
nop
// SEAL
// x
setreg t0, $44A8D553
sw t0, $0030(a1)
// y
setreg t0, $42CD9E00
sw t0, $0034(a1)
// z
setreg t0, $44B3B20F
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m71:
// x
setreg t0, $452056B6
sw t0, $0030(a1)
// y
setreg t0, $43486B00
sw t0, $0034(a1)
// z
setreg t0, $44C142DF
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)



set_m72:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m72
nop
// SEAL
// x
setreg t0, $44146910
sw t0, $0030(a1)
// y
setreg t0, $43610100
sw t0, $0034(a1)
// z
setreg t0, $44DFA5E7
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m72:
// x
setreg t0, $449FAC4E
sw t0, $0030(a1)
// y
setreg t0, $43610000
sw t0, $0034(a1)
// z
setreg t0, $44B14485
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m73:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m73
nop
// SEAL
// x
setreg t0, $44B300A9
sw t0, $0030(a1)
// y
setreg t0, $42660400
sw t0, $0034(a1)
// z
setreg t0, $44BE6D7B
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m73:
// x
setreg t0, $444A03AC
sw t0, $0030(a1)
// y
setreg t0, $42660400
sw t0, $0034(a1)
// z
setreg t0, $44D4C929
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m81:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m81
nop
// SEAL
// x
setreg t0, $450FCAD8
sw t0, $0030(a1)
// y
setreg t0, $00000000
sw t0, $0034(a1)
// z
setreg t0, $44BD663A
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m81:
// x
setreg t0, $44C4DA5A
sw t0, $0030(a1)
// y
setreg t0, $BF4CCCCD
sw t0, $0034(a1)
// z
setreg t0, $43E640D8
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m82:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m82
nop
// SEAL
// x
setreg t0, $445D0381
sw t0, $0030(a1)
// y
setreg t0, $BF4CCCCD
sw t0, $0034(a1)
// z
setreg t0, $45187C6F
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m82:
// x
setreg t0, $4510DEC5
sw t0, $0030(a1)
// y
setreg t0, $BF4CCCCD
sw t0, $0034(a1)
// z
setreg t0, $44D52265
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


set_m83:
// get team ID
lw t0, $00c8(a0)
setreg t1, $40000001
bne t0, t1, :_replaceXYZcoords_TERR_m83
nop
// SEAL
// x
setreg t0, $4530F74B
sw t0, $0030(a1)
// y
setreg t0, $42F40200
sw t0, $0034(a1)
// z
setreg t0, $44B32916
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)

_replaceXYZcoords_TERR_m83:
// x
setreg t0, $44AE5D9D
sw t0, $0030(a1)
// y
setreg t0, $42F40200
sw t0, $0034(a1)
// z
setreg t0, $44933142
beq zero, zero, :_replaceXYZcoords_setVariables
sw t0, $0038(a1)


_replaceXYZcoords_setVariables:
// enable respawn

// disable victory dance(causes freeze)
lui t0, $022
sb zero, $FD66(t0)


_replaceXYZcoords_END:
// original jump
jr t9
nop
//---------------------



















nop


// spawn point index

respawnPoints_m51:
// 0 -- offset
hexcode $45AAE259
hexcode $C2A93399
hexcode $450938DD
nop
// 10
hexcode $45AE5F62
hexcode $C2FCB800
hexcode $451B162A
nop
// 20
hexcode $45BDB5F1
hexcode $C303A7CC
hexcode $4529C986
nop
// 30
hexcode $45B3B12D
hexcode $C360BCCC
hexcode $4553414D
nop
// 40
hexcode $4594555B
hexcode $C2FF9199
hexcode $45552858
nop
// 50
hexcode $4595558D
hexcode $C12F4CCC
hexcode $451974CA
nop
// 60
hexcode $459A1BF9
hexcode $42460C00
hexcode $452D3E7C
nop
// 70
hexcode $45978BE4
hexcode $C1F98000
hexcode $4523F4F4
nop
// 80
hexcode $459B3444
hexcode $C228F733
hexcode $452DB6CD
nop
// 90
hexcode $4593CA2E
hexcode $C33780CC
hexcode $453D986D
nop
// 0 -- offset
hexcode $45AAE259
hexcode $C2A93399
hexcode $450938DD
nop
// 10
hexcode $45AE5F62
hexcode $C2FCB800
hexcode $451B162A
nop
// 20
hexcode $45BDB5F1
hexcode $C303A7CC
hexcode $4529C986
nop
// 30
hexcode $45B3B12D
hexcode $C360BCCC
hexcode $4553414D
nop
// 40
hexcode $4594555B
hexcode $C2FF9199
hexcode $45552858
nop
// 50
hexcode $4595558D
hexcode $C12F4CCC
hexcode $451974CA
nop
// 60
hexcode $459A1BF9
hexcode $42460C00
hexcode $452D3E7C
nop
// 70
hexcode $45978BE4
hexcode $C1F98000
hexcode $4523F4F4
nop
// 80
hexcode $459B3444
hexcode $C228F733
hexcode $452DB6CD
nop
// 90
hexcode $4593CA2E
hexcode $C33780CC
hexcode $453D986D
nop


respawnPoints_m52:
// 0 -- offset
hexcode $4585C374
hexcode $41E4D000
hexcode $452E0EBB
nop
// 10
hexcode $4578EA0B
hexcode $42CA2A00
hexcode $45361E84
nop
// 20
hexcode $4579AF04
hexcode $42CA2000
hexcode $455577B0
nop
// 30
hexcode $458E69EB
hexcode $42C93400
hexcode $4557BA81
nop
// 40
hexcode $45809E82
hexcode $425E0400
hexcode $45624519
nop
// 50
hexcode $458C053D
hexcode $C04A0000
hexcode $456C15AC
nop
// 60
hexcode $45989258
hexcode $C2089C00
hexcode $4561F23B
nop
// 70
hexcode $4596127A
hexcode $41ED2800
hexcode $453FB122
nop
// 80
hexcode $457D37BD
hexcode $41ED5800
hexcode $4530945B
nop
// 90
hexcode $4576AE7F
hexcode $C1CAC800
hexcode $457E2387
nop
// 0 -- offset
hexcode $4585C374
hexcode $41E4D000
hexcode $452E0EBB
nop
// 10
hexcode $4578EA0B
hexcode $42CA2A00
hexcode $45361E84
nop
// 20
hexcode $4579AF04
hexcode $42CA2000
hexcode $455577B0
nop
// 30
hexcode $458E69EB
hexcode $42C93400
hexcode $4557BA81
nop
// 40
hexcode $45809E82
hexcode $425E0400
hexcode $45624519
nop
// 50
hexcode $458C053D
hexcode $C04A0000
hexcode $456C15AC
nop
// 60
hexcode $45989258
hexcode $C2089C00
hexcode $4561F23B
nop
// 70
hexcode $4596127A
hexcode $41ED2800
hexcode $453FB122
nop
// 80
hexcode $457D37BD
hexcode $41ED5800
hexcode $4530945B
nop
// 90
hexcode $4576AE7F
hexcode $C1CAC800
hexcode $457E2387
nop


respawnPoints_m53:
// 0 -- offset
hexcode $44B99F1C
hexcode $44055780
hexcode $45058FD8
nop
// 10
hexcode $44B6C708
hexcode $4411AA40
hexcode $44C3937F
nop
// 20
hexcode $44E4A4B6
hexcode $441BE440
hexcode $44B5F180
nop
// 30
hexcode $450566F8
hexcode $44255AC0
hexcode $448DF556
nop
// 40
hexcode $45182FA0
hexcode $44301000
hexcode $44BFD50C
nop
// 50
hexcode $4515970F
hexcode $442EAEC0
hexcode $44D82719
nop
// 60
hexcode $45165468
hexcode $442BDB00
hexcode $45047C01
nop
// 70
hexcode $450550FB
hexcode $44073D80
hexcode $45162AC4
nop
// 80
hexcode $44DE452E
hexcode $44064340
hexcode $4504219B
nop
// 90
hexcode $44D48DA2
hexcode $43EF3000
hexcode $451A9E33
nop
// 0 -- offset
hexcode $44B99F1C
hexcode $44055780
hexcode $45058FD8
nop
// 10
hexcode $44B6C708
hexcode $4411AA40
hexcode $44C3937F
nop
// 20
hexcode $44E4A4B6
hexcode $441BE440
hexcode $44B5F180
nop
// 30
hexcode $450566F8
hexcode $44255AC0
hexcode $448DF556
nop
// 40
hexcode $45182FA0
hexcode $44301000
hexcode $44BFD50C
nop
// 50
hexcode $4515970F
hexcode $442EAEC0
hexcode $44D82719
nop
// 60
hexcode $45165468
hexcode $442BDB00
hexcode $45047C01
nop
// 70
hexcode $450550FB
hexcode $44073D80
hexcode $45162AC4
nop
// 80
hexcode $44DE452E
hexcode $44064340
hexcode $4504219B
nop
// 90
hexcode $44D48DA2
hexcode $43EF3000
hexcode $451A9E33
nop


respawnPoints_m61:
// 0 -- offset
hexcode $442EFB4E
hexcode $C2ADD400
hexcode $44F18699
nop
// 10
hexcode $44463398
hexcode $C2ADD000
hexcode $44DE95AB
nop
// 20
hexcode $43E96C92
hexcode $C23DA733
hexcode $449EC7E9
nop
// 30
hexcode $43E10B58
hexcode $4109C000
hexcode $448972C7
nop
// 40
hexcode $4436C9A8
hexcode $C0DDD999
hexcode $448B9307
nop
// 50
hexcode $442C9D76
hexcode $42509C00
hexcode $448C3338
nop
// 60
hexcode $44276345
hexcode $414DF000
hexcode $447A2E65
nop
// 70
hexcode $448A0207
hexcode $427928CD
hexcode $4426FB02
nop
// 80
hexcode $44AD4863
hexcode $4251B4CD
hexcode $44547530
nop
// 90
hexcode $445523D1
hexcode $C2162733
hexcode $4499A20E
nop
// 0 -- offset
hexcode $442EFB4E
hexcode $C2ADD400
hexcode $44F18699
nop
// 10
hexcode $44463398
hexcode $C2ADD000
hexcode $44DE95AB
nop
// 20
hexcode $43E96C92
hexcode $C23DA733
hexcode $449EC7E9
nop
// 30
hexcode $43E10B58
hexcode $4109C000
hexcode $448972C7
nop
// 40
hexcode $4436C9A8
hexcode $C0DDD999
hexcode $448B9307
nop
// 50
hexcode $442C9D76
hexcode $42509C00
hexcode $448C3338
nop
// 60
hexcode $44276345
hexcode $414DF000
hexcode $447A2E65
nop
// 70
hexcode $448A0207
hexcode $427928CD
hexcode $4426FB02
nop
// 80
hexcode $44AD4863
hexcode $4251B4CD
hexcode $44547530
nop
// 90
hexcode $445523D1
hexcode $C2162733
hexcode $4499A20E
nop



respawnPoints_m62:
// 0 -- offset
hexcode $44C4476E
hexcode $434CB633
hexcode $44E719A5
nop
// 10
hexcode $44CCD6F4
hexcode $432D1F33
hexcode $453036A2
nop
// 20
hexcode $44ED7D17
hexcode $43534A33
hexcode $45290515
nop
// 30
hexcode $4516656E
hexcode $43457F33
hexcode $4508ADBD
nop
// 40
hexcode $450B8835
hexcode $4389429A
hexcode $45293020
nop
// 50
hexcode $454CEBD6
hexcode $43492033
hexcode $4518B718
nop
// 60
hexcode $453392B1
hexcode $4327DE33
hexcode $45310280
nop
// 70
hexcode $45413830
hexcode $43817E1A
hexcode $4510B6C0
nop
// 80
hexcode $454CA539
hexcode $42FBA666
hexcode $4503223D
nop
// 90
hexcode $450B02CA
hexcode $43429233
hexcode $4509C5AD
nop
// 0 -- offset
hexcode $44C4476E
hexcode $434CB633
hexcode $44E719A5
nop
// 10
hexcode $44CCD6F4
hexcode $432D1F33
hexcode $453036A2
nop
// 20
hexcode $44ED7D17
hexcode $43534A33
hexcode $45290515
nop
// 30
hexcode $4516656E
hexcode $43457F33
hexcode $4508ADBD
nop
// 40
hexcode $450B8835
hexcode $4389429A
hexcode $45293020
nop
// 50
hexcode $454CEBD6
hexcode $43492033
hexcode $4518B718
nop
// 60
hexcode $453392B1
hexcode $4327DE33
hexcode $45310280
nop
// 70
hexcode $45413830
hexcode $43817E1A
hexcode $4510B6C0
nop
// 80
hexcode $454CA539
hexcode $42FBA666
hexcode $4503223D
nop
// 90
hexcode $450B02CA
hexcode $43429233
hexcode $4509C5AD
nop



respawnPoints_m63:
// 0 -- offset
hexcode $45055372
hexcode $C247FC00
hexcode $44E9A5BF
nop
// 10
hexcode $45027060
hexcode $00000000
hexcode $4502C1E9
nop
// 20
hexcode $44EA98B6
hexcode $42A00200
hexcode $44FABFAF
nop
// 30
hexcode $44C2C275
hexcode $41401000
hexcode $44FBA49C
nop
// 40
hexcode $44A2647B
hexcode $3FA10000
hexcode $450507F2
nop
// 50
hexcode $44C7B26F
hexcode $3F483333
hexcode $45479B41
nop
// 60
hexcode $4501141F
hexcode $00000000
hexcode $451B4657
nop
// 70
hexcode $44D61C99
hexcode $00000000
hexcode $45109E4D
nop
// 80
hexcode $44D9B075
hexcode $BFBF0000
hexcode $44FFC7DB
nop
// 90
hexcode $44B4705A
hexcode $41401000
hexcode $44F71A13
nop
// 0 -- offset
hexcode $45055372
hexcode $C247FC00
hexcode $44E9A5BF
nop
// 10
hexcode $45027060
hexcode $00000000
hexcode $4502C1E9
nop
// 20
hexcode $44EA98B6
hexcode $42A00200
hexcode $44FABFAF
nop
// 30
hexcode $44C2C275
hexcode $41401000
hexcode $44FBA49C
nop
// 40
hexcode $44A2647B
hexcode $3FA10000
hexcode $450507F2
nop
// 50
hexcode $44C7B26F
hexcode $3F483333
hexcode $45479B41
nop
// 60
hexcode $4501141F
hexcode $00000000
hexcode $451B4657
nop
// 70
hexcode $44D61C99
hexcode $00000000
hexcode $45109E4D
nop
// 80
hexcode $44D9B075
hexcode $BFBF0000
hexcode $44FFC7DB
nop
// 90
hexcode $44B4705A
hexcode $41401000
hexcode $44F71A13
nop


respawnPoints_m71:
// 0 -- offset
hexcode $44B0AE1F
hexcode $430BC500
hexcode $44C2DCF3
nop
// 10
hexcode $44E04290
hexcode $4337EF00
hexcode $44DF0331
nop
// 20
hexcode $44D1D172
hexcode $43443800
hexcode $44D895F6
nop
// 30
hexcode $44CA220C
hexcode $432E8F00
hexcode $44B01909
nop
// 40
hexcode $44C5754C
hexcode $436C7800
hexcode $448BEE86
nop
// 50
hexcode $44E64458
hexcode $435D8E00
hexcode $448F5E50
nop
// 60
hexcode $44E9D4DC
hexcode $43248E00
hexcode $4492A909
nop
// 70
hexcode $4510B8C3
hexcode $43480100
hexcode $448A248F
nop
// 80
hexcode $44DD3C4C
hexcode $43448F00
hexcode $44EB1F93
nop
// 90
hexcode $45096232
hexcode $434F8000
hexcode $44C4672C
nop
// 0 -- offset
hexcode $44B0AE1F
hexcode $430BC500
hexcode $44C2DCF3
nop
// 10
hexcode $44E04290
hexcode $4337EF00
hexcode $44DF0331
nop
// 20
hexcode $44D1D172
hexcode $43443800
hexcode $44D895F6
nop
// 30
hexcode $44CA220C
hexcode $432E8F00
hexcode $44B01909
nop
// 40
hexcode $44C5754C
hexcode $436C7800
hexcode $448BEE86
nop
// 50
hexcode $44E64458
hexcode $435D8E00
hexcode $448F5E50
nop
// 60
hexcode $44E9D4DC
hexcode $43248E00
hexcode $4492A909
nop
// 70
hexcode $4510B8C3
hexcode $43480100
hexcode $448A248F
nop
// 80
hexcode $44DD3C4C
hexcode $43448F00
hexcode $44EB1F93
nop
// 90
hexcode $45096232
hexcode $434F8000
hexcode $44C4672C
nop



respawnPoints_m72:
// 0 -- offset
hexcode $440A45F1
hexcode $43638000
hexcode $44BE7904
nop
// 10
hexcode $44A033C1
hexcode $43610000
hexcode $44B13B90
nop
// 20
hexcode $449FAC4E
hexcode $43610000
hexcode $44B14485
nop
// 30
hexcode $44146910
hexcode $43610100
hexcode $44DFA5E7
nop
// 40
hexcode $44B08782
hexcode $43638000
hexcode $44F22594
nop
// 50
hexcode $44A284B1
hexcode $43870080
hexcode $4501485D
nop
// 60
hexcode $44449E19
hexcode $4306FF00
hexcode $44A8296A
nop
// 70
hexcode $43F08BD3
hexcode $42B40200
hexcode $44138BBD
nop
// 80
hexcode $443B812E
hexcode $42B94800
hexcode $446B8384
nop
// 90
hexcode $44598933
hexcode $43638100
hexcode $44EB2DCE
nop
// 0 -- offset
hexcode $440A45F1
hexcode $43638000
hexcode $44BE7904
nop
// 10
hexcode $44A033C1
hexcode $43610000
hexcode $44B13B90
nop
// 20
hexcode $449FAC4E
hexcode $43610000
hexcode $44B14485
nop
// 30
hexcode $44146910
hexcode $43610100
hexcode $44DFA5E7
nop
// 40
hexcode $44B08782
hexcode $43638000
hexcode $44F22594
nop
// 50
hexcode $44A284B1
hexcode $43870080
hexcode $4501485D
nop
// 60
hexcode $44449E19
hexcode $4306FF00
hexcode $44A8296A
nop
// 70
hexcode $43F08BD3
hexcode $42B40200
hexcode $44138BBD
nop
// 80
hexcode $443B812E
hexcode $42B94800
hexcode $446B8384
nop
// 90
hexcode $44598933
hexcode $43638100
hexcode $44EB2DCE
nop


respawnPoints_m73:
// 0 -- offset
hexcode $44B300A9
hexcode $42660400
hexcode $44BE6D7B
nop
// 10
hexcode $444A03AC
hexcode $42660400
hexcode $44D4C929
nop
// 20
hexcode $44802527
hexcode $42660000
hexcode $44F16F73
nop
// 30
hexcode $449706F4
hexcode $42CD0000
hexcode $44F1BBF6
nop
// 40
hexcode $445D3013
hexcode $42CD0200
hexcode $44E9166A
nop
// 50
hexcode $4499C5E2
hexcode $43278100
hexcode $44FA8445
nop
// 60
hexcode $4463DB6B
hexcode $43138100
hexcode $44E8D638
nop
// 70
hexcode $44920D8F
hexcode $42A00400
hexcode $44FF2A5B
nop
// 80
hexcode $448FEC60
hexcode $40204000
hexcode $44B9D456
nop
// 90
hexcode $4479E234
hexcode $40204000
hexcode $44DA746B
nop
// 0 -- offset
hexcode $44B300A9
hexcode $42660400
hexcode $44BE6D7B
nop
// 10
hexcode $444A03AC
hexcode $42660400
hexcode $44D4C929
nop
// 20
hexcode $44802527
hexcode $42660000
hexcode $44F16F73
nop
// 30
hexcode $449706F4
hexcode $42CD0000
hexcode $44F1BBF6
nop
// 40
hexcode $445D3013
hexcode $42CD0200
hexcode $44E9166A
nop
// 50
hexcode $4499C5E2
hexcode $43278100
hexcode $44FA8445
nop
// 60
hexcode $4463DB6B
hexcode $43138100
hexcode $44E8D638
nop
// 70
hexcode $44920D8F
hexcode $42A00400
hexcode $44FF2A5B
nop
// 80
hexcode $448FEC60
hexcode $40204000
hexcode $44B9D456
nop
// 90
hexcode $4479E234
hexcode $40204000
hexcode $44DA746B
nop


respawnPoints_m81:
// 0 -- offset
hexcode $44CD34E7
hexcode $BF4CCCCD
hexcode $43DABF4A
nop
// 10
hexcode $44CCD9CA
hexcode $00000000
hexcode $446A1F1D
nop
// 20
hexcode $45007564
hexcode $BF4CCCCD
hexcode $447DE7E3
nop
// 30
hexcode $45117B0F
hexcode $41137333
hexcode $44A09ED4
nop
// 40
hexcode $45216506
hexcode $BF4CCCCD
hexcode $4488AB79
nop
// 50
hexcode $4513D13B
hexcode $BF3ECCCD
hexcode $44389945
nop
// 60
hexcode $44F1DAB0
hexcode $4192D99A
hexcode $43AC4413
nop
// 70
hexcode $45015F7A
hexcode $40000000
hexcode $444BCEB3
nop
// 80
hexcode $44D5B413
hexcode $00000000
hexcode $44A9A854
nop
// 90
hexcode $4507FDE8
hexcode $C18CA666
hexcode $44C933C8
nop
// 0 -- offset
hexcode $44CD34E7
hexcode $BF4CCCCD
hexcode $43DABF4A
nop
// 10
hexcode $44CCD9CA
hexcode $00000000
hexcode $446A1F1D
nop
// 20
hexcode $45007564
hexcode $BF4CCCCD
hexcode $447DE7E3
nop
// 30
hexcode $45117B0F
hexcode $41137333
hexcode $44A09ED4
nop
// 40
hexcode $45216506
hexcode $BF4CCCCD
hexcode $4488AB79
nop
// 50
hexcode $4513D13B
hexcode $BF3ECCCD
hexcode $44389945
nop
// 60
hexcode $44F1DAB0
hexcode $4192D99A
hexcode $43AC4413
nop
// 70
hexcode $45015F7A
hexcode $40000000
hexcode $444BCEB3
nop
// 80
hexcode $44D5B413
hexcode $00000000
hexcode $44A9A854
nop
// 90
hexcode $4507FDE8
hexcode $C18CA666
hexcode $44C933C8
nop


respawnPoints_m82:
// 0 -- offset
hexcode $4510DEC5
hexcode $BF4CCCCD
hexcode $44D52265
nop
// 10
hexcode $44838D91
hexcode $426DC8CD
hexcode $44DC138C
nop
// 20
hexcode $4440A3F0
hexcode $42B9E266
hexcode $44B4F1C3
nop
// 30
hexcode $44371293
hexcode $BF4CCCCD
hexcode $4463692F
nop
// 40
hexcode $440C525A
hexcode $C173DCCC
hexcode $44D3546A
nop
// 50
hexcode $4458CDBC
hexcode $C06AF333
hexcode $44F59CB6
nop
// 60
hexcode $4492C004
hexcode $C1A65E66
hexcode $4501A1FF
nop
// 70
hexcode $44A63DD1
hexcode $420364CD
hexcode $44CA1753
nop
// 80
hexcode $445E546D
hexcode $00000000
hexcode $45100542
nop
// 90
hexcode $4486F2FA
hexcode $C1372CCC
hexcode $451FB6D4
nop
// 0 -- offset
hexcode $4510DEC5
hexcode $BF4CCCCD
hexcode $44D52265
nop
// 10
hexcode $44838D91
hexcode $426DC8CD
hexcode $44DC138C
nop
// 20
hexcode $4440A3F0
hexcode $42B9E266
hexcode $44B4F1C3
nop
// 30
hexcode $44371293
hexcode $BF4CCCCD
hexcode $4463692F
nop
// 40
hexcode $440C525A
hexcode $C173DCCC
hexcode $44D3546A
nop
// 50
hexcode $4458CDBC
hexcode $C06AF333
hexcode $44F59CB6
nop
// 60
hexcode $4492C004
hexcode $C1A65E66
hexcode $4501A1FF
nop
// 70
hexcode $44A63DD1
hexcode $420364CD
hexcode $44CA1753
nop
// 80
hexcode $445E546D
hexcode $00000000
hexcode $45100542
nop
// 90
hexcode $4486F2FA
hexcode $C1372CCC
hexcode $451FB6D4
nop



respawnPoints_m83:
// 0 -- offset
hexcode $450619C5
hexcode $42A00200
hexcode $44BAFAA3
nop
// 10
hexcode $452547AD
hexcode $42F40000
hexcode $4491C8C4
nop
// 20
hexcode $4514E9A1
hexcode $42F40000
hexcode $44900BB1
nop
// 30
hexcode $44EC528B
hexcode $42F40000
hexcode $4490EA82
nop
// 40
hexcode $44EF84AB
hexcode $42F40000
hexcode $44BC7F3D
nop
// 50
hexcode $450382D1
hexcode $42F40000
hexcode $44BBE7DC
nop
// 60
hexcode $44DD085B
hexcode $42F40000
hexcode $44B93473
nop
// 70
hexcode $4518F8EC
hexcode $42A00200
hexcode $44908696
nop
// 80
hexcode $4530FE41
hexcode $42A00200
hexcode $4496763F
nop
// 90
hexcode $44B896DF
hexcode $42A00200
hexcode $44B9082F
nop
// 0 -- offset
hexcode $450619C5
hexcode $42A00200
hexcode $44BAFAA3
nop
// 10
hexcode $452547AD
hexcode $42F40000
hexcode $4491C8C4
nop
// 20
hexcode $4514E9A1
hexcode $42F40000
hexcode $44900BB1
nop
// 30
hexcode $44EC528B
hexcode $42F40000
hexcode $4490EA82
nop
// 40
hexcode $44EF84AB
hexcode $42F40000
hexcode $44BC7F3D
nop
// 50
hexcode $450382D1
hexcode $42F40000
hexcode $44BBE7DC
nop
// 60
hexcode $44DD085B
hexcode $42F40000
hexcode $44B93473
nop
// 70
hexcode $4518F8EC
hexcode $42A00200
hexcode $44908696
nop
// 80
hexcode $4530FE41
hexcode $42A00200
hexcode $4496763F
nop
// 90
hexcode $44B896DF
hexcode $42A00200
hexcode $44B9082F
nop


//-------------------- 

nop




//--------------------- IsSinglePlayerMapEnabled BOOL
SP_enabled_BOOL:
nop



//--------------------- map IDs
_m51:
string "m51"
nop
_m52:
string "m52"
nop
_m53:
string "m53"
nop
_m61:
string "m61"
nop
_m62:
string "m62"
nop
_m63:
string "m63"
nop
_m71:
string "m71"
nop
_m72:
string "m72"
nop
_m73:
string "m73"
nop
_m81:
string "m81"
nop
_m82:
string "m82"
nop
_m83:
string "m83"
nop
//---------------------


//--------------------- player models for terrorist

// CHAOS
m51_terror1:
string "m51_terror01"
nop
m51_terror2:
string "m51_terror02"
nop
m51_terror3:
string "m51_foreman"
nop
m51_terror4:
string "m51_foreman"
nop

// TERMINAL
m52_terror1:
string "m52_terror01"
nop
m52_terror2:
string "m52_terror02"
nop
m52_terror3:
string "m52_Rugova"
nop
m52_terror4:
string "m52_Rugova"
nop

// UPLAND
m53_terror1:
string "m53_terror01"
nop
m53_terror2:
string "m53_terror02"
nop
m53_terror3:
string "m53_Aide"
nop
m53_terror4:
string "m53_Castrioti"
nop

// URBAN
m61_terror1:
string "m61_terror01"
nop
m61_terror2:
string "m61_terror02"
nop
m61_terror3:
string "m61_terror03"
nop
m61_terror4:
string "m61_terror03"
nop

// STRANGLE
m62_terror1:
string "m62_terror01"
nop
m62_terror2:
string "m62_terror02"
nop
m62_terror3:
string "m62_terror03"
nop
m62_terror4:
string "m62_terror03"
nop

// HYDRO
m63_terror1:
string "M63_terror01"
nop
m63_terror2:
string "M63_terror02"
nop
m63_terror3:
string "M63_Quixada"
nop
m63_terror4:
string "M63_Trigger"
nop

// GUARDIAN
m71_terror1:
string "m71_terror01"
nop
m71_terror2:
string "m71_terror02"
nop
m71_terror3:
string "m71_terror03"
nop
m71_terror4:
string "m71_terror03"
nop

// SERVE
m72_terror1:
string "m72_terror01"
nop
m72_terror2:
string "m72_terror02"
nop
m72_terror3:
string "m72_officer"
nop
m72_terror4:
string "m72_officer"
nop

// TIDE
m73_terror1:
string "m73_terror01"
nop
m73_terror2:
string "m73_terror02"
nop
m73_terror3:
string "m73_terror03"
nop
m73_terror4:
string "m73_terror04"
nop

// LOCKDOWN
m81_terror1:
string "m81_terror01"
nop
m81_terror2:
string "m81_terror02"
nop
m81_terror3:
string "m81_terror03"
nop
m81_terror4:
string "m81_officer"
nop

// GUIDED
m82_terror1:
string "m82_terror01"
nop
m82_terror2:
string "m82_terror02"
nop
m82_terror3:
string "m82_terror03"
nop
m82_terror4:
string "m82_officer"
nop

// DELIVERY
m83_terror1:
string "m83_terror01"
nop
m83_terror2:
string "m83_terror02"
nop
m83_terror3:
string "m83_valeska"
nop
m83_terror4:
string "russian_Arjan"
nop


//---------------------






nop

////////////////////////////////////////////////////////////////////////////////
//                 Patches
////////////////////////////////////////////////////////////////////////////////
// Armory selection fix
address $002836A0
hexcode $10000003
address $00283680
nop
address $00283698
nop
address $002D4228
nop
// All weapons in game
address $002D25B0
addiu v0, zero, $1
// Map name fix
address $002FEF20
lw a1, $0094(s4)
// Gametype display in scoreboard (replaces "GAMETYPE ERROR02")
address $00410C90
string "RESPAWN"
// Disable MPEG videos
address $0027BC7C
hexcode $1000007E

////////////////////////////////////////////////////////////////////////////////
//                 Function hooks
////////////////////////////////////////////////////////////////////////////////
// Create Game map list
address $00275850
j :list_hook
// replace xyz coords on spawn
address $002B99AC
jal :_replaceXYZcoords
// level name copy (joining clients) -> SP/MP online check
address $00280B28
j :_checkMapType
// level name build: all three mp%d sites
address $0027A620
jal :_checkMapID_createGame
address $003158F0
jal :_checkMapID_createGame
address $00321244
jal :_checkMapID_createGame
// Terrorist team model edit + SP/MP mode switching
address $002AEA00
jal :_TerrModel_check
lui at, $0046
// enable respawn on SP maps
address $002A8D98
j :_respawnCheckForSP
// respawn points
address $0059EC3C
jal :_respawnPoints
// default weapons on SP maps
address $002B1504
jal :_defaultArmory
nop
// player armor to match MP
address $00559D58
jal :_editArmor
// reset SP variables when creating/joining a game
address $0027F844
j :_ResetSP
// null default weapon check
address $00543900
jal :_CheckWeaponID
