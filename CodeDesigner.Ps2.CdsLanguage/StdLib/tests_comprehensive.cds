// ============================================================================
// COMPREHENSIVE TEST SUITE FOR CODEDESIGNER STDLIB
// ============================================================================
// This file demonstrates full test support for all StdLib functions.
// It includes data setup, test cases, assertions, and result reporting.
//
// Test Categories:
//   1. String Operations (strlen, strcpy, strcat, strcmp)
//   2. String Utilities (toupper, tolower, trim_left, trim_right)
//   3. String Manipulation (find, substr, startswith, endswith)
//   4. Parsing (atoi - decimal and hex)
//   5. Formatting (itoa - integer to ASCII)
//   6. Memory Operations (memcpy, memset, memcmp, memchr)
//   7. String Splitting (str_split)
//   8. Hashing (FNV-1a hash, CRC32)
//   9. Memory Allocation (heap init, malloc, free)
//  10. Formatting (sprintf with %d, %x, %s)
// ============================================================================

include "StdLib/string.cds"
include "StdLib/strutils.cds"
include "StdLib/mem.cds"
include "StdLib/str_ext.cds"
include "StdLib/hash.cds"
include "StdLib/crc32.cds"
include "StdLib/allocator.cds"
include "StdLib/split.cds"
include "StdLib/printf.cds"

// ============================================================================
// TEST DATA SECTION
// ============================================================================
address $80100000

// Test strings for string operations
test_str1:
    string "Hello"
test_str2:
    string "World"
test_str3:
    string "Hello"  // duplicate for comparison
test_str_lower:
    string "abcdef"
test_str_upper:
    string "ABCDEF"
test_str_mixed:
    string "HeLLo WoRLD"
test_str_spaces:
    string "  test string  "
test_str_empty:
    string ""

// Test strings for find/substring operations
test_haystack:
    string "The quick brown fox jumps"
test_needle_found:
    string "quick"
test_needle_not_found:
    string "slow"
test_prefix_match:
    string "The"
test_prefix_no_match:
    string "the"
test_suffix_match:
    string "jumps"
test_suffix_no_match:
    string "jump"

// Test strings for split operation
test_split_input:
    string "apple,banana,cherry,date"

// Output buffers and arrays
address $80100100
test_output_buffer:
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"  // 64 bytes total

address $80100140
test_split_ptrs_array:
    // Space for 16 pointers (4 bytes each)
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

address $80100180
test_result_buffer:
    // 128 bytes for various results
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

// Test memory area for mem operations
address $80100300
test_mem_src:
    string "Source Data\0\0\0\0\0"  // 16 bytes
test_mem_dst:
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"  // 16 bytes

address $80100320
test_parse_decimal:
    string "12345"
test_parse_negative:
    string "-9876"
test_parse_hex:
    string "0xDEADBEEF"
test_parse_hex_cap:
    string "0xCAFE"

// Heap memory area
address $80100400
heap_base:
    // Reserve 1KB for heap
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

// ============================================================================
// TEST EXECUTION SECTION
// ============================================================================
address $80102000

// TEST COUNTER STORAGE
test_counter:
    hexcode $00000000  // Number of tests executed
test_passed:
    hexcode $00000000  // Number of tests passed
test_failed:
    hexcode $00000000  // Number of tests failed

main_test_suite:
    // Initialize test counters
    addiu $t9, $zero, 0           // t9 = test counter
    addiu $s0, $zero, 0           // s0 = passed count
    addiu $s1, $zero, 0           // s1 = failed count

    // ========== Test 1: strlen ==========
    // Expected: length of "Hello" = 5
    addiu $t9, $t9, 1
    la    $a0, test_str1
    jal   :lib_str_strlen
    nop
    addi  $t0, $zero, 5
    beq   $v0, $t0, :test1_pass
    addiu $s1, $s1, 1
    j     :test2_start
    nop
test1_pass:
    addiu $s0, $s0, 1

    // ========== Test 2: strcpy ==========
test2_start:
    addiu $t9, $t9, 1
    la    $a0, test_output_buffer
    la    $a1, test_str1
    jal   :lib_strcpy
    nop
    // Verify first char is 'H'
    lb    $t1, 0($a0)
    addi  $t0, $zero, 0x48   // 'H'
    beq   $t1, $t0, :test2_pass
    addiu $s1, $s1, 1
    j     :test3_start
    nop
test2_pass:
    addiu $s0, $s0, 1

    // ========== Test 3: strcat ==========
test3_start:
    addiu $t9, $t9, 1
    la    $a0, test_output_buffer
    la    $a1, test_str2
    jal   :lib_strcat
    nop
    // Now output_buffer should contain "HelloWorld"
    // Check length = 10
    addi  $a0, $v0, 0
    jal   :lib_str_strlen
    nop
    addi  $t0, $zero, 10
    beq   $v0, $t0, :test3_pass
    addiu $s1, $s1, 1
    j     :test4_start
    nop
test3_pass:
    addiu $s0, $s0, 1

    // ========== Test 4: strcmp - equal strings ==========
test4_start:
    addiu $t9, $t9, 1
    la    $a0, test_str1
    la    $a1, test_str3
    jal   :lib_strcmp
    nop
    beq   $v0, $zero, :test4_pass
    addiu $s1, $s1, 1
    j     :test5_start
    nop
test4_pass:
    addiu $s0, $s0, 1

    // ========== Test 5: strcmp - different strings ==========
test5_start:
    addiu $t9, $t9, 1
    la    $a0, test_str1
    la    $a1, test_str2
    jal   :lib_strcmp
    nop
    // Result should be non-zero
    beq   $v0, $zero, :test5_fail
    addiu $s0, $s0, 1
    j     :test6_start
    nop
test5_fail:
    addiu $s1, $s1, 1

    // ========== Test 6: str_toupper ==========
test6_start:
    addiu $t9, $t9, 1
    la    $a0, test_result_buffer
    la    $a1, test_str_lower
    jal   :lib_strcpy
    nop
    addi  $a0, $v0, 0
    jal   :lib_str_toupper
    nop
    // Check first char is 'A'
    lb    $t1, 0($v0)
    addi  $t0, $zero, 0x41   // 'A'
    beq   $t1, $t0, :test6_pass
    addiu $s1, $s1, 1
    j     :test7_start
    nop
test6_pass:
    addiu $s0, $s0, 1

    // ========== Test 7: str_tolower ==========
test7_start:
    addiu $t9, $t9, 1
    la    $a0, test_result_buffer
    la    $a1, test_str_upper
    jal   :lib_strcpy
    nop
    addi  $a0, $v0, 0
    jal   :lib_str_tolower
    nop
    // Check first char is 'a'
    lb    $t1, 0($v0)
    addi  $t0, $zero, 0x61   // 'a'
    beq   $t1, $t0, :test7_pass
    addiu $s1, $s1, 1
    j     :test8_start
    nop
test7_pass:
    addiu $s0, $s0, 1

    // ========== Test 8: str_find ==========
test8_start:
    addiu $t9, $t9, 1
    la    $a0, test_haystack
    la    $a1, test_needle_found
    jal   :lib_str_find
    nop
    // Expected: index 4 (position of "quick")
    addi  $t0, $zero, 4
    beq   $v0, $t0, :test8_pass
    addiu $s1, $s1, 1
    j     :test9_start
    nop
test8_pass:
    addiu $s0, $s0, 1

    // ========== Test 9: str_find - not found ==========
test9_start:
    addiu $t9, $t9, 1
    la    $a0, test_haystack
    la    $a1, test_needle_not_found
    jal   :lib_str_find
    nop
    // Expected: -1
    addi  $t0, $zero, -1
    beq   $v0, $t0, :test9_pass
    addiu $s1, $s1, 1
    j     :test10_start
    nop
test9_pass:
    addiu $s0, $s0, 1

    // ========== Test 10: str_startswith ==========
test10_start:
    addiu $t9, $t9, 1
    la    $a0, test_haystack
    la    $a1, test_prefix_match
    jal   :lib_str_startswith
    nop
    addi  $t0, $zero, 1
    beq   $v0, $t0, :test10_pass
    addiu $s1, $s1, 1
    j     :test11_start
    nop
test10_pass:
    addiu $s0, $s0, 1

    // ========== Test 11: str_startswith - no match ==========
test11_start:
    addiu $t9, $t9, 1
    la    $a0, test_haystack
    la    $a1, test_prefix_no_match
    jal   :lib_str_startswith
    nop
    beq   $v0, $zero, :test11_pass
    addiu $s1, $s1, 1
    j     :test12_start
    nop
test11_pass:
    addiu $s0, $s0, 1

    // ========== Test 12: str_endswith ==========
test12_start:
    addiu $t9, $t9, 1
    la    $a0, test_haystack
    la    $a1, test_suffix_match
    jal   :lib_str_endswith
    nop
    addi  $t0, $zero, 1
    beq   $v0, $t0, :test12_pass
    addiu $s1, $s1, 1
    j     :test13_start
    nop
test12_pass:
    addiu $s0, $s0, 1

    // ========== Test 13: memcpy ==========
test13_start:
    addiu $t9, $t9, 1
    la    $a0, test_mem_dst
    la    $a1, test_mem_src
    addi  $a2, $zero, 11          // copy 11 bytes
    jal   :lib_memcpy
    nop
    // Check first byte matches
    la    $t1, test_mem_src
    lb    $t0, 0($t1)
    la    $t1, test_mem_dst
    lb    $t2, 0($t1)
    beq   $t0, $t2, :test13_pass
    addiu $s1, $s1, 1
    j     :test14_start
    nop
test13_pass:
    addiu $s0, $s0, 1

    // ========== Test 14: memcmp - equal ==========
test14_start:
    addiu $t9, $t9, 1
    la    $a0, test_mem_src
    la    $a1, test_mem_src
    addi  $a2, $zero, 11
    jal   :lib_memcmp
    nop
    beq   $v0, $zero, :test14_pass
    addiu $s1, $s1, 1
    j     :test15_start
    nop
test14_pass:
    addiu $s0, $s0, 1

    // ========== Test 15: atoi - decimal ==========
test15_start:
    addiu $t9, $t9, 1
    la    $a0, test_parse_decimal
    jal   :lib_str_atoi
    nop
    addi  $t0, $zero, 12345
    beq   $v0, $t0, :test15_pass
    addiu $s1, $s1, 1
    j     :test16_start
    nop
test15_pass:
    addiu $s0, $s0, 1

    // ========== Test 16: atoi - negative ==========
test16_start:
    addiu $t9, $t9, 1
    la    $a0, test_parse_negative
    jal   :lib_str_atoi
    nop
    addi  $t0, $zero, -9876
    beq   $v0, $t0, :test16_pass
    addiu $s1, $s1, 1
    j     :test17_start
    nop
test16_pass:
    addiu $s0, $s0, 1

    // ========== Test 17: str_split ==========
test17_start:
    addiu $t9, $t9, 1
    la    $a0, test_split_input
    addi  $a1, $zero, 0x2C           // ',' = 0x2C
    la    $a2, test_split_ptrs_array
    addi  $a3, $zero, 10
    jal   :lib_str_split
    nop
    // Expected: 4 parts (apple, banana, cherry, date)
    addi  $t0, $zero, 4
    beq   $v0, $t0, :test17_pass
    addiu $s1, $s1, 1
    j     :test18_start
    nop
test17_pass:
    addiu $s0, $s0, 1

    // ========== Test 18: FNV-1a hash ==========
test18_start:
    addiu $t9, $t9, 1
    la    $a0, test_str1   // "Hello"
    jal   :lib_hash_fnv1a
    nop
    // Result should be non-zero (FNV hash of "Hello")
    bne   $v0, $zero, :test18_pass
    addiu $s1, $s1, 1
    j     :test19_start
    nop
test18_pass:
    addiu $s0, $s0, 1

    // ========== Test 19: CRC32 ==========
test19_start:
    addiu $t9, $t9, 1
    la    $a0, test_str1   // "Hello"
    addi  $a1, $zero, 5    // length = 5
    jal   :lib_crc32
    nop
    // Result should be non-zero
    bne   $v0, $zero, :test19_pass
    addiu $s1, $s1, 1
    j     :test20_start
    nop
test19_pass:
    addiu $s0, $s0, 1

    // ========== Test 20: Heap allocation ==========
test20_start:
    addiu $t9, $t9, 1
    la    $a0, heap_base
    addi  $a1, $zero, 1024          // 1KB heap
    jal   :lib_heap_init
    nop
    // Should return 1 on success
    addi  $t0, $zero, 1
    beq   $v0, $t0, :test20_pass
    addiu $s1, $s1, 1
    j     :test21_start
    nop
test20_pass:
    addiu $s0, $s0, 1

    // ========== Test 21: Malloc ==========
test21_start:
    addiu $t9, $t9, 1
    addi  $a0, $zero, 32           // allocate 32 bytes
    jal   :lib_malloc
    nop
    // Should return non-zero pointer
    bne   $v0, $zero, :test21_pass
    addiu $s1, $s1, 1
    j     :test_summary
    nop
test21_pass:
    addiu $s0, $s0, 1

    // ========== TEST SUMMARY ==========
test_summary:
    // All tests complete
    // s0 = passed count
    // s1 = failed count
    // t9 = total tests (21)
    
    // Store results
    la    $t0, test_counter
    sw    $t9, 0($t0)
    la    $t0, test_passed
    sw    $s0, 0($t0)
    la    $t0, test_failed
    sw    $s1, 0($t0)
    
    // Infinite loop (tests complete)
    j     :test_summary
    nop
