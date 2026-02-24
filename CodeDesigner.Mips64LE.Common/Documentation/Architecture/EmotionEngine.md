# Emotion Engine Core CPU Instruction Set

## Arithmetic & Integer Operations

| Instruction | Description |
|-------------|-------------|
| ADD | Add signed 32-bit integers (overflow trap) |
| ADDU | Add unsigned 32-bit integers |
| DADD | Add signed 64-bit integers (overflow trap) |
| DADDU | Add unsigned 64-bit integers |
| SUB | Subtract signed 32-bit integers |
| SUBU | Subtract unsigned 32-bit integers |
| DSUB | Subtract signed 64-bit integers |
| DSUBU | Subtract unsigned 64-bit integers |
| ADDI | Add immediate (signed, overflow trap) |
| ADDIU | Add immediate unsigned |
| DADDI | Add immediate 64-bit (overflow trap) |
| DADDIU | Add immediate 64-bit unsigned |
| MULT | Multiply signed 32-bit |
| MULTU | Multiply unsigned 32-bit |
| DMULT | Multiply signed 64-bit |
| DMULTU | Multiply unsigned 64-bit |
| DIV | Divide signed 32-bit |
| DIVU | Divide unsigned 32-bit |
| DDIV | Divide signed 64-bit |
| DDIVU | Divide unsigned 64-bit |
| MFHI | Move from HI register |
| MFLO | Move from LO register |
| MTHI | Move to HI register |
| MTLO | Move to LO register |

## Logical Operations

| Instruction | Description |
|-------------|-------------|
| AND | Bitwise AND |
| OR | Bitwise OR |
| XOR | Bitwise XOR |
| NOR | Bitwise NOR |
| ANDI | AND immediate |
| ORI | OR immediate |
| XORI | XOR immediate |

## Shift & Rotate

| Instruction | Description |
|-------------|-------------|
| SLL | Shift left logical |
| SRL | Shift right logical |
| SRA | Shift right arithmetic |
| DSLL | Shift left logical (64-bit) |
| DSRL | Shift right logical (64-bit) |
| DSRA | Shift right arithmetic (64-bit) |
| SLLV | Shift left logical variable |
| SRLV | Shift right logical variable |
| SRAV | Shift right arithmetic variable |
| DSLLV | Shift left logical variable (64-bit) |
| DSRLV | Shift right logical variable (64-bit) |
| DSRAV | Shift right arithmetic variable (64-bit) |

## Comparison & Set Instructions

| Instruction | Description |
|-------------|-------------|
| SLT | Set if less than (signed) |
| SLTU | Set if less than (unsigned) |
| SLTI | Set if less than immediate (signed) |
| SLTIU | Set if less than immediate (unsigned) |

## Branch & Jump

| Instruction | Description |
|-------------|-------------|
| BEQ | Branch if equal |
| BNE | Branch if not equal |
| BLEZ | Branch if less than or equal to zero |
| BGTZ | Branch if greater than zero |
| BLTZ | Branch if less than zero |
| BGEZ | Branch if greater than or equal to zero |
| BEQL | Branch if equal (likely) |
| BNEL | Branch if not equal (likely) |
| J | Jump |
| JAL | Jump and link |
| JR | Jump register |
| JALR | Jump and link register |

## Load Instructions

| Instruction | Description |
|-------------|-------------|
| LB | Load byte (signed) |
| LBU | Load byte (unsigned) |
| LH | Load halfword (signed) |
| LHU | Load halfword (unsigned) |
| LW | Load word |
| LWU | Load word unsigned |
| LD | Load doubleword |
| LWL | Load word left |
| LWR | Load word right |
| LDL | Load doubleword left |
| LDR | Load doubleword right |
| LQ | Load quadword (EE extension) |

## Store Instructions

| Instruction | Description |
|-------------|-------------|
| SB | Store byte |
| SH | Store halfword |
| SW | Store word |
| SD | Store doubleword |
| SWL | Store word left |
| SWR | Store word right |
| SDL | Store doubleword left |
| SDR | Store doubleword right |
| SQ | Store quadword (EE extension) |

## Move & Special

| Instruction | Description |
|-------------|-------------|
| MOVZ | Move if zero |
| MOVN | Move if not zero |
| LUI | Load upper immediate |
| NOP | No operation |
| SYNC | Memory synchronization |

## Trap & System

| Instruction | Description |
|-------------|-------------|
| SYSCALL | System call exception |
| BREAK | Breakpoint exception |
| TGE | Trap if greater or equal |
| TGEU | Trap if greater or equal unsigned |
| TLT | Trap if less than |
| TLTU | Trap if less than unsigned |
| TEQ | Trap if equal |
| TNE | Trap if not equal |

## Notes

- LQ and SQ are Emotion Engine–specific 128-bit load/store instructions
- The EE core supports dual-issue execution with pairing restrictions
- Branch delay slots are present and must be handled correctly
- This instruction set is not fully compatible with standard desktop MIPS64 CPUs
