using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE.TestProject
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
            AssertAnalysis(enc);
        }

        [TestMethod]
        public void Test_SYNC()
        {
            uint enc = MipsEncoder.EncodeR(0, 0, 0, 0, 0, 0x0F);
            AssertAsm("sync", enc);
            AssertDisasm(enc, "sync");
            var cpu = MakeCpu(); Exec(cpu, enc); // sync is a no-op
            AssertAnalysis(enc);
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
    }
}
