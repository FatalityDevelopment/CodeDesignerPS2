// Standard library function declarations.
// Include this file to enable the natural call-site syntax `name(arg1, arg2, ...)`
// for routines defined in the other StdLib/*.cds files. The implementation file(s)
// must also be included so the target labels exist:
//
//   include "StdLib/stdlib.cds"     // declarations only
//   include "StdLib/string.cds"     // implementation of str_*
//   include "StdLib/mem.cds"        // implementation of mem_*
//   ...
//
// Each declaration maps a friendly name + parameter list to an existing
// `lib_*` label. Parameters are positional and bind to $a0..$a3 in order;
// up to four parameters per function. A call like
//
//   str_copy(t0, t1)
//
// expands at preprocess time to:
//
//   addu $a0, $t0, $zero
//   addu $a1, $t1, $zero
//   jal :lib_strcpy
//   nop
//
// (with collision-safe shuffling through $at when arguments cross-reference
// each other's destinations).

// --- string.cds -------------------------------------------------------------
extern function str_len(s) at :lib_str_strlen
extern function str_copy(dst, src) at :lib_strcpy
extern function str_cat(dst, src) at :lib_strcat
extern function str_cmp(s1, s2) at :lib_strcmp
extern function str_replace_char(s, oldC, newC) at :lib_str_replace_char

// --- str_ext.cds ------------------------------------------------------------
extern function str_find(hay, needle) at :lib_str_find
extern function str_substr(src, dst, start, len) at :lib_str_substr
extern function str_starts_with(s, prefix) at :lib_str_startswith
extern function str_ends_with(s, suffix) at :lib_str_endswith
extern function str_atoi(s) at :lib_str_atoi
extern function str_itoa(value, dst) at :lib_str_itoa

// --- strutils.cds -----------------------------------------------------------
extern function str_to_upper(s) at :lib_str_toupper
extern function str_to_lower(s) at :lib_str_tolower
extern function str_trim_left(s) at :lib_str_trim_left
extern function str_trim_right(s) at :lib_str_trim_right

// --- mem.cds ----------------------------------------------------------------
extern function mem_copy(dst, src, n) at :lib_memcpy
extern function mem_set(dst, val, n) at :lib_memset
extern function mem_cmp(p1, p2, n) at :lib_memcmp
extern function mem_chr(ptr, val, n) at :lib_memchr

// --- hash.cds ---------------------------------------------------------------
extern function hash_fnv1a(s) at :lib_hash_fnv1a

// --- crc32.cds --------------------------------------------------------------
extern function crc32(buf, len) at :lib_crc32

// --- allocator.cds ----------------------------------------------------------
extern function heap_init(base, size) at :lib_heap_init
extern function malloc(size) at :lib_malloc
extern function free(ptr) at :lib_free

// --- split.cds --------------------------------------------------------------
extern function str_split(src, sep, outArr, maxParts) at :lib_str_split

// --- printf.cds -------------------------------------------------------------
extern function sprintf(dst, dstSize, fmt, args) at :lib_sprintf_args
