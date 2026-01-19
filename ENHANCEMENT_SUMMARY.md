# MIPS64 Disassembler Enhancement Summary

## Overview
This enhancement adds comprehensive support for PlayStation 2 Emotion Engine coprocessor and vector unit instructions to the MIPS64 disassembler.

## What Was Added

### 1. Coprocessor Instruction Support

#### COP0 (System Control Coprocessor) - Opcode 0x10
- **Move Instructions**: MFC0, DMFC0, CFC0, MTC0, DMTC0, CTC0
- **TLB Instructions**: TLBR, TLBWI, TLBWR, TLBP
- **Exception Handling**: ERET
- **Cache Operations**: CACHE

#### COP1 (Floating-Point Unit) - Opcode 0x11
- **Move Instructions**: MFC1, DMFC1, CFC1, MTC1, DMTC1, CTC1
- **Arithmetic**: ADD.S, SUB.S, MUL.S, DIV.S, SQRT.S, ABS.S, NEG.S, MOV.S
- **Conversion**: CVT.W.S, CVT.S.W
- **Comparison**: C.EQ.S, C.LT.S, C.LE.S
- **Branch**: BC1F, BC1T
- **Load/Store**: LWC1, SWC1

#### COP2/VU0 (Vector Unit 0) - Opcode 0x12
- **Vector Arithmetic**: VADD, VSUB, VMUL, VMADD, VMSUB
- **Vector Comparison**: VMAX, VMIN
- **Vector Operations**: VABS, VADDA, VMULA
- **Conversion**: VFTOI0, VITOF0

### 2. Missing Core Instructions

#### Extended Shift Instructions (64-bit + 32)
- **DSLL32** (funct=0x3C) - Shift left logical by (sa + 32)
- **DSRL32** (funct=0x3E) - Shift right logical by (sa + 32)
- **DSRA32** (funct=0x3F) - Shift right arithmetic by (sa + 32)

#### Branch-and-Link Instructions (REGIMM)
- **BLTZAL** (opcode=0x01, rt=0x10) - Branch if less than zero and link
- **BGEZAL** (opcode=0x01, rt=0x11) - Branch if greater/equal to zero and link
- **BLTZALL** (opcode=0x01, rt=0x12) - Branch if less than zero and link (likely)
- **BGEZALL** (opcode=0x01, rt=0x13) - Branch if greater/equal to zero and link (likely)

## Implementation Details

### Infrastructure Changes
1. **New Dictionaries**:
   - `CopInstructions`: Maps (opcode, rs, funct) to coprocessor instructions
   - `RegimmInstructions`: Maps (opcode, rt) to REGIMM instructions

2. **New Registration Methods**:
   - `RegisterCop()`: Registers coprocessor instructions with rs field
   - `RegisterRegimm()`: Registers REGIMM instructions with rt field

3. **Enhanced Disassemble Method**:
   - Added routing for coprocessor opcodes (0x10-0x13)
   - Added routing for REGIMM opcode (0x01)
   - Maintains backward compatibility with existing instruction types

### Instruction Encoding
- **Coprocessor Instructions**: Use rs field (bits 25-21) for sub-opcode
- **REGIMM Instructions**: Use rt field (bits 20-16) for sub-opcode
- **Function Field**: Used where needed for further differentiation

## Testing

### Test Coverage
- **6 new test methods** covering all added functionality
- **56 total tests** passing (50 original + 6 new)
- All existing functionality remains intact

### Test Categories
1. COP0 Instructions
2. COP1 Instructions
3. VU0 Instructions
4. Extended Shift Instructions
5. Branch-and-Link Instructions
6. Backward Compatibility

## Example Usage

```csharp
using CodeDesigner.Mips64LE.Common;

// COP0 instruction
string result1 = MipsDisassembler.Disassemble(0x40088000);
// Output: "mfc0 t0, $16"

// COP1 instruction
string result2 = MipsDisassembler.Disassemble(0x46010800);
// Output: "add.s $f1, $f16, $f1"

// VU0 instruction
string result3 = MipsDisassembler.Disassemble(0x4A000028);
// Output: "vadd vf0, vf16, vf0"

// Extended shift
string result4 = MipsDisassembler.Disassemble(0x00094FFC);
// Output: "dsll32 t1, t1, 31"

// Branch-and-link
string result5 = MipsDisassembler.Disassemble(0x04100010);
// Output: "bltzal zero, 0010"
```

## Files Modified
1. `CodeDesigner.Mips64LE.Common/Disassembler/MipsDisassembler.cs`
2. `CodeDesigner.Mips64LE.Common/Instructions/Core/Shift/ShiftInstructions.cs`
3. `CodeDesigner.Mips64LE.Common/Instructions/Core/BranchJump/BranchJumpInstructions.cs`
4. `CodeDesigner.Mips64LE.TestProject/TestCoprocessorInstructions.cs` (new)

## Backward Compatibility
✅ All 50 original tests still pass
✅ No breaking changes to existing API
✅ Existing instruction disassembly unchanged

## Future Enhancements
- VU1 (Vector Unit 1) instruction support
- Additional EE-specific parallel instructions (PEXT, PCPYH, etc.)
- COP3 instruction support (if needed)
- Enhanced formatting for vector instruction component masks

## Notes
- The infrastructure now supports easy addition of more coprocessor instructions
- The routing system is extensible for future instruction set additions
- All instruction classes follow the existing pattern and conventions
