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
    addi $v0, $a0, 0
toupper_loop:
    lb   $t0, 0($a0)
    beq  $t0, $zero, :toupper_done
    // check if 'a' (97) <= t0 <= 'z' (122)
    addi $t1, $zero, 97        // 'a'
    addi $t2, $zero, 122       // 'z'
    slt  $t3, $t0, $t1         // t3 = 1 if t0 < 'a'
    bne  $t3, $zero, :toupper_skip
    slt  $t3, $t2, $t0         // t3 = 1 if t0 > 'z'
    bne  $t3, $zero, :toupper_skip
    // in range ['a'..'z']: convert to uppercase by subtracting 32
    addiu $t0, $t0, -32
    sb   $t0, 0($a0)
toupper_skip:
    addiu $a0, $a0, 1
    j    :toupper_loop
    nop
toupper_done:
    jr   $ra
    nop

lib_str_tolower:
    addi $v0, $a0, 0
tolower_loop:
    lb   $t0, 0($a0)
    beq  $t0, $zero, :tolower_done
    addi $t1, $zero, 65        // 'A'
    addi $t2, $zero, 90        // 'Z'
    slt  $t3, $t0, $t1         // t3 = 1 if t0 < 'A'
    bne  $t3, $zero, :tolower_skip
    slt  $t3, $t2, $t0         // t3 = 1 if t0 > 'Z'
    bne  $t3, $zero, :tolower_skip
    // in range ['A'..'Z']: convert to lowercase by adding 32
    addiu $t0, $t0, 32
    sb   $t0, 0($a0)
tolower_skip:
    addiu $a0, $a0, 1
    j    :tolower_loop
    nop
tolower_done:
    jr   $ra
    nop

// Trim left: shift first non-space byte (and everything after) to the original base.
lib_str_trim_left:
    // $a0 = ptr (base)
    // find first non-space
    addi $t0, $a0, 0
find_nl:
    lb   $t1, 0($t0)
    beq  $t1, $zero, :trim_left_done // empty or all spaces
    addi $t2, $zero, 32
    beq  $t1, $t2, :advance_nl
    nop
    j    :trim_left_copy
    nop
advance_nl:
    addiu $t0, $t0, 1
    j    :find_nl
    nop
trim_left_copy:
    // t0 points at first non-space, a0 is dest base
    addi $t3, $t0, 0   // src
    addi $t4, $a0, 0   // dst
copy_loop_l:
    lb   $t5, 0($t3)
    sb   $t5, 0($t4)
    beq  $t5, $zero, :trim_left_done
    addiu $t3, $t3, 1
    addiu $t4, $t4, 1
    j    :copy_loop_l
    nop
trim_left_done:
    addi $v0, $a0, 0
    jr   $ra
    nop

// Trim right: walk to end of string then back up over trailing spaces, NUL-terminate.
lib_str_trim_right:
    // $a0 = ptr (base)
    addi $t0, $a0, 0
    // find end
tr_find_end:
    lb   $t1, 0($t0)
    beq  $t1, $zero, :tr_step_back
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
    addi $t2, $zero, 32
    beq  $t1, $t2, :tr_do_trim
    // non-space: done
    addiu $t0, $t0, 1
    sb   $zero, 0($t0)   // null-terminate after last non-space
    j    :tr_done
    nop
tr_do_trim:
    // found a space, overwrite with NUL and step back
    sb   $zero, 0($t0)
    addiu $t0, $t0, -1
    lb   $t1, 0($t0)
    bne  $t1, $zero, :tr_back_loop
    // if we reached start, ensure terminator at base
    addi $t0, $a0, 0
    sb   $zero, 0($t0)
    j    :tr_done
    nop
tr_terminate:
    // last non-space is at $t0; NUL-terminate after it
    addiu $t0, $t0, 1
    sb   $zero, 0($t0)
tr_done:
    addi $v0, $a0, 0
    jr   $ra
    nop
