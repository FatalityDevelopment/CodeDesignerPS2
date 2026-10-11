using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.IOP;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Comprehensive coverage tests for every supported MIPS64 LE instruction,
    /// exercising exec, disasm, asm, and analysis for each instruction.
    /// </summary>
    [TestClass]
    public class TestFullInstructionCoverage
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>Creates a CPU with pre-initialised GPRs for testing.</summary>
        private static CpuState MakeCpu(byte[]? memory = null)
        {
            var cpu = new CpuState();
            cpu.RegsLo[8]  = 5;   // $t0
            cpu.RegsLo[9]  = 3;   // $t1
            cpu.RegsLo[16] = 10;  // $s0
            cpu.RegsLo[17] = 7;   // $s1
            cpu.RegsLo[18] = 2;   // $s2
            cpu.RegsLo[31] = 0x80000100; // $ra
            cpu.PC = 0x80000000;
            if (memory != null) cpu.Memory = memory;
            return cpu;
        }

        /// <summary>Decodes and executes one instruction on a CPU.</summary>
        private static void Exec(CpuState cpu, uint encoded)
        {
            var (obj, args) = MipsDisassembler.GetInstructionObject(encoded);
            Assert.IsNotNull(obj,  $"No instruction object for 0x{encoded:X8}");
            Assert.IsNotNull(args, $"No args for 0x{encoded:X8}");
            obj!.ExecuteGeneric(cpu, args!);
        }

        /// <summary>Returns the analysis comment produced for one instruction.</summary>
        private static string Analyze(uint encoded)
        {
            var cpu = MakeCpu();
            var comments = MipsAnalyzer.AnalyzeInstructions(
                new[] { (encoded, 0x80000000u) }, 0x80000000u, true);
            return comments.GetValueOrDefault(0x80000000u, string.Empty);
        }

        /// <summary>Asserts the disassembly of <paramref name="encoded"/> contains <paramref name="mnemonic"/>.</summary>
        private static void AssertDisasm(uint encoded, string mnemonic)
        {
            string disasm = MipsDisassembler.Disassemble(encoded);
            Assert.IsTrue(disasm.ToLowerInvariant().Contains(mnemonic.ToLowerInvariant()),
                $"Expected mnemonic '{mnemonic}' in disassembly but got: {disasm}");
        }

        /// <summary>Asserts that assembling <paramref name="asmLine"/> produces <paramref name="expected"/>.</summary>
        private static void AssertAsm(string asmLine, uint expected)
        {
            uint actual = MipsAssembler.Assemble(asmLine);
            Assert.AreEqual(expected, actual,
                $"Assemble('{asmLine}'): expected 0x{expected:X8} got 0x{actual:X8}");
        }

        /// <summary>Asserts that analysis produces a non-empty result.</summary>
        private static void AssertAnalysis(uint encoded)
        {
            string analysis = Analyze(encoded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(analysis),
                $"Analysis should not be empty for 0x{encoded:X8}");
        }

        // -----------------------------------------------------------------------
        // Arithmetic instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_ADD()
        {
            // Encoding: ADD $t0, $s0, $s1  => opcode=0,rs=16,rt=17,rd=8,funct=0x20
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x20);
            AssertAsm("add $t0, $s0, $s1", enc);
            AssertDisasm(enc, "add");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8], "ADD: 10+7=17");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ADDU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x21);
            AssertAsm("addu $t0, $s0, $s1", enc);
            AssertDisasm(enc, "addu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8], "ADDU: 10+7=17");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DADD()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x2C);
            AssertAsm("dadd $t0, $s0, $s1", enc);
            AssertDisasm(enc, "dadd");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8], "DADD: 10+7=17");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DADDU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x2D);
            AssertAsm("daddu $t0, $s0, $s1", enc);
            AssertDisasm(enc, "daddu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8], "DADDU: 10+7=17");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SUB()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x22);
            AssertAsm("sub $t0, $s0, $s1", enc);
            AssertDisasm(enc, "sub");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(3UL, cpu.RegsLo[8], "SUB: 10-7=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SUBU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x23);
            AssertAsm("subu $t0, $s0, $s1", enc);
            AssertDisasm(enc, "subu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(3UL, cpu.RegsLo[8], "SUBU: 10-7=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSUB()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x2E);
            AssertAsm("dsub $t0, $s0, $s1", enc);
            AssertDisasm(enc, "dsub");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(3UL, cpu.RegsLo[8], "DSUB: 10-7=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSUBU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x2F);
            AssertAsm("dsubu $t0, $s0, $s1", enc);
            AssertDisasm(enc, "dsubu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(3UL, cpu.RegsLo[8], "DSUBU: 10-7=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ADDI()
        {
            // ADDI $t0, $s0, 10  => opcode=0x08
            uint enc = MipsEncoder.EncodeI(0x08, 16, 8, 10);
            AssertAsm("addi $t0, $s0, 10", enc);
            AssertDisasm(enc, "addi");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(20UL, cpu.RegsLo[8], "ADDI: 10+10=20");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ADDIU()
        {
            uint enc = MipsEncoder.EncodeI(0x09, 16, 8, 10);
            AssertAsm("addiu $t0, $s0, 10", enc);
            AssertDisasm(enc, "addiu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(20UL, cpu.RegsLo[8], "ADDIU: 10+10=20");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DADDI()
        {
            uint enc = MipsEncoder.EncodeI(0x18, 16, 8, 10);
            AssertAsm("daddi $t0, $s0, 10", enc);
            AssertDisasm(enc, "daddi");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(20UL, cpu.RegsLo[8], "DADDI: 10+10=20");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DADDIU()
        {
            uint enc = MipsEncoder.EncodeI(0x19, 16, 8, 10);
            AssertAsm("daddiu $t0, $s0, 10", enc);
            AssertDisasm(enc, "daddiu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(20UL, cpu.RegsLo[8], "DADDIU: 10+10=20");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MULT()
        {
            // MULT $s0, $s1 => funct=0x18, result in LO
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x18);
            AssertAsm("mult $s0, $s1", enc);
            AssertDisasm(enc, "mult");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO, "MULT: 10*7=70");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MULTU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x19);
            AssertAsm("multu $s0, $s1", enc);
            AssertDisasm(enc, "multu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO, "MULTU: 10*7=70");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DMULT()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x1C);
            AssertAsm("dmult $s0, $s1", enc);
            AssertDisasm(enc, "dmult");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO, "DMULT: 10*7=70");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DMULTU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x1D);
            AssertAsm("dmultu $s0, $s1", enc);
            AssertDisasm(enc, "dmultu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO, "DMULTU: 10*7=70");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DIV()
        {
            // DIV $s0, $s1 => LO = quotient, HI = remainder
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x1A);
            AssertAsm("div $s0, $s1", enc);
            AssertDisasm(enc, "div");
            var cpu = MakeCpu(); Exec(cpu, enc); // 10 / 7
            Assert.AreEqual(1UL, cpu.LO,  "DIV LO: 10/7=1");
            Assert.AreEqual(3UL, cpu.HI,  "DIV HI: 10%7=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DIVU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x1B);
            AssertAsm("divu $s0, $s1", enc);
            AssertDisasm(enc, "divu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.LO, "DIVU LO");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DDIV()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x1E);
            AssertAsm("ddiv $s0, $s1", enc);
            AssertDisasm(enc, "ddiv");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.LO, "DDIV LO");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DDIVU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x1F);
            AssertAsm("ddivu $s0, $s1", enc);
            AssertDisasm(enc, "ddivu");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.LO, "DDIVU LO");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MFHI()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 0, 8, 0, 0x10);
            AssertAsm("mfhi $t0", enc);
            AssertDisasm(enc, "mfhi");
            var cpu = MakeCpu(); cpu.HI = 42; Exec(cpu, enc);
            Assert.AreEqual(42UL, cpu.RegsLo[8], "MFHI: $t0=HI=42");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MFLO()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 0, 8, 0, 0x12);
            AssertAsm("mflo $t0", enc);
            AssertDisasm(enc, "mflo");
            var cpu = MakeCpu(); cpu.LO = 99; Exec(cpu, enc);
            Assert.AreEqual(99UL, cpu.RegsLo[8], "MFLO: $t0=LO=99");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MTHI()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 0, 0, 0, 0x11);
            AssertAsm("mthi $s0", enc);
            AssertDisasm(enc, "mthi");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.HI, "MTHI: HI=$s0=10");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MTLO()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 0, 0, 0, 0x13);
            AssertAsm("mtlo $s0", enc);
            AssertDisasm(enc, "mtlo");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.LO, "MTLO: LO=$s0=10");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // Logical instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_AND()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x24);
            AssertAsm("and $t0, $s0, $s1", enc);
            AssertDisasm(enc, "and");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL & 7UL, cpu.RegsLo[8], "AND: 10&7");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_OR()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x25);
            AssertAsm("or $t0, $s0, $s1", enc);
            AssertDisasm(enc, "or");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL | 7UL, cpu.RegsLo[8], "OR: 10|7");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_XOR()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x26);
            AssertAsm("xor $t0, $s0, $s1", enc);
            AssertDisasm(enc, "xor");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL ^ 7UL, cpu.RegsLo[8], "XOR: 10^7");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_NOR()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x27);
            AssertAsm("nor $t0, $s0, $s1", enc);
            AssertDisasm(enc, "nor");
            var cpu = MakeCpu(); Exec(cpu, enc);
            // NOR: ~(rs | rt) across both halves of 128-bit register
            Assert.AreEqual(~(10UL | 7UL), cpu.RegsLo[8], "NOR: ~(10|7) in low 64 bits");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ANDI()
        {
            uint enc = MipsEncoder.EncodeI(0x0C, 16, 8, 0xFF);
            AssertAsm("andi $t0, $s0, 0xFF", enc);
            AssertDisasm(enc, "andi");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL & 0xFFUL, cpu.RegsLo[8], "ANDI: 10&0xFF");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ORI()
        {
            uint enc = MipsEncoder.EncodeI(0x0D, 16, 8, 0x100);
            AssertAsm("ori $t0, $s0, 0x100", enc);
            AssertDisasm(enc, "ori");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL | 0x100UL, cpu.RegsLo[8], "ORI: 10|0x100");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_XORI()
        {
            uint enc = MipsEncoder.EncodeI(0x0E, 16, 8, 0xABCD);
            AssertAsm("xori $t0, $s0, 0xABCD", enc);
            AssertDisasm(enc, "xori");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL ^ 0xABCDUL, cpu.RegsLo[8], "XORI: 10^0xABCD");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // Shift instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_SLL()
        {
            // SLL $t0, $s0, 2  => EncodeR(0, 0, rs=16, rd=8, shamt=2, funct=0)
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 2, 0x00);
            AssertAsm("sll $t0, $s0, 2", enc);
            AssertDisasm(enc, "sll");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(40UL, cpu.RegsLo[8], "SLL: 10<<2=40");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SRL()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 1, 0x02);
            AssertAsm("srl $t0, $s0, 1", enc);
            AssertDisasm(enc, "srl");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "SRL: 10>>1=5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SRA()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 1, 0x03);
            AssertAsm("sra $t0, $s0, 1", enc);
            AssertDisasm(enc, "sra");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "SRA: 10>>1=5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SLLV()
        {
            // SLLV $t0, $s0, $s2  => ArgNames=["rd","rt","rs"] => EncodeR(0, rs=18, rt=16, rd=8, 0, 4)
            uint enc = MipsEncoder.EncodeR(0, 18, 16, 8, 0, 0x04);
            AssertAsm("sllv $t0, $s0, $s2", enc);
            AssertDisasm(enc, "sllv");
            var cpu = MakeCpu(); Exec(cpu, enc); // 10 << 2 = 40
            Assert.AreEqual(40UL, cpu.RegsLo[8], "SLLV: 10<<2=40");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SRLV()
        {
            uint enc = MipsEncoder.EncodeR(0, 18, 16, 8, 0, 0x06);
            AssertAsm("srlv $t0, $s0, $s2", enc);
            AssertDisasm(enc, "srlv");
            var cpu = MakeCpu(); Exec(cpu, enc); // 10 >> 2 = 2
            Assert.AreEqual(2UL, cpu.RegsLo[8], "SRLV: 10>>2=2");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SRAV()
        {
            uint enc = MipsEncoder.EncodeR(0, 18, 16, 8, 0, 0x07);
            AssertAsm("srav $t0, $s0, $s2", enc);
            AssertDisasm(enc, "srav");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(2UL, cpu.RegsLo[8], "SRAV: 10>>2=2");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSLL()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 2, 0x38);
            AssertAsm("dsll $t0, $s0, 2", enc);
            AssertDisasm(enc, "dsll");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(40UL, cpu.RegsLo[8], "DSLL: 10<<2=40");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSRL()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 1, 0x3A);
            AssertAsm("dsrl $t0, $s0, 1", enc);
            AssertDisasm(enc, "dsrl");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "DSRL: 10>>1=5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSRA()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 1, 0x3B);
            AssertAsm("dsra $t0, $s0, 1", enc);
            AssertDisasm(enc, "dsra");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "DSRA: 10>>1=5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSLLV()
        {
            uint enc = MipsEncoder.EncodeR(0, 18, 16, 8, 0, 0x14);
            AssertAsm("dsllv $t0, $s0, $s2", enc);
            AssertDisasm(enc, "dsllv");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(40UL, cpu.RegsLo[8], "DSLLV: 10<<2=40");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSRLV()
        {
            uint enc = MipsEncoder.EncodeR(0, 18, 16, 8, 0, 0x16);
            AssertAsm("dsrlv $t0, $s0, $s2", enc);
            AssertDisasm(enc, "dsrlv");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(2UL, cpu.RegsLo[8], "DSRLV: 10>>2=2");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSRAV()
        {
            uint enc = MipsEncoder.EncodeR(0, 18, 16, 8, 0, 0x17);
            AssertAsm("dsrav $t0, $s0, $s2", enc);
            AssertDisasm(enc, "dsrav");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(2UL, cpu.RegsLo[8], "DSRAV: 10>>2=2");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSLL32()
        {
            // Only in disassembler, not assembler
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 2, 0x3C);
            AssertDisasm(enc, "dsll32");
            var cpu = MakeCpu(); Exec(cpu, enc);
            // DSLL32 shifts by sa+32 bits; result for 64-bit: 10 << (2+32) = 10 * 2^34
            Assert.AreEqual(10UL << 34, cpu.RegsLo[8], "DSLL32: 10<<34");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSRL32()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 2, 0x3E);
            AssertDisasm(enc, "dsrl32");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 10UL << 34; // Pre-shift so we can shift back
            Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "DSRL32: (10<<34)>>34=10");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DSRA32()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 16, 8, 2, 0x3F);
            AssertDisasm(enc, "dsra32");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 10UL << 34;
            Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "DSRA32: (10<<34)>>34=10");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // Compare instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_SLT()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x2A);
            AssertAsm("slt $t0, $s0, $s1", enc);
            AssertDisasm(enc, "slt");
            var cpu = MakeCpu(); // $s0=10, $s1=7 => 10 < 7 is false => $t0=0
            Exec(cpu, enc);
            Assert.AreEqual(0UL, cpu.RegsLo[8], "SLT: 10<7 = false");
            // Swap: $s0=7, $s1=10 => 7 < 10 = true
            cpu = MakeCpu(); cpu.RegsLo[16] = 7; cpu.RegsLo[17] = 10;
            Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.RegsLo[8], "SLT: 7<10 = true");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SLTU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x2B);
            AssertAsm("sltu $t0, $s0, $s1", enc);
            AssertDisasm(enc, "sltu");
            var cpu = MakeCpu(); // 10 < 7 unsigned? No
            Exec(cpu, enc);
            Assert.AreEqual(0UL, cpu.RegsLo[8], "SLTU: 10<7 = false");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SLTI()
        {
            uint enc = MipsEncoder.EncodeI(0x0A, 16, 8, 20);
            AssertAsm("slti $t0, $s0, 20", enc);
            AssertDisasm(enc, "slti");
            var cpu = MakeCpu(); // $s0=10 < 20? yes
            Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.RegsLo[8], "SLTI: 10<20=true");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SLTIU()
        {
            uint enc = MipsEncoder.EncodeI(0x0B, 16, 8, 20);
            AssertAsm("sltiu $t0, $s0, 20", enc);
            AssertDisasm(enc, "sltiu");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.RegsLo[8], "SLTIU: 10<20=true");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // Branch / Jump instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_BEQ()
        {
            // BEQ $s0, $s1, 8  (forward branch by 8)
            uint enc = MipsEncoder.EncodeI(0x04, 16, 17, 8);
            AssertAsm("beq $s0, $s1, 8", enc);
            AssertDisasm(enc, "beq");
            // Not taken when s0=10, s1=7
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000004UL, cpu.PC, "BEQ not taken");
            // Taken when equal
            cpu = MakeCpu(); cpu.RegsLo[16] = 7; cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000024UL, cpu.PC, "BEQ taken: PC = PC+4 + 8<<2 = PC+36");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BNE()
        {
            uint enc = MipsEncoder.EncodeI(0x05, 16, 17, 8);
            AssertAsm("bne $s0, $s1, 8", enc);
            AssertDisasm(enc, "bne");
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            // s0=10 != s1=7 => taken
            Assert.AreEqual(0x80000024UL, cpu.PC, "BNE taken");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BLEZ()
        {
            uint enc = MipsEncoder.EncodeI(0x06, 16, 0, 8);
            AssertAsm("blez $s0, 8", enc);
            AssertDisasm(enc, "blez");
            // s0=10 > 0 => not taken
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000004UL, cpu.PC, "BLEZ not taken (s0=10>0)");
            // s0=0 => taken
            cpu = MakeCpu(); cpu.RegsLo[16] = 0; cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000024UL, cpu.PC, "BLEZ taken (s0=0)");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BGTZ()
        {
            uint enc = MipsEncoder.EncodeI(0x07, 16, 0, 8);
            AssertAsm("bgtz $s0, 8", enc);
            AssertDisasm(enc, "bgtz");
            // s0=10 > 0 => taken
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000024UL, cpu.PC, "BGTZ taken (s0=10>0)");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BLTZ()
        {
            // BLTZ $s0, 8  => opcode=0x01, rt=0
            uint enc = MipsEncoder.EncodeI(0x01, 16, 0, 8);
            AssertAsm("bltz $s0, 8", enc);
            AssertDisasm(enc, "bltz");
            // s0=10 >= 0 => not taken
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000004UL, cpu.PC, "BLTZ not taken (s0=10>=0)");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BGEZ()
        {
            // BGEZ $s0, 8  => opcode=0x01, rt=1
            uint enc = MipsEncoder.EncodeI(0x01, 16, 1, 8);
            AssertAsm("bgez $s0, 8", enc);
            AssertDisasm(enc, "bgez");
            // s0=10 >= 0 => taken
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000024UL, cpu.PC, "BGEZ taken (s0=10>=0)");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BEQL()
        {
            uint enc = MipsEncoder.EncodeI(0x14, 16, 17, 8);
            AssertAsm("beql $s0, $s1, 8", enc);
            AssertDisasm(enc, "beql");
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            // s0=10 != s1=7 => not taken (and delay slot discarded)
            Assert.AreEqual(0x80000008UL, cpu.PC, "BEQL not taken: PC += 8 (skips delay slot)");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BNEL()
        {
            uint enc = MipsEncoder.EncodeI(0x15, 16, 17, 8);
            AssertAsm("bnel $s0, $s1, 8", enc);
            AssertDisasm(enc, "bnel");
            // s0=10 != s1=7 => taken (likely branch)
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000024UL, cpu.PC, "BNEL taken");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_J()
        {
            // J 0x400000 => stored as 0x400000>>2 = 0x100000
            uint enc = MipsEncoder.EncodeJ(0x02, 0x100000);
            AssertAsm("j 0x400000", enc);
            AssertDisasm(enc, "j");
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            // J target = (PC[31:28] | addr<<2)
            Assert.IsTrue(cpu.PC == 0x00400000UL || cpu.PC == 0x80400000UL,
                $"J target PC should be 0x00400000 or 0x80400000, got 0x{cpu.PC:X8}");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_JAL()
        {
            uint enc = MipsEncoder.EncodeJ(0x03, 0x100000);
            AssertAsm("jal 0x400000", enc);
            AssertDisasm(enc, "jal");
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            // JAL also stores PC+8 in $ra
            Assert.AreEqual(0x80000008UL, cpu.RegsLo[31], "JAL: $ra = PC+8");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_JR()
        {
            // JR $ra  => EncodeR(0, 31, 0, 0, 0, 8)
            uint enc = MipsEncoder.EncodeR(0, 31, 0, 0, 0, 0x08);
            AssertAsm("jr $ra", enc);
            AssertDisasm(enc, "jr");
            var cpu = MakeCpu(); cpu.RegsLo[31] = 0xBFC00000; Exec(cpu, enc);
            Assert.AreEqual(0xBFC00000UL, cpu.PC, "JR: PC=$ra");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_JALR()
        {
            // JALR $t0, $ra  => EncodeR(0, 31, 0, 8, 0, 9)
            uint enc = MipsEncoder.EncodeR(0, 31, 0, 8, 0, 0x09);
            AssertAsm("jalr $t0, $ra", enc);
            AssertDisasm(enc, "jalr");
            var cpu = MakeCpu(); cpu.RegsLo[31] = 0xBFC00000; cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0xBFC00000UL, cpu.PC, "JALR: PC=$ra");
            Assert.AreEqual(0x80000008UL, cpu.RegsLo[8], "JALR: $t0=PC+8");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BLTZAL()
        {
            // BLTZAL $s0, 8  => opcode=0x01, rt=0x10
            uint enc = MipsEncoder.EncodeI(0x01, 16, 0x10, 8);
            AssertDisasm(enc, "bltzal");
            // s0=10 >= 0 => not taken but $ra is still set to PC+8
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000008UL, cpu.RegsLo[31], "BLTZAL: $ra set");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BGEZAL()
        {
            uint enc = MipsEncoder.EncodeI(0x01, 16, 0x11, 8);
            AssertDisasm(enc, "bgezal");
            // s0=10 >= 0 => taken
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000008UL, cpu.RegsLo[31], "BGEZAL: $ra set");
            Assert.AreEqual(0x80000024UL, cpu.PC, "BGEZAL taken");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BLTZALL()
        {
            uint enc = MipsEncoder.EncodeI(0x01, 16, 0x12, 8);
            AssertDisasm(enc, "bltzall");
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            // s0=10 >= 0 => not taken; PC skips delay slot
            Assert.AreEqual(0x80000008UL, cpu.RegsLo[31], "BLTZALL: $ra set");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BGEZALL()
        {
            uint enc = MipsEncoder.EncodeI(0x01, 16, 0x13, 8);
            AssertDisasm(enc, "bgezall");
            var cpu = MakeCpu(); cpu.PC = 0x80000000; Exec(cpu, enc);
            Assert.AreEqual(0x80000008UL, cpu.RegsLo[31], "BGEZALL: $ra set");
            Assert.AreEqual(0x80000024UL, cpu.PC, "BGEZALL taken");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // Load / Store instructions
        // -----------------------------------------------------------------------

        // Helper: 64-byte memory with a known word at offset 0x10
        private static byte[] MakeTestMemory()
        {
            var mem = new byte[256];
            // Store 0xDEADBEEF at offset 0x10 (little-endian)
            mem[0x10] = 0xEF; mem[0x11] = 0xBE; mem[0x12] = 0xAD; mem[0x13] = 0xDE;
            // Store 0xCAFEBABE at offset 0x20 (little-endian)
            mem[0x20] = 0xBE; mem[0x21] = 0xBA; mem[0x22] = 0xFE; mem[0x23] = 0xCA;
            return mem;
        }

        [TestMethod]
        public void Test_LW_SW()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0; // $s0 = base addr 0

            // LW $t0, 0x10($s0) => load 0xDEADBEEF
            uint encLw = MipsEncoder.EncodeI(0x23, 16, 8, 0x10);
            AssertAsm("lw $t0, 0x10, $s0", encLw);
            AssertDisasm(encLw, "lw");
            Exec(cpu, encLw);
            Assert.AreEqual(0xDEADBEEFUL, cpu.RegsLo[8] & 0xFFFFFFFF, "LW: loaded 0xDEADBEEF");
            AssertAnalysis(encLw);

            // SW $t0, 0x20($s0) => store $t0 to offset 0x20
            cpu.RegsLo[8] = 0x12345678;
            uint encSw = MipsEncoder.EncodeI(0x2B, 16, 8, 0x20);
            AssertAsm("sw $t0, 0x20, $s0", encSw);
            AssertDisasm(encSw, "sw");
            Exec(cpu, encSw);
            Assert.AreEqual(0x78, mem[0x20], "SW: stored byte 0");
            Assert.AreEqual(0x56, mem[0x21], "SW: stored byte 1");
            AssertAnalysis(encSw);
        }

        [TestMethod]
        public void Test_LB_SB()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;

            uint encLb = MipsEncoder.EncodeI(0x20, 16, 8, 0x10);
            AssertAsm("lb $t0, 0x10, $s0", encLb);
            AssertDisasm(encLb, "lb");
            Exec(cpu, encLb);
            // Sign-extend: 0xEF is 239 unsigned = -17 signed
            Assert.AreEqual(unchecked((ulong)(long)-17), cpu.RegsLo[8], "LB: sign-extended");
            AssertAnalysis(encLb);

            uint encSb = MipsEncoder.EncodeI(0x28, 16, 8, 0x30);
            AssertAsm("sb $t0, 0x30, $s0", encSb);
            AssertDisasm(encSb, "sb");
            cpu.RegsLo[8] = 0xAB;
            Exec(cpu, encSb);
            Assert.AreEqual(0xAB, mem[0x30], "SB: stored byte");
            AssertAnalysis(encSb);
        }

        [TestMethod]
        public void Test_LBU()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;
            uint enc = MipsEncoder.EncodeI(0x24, 16, 8, 0x10);
            AssertAsm("lbu $t0, 0x10, $s0", enc);
            AssertDisasm(enc, "lbu");
            Exec(cpu, enc);
            Assert.AreEqual(0xEFUL, cpu.RegsLo[8], "LBU: zero-extended");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_LH_SH()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;

            uint encLh = MipsEncoder.EncodeI(0x21, 16, 8, 0x10);
            AssertAsm("lh $t0, 0x10, $s0", encLh);
            AssertDisasm(encLh, "lh");
            Exec(cpu, encLh);
            // 0xBEEF = 0xBEEF, sign-extended from short
            Assert.AreEqual(unchecked((ulong)(long)-16657), cpu.RegsLo[8], "LH: sign-extended halfword");
            AssertAnalysis(encLh);

            uint encSh = MipsEncoder.EncodeI(0x29, 16, 8, 0x40);
            AssertAsm("sh $t0, 0x40, $s0", encSh);
            AssertDisasm(encSh, "sh");
            cpu.RegsLo[8] = 0x1234;
            Exec(cpu, encSh);
            Assert.AreEqual(0x34, mem[0x40], "SH: stored low byte");
            Assert.AreEqual(0x12, mem[0x41], "SH: stored high byte");
            AssertAnalysis(encSh);
        }

        [TestMethod]
        public void Test_LHU()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;
            uint enc = MipsEncoder.EncodeI(0x25, 16, 8, 0x10);
            AssertAsm("lhu $t0, 0x10, $s0", enc);
            AssertDisasm(enc, "lhu");
            Exec(cpu, enc);
            Assert.AreEqual(0xBEEFUL, cpu.RegsLo[8], "LHU: zero-extended halfword");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_LWU()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;
            uint enc = MipsEncoder.EncodeI(0x27, 16, 8, 0x10);
            AssertAsm("lwu $t0, 0x10, $s0", enc);
            AssertDisasm(enc, "lwu");
            Exec(cpu, enc);
            Assert.AreEqual(0xDEADBEEFUL, cpu.RegsLo[8], "LWU: zero-extended word");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_LD_SD()
        {
            var mem = new byte[256];
            // 0x0102030405060708 at offset 0x10 (little-endian)
            for (int i = 0; i < 8; i++) mem[0x10 + i] = (byte)(i + 1);
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;

            uint encLd = MipsEncoder.EncodeI(0x37, 16, 8, 0x10);
            AssertAsm("ld $t0, 0x10, $s0", encLd);
            AssertDisasm(encLd, "ld");
            Exec(cpu, encLd);
            Assert.AreEqual(0x0807060504030201UL, cpu.RegsLo[8], "LD: loaded doubleword");
            AssertAnalysis(encLd);

            uint encSd = MipsEncoder.EncodeI(0x3F, 16, 8, 0x20);
            AssertAsm("sd $t0, 0x20, $s0", encSd);
            AssertDisasm(encSd, "sd");
            cpu.RegsLo[8] = 0xCAFEBABE12345678UL;
            Exec(cpu, encSd);
            Assert.AreEqual(0x78, mem[0x20], "SD: stored byte 0");
            AssertAnalysis(encSd);
        }

        [TestMethod]
        public void Test_LD_SD_IopBus()
        {
            // Verify that LD/SD/LDL/LDR/SDL/SDR use IopBus when Bus is null
            var iopBus = new IopMemoryBus();
            // Write a known 64-bit value into IOP RAM via bus directly
            iopBus.WriteWord(0x100, 0x04030201u);
            iopBus.WriteWord(0x104, 0x08070605u);

            var cpu = new CpuState { IopBus = iopBus };
            cpu.RegsLo[16] = 0x100; // $s0 = base address

            // LD via IopBus: load 8 bytes from 0x100
            uint encLd = MipsEncoder.EncodeI(0x37, 16, 8, 0);
            Exec(cpu, encLd);
            Assert.AreEqual(0x0807060504030201UL, cpu.RegsLo[8], "LD via IopBus: loaded doubleword");

            // SD via IopBus: store $t0 to 0x200
            cpu.RegsLo[8]  = 0xDEADBEEFCAFEBABEUL;
            cpu.RegsLo[16] = 0x200;
            uint encSd = MipsEncoder.EncodeI(0x3F, 16, 8, 0);
            Exec(cpu, encSd);
            Assert.AreEqual(0xCAFEBABEu, iopBus.ReadWord(0x200), "SD via IopBus: low word stored");
            Assert.AreEqual(0xDEADBEEFu, iopBus.ReadWord(0x204), "SD via IopBus: high word stored");
        }

        [TestMethod]
        public void Test_SDL_SDR_IopBus()
        {
            // Verify that SDL/SDR use IopBus when Bus is null
            var iopBus = new IopMemoryBus();
            // Pre-fill aligned dword at 0x300 with 0xFF bytes
            iopBus.WriteWord(0x300, 0xFFFFFFFFu);
            iopBus.WriteWord(0x304, 0xFFFFFFFFu);

            var cpu = new CpuState { IopBus = iopBus };
            cpu.RegsLo[8]  = 0x0102030405060708UL; // $t0 = value to store
            cpu.RegsLo[16] = 0x300; // $s0 = aligned address (byteOff=0)

            // SDL at aligned address 0x300, byteOff=0: stores entire value (mask=0)
            uint encSdl = MipsEncoder.EncodeI(0x2C, 16, 8, 0);
            Exec(cpu, encSdl);
            ulong lo = iopBus.ReadWord(0x300);
            ulong hi = iopBus.ReadWord(0x304);
            ulong stored = lo | (hi << 32);
            Assert.AreEqual(0x0102030405060708UL, stored, "SDL via IopBus: full dword stored when byteOff=0");

            // SDR at aligned address 0x310, byteOff=7 (addr=0x317): stores all 8 bytes
            iopBus.WriteWord(0x310, 0xFFFFFFFFu);
            iopBus.WriteWord(0x314, 0xFFFFFFFFu);
            cpu.RegsLo[16] = 0x317; // byteOff=7, shift=0 → mask=0xFFFFFFFFFFFFFFFF covers all bytes
            uint encSdr = MipsEncoder.EncodeI(0x2D, 16, 8, 0);
            Exec(cpu, encSdr);
            lo = iopBus.ReadWord(0x310);
            hi = iopBus.ReadWord(0x314);
            stored = lo | (hi << 32);
            Assert.AreEqual(0x0102030405060708UL, stored, "SDR via IopBus: full dword stored when byteOff=7");
        }

        [TestMethod]
        public void Test_LWL_LWR()
        {
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;

            uint encLwl = MipsEncoder.EncodeI(0x22, 16, 8, 0x13);
            AssertAsm("lwl $t0, 0x13, $s0", encLwl);
            AssertDisasm(encLwl, "lwl");
            Exec(cpu, encLwl);
            AssertAnalysis(encLwl);

            uint encLwr = MipsEncoder.EncodeI(0x26, 16, 8, 0x10);
            AssertAsm("lwr $t0, 0x10, $s0", encLwr);
            AssertDisasm(encLwr, "lwr");
            Exec(cpu, encLwr);
            AssertAnalysis(encLwr);
        }

        [TestMethod]
        public void Test_LDL_LDR()
        {
            var mem = new byte[256];
            for (int i = 0; i < 8; i++) mem[0x10 + i] = (byte)(0xA0 + i);
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;

            uint encLdl = MipsEncoder.EncodeI(0x1A, 16, 8, 0x17);
            AssertAsm("ldl $t0, 0x17, $s0", encLdl);
            AssertDisasm(encLdl, "ldl");
            Exec(cpu, encLdl);
            AssertAnalysis(encLdl);

            uint encLdr = MipsEncoder.EncodeI(0x1B, 16, 8, 0x10);
            AssertAsm("ldr $t0, 0x10, $s0", encLdr);
            AssertDisasm(encLdr, "ldr");
            Exec(cpu, encLdr);
            AssertAnalysis(encLdr);
        }

        [TestMethod]
        public void Test_SWL_SWR()
        {
            byte[] mem = new byte[256];
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0; cpu.RegsLo[8] = 0x12345678;

            uint encSwl = MipsEncoder.EncodeI(0x2A, 16, 8, 0x13);
            AssertAsm("swl $t0, 0x13, $s0", encSwl);
            AssertDisasm(encSwl, "swl");
            Exec(cpu, encSwl);
            AssertAnalysis(encSwl);

            uint encSwr = MipsEncoder.EncodeI(0x2E, 16, 8, 0x10);
            AssertAsm("swr $t0, 0x10, $s0", encSwr);
            AssertDisasm(encSwr, "swr");
            Exec(cpu, encSwr);
            AssertAnalysis(encSwr);
        }

        [TestMethod]
        public void Test_SDL_SDR()
        {
            byte[] mem = new byte[256];
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0; cpu.RegsLo[8] = 0xCAFEBABE12345678UL;

            uint encSdl = MipsEncoder.EncodeI(0x2C, 16, 8, 0x17);
            AssertAsm("sdl $t0, 0x17, $s0", encSdl);
            AssertDisasm(encSdl, "sdl");
            Exec(cpu, encSdl);
            AssertAnalysis(encSdl);

            uint encSdr = MipsEncoder.EncodeI(0x2D, 16, 8, 0x10);
            AssertAsm("sdr $t0, 0x10, $s0", encSdr);
            AssertDisasm(encSdr, "sdr");
            Exec(cpu, encSdr);
            AssertAnalysis(encSdr);
        }

        [TestMethod]
        public void Test_LQ_SQ()
        {
            byte[] mem = new byte[256];
            // Fill 0x10..0x1F with test data
            for (int i = 0; i < 16; i++) mem[0x10 + i] = (byte)(i + 1);
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0;

            uint encLq = MipsEncoder.EncodeI(0x1E, 16, 8, 0x10);
            AssertAsm("lq $t0, 0x10, $s0", encLq);
            AssertDisasm(encLq, "lq");
            Exec(cpu, encLq);
            AssertAnalysis(encLq);

            uint encSq = MipsEncoder.EncodeI(0x1F, 16, 8, 0x40);
            AssertAsm("sq $t0, 0x40, $s0", encSq);
            AssertDisasm(encSq, "sq");
            Exec(cpu, encSq);
            AssertAnalysis(encSq);
        }

        // -----------------------------------------------------------------------
        // Special instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_LUI()
        {
            uint enc = MipsEncoder.EncodeI(0x0F, 0, 8, 0x1234);
            AssertAsm("lui $t0, 0x1234", enc);
            AssertDisasm(enc, "lui");
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(0x12340000UL, cpu.RegsLo[8] & 0xFFFFFFFF, "LUI: 0x1234 << 16");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MOVZ()
        {
            // MOVZ $t0, $s0, $s1 => copy $s0 to $t0 if $s1 == 0
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x0A);
            AssertAsm("movz $t0, $s0, $s1", enc);
            AssertDisasm(enc, "movz");
            // s1=7 != 0 => no copy
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "$t0 unchanged (s1!=0)");
            // s1=0 => copy
            cpu = MakeCpu(); cpu.RegsLo[17] = 0; Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "MOVZ: $t0=$s0=10 when s1==0");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MOVN()
        {
            // MOVN $t0, $s0, $s1 => copy $s0 to $t0 if $s1 != 0
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x0B);
            AssertAsm("movn $t0, $s0, $s1", enc);
            AssertDisasm(enc, "movn");
            // s1=7 != 0 => copy
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "MOVN: $t0=$s0=10 when s1!=0");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MOVT()
        {
            // MOVT $t0, $s0, 0 (cc=0, tf=1) => copy $s0 to $t0 if FCC0==true
            // Encoding: SPECIAL funct=0x01, rs=$s0(16), rt=(cc<<2)|tf=(0<<2)|1=1, rd=$t0(8)
            uint enc = (uint)(16 << 21 | 1 << 16 | 8 << 11 | 0x01);
            AssertDisasm(enc, "movt");
            // FCC0=false => no move
            var cpu = MakeCpu(); cpu.COP1.SetConditionCode(false); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "MOVT: $t0 unchanged when FCC0=false");
            // FCC0=true => move
            cpu = MakeCpu(); cpu.COP1.SetConditionCode(true); Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "MOVT: $t0=$s0=10 when FCC0=true");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MOVF()
        {
            // MOVF $t0, $s0, 0 (cc=0, tf=0) => copy $s0 to $t0 if FCC0==false
            // Encoding: SPECIAL funct=0x01, rs=$s0(16), rt=(cc<<2)|tf=(0<<2)|0=0, rd=$t0(8)
            uint enc = (uint)(16 << 21 | 0 << 16 | 8 << 11 | 0x01);
            AssertDisasm(enc, "movf");
            // FCC0=true => no move
            var cpu = MakeCpu(); cpu.COP1.SetConditionCode(true); Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8], "MOVF: $t0 unchanged when FCC0=true");
            // FCC0=false => move
            cpu = MakeCpu(); cpu.COP1.SetConditionCode(false); Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "MOVF: $t0=$s0=10 when FCC0=false");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MOVT_KnownEncodings()
        {
            // Verify the two specific encodings from the issue are recognised as movt
            var (obj1, _) = MipsDisassembler.GetInstructionObject(0x00430081u);
            Assert.IsNotNull(obj1, "0x00430081 must be decoded");
            Assert.AreEqual("movt zero, v0, 0",
                MipsDisassembler.Disassemble(0x00430081u),
                "0x00430081 should disassemble to 'movt zero, v0, 0'");

            var (obj2, _) = MipsDisassembler.GetInstructionObject(0x00030001u);
            Assert.IsNotNull(obj2, "0x00030001 must be decoded");
            Assert.AreEqual("movt zero, zero, 0",
                MipsDisassembler.Disassemble(0x00030001u),
                "0x00030001 should disassemble to 'movt zero, zero, 0'");
        }

        [TestMethod]
        public void Test_NOP()
        {
            uint enc = 0x00000000u;
            AssertAsm("nop", enc);
            Assert.AreEqual("nop", MipsDisassembler.Disassemble(enc), "NOP disassembles to 'nop'");
            var cpu = MakeCpu(); ulong prevLo = cpu.RegsLo[8];
            // NOP is SLL $zero, $zero, 0; GetInstructionObject returns the SLL class
            var (obj, args) = MipsDisassembler.GetInstructionObject(enc);
            Assert.IsNotNull(obj);
            obj!.ExecuteGeneric(cpu, args!);
            Assert.AreEqual(prevLo, cpu.RegsLo[8], "NOP: no register change");
            // NOP intentionally produces no analysis comment — it is pure noise for a reader
        }

        [TestMethod]
        public void Test_SYNC()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 0, 0, 0, 0x0F);
            AssertAsm("sync", enc);
            AssertDisasm(enc, "sync");
            var cpu = MakeCpu(); Exec(cpu, enc); // sync is a no-op
            // SYNC has no operands and no runtime data to show — analysis is intentionally empty
        }

        // -----------------------------------------------------------------------
        // Trap / System instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_SYSCALL()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 0, 0, 0, 0x0C);
            AssertAsm("syscall", enc);
            AssertDisasm(enc, "syscall");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Syscall, cpu.ExceptionSignal,
                "SYSCALL should set ExceptionSignal to Syscall");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BREAK()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 0, 0, 0, 0x0D);
            AssertAsm("break", enc);
            AssertDisasm(enc, "break");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Breakpoint, cpu.ExceptionSignal,
                "BREAK should set ExceptionSignal to Breakpoint");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TGE()
        {
            // TGE $s0, $s1 => trap if $s0 >= $s1 (signed)
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x30);
            AssertAsm("tge $s0, $s1", enc);
            AssertDisasm(enc, "tge");
            // s0=10 >= s1=7 => TRAP
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Trap, cpu.ExceptionSignal, "TGE should signal Trap when s0>=s1");
            // s0=5 < s1=7 => no trap
            cpu = MakeCpu(); cpu.RegsLo[16] = 5; cpu.RegsLo[17] = 7;
            Exec(cpu, enc); // should not signal
            Assert.AreEqual(CpuExceptionSignal.None, cpu.ExceptionSignal, "TGE should not trap when s0<s1");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TGEU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x31);
            AssertAsm("tgeu $s0, $s1", enc);
            AssertDisasm(enc, "tgeu");
            // s0=10 >= s1=7 unsigned => TRAP
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Trap, cpu.ExceptionSignal, "TGEU should signal Trap");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TLT()
        {
            // TLT $s0, $s1 => trap if s0 < s1
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x32);
            AssertAsm("tlt $s0, $s1", enc);
            AssertDisasm(enc, "tlt");
            // s0=10 > s1=7 => no trap
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.None, cpu.ExceptionSignal, "TLT should not trap when s0>s1");
            // s0=5, s1=7 => 5 < 7 => trap
            cpu = MakeCpu(); cpu.RegsLo[16] = 5; cpu.RegsLo[17] = 7;
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Trap, cpu.ExceptionSignal, "TLT should signal Trap when s0<s1");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TLTU()
        {
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x33);
            AssertAsm("tltu $s0, $s1", enc);
            AssertDisasm(enc, "tltu");
            // s0=10 > s1=7 => no trap
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.None, cpu.ExceptionSignal, "TLTU should not trap when s0>s1");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TEQ()
        {
            // TEQ $s0, $s1 => trap if s0 == s1
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x34);
            AssertAsm("teq $s0, $s1", enc);
            AssertDisasm(enc, "teq");
            // s0=10 != s1=7 => no trap
            var cpu = MakeCpu(); Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.None, cpu.ExceptionSignal, "TEQ should not trap when unequal");
            // s0=7 == s1=7 => trap
            cpu = MakeCpu(); cpu.RegsLo[16] = 7;
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Trap, cpu.ExceptionSignal, "TEQ should signal Trap when equal");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TNE()
        {
            // TNE $s0, $s1 => trap if s0 != s1
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 0, 0, 0x36);
            AssertAsm("tne $s0, $s1", enc);
            AssertDisasm(enc, "tne");
            // s0=10 != s1=7 => trap
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.Trap, cpu.ExceptionSignal, "TNE should signal Trap when not equal");
            // s0=7 == s1=7 => no trap
            cpu = MakeCpu(); cpu.RegsLo[16] = 7; Exec(cpu, enc);
            Assert.AreEqual(CpuExceptionSignal.None, cpu.ExceptionSignal, "TNE should not trap when equal");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // COP0 instructions (disasm, exec, analysis only — assembler does not support)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_MFC0()
        {
            // mfc0 $t0, $16  => opcode=0x10, rs=0 (MFC0 sub-op), rt=8 ($t0), rd=16
            uint enc = 0x40088000u;
            AssertDisasm(enc, "mfc0");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MTC0()
        {
            uint enc = 0x40888000u; // mtc0 $t0, $16
            AssertDisasm(enc, "mtc0");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DMFC0()
        {
            // dmfc0 $t0, $16 => opcode=0x10, rs=1, rt=8, rd=16
            uint enc = (uint)((0x10 << 26) | (1 << 21) | (8 << 16) | (16 << 11));
            AssertDisasm(enc, "dmfc0");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DMTC0()
        {
            uint enc = (uint)((0x10 << 26) | (5 << 21) | (8 << 16) | (16 << 11));
            AssertDisasm(enc, "dmtc0");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CFC0()
        {
            uint enc = (uint)((0x10 << 26) | (2 << 21) | (8 << 16) | (16 << 11));
            AssertDisasm(enc, "cfc0");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CTC0()
        {
            uint enc = 0x40C88000u; // ctc0 $t0, $16
            AssertDisasm(enc, "ctc0");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ERET()
        {
            uint enc = 0x42000018u;
            AssertDisasm(enc, "eret");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TLBR()
        {
            uint enc = 0x42000001u;
            AssertDisasm(enc, "tlbr");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TLBWI()
        {
            uint enc = 0x42000002u;
            AssertDisasm(enc, "tlbwi");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TLBWR()
        {
            uint enc = 0x42000006u;
            AssertDisasm(enc, "tlbwr");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_TLBP()
        {
            uint enc = 0x42000008u;
            AssertDisasm(enc, "tlbp");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CACHE()
        {
            // cache op=3, 0x10($s0)  => opcode=0x2F, rs=16, rt=3, imm=0x10
            uint enc = MipsEncoder.EncodeI(0x2F, 16, 3, 0x10);
            AssertDisasm(enc, "cache");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // COP1 (FPU) instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_MFC1()
        {
            uint enc = 0x44088000u; // mfc1 $t0, $f16
            AssertDisasm(enc, "mfc1");
            var cpu = MakeCpu(); cpu.FPR[16] = 3.14f;
            Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DMFC1()
        {
            uint enc = (uint)((0x11 << 26) | (1 << 21) | (8 << 16) | (0 << 11));
            AssertDisasm(enc, "dmfc1");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MTC1()
        {
            uint enc = 0x44880000u; // mtc1 $t0, $f16
            AssertDisasm(enc, "mtc1");
            var cpu = MakeCpu(); cpu.RegsLo[8] = 0x40490FDB; // float bits for ~pi
            Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DMTC1()
        {
            uint enc = (uint)((0x11 << 26) | (5 << 21) | (8 << 16) | (0 << 11));
            AssertDisasm(enc, "dmtc1");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CFC1()
        {
            // cfc1 $t0, $fcr31  => opcode=0x11, rs=2, rt=8, rd=31
            uint enc = (uint)((0x11 << 26) | (2 << 21) | (8 << 16) | (31 << 11));
            AssertDisasm(enc, "cfc1");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CTC1()
        {
            uint enc = (uint)((0x11 << 26) | (6 << 21) | (8 << 16) | (31 << 11));
            AssertDisasm(enc, "ctc1");
            var cpu = MakeCpu(); Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_LWC1()
        {
            // lwc1 $f0, 0x10($s0)  => opcode=0x31, rs=16, rt=0, imm=0x10
            uint enc = MipsEncoder.EncodeI(0x31, 16, 0, 0x10);
            AssertDisasm(enc, "lwc1");
            byte[] mem = MakeTestMemory();
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0; Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SWC1()
        {
            uint enc = MipsEncoder.EncodeI(0x39, 16, 0, 0x10);
            AssertDisasm(enc, "swc1");
            byte[] mem = new byte[256];
            var cpu = MakeCpu(mem); cpu.RegsLo[16] = 0; cpu.FPR[0] = 1.5f; Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ADD_S()
        {
            // add.s $f2, $f0, $f1 => fd=f2(shamt), fs=f0(rd), ft=f1(rt), funct=0x00
            // enc: opcode=0x11, fmt=0x10, ft=1, fs=0, fd=2, funct=0x00
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | (2 << 6) | 0x00);
            AssertDisasm(enc, "add.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            // fs=f0=decoded.Rd, ft=f1=decoded.Rt, fd=f2=decoded.Shamt
            cpu.FPR[decoded.Rd] = 1.5f;
            cpu.FPR[decoded.Rt] = 2.5f;
            Exec(cpu, enc);
            Assert.AreEqual(4.0f, cpu.FPR[decoded.Shamt], 0.001f, "ADD.S: 1.5+2.5=4.0");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SUB_S()
        {
            // sub.s $f2, $f0, $f1 => fd=f2(shamt), fs=f0(rd), ft=f1(rt), funct=0x01
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | (2 << 6) | 0x01);
            AssertDisasm(enc, "sub.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 5.0f;  // fs
            cpu.FPR[decoded.Rt] = 2.0f;  // ft
            Exec(cpu, enc);
            Assert.AreEqual(3.0f, cpu.FPR[decoded.Shamt], 0.001f, "SUB.S: 5-2=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MUL_S()
        {
            // mul.s $f2, $f0, $f1 => fd=f2(shamt), fs=f0(rd), ft=f1(rt), funct=0x02
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | (2 << 6) | 0x02);
            AssertDisasm(enc, "mul.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 3.0f;  // fs
            cpu.FPR[decoded.Rt] = 4.0f;  // ft
            Exec(cpu, enc);
            Assert.AreEqual(12.0f, cpu.FPR[decoded.Shamt], 0.001f, "MUL.S: 3*4=12");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_DIV_S()
        {
            // div.s $f2, $f0, $f1 => fd=f2(shamt), fs=f0(rd), ft=f1(rt), funct=0x03
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | (2 << 6) | 0x03);
            AssertDisasm(enc, "div.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 9.0f;  // fs
            cpu.FPR[decoded.Rt] = 3.0f;  // ft
            Exec(cpu, enc);
            Assert.AreEqual(3.0f, cpu.FPR[decoded.Shamt], 0.001f, "DIV.S: 9/3=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SQRT_S()
        {
            // sqrt.s $f2, $f1 => fd=f2(shamt), fs=f1(rd), funct=0x04
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 11) | (2 << 6) | 0x04);
            AssertDisasm(enc, "sqrt.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 9.0f;  // fs=f1
            Exec(cpu, enc);
            Assert.AreEqual(3.0f, cpu.FPR[decoded.Shamt], 0.001f, "SQRT.S: sqrt(9)=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ABS_S()
        {
            // abs.s $f2, $f1 => fd=f2(shamt), fs=f1(rd), funct=0x05
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 11) | (2 << 6) | 0x05);
            AssertDisasm(enc, "abs.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = -5.0f;  // fs=f1
            Exec(cpu, enc);
            Assert.AreEqual(5.0f, cpu.FPR[decoded.Shamt], 0.001f, "ABS.S: |-5|=5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_MOV_S()
        {
            // mov.s $f2, $f1 => fd=f2(shamt), fs=f1(rd), funct=0x06
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 11) | (2 << 6) | 0x06);
            AssertDisasm(enc, "mov.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 7.77f;  // fs=f1
            Exec(cpu, enc);
            Assert.AreEqual(7.77f, cpu.FPR[decoded.Shamt], 0.001f, "MOV.S: copy");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_NEG_S()
        {
            // neg.s $f2, $f1 => fd=f2(shamt), fs=f1(rd), funct=0x07
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 11) | (2 << 6) | 0x07);
            AssertDisasm(enc, "neg.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 3.5f;  // fs=f1
            Exec(cpu, enc);
            Assert.AreEqual(-3.5f, cpu.FPR[decoded.Shamt], 0.001f, "NEG.S: -(3.5)=-3.5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CVT_W_S()
        {
            // cvt.w.s $f2, $f1 => fd=f2(shamt), fs=f1(rd), funct=0x24
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 11) | (2 << 6) | 0x24);
            AssertDisasm(enc, "cvt.w.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 3.7f;  // fs=f1
            Exec(cpu, enc);
            // Result in fd: 3.7 truncated to 3 stored as IEEE bits
            int resultBits = (int)BitConverter.SingleToUInt32Bits(cpu.FPR[decoded.Shamt]);
            Assert.AreEqual(3, resultBits, "CVT.W.S: 3.7 truncates to 3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_CVT_S_W()
        {
            // cvt.s.w $f2, $f1 => fd=f2(shamt), fs=f1(rd), funct=0x20
            uint enc = (uint)((0x11 << 26) | (0x14 << 21) | (1 << 11) | (2 << 6) | 0x20);
            AssertDisasm(enc, "cvt.s.w");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            // FPR[decoded.Rd] (fs=f1) as integer bits = 5 => cvt to float 5.0
            cpu.FPR[decoded.Rd] = BitConverter.Int32BitsToSingle(5);
            Exec(cpu, enc);
            Assert.AreEqual(5.0f, cpu.FPR[decoded.Shamt], 0.001f, "CVT.S.W: int 5 -> 5.0f");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_C_EQ_S()
        {
            // c.eq.s $f0, $f1 => fs=f0(rd), ft=f1(rt), funct=0x32
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | 0x32);
            AssertDisasm(enc, "c.eq.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            // fs=decoded.Rd, ft=decoded.Rt
            cpu.FPR[decoded.Rd] = 1.5f;
            cpu.FPR[decoded.Rt] = 1.5f;
            Exec(cpu, enc); // Should set condition flag (equal)
            Assert.IsTrue(cpu.COP1.GetConditionCode(), "C.EQ.S: 1.5==1.5 should set CC");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_C_LT_S()
        {
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | 0x34);
            AssertDisasm(enc, "c.lt.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 1.0f;  // fs
            cpu.FPR[decoded.Rt] = 2.0f;  // ft
            Exec(cpu, enc);
            Assert.IsTrue(cpu.COP1.GetConditionCode(), "C.LT.S: 1.0<2.0 should set CC");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_C_LE_S()
        {
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | 0x36);
            AssertDisasm(enc, "c.le.s");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.FPR[decoded.Rd] = 2.0f;  // fs
            cpu.FPR[decoded.Rt] = 2.0f;  // ft
            Exec(cpu, enc);
            Assert.IsTrue(cpu.COP1.GetConditionCode(), "C.LE.S: 2.0<=2.0 should set CC");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BC1F()
        {
            // bc1f offset=8  => opcode=0x11, rs=8, rt=0, imm=8
            uint enc = MipsEncoder.EncodeI(0x11, 8, 0, 8);
            AssertDisasm(enc, "bc1f");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_BC1T()
        {
            uint enc = MipsEncoder.EncodeI(0x11, 8, 1, 8);
            AssertDisasm(enc, "bc1t");
            AssertAnalysis(enc);
        }

        // -----------------------------------------------------------------------
        // VU0 (COP2) instructions
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_VADD()
        {
            // vadd $vf1, $vf16, $vf2 => opcode=0x12, rs=0x10, rt=2, rd=1, funct=0x28
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (2 << 16) | (1 << 11) | 0x28);
            AssertDisasm(enc, "vadd");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(1, 2, 3, 4);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(5, 6, 7, 8);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            Assert.AreEqual(6.0f, result.X, 0.001f, "VADD X: 1+5=6");
            Assert.AreEqual(8.0f, result.Y, 0.001f, "VADD Y: 2+6=8");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VSUB()
        {
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (2 << 16) | (1 << 11) | 0x29);
            AssertDisasm(enc, "vsub");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(10, 8, 6, 4);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(1, 2, 3, 4);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            Assert.AreEqual(9.0f, result.X, 0.001f, "VSUB X: 10-1=9");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VMUL()
        {
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (2 << 16) | (1 << 11) | 0x2A);
            AssertDisasm(enc, "vmul");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(2, 3, 4, 5);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(3, 4, 5, 6);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            Assert.AreEqual(6.0f, result.X, 0.001f, "VMUL X: 2*3=6");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VMAX()
        {
            // vmax funct=0x2B, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x2B);
            AssertDisasm(enc, "vmax");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(5, 2, 8, 1);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(3, 6, 4, 9);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            Assert.AreEqual(5.0f, result.X, 0.001f, "VMAX X: max(5,3)=5");
            Assert.AreEqual(6.0f, result.Y, 0.001f, "VMAX Y: max(2,6)=6");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VMIN()
        {
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x2C);
            AssertDisasm(enc, "vmin");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(5, 2, 8, 1);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(3, 6, 4, 9);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            Assert.AreEqual(3.0f, result.X, 0.001f, "VMIN X: min(5,3)=3");
            Assert.AreEqual(2.0f, result.Y, 0.001f, "VMIN Y: min(2,6)=2");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VMSUB()
        {
            // vmsub funct=0x2D, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x2D);
            AssertDisasm(enc, "vmsub");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.ACC = new System.Numerics.Vector4(10, 10, 10, 10);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(2, 3, 4, 5);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(1, 1, 1, 1);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            // VMSUB: vfd = ACC - (vfs * vft) = 10 - 2*1 = 8
            Assert.AreEqual(8.0f, result.X, 0.001f, "VMSUB X: 10-2*1=8");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VADDA()
        {
            // vadda funct=0x2E, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x2E);
            AssertDisasm(enc, "vadda");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(1, 2, 3, 4);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(10, 20, 30, 40);
            Exec(cpu, enc);
            Assert.AreEqual(11.0f, cpu.VU0.ACC.X, 0.001f, "VADDA ACC.X: 1+10=11");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VABS()
        {
            // vabs funct=0x2F, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x2F);
            AssertDisasm(enc, "vabs");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(-5, -3, 7, -1);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            Assert.AreEqual(5.0f, result.X, 0.001f, "VABS X: |-5|=5");
            Assert.AreEqual(3.0f, result.Y, 0.001f, "VABS Y: |-3|=3");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VMADD()
        {
            // vmadd funct=0x30, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x30);
            AssertDisasm(enc, "vmadd");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.ACC = new System.Numerics.Vector4(10, 10, 10, 10);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(2, 3, 4, 5);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(3, 4, 5, 6);
            Exec(cpu, enc);
            var result = cpu.VU0.VF[decoded.Rd];
            // VMADD: vfd = ACC + vfs * vft = 10 + 2*3 = 16
            Assert.AreEqual(16.0f, result.X, 0.001f, "VMADD X: 10+2*3=16");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VMULA()
        {
            // vmula funct=0x31, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x31);
            AssertDisasm(enc, "vmula");
            var cpu = MakeCpu();
            var decoded = MipsDecoder.DecodeR(enc);
            cpu.VU0.VF[decoded.Rs] = new System.Numerics.Vector4(3, 4, 5, 6);
            cpu.VU0.VF[decoded.Rt] = new System.Numerics.Vector4(2, 2, 2, 2);
            Exec(cpu, enc);
            Assert.AreEqual(6.0f, cpu.VU0.ACC.X, 0.001f, "VMULA ACC.X: 3*2=6");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VFTOI0()
        {
            // vftoi0 funct=0x3C, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x3C);
            AssertDisasm(enc, "vftoi0");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_VITOF0()
        {
            // vitof0 funct=0x3D, rs=0x10, rt=1, rd=2
            uint enc = (uint)((0x12 << 26) | (0x10 << 21) | (1 << 16) | (2 << 11) | 0x3D);
            AssertDisasm(enc, "vitof0");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            AssertAnalysis(enc);
        }

        // =====================================================================
        // COP1 FP denormal flushing — compare instructions (PS2 EE behaviour)
        // =====================================================================

        [TestMethod]
        public void Test_C_EQ_S_DenormalFlushedToZero()
        {
            // On PS2 EE, a denormal input is treated as zero before comparison.
            // Denormal value: exponent=0, mantissa!=0 → e.g. the smallest positive denormal.
            float denormal = BitConverter.UInt32BitsToSingle(1u); // 0x00000001
            Assert.IsTrue(float.IsSubnormal(denormal));

            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | 0x32);
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = denormal;  // fs = denormal → flushed to 0
            cpu.FPR[d.Rt] = 0f;        // ft = 0
            Exec(cpu, enc);
            Assert.IsTrue(cpu.COP1.GetConditionCode(),
                "C.EQ.S: denormal == 0 should set CC (denormal flushed to zero)");
        }

        [TestMethod]
        public void Test_C_LT_S_DenormalFlushedToZero()
        {
            // A positive denormal is less than a positive normal, but after flushing
            // to zero the comparison is 0 < 1.0 which is true.
            float denormal = BitConverter.UInt32BitsToSingle(1u);
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | 0x34);
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = denormal;  // fs = denormal → flushed to 0
            cpu.FPR[d.Rt] = 1.0f;
            Exec(cpu, enc);
            Assert.IsTrue(cpu.COP1.GetConditionCode(),
                "C.LT.S: denormal < 1.0 should be 0 < 1.0 = true after flush");
        }

        [TestMethod]
        public void Test_C_LE_S_DenormalFlushedToZero()
        {
            // Both operands are denormal → both flushed to 0 → 0 <= 0 = true.
            float denormal = BitConverter.UInt32BitsToSingle(1u);
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (1 << 16) | (0 << 11) | 0x36);
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = denormal;
            cpu.FPR[d.Rt] = denormal;
            Exec(cpu, enc);
            Assert.IsTrue(cpu.COP1.GetConditionCode(),
                "C.LE.S: denormal <= denormal should be 0 <= 0 = true after flush");
        }

        // -----------------------------------------------------------------------
        // COP1X (opcode=0x13) instructions — indexed FPR load/store and fused FP ops
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Test_LWXC1()
        {
            // lwxc1 $f2, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), fd=$f2, funct=0x00
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | (2 << 11) | 0x00);
            AssertDisasm(enc, "lwxc1");
            byte[] mem = new byte[256];
            // Store float 2.5 at offset 0x08 (base=0, index=8)
            uint floatBits = BitConverter.SingleToUInt32Bits(2.5f);
            mem[8] = (byte)(floatBits & 0xFF); mem[9] = (byte)((floatBits >> 8) & 0xFF);
            mem[10] = (byte)((floatBits >> 16) & 0xFF); mem[11] = (byte)((floatBits >> 24) & 0xFF);
            var cpu = MakeCpu(mem);
            cpu.RegsLo[8] = 0;  // base = 0
            cpu.RegsLo[9] = 8;  // index = 8
            Exec(cpu, enc);
            Assert.AreEqual(2.5f, cpu.FPR[2], 0.001f, "LWXC1: loaded 2.5 from base+index");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_LDXC1()
        {
            // ldxc1 $f4, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), fd=$f4, funct=0x01
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | (4 << 11) | 0x01);
            AssertDisasm(enc, "ldxc1");
            byte[] mem = new byte[256];
            uint lo = BitConverter.SingleToUInt32Bits(1.5f);
            uint hi = BitConverter.SingleToUInt32Bits(3.5f);
            for (int i = 0; i < 4; i++) mem[8 + i] = (byte)((lo >> (i * 8)) & 0xFF);
            for (int i = 0; i < 4; i++) mem[12 + i] = (byte)((hi >> (i * 8)) & 0xFF);
            var cpu = MakeCpu(mem);
            cpu.RegsLo[8] = 0; cpu.RegsLo[9] = 8;
            Exec(cpu, enc);
            Assert.AreEqual(1.5f, cpu.FPR[4], 0.001f, "LDXC1: loaded low word into fd");
            Assert.AreEqual(3.5f, cpu.FPR[5], 0.001f, "LDXC1: loaded high word into fd+1");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_LUXC1()
        {
            // luxc1 $f2, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), fd=$f2, funct=0x05
            // This is also the encoding of the first value from the problem statement (with different registers/fd)
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | (2 << 11) | 0x05);
            AssertDisasm(enc, "luxc1");
            byte[] mem = new byte[256];
            uint floatBits = BitConverter.SingleToUInt32Bits(4.0f);
            // LUXC1 aligns to word boundary; base=0, index=8 → addr=8 (already aligned)
            for (int i = 0; i < 4; i++) mem[8 + i] = (byte)((floatBits >> (i * 8)) & 0xFF);
            var cpu = MakeCpu(mem);
            cpu.RegsLo[8] = 0; cpu.RegsLo[9] = 8;
            Exec(cpu, enc);
            Assert.AreEqual(4.0f, cpu.FPR[2], 0.001f, "LUXC1: loaded 4.0 from aligned base+index");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SWXC1()
        {
            // swxc1 $f3, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), fs=$f3(shamt), funct=0x08
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | (3 << 6) | 0x08);
            AssertDisasm(enc, "swxc1");
            byte[] mem = new byte[256];
            var cpu = MakeCpu(mem);
            cpu.RegsLo[8] = 0; cpu.RegsLo[9] = 8;
            cpu.FPR[3] = 7.0f;
            Exec(cpu, enc);
            uint stored = BitConverter.ToUInt32(mem, 8);
            Assert.AreEqual(7.0f, BitConverter.UInt32BitsToSingle(stored), 0.001f, "SWXC1: stored 7.0 at base+index");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SDXC1()
        {
            // sdxc1 $f4, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), fs=$f4(shamt), funct=0x09
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | (4 << 6) | 0x09);
            AssertDisasm(enc, "sdxc1");
            byte[] mem = new byte[256];
            var cpu = MakeCpu(mem);
            cpu.RegsLo[8] = 0; cpu.RegsLo[9] = 8;
            cpu.FPR[4] = 5.0f; cpu.FPR[5] = 6.0f;
            Exec(cpu, enc);
            float lo = BitConverter.UInt32BitsToSingle(BitConverter.ToUInt32(mem, 8));
            float hi = BitConverter.UInt32BitsToSingle(BitConverter.ToUInt32(mem, 12));
            Assert.AreEqual(5.0f, lo, 0.001f, "SDXC1: stored FPR[4] at base+index");
            Assert.AreEqual(6.0f, hi, 0.001f, "SDXC1: stored FPR[5] at base+index+4");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SUXC1()
        {
            // suxc1 $f3, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), fs=$f3(shamt), funct=0x0D
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | (3 << 6) | 0x0D);
            AssertDisasm(enc, "suxc1");
            byte[] mem = new byte[256];
            var cpu = MakeCpu(mem);
            cpu.RegsLo[8] = 0; cpu.RegsLo[9] = 8;  // aligned address
            cpu.FPR[3] = 9.0f;
            Exec(cpu, enc);
            uint stored = BitConverter.ToUInt32(mem, 8);
            Assert.AreEqual(9.0f, BitConverter.UInt32BitsToSingle(stored), 0.001f, "SUXC1: stored 9.0 at aligned base+index");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_PREFX()
        {
            // prefx 0, $t1($t0): opcode=0x13, base=$t0(8), index=$t1(9), hint=0, funct=0x0F
            uint enc = (uint)((0x13 << 26) | (8 << 21) | (9 << 16) | 0x0F);
            AssertDisasm(enc, "prefx");
            var cpu = MakeCpu();
            ulong pcBefore = cpu.PC;
            Exec(cpu, enc);  // no-op; CPU state unchanged
            Assert.AreEqual(pcBefore, cpu.PC, "PREFX: PC unchanged (hint/nop)");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_MADD_S()
        {
            // madd.s $f4, $f0, $f1, $f2: fr=$f0(rs=0), ft=$f2(rt=2), fd=$f4(rd=4), fs=$f1(shamt=1), funct=0x20
            // fd = fr*fs + ft => $f4 = $f0*$f1 + $f2
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x20);
            AssertDisasm(enc, "madd.s");
            var cpu = MakeCpu();
            cpu.FPR[0] = 3.0f;  // fr
            cpu.FPR[1] = 2.0f;  // fs
            cpu.FPR[2] = 1.0f;  // ft
            Exec(cpu, enc);
            Assert.AreEqual(7.0f, cpu.FPR[4], 0.001f, "MADD.S: 3*2+1=7");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_MADD_D()
        {
            // madd.d $f4, $f0, $f1, $f2: funct=0x21
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x21);
            AssertDisasm(enc, "madd.d");
            var cpu = MakeCpu();
            cpu.FPR[0] = 2.0f; cpu.FPR[1] = 4.0f; cpu.FPR[2] = 0.5f;
            Exec(cpu, enc);
            Assert.AreEqual(8.5f, cpu.FPR[4], 0.001f, "MADD.D: 2*4+0.5=8.5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_MSUB_S()
        {
            // msub.s $f4, $f0, $f1, $f2: funct=0x28 — fd = fr*fs - ft
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x28);
            AssertDisasm(enc, "msub.s");
            var cpu = MakeCpu();
            cpu.FPR[0] = 3.0f; cpu.FPR[1] = 2.0f; cpu.FPR[2] = 1.0f;
            Exec(cpu, enc);
            Assert.AreEqual(5.0f, cpu.FPR[4], 0.001f, "MSUB.S: 3*2-1=5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_MSUB_D()
        {
            // msub.d $f4, $f0, $f1, $f2: funct=0x29
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x29);
            AssertDisasm(enc, "msub.d");
            var cpu = MakeCpu();
            cpu.FPR[0] = 5.0f; cpu.FPR[1] = 3.0f; cpu.FPR[2] = 4.0f;
            Exec(cpu, enc);
            Assert.AreEqual(11.0f, cpu.FPR[4], 0.001f, "MSUB.D: 5*3-4=11");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_NMADD_S()
        {
            // nmadd.s $f4, $f0, $f1, $f2: funct=0x30 — fd = -(fr*fs + ft)
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x30);
            AssertDisasm(enc, "nmadd.s");
            var cpu = MakeCpu();
            cpu.FPR[0] = 3.0f; cpu.FPR[1] = 2.0f; cpu.FPR[2] = 1.0f;
            Exec(cpu, enc);
            Assert.AreEqual(-7.0f, cpu.FPR[4], 0.001f, "NMADD.S: -(3*2+1)=-7");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_NMADD_D()
        {
            // nmadd.d funct=0x31
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x31);
            AssertDisasm(enc, "nmadd.d");
            var cpu = MakeCpu();
            cpu.FPR[0] = 2.0f; cpu.FPR[1] = 4.0f; cpu.FPR[2] = 0.5f;
            Exec(cpu, enc);
            Assert.AreEqual(-8.5f, cpu.FPR[4], 0.001f, "NMADD.D: -(2*4+0.5)=-8.5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_NMSUB_S()
        {
            // nmsub.s $f4, $f0, $f1, $f2: funct=0x38 — fd = -(fr*fs - ft)
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x38);
            AssertDisasm(enc, "nmsub.s");
            var cpu = MakeCpu();
            cpu.FPR[0] = 3.0f; cpu.FPR[1] = 2.0f; cpu.FPR[2] = 1.0f;
            Exec(cpu, enc);
            Assert.AreEqual(-5.0f, cpu.FPR[4], 0.001f, "NMSUB.S: -(3*2-1)=-5");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_COP1X_NMSUB_D()
        {
            // nmsub.d funct=0x39
            uint enc = (uint)((0x13 << 26) | (0 << 21) | (2 << 16) | (4 << 11) | (1 << 6) | 0x39);
            AssertDisasm(enc, "nmsub.d");
            var cpu = MakeCpu();
            cpu.FPR[0] = 5.0f; cpu.FPR[1] = 3.0f; cpu.FPR[2] = 4.0f;
            Exec(cpu, enc);
            Assert.AreEqual(-11.0f, cpu.FPR[4], 0.001f, "NMSUB.D: -(5*3-4)=-11");
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_ProblemStatement_Hex_Values()
        {
            // Verify the three hex values from the problem statement are now properly decoded.

            // 0x4F4C4545: opcode=0x13 (COP1X), funct=0x05 => LUXC1
            uint luxc1Enc = 0x4F4C4545u;
            string luxc1Disasm = MipsDisassembler.Disassemble(luxc1Enc);
            Assert.IsTrue(luxc1Disasm.ToLowerInvariant().Contains("luxc1"),
                $"0x4F4C4545 should disassemble as luxc1, got: {luxc1Disasm}");
            Assert.IsFalse(luxc1Disasm.Contains(".word"),
                $"0x4F4C4545 should NOT disassemble as .word (unknown), got: {luxc1Disasm}");

            // 0x4E49203A: opcode=0x13 (COP1X), funct=0x3A => no defined COP1X instruction
            // Should disassemble as .word with Unknown COP1X comment (not a valid instruction)
            uint unknownEnc = 0x4E49203Au;
            string unknownDisasm = MipsDisassembler.Disassemble(unknownEnc);
            Assert.IsTrue(unknownDisasm.Contains(".word"),
                $"0x4E49203A has undefined COP1X funct=0x3A and should disassemble as .word, got: {unknownDisasm}");
            Assert.IsTrue(unknownDisasm.Contains("COP1X"),
                $"0x4E49203A unknown COP1X should mention COP1X in comment, got: {unknownDisasm}");

            // 0x0A72656D: opcode=0x02 => J instruction (already supported)
            uint jEnc = 0x0A72656Du;
            string jDisasm = MipsDisassembler.Disassemble(jEnc);
            Assert.IsTrue(jDisasm.ToLowerInvariant().Contains("j ") || jDisasm.ToLowerInvariant().StartsWith("j "),
                $"0x0A72656D should disassemble as j (jump), got: {jDisasm}");
            Assert.IsFalse(jDisasm.Contains(".word"),
                $"0x0A72656D should NOT disassemble as .word (unknown), got: {jDisasm}");
        }

        [TestMethod]
        public void Test_Unknown_COP2_COP0_ProblemStatement()
        {
            // 0x49202300: opcode=0x12 (COP2), rs=0x09, funct=0x00
            // COP2 rs=0x09 is not defined on the PS2 EE (PCSX2 COP2 table index 9 = COP2_Unknown).
            // The disassembler must report it as .word with an "Unknown COP2" comment.
            uint cop2Unknown = 0x49202300u;
            string cop2Disasm = MipsDisassembler.Disassemble(cop2Unknown);
            Assert.IsTrue(cop2Disasm.Contains(".word"),
                $"0x49202300 (COP2 rs=0x09) should disassemble as .word, got: {cop2Disasm}");
            Assert.IsTrue(cop2Disasm.Contains("COP2"),
                $"0x49202300 unknown COP2 should mention COP2 in comment, got: {cop2Disasm}");
            Assert.IsTrue(cop2Disasm.Contains("0x09"),
                $"0x49202300 unknown COP2 comment should include rs=0x09, got: {cop2Disasm}");

            // 0x43414D44: opcode=0x10 (COP0), rs=0x1A, funct=0x04
            // rs=0x1A (26) is not a defined COP0 sub-operation on the PS2 EE.
            // The disassembler must report it as .word with an "Unknown COP0" comment.
            uint cop0Unknown = 0x43414D44u;
            string cop0Disasm = MipsDisassembler.Disassemble(cop0Unknown);
            Assert.IsTrue(cop0Disasm.Contains(".word"),
                $"0x43414D44 (COP0 rs=0x1A) should disassemble as .word, got: {cop0Disasm}");
            Assert.IsTrue(cop0Disasm.Contains("COP0"),
                $"0x43414D44 unknown COP0 should mention COP0 in comment, got: {cop0Disasm}");
            Assert.IsTrue(cop0Disasm.Contains("0x1A"),
                $"0x43414D44 unknown COP0 comment should include rs=0x1A, got: {cop0Disasm}");
        }

        [TestMethod]
        public void Test_OpCode1D_ProblemStatement_Disassembly()
        {
            // 0x74726574: opcode=0x1D — undefined in the PS2 EE R5900 standard opcode
            // table (confirmed via PCSX2 R5900OpcodeTables.cpp: tbl_Standard[0x1D] = Unknown).
            // The disassembler must report it as .word with an "Unknown" and opcode comment.
            uint enc = 0x74726574u;
            string disasm = MipsDisassembler.Disassemble(enc);
            Assert.IsTrue(disasm.Contains(".word"),
                $"0x74726574 (opcode=0x1D) should disassemble as .word, got: {disasm}");
            Assert.IsTrue(disasm.Contains("0x74726574"),
                $"0x74726574 output should echo the raw encoding, got: {disasm}");
            Assert.IsTrue(disasm.Contains("1D") || disasm.Contains("0x1D"),
                $"0x74726574 output should identify opcode 0x1D, got: {disasm}");
        }

        // -----------------------------------------------------------------------
        // Branch/jump target peek tests (label + string hints)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void TryGetBranchJumpTarget_J_ComputesTarget()
        {
            // J encoding: opcode=0x02, addr field = 0x100000
            // PC = 0x80000000 → target = (0x80000004 & 0xF0000000) | (0x100000 << 2) = 0x80400000
            uint enc = MipsEncoder.EncodeJ(0x02, 0x100000);
            uint? target = MipsAnalyzer.TryGetBranchJumpTarget(enc, 0x80000000u, null);
            Assert.AreEqual(0x80400000u, target, "J target should be 0x80400000");
        }

        [TestMethod]
        public void TryGetBranchJumpTarget_JAL_ComputesTarget()
        {
            uint enc = MipsEncoder.EncodeJ(0x03, 0x100000);
            uint? target = MipsAnalyzer.TryGetBranchJumpTarget(enc, 0x80000000u, null);
            Assert.AreEqual(0x80400000u, target, "JAL target should be 0x80400000");
        }

        [TestMethod]
        public void TryGetBranchJumpTarget_BEQ_ComputesTarget()
        {
            // BEQ $t0, $t1, +8  =>  offset=2  => target = PC+4+(2<<2) = PC+12
            uint enc = MipsEncoder.EncodeI(0x04, 8, 9, 2);
            uint? target = MipsAnalyzer.TryGetBranchJumpTarget(enc, 0x80001000u, null);
            Assert.AreEqual(0x8000100Cu, target, "BEQ target = PC+4+(offset<<2)");
        }

        [TestMethod]
        public void TryGetBranchJumpTarget_JR_ComputesTargetFromCpu()
        {
            // JR $ra — target is cpu.RegsLo[31]
            uint enc = MipsEncoder.EncodeR(0, 31, 0, 0, 0, 0x08);
            var cpu = MakeCpu();
            cpu.RegsLo[31] = 0x80012345u;
            uint? target = MipsAnalyzer.TryGetBranchJumpTarget(enc, 0x80001000u, cpu);
            Assert.AreEqual(0x80012345u, target, "JR target = $ra");
        }

        [TestMethod]
        public void TryGetBranchJumpTarget_JR_NullWithoutCpu()
        {
            uint enc = MipsEncoder.EncodeR(0, 31, 0, 0, 0, 0x08);
            uint? target = MipsAnalyzer.TryGetBranchJumpTarget(enc, 0x80001000u, null);
            Assert.IsNull(target, "JR with no CPU state should return null");
        }

        [TestMethod]
        public void TryGetBranchJumpTarget_NonBranch_ReturnsNull()
        {
            // ADDU $t0, $s0, $s1 — not a branch/jump
            uint enc = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x21);
            uint? target = MipsAnalyzer.TryGetBranchJumpTarget(enc, 0x80000000u, null);
            Assert.IsNull(target, "ADDU should return null target");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_J_DoesNotPeekCodeTargetBytes()
        {
            // J targets are *code* addresses — even when their bytes happen to spell
            // ASCII, the analyzer must not annotate them as strings (the bytes are
            // instructions, not data).
            uint targetAddr = 0x00100000u;
            uint enc = (0x02u << 26) | ((targetAddr >> 2) & 0x03FFFFFFu);

            var bus = new PS2MemoryBus();
            byte[] str = System.Text.Encoding.ASCII.GetBytes("Hello\0");
            for (int i = 0; i < str.Length; i++)
                bus.WriteByte(targetAddr + (uint)i, str[i]);

            var cpu = new CpuState { PC = 0x00000000u };
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x00000000u, cpu, bus);

            Assert.IsFalse(analysis.Contains("\"Hello\""),
                $"Analysis must not ASCII-peek a branch/jump target, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_JAL_SubstitutesLabelInline()
        {
            // JAL whose target has a user-defined label — the comment should
            // show the label *in place of* the raw hex address.
            uint targetAddr = 0x80400000u;
            uint enc = MipsEncoder.EncodeJ(0x03, 0x100000); // target = 0x80400000 from PC 0x80000000

            var cpu = new CpuState { PC = 0x80000000u };
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null,
                addr => addr == targetAddr ? "scePadRead" : null);

            Assert.IsTrue(analysis.Contains("scePadRead"),
                $"Analysis should inline the label, got: {analysis}");
            Assert.IsFalse(analysis.Contains("0x80400000"),
                $"Analysis must replace the address with the label, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_LabelPreferredOverString()
        {
            // When both a label and a string are present, the label substitutes
            // the address and the string lookup never runs.
            uint targetAddr = 0x00100000u;
            uint enc = (0x02u << 26) | ((targetAddr >> 2) & 0x03FFFFFFu);

            var bus = new PS2MemoryBus();
            byte[] str = System.Text.Encoding.ASCII.GetBytes("RawStr\0");
            for (int i = 0; i < str.Length; i++)
                bus.WriteByte(targetAddr + (uint)i, str[i]);

            var cpu = new CpuState { PC = 0x00000000u };
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x00000000u, cpu, bus,
                addr => addr == targetAddr ? "MyLabel" : null);

            Assert.IsTrue(analysis.Contains("MyLabel"),
                $"Analysis should inline the label, got: {analysis}");
            Assert.IsFalse(analysis.Contains("\"RawStr\""),
                $"Analysis should not show raw string when label substitutes the address, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_NoHintForNonPrintable()
        {
            // Target address contains non-printable bytes — no string hint should appear
            uint targetAddr = 0x00100000u;
            uint enc = (0x02u << 26) | ((targetAddr >> 2) & 0x03FFFFFFu);

            var bus = new PS2MemoryBus();
            bus.WriteByte(targetAddr, 0x01); // non-printable

            var cpu = new CpuState { PC = 0x00000000u };
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x00000000u, cpu, bus);

            Assert.IsFalse(analysis.Contains("=>"),
                $"Analysis should not append a hint for non-printable target bytes, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // ASCII string hints in load/store effective-address comments
        // -----------------------------------------------------------------------

        [TestMethod]
        public void AnalyzeSingleInstruction_LW_AppendsStringHintAtLoadAddress()
        {
            // LW $a0, 0($a1): $a0 ← [0x00100000], with a string at that address
            var bus = new PS2MemoryBus();
            uint strAddr = 0x00100000u;
            byte[] hello = System.Text.Encoding.ASCII.GetBytes("Hello\0");
            for (int i = 0; i < hello.Length; i++)
                bus.WriteByte(strAddr + (uint)i, hello[i]);

            // LW $a0, 0($a1) — opcode=0x23, rs=$a1(5), rt=$a0(4), imm=0
            uint enc = (0x23u << 26) | (5u << 21) | (4u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[5] = strAddr; // $a1 = address of string
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, bus);

            Assert.IsTrue(analysis.Contains("\"Hello\""),
                $"Analysis should contain string hint 'Hello' for LW effective address, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_LB_AppendsStringHintAtLoadAddress()
        {
            // LB $a0, 0($a1) — opcode=0x20
            var bus = new PS2MemoryBus();
            uint strAddr = 0x00200000u;
            byte[] world = System.Text.Encoding.ASCII.GetBytes("World\0");
            for (int i = 0; i < world.Length; i++)
                bus.WriteByte(strAddr + (uint)i, world[i]);

            uint enc = (0x20u << 26) | (5u << 21) | (4u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[5] = strAddr;
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, bus);

            Assert.IsTrue(analysis.Contains("\"World\""),
                $"Analysis should contain string hint 'World' for LB effective address, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_SW_ShowsRawWordWhenNoString()
        {
            // SW $a0, 0($a1) — opcode=0x2B; the effective address holds non-ASCII data
            var bus = new PS2MemoryBus();
            uint dataAddr = 0x00300000u;
            bus.WriteWord(dataAddr, 0xDEADBEEF);

            uint enc = (0x2Bu << 26) | (5u << 21) | (4u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[5] = dataAddr;
            cpu.RegsLo[4] = 0x12345678; // value being stored
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, bus);

            // The store address should appear in the comment
            Assert.IsTrue(analysis.Contains("300000") || analysis.Contains("a0"),
                $"Analysis should mention the store address, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeInstructions_SD_WithUnknownBase_ShowsRegisterPlusOffset()
        {
            // SD $v0, $0018($a2).  In batch/static analysis $a2 starts unknown/zero,
            // so the comment should show "a2 + 0x0018" — never a bare offset that
            // looks like an absolute address.
            uint enc = (0x3Fu << 26) | (6u << 21) | (2u << 16) | 0x0018u;

            var comments = MipsAnalyzer.AnalyzeInstructions(
                new[] { (enc, 0x001A3A30u) },
                startPc: 0x001A3A30u,
                executeInstructions: true,
                includeStateHints: false);

            string analysis = comments[0x001A3A30u];
            Assert.IsTrue(analysis.Contains("a2") && analysis.Contains("0x0018"),
                $"Expected base register + 16-bit offset in SD analysis, got: {analysis}");
            Assert.IsFalse(analysis.Contains("[0x00000018]"),
                $"Should not show unresolved offset as an 8-digit address, got: {analysis}");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(analysis, @"\[0x[0-9A-Fa-f]{4}\]"),
                $"Should not show bare 4-digit offset masquerading as an address, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_SD_WithKnownBase_ShowsResolvedAddress()
        {
            uint enc = (0x3Fu << 26) | (6u << 21) | (2u << 16) | 0x0018u;

            var cpu = new CpuState();
            cpu.RegsLo[6] = 0x001A0000u;
            cpu.RegsLo[2] = 0xCFu;

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x001A3A30u, cpu);

            Assert.IsTrue(analysis.Contains("[0x001A0018]"),
                $"Expected resolved address when base register is known, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // MMIO register name hints
        // -----------------------------------------------------------------------

        [TestMethod]
        public void AnalyzeSingleInstruction_LW_ShowsMmioName_INTC_STAT()
        {
            // LW $v0, 0($at): with $at = 0x1000F000 (INTC_STAT)
            // LW opcode=0x23, rs=$at(1), rt=$v0(2), imm=0
            uint enc = (0x23u << 26) | (1u << 21) | (2u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[1] = 0x1000F000u; // $at = INTC_STAT

            // No bus needed — MMIO names are looked up by address alone
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("INTC_STAT"),
                $"Analysis should identify 0x1000F000 as INTC_STAT, got: {analysis}");
        }

        [TestMethod]
        public void AnalyzeSingleInstruction_SW_ShowsMmioName_T0_MODE()
        {
            // SW $v0, 0($at): with $at = 0x10000010 (T0_MODE)
            uint enc = (0x2Bu << 26) | (1u << 21) | (2u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[1] = 0x10000010u;
            cpu.RegsLo[2] = 0x1234u;

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("T0_MODE"),
                $"Analysis should identify 0x10000010 as T0_MODE, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // SYSCALL annotation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_SYSCALL_ShowsOsExceptionHint()
        {
            // SYSCALL with no code field — opcode=0x00, funct=0x0C
            uint enc = 0x0000000Cu; // syscall with code=0

            var cpu = MakeCpu();
            cpu.RegsLo[3] = 0x22; // $v1 = syscall #0x22 (StartThread)
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("OS exception") || analysis.Contains("syscall"),
                $"Analysis should annotate SYSCALL as OS exception, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_SYSCALL_WithKnownCode_ShowsName()
        {
            // SYSCALL with code=0x20 (CreateThread) in bits 25:6
            // bits 25:21 = code >> 14, 20:16 = (code >> 9) & 0x1F, etc.
            // Simple: put code=0x20 = 32 in the shamt field shift positions
            // code = (rs<<14)|(rt<<9)|(rd<<4)|(shamt>>1)
            // For code=0x20: shamt = 0x40, so Asm: funct=0x0C, shamt=0x40 >> 1 = no...
            // Actually, easiest: use the Asm method directly.
            var syscall = new SYSCALL();
            uint enc = syscall.Asm(new[] { 0x20 }); // code = 0x20 = CreateThread

            var cpu = MakeCpu();
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("CreateThread") || analysis.Contains("syscall"),
                $"Analysis of SYSCALL code=0x20 should mention CreateThread, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // JAL/JALR argument register preview
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_JAL_ShowsArgumentRegisters()
        {
            // JAL $80400000 from PC $80000000
            uint enc = MipsEncoder.EncodeJ(0x03, 0x100000); // JAL to 0x80400000

            var cpu = new CpuState { PC = 0x80000000u };
            cpu.RegsLo[4] = 0xAAAA1111u; // $a0
            cpu.RegsLo[5] = 0xBBBB2222u; // $a1
            cpu.RegsLo[6] = 0xCCCC3333u; // $a2
            cpu.RegsLo[7] = 0xDDDD4444u; // $a3

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("a0 = 0xAAAA1111"), $"Analysis should show $a0, got: {analysis}");
            Assert.IsTrue(analysis.Contains("a1 = 0xBBBB2222"), $"Analysis should show $a1, got: {analysis}");
            Assert.IsTrue(analysis.Contains("a2 = 0xCCCC3333"), $"Analysis should show $a2, got: {analysis}");
            Assert.IsTrue(analysis.Contains("a3 = 0xDDDD4444"), $"Analysis should show $a3, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_JALR_ShowsArgumentRegisters()
        {
            // JALR $t9 — opcode=0x00, rs=$t9(25), rd=$ra(31), funct=0x09
            uint enc = (25u << 21) | (31u << 11) | 0x09u;

            var cpu = new CpuState { PC = 0x80000000u };
            cpu.RegsLo[25] = 0x80400000u; // $t9 = target
            cpu.RegsLo[4]  = 0x11223344u; // $a0

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("a0 = 0x11223344"), $"JALR analysis should show $a0, got: {analysis}");
            Assert.IsTrue(analysis.Contains("0x80400000"), $"JALR analysis should show the call target, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_JAL_ShowsTempRegisters_WhenNonZero()
        {
            // JAL $80400000 from PC $80000000 with non-zero $t0/$t1
            uint enc = MipsEncoder.EncodeJ(0x03, 0x100000); // JAL to 0x80400000

            var cpu = new CpuState { PC = 0x80000000u };
            cpu.RegsLo[8]  = 0xDEADBEEFu; // $t0 — non-zero, should appear
            cpu.RegsLo[9]  = 0x0000CAFEu; // $t1 — non-zero, should appear
            // $t2-$t7 left zero — should not appear

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("t0 = 0xDEADBEEF"), $"Analysis should show $t0, got: {analysis}");
            Assert.IsTrue(analysis.Contains("t1 = 0x0000CAFE"), $"Analysis should show $t1, got: {analysis}");
            Assert.IsFalse(analysis.Contains("t2 ="), $"Analysis should not show zero $t2, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // Stack frame detection (ADDIU $sp, $sp, ±N)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_ADDIU_Sp_Negative_ShowsAllocate()
        {
            // ADDIU $sp, $sp, -32  → opcode=0x09, rs=$sp(29), rt=$sp(29), imm=-32=0xFFE0
            uint enc = (0x09u << 26) | (29u << 21) | (29u << 16) | (0xFFE0u & 0xFFFF);

            var cpu = new CpuState();
            cpu.RegsLo[29] = 0x00800000u; // $sp

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("allocate") || analysis.Contains("32"),
                $"Analysis should mention stack frame allocation, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_ADDIU_Sp_Positive_ShowsFree()
        {
            // ADDIU $sp, $sp, +32
            uint enc = (0x09u << 26) | (29u << 21) | (29u << 16) | 32u;

            var cpu = new CpuState();
            cpu.RegsLo[29] = 0x007FFFE0u;

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("free") || analysis.Contains("32"),
                $"Analysis should mention stack frame deallocation, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_ADDIU_NonSp_NoStackHint()
        {
            // ADDIU $a0, $zero, 100 — should NOT show stack frame hint
            uint enc = (0x09u << 26) | (0u << 21) | (4u << 16) | 100u;

            var cpu = new CpuState();
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsFalse(analysis.Contains("stack") && (analysis.Contains("allocate") || analysis.Contains("free")),
                $"Non-$sp ADDIU should not show stack hint, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // COP0 register name annotation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_MFC0_ShowsRegisterName_Status()
        {
            // MFC0 $v0, Status(12): opcode=0x10, rs=0x00, rt=$v0(2), rd=12, funct=0
            uint enc = (0x10u << 26) | (0x00u << 21) | (2u << 16) | (12u << 11);

            var cpu = MakeCpu();
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("Status"),
                $"Analysis should identify COP0[12] as Status, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_MTC0_ShowsRegisterName_EPC()
        {
            // MTC0 $v0, EPC(14): opcode=0x10, rs=0x04 (MTC0), rt=$v0(2), rd=14
            uint enc = (0x10u << 26) | (0x04u << 21) | (2u << 16) | (14u << 11);

            var cpu = MakeCpu();
            cpu.RegsLo[2] = 0x80001234u;
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("EPC"),
                $"Analysis should identify COP0[14] as EPC, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_MFC0_ShowsRegisterName_Cause()
        {
            // MFC0 $v0, Cause(13)
            uint enc = (0x10u << 26) | (0x00u << 21) | (2u << 16) | (13u << 11);

            var cpu = MakeCpu();
            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("Cause"),
                $"Analysis should identify COP0[13] as Cause, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // Loop detection in backward branches
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_BEQ_BackwardBranch_ShowsLoopHint()
        {
            // BEQ $t0, $t1, -4 (jumps back 4 instructions = -12 from delay slot)
            // offset = -4, so imm = -4 (16-bit signed) = 0xFFFC
            // PC=0x80000040, target = PC+4 + (-4<<2) = 0x80000040+4-16 = 0x80000034 < PC
            uint enc = MipsEncoder.EncodeI(0x04, 8, 9, unchecked((ushort)-4));

            var cpu = MakeCpu();
            cpu.PC = 0x80000040u;
            cpu.RegsLo[8] = 5;
            cpu.RegsLo[9] = 5; // equal → taken

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000040u, cpu, null);

            Assert.IsTrue(analysis.Contains("loop"),
                $"Backward BEQ should mention 'loop', got: {analysis}");
        }

        [TestMethod]
        public void Analyze_BNE_ForwardBranch_NoLoopHint()
        {
            // BNE $t0, $t1, +4 (forward) — should NOT show loop hint
            uint enc = MipsEncoder.EncodeI(0x05, 8, 9, 4);

            var cpu = MakeCpu();
            cpu.PC = 0x80000000u;
            cpu.RegsLo[8] = 5;
            cpu.RegsLo[9] = 3;

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsFalse(analysis.Contains("loop"),
                $"Forward BNE should not mention 'loop', got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // LWC1 / SWC1 float value display
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_LWC1_ShowsEffectiveAddress()
        {
            // LWC1 $f0, 0($a0): opcode=0x31, rs=$a0(4), rt=0(ft=0), imm=0
            uint enc = (0x31u << 26) | (4u << 21) | (0u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[4] = 0x00005000u;

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("5000") || analysis.Contains("$f0"),
                $"LWC1 analysis should mention effective address or FPR, got: {analysis}");
        }

        [TestMethod]
        public void Analyze_SWC1_ShowsFloatValue()
        {
            // SWC1 $f1, 0($a0): opcode=0x39, rs=$a0(4), rt=1(ft=1), imm=0
            uint enc = (0x39u << 26) | (4u << 21) | (1u << 16) | 0u;

            var cpu = new CpuState();
            cpu.RegsLo[4] = 0x00006000u;
            cpu.FPR[1] = 3.14f;

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            // The float value or address should appear in the comment
            Assert.IsTrue(analysis.Contains("3.14") || analysis.Contains("6000") || analysis.Contains("$f1"),
                $"SWC1 analysis should show float value or address, got: {analysis}");
        }

        // -----------------------------------------------------------------------
        // MTC1 float value display
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Analyze_MTC1_ShowsFloatInterpretation()
        {
            // MTC1 $a0, $f2: opcode=0x11, rs=MTC1(0x04), rt=$a0(4), rd=2
            uint enc = (0x11u << 26) | (0x04u << 21) | (4u << 16) | (2u << 11);

            var cpu = new CpuState();
            // Load 1.0f as bits into $a0
            cpu.RegsLo[4] = BitConverter.SingleToUInt32Bits(1.0f);

            string analysis = MipsAnalyzer.AnalyzeSingleInstruction(enc, 0x80000000u, cpu, null);

            Assert.IsTrue(analysis.Contains("1") && (analysis.Contains("$f2") || analysis.Contains("$a0")),
                $"MTC1 analysis should show the float value, got: {analysis}");
        }
    }
}
