// FNV-1a 32-bit string hash
// Usage:
//   include "StdLib/hash.cds"
//
// Convention:
//   $a0 = ptr to null-terminated ASCII string
//   returns: $v0 = 32-bit FNV-1a hash
//
// Algorithm:
//   hash = 2166136261
//   for each byte b:
//     hash ^= b
//     hash *= 16777619

lib_hash_fnv1a:
    // init
    addiu $v0, $zero, 0xFFFF       // load high bits (0xFFFF for sign-extended load)
    sll   $v0, $v0, 16             // shift to upper half
    addiu $v0, $v0, 0x9DC5         // add low bits: 0xFFFF0000 + 0x9DC5 = 0xFFFF9DC5 (sign-extended 0x811C9DC5)
    // loop over bytes
fnv_loop:
    lb    $t1, 0($a0)
    beq   $t1, $zero, :fnv_done
    // hash ^= b
    xor   $v0, $v0, $t1
    // multiply by 16777619 (0x01000193)
    // perform: hash = hash * 16777619
    // Load constant via addiu + shift
    addiu $t2, $zero, 0x0193       // load low half
    addiu $t3, $zero, 0x0100       // load high half
    sll   $t3, $t3, 16             // shift high to upper position
    addu  $t2, $t2, $t3            // combine: 0x0100 << 16 | 0x0193 = 0x01000193
    mult  $v0, $t2                 // multiply
    mflo  $v0                      // get lower 32 bits
    addiu $a0, $a0, 1
    j     :fnv_loop
    nop
fnv_done:
    jr    $ra
    nop