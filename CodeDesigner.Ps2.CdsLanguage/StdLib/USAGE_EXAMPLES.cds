// ============================================================================
// CODEDESIGNER STDLIB - COMPLETE USAGE EXAMPLES
// ============================================================================
// This file contains practical code examples for every function in the library.
// Each example is self-contained and demonstrates best practices.
// ============================================================================

// ============================================================================
// 1. STRING OPERATIONS - string.cds
// ============================================================================

// Example 1.1: strlen - Get string length
// Returns length in $v0 (excludes terminator)
example_strlen:
    la    $a0, mystring        // $a0 = pointer to string
    jal   :lib_str_strlen      // call strlen
    nop
    // $v0 now contains length
    // Example: "Hello" -> $v0 = 5

// Example 1.2: strcpy - Copy string (source to destination)
// Returns destination pointer in $v0
example_strcpy:
    la    $a0, destination     // $a0 = dst buffer
    la    $a1, source          // $a1 = src string
    jal   :lib_strcpy          // copy src -> dst
    nop
    // $v0 = destination pointer
    // destination now contains copy of source

// Example 1.3: strcat - Concatenate strings
// Appends src to end of dst (assumes dst has room)
example_strcat:
    la    $a0, buffer          // $a0 = dst string (with existing content)
    la    $a1, append_me       // $a1 = string to append
    jal   :lib_strcat          // concatenate
    nop
    // $v0 = destination pointer
    // buffer now contains original + appended

// Example 1.4: strcmp - Compare strings lexicographically
// Returns: 0 = equal, <0 = s1<s2, >0 = s1>s2
example_strcmp:
    la    $a0, string1         // $a0 = first string
    la    $a1, string2         // $a1 = second string
    jal   :lib_strcmp          // compare
    nop
    // $v0 = comparison result
    beq   $v0, $zero, :strings_equal
    blt   $v0, $zero, :string1_less
    j     :string1_greater
    nop
strings_equal:
    // strings are equal
    j     :end_strcmp
    nop
string1_less:
    // string1 < string2
    j     :end_strcmp
    nop
string1_greater:
    // string1 > string2
end_strcmp:
    nop

// Example 1.5: str_replace_char - Replace character in string
// Replaces all occurrences of oldChar with newChar
example_replace_char:
    la    $a0, mytext          // $a0 = string
    addi  $a1, $zero, 0x2D    // $a1 = '-' (old character)
    addi  $a2, $zero, 0x5F    // $a2 = '_' (new character)
    jal   :lib_str_replace_char
    nop
    // $v0 = number of replacements made

// ============================================================================
// 2. STRING UTILITIES - strutils.cds
// ============================================================================

// Example 2.1: str_toupper - Convert to uppercase (in-place)
// Modifies string in memory, no allocation needed
example_toupper:
    la    $a0, mytext          // $a0 = string pointer
    jal   :lib_str_toupper     // convert to uppercase
    nop
    // $v0 = same pointer as input
    // mytext is now uppercase

// Example 2.2: str_tolower - Convert to lowercase (in-place)
example_tolower:
    la    $a0, mytext          // $a0 = string pointer
    jal   :lib_str_tolower     // convert to lowercase
    nop
    // $v0 = same pointer as input
    // mytext is now lowercase

// Example 2.3: str_trim_left - Remove leading spaces
example_trim_left:
    la    $a0, text_with_spaces
    jal   :lib_str_trim_left
    nop
    // $v0 = pointer to string (at base)
    // string shifted left, leading spaces removed

// Example 2.4: str_trim_right - Remove trailing spaces
example_trim_right:
    la    $a0, text_with_spaces
    jal   :lib_str_trim_right
    nop
    // $v0 = pointer to string (at base)
    // trailing spaces removed

// ============================================================================
// 3. STRING SEARCH & MANIPULATION - str_ext.cds
// ============================================================================

// Example 3.1: str_find - Find substring
// Returns index of first occurrence or -1 if not found
example_find:
    la    $a0, haystack        // $a0 = string to search in
    la    $a1, needle          // $a1 = substring to find
    jal   :lib_str_find        // find
    nop
    // $v0 = index (0-based) or -1 if not found
    addi  $t0, $zero, -1
    beq   $v0, $t0, :not_found
    // found at index $v0

// Example 3.2: str_substr - Extract substring
// Copies len bytes starting at start index
example_substr:
    la    $a0, source_string   // $a0 = source
    la    $a1, dest_buffer     // $a1 = destination
    addi  $a2, $zero, 5        // $a2 = start index
    addi  $a3, $zero, 10       // $a3 = length to copy
    jal   :lib_str_substr      // extract substring
    nop
    // $v0 = destination pointer
    // dest_buffer now contains 10 bytes starting at index 5

// Example 3.3: str_startswith - Check prefix
// Returns 1 if string starts with prefix, 0 otherwise
example_startswith:
    la    $a0, text            // $a0 = string to check
    la    $a1, prefix          // $a1 = prefix to find
    jal   :lib_str_startswith
    nop
    // $v0 = 1 (true) or 0 (false)
    beq   $v0, $zero, :no_prefix
    // string starts with prefix

// Example 3.4: str_endswith - Check suffix
// Returns 1 if string ends with suffix, 0 otherwise
example_endswith:
    la    $a0, text            // $a0 = string to check
    la    $a1, suffix          // $a1 = suffix to find
    jal   :lib_str_endswith
    nop
    // $v0 = 1 (true) or 0 (false)
    beq   $v0, $zero, :no_suffix
    // string ends with suffix

// Example 3.5: str_atoi - Parse string to integer
// Supports decimal and 0x hexadecimal format
example_atoi_decimal:
    la    $a0, decimal_string  // $a0 = "12345"
    jal   :lib_str_atoi
    nop
    // $v0 = 12345

example_atoi_hex:
    la    $a0, hex_string      // $a0 = "0xDEADBEEF"
    jal   :lib_str_atoi
    nop
    // $v0 = 0xDEADBEEF

example_atoi_negative:
    la    $a0, negative_string // $a0 = "-999"
    jal   :lib_str_atoi
    nop
    // $v0 = -999

// Example 3.6: str_itoa - Convert integer to string
// Converts signed integer to decimal ASCII string
example_itoa:
    addi  $a0, $zero, 12345   // $a0 = integer value
    la    $a1, output_buffer   // $a1 = destination buffer
    jal   :lib_str_itoa
    nop
    // $v0 = pointer to output_buffer
    // output_buffer now contains "12345"

// ============================================================================
// 4. MEMORY OPERATIONS - mem.cds
// ============================================================================

// Example 4.1: memcpy - Copy memory block
// Copies n bytes from src to dst
example_memcpy:
    la    $a0, dest            // $a0 = destination
    la    $a1, source          // $a1 = source
    addi  $a2, $zero, 32       // $a2 = number of bytes
    jal   :lib_memcpy
    nop
    // $v0 = destination pointer
    // 32 bytes copied from source to dest

// Example 4.2: memset - Fill memory with value
// Sets n bytes to val
example_memset:
    la    $a0, buffer          // $a0 = buffer to fill
    addi  $a1, $zero, 0xFF     // $a1 = fill byte value
    addi  $a2, $zero, 256      // $a2 = number of bytes
    jal   :lib_memset
    nop
    // $v0 = destination pointer
    // buffer filled with 0xFF

// Example 4.3: memcmp - Compare memory blocks
// Compares n bytes from p1 and p2
example_memcmp:
    la    $a0, buffer1         // $a0 = first buffer
    la    $a1, buffer2         // $a1 = second buffer
    addi  $a2, $zero, 64       // $a2 = number of bytes
    jal   :lib_memcmp
    nop
    // $v0 = 0 (equal), <0 (p1<p2), >0 (p1>p2)
    beq   $v0, $zero, :mem_equal
    blt   $v0, $zero, :mem_first_less
    // first > second

// Example 4.4: memchr - Find byte in memory
// Returns index of first occurrence or -1
example_memchr:
    la    $a0, buffer          // $a0 = buffer to search
    addi  $a1, $zero, 0x42     // $a1 = byte to find
    addi  $a2, $zero, 128      // $a2 = buffer size
    jal   :lib_memchr
    nop
    // $v0 = index (0-based) or -1 if not found

// ============================================================================
// 5. STRING SPLITTING - split.cds
// ============================================================================

// Example 5.1: str_split - Split string by separator
// In-place split (separators replaced with NUL)
example_split:
    la    $a0, input_string    // $a0 = "apple,banana,cherry"
    addi  $a1, $zero, 0x2C     // $a1 = ',' separator
    la    $a2, ptrs_array      // $a2 = output array (word pointers)
    addi  $a3, $zero, 10       // $a3 = max parts
    jal   :lib_str_split
    nop
    // $v0 = number of parts found
    // ptrs_array[0] = pointer to "apple"
    // ptrs_array[1] = pointer to "banana"
    // ptrs_array[2] = pointer to "cherry"
    // Original separators replaced with NUL

// ============================================================================
// 6. HASHING FUNCTIONS - hash.cds & crc32.cds
// ============================================================================

// Example 6.1: FNV-1a hash
// Computes 32-bit FNV-1a hash of null-terminated string
example_fnv1a_hash:
    la    $a0, text_to_hash    // $a0 = string pointer
    jal   :lib_hash_fnv1a      // compute hash
    nop
    // $v0 = 32-bit FNV-1a hash value

// Example 6.2: CRC32 (IEEE 802.3)
// Computes CRC32 of arbitrary data
example_crc32:
    la    $a0, data_buffer     // $a0 = data pointer
    addi  $a1, $zero, 256      // $a1 = data length
    jal   :lib_crc32           // compute CRC32
    nop
    // $v0 = 32-bit CRC32 value

// ============================================================================
// 7. MEMORY ALLOCATION - allocator.cds
// ============================================================================

// Example 7.1: heap_init - Initialize heap
// Sets up bump allocator with base and size
example_heap_init:
    la    $a0, heap_memory     // $a0 = heap base address
    addi  $a1, $zero, 4096     // $a1 = heap size (4KB)
    jal   :lib_heap_init
    nop
    // $v0 = 1 (success) or 0 (failure)
    beq   $v0, $zero, :heap_init_failed
    // Heap initialized successfully

// Example 7.2: malloc - Allocate memory
// Allocates n bytes from heap
example_malloc:
    addi  $a0, $zero, 128      // $a0 = size to allocate
    jal   :lib_malloc
    nop
    // $v0 = pointer to allocated memory or 0 (failure)
    beq   $v0, $zero, :malloc_failed
    // memory allocated at $v0

// Example 7.3: free - Free memory (no-op for bump allocator)
example_free:
    addi  $a0, $zero, 0        // $a0 = pointer (unused)
    jal   :lib_free
    nop
    // $v0 = 1 (always succeeds)

// ============================================================================
// 8. FORMATTED OUTPUT - printf.cds
// ============================================================================

// Example 8.1: sprintf_args - Format string with arguments
// Supports: %s (string), %d (signed decimal), %x (hex lowercase)
example_sprintf:
    la    $a0, output_buffer   // $a0 = output buffer
    addi  $a1, $zero, 256      // $a1 = buffer size
    la    $a2, format_string   // $a2 = format string: "value=%d name=%s"
    la    $a3, args_array      // $a3 = arguments array
    jal   :lib_sprintf_args
    nop
    // $v0 = bytes written (excluding NUL)
    // output_buffer contains formatted string

// Format string examples:
// "Hello %s" - string substitution
// "Number: %d" - decimal integer
// "Hex: 0x%x" - hexadecimal (lowercase)
// "Mixed: %d items, name=%s" - multiple substitutions

// Argument array layout (words):
// args_array[0] = first argument value
// args_array[1] = second argument value
// etc.

// ============================================================================
// DATA SECTION - Example data for above examples
// ============================================================================
address $80100000

mystring:
    string "Hello"

destination:
    string "\0\0\0\0\0\0\0\0\0\0\0\0"

source:
    string "Source"

buffer:
    string "Original"

append_me:
    string " Appended"

string1:
    string "Apple"

string2:
    string "Banana"

text_with_spaces:
    string "   trimmed   "

haystack:
    string "The quick brown fox"

needle:
    string "quick"

prefix:
    string "The"

suffix:
    string "fox"

decimal_string:
    string "12345"

hex_string:
    string "0xDEADBEEF"

negative_string:
    string "-999"

output_buffer:
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

dest:
    string "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"

source_string:
    string "This is a test string for substring extraction"

data_buffer:
    string "Data for CRC32 calculation"

text_to_hash:
    string "Hash this text"

ptrs_array:
    // Space for 10 pointers (4 bytes each)
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

input_string:
    string "apple,banana,cherry"

args_array:
    hexcode $00004321      // example: 17185 for %d
    hexcode $80100100      // example: pointer to string for %s

format_string:
    string "Count=%d, Name=%s"

address $80101000
heap_memory:
    // Reserve 4KB for heap
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

buffer1:
    string "Compare this buffer"

buffer2:
    string "Compare this buffer"

text:
    string "Hello World"

input_string_split:
    string "one:two:three:four"
