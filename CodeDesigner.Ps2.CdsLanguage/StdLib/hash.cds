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
    addiu $v0, $zero, 0            // clear v0
    // load 2166136261 (0x811C9DC5)
    addiu $v0, $zero, 0x9DC5       // low half
    sll   $t0, $v0, 16
    addiu $v0, $t0, 0x811C         // now v0 = 0x811C9DC5 (assembled via two immediates)
    // loop over bytes
fnv_loop:
    lb    $t1, 0($a0)
    beq   $t1, $zero, :fnv_done
    // hash ^= b
    xor   $v0, $v0, $t1
    // multiply by 16777619 (0x01000193)
    // perform: hash = hash * 16777619
    // Use 32-bit multiply (mul) result limited to low 32 bits.
    addiu $t2, $zero, 16777619     // assembler accepts decimal immediates here
    mul   $v0, $v0, $t2
    addiu $a0, $a0, 1
    j     :fnv_loop
    nop
fnv_done:
    jr    $ra
    nop