// SOCOM II (SCUS-97275, r0004) - snaplines + skeleton ESP using the game's Draw2DLine
//
// Hook: CHUD::Draw (0x00216770) ends with `jal 0x00215C10` (its HUD text pass) at 0x00216DA8.
// That jal becomes `jal esp_hook`; the hook runs the text pass itself, then draws, then returns.
// Drawing after the text pass matters: font_text_01's texture is only valid in VRAM then
// (names drawn earlier, at the DrawAutoReticle call, came out as garbage glyphs).
// The local seal comes from CHUD::Draw's s3.
//
// No VU0: an earlier version that called sceVu0CopyMatrix / TransformPoints4 from this hook
// crashed the PCSX2 debug build within seconds. Everything here is plain FPU.
// Install with the VM paused (PINE writes race the recompiler).
//
// Mailbox:
//   0x000E0000  bit0 = snaplines, bit1 = skeletons, bit2 = 2D boxes, bit3 = name + distance (0 = off, 15 = all)
//   0x000E0004  1 = include teammates
//   0x000E0008  line counter (scratch; capped at 300 per frame)
//   0x000E000C  enemies labelled this frame (max 8; two pool slots each)
//   0x000E3100 text buffer, 0x000E3140 format string ("[%d.%dm]")
//   0x000E0010  colour r,g,b,a (floats 0..1)
//   0x000E0040 / 0x000E0050  line endpoints (vec4 scratch); 0x000E0060 box min/max x,y; 0x000E0070 vec4 scratch
//
// Structures (see RENDER.md):
//   seal ZArray 0x00442CE8 {count, begin, end}; iterator {next, prev, data}; end sentinel 0x00442CEC
//   seal +0x1C origin, +0x28 CNode* (matrix at +0x00), +0xC8 team, +0x1044 health,
//        +0x2E8 CZBodyPart*[32]; bone +0x00 origin, +0x1C parent, +0x20 quat (x,y,z,w)
//   W2S matrix = [[[0x0045F750] + 0x128] + 0x3F0] + 0x20; pixel = (x/w - 1728, y/w - 1824)

address $000E0000
esp_mode:
    hexcode $0000000F
esp_team:
    hexcode $00000000
esp_count:
    hexcode $00000000
    hexcode $00000000
esp_color:
    hexcode $3F800000
    hexcode $00000000
    hexcode $00000000
    hexcode $3F800000

address $000E3140
esp_fmt:
    hexcode $2E64255B                  // "[%d.%dm]"
    hexcode $5D6D6425
    hexcode $00000000

address $000ED000
esp_hook:
    addiu $sp, $sp, -160
    sq    $s0, $0000($sp)
    sq    $s1, $0010($sp)
    sq    $s2, $0020($sp)
    sq    $s3, $0030($sp)
    sq    $s4, $0040($sp)
    sq    $s5, $0050($sp)
    sq    $s6, $0060($sp)
    sq    $s7, $0070($sp)
    sd    $ra, $0080($sp)
    jal   $00215C10                    // the game's own HUD text pass first (binds font_text_01)
    nop
    lui   $t0, $000E
    sw    $zero, $000C($t0)            // reset name slot counter
    lui   $t1, $0043
    lw    $t1, $5818($t1)              // HUD object
    beq   $t1, $zero, :hide_block
    addiu $t1, $t1, $0F98              // first borrowed pool slot (pool index 12)
    lw    $t3, $000C($t1)              // guard: must really be a C2DString
    lui   $t4, $0043
    ori   $t4, $t4, $3690
    bne   $t3, $t4, :hide_block
    addiu $t2, $zero, 16
hide_loop:
    lbu   $t3, $0014($t1)              // clear visible bit 6
    andi  $t3, $t3, $00BF
    sb    $t3, $0014($t1)
    addiu $t2, $t2, -1
    bne   $t2, $zero, :hide_loop
    addiu $t1, $t1, $0098
    b     :hide_done
    nop
hide_block:
    lui   $t0, $000E                   // no usable pool: mark all name slots as used
    addiu $t1, $zero, 8
    sw    $t1, $000C($t0)
hide_done:
    lui   $t0, $000E
    lw    $t1, $0000($t0)
    beq   $t1, $zero, :esp_done
    sw    $zero, $0008($t0)            // reset line counter
    beq   $s3, $zero, :esp_done
    move  $s6, $s3                     // s6 = local seal (CHUD::Draw keeps it in s3)
    lui   $t0, $0046
    lw    $t0, -2224($t0)              // [0x0045F750] world
    beq   $t0, $zero, :esp_done
    nop
    lw    $t0, $0128($t0)              // camera
    beq   $t0, $zero, :esp_done
    nop
    lw    $t0, $03F0($t0)              // scratch
    beq   $t0, $zero, :esp_done
    nop
    addiu $s5, $t0, $0020              // s5 = W2S matrix
    lui   $t0, $0044
    lw    $s0, $2CEC($t0)              // first iterator

esp_next:
    lui   $t0, $0044
    ori   $t0, $t0, $2CEC
    beq   $s0, $t0, :esp_done          // sentinel
    nop
    beq   $s0, $zero, :esp_done
    nop
    lw    $s1, $0008($s0)              // s1 = seal
    lw    $s0, $0000($s0)              // advance now
    beq   $s1, $zero, :esp_next
    nop
    beq   $s1, $s6, :esp_next
    nop
    lw    $t1, $00C8($s1)              // team
    lui   $t2, $0800
    and   $t3, $t1, $t2
    bne   $t3, $zero, :esp_next        // turrets (0x48000000)
    nop
    lui   $t0, $000E
    lw    $t3, $0004($t0)
    bne   $t3, $zero, :esp_team_ok
    nop
    lw    $t2, $00C8($s6)
    xor   $t3, $t1, $t2
    srl   $t3, $t3, 31
    beq   $t3, $zero, :esp_next        // same side
    nop
esp_team_ok:
    lwc1  $f0, $1044($s1)              // health
    mtc1  $zero, $f1
    c.le.s $f0, $f1
    bc1t  :esp_next
    nop

    // cull: project the origin; skip enemies that are off screen
    lwc1  $f6, $001C($s1)
    lwc1  $f7, $0020($s1)
    lwc1  $f8, $0024($s1)
    lui   $a0, $000E
    jal   :proj
    ori   $a0, $a0, $0040
    beq   $v0, $zero, :esp_next
    nop
    lui   $t0, $000E                   // seed the box bounds with the origin
    lw    $t1, $0040($t0)
    sw    $t1, $0060($t0)
    sw    $t1, $0064($t0)
    lw    $t1, $0044($t0)
    sw    $t1, $0068($t0)
    sw    $t1, $006C($t0)

    lui   $t0, $000E
    lw    $t1, $0000($t0)
    andi  $t1, $t1, 1
    beq   $t1, $zero, :esp_skel
    nop
    lui   $a0, $000E                   // snapline: screen bottom-centre -> origin
    lui   $t0, $43A0
    sw    $t0, $0050($a0)
    lui   $t0, $43E0
    sw    $t0, $0054($a0)
    sw    $zero, $0058($a0)
    lui   $t0, $3F80
    jal   :draw_ln
    sw    $t0, $005C($a0)

esp_skel:
    lui   $t0, $000E
    lw    $t1, $0000($t0)
    andi  $t1, $t1, 2
    beq   $t1, $zero, :esp_box
    nop
    lw    $s4, $0028($s1)              // s4 = CNode* (matrix)
    beq   $s4, $zero, :esp_box
    nop
    addiu $s2, $zero, 1                // bone index (skip root)
esp_bone:
    sll   $t0, $s2, 2
    addu  $t0, $t0, $s1
    lw    $s3, $02E8($t0)              // bone
    beq   $s3, $zero, :esp_bone_next
    nop
    lw    $s7, $001C($s3)              // parent
    beq   $s7, $zero, :esp_bone_next
    nop
    move  $a0, $s3
    jal   :bone_w
    move  $a1, $s4
    lui   $a0, $000E
    jal   :proj
    ori   $a0, $a0, $0040
    beq   $v0, $zero, :esp_bone_next
    nop
    move  $a0, $s7
    jal   :bone_w
    move  $a1, $s4
    lui   $a0, $000E
    jal   :proj
    ori   $a0, $a0, $0050
    beq   $v0, $zero, :esp_bone_next
    nop
    jal   :draw_ln
    nop
esp_bone_next:
    addiu $s2, $s2, 1
    slti  $t0, $s2, 32
    bne   $t0, $zero, :esp_bone
    nop

    // 2D box: min/max of every projected bone (plus the origin), 4 lines
esp_box:
    lui   $t0, $000E
    lw    $t1, $0000($t0)
    andi  $t1, $t1, 4
    beq   $t1, $zero, :esp_name
    nop
    lw    $s4, $0028($s1)
    beq   $s4, $zero, :esp_name
    nop
    move  $s2, $zero
eb_bone:
    sll   $t0, $s2, 2
    addu  $t0, $t0, $s1
    lw    $s3, $02E8($t0)
    beq   $s3, $zero, :eb_next
    nop
    move  $a0, $s3
    jal   :bone_w
    move  $a1, $s4
    lui   $a0, $000E
    jal   :proj
    ori   $a0, $a0, $0070
    beq   $v0, $zero, :eb_next
    nop
    lui   $t0, $000E
    lwc1  $f2, $0070($t0)              // x
    lwc1  $f3, $0074($t0)              // y
    lwc1  $f4, $0060($t0)              // min x
    c.lt.s $f2, $f4
    bc1f  :eb_maxx
    nop
    swc1  $f2, $0060($t0)
eb_maxx:
    lwc1  $f4, $0064($t0)              // max x
    c.lt.s $f4, $f2
    bc1f  :eb_miny
    nop
    swc1  $f2, $0064($t0)
eb_miny:
    lwc1  $f4, $0068($t0)              // min y
    c.lt.s $f3, $f4
    bc1f  :eb_maxy
    nop
    swc1  $f3, $0068($t0)
eb_maxy:
    lwc1  $f4, $006C($t0)              // max y
    c.lt.s $f4, $f3
    bc1f  :eb_next
    nop
    swc1  $f3, $006C($t0)
eb_next:
    addiu $s2, $s2, 1
    slti  $t0, $s2, 32
    bne   $t0, $zero, :eb_bone
    nop

    lui   $t0, $000E
    sw    $zero, $0048($t0)
    sw    $zero, $0058($t0)
    lui   $t1, $3F80
    sw    $t1, $004C($t0)
    sw    $t1, $005C($t0)
    lw    $t1, $0060($t0)              // top: (minx,miny) -> (maxx,miny)
    sw    $t1, $0040($t0)
    lw    $t2, $0068($t0)
    sw    $t2, $0044($t0)
    lw    $t1, $0064($t0)
    sw    $t1, $0050($t0)
    jal   :draw_ln
    sw    $t2, $0054($t0)
    lui   $t0, $000E                   // right: (maxx,miny) -> (maxx,maxy)
    lw    $t1, $0064($t0)
    sw    $t1, $0040($t0)
    lw    $t2, $0068($t0)
    sw    $t2, $0044($t0)
    sw    $t1, $0050($t0)
    lw    $t2, $006C($t0)
    jal   :draw_ln
    sw    $t2, $0054($t0)
    lui   $t0, $000E                   // bottom: (maxx,maxy) -> (minx,maxy)
    lw    $t1, $0064($t0)
    sw    $t1, $0040($t0)
    lw    $t2, $006C($t0)
    sw    $t2, $0044($t0)
    lw    $t1, $0060($t0)
    sw    $t1, $0050($t0)
    jal   :draw_ln
    sw    $t2, $0054($t0)
    lui   $t0, $000E                   // left: (minx,maxy) -> (minx,miny)
    lw    $t1, $0060($t0)
    sw    $t1, $0040($t0)
    lw    $t2, $006C($t0)
    sw    $t2, $0044($t0)
    sw    $t1, $0050($t0)
    lw    $t2, $0068($t0)
    jal   :draw_ln
    sw    $t2, $0054($t0)

    // name, and distance on the line below, under the box.
    // Text has to be drawn by the game's HUD container (it binds font_text_01's palette); drawing a
    // C2DString ourselves gave garbage glyphs. So we borrow the last 16 slots of an idle pool of 28
    // C2DStrings in the HUD object ([0x00435818] + 0x878 + i*0x98, text "00", hidden) - two per
    // enemy (name, then "[8.7m]"; "\n" does not start a new line in this font code), set text +
    // position + visible, and the game draws them. Slots are hidden again at the start of every frame.
esp_name:
    lui   $t0, $000E
    lw    $t1, $0000($t0)
    andi  $t1, $t1, 8
    beq   $t1, $zero, :esp_next
    nop
    lw    $t1, $000C($t0)              // next free enemy slot (0..7)
    slti  $t2, $t1, 8
    beq   $t2, $zero, :esp_next
    addiu $t2, $t1, 1
    sw    $t2, $000C($t0)
    lui   $t2, $0043
    lw    $t2, $5818($t2)              // HUD object [0x00435818]
    beq   $t2, $zero, :esp_next
    addiu $t3, $zero, $0130            // two pool slots per enemy
    mult  $t1, $t3
    mflo  $t3
    addu  $t2, $t2, $t3
    addiu $s3, $t2, $0F98              // s3 = name slot, s3 + 0x98 = distance slot
    lw    $a1, $0014($s1)              // name
    beq   $a1, $zero, :esp_next
    nop
    move  $a0, $s3                     // SetString(name slot, name, 0)
    jal   $00380AD0
    move  $a2, $zero
    lwc1  $f1, $001C($s1)              // distance in units (= metres * 10)
    lwc1  $f2, $001C($s6)
    sub.s $f1, $f1, $f2
    mul.s $f1, $f1, $f1
    lwc1  $f2, $0020($s1)
    lwc1  $f3, $0020($s6)
    sub.s $f2, $f2, $f3
    mul.s $f2, $f2, $f2
    add.s $f1, $f1, $f2
    lwc1  $f2, $0024($s1)
    lwc1  $f3, $0024($s6)
    sub.s $f2, $f2, $f3
    mul.s $f2, $f2, $f2
    add.s $f1, $f1, $f2
    hexcode $46010044                  // sqrt.s $f1, $f1 in EE encoding (source in ft; the assembler emits the MIPS form)
    cvt.w.s $f1, $f1
    mfc1  $t3, $f1
    addiu $t4, $zero, 10
    div   $t3, $t4
    mflo  $a2                          // whole metres
    mfhi  $a3                          // tenths
    lui   $a0, $000E
    ori   $a0, $a0, $3100
    lui   $a1, $000E
    jal   $001988D0                    // sprintf(buf, "[%d.%dm]", m, tenths)
    ori   $a1, $a1, $3140
    addiu $a0, $s3, $0098              // SetString(distance slot, buf, 0)
    lui   $a1, $000E
    ori   $a1, $a1, $3100
    jal   $00380AD0
    move  $a2, $zero
    lui   $t0, $000E                   // name at (box min x, box max y + 2), distance 11 px below
    lw    $t1, $0060($t0)
    sw    $t1, $0090($s3)
    sw    $t1, $0128($s3)              // (s3 + 0x98) + 0x90
    lwc1  $f0, $006C($t0)
    lui   $t1, $4000                   // 2.0
    mtc1  $t1, $f1
    add.s $f0, $f0, $f1
    swc1  $f0, $0094($s3)
    lui   $t1, $4130                   // 11.0
    mtc1  $t1, $f1
    add.s $f0, $f0, $f1
    swc1  $f0, $012C($s3)              // (s3 + 0x98) + 0x94
    lbu   $t1, $0014($s3)              // both visible (bit 6)
    ori   $t1, $t1, $0040
    sb    $t1, $0014($s3)
    lbu   $t1, $00AC($s3)              // (s3 + 0x98) + 0x14
    ori   $t1, $t1, $0040
    sb    $t1, $00AC($s3)
    b     :esp_next
    nop

esp_done:
    lq    $s0, $0000($sp)
    lq    $s1, $0010($sp)
    lq    $s2, $0020($sp)
    lq    $s3, $0030($sp)
    lq    $s4, $0040($sp)
    lq    $s5, $0050($sp)
    lq    $s6, $0060($sp)
    lq    $s7, $0070($sp)
    ld    $ra, $0080($sp)
    jr    $ra                          // back into CHUD::Draw's epilogue
    addiu $sp, $sp, 160

// draw_ln(): Draw2DLine(0xE0040, 0xE0050, colour, colour) unless 300 lines already drawn
draw_ln:
    lui   $t0, $000E
    lw    $t1, $0008($t0)
    slti  $t2, $t1, 300
    beq   $t2, $zero, :draw_ln_out
    addiu $t1, $t1, 1
    sw    $t1, $0008($t0)
    lui   $a0, $000E
    ori   $a0, $a0, $0040
    lui   $a1, $000E
    ori   $a1, $a1, $0050
    lui   $a2, $000E
    ori   $a2, $a2, $0010
    j     $0037D570                    // tail call; returns to our caller
    move  $a3, $a2
draw_ln_out:
    jr    $ra
    nop

// bone_w(a0 = bone, a1 = node matrix) -> f6,f7,f8 world position (leaf)
// v = bone.origin; for each parent: v = rot(parent.quat, v) + parent.origin; then v * nodeMatrix
bone_w:
    lwc1  $f1, $0000($a0)
    lwc1  $f2, $0004($a0)
    lwc1  $f3, $0008($a0)
    lw    $t0, $001C($a0)
    addiu $t1, $zero, 32
bw_loop:
    beq   $t0, $zero, :bw_end
    nop
    lwc1  $f4, $0020($t0)              // qx
    lwc1  $f5, $0024($t0)              // qy
    lwc1  $f6, $0028($t0)              // qz
    lwc1  $f7, $002C($t0)              // qw
    mul.s $f16, $f5, $f3               // uv = q.xyz x v
    mul.s $f17, $f6, $f2
    sub.s $f8, $f16, $f17
    mul.s $f16, $f6, $f1
    mul.s $f17, $f4, $f3
    sub.s $f9, $f16, $f17
    mul.s $f16, $f4, $f2
    mul.s $f17, $f5, $f1
    sub.s $f10, $f16, $f17
    mul.s $f16, $f5, $f10              // uuv = q.xyz x uv
    mul.s $f17, $f6, $f9
    sub.s $f11, $f16, $f17
    mul.s $f16, $f6, $f8
    mul.s $f17, $f4, $f10
    sub.s $f12, $f16, $f17
    mul.s $f16, $f4, $f9
    mul.s $f17, $f5, $f8
    sub.s $f13, $f16, $f17
    add.s $f14, $f7, $f7               // v += uv * 2w + uuv * 2
    mul.s $f16, $f8, $f14
    add.s $f1, $f1, $f16
    add.s $f1, $f1, $f11
    add.s $f1, $f1, $f11
    mul.s $f16, $f9, $f14
    add.s $f2, $f2, $f16
    add.s $f2, $f2, $f12
    add.s $f2, $f2, $f12
    mul.s $f16, $f10, $f14
    add.s $f3, $f3, $f16
    add.s $f3, $f3, $f13
    add.s $f3, $f3, $f13
    lwc1  $f16, $0000($t0)             // + parent origin
    add.s $f1, $f1, $f16
    lwc1  $f16, $0004($t0)
    add.s $f2, $f2, $f16
    lwc1  $f16, $0008($t0)
    add.s $f3, $f3, $f16
    lw    $t0, $001C($t0)
    addiu $t1, $t1, -1
    bne   $t1, $zero, :bw_loop
    nop
bw_end:
    lwc1  $f0, $0000($a1)              // x' = x*m0 + y*m4 + z*m8 + m12
    mul.s $f6, $f1, $f0
    lwc1  $f0, $0010($a1)
    mul.s $f0, $f2, $f0
    add.s $f6, $f6, $f0
    lwc1  $f0, $0020($a1)
    mul.s $f0, $f3, $f0
    add.s $f6, $f6, $f0
    lwc1  $f0, $0030($a1)
    add.s $f6, $f6, $f0
    lwc1  $f0, $0004($a1)              // y'
    mul.s $f7, $f1, $f0
    lwc1  $f0, $0014($a1)
    mul.s $f0, $f2, $f0
    add.s $f7, $f7, $f0
    lwc1  $f0, $0024($a1)
    mul.s $f0, $f3, $f0
    add.s $f7, $f7, $f0
    lwc1  $f0, $0034($a1)
    add.s $f7, $f7, $f0
    lwc1  $f0, $0008($a1)              // z'
    mul.s $f8, $f1, $f0
    lwc1  $f0, $0018($a1)
    mul.s $f0, $f2, $f0
    add.s $f8, $f8, $f0
    lwc1  $f0, $0028($a1)
    mul.s $f0, $f3, $f0
    add.s $f8, $f8, $f0
    lwc1  $f0, $0038($a1)
    jr    $ra
    add.s $f8, $f8, $f0

// proj(f6,f7,f8 = world, a0 = out vec4, s5 = W2S matrix) -> v0 = 1 if on screen (leaf)
proj:
    move  $v0, $zero
    lwc1  $f0, $0000($s5)              // gs.x
    mul.s $f10, $f6, $f0
    lwc1  $f0, $0010($s5)
    mul.s $f0, $f7, $f0
    add.s $f10, $f10, $f0
    lwc1  $f0, $0020($s5)
    mul.s $f0, $f8, $f0
    add.s $f10, $f10, $f0
    lwc1  $f0, $0030($s5)
    add.s $f10, $f10, $f0
    lwc1  $f0, $0004($s5)              // gs.y
    mul.s $f11, $f6, $f0
    lwc1  $f0, $0014($s5)
    mul.s $f0, $f7, $f0
    add.s $f11, $f11, $f0
    lwc1  $f0, $0024($s5)
    mul.s $f0, $f8, $f0
    add.s $f11, $f11, $f0
    lwc1  $f0, $0034($s5)
    add.s $f11, $f11, $f0
    lwc1  $f0, $000C($s5)              // gs.w
    mul.s $f12, $f6, $f0
    lwc1  $f0, $001C($s5)
    mul.s $f0, $f7, $f0
    add.s $f12, $f12, $f0
    lwc1  $f0, $002C($s5)
    mul.s $f0, $f8, $f0
    add.s $f12, $f12, $f0
    lwc1  $f0, $003C($s5)
    add.s $f12, $f12, $f0
    lui   $t0, $3A83
    ori   $t0, $t0, $126F              // 0.001
    mtc1  $t0, $f1
    c.le.s $f12, $f1
    bc1t  :proj_out                    // behind camera
    nop
    lui   $t0, $3F80
    mtc1  $t0, $f1
    div.s $f1, $f1, $f12               // 1/w
    mul.s $f2, $f10, $f1
    lui   $t0, $44D8                   // 1728
    mtc1  $t0, $f3
    sub.s $f2, $f2, $f3
    mul.s $f4, $f11, $f1
    lui   $t0, $44E4                   // 1824
    mtc1  $t0, $f3
    sub.s $f4, $f4, $f3
    mtc1  $zero, $f5
    c.lt.s $f2, $f5
    bc1t  :proj_out
    nop
    c.lt.s $f4, $f5
    bc1t  :proj_out
    nop
    lui   $t0, $4420                   // 640
    mtc1  $t0, $f3
    c.lt.s $f3, $f2
    bc1t  :proj_out
    nop
    lui   $t0, $43E0                   // 448
    mtc1  $t0, $f3
    c.lt.s $f3, $f4
    bc1t  :proj_out
    nop
    swc1  $f2, $0000($a0)
    swc1  $f4, $0004($a0)
    sw    $zero, $0008($a0)
    lui   $t0, $3F80
    sw    $t0, $000C($a0)
    addiu $v0, $zero, 1
proj_out:
    jr    $ra
    nop

address $002167C0                      // previous hook site: restore jal DrawAutoReticle
    hexcode $0C085D54

address $00216DA8                      // CHUD::Draw's last call: jal 0x00215C10 (a0 = hud via delay slot)
    jal   :esp_hook
