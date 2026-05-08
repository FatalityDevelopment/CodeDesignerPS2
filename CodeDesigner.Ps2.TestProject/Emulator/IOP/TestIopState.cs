using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.Hardware;
using CodeDesigner.Ps2.EmulatorCore.IOP;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestIopState
    {
        // =====================================================================
        // IopState tests
        // =====================================================================

        [TestMethod]
        public void IopState_InitialState_AllRegistersZero()
        {
            var s = new IopState();
            s.Reset();
            for (int i = 0; i < 32; i++)
                Assert.AreEqual(0u, s.ReadReg(i), $"GPR[{i}] must be 0 after reset");
        }

        [TestMethod]
        public void IopState_Reset_PcIsResetVector()
        {
            var s = new IopState();
            s.Reset();
            Assert.AreEqual(0xBFC00000u, s.PC, "IOP reset vector must be 0xBFC00000");
        }

        [TestMethod]
        public void IopState_Reset_SrHasBevSet()
        {
            var s = new IopState();
            s.Reset();
            Assert.AreNotEqual(0u, s.SR & IopState.SR_BEV, "SR.BEV must be 1 after reset");
        }

        [TestMethod]
        public void IopState_WriteZeroReg_IsIgnored()
        {
            var s = new IopState();
            s.Reset();
            s.WriteReg(0, 0xDEADBEEFu);
            Assert.AreEqual(0u, s.ReadReg(0), "$zero must always read as 0");
        }

        [TestMethod]
        public void IopState_WriteReadAllGprs()
        {
            var s = new IopState();
            for (int i = 1; i < 32; i++)
            {
                s.WriteReg(i, (uint)(i * 0x11111111));
                Assert.AreEqual((uint)(i * 0x11111111), s.ReadReg(i), $"GPR[{i}] round-trip failed");
            }
        }

        [TestMethod]
        public void IopState_Cop0Registers_RoundTrip()
        {
            var s = new IopState();
            s.WriteCop0(12, 0xABCD1234u);
            s.WriteCop0(13, 0x00000010u);
            s.WriteCop0(14, 0x80100000u);

            Assert.AreEqual(0xABCD1234u, s.ReadCop0(12), "SR round-trip");
            Assert.AreEqual(0x00000010u, s.ReadCop0(13), "Cause round-trip");
            Assert.AreEqual(0x80100000u, s.ReadCop0(14), "EPC round-trip");
        }

        [TestMethod]
        public void IopState_Cop0PrId_ReturnsR3000AId()
        {
            var s = new IopState();
            Assert.AreEqual(0x0000001Fu, s.ReadCop0(15), "PRId must return R3000A identifier");
        }

        [TestMethod]
        public void IopState_HiLo_RoundTrip()
        {
            var s = new IopState();
            s.HI = 0x12345678u;
            s.LO = 0x9ABCDEF0u;
            Assert.AreEqual(0x12345678u, s.HI);
            Assert.AreEqual(0x9ABCDEF0u, s.LO);
        }

    }
}
