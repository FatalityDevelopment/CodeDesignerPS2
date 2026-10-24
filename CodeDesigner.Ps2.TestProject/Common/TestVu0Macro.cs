using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// VU0 macro mode (COP2 with the CO bit set). Encodings follow the layout
    /// <c>010010 1 dest ft fs fd funct</c> and PCSX2's SPECIAL1 / SPECIAL2 tables
    /// (R5900OpcodeTables.cpp); SPECIAL2 is indexed by <c>(fd &lt;&lt; 2) | (funct &amp; 3)</c>.
    /// </summary>
    [TestClass]
    public class TestVu0Macro
    {
        [DataTestMethod]
        [DataRow(0x4A0003BFu, "vwaitq")]
        [DataRow(0x4A0002FFu, "vnop")]
        [DataRow(0x4A000038u, "vcallms 0x0")]
        [DataRow(0x4A00D839u, "vcallmsr vi27")]
        [DataRow(0x4BE31068u, "vadd.xyzw vf1, vf2, vf3")]
        [DataRow(0x4B031068u, "vadd.x vf1, vf2, vf3")]
        [DataRow(0x4BE310C0u, "vaddx.xyzw vf3, vf2, vf3x")]
        [DataRow(0x4BC0105Cu, "vmulq.xyz vf1, vf2, Q")]
        [DataRow(0x4A8203BDu, "vsqrt Q, vf2y")]
        [DataRow(0x4A820BBCu, "vdiv Q, vf1x, vf2y")]
        [DataRow(0x4BE1133Cu, "vmove.xyzw vf1, vf2")]
        [DataRow(0x4A20143Eu, "vrinit R, vf2y")]
        [DataRow(0x4BE3103Cu, "vaddax.xyzw ACC, vf2, vf3x")]
        [DataRow(0x4BE1137Cu, "vlqi.xyzw vf1, (vi2++)")]
        [DataRow(0x4A0110F0u, "viadd vi3, vi2, vi1")]
        [DataRow(0x4A0117F2u, "viaddi vi1, vi2, -1")]
        [DataRow(0x4BC209FFu, "vclipw.xyz vf1, vf2w")]
        [DataRow(0x4BC20AFEu, "vopmula.xyz ACC, vf1, vf2")]
        [DataRow(0x4BE1113Du, "vitof4.xyzw vf1, vf2")]
        [DataRow(0x48281800u, "qmfc2 t0, vf3")]
        [DataRow(0x48281801u, "qmfc2.i t0, vf3")]
        [DataRow(0x48A81800u, "qmtc2 t0, vf3")]
        [DataRow(0x4848D800u, "cfc2 t0, CMSAR0")]
        [DataRow(0x48C88000u, "ctc2 t0, Status")]
        [DataRow(0xD9010000u, "lqc2 vf1, $0000(t0)")]
        public void Disassembles_AndRoundTrips(uint word, string expected)
        {
            Assert.AreEqual(expected, MipsDisassembler.Disassemble(word));
            Assert.AreEqual(word, MipsAssembler.Assemble(expected), $"assemble '{expected}'");
        }

        [TestMethod]
        public void Bc2_BranchesDecodeWithTarget()
        {
            Assert.AreEqual("bc2f $00100014", MipsDisassembler.Disassemble(0x49000004, 0x00100000));
            Assert.AreEqual("bc2tl $00100014", MipsDisassembler.Disassemble(0x49030004, 0x00100000));
        }

        private static CpuState Run(uint word, Action<CpuState> setup)
        {
            var cpu = new CpuState();
            setup(cpu);
            var (obj, args) = MipsDisassembler.GetInstructionObject(word);
            Assert.IsNotNull(obj, $"0x{word:X8} must decode");
            obj!.ExecuteGeneric(cpu, args!);
            return cpu;
        }

        [TestMethod]
        public void Vadd_WritesOnlyDestComponents()
        {
            var cpu = Run(0x4B031068, c => // vadd.x vf1, vf2, vf3
            {
                c.VU0.WriteVF(1, new Vector4(-1, -1, -1, -1));
                c.VU0.WriteVF(2, new Vector4(1, 2, 3, 4));
                c.VU0.WriteVF(3, new Vector4(10, 20, 30, 40));
            });
            Assert.AreEqual(new Vector4(11, -1, -1, -1), cpu.VU0.ReadVF(1));
        }

        [TestMethod]
        public void VmulBroadcast_UsesOneFtComponent()
        {
            var cpu = Run(0x4BE31058, c => // vmulx.xyzw vf1, vf2, vf3x
            {
                c.VU0.WriteVF(2, new Vector4(1, 2, 3, 4));
                c.VU0.WriteVF(3, new Vector4(10, 20, 30, 40));
            });
            Assert.AreEqual(new Vector4(10, 20, 30, 40), cpu.VU0.ReadVF(1));
        }

        [TestMethod]
        public void Vdiv_WritesQ()
        {
            var cpu = Run(0x4A820BBC, c => // vdiv Q, vf1x, vf2y
            {
                c.VU0.WriteVF(1, new Vector4(12, 0, 0, 0));
                c.VU0.WriteVF(2, new Vector4(0, 4, 0, 0));
            });
            Assert.AreEqual(3f, cpu.VU0.Q, 1e-6f);
        }

        [TestMethod]
        public void Vitof4_And_Vftoi4_ConvertFixedPoint()
        {
            var itof = Run(0x4BE1113D, c => // vitof4.xyzw vf1, vf2
                c.VU0.WriteVF(2, new Vector4(BitConverter.Int32BitsToSingle(64), 0, 0, 0)));
            Assert.AreEqual(4f, itof.VU0.ReadVF(1).X, 1e-6f);

            var ftoi = Run(0x4BE1117D, c => // vftoi4.xyzw vf1, vf2
                c.VU0.WriteVF(2, new Vector4(2, 0, 0, 0)));
            Assert.AreEqual(32, BitConverter.SingleToInt32Bits(ftoi.VU0.ReadVF(1).X));
        }

        [TestMethod]
        public void Vopmula_Vopmsub_ComputeCrossProduct()
        {
            var cpu = Run(0x4BC20AFE, c => // vopmula.xyz ACC, vf1, vf2
            {
                c.VU0.WriteVF(1, new Vector4(1, 0, 0, 0));
                c.VU0.WriteVF(2, new Vector4(0, 1, 0, 0));
            });
            // ACC = (fs.y*ft.z, fs.z*ft.x, fs.x*ft.y) = (0, 0, 1)
            Assert.AreEqual(new Vector3(0, 0, 1), new Vector3(cpu.VU0.ACC.X, cpu.VU0.ACC.Y, cpu.VU0.ACC.Z));
        }

        [TestMethod]
        public void Viaddi_AddsSignedImmediate()
        {
            var cpu = Run(0x4A0117F2, c => c.VU0.WriteVI(2, 10)); // viaddi vi1, vi2, -1
            Assert.AreEqual((ushort)9, cpu.VU0.ReadVI(1));
        }

        [TestMethod]
        public void Qmfc2_Qmtc2_MoveAll128Bits()
        {
            var cpu = Run(0x48281800, c => c.VU0.WriteVF(3, new Vector4(1, 2, 3, 4))); // qmfc2 t0, vf3
            Assert.AreEqual(BitConverter.SingleToUInt32Bits(1f) | ((ulong)BitConverter.SingleToUInt32Bits(2f) << 32), cpu.RegsLo[8]);
            Assert.AreEqual(BitConverter.SingleToUInt32Bits(3f) | ((ulong)BitConverter.SingleToUInt32Bits(4f) << 32), cpu.RegsHi[8]);

            var back = Run(0x48A81800, c => { c.RegsLo[8] = cpu.RegsLo[8]; c.RegsHi[8] = cpu.RegsHi[8]; }); // qmtc2 t0, vf3
            Assert.AreEqual(new Vector4(1, 2, 3, 4), back.VU0.ReadVF(3));
        }

        [TestMethod]
        public void UnassignedSlots_DecodeAsWord()
        {
            // SPECIAL1 0x33 / 0x36 / 0x37 / 0x3A / 0x3B and SPECIAL2 0x2B are COP2_Unknown in PCSX2.
            foreach (uint word in new[] { 0x4BE31073u, 0x4BE31076u, 0x4BE3107Au, 0x4BE3107Bu, 0x4BE312BFu })
            {
                string text = MipsDisassembler.Disassemble(word);
                if (text.StartsWith(".word", StringComparison.Ordinal)) continue;
                Assert.Fail($"0x{word:X8} decoded as '{text}'");
            }
        }

        [TestMethod]
        public void Assembler_RejectsBadDestAndOperands()
        {
            Assert.IsFalse(MipsAssembler.TryAssemble("vadd.zx vf1, vf2, vf3", out _, out _), "dest must be in xyzw order");
            Assert.IsFalse(MipsAssembler.TryAssemble("vdiv.x Q, vf1x, vf2y", out _, out _), "vdiv takes no dest");
            Assert.IsFalse(MipsAssembler.TryAssemble("viaddi vi1, vi2, 16", out _, out _), "imm5 range");
            Assert.IsFalse(MipsAssembler.TryAssemble("vcallms 0x4", out _, out _), "vcallms address must be 8-aligned");
        }
    }
}
