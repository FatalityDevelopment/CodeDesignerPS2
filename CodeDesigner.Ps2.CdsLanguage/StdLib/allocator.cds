// Simple bump allocator (no free). Caller must reserve heap storage in memory
// and call lib_heap_init once before the first lib_malloc.
// Usage:
//   include "StdLib/allocator.cds"
//
// Conventions:
//   lib_heap_init(a0=base, a1=size) -> v0 = 1 on success, 0 on failure
//   lib_malloc   (a0=size)          -> v0 = pointer or 0 on failure (size aligned to 4)
//   lib_free     (a0=ptr)           -> no-op (returns 1)

    // Heap state: two words [cur, end]. Loaded/stored via la-resolved address.
_heap_state:
    hexcode $00000000              // [0]: cur
    hexcode $00000000              // [4]: end

lib_heap_init:
    // a0 = base, a1 = size
    addi  $v0, $zero, 0
    beq   $a0, $zero, :heap_init_fail
    addiu $t0, $a0, 0
    la    $t9, :_heap_state        // load heap-state storage address
    sw    $t0, 0($t9)              // store cur
    addu  $t1, $a0, $a1
    sw    $t1, 4($t9)              // end = base + size
    addi  $v0, $zero, 1
    jr    $ra
    nop
heap_init_fail:
    addi  $v0, $zero, 0
    jr    $ra
    nop

lib_malloc:
    // a0 = size; size is aligned up to 4 bytes before allocation.
    addi  $v0, $zero, 0
    beq   $a0, $zero, :malloc_zero_return
    nop
    la    $t9, :_heap_state
    lw    $t0, 0($t9)              // cur
    lw    $t1, 4($t9)              // end
    // align size up to 4
    addiu $t3, $a0, 3
    addi  $t4, $zero, -4
    and   $t3, $t3, $t4
    addu  $t2, $t0, $t3            // new_cur = cur + alignedSize
    // fail if new_cur > end (i.e. NOT (new_cur < end || new_cur == end))
    sltu  $t4, $t1, $t2            // 1 if end < new_cur
    bne   $t4, $zero, :malloc_fail
    nop
    sw    $t2, 0($t9)              // store new cur
    addu  $v0, $t0, $zero          // return old cur (move)
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
