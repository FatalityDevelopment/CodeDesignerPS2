// Extra string utilities (case transforms, simple trimming).
// include "StdLib/strutils.cds"
//
// Functions:
//   lib_str_toupper   (a0=ptr) -> v0 = ptr (in-place)
//   lib_str_tolower   (a0=ptr) -> v0 = ptr (in-place)
//   lib_str_trim_left (a0=ptr) -> v0 = ptr (shifts string left to remove leading spaces)
//   lib_str_trim_right (a0=ptr) -> v0 = ptr (removes trailing spaces in-place)

//
// Note: ASCII space = 0x20. Only basic ASCII letters are transformed.

lib_str_toupper:
    // $a0 = ptr
    addi $v0, $a0, $0
toupper_loop:
    lb   $t0, $0($a0)
    beq  $t0, $zero, :toupper_done
    // if 'a' <= t0 <= 'z' then t0 -= 32
    addi $t1, $t0, $0
    addi $t2, $zero, $97      // 'a'
    sltu $t3, $t2, $t1        // t3 = (97 < t0) ? 1 : 0   [we'll do range check differently]
    // simpler: check t0 >= 'a' and <= 'z'
    addi $t4, $t0, $0
    addi $t5, $zero, $97
    slt  $t6, $t5, $t4        // t6 = 1 if 97 < t0 (t0 > 97)
    addi $t5, $zero, $122
    slt  $t7, $t4, $t5        // t7 = 1 if t0 < 122
    // Use combined condition via beq on adjusted values is cumbersome; use simple compares:
    addi $t8, $zero, $97
    addi $t9, $zero, $122
    blt  $t0, $t8, :toupper_skip
    bgt  $t0, $t9, :toupper_skip
    // in range
    addiu $t0, $t0, -32
    sb   $t0, $0($a0)
toupper_skip:
    addiu $a0, $a0, $1
    j    :toupper_loop
    nop
toupper_done:
    jr   $ra
    nop

lib_str_tolower:
    addi $v0, $a0, $0
tolower_loop:
    lb   $t0, $0($a0)
    beq  $t0, $zero, :tolower_done
    addi $t8, $zero, $65   // 'A'
    addi $t9, $zero, $90   // 'Z'
    blt  $t0, $t8, :tolower_skip
    bgt  $t0, $t9, :tolower_skip
    addiu $t0, $t0, 32
    sb   $t0, $0($a0)
tolower_skip:
    addiu $a0, $a0, $1
    j    :tolower_loop
    nop
tolower_done:
    jr   $ra
    nop

// Trim left: shift pointer to first non-space and copy forward to original base.
// For simplicity this routine overwrites from original start and returns pointer to that start.
lib_str_trim_left:
    // $a0 = ptr (base)
    // find first non-space
    addi $t0, $a0, $0
find_nl:
    lb   $t1, $0($t0)
    beq  $t1, $zero, :trim_left_done // empty or all spaces
    addi $t2, $zero, $32
    beq  $t1, $t2, :advance_nl
    j    :trim_left_copy
    nop
advance_nl:
    addiu $t0, $t0, $1
    j    :find_nl
    nop
trim_left_copy:
    // t0 points at first non-space, a0 is dest base
    addi $t3, $t0, $0   // src
    addi $t4, $a0, $0   // dst
copy_loop_l:
    lb   $t5, $0($t3)
    sb   $t5, $0($t4)
    beq  $t5, $zero, :trim_left_done
    addiu $t3, $t3, $1
    addiu $t4, $t4, $1
    j    :copy_loop_l
    nop
trim_left_done:
    addi $v0, $a0, $0
    jr   $ra
    nop

// Trim right: remove trailing spaces by walking to end and backing up
lib_str_trim_right:
    // $a0 = ptr (base)
    addi $t0, $a0, $0
    // find end
tr_find_end:
    lb   $t1, $0($t0)
    beq  $t1, $zero, :tr_step_back
    addiu $t0, $t0, $1
    j    :tr_find_end
    nop
tr_step_back:
    addiu $t0, $t0, -1   // step to last char
tr_back_loop:
    lb   $t1, $0($t0)
    addi $t2, $zero, $32
    beq  $t1, $t2, :tr_do_trim
    // non-space: done
    addiu $t0, $t0, $1
    sb   $zero, $0($t0)   // null-terminate after last non-space
    j    :tr_done
    nop
tr_do_trim:
    // found a space, overwrite with NUL and step back
    sb   $zero, $0($t0)
    addiu $t0, $t0, -1
    lb   $t1, $0($t0)
    bne  $t1, $zero, :tr_back_loop
    // if we reached start, ensure terminator at base
    addi $t0, $a0, $0
    sb   $zero, $0($t0)
    j    :tr_done
    nop
tr_done:
    addi $v0, $a0, $0
    jr   $ra
    nop