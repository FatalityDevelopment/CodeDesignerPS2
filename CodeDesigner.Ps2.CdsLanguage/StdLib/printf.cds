// Minimal snprintf-like formatter that consumes an argument array.
// Supports %s (string pointer), %d (signed decimal), %x (hex lower-case).
// Usage:
//   include "StdLib/str_ext.cds"   // for lib_str_itoa
//   include "StdLib/printf.cds"
//
// Conventions:
//   a0 = dst (buffer)
//   a1 = dstSize (bytes)
//   a2 = fmt (pointer to null-terminated format string)
//   a3 = argsPtr (pointer to uint32_t argument array)
//   returns: v0 = number of bytes written (excluding final NUL)
//
// Internal scratch use is restricted to $t0-$t9 and $at.

// lib_str_itoa lives in str_ext.cds; includes are only ever inlined once, so this is
// safe even when the caller already included str_ext.cds itself.
include "str_ext.cds"

    // Temporary 16-byte buffer for numeric conversions.
_fmt_tmpbuf:
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

lib_sprintf_args:
    addi  $v0, $zero, 0          // written
    beq   $a1, $zero, :spf_done
    nop
    addu  $t0, $a0, $zero        // dstPtr
    addu  $t1, $a2, $zero        // fmtPtr
    addu  $t2, $a3, $zero        // argsPtr
    addu  $t3, $a1, $zero        // remaining size
    addi  $t4, $zero, 0          // argIndex offset in bytes
spf_loop:
    lb    $t5, 0($t1)
    beq   $t5, $zero, :spf_finish_write
    addiu $t6, $zero, 37         // '%' = 0x25 = 37
    beq   $t5, $t6, :spf_percent
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
    addiu $t1, $t1, 1            // skip '%'
    lb    $t6, 0($t1)
    beq   $t6, $zero, :spf_finish_write
    nop
    // load next arg: lw argVal, 0(argsPtr + t4)
    addu  $t7, $t2, $t4
    lw    $t8, 0($t7)
    // advance arg index by 4
    addiu $t4, $t4, 4
    // dispatch on specifier
    addiu $t9, $zero, 115        // 's'
    beq   $t6, $t9, :spf_spec_s
    addiu $t9, $zero, 100        // 'd'
    beq   $t6, $t9, :spf_spec_d
    addiu $t9, $zero, 120        // 'x'
    beq   $t6, $t9, :spf_spec_x
    // unknown specifier: emit it literally
    beq   $t3, $zero, :spf_finish_write
    sb    $t6, 0($t0)
    addiu $t0, $t0, 1
    addiu $v0, $v0, 1
    addiu $t1, $t1, 1
    addiu $t3, $t3, -1
    j     :spf_loop
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
    j     :spf_copy_s
    nop
spf_spec_s_done:
    addiu $t1, $t1, 1
    j     :spf_loop
    nop

spf_spec_d:
    // decimal: call lib_str_itoa to format $t8 into _fmt_tmpbuf, then copy result
    addi  $a0, $t8, 0            // int value
    la    $a1, :_fmt_tmpbuf
    jal   :lib_str_itoa
    nop
    // returned dst in v0 (pointer)
    addi  $t9, $v0, 0
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
    j     :spf_copy_s_from_tmp
    nop
spf_spec_d_done:
    addiu $t1, $t1, 1
    j     :spf_loop
    nop

spf_spec_x:
    // hex: convert $t8 to hex string in _fmt_tmpbuf (digits emitted reversed),
    // then copy in the original order back into the dst buffer.
    la    $t9, :_fmt_tmpbuf      // tmp write ptr
    addi  $t7, $t8, 0            // value being shifted; preserves $t8
    // if value == 0, write '0'
    beq   $t7, $zero, :spf_write_hex_zero
    nop
spf_hex_loop:
    andi  $t6, $t7, 0xF
    // convert nibble to ascii
    slti  $at, $t6, 10
    bne   $at, $zero, :spf_hex_digit_num
    nop
    addiu $t5, $t6, 87           // 'a' - 10  (a..f)
    j     :spf_hex_store
    nop
spf_hex_digit_num:
    addiu $t5, $t6, 48           // '0'..'9'
spf_hex_store:
    sb    $t5, 0($t9)
    addiu $t9, $t9, 1
    srl   $t7, $t7, 4
    bne   $t7, $zero, :spf_hex_loop
    nop
    // step $t9 back to last digit, then walk backwards copying into dst
    addiu $t9, $t9, -1
spf_hex_rev_loop:
    // stop when we've walked past the buffer start
    la    $at, :_fmt_tmpbuf
    slt   $at, $t9, $at          // $t9 < base?
    bne   $at, $zero, :spf_hex_rev_done
    nop
    beq   $t3, $zero, :spf_finish_write
    nop
    lb    $t5, 0($t9)
    sb    $t5, 0($t0)
    addiu $t0, $t0, 1
    addiu $t3, $t3, -1
    addiu $v0, $v0, 1
    addiu $t9, $t9, -1
    j     :spf_hex_rev_loop
    nop
spf_hex_rev_done:
    addiu $t1, $t1, 1
    j     :spf_loop
    nop

spf_write_hex_zero:
    beq   $t3, $zero, :spf_finish_write
    addiu $t5, $zero, 48   // '0' character (ASCII 48)
    sb    $t5, 0($t0)
    addiu $t0, $t0, 1
    addiu $v0, $v0, 1
    addiu $t1, $t1, 1
    addiu $t3, $t3, -1
    j     :spf_loop
    nop

spf_finish_write:
    // ensure NUL if room
    beq   $t3, $zero, :spf_done
    nop
    sb    $zero, 0($t0)
spf_done:
    jr    $ra
    nop
