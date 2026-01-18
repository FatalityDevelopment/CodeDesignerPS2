export function getNonce() {
  let text = "";
  const chars =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  for (let i = 0; i < 32; i++)
    text += chars.charAt(Math.floor(Math.random() * chars.length));
  return text;
}

export function formatBytes(bytes: Buffer) {
  return [...bytes]
    .reverse()
    .map((b) => b.toString(16).padStart(2, "0"))
    .join(" ");
}

/**
 * Disassemble MIPS binary code with inline pointer calculation comments
 * @param startAddr - Virtual address where disassembly begins
 * @param buffer - Binary data to disassemble
 * @returns Array of disassembled instructions with analysis comments
 */
export function disassemble(startAddr: number, buffer: Buffer) {
  const rows: any[] = [];
  const regs = new Map<string, number>(); // Track register values for inline comments

  /**
   * Parse hex immediate from instruction args
   */
  const parseInlineImm = (s: string): number => {
    const cleaned = s.replace("$", "");
    return parseInt(cleaned, 16);
  };

  /**
   * Get signed 16-bit immediate
   */
  const getSignedInline = (val: number): number => {
    return (val << 16) >> 16;
  };

  for (let offset = 0; offset < buffer.length; offset += 4) {
    const slice = buffer.slice(offset, offset + 4);
    if (slice.length < 4) break;

    const bytesLE = Array.from(slice)
      .reverse()
      .map((b) => b.toString(16).padStart(2, "0").toUpperCase())
      .join(" ");
    const instr = slice.readUInt32LE(0);
    const currentAddr = startAddr + offset;
    const decoded = decodeMIPS(instr, currentAddr);

    if (decoded) {
      let inlineComment = decoded.comment || "";
      const op = decoded.op.toLowerCase();
      const args = decoded.args.split(",").map((a: string) => a.trim());

      // Track and comment on pointer calculations
      if (op === "lui" && args.length >= 2) {
        const rt = args[0];
        const imm = parseInlineImm(args[1]);
        const value = (imm << 16) >>> 0;
        regs.set(rt, value);
      }
      else if ((op === "ori" || op === "addiu" || op === "addi") && args.length >= 3) {
        const rt = args[0];
        const rs = args[1];
        const imm = parseInlineImm(args[2]);
        const baseValue = regs.get(rs);
        
        if (baseValue !== undefined) {
          const signedImm = getSignedInline(imm);
          const result = op === "ori" ? (baseValue | imm) >>> 0 : (baseValue + signedImm) >>> 0;
          regs.set(rt, result);
          inlineComment = `; ${rt} = 0x${result.toString(16).toUpperCase()} (calculated addr)`;
        } else {
          regs.delete(rt);
        }
      }
      else if ((op === "addu" || op === "add") && args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rt = args[2];
        const a = regs.get(rs);
        const b = regs.get(rt);
        
        if (a !== undefined && b !== undefined) {
          const result = (a + b) >>> 0;
          regs.set(rd, result);
          inlineComment = `; ${rd} = 0x${result.toString(16).toUpperCase()} (calculated addr)`;
        } else {
          regs.delete(rd);
        }
      }
      else if ((op === "subu" || op === "sub") && args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rt = args[2];
        const a = regs.get(rs);
        const b = regs.get(rt);
        
        if (a !== undefined && b !== undefined) {
          const result = (a - b) >>> 0;
          regs.set(rd, result);
          inlineComment = `; ${rd} = 0x${result.toString(16).toUpperCase()} (calculated addr)`;
        } else {
          regs.delete(rd);
        }
      }
      else if (op === "sll" && args.length >= 3) {
        const rd = args[0];
        const rt = args[1];
        const sa = parseInt(args[2], 10);
        const val = regs.get(rt);
        
        if (val !== undefined) {
          const result = (val << sa) >>> 0;
          regs.set(rd, result);
          inlineComment = `; ${rd} = 0x${result.toString(16).toUpperCase()} (shifted ptr)`;
        } else {
          regs.delete(rd);
        }
      }
      // Clear register tracking for operations that modify registers unpredictably
      else if (args.length > 0 && !op.startsWith("b") && op !== "j" && op !== "jal" && op !== "nop") {
        const destReg = args[0];
        if (destReg && !destReg.includes("(") && !destReg.includes("$")) {
          regs.delete(destReg);
        }
      }

      rows.push({
        addr: currentAddr.toString(16).toUpperCase().padStart(8, "0"),
        bytes: bytesLE,
        label: decoded.label || "",
        op: decoded.op,
        args: decoded.args,
        comment: inlineComment,
      });
    }
  }

  console.log("Disassembled", rows.length, "instructions");
  if (rows.length > 0) {
    console.log(
      "First instruction:",
      rows[0].addr,
      rows[0].bytes,
      rows[0].op,
      rows[0].args
    );
  }

  return rows;
}

/**
 * Represents a pointer reference found during analysis
 */
export interface PointerRef {
  instrAddr: number;                    // Address of the instruction making the reference
  kind: "load" | "store" | "jump" | "call";  // Type of memory operation
  baseReg: string;                      // Base register used (e.g., "gp", "sp")
  baseValue: number;                    // Resolved value of base register
  offset: number;                       // Offset from base (signed)
  targetAddr: number;                   // Final calculated address
  size?: number;                        // Size of access in bytes (1, 2, 4, 8, 16)
  type?: string;                        // Specific instruction type (lb, lw, etc.)
  isFunction?: boolean;                 // Whether target is likely a function
  dataType?: string;                    // Inferred data type (int, float, string, etc.)
}

/**
 * Analyze disassembled instructions to track register values and identify pointer references
 * This performs data flow analysis to resolve memory addresses and function calls
 * @param rows - Array of disassembled instructions
 * @returns Array of resolved pointer references with metadata
 */
export function analyzePointers(rows: any[]): PointerRef[] {
  const refs: PointerRef[] = [];
  const regs = new Map<string, number>();

  /**
   * Parse hexadecimal immediate value from string (e.g., "$1234" -> 0x1234)
   */
  const parseImm = (s: string): number => {
    const cleaned = s.replace("$", "");
    return parseInt(cleaned, 16);
  };

  /**
   * Convert 16-bit value to signed integer
   */
  const getSignedImm = (val: number): number => {
    return (val << 16) >> 16;
  };

  /**
   * Determine memory access size from instruction mnemonic
   */
  const getAccessSize = (op: string): number => {
    if (op.includes("b")) return 1;  // byte (lb, sb, lbu)
    if (op.includes("h")) return 2;  // halfword (lh, sh, lhu)
    if (op.includes("w") && !op.includes("q")) return 4;  // word (lw, sw)
    if (op === "ld" || op === "sd") return 8;  // doubleword
    if (op === "lq" || op === "sq") return 16; // quadword (PS2 128-bit)
    return 4; // default to word
  };

  /**
   * Infer data type from instruction and context
   */
  const inferDataType = (op: string, targetAddr: number): string | undefined => {
    // Floating point operations
    if (op.includes("c1") || op.includes(".s") || op.includes(".w")) {
      return "float";
    }
    // Vector operations (PS2)
    if (op.startsWith("v") || op.includes("c2") || op === "lq" || op === "sq") {
      return "vector";
    }
    // Byte operations might be strings
    if (op === "lb" || op === "lbu" || op === "sb") {
      return "byte/char";
    }
    // Check if address looks like code (aligned to 4 bytes)
    if (targetAddr % 4 === 0 && targetAddr >= 0x00100000) {
      return "code/function";
    }
    return undefined;
  };

  /**
   * Check if an address is likely a function entry point
   */
  const isLikelyFunction = (addr: number, rows: any[]): boolean => {
    // Find the row at this address
    const targetRow = rows.find(r => parseInt(r.addr, 16) === addr);
    if (!targetRow) return false;
    
    // Common function prologue patterns
    const op = targetRow.op.toLowerCase();
    const args = targetRow.args.toLowerCase();
    
    // Stack frame allocation: addiu sp, sp, -X
    if (op === "addiu" && args.includes("sp, sp") && args.includes("-")) {
      return true;
    }
    
    // Save return address: sw ra, X(sp)
    if (op === "sw" && args.includes("ra") && args.includes("sp")) {
      return true;
    }
    
    return false;
  };

  for (const row of rows) {
    const instrAddr = parseInt(row.addr, 16);
    const op = row.op.toLowerCase();
    const args = row.args.split(",").map((a: string) => a.trim());

    // LUI - Load Upper Immediate
    if (op === "lui") {
      if (args.length >= 2) {
        const rt = args[0];
        const imm = parseImm(args[1]);
        regs.set(rt, (imm << 16) >>> 0);
        console.log(`LUI: ${rt} = 0x${((imm << 16) >>> 0).toString(16).toUpperCase()}`);
      }
      continue;
    }

    // ADDIU/ADDI - Add Immediate (Unsigned)
    if (op === "addiu" || op === "addi") {
      if (args.length >= 3) {
        const rt = args[0];
        const rs = args[1];
        const imm = parseImm(args[2]);
        const base = regs.get(rs);
        
        if (base !== undefined) {
          const signedImm = getSignedImm(imm);
          const result = (base + signedImm) >>> 0;
          regs.set(rt, result);
          console.log(`ADDIU: ${rt} = ${rs}(0x${base.toString(16)}) + 0x${imm.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rt);
          console.log(`ADDIU: ${rt} = unknown (${rs} not tracked)`);
        }
      }
      continue;
    }

    // ADDU/ADD - Add Unsigned
    if (op === "addu" || op === "add") {
      if (args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rt = args[2];
        const a = regs.get(rs);
        const b = regs.get(rt);
        
        if (a !== undefined && b !== undefined) {
          const result = (a + b) >>> 0;
          regs.set(rd, result);
          console.log(`ADDU: ${rd} = ${rs}(0x${a.toString(16)}) + ${rt}(0x${b.toString(16)}) = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rd);
        }
      }
      continue;
    }

    // SUBU/SUB - Subtract
    if (op === "subu" || op === "sub") {
      if (args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rt = args[2];
        const a = regs.get(rs);
        const b = regs.get(rt);
        
        if (a !== undefined && b !== undefined) {
          const result = (a - b) >>> 0;
          regs.set(rd, result);
          console.log(`SUBU: ${rd} = ${rs}(0x${a.toString(16)}) - ${rt}(0x${b.toString(16)}) = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rd);
        }
      }
      continue;
    }

    // AND/ANDI - Bitwise AND
    if (op === "and" || op === "andi") {
      if (args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rtOrImm = args[2];
        const a = regs.get(rs);
        const b = op === "andi" ? parseImm(rtOrImm) : regs.get(rtOrImm);
        
        if (a !== undefined && b !== undefined) {
          const result = (a & b) >>> 0;
          regs.set(rd, result);
          console.log(`${op.toUpperCase()}: ${rd} = 0x${a.toString(16)} & 0x${b.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rd);
        }
      }
      continue;
    }

    // OR - Bitwise OR
    if (op === "or") {
      if (args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rt = args[2];
        const a = regs.get(rs);
        const b = regs.get(rt);
        
        if (a !== undefined && b !== undefined) {
          const result = (a | b) >>> 0;
          regs.set(rd, result);
          console.log(`OR: ${rd} = 0x${a.toString(16)} | 0x${b.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rd);
        }
      }
      continue;
    }

    // ORI - OR Immediate
    if (op === "ori") {
      if (args.length >= 3) {
        const rt = args[0];
        const rs = args[1];
        const imm = parseImm(args[2]);
        const base = regs.get(rs);
        
        if (base !== undefined) {
          const result = (base | imm) >>> 0;
          regs.set(rt, result);
          console.log(`ORI: ${rt} = 0x${base.toString(16)} | 0x${imm.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rt);
        }
      }
      continue;
    }

    // XOR/XORI - Bitwise XOR
    if (op === "xor" || op === "xori") {
      if (args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rtOrImm = args[2];
        const a = regs.get(rs);
        const b = op === "xori" ? parseImm(rtOrImm) : regs.get(rtOrImm);
        
        if (a !== undefined && b !== undefined) {
          const result = (a ^ b) >>> 0;
          regs.set(rd, result);
          console.log(`${op.toUpperCase()}: ${rd} = 0x${a.toString(16)} ^ 0x${b.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rd);
        }
      }
      continue;
    }

    // NOR - Bitwise NOR
    if (op === "nor") {
      if (args.length >= 3) {
        const rd = args[0];
        const rs = args[1];
        const rt = args[2];
        const a = regs.get(rs);
        const b = regs.get(rt);
        
        if (a !== undefined && b !== undefined) {
          const result = (~(a | b)) >>> 0;
          regs.set(rd, result);
          console.log(`NOR: ${rd} = ~(0x${a.toString(16)} | 0x${b.toString(16)}) = 0x${result.toString(16).toUpperCase()}`);
        } else {
          regs.delete(rd);
        }
      }
      continue;
    }

    // SLL - Shift Left Logical
    if (op === "sll" && args.length >= 3) {
      const rd = args[0];
      const rt = args[1];
      const sa = parseInt(args[2], 10);
      const val = regs.get(rt);
      
      if (val !== undefined) {
        const result = (val << sa) >>> 0;
        regs.set(rd, result);
        console.log(`SLL: ${rd} = 0x${val.toString(16)} << ${sa} = 0x${result.toString(16).toUpperCase()}`);
      } else {
        regs.delete(rd);
      }
      continue;
    }

    // SRL - Shift Right Logical
    if (op === "srl" && args.length >= 3) {
      const rd = args[0];
      const rt = args[1];
      const sa = parseInt(args[2], 10);
      const val = regs.get(rt);
      
      if (val !== undefined) {
        const result = (val >>> sa) >>> 0;
        regs.set(rd, result);
        console.log(`SRL: ${rd} = 0x${val.toString(16)} >> ${sa} = 0x${result.toString(16).toUpperCase()}`);
      } else {
        regs.delete(rd);
      }
      continue;
    }

    // SRA - Shift Right Arithmetic
    if (op === "sra" && args.length >= 3) {
      const rd = args[0];
      const rt = args[1];
      const sa = parseInt(args[2], 10);
      const val = regs.get(rt);
      
      if (val !== undefined) {
        const result = (val >> sa) >>> 0;
        regs.set(rd, result);
        console.log(`SRA: ${rd} = 0x${val.toString(16)} >> ${sa} (arith) = 0x${result.toString(16).toUpperCase()}`);
      } else {
        regs.delete(rd);
      }
      continue;
    }

    // Memory operations with offset(base) format
    const memArg = args.find((a: string) => a.includes("(") && a.includes(")"));
    if (memArg) {
      const match = memArg.match(/\$([0-9A-Fa-f]+)\((\w+)\)/);
      if (match) {
        const offset = parseImm(match[1]);
        const baseReg = match[2];
        const baseValue = regs.get(baseReg);

        // Only add reference if we know the base value
        if (baseValue !== undefined) {
          const signedOffset = getSignedImm(offset);
          const targetAddr = (baseValue + signedOffset) >>> 0;
          const size = getAccessSize(op);
          const dataType = inferDataType(op, targetAddr);
          const isFunction = isLikelyFunction(targetAddr, rows);
          
          refs.push({
            instrAddr,
            kind: op.startsWith("l") ? "load" : "store",
            baseReg,
            baseValue,
            offset: signedOffset,
            targetAddr,
            size,
            type: op,
            dataType,
            isFunction,
          });
          
          console.log(
            `${op.toUpperCase()}: @0x${instrAddr.toString(16)} -> 0x${targetAddr.toString(16).toUpperCase()} ` +
            `[${baseReg}=0x${baseValue.toString(16)} + ${signedOffset}] (${size} bytes${dataType ? ', ' + dataType : ''}${isFunction ? ', FUNCTION' : ''})`
          );
        }
      }
      continue;
    }

    // JAL - Jump and Link (function call)
    if (op === "jal") {
      const targetMatch = args[0].match(/\$([0-9A-Fa-f]+)/);
      if (targetMatch) {
        const targetAddr = parseImm(targetMatch[1]);
        const isFunction = isLikelyFunction(targetAddr, rows);
        
        refs.push({
          instrAddr,
          kind: "call",
          baseReg: "pc",
          baseValue: instrAddr,
          offset: 0,
          targetAddr,
          type: "jal",
          isFunction: true,  // JAL always calls a function
          dataType: "function",
        });
        console.log(`JAL: @0x${instrAddr.toString(16)} -> 0x${targetAddr.toString(16).toUpperCase()} (function call)`);
      }
      continue;
    }

    // JR/JALR - Jump Register (indirect jumps/calls)
    if (op === "jr" || op === "jalr") {
      const reg = args[op === "jalr" && args.length > 1 ? 1 : 0];
      const baseValue = regs.get(reg);

      if (baseValue !== undefined) {
        const isFunction = op === "jalr" || isLikelyFunction(baseValue, rows);
        
        refs.push({
          instrAddr,
          kind: op === "jalr" ? "call" : "jump",
          baseReg: reg,
          baseValue,
          offset: 0,
          targetAddr: baseValue,
          type: op,
          isFunction,
          dataType: isFunction ? "function" : undefined,
        });
        
        console.log(`${op.toUpperCase()}: @0x${instrAddr.toString(16)} -> 0x${baseValue.toString(16).toUpperCase()}${isFunction ? ' (function)' : ''}`);
      }
    }

    // Clear destination register for most other operations to avoid stale data
    if (args.length > 0 && !op.startsWith("b") && op !== "j" && op !== "jal") {
      const destReg = args[0];
      if (!memArg && destReg && !destReg.includes("(")) {
        // Only clear if it's not a memory operation and not a branch/jump
        if (!["nop", "syscall", "break", "sync", "jr", "jalr"].includes(op)) {
          // Don't clear if we already handled it above
          const handledOps = ["lui", "addiu", "addi", "addu", "add", "subu", "sub", 
                             "and", "andi", "or", "ori", "xor", "xori", "nor",
                             "sll", "srl", "sra"];
          if (!handledOps.includes(op)) {
            regs.delete(destReg);
          }
        }
      }
    }
  }

  console.log(`Found ${refs.length} resolved pointer references`);
  return refs;
}

function decodeMIPS(instr: number, pc: number) {
  const opcode = (instr >> 26) & 0x3f;
  const rs = (instr >> 21) & 0x1f;
  const rt = (instr >> 16) & 0x1f;
  const rd = (instr >> 11) & 0x1f;
  const sa = (instr >> 6) & 0x1f;
  const funct = instr & 0x3f;
  const imm = instr & 0xffff;
  const signedImm = imm & 0x8000 ? imm | 0xffff0000 : imm;
  const target = instr & 0x3ffffff;
  const code = (instr >> 6) & 0xfffff;

  const reg = (r: number) => {
    const names = [
      "zero",
      "at",
      "v0",
      "v1",
      "a0",
      "a1",
      "a2",
      "a3",
      "t0",
      "t1",
      "t2",
      "t3",
      "t4",
      "t5",
      "t6",
      "t7",
      "s0",
      "s1",
      "s2",
      "s3",
      "s4",
      "s5",
      "s6",
      "s7",
      "t8",
      "t9",
      "k0",
      "k1",
      "gp",
      "sp",
      "fp",
      "ra",
    ];
    return names[r];
  };

  const freg = (r: number) => `f${r}`;

  // Format signed immediate with 4-digit hex padding
  const formatImm = (val: number) => {
    const hex = (val & 0xffff).toString(16).toUpperCase().padStart(4, "0");
    return `$${hex}`;
  };

  // Format branch/jump target address with 8-digit hex padding
  const formatAddr = (addr: number) => {
    const hex = (addr >>> 0).toString(16).toUpperCase().padStart(8, "0");
    return `$${hex}`;
  };

  // Format branch with target address and instruction count
  const formatBranch = (targetAddr: number, offset: number) => {
    const addrHex = (targetAddr >>> 0)
      .toString(16)
      .toUpperCase()
      .padStart(8, "0");
    const instrCount = offset / 4; // Convert byte offset to instruction count
    const sign = instrCount >= 0 ? "+" : "";
    const arrow = instrCount >= 0 ? "▼" : "▲";
    return `$${addrHex} (${sign}${instrCount + 1}${arrow})`;
  };

  if (instr === 0) {
    return { op: "nop", args: "" };
  }

  // SPECIAL opcode (0x00)
  if (opcode === 0x00) {
    switch (funct) {
      case 0x00:
        return sa === 0
          ? { op: "nop", args: "" }
          : { op: "sll", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x02:
        return { op: "srl", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x03:
        return { op: "sra", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x04:
        return { op: "sllv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
      case 0x06:
        return { op: "srlv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
      case 0x07:
        return { op: "srav", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
      case 0x08:
        return { op: "jr", args: reg(rs) };
      case 0x09:
        return rd === 31
          ? { op: "jalr", args: reg(rs) }
          : { op: "jalr", args: `${reg(rd)}, ${reg(rs)}` };
      case 0x0a:
        return { op: "movz", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x0b:
        return { op: "movn", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x0c:
        return { op: "syscall", args: code > 0 ? `${code}` : "" };
      case 0x0d:
        return { op: "break", args: code > 0 ? `${code}` : "" };
      case 0x0f:
        return { op: "sync", args: sa > 0 ? `${sa}` : "" };
      case 0x10:
        return { op: "mfhi", args: reg(rd) };
      case 0x11:
        return { op: "mthi", args: reg(rs) };
      case 0x12:
        return { op: "mflo", args: reg(rd) };
      case 0x13:
        return { op: "mtlo", args: reg(rs) };
      case 0x14:
        return { op: "dsllv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
      case 0x16:
        return { op: "dsrlv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
      case 0x17:
        return { op: "dsrav", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
      case 0x18:
        return rd === 0
          ? { op: "mult", args: `${reg(rs)}, ${reg(rt)}` }
          : { op: "mult", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x19:
        return rd === 0
          ? { op: "multu", args: `${reg(rs)}, ${reg(rt)}` }
          : { op: "multu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x1a:
        return { op: "div", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x1b:
        return { op: "divu", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x1c:
        return {
          op: "dmult",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x1d:
        return {
          op: "dmultu",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x1e:
        return { op: "ddiv", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x1f:
        return { op: "ddivu", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x20:
        return { op: "add", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x21:
        return { op: "addu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x22:
        return { op: "sub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x23:
        return { op: "subu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x24:
        return { op: "and", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x25:
        return { op: "or", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x26:
        return { op: "xor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x27:
        return { op: "nor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x28:
        return { op: "mfsa", args: reg(rd) };
      case 0x29:
        return { op: "mtsa", args: reg(rs) };
      case 0x2a:
        return { op: "slt", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x2b:
        return { op: "sltu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x2c:
        return { op: "dadd", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x2d:
        return { op: "daddu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x2e:
        return { op: "dsub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x2f:
        return { op: "dsubu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
      case 0x30:
        return { op: "tge", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x31:
        return { op: "tgeu", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x32:
        return { op: "tlt", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x33:
        return { op: "tltu", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x34:
        return { op: "teq", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x36:
        return { op: "tne", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x38:
        return { op: "dsll", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3a:
        return { op: "dsrl", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3b:
        return { op: "dsra", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3c:
        return { op: "dsll32", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3e:
        return { op: "dsrl32", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3f:
        return { op: "dsra32", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
    }
  }

  // REGIMM opcode (0x01)
  if (opcode === 0x01) {
    switch (rt) {
      case 0x00:
        return {
          op: "bltz",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x01:
        return {
          op: "bgez",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x02:
        return {
          op: "bltzl",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x03:
        return {
          op: "bgezl",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x08:
        return { op: "tgei", args: `${reg(rs)}, ${formatImm(signedImm)}` };
      case 0x09:
        return { op: "tgeiu", args: `${reg(rs)}, ${formatImm(signedImm)}` };
      case 0x0a:
        return { op: "tlti", args: `${reg(rs)}, ${formatImm(signedImm)}` };
      case 0x0b:
        return { op: "tltiu", args: `${reg(rs)}, ${formatImm(signedImm)}` };
      case 0x0c:
        return { op: "teqi", args: `${reg(rs)}, ${formatImm(signedImm)}` };
      case 0x0e:
        return { op: "tnei", args: `${reg(rs)}, ${formatImm(signedImm)}` };
      case 0x10:
        return {
          op: "bltzal",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x11:
        return {
          op: "bgezal",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x12:
        return {
          op: "bltzall",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x13:
        return {
          op: "bgezall",
          args: `${reg(rs)}, ${formatBranch(
            pc + 4 + (signedImm << 2),
            signedImm << 2
          )}`,
        };
      case 0x18:
        return { op: "mtsab", args: `${reg(rs)}, ${formatImm(imm)}` };
      case 0x19:
        return { op: "mtsah", args: `${reg(rs)}, ${formatImm(imm)}` };
    }
  }

  // J and JAL (0x02, 0x03)
  if (opcode === 0x02) {
    return { op: "j", args: formatAddr((pc & 0xf0000000) | (target << 2)) };
  }
  if (opcode === 0x03) {
    return { op: "jal", args: formatAddr((pc & 0xf0000000) | (target << 2)) };
  }

  // Branch instructions (0x04-0x07, 0x14-0x17)
  if (opcode === 0x04)
    return {
      op: "beq",
      args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x05)
    return {
      op: "bne",
      args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x06)
    return {
      op: "blez",
      args: `${reg(rs)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x07)
    return {
      op: "bgtz",
      args: `${reg(rs)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x14)
    return {
      op: "beql",
      args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x15)
    return {
      op: "bnel",
      args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x16)
    return {
      op: "blezl",
      args: `${reg(rs)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };
  if (opcode === 0x17)
    return {
      op: "bgtzl",
      args: `${reg(rs)}, ${formatBranch(
        pc + 4 + (signedImm << 2),
        signedImm << 2
      )}`,
    };

  // Immediate arithmetic/logical (0x08-0x0f, 0x18-0x19)
  if (opcode === 0x08)
    return {
      op: "addi",
      args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
    };
  if (opcode === 0x09)
    return {
      op: "addiu",
      args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
    };
  if (opcode === 0x0a)
    return {
      op: "slti",
      args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
    };
  if (opcode === 0x0b)
    return { op: "sltiu", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
  if (opcode === 0x0c)
    return { op: "andi", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
  if (opcode === 0x0d)
    return { op: "ori", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
  if (opcode === 0x0e)
    return { op: "xori", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
  if (opcode === 0x0f)
    return { op: "lui", args: `${reg(rt)}, ${formatImm(imm)}` };
  if (opcode === 0x18)
    return {
      op: "daddi",
      args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
    };
  if (opcode === 0x19)
    return {
      op: "daddiu",
      args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
    };

  // Load instructions (0x1a-0x1b, 0x20-0x27, 0x37)
  if (opcode === 0x1a)
    return {
      op: "ldl",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x1b)
    return {
      op: "ldr",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x20)
    return {
      op: "lb",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x21)
    return {
      op: "lh",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x22)
    return {
      op: "lwl",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x23)
    return {
      op: "lw",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x24)
    return {
      op: "lbu",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x25)
    return {
      op: "lhu",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x26)
    return {
      op: "lwr",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x27)
    return {
      op: "lwu",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x37)
    return {
      op: "ld",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };

  // Store instructions (0x28-0x2f, 0x3f)
  if (opcode === 0x28)
    return {
      op: "sb",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x29)
    return {
      op: "sh",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x2a)
    return {
      op: "swl",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x2b)
    return {
      op: "sw",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x2c)
    return {
      op: "sdl",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x2d)
    return {
      op: "sdr",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x2e)
    return {
      op: "swr",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x2f)
    return { op: "cache", args: `${rt}, ${formatImm(signedImm)}(${reg(rs)})` };
  if (opcode === 0x33)
    return { op: "pref", args: `${rt}, ${formatImm(signedImm)}(${reg(rs)})` };
  if (opcode === 0x3f)
    return {
      op: "sd",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };

  // Quadword loads/stores (PS2 specific - 128-bit)
  if (opcode === 0x1e)
    return {
      op: "lq",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x1f)
    return {
      op: "sq",
      args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };

  // Floating point loads/stores
  if (opcode === 0x31)
    return {
      op: "lwc1",
      args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x35)
    return {
      op: "ldc1",
      args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x36)
    return {
      op: "lqc2",
      args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x39)
    return {
      op: "swc1",
      args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x3d)
    return {
      op: "sdc1",
      args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };
  if (opcode === 0x3e)
    return {
      op: "sqc2",
      args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
    };

  // MMI opcode (0x1C) - PS2 specific
  if (opcode === 0x1c) {
    const mmiFunc = funct & 0x3f;
    const mmiFmt = (instr >> 6) & 0x1f;

    switch (mmiFunc) {
      case 0x00:
        return {
          op: "madd",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x01:
        return {
          op: "maddu",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x04:
        return { op: "plzcw", args: `${reg(rd)}, ${reg(rs)}` };
      case 0x08:
        switch (mmiFmt) {
          case 0x00:
            return { op: "paddw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x01:
            return { op: "psubw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x02:
            return { op: "pcgtw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x03:
            return { op: "pmaxw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x04:
            return { op: "paddh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x05:
            return { op: "psubh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x06:
            return { op: "pcgth", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x07:
            return { op: "pmaxh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x08:
            return { op: "paddb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x09:
            return { op: "psubb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0a:
            return { op: "pcgtb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x10:
            return { op: "paddsw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x11:
            return { op: "psubsw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x12:
            return { op: "pextlw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x13:
            return { op: "ppacw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x14:
            return { op: "paddsh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x15:
            return { op: "psubsh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x16:
            return { op: "pextlh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x17:
            return { op: "ppach", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x18:
            return { op: "paddsb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x19:
            return { op: "psubsb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x1a:
            return { op: "pextlb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x1b:
            return { op: "ppacb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x1e:
            return { op: "pext5", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x1f:
            return { op: "ppac5", args: `${reg(rd)}, ${reg(rt)}` };
        }
        break;
      case 0x09:
        switch (mmiFmt) {
          case 0x00:
            return { op: "pmaddw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x01:
            return { op: "pmsubw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x02:
            return { op: "psllvw", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
          case 0x03:
            return { op: "psrlvw", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
          case 0x04:
            return { op: "pmfhi", args: reg(rd) };
          case 0x05:
            return { op: "pmflo", args: reg(rd) };
          case 0x06:
            return { op: "pinth", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x08:
            return { op: "pmultw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x09:
            return { op: "pdivw", args: `${reg(rs)}, ${reg(rt)}` };
          case 0x0a:
            return { op: "pcpyld", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0c:
            return { op: "pmaddh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0d:
            return { op: "phmadh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0e:
            return { op: "pand", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0f:
            return { op: "pxor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x10:
            return { op: "pmsubh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x11:
            return { op: "phmsbh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x12:
            return { op: "pexeh", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x13:
            return { op: "prevh", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x14:
            return { op: "pmulth", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x15:
            return { op: "pdivbw", args: `${reg(rs)}, ${reg(rt)}` };
          case 0x16:
            return { op: "pexew", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x17:
            return { op: "prot3w", args: `${reg(rd)}, ${reg(rt)}` };
        }
        break;
      case 0x10:
        return { op: "mfhi1", args: reg(rd) };
      case 0x11:
        return { op: "mthi1", args: reg(rs) };
      case 0x12:
        return { op: "mflo1", args: reg(rd) };
      case 0x13:
        return { op: "mtlo1", args: reg(rs) };
      case 0x18:
        return {
          op: "mult1",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x19:
        return {
          op: "multu1",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x1a:
        return { op: "div1", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x1b:
        return { op: "divu1", args: `${reg(rs)}, ${reg(rt)}` };
      case 0x20:
        return {
          op: "madd1",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x21:
        return {
          op: "maddu1",
          args:
            rd === 0
              ? `${reg(rs)}, ${reg(rt)}`
              : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
        };
      case 0x28:
        switch (mmiFmt) {
          case 0x00:
            return {
              op: "pmadduw",
              args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
            };
          case 0x02:
            return { op: "psravw", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
          case 0x03:
            return { op: "pmthi", args: reg(rs) };
          case 0x04:
            return { op: "pmtlo", args: reg(rs) };
          case 0x05:
            return { op: "pinteh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x08:
            return {
              op: "pmultuw",
              args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
            };
          case 0x09:
            return { op: "pdivuw", args: `${reg(rs)}, ${reg(rt)}` };
          case 0x0a:
            return { op: "pcpyud", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0e:
            return { op: "por", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0f:
            return { op: "pnor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x12:
            return { op: "pexch", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x13:
            return { op: "pcpyh", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x16:
            return { op: "pexcw", args: `${reg(rd)}, ${reg(rt)}` };
        }
        break;
      case 0x29:
        switch (mmiFmt) {
          case 0x00:
            return { op: "psubuw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x01:
            return { op: "pextuw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x02:
            return { op: "pceqw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x03:
            return { op: "pminw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x04:
            return { op: "padsbh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x05:
            return { op: "psubuh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x06:
            return { op: "pceqh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x07:
            return { op: "pminh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x0a:
            return { op: "pceqb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x10:
            return { op: "padduw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x11:
            return { op: "pextuw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x14:
            return { op: "padduh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x15:
            return { op: "psubuh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x16:
            return { op: "pextuh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x18:
            return { op: "paddub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x19:
            return { op: "psubub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x1a:
            return { op: "pextub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x1b:
            return { op: "qfsrv", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
          case 0x1d:
            return { op: "pabsw", args: `${reg(rd)}, ${reg(rt)}` };
          case 0x1e:
            return { op: "pabsh", args: `${reg(rd)}, ${reg(rt)}` };
        }
        break;
      case 0x30:
        const fmt = mmiFmt & 0x1f;
        switch (fmt) {
          case 0x00:
            return { op: "pmfhl.lw", args: reg(rd) };
          case 0x01:
            return { op: "pmfhl.uw", args: reg(rd) };
          case 0x02:
            return { op: "pmfhl.slw", args: reg(rd) };
          case 0x03:
            return { op: "pmfhl.lh", args: reg(rd) };
          case 0x04:
            return { op: "pmfhl.sh", args: reg(rd) };
        }
        break;
      case 0x31:
        if (mmiFmt === 0x00) return { op: "pmthl.lw", args: reg(rs) };
        break;
      case 0x34:
        return { op: "psllh", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x36:
        return { op: "psrlh", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x37:
        return { op: "psrah", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3c:
        return { op: "psllw", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3e:
        return { op: "psrlw", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
      case 0x3f:
        return { op: "psraw", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
    }
  }

  // COP0 opcode (0x10)
  if (opcode === 0x10) {
    const cop0Func = rs;
    if (cop0Func === 0x00) return { op: "mfc0", args: `${reg(rt)}, $${rd}` };
    if (cop0Func === 0x04) return { op: "mtc0", args: `${reg(rt)}, $${rd}` };
    if (cop0Func === 0x08) {
      const bc0 = rt;
      if (bc0 === 0x00)
        return {
          op: "bc0f",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc0 === 0x01)
        return {
          op: "bc0t",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc0 === 0x02)
        return {
          op: "bc0fl",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc0 === 0x03)
        return {
          op: "bc0tl",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
    }
    if (cop0Func === 0x10) {
      if (funct === 0x01) return { op: "tlbr", args: "" };
      if (funct === 0x02) return { op: "tlbwi", args: "" };
      if (funct === 0x06) return { op: "tlbwr", args: "" };
      if (funct === 0x08) return { op: "tlbp", args: "" };
      if (funct === 0x18) return { op: "eret", args: "" };
      if (funct === 0x38) return { op: "ei", args: "" };
      if (funct === 0x39) return { op: "di", args: "" };
    }
  }

  // COP1 (FPU) opcode (0x11)
  if (opcode === 0x11) {
    const cop1Func = rs;
    if (cop1Func === 0x00)
      return { op: "mfc1", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop1Func === 0x02)
      return { op: "cfc1", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop1Func === 0x04)
      return { op: "mtc1", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop1Func === 0x06)
      return { op: "ctc1", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop1Func === 0x08) {
      const bc1 = rt;
      if (bc1 === 0x00)
        return {
          op: "bc1f",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc1 === 0x01)
        return {
          op: "bc1t",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc1 === 0x02)
        return {
          op: "bc1fl",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc1 === 0x03)
        return {
          op: "bc1tl",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
    }
    if (cop1Func === 0x10) {
      // S format (single precision)
      switch (funct) {
        case 0x00:
          return { op: "add.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
        case 0x01:
          return { op: "sub.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
        case 0x02:
          return { op: "mul.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
        case 0x03:
          return { op: "div.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
        case 0x04:
          return { op: "sqrt.s", args: `${freg(sa)}, ${freg(rt)}` };
        case 0x05:
          return { op: "abs.s", args: `${freg(sa)}, ${freg(rd)}` };
        case 0x06:
          return { op: "mov.s", args: `${freg(sa)}, ${freg(rd)}` };
        case 0x07:
          return { op: "neg.s", args: `${freg(sa)}, ${freg(rd)}` };
        case 0x16:
          return {
            op: "rsqrt.s",
            args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}`,
          };
        case 0x18:
          return { op: "adda.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x19:
          return { op: "suba.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x1a:
          return { op: "mula.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x1c:
          return {
            op: "madd.s",
            args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}`,
          };
        case 0x1d:
          return {
            op: "msub.s",
            args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}`,
          };
        case 0x1e:
          return { op: "madda.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x1f:
          return { op: "msuba.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x24:
          return { op: "cvt.w.s", args: `${freg(sa)}, ${freg(rd)}` };
        case 0x28:
          return { op: "max.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
        case 0x29:
          return { op: "min.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
        case 0x30:
          return { op: "c.f.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x32:
          return { op: "c.eq.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x34:
          return { op: "c.lt.s", args: `${freg(rd)}, ${freg(rt)}` };
        case 0x36:
          return { op: "c.le.s", args: `${freg(rd)}, ${freg(rt)}` };
      }
    }
    if (cop1Func === 0x14) {
      // W format (word fixed point)
      if (funct === 0x20)
        return { op: "cvt.s.w", args: `${freg(sa)}, ${freg(rd)}` };
    }
  }

  // COP2 (VU0) opcode (0x12) - PS2 Vector Unit
  if (opcode === 0x12) {
    const cop2Func = rs;
    if (cop2Func === 0x01)
      return { op: "qmfc2", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop2Func === 0x02)
      return { op: "cfc2", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop2Func === 0x05)
      return { op: "qmtc2", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop2Func === 0x06)
      return { op: "ctc2", args: `${reg(rt)}, ${freg(rd)}` };
    if (cop2Func === 0x08) {
      const bc2 = rt;
      if (bc2 === 0x00)
        return {
          op: "bc2f",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc2 === 0x01)
        return {
          op: "bc2t",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc2 === 0x02)
        return {
          op: "bc2fl",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
      if (bc2 === 0x03)
        return {
          op: "bc2tl",
          args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
        };
    }

    // VU0 macro mode instructions (COP2 special functions)
    if ((cop2Func & 0x10) === 0x10) {
      const vuFunc = funct & 0x3f;
      const dest = (instr >> 21) & 0xf; // destination mask (xyzw)
      const ft = (instr >> 16) & 0x1f;
      const fs = (instr >> 11) & 0x1f;
      const fd = (instr >> 6) & 0x1f;
      const bc = (instr >> 0) & 0x3; // broadcast field

      const vreg = (r: number) => `vf${r}`;
      const vireg = (r: number) => `vi${r}`;
      const destStr = (d: number) => {
        if (d === 0xf) return "";
        let s = "";
        if (d & 8) s += "x";
        if (d & 4) s += "y";
        if (d & 2) s += "z";
        if (d & 1) s += "w";
        return s ? `.${s}` : "";
      };
      const bcStr = (b: number) => ["x", "y", "z", "w"][b];

      // VU0 computational instructions
      switch (vuFunc) {
        case 0x00:
          return {
            op: "vadd",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x01:
          return {
            op: "vsub",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x02:
          return {
            op: "vmul",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x03:
          return {
            op: "vmax",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x04:
          return {
            op: "vmin",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x05:
          return {
            op: "vmul",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(
              ft
            )}.${bcStr(bc)}`,
          };
        case 0x06:
          return {
            op: "vmulq",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
          };
        case 0x07:
          return {
            op: "vmaxi",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
          };
        case 0x08:
          return {
            op: "vaddq",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
          };
        case 0x09:
          return {
            op: "vsubq",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
          };
        case 0x0a:
          return {
            op: "vaddi",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
          };
        case 0x0b:
          return {
            op: "vsubi",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
          };
        case 0x0c:
          return {
            op: "vmini",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
          };
        case 0x0d:
          return {
            op: "vadd",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(
              ft
            )}.${bcStr(bc)}`,
          };
        case 0x0e:
          return {
            op: "vsub",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(
              ft
            )}.${bcStr(bc)}`,
          };
        case 0x0f:
          return {
            op: "vopmsub",
            args: `${vreg(fd)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x10:
          return {
            op: "vacc",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x11:
          return {
            op: "vmsub",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x12:
          return {
            op: "vmadd",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x13:
          return {
            op: "vmmax",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x14:
          return {
            op: "vmmin",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x15:
          return {
            op: "vmsub",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(
              ft
            )}.${bcStr(bc)}`,
          };
        case 0x16:
          return {
            op: "vmsubq",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
          };
        case 0x17:
          return {
            op: "vmmini",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
          };
        case 0x18:
          return {
            op: "vaddabc",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
          };
        case 0x19:
          return {
            op: "vsubabc",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
          };
        case 0x1a:
          return {
            op: "vmulabc",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
          };
        case 0x1b:
          return {
            op: "vmadda",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x1c:
          return {
            op: "vmsuba",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x1d:
          return {
            op: "vmadd",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(
              ft
            )}.${bcStr(bc)}`,
          };
        case 0x1e:
          return {
            op: "vmaddq",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
          };
        case 0x1f:
          return {
            op: "vmmaxi",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
          };
        case 0x20:
          return { op: "vaddai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
        case 0x21:
          return { op: "vsubai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
        case 0x22:
          return { op: "vmulai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
        case 0x23:
          return { op: "vmaddai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
        case 0x24:
          return { op: "vmsubai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
        case 0x25:
          return {
            op: "vadda",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x26:
          return {
            op: "vsuba",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x27:
          return {
            op: "vmula",
            args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
          };
        case 0x28:
          return { op: "vopmula", args: `ACC, ${vreg(fs)}, ${vreg(ft)}` };
        case 0x29:
          return { op: "vnop", args: "" };
        case 0x2a:
          return {
            op: "vmove",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}`,
          };
        case 0x2b:
          return {
            op: "vmr32",
            args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}`,
          };
        case 0x2c:
          return {
            op: "vlqi",
            args: `${vreg(fd)}${destStr(dest)}, (${vireg(fs)}++)`,
          };
        case 0x2d:
          return {
            op: "vsqi",
            args: `${vreg(fs)}${destStr(dest)}, (${vireg(ft)}++)`,
          };
        case 0x2e:
          return {
            op: "vlqd",
            args: `${vreg(fd)}${destStr(dest)}, (--${vireg(fs)})`,
          };
        case 0x2f:
          return {
            op: "vsqd",
            args: `${vreg(fs)}${destStr(dest)}, (--${vireg(ft)})`,
          };
        case 0x30:
          return { op: "vdiv", args: `Q, ${vreg(fs)}, ${vreg(ft)}` };
        case 0x31:
          return { op: "vsqrt", args: `Q, ${vreg(ft)}` };
        case 0x32:
          return { op: "vrsqrt", args: `Q, ${vreg(fs)}, ${vreg(ft)}` };
        case 0x33:
          return { op: "vwaitq", args: "" };
        case 0x34:
          return { op: "vmtir", args: `${vireg(ft)}, ${vreg(fs)}` };
        case 0x35:
          return {
            op: "vmfir",
            args: `${vreg(fd)}${destStr(dest)}, ${vireg(fs)}`,
          };
        case 0x36:
          return { op: "vilwr", args: `${vireg(ft)}, (${vireg(fs)})` };
        case 0x37:
          return { op: "viswr", args: `${vireg(ft)}, (${vireg(fs)})` };
        case 0x38:
          return { op: "vrnext", args: `${vreg(fd)}${destStr(dest)}, R` };
        case 0x39:
          return { op: "vrget", args: `${vreg(fd)}${destStr(dest)}, R` };
        case 0x3a:
          return { op: "vrinit", args: `R, ${vreg(fs)}` };
        case 0x3b:
          return { op: "vrxor", args: `R, ${vreg(fs)}` };
        case 0x3c:
          return { op: "vclip", args: `${vreg(fs)}, ${vreg(ft)}` };
        case 0x3d:
          return { op: "vmaddaq", args: `ACC${destStr(dest)}, ${vreg(fs)}, Q` };
        case 0x3e:
          return { op: "vmsubaq", args: `ACC${destStr(dest)}, ${vreg(fs)}, Q` };
        case 0x3f:
          return { op: "vmulaq", args: `ACC${destStr(dest)}, ${vreg(fs)}, Q` };
      }

      // VU0 special instructions (lower instructions with different encoding)
      const vuLower = (instr >> 25) & 0x7f;
      if (
        vuLower === 0x30 ||
        vuLower === 0x31 ||
        vuLower === 0x32 ||
        vuLower === 0x33
      ) {
        const it = (instr >> 16) & 0x1f;
        const is = (instr >> 11) & 0x1f;
        const id = (instr >> 6) & 0x1f;
        const vireg = (r: number) => `vi${r}`;

        switch (funct) {
          case 0x30:
            return {
              op: "viadd",
              args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
            };
          case 0x31:
            return {
              op: "visub",
              args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
            };
          case 0x32:
            return {
              op: "viaddi",
              args: `${vireg(it)}, ${vireg(is)}, ${
                (instr & 0x1f) - (instr & 0x10 ? 32 : 0)
              }`,
            };
          case 0x33:
            return {
              op: "viand",
              args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
            };
          case 0x34:
            return {
              op: "vior",
              args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
            };
        }
      }
    }
  }

  // VU1 instructions via VCALLMS/VCALLMSR (executed through COP2 interface)
  // VU1 uses same instruction encoding as VU0 but executes on VU1 unit
  // These are typically triggered via special COP2 instructions or DMA

  // If we reach here, instruction is unknown
  return { op: "unknown", args: "", label: "", comment: "" };
}