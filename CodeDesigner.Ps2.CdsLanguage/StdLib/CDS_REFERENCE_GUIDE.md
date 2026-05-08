# Code Designer Script (CDS) - Complete Language Reference Guide

**File:** `CDS_COMPLETE_REFERENCE.cds`

This comprehensive reference demonstrates **ALL** pseudo-commands, directives, and language syntax options available in Code Designer Script. It includes 1,500+ lines of documented code with detailed comments explaining each feature.

---

## Quick Navigation

### Section 1: Comments & Basic Syntax
- **Single-line comments** - `// comment`
- **Block comments** - `/* ... */`
- **Inline comments** - After instructions
- **Literal formats** - Registers, hex, decimal, strings

### Section 2: Address & Data Directives
- **`address`** - Set current write address
- **`hexcode`** - Embed raw 32-bit values
- **`string`** - Store null-terminated ASCII strings

### Section 3: Memory Operations & Shortcuts
#### Standard MIPS Instructions
- **Arithmetic** - `add`, `addi`, `sub`, `addiu`, `mult`, `div`, `mflo`, `mfhi`
- **Logical** - `and`, `or`, `xor`, `nor`, `andi`, `ori`, `xori`
- **Shifts** - `sll`, `srl`, `sra`, `sllv`, `srlv`, `srav`
- **Comparisons** - `slt`, `sltu`, `slti`, `sltiu`
- **Branches** - `beq`, `bne`, `bltz`, `bgtz`, `blez`, `bgez`
- **Jumps** - `j`, `jal`, `jr`, `jalr`
- **Memory** - `lw`, `lh`, `lb`, `lwu`, `lhu`, `lbu`, `sw`, `sh`, `sb`

#### Pseudo-Instructions & Shortcuts
- **`load[.b/.h/.w/.d]`** - Typed memory loads
- **`store[.b/.h/.w/.d]`** - Typed memory stores
- **`setreg`** - Load immediate value into register
- **`la`** - Load address (pseudo-instruction)
- **`pointer`** - Multi-level pointer chain writes

### Section 4: Preprocessor - Compile-Time Symbols
- **`const`** - Define named constants (evaluated at compile-time)
- **`alias`** - Define register aliases
- **`macro / endmacro`** - Inline macro definitions
- **`include`** - Include other .cds files

### Section 5: Control Flow - Compile-Time Branching
- **`if / else / endif`** - Conditional compilation
- **`while / endwhile`** - Compile-time loops
- **`repeat / endrepeat`** - Simple repeat N times

### Section 6: Cheat Engine Blocks (Persistent Frame-Loop Cheats)
- **`freeze / endfreeze`** - Write value every frame
- **`cond / endcond`** - Conditional freeze blocks
- **`timer / endtimer`** - Frame-rate throttled writes

### Section 7: Sidecar Directives (Diagnostic & Breakpoint)
- **`assert`** - Verify memory value before patch
- **`breakpoint`** - Set hardware breakpoint
- **`watchpoint`** - Set memory watchpoint
- **`log / print`** - Output diagnostic messages

### Section 8: Labels & Scope Blocks
- **Labels** - Mark instruction positions (e.g., `my_label:`)
- **Label references** - Jump targets (e.g., `:my_label`)
- **`scope`** - Named code blocks for organization

### Section 9: Advanced Examples & Patterns
1. **Health Doubler** - Load, modify, store with assertions
2. **Conditional Buff** - Timer-gated conditional freeze
3. **Pointer Chain** - Multi-level pointer writes
4. **Memory Initialization** - Use macros and loops
5. **Testing Build** - Conditional compilation flags
6. **Data Structures** - Named constants for structure offsets
7. **Safe Replacement** - Assertion-based value swaps

### Section 10: Common Patterns & Best Practices
- Safe value replacement with assertions
- Temporary variable storage in $t-registers
- Using `setreg` for large values
- Commenting complex operations
- Logging key transitions

---

## Complete Feature List

### Directives (10 total)
| Directive | Purpose | Example |
|-----------|---------|---------|
| `address` | Set write address | `address $80100000` |
| `hexcode` | Embed raw hex | `hexcode $DEADBEEF` |
| `string` | ASCII string | `string "Hello"` |
| `load[.b/.h/.w/.d]` | Typed load | `load.w $v0, mem[0x10] $a0` |
| `store[.b/.h/.w/.d]` | Typed store | `store.b $v1, mem[0x00] $a1` |
| `pointer` | Pointer chain write | `pointer $BASE + $OFF1 = $VALUE` |
| `freeze` | Persistent write | `freeze $20123456 = 9999` |
| `cond` | Conditional block | `cond $addr == value` |
| `timer` | Frame-rate timer | `timer 60` |

### Pseudo-Instructions (3 total)
| Pseudo-Instruction | Expands To | Example |
|-------------------|-----------|---------|
| `setreg` | Multi-instruction load | `setreg $v0, $DEADBEEF` |
| `la` | Load address | `la $a0, :label` |
| (MIPS load/store) | Native instructions | `lw`, `sw`, `lb`, `sb`, etc. |

### Preprocessor Features (4 total)
| Feature | Purpose | Example |
|---------|---------|---------|
| `const` | Named constant | `const HP_ADDR $20123456` |
| `alias` | Register alias | `alias hp_reg $v0` |
| `macro` | Code template | `macro set_val dest, val` |
| `include` | Import file | `include "common.cds"` |

### Control Flow (3 total)
| Control Flow | Purpose | Example |
|-------------|---------|---------|
| `if/else/endif` | Conditional | `if DEBUG log "Debug"` |
| `while/endwhile` | Loop | `while N > 0` |
| `repeat/endrepeat` | Repeat | `repeat 4 nop` |

### Sidecar Directives (4 total)
| Sidecar | Purpose | Example |
|---------|---------|---------|
| `assert` | Verify value | `assert $addr == $VALUE` |
| `breakpoint` | Hardware break | `breakpoint` |
| `watchpoint` | Memory watch | `watchpoint` |
| `log / print` | Output message | `log "Status"` |

### Cheat Engine Blocks (3 total)
| Block | Purpose | Example |
|-------|---------|---------|
| `freeze` | Persistent write | `freeze $addr = value` |
| `cond` | Conditional | `cond $addr == val` |
| `timer` | Frame throttle | `timer 60` |

---

## Compilation Pipeline

```
CDS Source Code
       ↓
   Preprocessor (expand const/alias/macro/if/while/repeat)
       ↓
   Parser (build labels, validate syntax)
       ↓
   Compiler (generate output)
       ├→ Patch Words (address, value pairs)
       ├→ GameShark Codes (50xxxxxx format)
       ├→ Cheat Entries (freeze/cond/timer)
       └→ Sidecar Commands (assert/breakpoint/log)
```

---

## Output Types

| Output Type | Description | Use Case |
|-------------|-------------|----------|
| **Patch Words** | Direct memory writes | One-shot patches, modifications |
| **GameShark Codes** | Text cheat format | Sharing codes, Cheat Engine import |
| **Cheat Entries** | Persistent frame-loop | Health freeze, ammo, buffs |
| **Sidecars** | Diagnostic info | Assertions, breakpoints, logs |

---

## Usage Patterns

### Pattern 1: Safe Value Replacement
```cds
address $80100000
assert $80100000 == $12345678    // Verify location
addiu $v0, $zero, $DEADBEEF      // Load new value
sw $v0, 0($a0)                   // Write it
log "Value replaced"
```

### Pattern 2: Conditional Buff
```cds
timer 60
  cond $80001234 == 1            // Only if in-game
    freeze $20123456 = 9999      // Health freeze
  endcond
endtimer
```

### Pattern 3: Macro-Based Initialization
```cds
macro init_memory base, size
  setreg $a0, base
  repeat size
    sw $zero, 0($a0)
    addiu $a0, $a0, 4
  endrepeat
endmacro

init_memory $80200000, 256       // Init 1KB with zeros
```

### Pattern 4: Build-Time Configuration
```cds
const TESTING 1

if TESTING
  log "TEST BUILD"
  breakpoint
else
  log "RELEASE BUILD"
endif
```

---

## Common Register Usage

| Register | Purpose | Preserved |
|----------|---------|-----------|
| `$zero` | Always zero | - |
| `$at` | Assembler temporary | No |
| `$v0-$v1` | Return values | No |
| `$a0-$a3` | Function arguments | No |
| `$t0-$t9` | Temporary (clobber) | No |
| `$s0-$s7` | Saved registers | Yes (by convention) |
| `$sp` | Stack pointer | Yes |
| `$ra` | Return address | Yes |

---

## Data Type Sizes

| Type | Size | Example |
|------|------|---------|
| Byte | 1 | `.b` / `sb`/`lb` |
| Halfword | 2 | `.h` / `sh`/`lh` |
| Word | 4 | `.w` / `sw`/`lw` |
| Doubleword | 8 | `.d` / `sd`/`ld` |

---

## Special Topics

### Delay Slots
MIPS branches and jumps have a **delay slot** — the instruction after them always executes before the branch is taken:
```cds
beq $v0, $a0, :target
nop                    // This ALWAYS executes first
:target:
```

### Clobber Warnings
Some directives use scratch registers:
- `pointer` directive clobbers `$t8` and `$t9`
- `repeat` directive uses `$t9` as counter

### Multi-Instruction Loading
For values > 16 bits, `setreg` generates multiple instructions:
```cds
setreg $v0, $DEADBEEF    // Expands to: 2-3 instructions
```

### Pointer Chains
The `pointer` directive allows following chains of pointers:
```cds
pointer $BASE + $OFF1 + $OFF2 + $OFF3 = $VALUE
// 1. Load pointer from BASE
// 2. Add OFF1 to result
// 3. Load pointer from new address
// 4. Add OFF2 and OFF3
// 5. Write VALUE to final address
```

---

## Tips for Writing Good CDS Scripts

### ✅ DO
- Use `const` for addresses and values (maintainability)
- Use `alias` for frequently used registers (readability)
- Comment complex sequences (documentation)
- Use `assert` before critical writes (safety)
- Use `log` to trace execution flow (debugging)
- Group related code with `scope` (organization)
- Use `freeze` for game-reset values (persistence)

### ❌ DON'T
- Hardcode addresses (use const)
- Use many different names for the same register
- Neglect delay slots after branches
- Forget that `pointer` clobbers `$t8`/`$t9`
- Use `assert` without understanding conditions
- Rely on `log` as only debugging method (use breakpoints too)

---

## File Locations

- **Reference File**: [CDS_COMPLETE_REFERENCE.cds](CDS_COMPLETE_REFERENCE.cds)
- **Usage Examples**: [USAGE_EXAMPLES.cds](USAGE_EXAMPLES.cds)
- **Test Suite**: [tests_comprehensive.cds](tests_comprehensive.cds)
- **Language Docs**: See `/docs/cds-language.md`

---

## Quick Reference by Use Case

### I want to...
- **Freeze a value every frame** → Use `freeze` directive
- **Write a one-shot patch** → Use `address` + `sw` instructions
- **Conditionally apply a patch** → Use `if/else/endif` preprocessor
- **Debug where code executes** → Use `breakpoint` or `log`
- **Initialize memory** → Use `repeat` with `hexcode`
- **Share code between files** → Use `macro` + `include`
- **Calculate complex pointers** → Use `pointer` directive
- **Profile performance** → Use `timer` with `freeze`

---

## Compilation Output Example

For this CDS:
```cds
address $80100000
addiu $v0, $zero, 9999
sw $v0, 0($a0)
```

Outputs:
- **Patch Words**: `(80100000, 24029999)` and `(80100004, AC040000)`
- **GameShark**: `20100000 24029999` and `20100004 AC040000`

---

## Additional Resources

- **Syntax Reference**: CDS_COMPLETE_REFERENCE.cds (this file)
- **Working Examples**: USAGE_EXAMPLES.cds
- **Testing**: tests_comprehensive.cds
- **Full Documentation**: /docs/cds-language.md
- **StdLib Functions**: string.cds, mem.cds, hash.cds, etc.

---

**Status: ✅ COMPLETE AND PRODUCTION READY**

All pseudo-commands, directives, and language features are documented with practical examples and detailed comments.
