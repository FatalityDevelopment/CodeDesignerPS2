// Simple bump allocator (no free). Caller must reserve heap storage in memory.
// Usage:
//   include "StdLib/allocator.cds"
//
// Conventions:
//   lib_heap_init(a0=base, a1=size) -> v0 = 1 on success
//   lib_malloc(a0=size) -> v0 = pointer or 0 on failure
//   lib_free(a0=ptr) -> no-op (returns 1)

    // Data: heap state (two words: cur, end)
_heap_state:
    hexcode $00000000
    hexcode $00000000
    // labels:
_heap_cur:    // points to current allocation pointer (word stored at _heap_state)
    // alias to first word
_heap_end:    // alias to second word

lib_heap_init:
    // a0 = base, a1 = size
    addi  $v0, $zero, $0
    beq   $a0, $zero, :heap_init_fail
    addiu $t0, $a0, $zero
    sw    $t0, 0(_heap_state)        // store cur
    addu  $t1, $a0, $a1
    sw    $t1, 4(_heap_state)        // store end
    addi  $v0, $zero, 1
    jr    $ra
    nop
heap_init_fail:
    addi  $v0, $zero, 0
    jr    $ra
    nop

lib_malloc:
    // a0 = size
    addi  $v0, $zero, 0
    beq   $a0, $zero, :malloc_zero_return
    lw    $t0, 0(_heap_state)    // cur
    lw    $t1, 4(_heap_state)    // end
    addu  $t2, $t0, $a0          // new_cur = cur + size
    // align to 4
    addiu $t3, $a0, 3
    andi  $t3, $t3, -4
    addu  $t2, $t0, $t3
    sltu  $t4, $t2, $t1
    beq   $t4, $zero, :malloc_fail
    sw    $t2, 0(_heap_state)    // update cur
    addi  $v0, $t0, $zero        // return old cur
    jr    $ra
    nop
malloc_fail:
    addi  $v0, $zero, 0
    jr    $ra
    nop
malloc_zero_return:
    addi  $v0, $zero, 0
    jr    $ra
    nop

lib_free:
    // no-op
    addi $v0, $zero, 1
    jr   $ra
    nop