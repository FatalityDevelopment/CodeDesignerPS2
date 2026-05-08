// FNV-1a 32-bit string hash
// Usage:
//   include "StdLib/hash.cds"
//
// Convention:
//   $a0 = ptr to null-terminated ASCII string
//   returns: $v0 = 32-bit FNV-1a hash
//
// Algorithm:
//   hash = 2166136261     (0x811C9DC5, FNV offset basis)
//   for each byte b:
//     hash ^= b
//     hash *= 16777619    (0x01000193, FNV prime)

lib_hash_fnv1a:
    // init hash = 0x811C9DC5  (use lui+ori, not setreg, because lo16 bit 15 is set)
    lui   $v0, $811C
    ori   $v0, $v0, $9DC5
    // load FNV prime into $t2 once before the loop  (lo16 bit 15 clear: setreg is fine)
    setreg $t2, $01000193
fnv_loop:
    lb    $t1, 0($a0)
    beq   $t1, $zero, :fnv_done
    nop
    // hash ^= byte
    xor   $v0, $v0, $t1
    // hash *= prime  (32-bit multiply, low 32 bits go to LO via multu)
    multu $v0, $t2
    mflo  $v0
    addiu $a0, $a0, 1
    j     :fnv_loop
    nop
fnv_done:
    jr    $ra
    nop
