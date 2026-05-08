// Extra string utilities (case transforms, simple trimming).
// include "StdLib/strutils.cds"
//
// Functions:
//   lib_str_toupper    (a0=ptr) -> v0 = ptr (in-place)
//   lib_str_tolower    (a0=ptr) -> v0 = ptr (in-place)
//   lib_str_trim_left  (a0=ptr) -> v0 = ptr (shifts string left to remove leading spaces)
//   lib_str_trim_right (a0=ptr) -> v0 = ptr (removes trailing spaces in-place)
//
// Note: ASCII space = 32 (decimal). Only basic ASCII letters are transformed.

lib_str_toupper:
    // $a0 = ptr
    addi $v0, $a0, $0
    addi $t1, $a0, $0          // walking ptr (preserve $a0 for return)
toupper_loop:
    lb   $t0, 0($t1)
    beq  $t0, $zero, :toupper_done
    nop
    // skip if t0 < 'a' or t0 > 'z'
    slti $at, $t0, 97          // < 'a' -> skip
    bne  $at, $zero, :toupper_skip
    nop
    slti $at, $t0, 123         // < 123 -> in [a..z]; else skip
    beq  $at, $zero, :toupper_skip
    nop
    // in range: t0 -= 32
    addiu $t0, $t0, -32
    sb   $t0, 0($t1)
toupper_skip:
    addiu $t1, $t1, 1
    j    :toupper_loop
    nop
toupper_done:
    jr   $ra
    nop

lib_str_tolower:
    addi $v0, $a0, $0
    addi $t1, $a0, $0
tolower_loop:
    lb   $t0, 0($t1)
    beq  $t0, $zero, :tolower_done
    nop
    // skip if t0 < 'A' or t0 > 'Z'
    slti $at, $t0, 65          // < 'A' -> skip
    bne  $at, $zero, :tolower_skip
    nop
    slti $at, $t0, 91          // < 91 -> in [A..Z]; else skip
    beq  $at, $zero, :tolower_skip
    nop
    addiu $t0, $t0, 32
    sb   $t0, 0($t1)
tolower_skip:
    addiu $t1, $t1, 1
    j    :tolower_loop
    nop
tolower_done:
    jr   $ra
    nop

// Trim left: shift first non-space byte (and everything after) to the original base.
lib_str_trim_left:
    // $a0 = ptr (base)
    addi $t0, $a0, $0          // scan ptr
    addiu $t9, $zero, 32       // ' '
find_nl:
    lb   $t1, 0($t0)
    beq  $t1, $zero, :trim_left_done
    nop
    bne  $t1, $t9, :trim_left_copy
    nop
    addiu $t0, $t0, 1
    j    :find_nl
    nop
trim_left_copy:
    // $t0 = first non-space, $a0 = dest base
    addi $t3, $t0, $0          // src
    addi $t4, $a0, $0          // dst
copy_loop_l:
    lb   $t5, 0($t3)
    sb   $t5, 0($t4)
    beq  $t5, $zero, :trim_left_done
    nop
    addiu $t3, $t3, 1
    addiu $t4, $t4, 1
    j    :copy_loop_l
    nop
trim_left_done:
    addi $v0, $a0, $0
    jr   $ra
    nop

// Trim right: walk to end of string then back up over trailing spaces, NUL-terminate.
lib_str_trim_right:
    // $a0 = ptr (base)
    addi $t0, $a0, $0
    addiu $t9, $zero, 32       // ' '
tr_find_end:
    lb   $t1, 0($t0)
    beq  $t1, $zero, :tr_check_empty
    nop
    addiu $t0, $t0, 1
    j    :tr_find_end
    nop
tr_check_empty:
    // if string is empty (t0 == base), nothing to do
    beq  $t0, $a0, :tr_done
    nop
    // step back to last char
    addiu $t0, $t0, -1
tr_back_loop:
    lb   $t1, 0($t0)
    bne  $t1, $t9, :tr_terminate
    nop
    sb   $zero, 0($t0)
    // if we've reached the base, stop
    beq  $t0, $a0, :tr_done
    nop
    addiu $t0, $t0, -1
    j    :tr_back_loop
    nop
tr_terminate:
    // last non-space is at $t0; NUL-terminate after it
    addiu $t0, $t0, 1
    sb   $zero, 0($t0)
tr_done:
    addi $v0, $a0, $0
    jr   $ra
    nop
