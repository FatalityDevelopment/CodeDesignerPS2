// Extended string functions: find (substring), substr (copy range), startswith, endswith,
// atoi (decimal/hex), itoa_dec.
// Usage: include "StdLib/str_ext.cds"
// Returns & conventions: integers in $v0, strings via memory pointers in $v0 when applicable.
// Clobbers: $t0-$t6, uses $t9/$t8 as scratch if needed (preprocessor warns about $t8/$t9).

// find(hay, needle) -> v0 = index (0-based) or -1 if not found
lib_str_find:
    // $a0 = hay, $a1 = needle
    // if needle[0] == 0 -> return 0
    lb   $t0, 0($a1)
    beq  $t0, $zero, :find_empty
    addi $t1, $a0, $0      // hay_ptr
    addi $t2, $zero, $0    // idx
find_outer:
    lb   $t3, 0($t1)
    beq  $t3, $zero, :find_notfound
    // attempt match at t1
    addi $t4, $t1, $0
    addi $t5, $a1, $0
find_inner:
    lb   $t6, 0($t5)
    beq  $t6, $zero, :find_matched
    lb   $t7, 0($t4)
    bne  $t7, $t6, :find_advance
    addiu $t4, $t4, 1
    addiu $t5, $t5, 1
    j    :find_inner
    nop
find_advance:
    addiu $t1, $t1, 1
    addiu $t2, $t2, 1
    j    :find_outer
    nop
find_matched:
    addi $v0, $t2, $0
    jr   $ra
    nop
find_empty:
    addi $v0, $zero, $0
    jr   $ra
    nop
find_notfound:
    addi $v0, $zero, -1
    jr   $ra
    nop

// substr(src, dst, start, len) -> copies up to len bytes from start into dst, ensures NUL, returns dst
lib_str_substr:
    // $a0 = src, $a1 = dst, $a2 = start, $a3 = len
    addi $v0, $a1, $0
    beq  $a3, $zero, :substr_done_zero
    addi $t0, $a0, $0
    // advance t0 by start
    beq  $a2, $zero, :substr_copy
adv_start:
    lb   $t1, 0($t0)
    beq  $t1, $zero, :substr_done_zero
    addiu $t0, $t0, 1
    addiu $a2, $a2, -1
    bne  $a2, $zero, :adv_start
    nop
substr_copy:
    lb   $t2, 0($t0)
    beq  $t2, $zero, :substr_terminate
    sb   $t2, 0($a1)
    addiu $a1, $a1, 1
    addiu $t0, $t0, 1
    addiu $a3, $a3, -1
    bne  $a3, $zero, :substr_copy
    nop
substr_terminate:
    sb   $zero, 0($a1)
    jr   $ra
    nop
substr_done_zero:
    sb   $zero, 0($a1)
    jr   $ra
    nop

// startswith(str, prefix) -> v0 = 1 true, 0 false
lib_str_startswith:
    // $a0=str, $a1=prefix
    addi $t0, $a0, $0
    addi $t1, $a1, $0
sw_loop:
    lb   $t2, 0($t1)
    beq  $t2, $zero, :sw_true
    lb   $t3, 0($t0)
    beq  $t3, $zero, :sw_false
    bne  $t2, $t3, :sw_false
    addiu $t0, $t0, 1
    addiu $t1, $t1, 1
    j    :sw_loop
    nop
sw_true:
    addi $v0, $zero, 1
    jr   $ra
    nop
sw_false:
    addi $v0, $zero, 0
    jr   $ra
    nop

// endswith(str, suffix) -> v0 = 1 true, 0 false
lib_str_endswith:
    // $a0=str, $a1=suffix
    // find lengths
    addi $t0, $a0, $0
    addi $t1, $a1, $0
    addi $t2, $zero, $0
ew_len_s:
    lb   $t3, 0($t1)
    beq  $t3, $zero, :ew_len_str
    addiu $t1, $t1, 1
    addiu $t2, $t2, 1
    j    :ew_len_s
    nop
ew_len_str:
    addi $t4, $zero, $0
ew_len_h:
    lb   $t5, 0($t0)
    beq  $t5, $zero, :ew_check
    addiu $t0, $t0, 1
    addiu $t4, $t4, 1
    j    :ew_len_h
    nop
ew_check:
    blt  $t4, $t2, :ew_false
    // set pointers to ends
    subu $t0, $t0, $t2   // t0 = str_end - suffix_len
    subu $t1, $a1, $zero // reuse
ew_cmp:
    lb   $t6, 0($t1)
    beq  $t6, $zero, :ew_true
    lb   $t7, 0($t0)
    bne  $t6, $t7, :ew_false
    addiu $t0, $t0, 1
    addiu $t1, $t1, 1
    j    :ew_cmp
    nop
ew_true:
    addi $v0, $zero, 1
    jr   $ra
    nop
ew_false:
    addi $v0, $zero, 0
    jr   $ra
    nop

// atoi: parse decimal or 0xHEX -> v0 = value, assumes valid input
lib_str_atoi:
    // $a0 = ptr
    lb   $t0, 0($a0)
    beq  $t0, $zero, :atoi_zero
    // detect optional sign
    addi $t1, $zero, 1   // sign = 1
    beq  $t0, $45, :atoi_minus // '-'
    beq  $t0, $43, :atoi_plus  // '+'
    j    :atoi_start
    nop
atoi_minus:
    addi $t1, $zero, -1
    addiu $a0, $a0, 1
    j    :atoi_start
    nop
atoi_plus:
    addiu $a0, $a0, 1
atoi_start:
    lb   $t0, 0($a0)
    beq  $t0, $zero, :atoi_zero
    // hex?
    beq  $t0, $48, :atoi_maybe_hex // '0'
    j    :atoi_dec
    nop
atoi_maybe_hex:
    lb   $t2, 1($a0)
    beq  $t2, $120, :atoi_hex // 'x' lowercase
    beq  $t2, $88, :atoi_hex  // 'X' uppercase
    j    :atoi_dec
    nop
atoi_hex:
    addiu $a0, $a0, 2
    addi $v0, $zero, $0
atoi_hex_loop:
    lb   $t3, 0($a0)
    beq  $t3, $zero, :atoi_apply_sign
    // convert hex digit
    blt  $t3, $58, :hex_digit_digit
    blt  $t3, $71, :hex_digit_upper
    blt  $t3, $103, :hex_digit_lower
    j    :atoi_apply_sign
    nop
hex_digit_digit:
    addiu $t3, $t3, -48
    j    :hex_acc
    nop
hex_digit_upper:
    addiu $t3, $t3, -55
    j    :hex_acc
    nop
hex_digit_lower:
    addiu $t3, $t3, -87
hex_acc:
    sll  $v0, $v0, 4
    addu $v0, $v0, $t3
    addiu $a0, $a0, 1
    j    :atoi_hex_loop
    nop
atoi_dec:
    addi $v0, $zero, $0
atoi_dec_loop:
    lb   $t3, 0($a0)
    beq  $t3, $zero, :atoi_apply_sign
    blt  $t3, $48, :atoi_apply_sign
    bgt  $t3, $57, :atoi_apply_sign
    addiu $t3, $t3, -48
    muli $v0, $v0, 10
    addu $v0, $v0, $t3
    addiu $a0, $a0, 1
    j    :atoi_dec_loop
    nop
atoi_apply_sign:
    mul  $v0, $v0, $t1
    jr   $ra
    nop
atoi_zero:
    addi $v0, $zero, $0
    jr   $ra
    nop

// itoa_dec(value, dst) -> write decimal string into dst, returns dst in v0
lib_str_itoa:
    // $a0 = value, $a1 = dst
    addi $v0, $a1, $0
    // handle zero
    beq  $a0, $zero, :itoa_write_zero
    addi $t0, $a1, $0     // dst ptr
    addi $t1, $zero, $0   // digit count
    // handle negative (treat $a0 as signed)
    bltz $a0, :itoa_negative
itoa_loop:
    // get digit = value % 10  (simple repeated subtract is expensive; we use div pseudo)
    div  $a0, $zero, $a0, 10  // pseudo: assume assembler supports dividing with immediate; if not replace with helper
    mfhi $t2
    mflo $a0
    addiu $t2, $t2, 48
    sb   $t2, 0($t0)
    addiu $t0, $t0, 1
    addiu $t1, $t1, 1
    bne  $a0, $zero, :itoa_loop
    j    :itoa_finish
    nop
itoa_negative:
    negu $a0, $a0
    addi $t3, $zero, 1
    j    :itoa_loop
    nop
itoa_write_zero:
    sb   $48, 0($a1)
    sb   $zero, 1($a1)
    jr   $ra
    nop
itoa_finish:
    // reverse digits in-place; t0 points after last digit, a1 is base
    subu $t4, $t0, $a1
    addiu $t4, $t4, -1  // last index
    addi $t5, $a1, $0
rev_loop:
    beq  $t1, $zero, :rev_done
    lb   $t6, 0($a1)
    lb   $t7, 0($t4)
    sb   $t7, 0($a1)
    sb   $t6, 0($t4)
    addiu $a1, $a1, 1
    addiu $t4, $t4, -1
    addiu $t1, $t1, -2
    bgtz $t1, :rev_loop
    nop
rev_done:
    sb   $zero, 0($t0)
    jr   $ra
    nop