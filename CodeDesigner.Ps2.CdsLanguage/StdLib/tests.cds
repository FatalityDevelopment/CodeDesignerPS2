// Quick tests / examples for StdLib routines.
// Include the libraries you want to exercise.
include "StdLib/string.cds"
include "StdLib/strutils.cds"
include "StdLib/mem.cds"
include "StdLib/str_ext.cds"
include "StdLib/hash.cds"
include "StdLib/crc32.cds"
include "StdLib/allocator.cds"
include "StdLib/split.cds"
include "StdLib/printf.cds"

// Data area for tests
address $80101000
test_src:
    string "one,two,three"
test_outbuf:
    // reserve 64 bytes
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

test_ptrs:
    // space for 8 pointers
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

main_test:
    // Test split
    setreg $a0, $80101000
    addiu $a1, $zero, $2C   // ',' ascii = 0x2C
    setreg $a2, $80101040   // test_ptrs
    addiu $a3, $zero, $8
    jal :lib_str_split
    nop
    // v0 = count

    // Test hash
    setreg $a0, $80101000
    addiu $a1, $zero, $0
    jal :lib_hash_fnv1a
    nop

    // Test crc32 of the source string (length 13)
    setreg $a0, $80101000
    addiu $a1, $zero, $0D
    jal :lib_crc32
    nop

    // heap init (use some memory at 0x80101100)
    setreg $a0, $80101100
    addiu $a1, $zero, $400   // 1KB heap
    jal :lib_heap_init
    nop

    // allocate 16 bytes
    addiu $a0, $zero, $10
    jal :lib_malloc
    nop

    // format string example
    setreg $a0, $80101020  // dst
    addiu $a1, $zero, $40  // size
    setreg $a2, $80101060  // fmt string label (below)
    setreg $a3, $80101080  // args array
    jal :lib_sprintf_args
    nop

    j :main_test
    nop

// Format & args data
address $80101060
fmt_example:
    string "split_count=%d crc=%x name=%s"
address $80101080
args_arr:
    hexcode $00000000    // place-holders: parser will fill at runtime in tests if desired
    hexcode $00000000
    hexcode $00000000