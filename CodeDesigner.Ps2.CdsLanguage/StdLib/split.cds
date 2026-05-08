// str_split: split a null-terminated string by a separator byte.
// Writes pointers (word addresses) into caller-supplied out array.
// Usage:
//   include "StdLib/split.cds"
//
// Convention:
//   a0 = src (in-place split, separators overwritten with NUL)
//   a1 = sepChar (byte value, low byte of register)
//   a2 = outArrayPtr (word-addressable)
//   a3 = maxParts
//   returns: v0 = number of parts written

lib_str_split:
    addi $v0, $zero, 0       // count = 0
    beq  $a0, $zero, :split_done
    beq  $a3, $zero, :split_done
    addi $t0, $a0, $zero     // ptr
    addi $t1, $a2, $zero     // outPtr base
split_outer:
    lb   $t2, 0($t0)
    beq  $t2, $zero, :split_done
    // record token start
    sw   $t0, 0($t1)         // write pointer
    addiu $v0, $v0, 1
    addiu $t1, $t1, 4
    // walk until sep or NUL
split_inner:
    lb   $t3, 0($t0)
    beq  $t3, $zero, :split_advance_to_end
    beq  $t3, $a1, :split_separator_found
    addiu $t0, $t0, 1
    j    :split_inner
    nop
split_separator_found:
    // overwrite sep with NUL
    sb   $zero, 0($t0)
    addiu $t0, $t0, 1
    // check if we've reached maxParts
    bge  $v0, $a3, :split_done
    j    :split_outer
    nop
split_advance_to_end:
    // NUL found -> done
    jr   $ra
    nop
split_done:
    addi $v0, $v0, 0
    jr   $ra
    nop