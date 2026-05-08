// Standard string library for CDS (ASCII, null-terminated C-style strings).
// Usage:
//   include "StdLib/string.cds"
//
// Conventions:
//   - Functions follow a simple calling convention:
//       $a0, $a1, $a2, $a3  = arguments (as needed)
//       $v0                  = return value
//       $ra                  = return address (caller must use jal)
//   - Scratch registers used: $t0, $t1, $t2 (callee-saved by convention not required here).
//   - All strings are ASCII, null-terminated in memory.
//
// Functions provided:
//   lib_str_strlen   (a0=ptr) -> v0 = length (bytes, excluding terminator)
//   lib_strcpy       (a0=dst, a1=src) -> v0 = dst
//   lib_strcat       (a0=dst, a1=src) -> v0 = dst (assumes dst has room)
//   lib_strcmp       (a0=s1, a1=s2) -> v0 = 0 equal, <0 s1<s2, >0 s1>s2
//   lib_str_replace_char (a0=ptr, a1=oldChar, a2=newChar) -> v0 = replacements count

// -----------------------------
// strlen: count bytes until NUL
// -----------------------------
lib_str_strlen:
    // expects: $a0 -> pointer to string
    addi $v0, $zero, 0        // length = 0
strlen_loop:
    lb   $t0, 0($a0)          // load byte
    beq  $t0, $zero, :strlen_done
    addiu $v0, $v0, 1
    addiu $a0, $a0, 1
    j    :strlen_loop
    nop
strlen_done:
    jr   $ra
    nop

// -----------------------------
// strcpy: copy src -> dst (incl. terminator)
// returns dst in $v0
// -----------------------------
lib_strcpy:
    // $a0 = dst, $a1 = src
    addi $v0, $a0, 0          // return dst in v0
copy_loop:
    lb   $t0, 0($a1)          // load src byte
    sb   $t0, 0($a0)          // store to dst
    beq  $t0, $zero, :copy_done
    addiu $a0, $a0, 1
    addiu $a1, $a1, 1
    j    :copy_loop
    nop
copy_done:
    jr   $ra
    nop

// -----------------------------
// strcat: append src to dst (dst must have room)
// returns dst in $v0
// -----------------------------
lib_strcat:
    // $a0 = dst, $a1 = src
    addi $v0, $a0, 0          // return dst in v0
    // find end of dst
strcat_find_end:
    lb   $t0, 0($a0)
    beq  $t0, $zero, :strcat_copy
    addiu $a0, $a0, 1
    j    :strcat_find_end
    nop
strcat_copy:
    // now $a0 points at the terminator byte of dst
    lb   $t0, 0($a1)
    sb   $t0, 0($a0)
    beq  $t0, $zero, :strcat_done
    addiu $a0, $a0, 1
    addiu $a1, $a1, 1
    j    :strcat_copy
    nop
strcat_done:
    jr   $ra
    nop

// -----------------------------
// strcmp: lexicographic comparison
// returns in $v0: 0 equal, <0 s1<s2, >0 s1>s2
// -----------------------------
lib_strcmp:
    // $a0 = s1, $a1 = s2
strcmp_loop:
    lb   $t0, 0($a0)          // c1
    lb   $t1, 0($a1)          // c2
    beq  $t0, $t1, :strcmp_check_zero
    // not equal: compute signed difference (byte signedness: ascii 0..127 safe)
    subu $v0, $t0, $t1
    jr   $ra
    nop
strcmp_check_zero:
    beq  $t0, $zero, :strcmp_equal
    addiu $a0, $a0, 1
    addiu $a1, $a1, 1
    j    :strcmp_loop
    nop
strcmp_equal:
    addi $v0, $zero, $0
    jr   $ra
    nop

// -----------------------------
// str_replace_char: replace all occurrences of oldChar with newChar
// returns count of replacements in $v0
// arguments:
//   $a0 = pointer to string (in-place modification)
//   $a1 = oldChar (value in low byte of register, e.g. $a1 = $0000002E for '.')
//   $a2 = newChar
// -----------------------------
lib_str_replace_char:
    addi $v0, $zero, $0       // replacements = 0
replace_loop:
    lb   $t0, $0($a0)
    beq  $t0, $zero, :replace_done
    nop
    beq  $t0, $a1, :replace_do
    addiu $a0, $a0, $1
    j    :replace_loop
    nop
replace_do:
    // write newChar (byte from $a2)
    sb   $a2, $0($a0)
    addiu $v0, $v0, $1
    addiu $a0, $a0, $1
    j    :replace_loop
    nop
replace_done:
    jr   $ra
    nop