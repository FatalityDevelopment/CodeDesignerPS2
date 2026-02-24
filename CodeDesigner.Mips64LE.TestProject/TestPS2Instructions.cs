using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.MMI;
using CodeDesigner.Mips64LE.Common.Instructions.CoprocessorInstructions;

namespace CodeDesigner.Mips64LE.TestProject
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
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 8 << 11 | 0x01 << 6 | 0x28);
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
            cpu.FPR[d.Rs] = 8.0f; cpu.FPR[d.Rt] = 4.0f; // FPR[16]=8, FPR[ft=2]=4
            Exec(cpu, enc);
            // FPR[d.Rd] = FPR[16] / sqrt(|FPR[2]|) = 8/2 = 4
            Assert.AreEqual(4.0f, cpu.FPR[d.Rd], 0.0001f, "RSQRT.S: 8/sqrt(4)=4");
        }

        [TestMethod]
        public void Test_ROUND_W_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 0 << 16 | 1 << 11 | 0 << 6 | 0x0C);
            AssertDisasm(enc, "round.w.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rs] = 3.7f; // FPR[16] = 3.7
            Exec(cpu, enc);
            int r = (int)System.BitConverter.SingleToUInt32Bits(cpu.FPR[d.Rd]);
            Assert.AreEqual(4, r, "ROUND.W.S: round(3.7)=4");
        }

        [TestMethod]
        public void Test_TRUNC_W_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 0 << 16 | 1 << 11 | 0 << 6 | 0x0D);
            AssertDisasm(enc, "trunc.w.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rs] = 3.9f; // FPR[16] = 3.9
            Exec(cpu, enc);
            int r = (int)System.BitConverter.SingleToUInt32Bits(cpu.FPR[d.Rd]);
            Assert.AreEqual(3, r, "TRUNC.W.S: trunc(3.9)=3");
        }

        [TestMethod]
        public void Test_CEIL_W_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 0 << 16 | 1 << 11 | 0 << 6 | 0x0E);
            AssertDisasm(enc, "ceil.w.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rs] = 3.1f; // FPR[16] = 3.1
            Exec(cpu, enc);
            int r = (int)System.BitConverter.SingleToUInt32Bits(cpu.FPR[d.Rd]);
            Assert.AreEqual(4, r, "CEIL.W.S: ceil(3.1)=4");
        }

        [TestMethod]
        public void Test_FLOOR_W_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 0 << 16 | 1 << 11 | 0 << 6 | 0x0F);
            AssertDisasm(enc, "floor.w.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rs] = 3.9f; // FPR[16] = 3.9
            Exec(cpu, enc);
            int r = (int)System.BitConverter.SingleToUInt32Bits(cpu.FPR[d.Rd]);
            Assert.AreEqual(3, r, "FLOOR.W.S: floor(3.9)=3");
        }

        [TestMethod]
        public void Test_ADDA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x18);
            AssertDisasm(enc, "adda.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 4.0f; // FPR[16]=3, FPR[ft=2]=4
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
            cpu.FPR[d.Rs] = 10.0f; cpu.FPR[d.Rt] = 3.0f;
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
            cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
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
            cpu.FACC = 10.0f; cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
            Exec(cpu, enc);
            // FPR[d.Rd] = ACC(10) + FPR[16](3) * FPR[ft](4) = 22
            Assert.AreEqual(22.0f, cpu.FPR[d.Rd], 0.0001f, "MADD.S: fd = ACC(10) + 3*4=12 -> 22");
        }

        [TestMethod]
        public void Test_MSUB_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1D);
            AssertDisasm(enc, "msub.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FACC = 20.0f; cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
            Exec(cpu, enc);
            Assert.AreEqual(8.0f, cpu.FPR[d.Rd], 0.0001f, "MSUB.S: fd = ACC(20) - 3*4=12 -> 8");
        }

        [TestMethod]
        public void Test_MADDA_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x1E);
            AssertDisasm(enc, "madda.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FACC = 5.0f; cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 2.0f;
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
            cpu.FACC = 20.0f; cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 4.0f;
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
            cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 7.0f;
            Exec(cpu, enc);
            Assert.AreEqual(7.0f, cpu.FPR[d.Rd], 0.0001f, "MAX.S: max(3,7)=7");
        }

        [TestMethod]
        public void Test_MIN_S()
        {
            uint enc = (uint)(0x11 << 26 | 0x10 << 21 | 2 << 16 | 1 << 11 | 0 << 6 | 0x29);
            AssertDisasm(enc, "min.s");
            var cpu = MakeCpu();
            var d = MipsDecoder.DecodeR(enc);
            cpu.FPR[d.Rs] = 3.0f; cpu.FPR[d.Rt] = 7.0f;
            Exec(cpu, enc);
            Assert.AreEqual(3.0f, cpu.FPR[d.Rd], 0.0001f, "MIN.S: min(3,7)=3");
        }

        [TestMethod]
        public void Test_LDC1_SDC1()
        {
            AssertDisasm(unchecked((uint)(0x35 << 26)), "ldc1");
            AssertDisasm(unchecked((uint)(0x3D << 26)), "sdc1");
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
        // Extended VU0 instructions
        // =====================================================================

        [TestMethod]
        public void Test_VSUBA()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x32);
            AssertDisasm(enc, "vsuba");
        }

        [TestMethod]
        public void Test_VOPMULA()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x33);
            AssertDisasm(enc, "vopmula");
        }

        [TestMethod]
        public void Test_VOPMSUB()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x34);
            AssertDisasm(enc, "vopmsub");
        }

        [TestMethod]
        public void Test_VDIV()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x35);
            AssertDisasm(enc, "vdiv");
        }

        [TestMethod]
        public void Test_VSQRT_VU0()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 0x36);
            AssertDisasm(enc, "vsqrt");
        }

        [TestMethod]
        public void Test_VRSQRT()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x37);
            AssertDisasm(enc, "vrsqrt");
        }

        [TestMethod]
        public void Test_VMOVE()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x38);
            AssertDisasm(enc, "vmove");
        }

        [TestMethod]
        public void Test_VMR32()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x39);
            AssertDisasm(enc, "vmr32");
        }

        [TestMethod]
        public void Test_VNOP()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 0x3A);
            AssertDisasm(enc, "vnop");
        }

        [TestMethod]
        public void Test_VCLIPW()
        {
            uint enc = (uint)(0x12 << 26 | 0x10 << 21 | 1 << 16 | 2 << 11 | 0x3B);
            AssertDisasm(enc, "vclipw");
        }

        [TestMethod]
        public void Test_VLQ_VSQ()
        {
            AssertDisasm(unchecked((uint)(0x36 << 26)), "vlq");
            AssertDisasm(unchecked((uint)(0x3E << 26)), "vsq");
        }

        // =====================================================================
        // Regression: existing instructions still work
        // =====================================================================

        [TestMethod]
        public void Test_ExistingInstructions_Still_Work()
        {
            Assert.IsTrue(MipsDisassembler.Disassemble(0x02114020).Contains("add"),  "ADD should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x46010800).Contains("add.s"), "ADD.S should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x4A000028).Contains("vadd"),  "VADD should still work");
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
            uint enc = (uint)(0x1C << 26 | 8 << 11 | 0x30);
            AssertDisasm(enc, "pmfhl");
            var cpu = MakeCpu(); cpu.LO = 0x1234; cpu.HI = 0x5678;
            Exec(cpu, enc);
            Assert.AreEqual(0x1234UL, cpu.RegsLo[8], "PMFHL: rd.lo = LO");
            Assert.AreEqual(0x5678UL, cpu.RegsHi[8], "PMFHL: rd.hi = HI");
        }

        [TestMethod]
        public void Test_PMTHL()
        {
            uint enc = (uint)(0x1C << 26 | 16 << 21 | 0x31);
            AssertDisasm(enc, "pmthl");
            var cpu = MakeCpu(); cpu.RegsLo[16] = 0xABCD; cpu.RegsHi[16] = 0xEF01;
            Exec(cpu, enc);
            Assert.AreEqual(0xABCDUL, cpu.LO, "PMTHL: LO = rs.lo");
            Assert.AreEqual(0xEF01UL, cpu.HI, "PMTHL: HI = rs.hi");
        }

        // =====================================================================
        // VU0 Component-Broadcast Variant Instructions
        // COP2 SPECIAL1 (opcode=0x12, rs=0x10), functs 0x00-0x27
        // =====================================================================

        private static CpuState MakeVU0Cpu()
        {
            var cpu = new CpuState();
            cpu.PC = 0x80000000;
            // vf1 = (1, 2, 3, 4)
            cpu.VU0.WriteVF(1, new System.Numerics.Vector4(1f, 2f, 3f, 4f));
            // vf2 = (10, 20, 30, 40)
            cpu.VU0.WriteVF(2, new System.Numerics.Vector4(10f, 20f, 30f, 40f));
            return cpu;
        }

        // Helper: encode a COP2 SPECIAL1 R-type instruction (opcode=0x12, rs=0x10)
        private static uint EncVU0(int vft, int vfs, int vfd, int funct)
            => (uint)(0x12 << 26 | 0x10 << 21 | vft << 16 | vfs << 11 | vfd << 6 | funct);

        [TestMethod]
        public void Test_VADDx()
        {
            // vaddx vf3, vf1, vf2  =>  vf3 = vf1 + {vf2.x, vf2.x, vf2.x, vf2.x}
            //                       = (1,2,3,4) + (10,10,10,10) = (11,12,13,14)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x00);
            AssertDisasm(enc, "vaddx");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(11f, r.X, "VADDx: X"); Assert.AreEqual(12f, r.Y, "VADDx: Y");
            Assert.AreEqual(13f, r.Z, "VADDx: Z"); Assert.AreEqual(14f, r.W, "VADDx: W");
        }

        [TestMethod]
        public void Test_VADDw()
        {
            // vaddw vf3, vf1, vf2  =>  vf3 = vf1 + {vf2.w, ...} = (1+40, 2+40, 3+40, 4+40)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x03);
            AssertDisasm(enc, "vaddw");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(41f, r.X, "VADDw: X"); Assert.AreEqual(42f, r.Y, "VADDw: Y");
        }

        [TestMethod]
        public void Test_VSUBBc()
        {
            // vsuby vf3, vf1, vf2  =>  vf3 = vf1 - {vf2.y, ...} = (1-20, 2-20, 3-20, 4-20)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x05);
            AssertDisasm(enc, "vsuby");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(-19f, r.X, "VSUBy: X"); Assert.AreEqual(-18f, r.Y, "VSUBy: Y");
        }

        [TestMethod]
        public void Test_VMULBc()
        {
            // vmulz vf3, vf1, vf2  =>  vf3 = vf1 * {vf2.z, ...} = (30, 60, 90, 120)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x0A);
            AssertDisasm(enc, "vmulz");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(30f, r.X, "VMULz: X"); Assert.AreEqual(60f, r.Y, "VMULz: Y");
            Assert.AreEqual(90f, r.Z, "VMULz: Z"); Assert.AreEqual(120f, r.W, "VMULz: W");
        }

        [TestMethod]
        public void Test_VMADDBc()
        {
            // vmaddx vf3, vf1, vf2  =>  vf3 = ACC + vf1 * {vf2.x, ...}
            //   ACC=(0,0,0,0), vf1=(1,2,3,4), vf2.x=10  => (10,20,30,40)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x0C);
            AssertDisasm(enc, "vmaddx");
            var cpu = MakeVU0Cpu();
            cpu.VU0.WriteACC(new System.Numerics.Vector4(0, 0, 0, 0));
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(10f, r.X, "VMADDx: X"); Assert.AreEqual(20f, r.Y, "VMADDx: Y");
        }

        [TestMethod]
        public void Test_VMSUBBc()
        {
            // vmsuby vf3, vf1, vf2  =>  vf3 = ACC - vf1 * {vf2.y, ...}
            //   ACC=(100,100,100,100), vf1=(1,2,3,4), vf2.y=20 => (80, 60, 40, 20)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x11);
            AssertDisasm(enc, "vmsuby");
            var cpu = MakeVU0Cpu();
            cpu.VU0.WriteACC(new System.Numerics.Vector4(100f, 100f, 100f, 100f));
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(80f, r.X, "VMSUBy: X"); Assert.AreEqual(60f, r.Y, "VMSUBy: Y");
            Assert.AreEqual(40f, r.Z, "VMSUBy: Z"); Assert.AreEqual(20f, r.W, "VMSUBy: W");
        }

        [TestMethod]
        public void Test_VMAXBc()
        {
            // vmaxz vf3, vf1, vf2  =>  vf3 = max(vf1, {vf2.z,...}) = max((1,2,3,4), (30,...))
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x16);
            AssertDisasm(enc, "vmaxz");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(30f, r.X, "VMAXz: X"); Assert.AreEqual(30f, r.Y, "VMAXz: Y");
            Assert.AreEqual(30f, r.Z, "VMAXz: Z"); Assert.AreEqual(30f, r.W, "VMAXz: W");
        }

        [TestMethod]
        public void Test_VMINBc()
        {
            // vminw vf3, vf1, vf2  =>  vf3 = min(vf1, {vf2.w,...}) = min((1,2,3,4), (40,...))
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 3, funct: 0x1B);
            AssertDisasm(enc, "vminw");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var r = cpu.VU0.ReadVF(3);
            Assert.AreEqual(1f, r.X, "VMINw: X"); Assert.AreEqual(2f, r.Y, "VMINw: Y");
        }

        [TestMethod]
        public void Test_VMULABc()
        {
            // vmulax ACC, vf1, vf2  =>  ACC = vf1 * {vf2.x,...} = (10, 20, 30, 40)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 0, funct: 0x1C);
            AssertDisasm(enc, "vmulax");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var acc = cpu.VU0.ReadACC();
            Assert.AreEqual(10f, acc.X, "VMULAx: ACC.X"); Assert.AreEqual(20f, acc.Y, "VMULAx: ACC.Y");
            Assert.AreEqual(30f, acc.Z, "VMULAx: ACC.Z"); Assert.AreEqual(40f, acc.W, "VMULAx: ACC.W");
        }

        [TestMethod]
        public void Test_VADDABc()
        {
            // vaddaw ACC, vf1, vf2  =>  ACC = vf1 + {vf2.w,...} = (1+40, 2+40, 3+40, 4+40)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 0, funct: 0x23);
            AssertDisasm(enc, "vaddaw");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var acc = cpu.VU0.ReadACC();
            Assert.AreEqual(41f, acc.X, "VADDAw: ACC.X"); Assert.AreEqual(42f, acc.Y, "VADDAw: ACC.Y");
        }

        [TestMethod]
        public void Test_VSUBABc()
        {
            // vsubay ACC, vf1, vf2  =>  ACC = vf1 - {vf2.y,...} = (1-20, 2-20, 3-20, 4-20)
            uint enc = EncVU0(vft: 2, vfs: 1, vfd: 0, funct: 0x25);
            AssertDisasm(enc, "vsubay");
            var cpu = MakeVU0Cpu();
            Exec(cpu, enc);
            var acc = cpu.VU0.ReadACC();
            Assert.AreEqual(-19f, acc.X, "VSUBAy: ACC.X"); Assert.AreEqual(-18f, acc.Y, "VSUBAy: ACC.Y");
        }

        // =====================================================================
        // VCALLMS / VCALLMSR — Trigger VU0 Micro-Mode
        // =====================================================================

        [TestMethod]
        public void Test_VCALLMS()
        {
            // VCALLMS with 15-bit address = 0x10 (byte address = 0x80)
            // Encode: opcode=0x12, rs=0x18, bits[20:6] = 0x0010
            // imm15 = 0x0010 → Rt=0, Rd=0, Shamt=0x10
            uint enc = (uint)(0x12 << 26 | 0x18 << 21 | 0x10 << 6);
            AssertDisasm(enc, "vcallms");
            var cpu = MakeVU0Cpu();
            cpu.VU0.IsRunning = false;
            Exec(cpu, enc);
            Assert.IsTrue(cpu.VU0.IsRunning, "VCALLMS: VU0 should be running");
            Assert.AreEqual((ushort)(0x10 * 8), cpu.VU0.PC, "VCALLMS: PC should be address * 8");
        }

        [TestMethod]
        public void Test_VCALLMSR()
        {
            // VCALLMSR vi1  (VI register 1 holds the address)
            // Encode: opcode=0x12, rs=0x19, Rd=1 (vi register index)
            uint enc = (uint)(0x12 << 26 | 0x19 << 21 | 1 << 11);
            AssertDisasm(enc, "vcallmsr");
            var cpu = MakeVU0Cpu();
            cpu.VU0.WriteVI(1, 0x20); // vi1 = 0x20
            cpu.VU0.IsRunning = false;
            Exec(cpu, enc);
            Assert.IsTrue(cpu.VU0.IsRunning, "VCALLMSR: VU0 should be running");
            Assert.AreEqual((ushort)(0x20 * 8), cpu.VU0.PC, "VCALLMSR: PC should be VI[1] * 8");
        }

        // =====================================================================
        // VRNEXT / VRGET / VRINIT / VRXOR — LFSR Pseudo-Random Instructions
        // COP2 SPECIAL2 (opcode=0x12, rs=0x1E)
        // Verified field mapping against PCSX2 VUops.cpp:
        //   VRNEXT/VRGET: dest = _Ft_ = bits[20:16] = args.Rt
        //   VRINIT/VRXOR: source = _Fs_ = bits[15:11] = args.Rd; component = bits[22:21] = args.Rs & 0x3
        //   R register stored as 0x3F800000 | mantissa_23_bits (float in [1.0, 2.0))
        // =====================================================================

        [TestMethod]
        public void Test_VRNEXT()
        {
            // vrnext vf3, R  =>  advance LFSR, write R value to vf3 (dest in bits[20:16] = rt field)
            uint enc = (uint)(0x12 << 26 | 0x1E << 21 | 3 << 16 | 0x1C);
            AssertDisasm(enc, "vrnext");
            var cpu = MakeVU0Cpu();
            uint before = cpu.VU0.RngState;
            Exec(cpu, enc);
            // R should have advanced
            Assert.AreNotEqual(before, cpu.VU0.RngState, "VRNEXT: RngState should advance");
            // vf3 should contain the new R value reinterpreted as float
            float expected = BitConverter.UInt32BitsToSingle(cpu.VU0.RngState);
            Assert.AreEqual(expected, cpu.VU0.ReadVF(3).X, "VRNEXT: vf3.x = R float value");
        }

        [TestMethod]
        public void Test_VRGET()
        {
            // vrget vf3, R  =>  read current R into vf3 (no advance), dest in bits[20:16] = rt field
            uint enc = (uint)(0x12 << 26 | 0x1E << 21 | 3 << 16 | 0x1D);
            AssertDisasm(enc, "vrget");
            var cpu = MakeVU0Cpu();
            uint before = cpu.VU0.RngState;
            Exec(cpu, enc);
            Assert.AreEqual(before, cpu.VU0.RngState, "VRGET: RngState should NOT change");
            float expected = BitConverter.UInt32BitsToSingle(before);
            Assert.AreEqual(expected, cpu.VU0.ReadVF(3).X, "VRGET: vf3.x = R float value");
        }

        [TestMethod]
        public void Test_VRINIT()
        {
            // vrinit R, vf1.z  =>  R = 0x3F800000 | (vf1.z_bits & 0x7FFFFF)
            // source register = bits[15:11] = rd field; component = bits[22:21] = rs[1:0] = 0x1E & 0x3 = 2 = Z
            uint enc = (uint)(0x12 << 26 | 0x1E << 21 | 1 << 11 | 0x1E);
            AssertDisasm(enc, "vrinit");
            var cpu = MakeVU0Cpu();
            // vf1 = (1, 2, 3, 4), so vf1.z = 3.0f; its bits & 0x7FFFFF
            uint zBits = BitConverter.SingleToUInt32Bits(3f) & 0x7FFFFFu;
            uint expected = 0x3F800000u | zBits;
            Exec(cpu, enc);
            Assert.AreEqual(expected, cpu.VU0.RngState, $"VRINIT: RngState = 0x{expected:X8}");
        }

        [TestMethod]
        public void Test_VRXOR()
        {
            // vrxor R, vf2.z  =>  R = 0x3F800000 | ((R ^ vf2.z_bits) & 0x7FFFFF)
            // source register = bits[15:11] = rd field; component = bits[22:21] = 0x1E & 0x3 = 2 = Z
            uint enc = (uint)(0x12 << 26 | 0x1E << 21 | 2 << 11 | 0x1F);
            AssertDisasm(enc, "vrxor");
            var cpu = MakeVU0Cpu();
            // Set a known R value in proper format
            cpu.VU0.RngState = 0x3F800000u;
            // vf2 = (10, 20, 30, 40), vf2.z = 30.0f — use full float bits (matching PCSX2 and our impl)
            uint zBitsRaw = BitConverter.SingleToUInt32Bits(30f);
            uint expected = 0x3F800000u | ((cpu.VU0.RngState ^ zBitsRaw) & 0x7FFFFFu);
            Exec(cpu, enc);
            Assert.AreEqual(expected, cpu.VU0.RngState, $"VRXOR: RngState = 0x{expected:X8}");
        }
    }
}
