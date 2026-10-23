using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.Cpu.Instructions.MMI;
using CodeDesigner.Ps2.Common.Instructions.CoprocessorInstructions;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for PS2 EE architecture instructions: MMI, extended COP1, and additional VU0.
    /// </summary>
    [TestClass]
    public class TestPS2Instructions
    {
        private static CpuState MakeCpu(byte[]? memory = null)
        {
            var cpu = new CpuState();
            cpu.RegsLo[8]  = 0x00000005; // $t0 = 5
            cpu.RegsLo[9]  = 0x00000003; // $t1 = 3
            cpu.RegsLo[16] = 0x0000000A; // $s0 = 10
            cpu.RegsLo[17] = 0x00000007; // $s1 = 7
            cpu.RegsLo[18] = 0x00000002; // $s2 = 2
            cpu.PC = 0x80000000;
            if (memory != null) cpu.Memory = memory;
            return cpu;
        }

        private static void Exec(CpuState cpu, uint encoded)
        {
            var (obj, args) = MipsDisassembler.GetInstructionObject(encoded);
            Assert.IsNotNull(obj,  $"No instruction for 0x{encoded:X8}");
            Assert.IsNotNull(args, $"No args for 0x{encoded:X8}");
            obj!.ExecuteGeneric(cpu, args!);
        }

        private static void AssertDisasm(uint encoded, string mnemonic)
        {
            string d = MipsDisassembler.Disassemble(encoded);
            Assert.IsTrue(d.ToLowerInvariant().Contains(mnemonic.ToLowerInvariant()),
                $"Expected '{mnemonic}' in disassembly but got: {d}");
        }

        // =====================================================================
        // MMI: Direct instructions
        // =====================================================================

        [TestMethod]
        public void Test_MADD()
        {
            // MADD $t0, $s0, $s1  =>  HI:LO += 10*7=70, $t0=LO
            // opcode=0x1C, rs=16, rt=17, rd=8, funct=0x00
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x00);
            AssertDisasm(enc, "madd");
            var cpu = MakeCpu();
            cpu.HI = 0; cpu.LO = 0;
            Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO, "MADD: LO should be 70");
            Assert.AreEqual(70UL, cpu.RegsLo[8], "MADD: $t0 should equal LO=70");
        }

        [TestMethod]
        public void Test_MADDU()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x01);
            AssertDisasm(enc, "maddu");
            var cpu = MakeCpu();
            cpu.HI = 0; cpu.LO = 0;
            Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO, "MADDU: LO should be 70");
        }

        [TestMethod]
        public void Test_PLZCW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 8 << 11 | 0x04);
            AssertDisasm(enc, "plzcw");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0x0000000A; // positive, so count leading zeros
            Exec(cpu, enc);
            // 0x0000000A = 0b1010, has 27 leading zeros (bits 0..27), PLZCW subtracts 1: result=27
            // But PLZCW result = CLZ(val) - 1
            Assert.IsTrue(cpu.RegsLo[8] > 0, "PLZCW: should produce non-zero result");
        }

        [TestMethod]
        public void Test_MFHI1_MTHI1()
        {
            // MTHI1 $s0  (HI1 = $s0 = 10)
            uint encMt = (uint)(0x1C << 26 | 16 << 21 | 0x11);
            AssertDisasm(encMt, "mthi1");
            var cpu = MakeCpu();
            Exec(cpu, encMt);
            Assert.AreEqual(10UL, cpu.HI1, "MTHI1: HI1 should be 10");

            // MFHI1 $t0  (rd=8, HI1->$t0)
            uint encMf = (uint)(0x1C << 26 | 8 << 11 | 0x10);
            AssertDisasm(encMf, "mfhi1");
            Exec(cpu, encMf);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "MFHI1: $t0 should be 10");
        }

        [TestMethod]
        public void Test_MFLO1_MTLO1()
        {
            uint encMt = (uint)(0x1C << 26 | 17 << 21 | 0x13);
            AssertDisasm(encMt, "mtlo1");
            var cpu = MakeCpu();
            Exec(cpu, encMt);
            Assert.AreEqual(7UL, cpu.LO1, "MTLO1: LO1 should be 7");

            uint encMf = (uint)(0x1C << 26 | 8 << 11 | 0x12);
            AssertDisasm(encMf, "mflo1");
            Exec(cpu, encMf);
            Assert.AreEqual(7UL, cpu.RegsLo[8], "MFLO1: $t0 should be 7");
        }

        [TestMethod]
        public void Test_MULT1()
        {
            // MULT1 $t0, $s0, $s1  => LO1:HI1 = 10*7=70
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x18);
            AssertDisasm(enc, "mult1");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO1, "MULT1: LO1 should be 70");
            Assert.AreEqual(0UL,  cpu.HI1, "MULT1: HI1 should be 0");
            Assert.AreEqual(70UL, cpu.RegsLo[8], "MULT1: $t0 should be 70");
        }

        [TestMethod]
        public void Test_MULTU1()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x19);
            AssertDisasm(enc, "multu1");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO1, "MULTU1: LO1 should be 70");
        }

        [TestMethod]
        public void Test_DIV1()
        {
            // DIV1 $s0, $s1  => LO1=10/7=1, HI1=10%7=3
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 0x1A);
            AssertDisasm(enc, "div1");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.LO1, "DIV1: LO1 should be 1 (10/7)");
            Assert.AreEqual(3UL, cpu.HI1, "DIV1: HI1 should be 3 (10%7)");
        }

        [TestMethod]
        public void Test_DIVU1()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 0x1B);
            AssertDisasm(enc, "divu1");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(1UL, cpu.LO1, "DIVU1: LO1 should be 1");
            Assert.AreEqual(3UL, cpu.HI1, "DIVU1: HI1 should be 3");
        }

        [TestMethod]
        public void Test_MADD1()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x20);
            AssertDisasm(enc, "madd1");
            var cpu = MakeCpu();
            cpu.HI1 = 0; cpu.LO1 = 0;
            Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO1, "MADD1: LO1 should be 70");
        }

        [TestMethod]
        public void Test_MADDU1()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x21);
            AssertDisasm(enc, "maddu1");
            var cpu = MakeCpu();
            cpu.HI1 = 0; cpu.LO1 = 0;
            Exec(cpu, enc);
            Assert.AreEqual(70UL, cpu.LO1, "MADDU1: LO1 should be 70");
        }

        // =====================================================================
        // MMI0 sub-group
        // =====================================================================

        [TestMethod]
        public void Test_PADDW()
        {
            // PADDW $t0, $s0, $s1  (shamt=0x00, funct=0x08)
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x00 << 6 | 0x08);
            AssertDisasm(enc, "paddw");
            var cpu = MakeCpu();
            // $s0 = [10, 0, 0, 0], $s1 = [7, 0, 0, 0]
            Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PADDW: W0 should be 17");
        }

        [TestMethod]
        public void Test_PSUBW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x01 << 6 | 0x08);
            AssertDisasm(enc, "psubw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(3UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PSUBW: W0 should be 3 (10-7)");
        }

        [TestMethod]
        public void Test_PMAXW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x03 << 6 | 0x08);
            AssertDisasm(enc, "pmaxw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(10UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PMAXW: W0 should be max(10,7)=10");
        }

        [TestMethod]
        public void Test_PADDH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x04 << 6 | 0x08);
            AssertDisasm(enc, "paddh");
            var cpu = MakeCpu();
            // $s0 lower 16 bits = 10, $s1 lower 16 bits = 7
            Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8] & 0xFFFF, "PADDH: H0 should be 17");
        }

        [TestMethod]
        public void Test_PADDB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x08 << 6 | 0x08);
            AssertDisasm(enc, "paddb");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8] & 0xFF, "PADDB: B0 should be 17");
        }

        [TestMethod]
        public void Test_PEXTLW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x12 << 6 | 0x08);
            AssertDisasm(enc, "pextlw");
            var cpu = MakeCpu();
            // $s0 = [10, 0, 0, 0], $s1 = [7, 0, 0, 0]
            // result: rd.W0=rt.W0=7, rd.W1=rs.W0=10, rd.W2=rt.W1=0, rd.W3=rs.W1=0
            Exec(cpu, enc);
            Assert.AreEqual(7UL,  cpu.RegsLo[8] & 0xFFFFFFFF,        "PEXTLW: W0 should be 7");
            Assert.AreEqual(10UL, (cpu.RegsLo[8] >> 32) & 0xFFFFFFFF, "PEXTLW: W1 should be 10");
        }

        [TestMethod]
        public void Test_PPACW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x13 << 6 | 0x08);
            AssertDisasm(enc, "ppacw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            // rd.W0=rt.W0=7, rd.W1=rt.W2=0, rd.W2=rs.W0=10, rd.W3=rs.W2=0
            Assert.AreEqual(7UL,  cpu.RegsLo[8] & 0xFFFFFFFF, "PPACW: W0 should be rt.W0=7");
        }

        // =====================================================================
        // MMI1 sub-group
        // =====================================================================

        [TestMethod]
        public void Test_PABSW()
        {
            // PABSW rd, rt: the source is the rt field.
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 8 << 11 | 0x01 << 6 | 0x28);
            AssertDisasm(enc, "pabsw");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = unchecked((ulong)(long)-5); // W0 = -5
            Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PABSW: abs(-5) should be 5");
        }

        [TestMethod]
        public void Test_PCEQW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x02 << 6 | 0x28);
            AssertDisasm(enc, "pceqw");
            var cpu = MakeCpu();
            // $s0=10, $s1=7: W0 not equal -> 0
            Exec(cpu, enc);
            Assert.AreEqual(0UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PCEQW: 10!=7 so result=0");

            // Set equal
            cpu.RegsLo[16] = 7;
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFFFFFUL, cpu.RegsLo[8] & 0xFFFFFFFF, "PCEQW: 7==7 so result=-1");
        }

        [TestMethod]
        public void Test_PMINW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x03 << 6 | 0x28);
            AssertDisasm(enc, "pminw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(7UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PMINW: min(10,7)=7");
        }

        [TestMethod]
        public void Test_PADDUW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x10 << 6 | 0x28);
            AssertDisasm(enc, "padduw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PADDUW: 10+7=17");
        }

        [TestMethod]
        public void Test_PSUBUW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x11 << 6 | 0x28);
            AssertDisasm(enc, "psubuw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(3UL, cpu.RegsLo[8] & 0xFFFFFFFF, "PSUBUW: 10-7=3");
        }

        [TestMethod]
        public void Test_PADDUB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x18 << 6 | 0x28);
            AssertDisasm(enc, "paddub");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(17UL, cpu.RegsLo[8] & 0xFF, "PADDUB: B0 = 10+7=17");
        }

        // =====================================================================
        // MMI2 / MMI3 sub-group
        // =====================================================================

        [TestMethod]
        public void Test_PMFHI_PMTHI()
        {
            // PMTHI $s0 => HI=LO of $s0=10, HI1=HI of $s0=0
            uint encMt = (uint)(0x1C << 26 | 16 << 21 | 0x08 << 6 | 0x29);
            AssertDisasm(encMt, "pmthi");
            var cpu = MakeCpu();
            Exec(cpu, encMt);
            Assert.AreEqual(10UL, cpu.HI, "PMTHI: HI should be LO of $s0=10");

            // PMFHI $t0 => $t0[63:0]=HI=10, $t0[127:64]=HI1=0
            uint encMf = (uint)(0x1C << 26 | 8 << 11 | 0x08 << 6 | 0x09);
            AssertDisasm(encMf, "pmfhi");
            Exec(cpu, encMf);
            Assert.AreEqual(10UL, cpu.RegsLo[8], "PMFHI: $t0[63:0] should be HI=10");
        }

        [TestMethod]
        public void Test_PMFLO_PMTLO()
        {
            uint encMt = (uint)(0x1C << 26 | 17 << 21 | 0x09 << 6 | 0x29);
            AssertDisasm(encMt, "pmtlo");
            var cpu = MakeCpu();
            Exec(cpu, encMt);
            Assert.AreEqual(7UL, cpu.LO, "PMTLO: LO should be LO of $s1=7");

            uint encMf = (uint)(0x1C << 26 | 8 << 11 | 0x09 << 6 | 0x09);
            AssertDisasm(encMf, "pmflo");
            Exec(cpu, encMf);
            Assert.AreEqual(7UL, cpu.RegsLo[8], "PMFLO: $t0[63:0] should be LO=7");
        }

        [TestMethod]
        public void Test_PCPYLD()
        {
            // PCPYLD $t0, $s0, $s1 => $t0.lo=$s1.lo, $t0.hi=$s0.lo
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x0E << 6 | 0x09);
            AssertDisasm(enc, "pcpyld");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(7UL,  cpu.RegsLo[8], "PCPYLD: lower = $s1.lo = 7");
            Assert.AreEqual(10UL, cpu.RegsHi[8], "PCPYLD: upper = $s0.lo = 10");
        }

        [TestMethod]
        public void Test_PCPYUD()
        {
            // PCPYUD $t0, $s0, $s1 => $t0.lo=$s1.hi, $t0.hi=$s0.hi
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x0E << 6 | 0x29);
            AssertDisasm(enc, "pcpyud");
            var cpu = MakeCpu();
            cpu.RegsHi[16] = 0xABCD;
            cpu.RegsHi[17] = 0x1234;
            Exec(cpu, enc);
            Assert.AreEqual(0x1234UL, cpu.RegsLo[8], "PCPYUD: lower = $s1.hi = 0x1234");
            Assert.AreEqual(0xABCDUL, cpu.RegsHi[8], "PCPYUD: upper = $s0.hi = 0xABCD");
        }

        [TestMethod]
        public void Test_PAND()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x12 << 6 | 0x09);
            AssertDisasm(enc, "pand");
            var cpu = MakeCpu();
            // $s0=10 (0b1010), $s1=7 (0b0111) => AND = 0b0010 = 2
            Exec(cpu, enc);
            Assert.AreEqual(2UL, cpu.RegsLo[8], "PAND: 10 & 7 = 2");
        }

        [TestMethod]
        public void Test_POR()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x12 << 6 | 0x29);
            AssertDisasm(enc, "por");
            var cpu = MakeCpu();
            // $s0=10 (0b1010), $s1=7 (0b0111) => OR = 0b1111 = 15
            Exec(cpu, enc);
            Assert.AreEqual(15UL, cpu.RegsLo[8], "POR: 10 | 7 = 15");
        }

        [TestMethod]
        public void Test_PCPYH()
        {
            uint enc = (uint)(0x1C << 26 | 17 << 16 | 8 << 11 | 0x1B << 6 | 0x29);
            AssertDisasm(enc, "pcpyh");
            var cpu = MakeCpu();
            // $s1=7; copy H0=7 to all 4 halfwords of lower 64 bits
            Exec(cpu, enc);
            Assert.AreEqual(7UL,     cpu.RegsLo[8] & 0xFFFF,         "PCPYH: H0 should be 7");
            Assert.AreEqual(7UL,     (cpu.RegsLo[8] >> 16) & 0xFFFF, "PCPYH: H1 should be 7");
            Assert.AreEqual(7UL,     (cpu.RegsLo[8] >> 32) & 0xFFFF, "PCPYH: H2 should be 7");
            Assert.AreEqual(7UL,     (cpu.RegsLo[8] >> 48) & 0xFFFF, "PCPYH: H3 should be 7");
        }

        // =====================================================================
        // Extended COP1 instructions
        // =====================================================================

        [TestMethod]
        public void Test_RSQRT_S()
        {
            // RSQRT.S fd=0, fs=1, ft=2  (opcode=0x11, rs=0x10, funct=0x16)
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x16);
            AssertDisasm(enc, "rsqrt.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = 8.0f; cpu.FPR[d.Rt] = 4.0f; // FPR[16]=8, FPR[ft=2]=4
            Exec(cpu, enc);
            // FPR[d.Shamt] = FPR[16] / sqrt(|FPR[2]|) = 8/2 = 4
            Assert.AreEqual(4.0f, cpu.FPR[d.Shamt], 0.0001f, "RSQRT.S: 8/sqrt(4)=4");
        }

        [TestMethod]
        public void Test_ADDA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x18);
            AssertDisasm(enc, "adda.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 4.0f; // FPR[16]=3, FPR[ft=2]=4
            Exec(cpu, enc);
            Assert.AreEqual(7.0f, cpu.FACC, 0.0001f, "ADDA.S: ACC = FPR[16](3)+FPR[ft](4)=7");
        }

        [TestMethod]
        public void Test_SUBA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x19);
            AssertDisasm(enc, "suba.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = 10.0f; cpu.FPR[d.Rt] = 3.0f;
            Exec(cpu, enc);
            Assert.AreEqual(7.0f, cpu.FACC, 0.0001f, "SUBA.S: ACC = 10-3=7");
        }

        [TestMethod]
        public void Test_MULA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1A);
            AssertDisasm(enc, "mula.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
            Exec(cpu, enc);
            Assert.AreEqual(12.0f, cpu.FACC, 0.0001f, "MULA.S: ACC = 3*4=12");
        }

        [TestMethod]
        public void Test_MADD_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1C);
            AssertDisasm(enc, "madd.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FACC = 10.0f; cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
            Exec(cpu, enc);
            // FPR[d.Shamt] = ACC(10) + FPR[16](3) * FPR[ft](4) = 22
            Assert.AreEqual(22.0f, cpu.FPR[d.Shamt], 0.0001f, "MADD.S: fd = ACC(10) + 3*4=12 -> 22");
        }

        [TestMethod]
        public void Test_MSUB_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1D);
            AssertDisasm(enc, "msub.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FACC = 20.0f; cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
            Exec(cpu, enc);
            Assert.AreEqual(8.0f, cpu.FPR[d.Shamt], 0.0001f, "MSUB.S: fd = ACC(20) - 3*4=12 -> 8");
        }

        [TestMethod]
        public void Test_MADDA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1E);
            AssertDisasm(enc, "madda.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FACC = 5.0f; cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 2.0f;
            Exec(cpu, enc);
            Assert.AreEqual(11.0f, cpu.FACC, 0.0001f, "MADDA.S: ACC = 5 + 3*2=6 -> 11");
        }

        [TestMethod]
        public void Test_MSUBA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1F);
            AssertDisasm(enc, "msuba.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FACC = 20.0f; cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
            Exec(cpu, enc);
            Assert.AreEqual(8.0f, cpu.FACC, 0.0001f, "MSUBA.S: ACC = 20 - 3*4=12 -> 8");
        }

        [TestMethod]
        public void Test_MAX_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x28);
            AssertDisasm(enc, "max.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 7.0f;
            Exec(cpu, enc);
            Assert.AreEqual(7.0f, cpu.FPR[d.Shamt], 0.0001f, "MAX.S: max(3,7)=7");
        }

        [TestMethod]
        public void Test_MIN_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x29);
            AssertDisasm(enc, "min.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rd] = 3.0f; cpu.FPR[d.Rt] = 7.0f;
            Exec(cpu, enc);
            Assert.AreEqual(3.0f, cpu.FPR[d.Shamt], 0.0001f, "MIN.S: min(3,7)=3");
        }

        [TestMethod]
        public void Test_BC1FL_BC1TL()
        {
            // BC1FL: opcode=0x11, rs=0x08, rt=0x02
            uint encFL = (uint)(0x11 << 26 | 0x08 << 21 | 0x02 << 16 | 0x0010);
            AssertDisasm(encFL, "bc1fl");

            // BC1TL: opcode=0x11, rs=0x08, rt=0x03
            uint encTL = (uint)(0x11 << 26 | 0x08 << 21 | 0x03 << 16 | 0x0010);
            AssertDisasm(encTL, "bc1tl");
        }

        // =====================================================================
        // Regression: existing instructions still work
        // =====================================================================

        [TestMethod]
        public void Test_ExistingInstructions_Still_Work()
        {
            Assert.IsTrue(MipsDisassembler.Disassemble(0x02114020).Contains("add"),  "ADD should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x46010800).Contains("add.s"), "ADD.S should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x4BE00028).Contains("vadd"),  "VADD should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x42000018).Contains("eret"),  "ERET should still work");
        }

        // =====================================================================
        // New branch likely instructions
        // =====================================================================

        [TestMethod]
        public void Test_BLTZL()
        {
            // bltzl $s0, offset=4 => opcode=0x01, rs=16, rt=0x02, imm=4
            uint enc = (uint)(0x01 << 26 | 16 << 21 | 0x02 << 16 | 4);
            AssertDisasm(enc, "bltzl");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = unchecked((ulong)-1L); // negative
            cpu.PC = 0x80000000;
            Exec(cpu, enc);
            Assert.AreEqual(0x80000014UL, cpu.PC, "BLTZL taken: PC = PC + 4 + 16 = 0x80000014");
        }

        [TestMethod]
        public void Test_BGEZL()
        {
            uint enc = (uint)(0x01 << 26 | 16 << 21 | 0x03 << 16 | 2);
            AssertDisasm(enc, "bgezl");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 10; // non-negative
            cpu.PC = 0x80000000;
            Exec(cpu, enc);
            Assert.AreEqual(0x8000000CUL, cpu.PC, "BGEZL taken: PC = PC + 4 + 8 = 0x8000000C");
        }

        [TestMethod]
        public void Test_BLEZL()
        {
            uint enc = (uint)(0x16 << 26 | 16 << 21 | 2);
            AssertDisasm(enc, "blezl");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0; // zero <= 0
            cpu.PC = 0x80000000;
            Exec(cpu, enc);
            Assert.AreEqual(0x8000000CUL, cpu.PC, "BLEZL taken: PC = PC + 4 + 8 = 0x8000000C");
        }

        [TestMethod]
        public void Test_BGTZL()
        {
            uint enc = (uint)(0x17 << 26 | 16 << 21 | 2);
            AssertDisasm(enc, "bgtzl");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 5; // positive
            cpu.PC = 0x80000000;
            Exec(cpu, enc);
            Assert.AreEqual(0x8000000CUL, cpu.PC, "BGTZL taken: PC = PC + 4 + 8 = 0x8000000C");
        }

        // =====================================================================
        // MFSA / MTSA tests
        // =====================================================================

        [TestMethod]
        public void Test_MFSA()
        {
            // mfsa $t0 => rd=8, funct=0x28
            uint enc = (uint)(8 << 11 | 0x28);
            AssertDisasm(enc, "mfsa");
            var cpu = MakeCpu(); cpu.SA = 42;
            Exec(cpu, enc);
            Assert.AreEqual(42UL, cpu.RegsLo[8], "MFSA: $t0 = SA = 42");
        }

        [TestMethod]
        public void Test_MTSA()
        {
            // mtsa $s0 => rs=16, funct=0x29
            uint enc = (uint)(16 << 21 | 0x29);
            AssertDisasm(enc, "mtsa");
            var cpu = MakeCpu(); cpu.RegsLo[16] = 7;
            Exec(cpu, enc);
            Assert.AreEqual(7, cpu.SA, "MTSA: SA = $s0 = 7");
        }

        // =====================================================================
        // MTSAB / MTSAH tests
        // =====================================================================

        [TestMethod]
        public void Test_MTSAB()
        {
            // mtsab $s0, 0 => REGIMM rt=0x18, rs=16, imm=0
            uint enc = (uint)(0x01 << 26 | 16 << 21 | 0x18 << 16 | 0);
            AssertDisasm(enc, "mtsab");
            var cpu = MakeCpu(); cpu.RegsLo[16] = 2; // rs=2, imm=0 => SA = (2^0)*8 = 16
            Exec(cpu, enc);
            Assert.AreEqual(16, cpu.SA, "MTSAB: SA = (2 ^ 0) * 8 = 16");
        }

        [TestMethod]
        public void Test_MTSAH()
        {
            // mtsah $s0, 0 => REGIMM rt=0x19, rs=16, imm=0
            uint enc = (uint)(0x01 << 26 | 16 << 21 | 0x19 << 16 | 0);
            AssertDisasm(enc, "mtsah");
            var cpu = MakeCpu(); cpu.RegsLo[16] = 3; // rs=3, imm=0 => SA = (3^0)*16 = 48
            Exec(cpu, enc);
            Assert.AreEqual(48, cpu.SA, "MTSAH: SA = (3 ^ 0) * 16 = 48");
        }

        // =====================================================================
        // COP0 EI / DI tests
        // =====================================================================

        [TestMethod]
        public void Test_EI()
        {
            // ei => opcode=0x10 (COP0), rs=0x10, funct=0x38
            uint enc = (uint)(0x10 << 26 | 0x10 << 21 | 0x38);
            AssertDisasm(enc, "ei");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.IsTrue((cpu.COP0.ReadRegister32(12) & 0x10000u) != 0, "EI: sets EIE bit");
        }

        [TestMethod]
        public void Test_DI()
        {
            uint enc = (uint)(0x10 << 26 | 0x10 << 21 | 0x39);
            AssertDisasm(enc, "di");
            var cpu = MakeCpu();
            cpu.COP0.WriteRegister32(12, cpu.COP0.ReadRegister32(12) | 0x10000u);
            Exec(cpu, enc);
            Assert.IsTrue((cpu.COP0.ReadRegister32(12) & 0x10000u) == 0, "DI: clears EIE bit");
        }

        // =====================================================================
        // COP1 C_F test
        // =====================================================================

        [TestMethod]
        public void Test_C_F()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 1 << 16 | 0 << 11 | 0x30);
            AssertDisasm(enc, "c.f.s");
            var cpu = MakeCpu();
            cpu.COP1.SetConditionCode(true); // pre-set to true
            Exec(cpu, enc);
            Assert.IsFalse(cpu.COP1.GetConditionCode(), "C.F.S: always sets CC=false");
        }

        // =====================================================================
        // MMI shift tests
        // =====================================================================

        [TestMethod]
        public void Test_PSLLH()
        {
            // psllh $t0, $s0, 2 => funct=0x34, rd=8, rt=16, sa=2
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 8 << 11 | 2 << 6 | 0x34);
            AssertDisasm(enc, "psllh");
            var cpu = MakeCpu(); cpu.RegsLo[16] = 0x0001000200030004UL;
            Exec(cpu, enc);
            Assert.AreEqual(0x00040008000C0010UL, cpu.RegsLo[8], "PSLLH: shift each halfword left by 2");
        }

        [TestMethod]
        public void Test_PXOR()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x13 << 6 | 0x09);
            AssertDisasm(enc, "pxor");
            var cpu = MakeCpu();
            Exec(cpu, enc);
            Assert.AreEqual(cpu.RegsLo[16] ^ cpu.RegsLo[17], cpu.RegsLo[8], "PXOR: 128-bit XOR");
        }

        [TestMethod]
        public void Test_PNOR()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 8 << 11 | 0x13 << 6 | 0x29);
            AssertDisasm(enc, "pnor");
            var cpu = MakeCpu();
            ulong expectedLo = ~(cpu.RegsLo[16] | cpu.RegsLo[17]);
            Exec(cpu, enc);
            Assert.AreEqual(expectedLo, cpu.RegsLo[8], "PNOR: 128-bit NOR");
        }

        [TestMethod]
        public void Test_PMFHL()
        {
            // pmfhl.lw t0: rd = { LO.w0, HI.w0, LO.w2, HI.w2 }
            uint enc = (uint)(0x1C << 26 | 8 << 11 | 0x30);
            Assert.AreEqual("pmfhl.lw t0", MipsDisassembler.Disassemble(enc));
            var cpu = MakeCpu(); cpu.LO = 0x1234; cpu.HI = 0x5678; cpu.LO1 = 0x9ABC; cpu.HI1 = 0xDEF0;
            Exec(cpu, enc);
            Assert.AreEqual(0x00005678_00001234UL, cpu.RegsLo[8], "PMFHL.LW: rd.w0 = LO.w0, rd.w1 = HI.w0");
            Assert.AreEqual(0x0000DEF0_00009ABCUL, cpu.RegsHi[8], "PMFHL.LW: rd.w2 = LO.w2, rd.w3 = HI.w2");
        }

        [TestMethod]
        public void Test_PMFHL_UW_And_SH()
        {
            // pmfhl.uw t0: rd = { LO.w1, HI.w1, LO.w3, HI.w3 }
            uint uw = (uint)(0x1C << 26 | 8 << 11 | 1 << 6 | 0x30);
            Assert.AreEqual("pmfhl.uw t0", MipsDisassembler.Disassemble(uw));
            var cpu = MakeCpu();
            cpu.LO = 0x11111111_00000000UL; cpu.HI = 0x22222222_00000000UL;
            Exec(cpu, uw);
            Assert.AreEqual(0x22222222_11111111UL, cpu.RegsLo[8]);

            // pmfhl.sh t0 saturates each word to a halfword.
            uint sh = (uint)(0x1C << 26 | 8 << 11 | 4 << 6 | 0x30);
            Assert.AreEqual("pmfhl.sh t0", MipsDisassembler.Disassemble(sh));
            cpu.LO = 0xFFFF0000_00012345UL; // w0 = 0x12345 -> 0x7FFF, w1 = -65536 -> 0x8000
            cpu.HI = 0; cpu.LO1 = 0; cpu.HI1 = 0;
            Exec(cpu, sh);
            Assert.AreEqual(0x8000_7FFFUL, cpu.RegsLo[8] & 0xFFFFFFFF);
        }

        [TestMethod]
        public void Test_PMTHL()
        {
            // pmthl.lw s0: LO.w0 = rs.w0, HI.w0 = rs.w1, LO.w2 = rs.w2, HI.w2 = rs.w3
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 0x31);
            Assert.AreEqual("pmthl.lw s0", MipsDisassembler.Disassemble(enc));
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0x00002222_00001111UL; cpu.RegsHi[16] = 0x00004444_00003333UL;
            Exec(cpu, enc);
            Assert.AreEqual(0x1111UL, cpu.LO & 0xFFFFFFFF, "PMTHL.LW: LO.w0 = rs.w0");
            Assert.AreEqual(0x2222UL, cpu.HI & 0xFFFFFFFF, "PMTHL.LW: HI.w0 = rs.w1");
            Assert.AreEqual(0x3333UL, cpu.LO1 & 0xFFFFFFFF, "PMTHL.LW: LO.w2 = rs.w2");
            Assert.AreEqual(0x4444UL, cpu.HI1 & 0xFFFFFFFF, "PMTHL.LW: HI.w2 = rs.w3");
        }

        // =====================================================================
        // MMI0 sub-instructions (funct=0x08, dispatched by shamt)
        // =====================================================================

        [TestMethod]
        public void Test_PCGTW()
        {
            // PCGTW $s2, $s0, $s1  (rd=18, rs=16, rt=17, shamt=0x02)
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x02 << 6 | 0x08);
            AssertDisasm(enc, "pcgtw");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFFFFFU, (uint)(cpu.RegsLo[18] & 0xFFFFFFFF), "PCGTW: 10>7 -> -1");
        }

        [TestMethod]
        public void Test_PSUBH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x05 << 6 | 0x08);
            AssertDisasm(enc, "psubh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFFFF, "PSUBH: 10-7=3");
        }

        [TestMethod]
        public void Test_PMAXH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x07 << 6 | 0x08);
            AssertDisasm(enc, "pmaxh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(10U, cpu.RegsLo[18] & 0xFFFF, "PMAXH: max(10,7)=10");
        }

        [TestMethod]
        public void Test_PSUBB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x09 << 6 | 0x08);
            AssertDisasm(enc, "psubb");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFF, "PSUBB: 10-7=3");
        }

        [TestMethod]
        public void Test_PEXTLH()
        {
            // PEXTLH $s2, $s0, $s1  interleave lower 4 halfwords: H0=rt[0], H1=rs[0], H2=rt[1], H3=rs[1]
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x16 << 6 | 0x08);
            AssertDisasm(enc, "pextlh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(7U, cpu.RegsLo[18] & 0xFFFF, "PEXTLH: H0=rt[0]=7");
            Assert.AreEqual(10U, (cpu.RegsLo[18] >> 16) & 0xFFFF, "PEXTLH: H1=rs[0]=10");
        }

        [TestMethod]
        public void Test_PPACH()
        {
            // PPACH $s2, $s0, $s1  pack even halfwords: rd[0..3]=rt even, rd[4..7]=rs even
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x17 << 6 | 0x08);
            AssertDisasm(enc, "ppach");
            var cpu = MakeCpu(); // s1=7
            Exec(cpu, enc);
            Assert.AreEqual(7U, cpu.RegsLo[18] & 0xFFFF, "PPACH: rd[0]=rt even halfword=7");
        }

        [TestMethod]
        public void Test_PEXTLB()
        {
            // PEXTLB $s2, $s0, $s1  interleave lower 8 bytes
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1A << 6 | 0x08);
            AssertDisasm(enc, "pextlb");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(7U, cpu.RegsLo[18] & 0xFF, "PEXTLB: B0=rt[0]=7");
            Assert.AreEqual(10U, (cpu.RegsLo[18] >> 8) & 0xFF, "PEXTLB: B1=rs[0]=10");
        }

        [TestMethod]
        public void Test_PPACB()
        {
            // PPACB $s2, $s0, $s1  pack even bytes
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1B << 6 | 0x08);
            AssertDisasm(enc, "ppacb");
            var cpu = MakeCpu(); // s1=7
            Exec(cpu, enc);
            Assert.AreEqual(7U, cpu.RegsLo[18] & 0xFF, "PPACB: rd[0]=rt even byte=7");
        }

        [TestMethod]
        public void Test_PCGTH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x06 << 6 | 0x08);
            AssertDisasm(enc, "pcgth");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFU, cpu.RegsLo[18] & 0xFFFF, "PCGTH: 10>7 -> 0xFFFF");
        }

        [TestMethod]
        public void Test_PCGTB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0A << 6 | 0x08);
            AssertDisasm(enc, "pcgtb");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(0xFFU, cpu.RegsLo[18] & 0xFF, "PCGTB: 10>7 -> 0xFF");
        }

        [TestMethod]
        public void Test_PADDSW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x10 << 6 | 0x08);
            AssertDisasm(enc, "paddsw");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(17U, cpu.RegsLo[18] & 0xFFFFFFFF, "PADDSW: 10+7=17");
        }

        [TestMethod]
        public void Test_PSUBSW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x11 << 6 | 0x08);
            AssertDisasm(enc, "psubsw");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFFFFFFFF, "PSUBSW: 10-7=3");
        }

        [TestMethod]
        public void Test_PADDSH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x14 << 6 | 0x08);
            AssertDisasm(enc, "paddsh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(17U, cpu.RegsLo[18] & 0xFFFF, "PADDSH: 10+7=17");
        }

        [TestMethod]
        public void Test_PSUBSH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x15 << 6 | 0x08);
            AssertDisasm(enc, "psubsh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFFFF, "PSUBSH: 10-7=3");
        }

        [TestMethod]
        public void Test_PADDSB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x18 << 6 | 0x08);
            AssertDisasm(enc, "paddsb");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(17U, cpu.RegsLo[18] & 0xFF, "PADDSB: 10+7=17");
        }

        [TestMethod]
        public void Test_PSUBSB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x19 << 6 | 0x08);
            AssertDisasm(enc, "psubsb");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFF, "PSUBSB: 10-7=3");
        }

        [TestMethod]
        public void Test_PEXT5()
        {
            // PEXT5 $s2, $s1  (rd=18, rt=17, shamt=0x1E)
            // Set s1=1 (r=1,g=0,b=0,a=0). Expanded: r = (1<<3)|(1>>2) = 8
            uint enc = (uint)(0x1C << 26 | 17 << 16 | 18 << 11 | 0x1E << 6 | 0x08);
            AssertDisasm(enc, "pext5");
            var cpu = MakeCpu(); // s1=7 -> r=7&0x1F=7, expanded=(7<<3)|(7>>2)=56|1=57
            Exec(cpu, enc);
            Assert.AreEqual((7U << 3) | (7U >> 2), cpu.RegsLo[18] & 0xFF, "PEXT5: R channel expanded");
        }

        [TestMethod]
        public void Test_PPAC5()
        {
            // PPAC5 $s2, $s1  pack 8888 to 1555. s1=7 input: r=7>>3=0, result r=0
            // Use s0=0x080808 (r=8,g=8,b=8): r=8>>3=1, g=8>>3=1, b=8>>3=1 -> 0x0421
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 0x1F << 6 | 0x08);
            AssertDisasm(enc, "ppac5");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0x00080808; // b=8,g=8,r=8
            Exec(cpu, enc);
            uint expected = (1u << 10) | (1u << 5) | 1u; // b=1,g=1,r=1 = 0x0421
            Assert.AreEqual(expected, cpu.RegsLo[18] & 0xFFFF, "PPAC5: 8-8-8 packed to 1-1-1");
        }

        // =====================================================================
        // MMI1 sub-instructions (funct=0x28, dispatched by shamt)
        // =====================================================================

        [TestMethod]
        public void Test_PABSH()
        {
            // PABSH $s2, $s0  (rd=18, rt=16, shamt=0x05)
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 0x05 << 6 | 0x28);
            AssertDisasm(enc, "pabsh");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0xFFF9; // lower 16 bits = -7 as signed short
            Exec(cpu, enc);
            Assert.AreEqual(7U, cpu.RegsLo[18] & 0xFFFF, "PABSH: abs(-7)=7");
        }

        [TestMethod]
        public void Test_PCEQH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x06 << 6 | 0x28);
            AssertDisasm(enc, "pceqh");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 10; cpu.RegsLo[17] = 10; // same -> 0xFFFF
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFU, cpu.RegsLo[18] & 0xFFFF, "PCEQH: 10==10 -> 0xFFFF");
        }

        [TestMethod]
        public void Test_PMINH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x07 << 6 | 0x28);
            AssertDisasm(enc, "pminh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(7U, cpu.RegsLo[18] & 0xFFFF, "PMINH: min(10,7)=7");
        }

        [TestMethod]
        public void Test_PCEQB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0A << 6 | 0x28);
            AssertDisasm(enc, "pceqb");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 5; cpu.RegsLo[17] = 5; // same -> 0xFF
            Exec(cpu, enc);
            Assert.AreEqual(0xFFU, cpu.RegsLo[18] & 0xFF, "PCEQB: 5==5 -> 0xFF");
        }

        [TestMethod]
        public void Test_PSUBUW_Normal()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x11 << 6 | 0x28);
            AssertDisasm(enc, "psubuw");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFFFFFFFF, "PSUBUW: 10-7=3");
        }

        [TestMethod]
        public void Test_PSUBUW_Underflow()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x11 << 6 | 0x28);
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 3; cpu.RegsLo[17] = 7; // 3-7 underflows -> 0
            Exec(cpu, enc);
            Assert.AreEqual(0U, cpu.RegsLo[18] & 0xFFFFFFFF, "PSUBUW: 3-7 saturates to 0");
        }

        [TestMethod]
        public void Test_PADDUH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x14 << 6 | 0x28);
            AssertDisasm(enc, "padduh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(17U, cpu.RegsLo[18] & 0xFFFF, "PADDUH: 10+7=17");
        }

        [TestMethod]
        public void Test_PSUBUH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x15 << 6 | 0x28);
            AssertDisasm(enc, "psubuh");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFFFF, "PSUBUH: 10-7=3");
        }

        [TestMethod]
        public void Test_PSUBUB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x19 << 6 | 0x28);
            AssertDisasm(enc, "psubub");
            var cpu = MakeCpu(); // s0=10, s1=7
            Exec(cpu, enc);
            Assert.AreEqual(3U, cpu.RegsLo[18] & 0xFF, "PSUBUB: 10-7=3");
        }

        [TestMethod]
        public void Test_PADSBH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x04 << 6 | 0x28);
            AssertDisasm(enc, "padsbh");
            var cpu = MakeCpu();
            Exec(cpu, enc); // just verify no crash
        }

        [TestMethod]
        public void Test_PEXTUW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x12 << 6 | 0x28);
            AssertDisasm(enc, "pextuw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PEXTUH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x16 << 6 | 0x28);
            AssertDisasm(enc, "pextuh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PEXTUB()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1A << 6 | 0x28);
            AssertDisasm(enc, "pextub");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_QFSRV()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1B << 6 | 0x28);
            AssertDisasm(enc, "qfsrv");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        // =====================================================================
        // MMI shift/move instructions (opcode=0x1C, own funct codes)
        // =====================================================================

        public void Test_PSRLH()
        {
            // PSRLH $s2, $s0, 1  (rd=18, rt=16, sa=1, funct=0x36)
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 1 << 6 | 0x36);
            AssertDisasm(enc, "psrlh");
            var cpu = MakeCpu(); // s0=10
            Exec(cpu, enc);
            Assert.AreEqual(5U, cpu.RegsLo[18] & 0xFFFF, "PSRLH: 10>>1=5");
        }

        [TestMethod]
        public void Test_PSRAH()
        {
            // PSRAH $s2, $s0, 1  (rd=18, rt=16, sa=1, funct=0x37)
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 1 << 6 | 0x37);
            AssertDisasm(enc, "psrah");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0xFFFC; // lower 16 bits = -4 as signed short
            Exec(cpu, enc);
            // Arithmetic shift right: -4 >> 1 = -2 = 0xFFFE
            Assert.AreEqual(0xFFFEU, cpu.RegsLo[18] & 0xFFFF, "PSRAH: -4>>>1=-2 (arithmetic)");
        }

        [TestMethod]
        public void Test_PSLLW()
        {
            // PSLLW $s2, $s0, 1  (rd=18, rt=16, sa=1, funct=0x3C)
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 1 << 6 | 0x3C);
            AssertDisasm(enc, "psllw");
            var cpu = MakeCpu(); // s0=10
            Exec(cpu, enc);
            Assert.AreEqual(20U, cpu.RegsLo[18] & 0xFFFFFFFF, "PSLLW: 10<<1=20");
        }

        [TestMethod]
        public void Test_PSRLW()
        {
            // PSRLW $s2, $s0, 1  (rd=18, rt=16, sa=1, funct=0x3E)
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 1 << 6 | 0x3E);
            AssertDisasm(enc, "psrlw");
            var cpu = MakeCpu(); // s0=10
            Exec(cpu, enc);
            Assert.AreEqual(5U, cpu.RegsLo[18] & 0xFFFFFFFF, "PSRLW: 10>>1=5");
        }

        [TestMethod]
        public void Test_PSRAW()
        {
            // PSRAW $s2, $s0, 1  (rd=18, rt=16, sa=1, funct=0x3F)
            uint enc = (uint)(0x1C << 26 | 16 << 16 | 18 << 11 | 1 << 6 | 0x3F);
            AssertDisasm(enc, "psraw");
            var cpu = MakeCpu(); // s0=10
            Exec(cpu, enc);
            Assert.AreEqual(5U, cpu.RegsLo[18] & 0xFFFFFFFF, "PSRAW: 10>>>1=5");
        }

        // =====================================================================
        // MMI2 sub-instructions (funct=0x09, dispatched by shamt)
        // =====================================================================

        [TestMethod]
        public void Test_PMADDW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x00 << 6 | 0x09);
            AssertDisasm(enc, "pmaddw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PSLLVW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x02 << 6 | 0x09);
            AssertDisasm(enc, "psllvw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PSRLVW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x03 << 6 | 0x09);
            AssertDisasm(enc, "psrlvw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PMSUBW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x04 << 6 | 0x09);
            AssertDisasm(enc, "pmsubw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PINTH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0A << 6 | 0x09);
            AssertDisasm(enc, "pinth");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PMULTW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0C << 6 | 0x09);
            AssertDisasm(enc, "pmultw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PDIVW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0D << 6 | 0x09);
            AssertDisasm(enc, "pdivw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PMADDH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x10 << 6 | 0x09);
            AssertDisasm(enc, "pmaddh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PHMADH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x11 << 6 | 0x09);
            AssertDisasm(enc, "phmadh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        public void Test_PMSUBH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x14 << 6 | 0x09);
            AssertDisasm(enc, "pmsubh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PHMSBH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x15 << 6 | 0x09);
            AssertDisasm(enc, "phmsbh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PEXEH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1A << 6 | 0x09);
            AssertDisasm(enc, "pexeh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PREVH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1B << 6 | 0x09);
            AssertDisasm(enc, "prevh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PMULTH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1C << 6 | 0x09);
            AssertDisasm(enc, "pmulth");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PDIVBW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1D << 6 | 0x09);
            AssertDisasm(enc, "pdivbw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PEXEW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1E << 6 | 0x09);
            AssertDisasm(enc, "pexew");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PROT3W()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1F << 6 | 0x09);
            AssertDisasm(enc, "prot3w");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        // =====================================================================
        // MMI3 sub-instructions (funct=0x29, dispatched by shamt)
        // =====================================================================

        [TestMethod]
        public void Test_PMADDUW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x00 << 6 | 0x29);
            AssertDisasm(enc, "pmadduw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PSRAVW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x03 << 6 | 0x29);
            AssertDisasm(enc, "psravw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PINTEH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0A << 6 | 0x29);
            AssertDisasm(enc, "pinteh");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PMULTUW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0C << 6 | 0x29);
            AssertDisasm(enc, "pmultuw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PDIVUW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x0D << 6 | 0x29);
            AssertDisasm(enc, "pdivuw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PEXCH()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1A << 6 | 0x29);
            AssertDisasm(enc, "pexch");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        [TestMethod]
        public void Test_PEXCW()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 17 << 16 | 18 << 11 | 0x1E << 6 | 0x29);
            AssertDisasm(enc, "pexcw");
            var cpu = MakeCpu();
            Exec(cpu, enc);
        }

        // =====================================================================
        // VU0 coprocessor transfer instructions (COP2 rs variants)
        // =====================================================================

        [TestMethod]
        public void Test_QMFC2()
        {
            // QMFC2 $s0, VF1: GPR in rt (bits 20:16), VF register in rd (bits 15:11)
            uint enc = (uint)(0x12 << 26 | 0x01 << 21 | 16 << 16 | 1 << 11);
            Assert.AreEqual("qmfc2 s0, vf1", MipsDisassembler.Disassemble(enc));
            Assert.AreEqual("qmfc2.i s0, vf1", MipsDisassembler.Disassemble(enc | 1));
            var cpu = MakeCpu();
            cpu.VU0.WriteVF(1, new System.Numerics.Vector4(1.0f, 2.0f, 3.0f, 4.0f));
            Exec(cpu, enc);
            // X and Y are packed into RegsLo[16]
            uint xBits = System.BitConverter.SingleToUInt32Bits(1.0f);
            uint yBits = System.BitConverter.SingleToUInt32Bits(2.0f);
            ulong expected = (ulong)xBits | ((ulong)yBits << 32);
            Assert.AreEqual(expected, cpu.RegsLo[16], "QMFC2: RegsLo = VF1.xy bits");
        }

        [TestMethod]
        public void Test_CFC2()
        {
            // CFC2 $s0, vi16 (status register)  (rs=0x02, ctrl=16 in rt bits, rd=16 in rd bits)
            uint enc = (uint)(0x12 << 26 | 0x02 << 21 | 16 << 16 | 16 << 11);
            AssertDisasm(enc, "cfc2");
            var cpu = MakeCpu();
            cpu.VU0.StatusFlags = 0x5;
            Exec(cpu, enc);
            Assert.AreEqual(5UL, cpu.RegsLo[16], "CFC2: read status flags=5");
        }

        [TestMethod]
        public void Test_QMTC2()
        {
            // QMTC2 VF2, $s0  (rs=0x05, rt=16 in rt bits, vfd=2 in rd bits)
            // Encoding: (0x12<<26 | 0x05<<21 | gpr_rt<<16 | vfd<<11)
            uint enc = (uint)(0x12 << 26 | 0x05 << 21 | 16 << 16 | 2 << 11);
            AssertDisasm(enc, "qmtc2");
            var cpu = MakeCpu();
            uint xBits = System.BitConverter.SingleToUInt32Bits(5.0f);
            cpu.RegsLo[16] = xBits; // X in low 32 bits
            Exec(cpu, enc);
            var v = cpu.VU0.ReadVF(2);
            Assert.AreEqual(5.0f, v.X, 0.001f, "QMTC2: VF2.X = 5.0");
        }

        [TestMethod]
        public void Test_CTC2()
        {
            // CTC2 vi16, $s0  (rs=0x06, rt=16 in rt bits, rd=16 in rd bits)
            uint enc = (uint)(0x12 << 26 | 0x06 << 21 | 16 << 16 | 16 << 11);
            AssertDisasm(enc, "ctc2");
            var cpu = MakeCpu();
            cpu.RegsLo[16] = 0xAB;
            Exec(cpu, enc);
            Assert.AreEqual(0xABU, cpu.VU0.StatusFlags, "CTC2: status flags=0xAB");
        }

    }

    // =====================================================================
    // Bug fix verification tests
    // =====================================================================

    [TestClass]
    public class TestBugFixes
    {
        private static CpuState MakeCpu()
        {
            var cpu = new CpuState();
            cpu.PC = 0x80000000;
            return cpu;
        }

        private static void Exec(CpuState cpu, uint encoded)
        {
            var (obj, args) = MipsDisassembler.GetInstructionObject(encoded);
            Assert.IsNotNull(obj,  $"No instruction for 0x{encoded:X8}");
            Assert.IsNotNull(args, $"No args for 0x{encoded:X8}");
            obj!.ExecuteGeneric(cpu, args!);
        }

        // BUG 2: MULT — EE sign-extends 32-bit halves to 64-bit LO/HI
        [TestMethod]
        public void Test_MULT_NegativeSignExtend()
        {
            // mult $t0, $t1 with negative result: (-5) * 3 = -15
            // opcode=0, rs=8, rt=9, funct=0x18
            uint enc = (uint)(8 << 21 | 9 << 16 | 0x18);
            var cpu = MakeCpu();
            cpu.RegsLo[8] = unchecked((ulong)(long)-5); // -5
            cpu.RegsLo[9] = 3;
            Exec(cpu, enc);
            // -15 = 0xFFFFFFF1, sign-extended to 64-bit = 0xFFFFFFFFFFFFFFF1
            Assert.AreEqual(0xFFFFFFFFFFFFFFF1UL, cpu.LO, "MULT negative: LO should be sign-extended -15");
            Assert.AreEqual(0xFFFFFFFFFFFFFFFFUL, cpu.HI, "MULT negative: HI should be sign-extended -1 (upper half of -15 * 1)");
        }

        // BUG 2: MULTU — EE sign-extends 32-bit halves to 64-bit LO/HI
        [TestMethod]
        public void Test_MULTU_LargeSignExtend()
        {
            // multu with large result where lower half has bit 31 set
            // 0x80000001 * 1 = 0x80000001 (bit 31 set → sign-extend to 0xFFFFFFFF80000001)
            uint enc = (uint)(8 << 21 | 9 << 16 | 0x19);
            var cpu = MakeCpu();
            cpu.RegsLo[8] = 0x80000001; // large unsigned
            cpu.RegsLo[9] = 1;
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFFFFF80000001UL, cpu.LO, "MULTU: LO sign-extended from 32 bits");
            Assert.AreEqual(0UL, cpu.HI, "MULTU: HI should be 0");
        }

        // BUG 3: DIV — EE sign-extends quotient/remainder to 64-bit
        [TestMethod]
        public void Test_DIV_NegativeSignExtend()
        {
            // div: -7 / 2 = quotient=-3, remainder=-1
            uint enc = (uint)(8 << 21 | 9 << 16 | 0x1A);
            var cpu = MakeCpu();
            cpu.RegsLo[8] = unchecked((ulong)(long)-7); // -7
            cpu.RegsLo[9] = 2;
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFFFFFFFFFFFFDUL, cpu.LO, "DIV: quotient -3 sign-extended");
            Assert.AreEqual(0xFFFFFFFFFFFFFFFFUL, cpu.HI, "DIV: remainder -1 sign-extended");
        }

        // BUG 9: BC1F — PC should advance when branch not taken
        [TestMethod]
        public void Test_BC1F_NotTaken()
        {
            // bc1f offset=4 — CC=true so branch not taken, PC should advance by 4
            uint enc = (uint)(0x11 << 26 | 0x08 << 21 | 0x00 << 16 | 4);
            var cpu = MakeCpu();
            cpu.COP1.SetConditionCode(true); // branch not taken (BC1F branches when false)
            cpu.PC = 0x80000000;
            Exec(cpu, enc);
            Assert.AreEqual(0x80000004UL, cpu.PC, "BC1F not taken: PC should advance by 4");
        }

        // BUG 9: BC1T — PC should advance when branch not taken
        [TestMethod]
        public void Test_BC1T_NotTaken()
        {
            uint enc = (uint)(0x11 << 26 | 0x08 << 21 | 0x01 << 16 | 4);
            var cpu = MakeCpu();
            cpu.COP1.SetConditionCode(false); // branch not taken (BC1T branches when true)
            cpu.PC = 0x80000000;
            Exec(cpu, enc);
            Assert.AreEqual(0x80000004UL, cpu.PC, "BC1T not taken: PC should advance by 4");
        }

        // BUG 10: ERET — should use ErrorEPC when ERL is set
        [TestMethod]
        public void Test_ERET_ERL()
        {
            uint enc = (uint)(0x10 << 26 | 0x10 << 21 | 0x18);
            var cpu = MakeCpu();
            // Set ERL bit (bit 2) in Status register
            uint status = cpu.COP0.ReadRegister32(12);
            cpu.COP0.WriteRegister32(12, status | (1u << 2));
            // Set ErrorEPC (reg 30) to known address
            cpu.COP0.WriteRegister64(30, 0x80001234);
            Exec(cpu, enc);
            Assert.AreEqual(0x80001234UL, cpu.PC, "ERET with ERL: should restore from ErrorEPC");
            // ERL bit should be cleared
            Assert.AreEqual(0u, cpu.COP0.ReadRegister32(12) & (1u << 2), "ERET with ERL: ERL bit should be cleared");
        }

        // BUG 11: MFHI — RegsHi sign-extended from HI bit 63
        [TestMethod]
        public void Test_MFHI_SignExtend()
        {
            uint enc = (uint)(8 << 11 | 0x10); // mfhi $t0
            var cpu = MakeCpu();
            cpu.HI = 0x8000000000000000UL; // bit 63 set → negative
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFFFFFFFFFFFFFUL, cpu.RegsHi[8], "MFHI: RegsHi should be sign-extended -1");
        }

        // BUG 11: MFLO — RegsHi sign-extended from LO bit 63
        [TestMethod]
        public void Test_MFLO_SignExtend()
        {
            uint enc = (uint)(8 << 11 | 0x12); // mflo $t0
            var cpu = MakeCpu();
            cpu.LO = 0xFFFFFFFFFFFFFFFFUL;
            Exec(cpu, enc);
            Assert.AreEqual(0xFFFFFFFFFFFFFFFFUL, cpu.RegsHi[8], "MFLO: RegsHi should be sign-extended -1");
        }

        // BUG 14: DSRA — RegsHi sign-extended for negative result
        [TestMethod]
        public void Test_DSRA_NegativeSignExtend()
        {
            // dsra $t0, $t1, 1 — shift -4 right by 1 = -2
            uint enc = (uint)(9 << 16 | 8 << 11 | 1 << 6 | 0x3B);
            var cpu = MakeCpu();
            cpu.RegsLo[9] = unchecked((ulong)(long)-4); // -4
            Exec(cpu, enc);
            Assert.AreEqual(unchecked((ulong)(long)-2), cpu.RegsLo[8], "DSRA: -4>>1 = -2");
            Assert.AreEqual(0xFFFFFFFFFFFFFFFFUL, cpu.RegsHi[8], "DSRA: RegsHi sign-extended for negative");
        }

        // FPU: Test proper register field mapping (fd=shamt, fs=rd, ft=rt)
        [TestMethod]
        public void Test_FPU_RegisterFieldMapping()
        {
            // add.s $f5, $f3, $f4 => fd=5(shamt), fs=3(rd), ft=4(rt)
            uint enc = (uint)((0x11 << 26) | (0x10 << 21) | (4 << 16) | (3 << 11) | (5 << 6) | 0x00);
            var cpu = MakeCpu();
            cpu.FPR[3] = 10.0f; // fs
            cpu.FPR[4] = 5.0f;  // ft
            Exec(cpu, enc);
            Assert.AreEqual(15.0f, cpu.FPR[5], 0.001f, "ADD.S: fd=f5 should get fs+ft=15");
            // Source registers should be unchanged
            Assert.AreEqual(10.0f, cpu.FPR[3], 0.001f, "ADD.S: fs=f3 should be unchanged");
            Assert.AreEqual(5.0f, cpu.FPR[4], 0.001f, "ADD.S: ft=f4 should be unchanged");
        }

    }
}
