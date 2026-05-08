// Demo file showing native CDS function definitions.
// Each `function NAME(p1, p2, ...) { ... }` block:
//   - Defines a label NAME at the function entry.
//   - Aliases each parameter to $a0..$a3 inside the body.
//   - Auto-emits a stack-frame prologue saving $ra (if the body calls another
//     function via jal/jalr) and any callee-saved register ($s0..$s7, $fp)
//     that the body touches. Frame size is rounded up to a 16-byte multiple
//     per the PS2 EE ABI.
//   - Auto-emits the matching epilogue: restore saves, drop the frame,
//     `jr $ra` + delay-slot `nop`.
//
// Once defined, the function is callable with `NAME(arg1, arg2, ...)` from
// anywhere in the same document — including before the definition appears.
// Argument loading is collision-safe; cross-arg cycles are routed through $at.

include "StdLib/stdlib.cds"
include "StdLib/string.cds"

address $80100000

// ─ Leaf function: no callee-saved regs, no calls → no frame at all. ─────
function add_three(a, b, c) {
    addu $v0, a, b
    addu $v0, $v0, c
}

// ─ Function that uses callee-saved registers — auto-saves them. ─────────
function sum_first_n(n) {
    addiu $s0, $zero, 0          // accumulator
    addiu $s1, $zero, 1          // counter
sum_loop:
    slt   $at, n, $s1
    bne   $at, $zero, :sum_done
    nop
    addu  $s0, $s0, $s1
    addiu $s1, $s1, 1
    j     :sum_loop
    nop
sum_done:
    addu  $v0, $s0, $zero
}

// ─ Function that calls another function — auto-saves $ra and $s0. ──────
function copy_then_count(dst, src) {
    str_copy(dst, src)           // jal :lib_strcpy + arg loads
    addu $s0, dst, $zero         // save dst (callee-saved register)
    str_len(s0)                  // jal :lib_str_strlen
}

// ─ Caller using the functions defined above. ────────────────────────────
main:
    addiu $a0, $zero, 10
    addiu $a1, $zero, 20
    addiu $a2, $zero, 30
    add_three(a0, a1, a2)        // expects $v0 = 60

    addiu $a0, $zero, 5
    sum_first_n(a0)              // expects $v0 = 15  (1+2+3+4+5)

    j :main
    nop
