// CRC32 (IEEE 802.3) bitwise implementation
// Usage:
//   include "StdLib/crc32.cds"
//
// Convention:
//   $a0 = ptr to buffer
//   $a1 = length (bytes)
//   returns: $v0 = CRC32 (uint32)

lib_crc32:
    // init crc = 0xFFFFFFFF
    addiu $v0, $zero, -1        // v0 = 0xFFFFFFFF
    beq   $a1, $zero, :crc_done_check
crc_loop_byte:
    lb    $t0, 0($a0)           // byte
    addiu $a0, $a0, 1
    beq   $t0, $zero, :crc_process_byte_continue
    // proceed (we still process 0 bytes as valid)
crc_process_byte_continue:
    xor   $v0, $v0, $t0        // crc ^= byte
    // process 8 bits
    addi  $t1, $zero, 8
crc_bit_loop:
    andi  $t2, $v0, 1
    beq   $t2, $zero, :crc_shift_only
    // crc = (crc >> 1) ^ 0xEDB88320
    srl   $v0, $v0, 1
    // Load constant 0xEDB88320 into t5
    addiu $t5, $zero, 0xEDB8
    sll   $t5, $t5, 16
    addiu $t5, $t5, 0x8320        // t5 = 0xEDB88320
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

crc_done_check:
    // final xor with 0xFFFFFFFF (invert)
    nor   $v0, $v0, $zero
    jr    $ra
    nop