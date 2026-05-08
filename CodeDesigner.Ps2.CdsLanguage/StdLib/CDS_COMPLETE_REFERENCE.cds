// ============================================================================
// CODE DESIGNER SCRIPT (CDS) - COMPLETE LANGUAGE REFERENCE & SYNTAX TEST
// ============================================================================
// This file demonstrates ALL pseudo-commands, directives, and language features
// available in Code Designer Script with detailed comments explaining each one.
//
// CDS compiles to:
//   1. MIPS machine code (patch words)
//   2. GameShark cheat codes
//   3. Persistent cheat-engine entries (freeze/cond/timer)
//   4. Sidecar diagnostics (assert/breakpoint/watchpoint/log)
//
// ORGANIZATION:
//   Section 1: Comments & Basic Syntax
//   Section 2: Address & Data Directives
//   Section 3: Memory Operations & Shortcuts
//   Section 4: Preprocessor (const/alias/macro/include)
//   Section 5: Control Flow (if/while/repeat)
//   Section 6: Cheat Engine Blocks (freeze/cond/timer)
//   Section 7: Sidecar Directives (assert/breakpoint/watchpoint/log)
//   Section 8: Label & Scope Blocks
//   Section 9: Advanced Examples
// ============================================================================

// ============================================================================
// SECTION 1: COMMENTS & BASIC SYNTAX
// ============================================================================

// This is a single-line comment (line starts with //)
// Anything after // is ignored by the parser

/* This is a block comment.
   Multiple lines are supported.
   Block comments use /* and */ delimiters.
*/

// Inline comments also work:
nop  // This nop does nothing

// Supported characters:
// - Register names: $v0, $a0, $sp, $ra, $zero, $at, $t0, etc.
// - Hex literals: $DEADBEEF, $12345678
// - Decimal literals: 12345, -999
// - String literals: "Hello", "World"
// - Labels (lowercase with colons): my_label:, start:, loop_end:

// ============================================================================
// SECTION 2: ADDRESS & DATA DIRECTIVES
// ============================================================================

// ------ DIRECTIVE: address ------
// Sets the current write address. All following instructions/data start here.
// Format: address <hex_address>
// The hex address must be prefixed with $
address $80100000

// Now any instructions/data written will be placed at $80100000
nop

// Changing address mid-script:
address $80100100

// You can change address multiple times in a script
// Each block of code follows its address

// ------ DIRECTIVE: hexcode ------
// Embeds raw 32-bit hex values directly into the output.
// Format: hexcode <hex_value>
// Useful for: machine code you want to bypass the assembler, constants
hexcode $DEADBEEF     // Raw 32-bit value

hexcode $00000000     // Zero word (often used for alignment or data)

// Multiple hexcode directives:
hexcode $12345678
hexcode $ABCDEF00

// ------ DIRECTIVE: string ------
// Embeds a null-terminated ASCII string.
// Format: string "text"
// Characters are stored as bytes; the string ends with a NUL byte ($00)
address $80100200
string "Hello World"    // Stored as: H e l l o   W o r l d \0

string "Short"          // Only 6 bytes (5 + NUL)

string ""               // Empty string = just NUL byte

// ============================================================================
// SECTION 3: MEMORY OPERATIONS & SHORTCUTS
// ============================================================================

// ------ MIPS Standard Instructions ------
// All standard MIPS R3000 instructions are supported.
address $80100000

// Arithmetic instructions:
addiu $v0, $zero, 100      // Add immediate unsigned: v0 = 0 + 100 = 100
addi  $a0, $v0, -50        // Add immediate (signed): a0 = v0 - 50
add   $v1, $v0, $a0        // Add: v1 = v0 + a0
sub   $v1, $v0, $a0        // Subtract: v1 = v0 - a0
mult  $v0, $a0             // Multiply: HI:LO = v0 * a0
div   $v0, $a0             // Divide: LO = v0 / a0, HI = v0 % a0
mflo  $t0                   // Move from LO register
mfhi  $t1                   // Move from HI register

// Logical instructions:
and   $v0, $a0, $t0        // AND: v0 = a0 & t0
or    $v0, $a0, $t0        // OR: v0 = a0 | t0
xor   $v0, $a0, $t0        // XOR: v0 = a0 ^ t0
nor   $v0, $a0, $t0        // NOR: v0 = ~(a0 | t0)
andi  $v0, $a0, $FF        // AND immediate: v0 = a0 & 0xFF
ori   $v0, $a0, $FF        // OR immediate: v0 = a0 | 0xFF
xori  $v0, $a0, $FF        // XOR immediate: v0 = a0 ^ 0xFF

// Shift instructions:
sll   $v0, $a0, 2          // Shift left logical: v0 = a0 << 2
srl   $v0, $a0, 2          // Shift right logical: v0 = a0 >> 2
sra   $v0, $a0, 2          // Shift right arithmetic (sign-extend): v0 = a0 >> 2 (signed)
sllv  $v0, $a0, $t0        // Shift left logical variable: v0 = a0 << (t0 & 0x1F)
srlv  $v0, $a0, $t0        // Shift right logical variable
srav  $v0, $a0, $t0        // Shift right arithmetic variable

// Comparison instructions (set on condition):
slt   $v0, $a0, $t0        // Set if less than: v0 = (a0 < t0) ? 1 : 0
sltu  $v0, $a0, $t0        // Set if less than unsigned: v0 = ((unsigned)a0 < (unsigned)t0) ? 1 : 0
slti  $v0, $a0, 100        // Set if less than immediate: v0 = (a0 < 100) ? 1 : 0
sltiu $v0, $a0, 100        // Set if less than immediate unsigned

// Branch instructions:
beq   $v0, $a0, :my_label     // Branch if equal: if (v0 == a0) jump to :my_label
bne   $v0, $a0, :my_label     // Branch if not equal: if (v0 != a0) jump to :my_label
bltz  $v0, :my_label          // Branch if less than zero: if (v0 < 0) jump
bgtz  $v0, :my_label          // Branch if greater than zero: if (v0 > 0) jump
blez  $v0, :my_label          // Branch if less than or equal zero
bgez  $v0, :my_label          // Branch if greater than or equal zero

// Jump instructions:
j     :my_label             // Unconditional jump
jal   :my_function          // Jump and link (subroutine call, stores return in $ra)
jr    $ra                   // Jump register (return from subroutine)
jalr  $v0                   // Jump and link register

// Memory access instructions (CRITICAL: delay slot):
lw    $v0, 0($a0)           // Load word: v0 = mem[a0 + 0]
lh    $v0, 0($a0)           // Load halfword (16-bit, sign-extend): v0 = mem[a0 + 0]
lb    $v0, 0($a0)           // Load byte (8-bit, sign-extend): v0 = mem[a0 + 0]
lwu   $v0, 0($a0)           // Load word unsigned (no sign extend, PS2 64-bit)
lhu   $v0, 0($a0)           // Load halfword unsigned
lbu   $v0, 0($a0)           // Load byte unsigned (no sign extend)

sw    $v0, 0($a0)           // Store word: mem[a0 + 0] = v0
sh    $v0, 0($a0)           // Store halfword: mem[a0 + 0] = v0 (16-bit)
sb    $v0, 0($a0)           // Store byte: mem[a0 + 0] = v0 (8-bit)

// Delay slot note: MIPS has a branch delay slot!
// The instruction AFTER a branch/jump ALWAYS executes before the branch is taken.
// Best practice: use 'nop' in delay slots to avoid confusion.
beq   $v0, $a0, :target
nop                         // This executes regardless of branch
// End delay slot

my_label:
// Continue here

// ------ SHORTHAND: load[.b/.h/.w/.d] ------
// Typed memory load with automatic size specification.
// Format: load.<size> $dest, mem[offset] $base
// Sizes: .b (byte), .h (halfword), .w (word), .d (doubleword)
// This expands to: lb/lh/lw/ld $dest, offset($base)

address $80100300
load.b  $v0, mem[0] $a0        // Equivalent to: lb $v0, 0($a0)
load.h  $v0, mem[2] $a0        // Equivalent to: lh $v0, 2($a0)
load.w  $v0, mem[4] $a0        // Equivalent to: lw $v0, 4($a0)
load.d  $v0, mem[8] $a0        // Equivalent to: ld $v0, 8($a0)

// ------ SHORTHAND: store[.b/.h/.w/.d] ------
// Typed memory store with automatic size specification.
// Format: store.<size> $value, mem[offset] $base
// Sizes: .b (byte), .h (halfword), .w (word), .d (doubleword)

store.b $v0, mem[0] $a0        // Equivalent to: sb $v0, 0($a0)
store.h $v0, mem[2] $a0        // Equivalent to: sh $v0, 2($a0)
store.w $v0, mem[4] $a0        // Equivalent to: sw $v0, 4($a0)
store.d $v0, mem[8] $a0        // Equivalent to: sd $v0, 8($a0)

// ------ PSEUDO-INSTRUCTION: setreg ------
// Convenient way to load an immediate value into a register.
// Format: setreg <register>, <value>
// Automatically loads the value using addiu if it fits in 16 bits,
// or uses a multi-instruction sequence for larger values.

setreg $v0, 12345           // Load 12345 into $v0
setreg $a0, $DEADBEEF       // Load $DEADBEEF into $a0

// ------ PSEUDO-INSTRUCTION: la (Load Address) ------
// Loads a 32-bit address/label into a register.
// Format: la <register>, <address_or_label>

la $v0, $80100000           // Load $80100000 into $v0
la $a0, :my_label           // Load address of my_label into $a0

// ------ DIRECTIVE: pointer ------
// Advanced: Follows a multi-level pointer chain and writes a value.
// Format: pointer <base_addr> + <offset1> [+ <offset2> ...] = <value>
// Useful for: Game data structures accessed via pointer chains
// NOTE: Clobbers scratch registers $t8 and $t9

address $80100400
pointer $80200000 + $10 + $8 = $DEADBEEF
// This will:
// 1. Load mem[$80200000 + $10] (follow first pointer)
// 2. Add $8 to result
// 3. Write $DEADBEEF to resulting address

// ============================================================================
// SECTION 4: PREPROCESSOR - COMPILE-TIME SYMBOLS
// ============================================================================

// ------ PREPROCESSOR: const ------
// Define a named constant (evaluated at compile-time).
// Format: const <NAME> <value>
// Constants can be used in any operand, address, or condition.

const HEALTH_ADDR $20123456   // Symbolic address
const MAX_HP 9999              // Symbolic value
const CRITICAL_HP 100
const BUFFER_SIZE 256

// Use constants:
address HEALTH_ADDR            // Expands to: address $20123456
setreg $v0, MAX_HP             // Expands to: setreg $v0, 9999
addi   $t0, $zero, CRITICAL_HP // Expands to: addi $t0, $zero, 100

// Constants can reference other constants:
const HALF_HP (MAX_HP / 2)     // HALF_HP = 4999 (if math is supported)

// ------ PREPROCESSOR: alias ------
// Define a symbolic alias for a register (convenience).
// Format: alias <NAME> <register>
// Reduces typing and improves readability.

alias hp_reg $v0              // Rename $v0 to hp_reg
alias dmg_reg $a0             // Rename $a0 to dmg_reg

// Use aliases as if they were register names:
addiu hp_reg, $zero, MAX_HP   // Same as: addiu $v0, $zero, MAX_HP
add   hp_reg, hp_reg, dmg_reg // Same as: add $v0, $v0, $a0

// ------ PREPROCESSOR: macro / endmacro ------
// Define an inline macro (code template).
// Format: macro <NAME> <params> ... <body> endmacro
// When called, the macro expands inline (no subroutine call).

macro set_value dest, value
  addiu dest, $zero, value
endmacro

macro load_addr reg, addr
  la reg, addr
endmacro

macro double_reg reg
  add reg, reg, reg
endmacro

// Use macros (they expand in-place):
set_value $v0, 100            // Expands to: addiu $v0, $zero, 100
load_addr $a0, $80100000       // Expands to: la $a0, $80100000
double_reg $t0                // Expands to: add $t0, $t0, $t0

// Macro parameters are substituted literally:
macro add3 dest, s1, s2, s3
  add   dest, s1, s2
  add   dest, dest, s3
endmacro

add3 $v0, $a0, $a1, $a2       // Expands to:
                              //   add $v0, $a0, $a1
                              //   add $v0, $v0, $a2

// ------ PREPROCESSOR: include ------
// Include another .cds file (like C #include).
// Format: include "path/to/file.cds"
// Relative paths are resolved from the current file's directory.

// include "common/constants.cds"  // Would load constants from another file
// include "routines/utils.cds"     // Would load utility routines

// Include is useful for:
// - Sharing common constants across multiple scripts
// - Including standard library functions
// - Organizing code into modules

// ============================================================================
// SECTION 5: CONTROL FLOW - COMPILE-TIME BRANCHING
// ============================================================================

// ------ PREPROCESSOR: if / else / endif ------
// Compile-time conditional. Body expands only if condition is true.
// Format: if <condition> ... [else ...] endif
// Conditions can be: constants, symbols, expressions (if supported)

const DEBUG 1
const PROFILE 0

if DEBUG
  log "Debug build activated"
  nop  // Debugging marker
endif

if PROFILE
  log "Profiling enabled"
endif

// Conditional with else:
if MAX_HP > 500
  log "High HP limit"
else
  log "Low HP limit"
endif

// Nested if/else:
if DEBUG
  if PROFILE
    log "Debug + Profiling"
  else
    log "Debug only"
  endif
else
  log "No debug"
endif

// ------ PREPROCESSOR: while / endwhile ------
// Compile-time loop. Body expands N times until condition is false.
// Format: while <condition> ... endwhile
// Common pattern: while N > 0; expands N times.

const ITERATIONS 3

// Expand code 3 times:
while ITERATIONS > 0
  nop
  const ITERATIONS (ITERATIONS - 1)  // Decrement (pseudo-decrement)
endwhile

// This expands to:
// nop
// nop
// nop

// ------ PREPROCESSOR: repeat / endrepeat ------
// Compile-time loop. Body expands N times (simpler than while).
// Format: repeat <count> ... endrepeat
// The iteration variable $t9 is NOT set (unlike some other languages).

repeat 5
  nop
endrepeat

// This expands to:
// nop
// nop
// nop
// nop
// nop

// Practical example: Initialize memory to zero
repeat 16
  hexcode $00000000
endrepeat

// ============================================================================
// SECTION 6: CHEAT ENGINE BLOCKS (Persistent Frame-Loop Cheats)
// ============================================================================

// These blocks compile to persistent cheat-engine entries that run
// every frame (or on a schedule). They're NOT one-shot patches.

// ------ DIRECTIVE: freeze / endfreeze ------
// Write a value every game frame (persistent).
// Format: freeze <address> = <value>
// Use when: The game continuously resets a value and you want to keep yours
// Common uses: Infinite health, infinite ammo, always max stats

address $80100500
freeze $20123456 = 9999       // Every frame: write 9999 to address $20123456

// Multiple freezes:
freeze $20123458 = 100        // Every frame: write 100 here
freeze $2012345C = 50         // Every frame: write 50 here

// ------ DIRECTIVE: cond / endcond ------
// Wrap a freeze block in a condition (memory value check).
// Format: cond <address> <operator> <value> ... endcond
// The freeze(s) only run when the condition is true.
// Operators: ==, !=, <, >, <=, >=

address $80100600
cond $80001234 == 1           // Only if mem[$80001234] == 1
  freeze $20123456 = 9999
endcond

// Real-world example: Freeze health only when player is in-game
cond $80002000 == 1           // If in_game == 1
  cond $80002004 != 0         // AND player_alive != 0
    freeze $80002100 = 9999   // Keep health at 9999
  endcond
endcond

// ------ DIRECTIVE: timer / endtimer ------
// Run the freeze block once every N frames (frame rate throttling).
// Format: timer <frame_count> ... endtimer
// Useful for: Applying effects at specific intervals without overwhelming the game

address $80100700
timer 30                       // Run once every 30 frames
  freeze $20123456 = 9999
endtimer

// timer + cond combination:
timer 10
  cond $80001234 == 1
    freeze $20123456 = 9999
  endcond
endtimer

// ============================================================================
// SECTION 7: SIDECAR DIRECTIVES (Diagnostic & Breakpoint)
// ============================================================================

// Sidecar directives don't generate patch code, but instead:
// - Check conditions before the patch runs (assert)
// - Set breakpoints during the patch (breakpoint/watchpoint)
// - Output diagnostic messages (log/print)

// ------ DIRECTIVE: assert ------
// Verify a memory value before applying the patch.
// Format: assert <address> <operator> <value>
// If the assertion fails, the patch is NOT applied and an error is shown.
// Operators: ==, !=, <, >, <=, >=

address $80100800
assert $20123456 == $DEADBEEF   // Verify original value is DEADBEEF
addiu $v0, $zero, 9999          // Only execute if assertion passed

assert $20123458 != 0            // Verify value is not zero

// Multiple assertions (all must pass):
assert $80001000 == $12345678
assert $80001004 > 0
// If both pass, the following code runs:
sw $v0, 0($a0)

// Common assertion patterns:
// - Verify original value unchanged since code was written
// - Check a version byte to ensure game is the right version
// - Validate that an address contains data (not zeroed)

// ------ DIRECTIVE: breakpoint ------
// Set a hardware breakpoint at the current address.
// When the game executes this address, the emulator breaks and pauses.
// Format: breakpoint (no arguments)

address $80100900
breakpoint              // Emulator pauses when reaching $80100900
addiu $v0, $zero, 0

// Useful for:
// - Debugging: pause at specific game events
// - Analysis: break before/after a specific code section
// - Testing: verify your patch is actually reached

// ------ DIRECTIVE: watchpoint ------
// Set a hardware watchpoint (break on memory access).
// Emulator breaks when the address is read or written.
// Format: watchpoint (set at current address, but typically used with specific addresses)

address $80101000
watchpoint              // Break on access to memory at $80101000

// Practical uses:
// - Find where a variable is being modified
// - Break when a specific memory location is read
// - Debug unexpected value changes

// ------ DIRECTIVE: log / print ------
// Output a diagnostic message (appears in VS Code output panel).
// Format: log "message" or print "message"
// Both are equivalent; use whichever you prefer.
// Variables in the message are NOT substituted (yet); use string literals.

address $80101100
log "Patch initialized at $80101100"
print "About to set health to 9999"

// Practical messages:
log "Patch START: health doubler"
log "Checking preconditions"
log "Health modified"
log "Patch COMPLETE"

// Multi-line messages (as concatenation):
log "Applying health freeze"
// (Note: each log is a separate output line)

// ============================================================================
// SECTION 8: LABELS & SCOPE BLOCKS
// ============================================================================

// ------ LABELS ------
// Labels mark instruction positions and can be jump targets.
// Format: <name>: (lowercase, colon suffix)
// References: :name (colon prefix when used in jumps/branches)

address $80101200

my_function:
  // Function body
  addiu $v0, $zero, 100
  jr $ra
  nop

loop_start:
  addiu $t0, $t0, 1
  bne $t0, $a0, :loop_start  // Jump back to loop_start if not equal
  nop

label_end:
  // End of code

// Forward references (jump to label defined later):
j :forward_label
nop

forward_label:
  nop

// Multiple labels can point to the same location:
same_location:
another_name:
  addiu $v0, $zero, 1

// ------ SCOPE BLOCKS ------
// Group related code with a named scope.
// Format: scope <name> { ... }
// Scopes are mainly organizational; they help structure large patches.

scope initialize_health {
  setreg $v0, $80100000
  la $a0, $20123456
  sw $v0, 0($a0)
}

scope apply_buffs {
  // Buff 1
  setreg $v0, 9999
  la $a0, $20123456
  sw $v0, 0($a0)
  
  // Buff 2
  setreg $v0, 100
  la $a0, $20123458
  sw $v0, 0($a0)
}

// ============================================================================
// SECTION 9: ADVANCED EXAMPLES & PATTERNS
// ============================================================================

// Example 1: Health Doubler Patch with Safety Checks
// ─────────────────────────────────────────────────
const HEALTH_ADDR $80100000
const VERSION_BYTE $80100004
const EXPECTED_VERSION $42

address HEALTH_ADDR
assert HEALTH_ADDR == $DEADBEEF    // Verify offset/version
assert VERSION_BYTE == EXPECTED_VERSION

// Load current health, double it, store it back
la $a0, HEALTH_ADDR
lw $v0, 0($a0)                     // Load current health
add $v0, $v0, $v0                  // Double it (v0 = v0 + v0)
sw $v0, 0($a0)                     // Store back

log "Health doubled"

// Example 2: Conditional Buff with Timer
// ──────────────────────────────────────
const PLAYER_STATE_ADDR $80200000
const IN_GAME_FLAG $80200004

address $80101300

timer 60                            // Every 60 frames
  cond IN_GAME_FLAG == 1            // Only if player is in-game
    freeze HEALTH_ADDR = 9999       // Keep health maxed
    freeze $80200008 = 100          // Keep ammo maxed
  endcond
endtimer

log "Buff timer activated"

// Example 3: Complex Pointer Chain Write
// ───────────────────────────────────────
// Follow a pointer chain: Base[Offset1].member[Offset2] = Value

address $80101400

pointer $80200000 + $20 + $8 = $DEADBEEF
log "Pointer chain write complete"

// Example 4: Initialization with Macro and Loop
// ──────────────────────────────────────────────
macro init_memory base, size
  setreg $a0, base
  repeat size
    sw $zero, 0($a0)
    addiu $a0, $a0, 4
  endrepeat
endmacro

address $80101500

init_memory $80200000, 256        // Initialize 256 dwords = 1KB with zeros

log "Memory initialized"

// Example 5: Conditional Compilation for Testing
// ────────────────────────────────────────────────
const TESTING 1

address $80101600

if TESTING
  log "TEST BUILD: Extra assertions enabled"
  breakpoint                       // Break on patch apply (for testing)
  assert $80200000 == 0            // Test assertion
else
  log "RELEASE BUILD: No debug overhead"
endif

nop

// Example 6: Data Structures with Named Constants
// ────────────────────────────────────────────────
const PLAYER_DATA $80200000
const PLAYER_HEALTH_OFF $00
const PLAYER_MANA_OFF $04
const PLAYER_STAMINA_OFF $08
const PLAYER_LEVEL_OFF $0C

address $80101700

// Buff all player stats at once:
la $a0, PLAYER_DATA
addiu $v0, $zero, 9999
sw $v0, PLAYER_HEALTH_OFF($a0)
addiu $v0, $zero, 500
sw $v0, PLAYER_MANA_OFF($a0)
addiu $v0, $zero, 1000
sw $v0, PLAYER_STAMINA_OFF($a0)
addiu $v0, $zero, 99
sw $v0, PLAYER_LEVEL_OFF($a0)

// ============================================================================
// SECTION 10: COMMON PATTERNS & BEST PRACTICES
// ============================================================================

// Pattern 1: Safe value replacement with assertion
// ─────────────────────────────────────────────────
address $80101800
assert $80200000 == $12345678      // Verify we're at the right location
addiu $v0, $zero, $DEADBEEF        // Load new value
sw $v0, 0($a0)                     // Write new value
log "Value replaced safely"

// Pattern 2: Temporary variable storage in $t-registers
// ────────────────────────────────────────────────────
// Temporary registers: $t0-$t9 (safe to clobber)
// Argument registers: $a0-$a3 (preserved by convention)
// Return registers: $v0-$v1

address $80101900
la $t0, $80200000                  // $t0 = temporary address
lw $t1, 0($t0)                     // $t1 = temporary value
add $t2, $t1, $t1                  // $t2 = temporary result
sw $t2, 0($t0)                     // Store back

// Pattern 3: Use setreg for large values
// ────────────────────────────────────
address $80101A00
setreg $v0, $DEADBEEF              // Handles multi-instruction loading
sw $v0, 0($a0)

// Pattern 4: Comment complex operations
// ──────────────────────────────────────
address $80101B00
// Pattern: Load address -> Load value -> Modify -> Store
la $a0, $80200000                  // Load base address
lw $v0, 0($a0)                     // Load current value
add $v0, $v0, $v0                  // Modify (double it)
sw $v0, 0($a0)                     // Store modified value

// Pattern 5: Log key transitions
// ──────────────────────────────
address $80101C00
log "=== Patch Start ==="
nop
log "Step 1: Initialization complete"
nop
log "Step 2: Values modified"
nop
log "=== Patch End ==="

// ============================================================================
// COMPILATION & OUTPUT NOTES
// ============================================================================

// When this file is compiled:
// 1. The preprocessor expands all const/alias/macro/if/while/repeat
// 2. The parser builds labels and validates syntax
// 3. The compiler generates output:
//    - Patch words (address, value pairs)
//    - GameShark codes (50xxxxxx format)
//    - Cheat entries (freeze/cond/timer)
//    - Sidecar commands (assert/breakpoint/log)

// Output location in VS Code:
// - Patch codes appear in the CDS tab output box
// - Errors/warnings appear in the Problems panel
// - Log messages appear in the Output panel
// - Compilation time: typically < 100ms

// Files can be imported via include directive and built into larger systems.
// See the examples in USAGE_EXAMPLES.cds for practical applications.

// ============================================================================
// END OF CDS LANGUAGE REFERENCE
// ============================================================================
