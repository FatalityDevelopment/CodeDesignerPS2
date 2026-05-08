// Quick tests / examples for StdLib routines.
// Demonstrates the natural call-site syntax made available by stdlib.cds.

include "StdLib/stdlib.cds"        // function declarations
include "StdLib/string.cds"
include "StdLib/strutils.cds"
include "StdLib/mem.cds"
include "StdLib/str_ext.cds"
include "StdLib/hash.cds"
include "StdLib/crc32.cds"
include "StdLib/allocator.cds"
include "StdLib/split.cds"
include "StdLib/printf.cds"

// --- Data area for tests ----------------------------------------------------
address $80101000
test_src:
    string "one,two,three"
test_outbuf:
    // 48 bytes of zero-filled output buffer
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

test_ptrs:
    // space for 8 pointers (32 bytes)
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

main_test:
    // ─ str_split(test_src, ',', test_ptrs, 8) ──────────────────────────────
    setreg $t0, $80101000               // src pointer
    addiu  $t1, $zero, 44               // ',' (decimal)
    setreg $t2, $80101040               // out array
    addiu  $t3, $zero, 8                // maxParts
    str_split(t0, t1, t2, t3)

    // ─ hash_fnv1a(test_src) ────────────────────────────────────────────────
    setreg $t0, $80101000
    hash_fnv1a(t0)

    // ─ crc32(test_src, 13) ─────────────────────────────────────────────────
    setreg $t0, $80101000
    addiu  $t1, $zero, 13
    crc32(t0, t1)

    // ─ heap_init(0x80101100, 1024) ─────────────────────────────────────────
    setreg $t0, $80101100
    addiu  $t1, $zero, 1024
    heap_init(t0, t1)

    // ─ malloc(16) ──────────────────────────────────────────────────────────
    addiu  $t0, $zero, 16
    malloc(t0)

    // ─ sprintf(dst=0x80101020, size=64, fmt, args) ─────────────────────────
    setreg $t0, $80101020
    addiu  $t1, $zero, 64
    setreg $t2, $80101060
    setreg $t3, $80101080
    sprintf(t0, t1, t2, t3)

    j :main_test
    nop

// Format & args data
address $80101060
fmt_example:
    string "split_count=%d crc=%x name=%s"

address $80101080
args_arr:
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
