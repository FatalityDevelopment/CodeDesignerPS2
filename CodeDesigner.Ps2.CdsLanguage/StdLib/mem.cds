// Memory helpers: memcpy, memset, memcmp, memchr
// Usage: include "StdLib/mem.cds"
// Conventions: $a0..$a3 = args, $v0 = return, clobbers $t0-$t3

// memcpy(dst, src, n) -> returns dst in $v0
lib_memcpy:
    // $a0 = dst, $a1 = src, $a2 = n
    addi $v0, $a0, $0
    beq  $a2, $zero, :memcpy_done
memcpy_loop:
    lb   $t0, 0($a1)
    sb   $t0, 0($a0)
    addiu $a0, $a0, 1
    addiu $a1, $a1, 1
    addiu $a2, $a2, -1
    bne  $a2, $zero, :memcpy_loop
    nop
memcpy_done:
    jr   $ra
    nop

// memset(dst, val, n) -> returns dst in $v0
lib_memset:
    // $a0 = dst, $a1 = val (low byte), $a2 = n
    addi $v0, $a0, $0
    beq  $a2, $zero, :memset_done
memset_loop:
    sb   $a1, 0($a0)
    addiu $a0, $a0, 1
    addiu $a2, $a2, -1
    bne  $a2, $zero, :memset_loop
    nop
memset_done:
    jr   $ra
    nop

// memcmp(p1, p2, n) -> v0 = 0 equal, <0 p1<p2, >0 p1>p2
lib_memcmp:
    // $a0 = p1, $a1 = p2, $a2 = n
    beq  $a2, $zero, :memcmp_equal
memcmp_loop:
    lb   $t0, 0($a0)
    lb   $t1, 0($a1)
    beq  $t0, $t1, :memcmp_next
    subu $v0, $t0, $t1
    jr   $ra
    nop
memcmp_next:
    addiu $a0, $a0, 1
    addiu $a1, $a1, 1
    addiu $a2, $a2, -1
    bne  $a2, $zero, :memcmp_loop
    nop
memcmp_equal:
    addi $v0, $zero, $0
    jr   $ra
    nop

// memchr(ptr, val, n) -> v0 = index (0-based) of first match, or -1 (0xFFFFFFFF)
lib_memchr:
    // $a0 = ptr, $a1 = val (low byte), $a2 = n
    addi $t0, $zero, $0    // idx = 0
    beq  $a2, $zero, :memchr_notfound
memchr_loop:
    lb   $t1, 0($a0)
    beq  $t1, $a1, :memchr_found
    addiu $a0, $a0, 1
    addiu $t0, $t0, 1
    addiu $a2, $a2, -1
    bne  $a2, $zero, :memchr_loop
    nop
memchr_notfound:
    addi $v0, $zero, -1
    jr   $ra
    nop
memchr_found:
    addi $v0, $t0, $0
    jr   $ra
    nop