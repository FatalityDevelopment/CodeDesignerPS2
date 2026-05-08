// Minimal snprintf-like formatter that consumes an argument array.
// Supports %s (string pointer), %d (signed decimal), %x (hex lower-case).
// Usage:
//   include "StdLib/printf.cds"
// Requires lib_str_itoa from str_ext.cds for decimal conversion.
//
// Conventions:
//   a0 = dst (buffer)
//   a1 = dstSize (bytes)
//   a2 = fmt (pointer to null-terminated format string)
//   a3 = argsPtr (pointer to uint32_t argument array)
//   returns: v0 = number of bytes written (excluding final NUL)

    // Temporary small buffer for numeric conversions (16 bytes)
_fmt_tmpbuf:
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

lib_sprintf_args:
    addi  $v0, $zero, 0        // written
    beq   $a1, $zero, :spf_done
    addi  $t0, $a0, $zero     // dstPtr
    addi  $t1, $a2, $zero     // fmtPtr
    addi  $t2, $a3, $zero     // argsPtr
    addi  $t3, $a1, $zero     // remaining size
    addi  $t4, $zero, 0       // argIndex offset in bytes
spf_loop:
    lb    $t5, 0($t1)
    beq   $t5, $zero, :spf_finish_write
    beq   $t5, 0x25, :spf_percent   // '%'
    // copy literal char if room
    beq   $t3, $zero, :spf_finish_write
    sb    $t5, 0($t0)
    addiu $t0, $t0, 1
    addiu $t1, $t1, 1
    addiu $t3, $t3, -1
    addiu $v0, $v0, 1
    j     :spf_loop
    nop

spf_percent:
    addiu $t1, $t1, 1           // skip '%'
    lb    $t6, 0($t1)
    beq   $t6, $zero, :spf_finish_write
    // load next arg: lw argVal, 0(argsPtr + t4)
    addu  $t7, $t2, $t4
    lw    $t8, 0($t7)
    // advance arg index by 4
    addiu $t4, $t4, 4
    // handle specifier
    beq   $t6, 0x73, :spf_spec_s   // 's'
    beq   $t6, 0x64, :spf_spec_d   // 'd'
    beq   $t6, 0x78, :spf_spec_x   // 'x'
    // unknown specifier: emit it literally
    beq   $t3, $zero, :spf_finish_write
    sb    $t6, 0($t0)
    addiu $t0, $t0, 1
    addiu $v0, $v0, 1
    addiu $t1, $t1, 1
    addiu $t3, $t3, -1
    j    :spf_loop
    nop

spf_spec_s:
    // t8 = pointer to string
    addi  $t9, $t8, 0
    // copy bytes
spf_copy_s:
    lb    $t5, 0($t9)
    beq   $t5, $zero, :spf_spec_s_done
    beq   $t3, $zero, :spf_finish_write
    sb    $t5, 0($t0)
    addiu $t9, $t9, 1
    addiu $t0, $t0, 1
    addiu $t3, $t3, -1
    addiu $v0, $v0, 1
    j    :spf_copy_s
    nop
spf_spec_s_done:
    addiu $t1, $t1, 1
    j    :spf_loop
    nop

spf_spec_d:
    // decimal: call lib_str_itoa to write into _fmt_tmpbuf, then copy
    addi  $a0, $t8, 0       // int value
    la    $a1, _fmt_tmpbuf
    jal   :lib_str_itoa
    nop
    // returned dst in v0 (pointer)
    addi  $t9, $v0, 0
    // copy string from t9
    j     :spf_copy_s_from_tmp
    nop
spf_copy_s_from_tmp:
    lb    $t5, 0($t9)
    beq   $t5, $zero, :spf_spec_d_done
    beq   $t3, $zero, :spf_finish_write
    sb    $t5, 0($t0)
    addiu $t9, $t9, 1
    addiu $t0, $t0, 1
    addiu $t3, $t3, -1
    addiu $v0, $v0, 1
    j    :spf_copy_s_from_tmp
    nop
spf_spec_d_done:
    addiu $t1, $t1, 1
    j    :spf_loop
    nop

spf_spec_x:
    // hex: convert to hex string into tmp buffer (simple nibble loop)
    addi  $t9, $a0, 0       // reuse t9 as tmp pointer base will be _fmt_tmpbuf
    la    $t9, _fmt_tmpbuf
    addi  $t10, $zero, 0
    addi  $t11, $t8, 0      // value
    // if value = 0, write '0'
    beq   $t11, $zero, :spf_write_hex_zero
    // produce digits into tmp buffer reverse
spf_hex_loop:
    andi  $t12, $t11, 0xF
    // convert nibble to ascii
    blt   $t12, 10, :hex_digit_num
    addiu $t13, $t12, 87    // 'a' - 10 = 97 -10 =87
    j     :hex_store
    nop
hex_digit_num:
    addiu $t13, $t12, 48
hex_store:
    sb    $t13, 0($t9)
    addiu $t9, $t9, 1
    srl   $t11, $t11, 4
    bne   $t11, $zero, :spf_hex_loop
    nop
    // reverse tmp digits into dst
spf_hex_reverse:
    // t9 points after last digit, step back
    addiu $t9, $t9, -1
spf_hex_rev_loop:
    lb    $t14, 0($t9)
    beq   $t14, $zero, :spf_hex_rev_done
    beq   $t3, $zero, :spf_finish_write
    sb    $t14, 0($t0)
    addiu $t0, $t0, 1
    addiu $v0, $v0, 1
    addiu $t9, $t9, -1
    j    :spf_hex_rev_loop
    nop
spf_hex_rev_done:
    addiu $t1, $t1, 1
    j    :spf_loop
    nop

spf_write_hex_zero:
    beq   $t3, $zero, :spf_finish_write
    sb    $zero, 0($t0)    // '0' char = 48 but we used 0; correct to ascii
    sb    $48, 0($t0)
    addiu $t0, $t0, 1
    addiu $v0, $v0, 1
    addiu $t1, $t1, 1
    addiu $t3, $t3, -1
    j    :spf_loop
    nop

spf_finish_write:
    // ensure NUL if room
    beq   $t3, $zero, :spf_return
    sb    $zero, 0($t0)
spf_return:
    jr    $ra
    nop