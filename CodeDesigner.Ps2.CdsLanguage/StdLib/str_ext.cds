// Extended string functions: find (substring), substr (copy range), startswith, endswith,
// atoi (decimal/hex), itoa_dec.
// Usage: include "StdLib/str_ext.cds"
// Returns & conventions: integers in $v0, strings via memory pointers in $v0 when applicable.
// Clobbers: $t0-$t7, plus $t8/$t9 as scratch in atoi/itoa.

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
    addi $t2, $zero, $0       // suffix length
ew_len_s:
    lb   $t3, 0($t1)
    beq  $t3, $zero, :ew_len_str
    addiu $t1, $t1, 1
    addiu $t2, $t2, 1
    j    :ew_len_s
    nop
ew_len_str:
    addi $t4, $zero, $0       // hay length
ew_len_h:
    lb   $t5, 0($t0)
    beq  $t5, $zero, :ew_check
    addiu $t0, $t0, 1
    addiu $t4, $t4, 1
    j    :ew_len_h
    nop
ew_check:
    // if hayLen < suffixLen: return false
    slt  $at, $t4, $t2
    bne  $at, $zero, :ew_false
    nop
    // align str_ptr to (str_end - suffix_len): t0 currently = str_end, subtract t2
    subu $t0, $t0, $t2
    addi $t1, $a1, $0         // restart suffix ptr
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

// atoi: parse decimal or 0xHEX -> v0 = value, assumes valid input.
// Uses $t8 to materialise ASCII char-code constants (BEQ requires reg-reg).
lib_str_atoi:
    // $a0 = ptr
    lb   $t0, 0($a0)
    beq  $t0, $zero, :atoi_zero
    // detect optional sign
    addi $t1, $zero, 1        // sign = 1
    addiu $t8, $zero, 45      // '-'
    beq  $t0, $t8, :atoi_minus
    addiu $t8, $zero, 43      // '+'
    beq  $t0, $t8, :atoi_plus
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
    // hex?  (leading "0x" or "0X")
    addiu $t8, $zero, 48      // '0'
    beq  $t0, $t8, :atoi_maybe_hex
    j    :atoi_dec
    nop
atoi_maybe_hex:
    lb   $t2, 1($a0)
    addiu $t8, $zero, 120     // 'x'
    beq  $t2, $t8, :atoi_hex
    addiu $t8, $zero, 88      // 'X'
    beq  $t2, $t8, :atoi_hex
    j    :atoi_dec
    nop
atoi_hex:
    addiu $a0, $a0, 2
    addi $v0, $zero, 0
atoi_hex_loop:
    lb   $t3, 0($a0)
    beq  $t3, $zero, :atoi_apply_sign
    // convert hex digit (check ranges)
    // digits 0-9 are ASCII 48-57
    addi $t4, $zero, 48
    addi $t5, $zero, 57
    slt  $t6, $t3, $t4
    bne  $t6, $zero, :hex_check_upper
    slt  $t6, $t5, $t3
    beq  $t6, $zero, :hex_digit_digit
hex_check_upper:
    // A-F are ASCII 65-70 or a-f are 97-102
    addi $t4, $zero, 65
    addi $t5, $zero, 70
    slt  $t6, $t3, $t4
    bne  $t6, $zero, :hex_check_lower
    slt  $t6, $t5, $t3
    beq  $t6, $zero, :hex_digit_upper
hex_check_lower:
    addi $t4, $zero, 97
    addi $t5, $zero, 102
    slt  $t6, $t3, $t4
    bne  $t6, $zero, :atoi_apply_sign
    slt  $t6, $t5, $t3
    bne  $t6, $zero, :atoi_apply_sign
    j    :hex_digit_lower
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
    addi $v0, $zero, 0     // init accumulator
    addi $t2, $zero, 10    // load constant 10 for multiplier
atoi_dec_loop:
    lb   $t3, 0($a0)
    beq  $t3, $zero, :atoi_apply_sign
    addi $t4, $zero, 48
    addi $t5, $zero, 57
    slt  $t6, $t3, $t4
    bne  $t6, $zero, :atoi_apply_sign
    slt  $t6, $t5, $t3
    bne  $t6, $zero, :atoi_apply_sign
    addiu $t3, $t3, -48         // convert ASCII to digit value
    mult $v0, $t2               // v0 * 10
    mflo $v0                    // get result (low 32 bits)
    addu $v0, $v0, $t3          // add new digit
    addiu $a0, $a0, 1
    j    :atoi_dec_loop
    nop
atoi_apply_sign:
    mult $v0, $t1
    mflo $v0           // get result
    jr   $ra
    nop
atoi_zero:
    addi $v0, $zero, 0
    jr   $ra
    nop

// itoa_dec(value, dst) -> write decimal string into dst, returns dst in v0.
// Handles negative values (sign-prepended). Uses $t8 = 10 as divisor.
lib_str_itoa:
    // $a0 = value, $a1 = dst
    addi $v0, $a1, 0
    // handle zero
    beq  $a0, $zero, :itoa_write_zero
    addi $t0, $a1, 0           // dst ptr
    addi $t1, $zero, 0         // digit count
    // handle negative (treat $a0 as signed)
    bltz $a0, :itoa_negative
    nop
    j    :itoa_loop
    nop
itoa_negative:
    addi $t1, $zero, 1
    negu $a0, $a0             // pseudo: a0 = -a0  (assembler rewrites to subu)
itoa_loop:
    // get digit = value % 10
    // Divide by 10 using mult/div helper
    // For simplicity, use mod by repeated subtraction (slow but works)
    addi $t2, $zero, 10
    div  $a0, $t2              // a0 / 10; quotient in LO, remainder in HI
    mfhi $t3                   // remainder = a0 % 10
    mflo $a0                   // quotient for next iteration
    addiu $t3, $t3, 48         // convert to ASCII digit
    sb   $t3, 0($t0)
    addiu $t0, $t0, 1
    bne  $a0, $zero, :itoa_loop
    nop
    j    :itoa_maybe_sign
    nop
itoa_maybe_sign:
    // Append '-' before reversing so it becomes the leading character.
    beq  $t1, $zero, :itoa_finish
    nop
    addiu $t8, $zero, 45      // '-'
    sb   $t8, 0($t0)
    addiu $t0, $t0, 1
itoa_finish:
    // null-terminate at $t0, then reverse the [a1, t0) range in place
    sb   $zero, 0($t0)
    addiu $t3, $t0, -1        // last digit ptr
    addi $t4, $a1, $0         // first digit ptr
itoa_rev_loop:
    // loop while first < last
    slt  $at, $t4, $t3
    beq  $at, $zero, :itoa_done
    nop
    lb   $t5, 0($t4)
    lb   $t6, 0($t3)
    sb   $t6, 0($t4)
    sb   $t5, 0($t3)
    addiu $t4, $t4, 1
    addiu $t3, $t3, -1
    j    :itoa_rev_loop
    nop
itoa_done:
    jr   $ra
    nop
itoa_write_zero:
    addiu $t8, $zero, 48      // '0'
    sb   $t8, 0($a1)
    sb   $zero, 1($a1)
    jr   $ra
    nop
