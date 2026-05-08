// CRC32 (IEEE 802.3) bitwise implementation
// Usage:
//   include "StdLib/crc32.cds"
//
// Convention:
//   $a0 = ptr to buffer
//   $a1 = length (bytes)
//   returns: $v0 = CRC32 (uint32)
//
// Polynomial: 0xEDB88320 (reversed). Loaded once into $t5 before the byte loop.

lib_crc32:
    // crc = 0xFFFFFFFF
    addiu $v0, $zero, -1            // sign-extends to 0xFFFFFFFF
    // load polynomial mask into $t5 once  (use lui+ori; lo16 bit 15 set)
    lui   $t5, $EDB8
    ori   $t5, $t5, $8320
    beq   $a1, $zero, :crc_finalize
    nop
crc_loop_byte:
    lb    $t0, 0($a0)
    addiu $a0, $a0, 1
    xor   $v0, $v0, $t0             // crc ^= byte
    addi  $t1, $zero, 8             // bit counter
crc_bit_loop:
    andi  $t2, $v0, 1
    beq   $t2, $zero, :crc_shift_only
    nop
    // crc = (crc >> 1) ^ 0xEDB88320
    srl   $v0, $v0, 1
    xor   $v0, $v0, $t5
    j     :crc_continue_from_shift
    nop
crc_shift_only:
    srl   $v0, $v0, 1
crc_continue_from_shift:
    addiu $t1, $t1, -1
    bne   $t1, $zero, :crc_bit_loop
    nop
    addiu $a1, $a1, -1
    bne   $a1, $zero, :crc_loop_byte
    nop
crc_finalize:
    // final xor with 0xFFFFFFFF (invert)
    nor   $v0, $v0, $zero
    jr    $ra
    nop
