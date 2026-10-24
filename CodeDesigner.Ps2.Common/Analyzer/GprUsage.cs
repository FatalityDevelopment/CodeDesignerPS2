namespace CodeDesigner.Ps2.Common
{
    /// <summary>How an instruction changes control flow, for the static analyzer.</summary>
    public enum FlowKind
    {
        /// <summary>Falls through to the next instruction.</summary>
        None,
        /// <summary>Conditional branch with a delay slot (beq, bgez, bc1t, …).</summary>
        Branch,
        /// <summary>Conditional branch-likely; the delay slot runs only when taken.</summary>
        BranchLikely,
        /// <summary>Unconditional jump that never returns here (j, jr, b).</summary>
        Jump,
        /// <summary>Call that returns after the delay slot (jal, jalr, bal, bgezal).</summary>
        Call,
    }

    /// <summary>
    /// GPR reads/writes and control-flow class of one R5900 instruction word.
    /// Used by <see cref="MipsAnalyzer"/> to decide which simulated register
    /// values are actually known, so hints never print values derived from a
    /// register the analyzer never saw being set.
    /// </summary>
    public readonly struct GprUsage
    {
        /// <summary>Bit per GPR read by the instruction.</summary>
        public uint Reads { get; init; }
        /// <summary>Bit per GPR written by the instruction.</summary>
        public uint Writes { get; init; }
        /// <summary>The written register's value is known whenever the reads are (e.g. addiu, lui, jal's $ra).</summary>
        public bool DeterministicWrite { get; init; }
        /// <summary>The instruction reads memory into its written register.</summary>
        public bool IsLoad { get; init; }
        /// <summary>The instruction computes a value only from its reads (ALU op), so its hint is a value.</summary>
        public bool IsAlu { get; init; }
        public FlowKind Flow { get; init; }

        private static uint Bit(int reg) => reg == 0 ? 0u : 1u << reg;

        /// <summary>Classifies <paramref name="word"/>.</summary>
        public static GprUsage Of(uint word)
        {
            int op    = (int)(word >> 26);
            int rs    = (int)((word >> 21) & 0x1F);
            int rt    = (int)((word >> 16) & 0x1F);
            int rd    = (int)((word >> 11) & 0x1F);
            int funct = (int)(word & 0x3F);

            switch (op)
            {
                case 0x00: // SPECIAL
                    switch (funct)
                    {
                        case 0x00: case 0x02: case 0x03:              // sll/srl/sra
                        case 0x38: case 0x3A: case 0x3B:              // dsll/dsrl/dsra
                        case 0x3C: case 0x3E: case 0x3F:              // dsll32/dsrl32/dsra32
                            return Alu(Bit(rt), rd);
                        case 0x04: case 0x06: case 0x07:              // sllv/srlv/srav
                        case 0x14: case 0x16: case 0x17:              // dsllv/dsrlv/dsrav
                        case 0x20: case 0x21: case 0x22: case 0x23:   // add/addu/sub/subu
                        case 0x24: case 0x25: case 0x26: case 0x27:   // and/or/xor/nor
                        case 0x2A: case 0x2B:                         // slt/sltu
                        case 0x2C: case 0x2D: case 0x2E: case 0x2F:   // dadd/daddu/dsub/dsubu
                            return Alu(Bit(rs) | Bit(rt), rd);
                        case 0x08:                                    // jr
                            return new GprUsage { Reads = Bit(rs), Flow = FlowKind.Jump };
                        case 0x09:                                    // jalr
                            return new GprUsage { Reads = Bit(rs), Writes = Bit(rd), DeterministicWrite = true, Flow = FlowKind.Call };
                        case 0x0A: case 0x0B:                         // movz/movn
                            return Alu(Bit(rs) | Bit(rt) | Bit(rd), rd);
                        case 0x10: case 0x12: case 0x28:              // mfhi/mflo/mfsa
                            return new GprUsage { Writes = Bit(rd) };
                        case 0x11: case 0x13: case 0x29:              // mthi/mtlo/mtsa
                            return new GprUsage { Reads = Bit(rs) };
                        case 0x18: case 0x19:                         // mult/multu (rd = LO)
                            return new GprUsage { Reads = Bit(rs) | Bit(rt), Writes = Bit(rd), DeterministicWrite = true, IsAlu = true };
                        case 0x1A: case 0x1B:                         // div/divu
                        case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: case 0x36: // traps
                            return new GprUsage { Reads = Bit(rs) | Bit(rt) };
                        default:                                      // syscall/break/sync
                            return default;
                    }

                case 0x01: // REGIMM
                    switch (rt)
                    {
                        case 0x00: case 0x01:                         // bltz/bgez (bgez zero = b)
                            return new GprUsage { Reads = Bit(rs), Flow = rs == 0 && rt == 0x01 ? FlowKind.Jump : FlowKind.Branch };
                        case 0x02: case 0x03:                         // bltzl/bgezl
                            return new GprUsage { Reads = Bit(rs), Flow = FlowKind.BranchLikely };
                        case 0x10: case 0x11: case 0x12: case 0x13:   // bltzal/bgezal(l)
                            return new GprUsage
                            {
                                Reads = Bit(rs), Writes = Bit(31), DeterministicWrite = true,
                                Flow = rs == 0 && rt == 0x11 ? FlowKind.Call : FlowKind.Branch,
                            };
                        default:                                      // trap-immediates, mtsab/mtsah
                            return new GprUsage { Reads = Bit(rs) };
                    }

                case 0x02: return new GprUsage { Flow = FlowKind.Jump };                                                   // j
                case 0x03: return new GprUsage { Writes = Bit(31), DeterministicWrite = true, Flow = FlowKind.Call };     // jal

                case 0x04: case 0x05:                                 // beq/bne (beq zero,zero = b)
                    return new GprUsage
                    {
                        Reads = Bit(rs) | Bit(rt),
                        Flow = op == 0x04 && rs == 0 && rt == 0 ? FlowKind.Jump : FlowKind.Branch,
                    };
                case 0x06: case 0x07:                                 // blez/bgtz
                    return new GprUsage { Reads = Bit(rs), Flow = FlowKind.Branch };
                case 0x14: case 0x15:                                 // beql/bnel
                    return new GprUsage { Reads = Bit(rs) | Bit(rt), Flow = FlowKind.BranchLikely };
                case 0x16: case 0x17:                                 // blezl/bgtzl
                    return new GprUsage { Reads = Bit(rs), Flow = FlowKind.BranchLikely };

                case 0x08: case 0x09: case 0x0A: case 0x0B:           // addi/addiu/slti/sltiu
                case 0x0C: case 0x0D: case 0x0E:                      // andi/ori/xori
                case 0x18: case 0x19:                                 // daddi/daddiu
                    return Alu(Bit(rs), rt);
                case 0x0F:                                            // lui
                    return Alu(0, rt);

                case 0x10: case 0x11: case 0x12:                      // COP0/COP1/COP2
                    if (op == 0x11 && rs == 0x08 || op == 0x12 && rs == 0x08 || op == 0x10 && rs == 0x08)
                        return new GprUsage { Flow = (rt & 2) != 0 ? FlowKind.BranchLikely : FlowKind.Branch }; // bcNf/t(l)
                    if (rs <= 0x03)                                   // mfc/cfc/qmfc2
                        return new GprUsage { Writes = Bit(rt) };
                    if (rs >= 0x04 && rs <= 0x07)                     // mtc/ctc/qmtc2
                        return new GprUsage { Reads = Bit(rt) };
                    return default;

                case 0x1C: // MMI
                    switch (funct)
                    {
                        case 0x10: case 0x12:                         // mfhi1/mflo1
                            return new GprUsage { Writes = Bit(rd) };
                        case 0x11: case 0x13:                         // mthi1/mtlo1
                            return new GprUsage { Reads = Bit(rs) };
                        case 0x1A: case 0x1B:                         // div1/divu1
                            return new GprUsage { Reads = Bit(rs) | Bit(rt) };
                        case 0x09: case 0x29:                         // MMI2/MMI3 (pmfhi/pmflo read HI/LO)
                        {
                            int sa = (int)((word >> 6) & 0x1F);
                            if (funct == 0x09 && (sa == 0x08 || sa == 0x09))
                                return new GprUsage { Writes = Bit(rd) };
                            return Alu(Bit(rs) | Bit(rt), rd);
                        }
                        case 0x30:                                    // pmfhl
                            return new GprUsage { Writes = Bit(rd) };
                        case 0x00: case 0x01: case 0x20: case 0x21:   // madd/maddu/madd1/maddu1 (read HI/LO)
                            return new GprUsage { Reads = Bit(rs) | Bit(rt), Writes = Bit(rd) };
                        default:
                            return Alu(Bit(rs) | Bit(rt), rd);
                    }

                case 0x1A: case 0x1B:                                 // ldl/ldr (merge into rt)
                case 0x22: case 0x26:                                 // lwl/lwr
                    return new GprUsage { Reads = Bit(rs) | Bit(rt), Writes = Bit(rt), IsLoad = true };
                case 0x1E: case 0x20: case 0x21: case 0x23:           // lq/lb/lh/lw
                case 0x24: case 0x25: case 0x27: case 0x37:           // lbu/lhu/lwu/ld
                    return new GprUsage { Reads = Bit(rs), Writes = Bit(rt), IsLoad = true };

                case 0x1F: case 0x28: case 0x29: case 0x2A: case 0x2B: // sq/sb/sh/swl/sw
                case 0x2C: case 0x2D: case 0x2E: case 0x3F:           // sdl/sdr/swr/sd
                    return new GprUsage { Reads = Bit(rs) | Bit(rt) };
                case 0x2F: case 0x31: case 0x33: case 0x36:           // cache/lwc1/pref/lqc2
                case 0x39: case 0x3E:                                 // swc1/sqc2
                    return new GprUsage { Reads = Bit(rs) };

                default:
                    return default;
            }
        }

        private static GprUsage Alu(uint reads, int dest) => new()
        {
            Reads = reads, Writes = Bit(dest), DeterministicWrite = true, IsAlu = true,
        };
    }
}
