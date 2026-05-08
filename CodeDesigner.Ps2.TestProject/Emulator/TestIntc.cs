using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.Hardware;
using CodeDesigner.Ps2.EmulatorCore.IOP;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using CodeDesigner.Ps2.Common.VectorUnits.VU1;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestIntc
    {
        // =====================================================================
        // INTC tests
        // =====================================================================

        [TestMethod]
        public void INTC_InitialState_NoPendingInterrupts()
        {
            var intc = new INTC();
            Assert.AreEqual(0u, intc.Stat);
            Assert.AreEqual(0u, intc.Mask);
            Assert.IsFalse(intc.IsInterruptPending);
        }

        [TestMethod]
        public void INTC_RaiseInterrupt_SetsStat()
        {
            var intc = new INTC();
            intc.RaiseInterrupt(INTC.INT_TIMER0);
            Assert.AreEqual(1u << INTC.INT_TIMER0, intc.Stat);
        }

        [TestMethod]
        public void INTC_PendingOnlyWhenBothStatAndMaskSet()
        {
            var intc = new INTC();
            intc.RaiseInterrupt(INTC.INT_TIMER0); // STAT bit set
            Assert.IsFalse(intc.IsInterruptPending, "No interrupt pending if MASK bit is 0");

            // Toggle MASK bit for TIMER0 via WriteRegister
            intc.WriteRegister(INTC.REG_MASK, 1u << INTC.INT_TIMER0);
            Assert.IsTrue(intc.IsInterruptPending, "Interrupt must be pending when both STAT and MASK set");
        }

        [TestMethod]
        public void INTC_WriteStatClearsBit()
        {
            var intc = new INTC();
            intc.RaiseInterrupt(2);
            intc.RaiseInterrupt(3);

            // Write 1 to bit 2 to clear it (write-1-to-clear)
            intc.WriteRegister(INTC.REG_STAT, 1u << 2);
            Assert.AreEqual(0u, intc.Stat & (1u << 2), "Bit 2 must be cleared after W1C write");
            Assert.AreNotEqual(0u, intc.Stat & (1u << 3), "Bit 3 must remain set");
        }

        [TestMethod]
        public void INTC_WriteMaskToggles()
        {
            var intc = new INTC();
            // First toggle: 0 → 1
            intc.WriteRegister(INTC.REG_MASK, 0b0110u);
            Assert.AreEqual(0b0110u, intc.Mask, "First toggle must set bits 1 and 2");

            // Second toggle: 1 → 0 for bit 1 only
            intc.WriteRegister(INTC.REG_MASK, 0b0010u);
            Assert.AreEqual(0b0100u, intc.Mask, "Toggle must flip bit 1 back to 0");
        }

        [TestMethod]
        public void INTC_VBlank_Helpers()
        {
            var intc = new INTC();
            intc.RaiseVBlankStart();
            Assert.AreNotEqual(0u, intc.Stat & (1u << INTC.INT_VBLANK_S));

            intc.RaiseVBlankEnd();
            Assert.AreNotEqual(0u, intc.Stat & (1u << INTC.INT_VBLANK_E));
        }

        // =====================================================================
        // INTC I/O register routing via PS2MemoryBus
        // =====================================================================

        [TestMethod]
        public void MemoryBus_IntcStat_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            // Raise interrupt bit 9 (Timer 0)
            bus.Intc.RaiseInterrupt(9);

            uint stat = bus.ReadWord(0x1000F000u);
            Assert.AreEqual(1u << 9, stat, "INTC_STAT must reflect raised interrupt");

            // Write-1-to-clear via the bus
            bus.WriteWord(0x1000F000u, 1u << 9);
            Assert.AreEqual(0u, bus.ReadWord(0x1000F000u), "INTC_STAT bit must be cleared");
        }

        [TestMethod]
        public void MemoryBus_IntcMask_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            // Toggle mask bit for Timer 0 (bit 9)
            bus.WriteWord(0x1000F010u, 1u << 9);
            Assert.AreEqual(1u << 9, bus.ReadWord(0x1000F010u), "INTC_MASK must have bit 9 set");

            // Toggle again: bit 9 goes back to 0
            bus.WriteWord(0x1000F010u, 1u << 9);
            Assert.AreEqual(0u, bus.ReadWord(0x1000F010u), "INTC_MASK bit 9 must toggle back to 0");
        }

    }
}
