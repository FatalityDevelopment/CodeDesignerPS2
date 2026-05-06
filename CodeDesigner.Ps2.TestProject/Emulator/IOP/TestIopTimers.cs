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
    public class TestIopTimers
    {
        // =====================================================================
        // IopTimer tests
        // =====================================================================

        [TestMethod]
        public void IopTimer_Tick_IncrementsCount()
        {
            // IOP timers always count from their clock source regardless of MODE bits.
            // The hardware has no count-enable bit; MODE only controls interrupts and clock.
            var t = new IopTimer(0);
            t.Tick(5);
            Assert.AreEqual(5u, t.Count, "Timer must always count regardless of MODE");
        }

        [TestMethod]
        public void IopTimer_Tick_AlwaysCountsWithModeZero()
        {
            // Per PS1/IOP hardware spec (no$psx.de): the timer has no CUE (count-up enable)
            // bit.  It always counts from the selected clock source.  The former
            // IopTimer_Tick_DisabledWhenCueNotSet test verified incorrect CUE behaviour;
            // this test documents the correct always-counting behaviour.
            var t = new IopTimer(0);
            // MODE=0 (no interrupt enables) — timer still counts
            t.Tick(100);
            Assert.AreEqual(100u, t.Count, "Timer must always count even with MODE=0");
        }

        [TestMethod]
        public void IopTimer_Tick_CompareFires_WhenCmpeSet()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x0010u); // CMPE
            t.WriteRegister(IopTimer.REG_TARGET,  10u);

            bool fired = false;
            t.CompareInterruptPending += (_, _) => fired = true;

            t.Tick(10);

            Assert.IsTrue(fired, "CompareInterruptPending must fire when count reaches target");
            Assert.AreNotEqual(0u, t.Mode & 0x0400u, "EQUF must be set after compare match");
        }

        [TestMethod]
        public void IopTimer_Tick_CompareNotFired_WhenCmpeCleared()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x0000u); // CMPE=0
            t.WriteRegister(IopTimer.REG_TARGET,  5u);

            bool fired = false;
            t.CompareInterruptPending += (_, _) => fired = true;

            t.Tick(5);

            Assert.IsFalse(fired, "CompareInterruptPending must not fire when CMPE is clear");
        }

        [TestMethod]
        public void IopTimer_Tick_OverflowFires_WhenOvfeSet()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x0020u); // OVFE

            bool fired = false;
            t.OverflowInterruptPending += (_, _) => fired = true;

            // Tick past 0xFFFF to cause a 16-bit overflow
            t.Tick(0x10000u);

            Assert.IsTrue(fired, "OverflowInterruptPending must fire on 16-bit counter wrap");
            Assert.AreNotEqual(0u, t.Mode & 0x0800u, "OVFF must be set after overflow");
        }

        [TestMethod]
        public void IopTimer_Tick_OverflowNotFired_WhenOvfeCleared()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x0000u); // OVFE=0

            bool fired = false;
            t.OverflowInterruptPending += (_, _) => fired = true;

            t.Tick(0x10000u);

            Assert.IsFalse(fired, "OverflowInterruptPending must not fire when OVFE is clear");
        }

        [TestMethod]
        public void IopTimer_ModeRead_ClearsEqufAndOvff()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x0030u); // CMPE | OVFE
            t.WriteRegister(IopTimer.REG_TARGET,  3u);

            t.Tick(3);           // hit compare → sets EQUF
            t.Tick(0x10000u);    // overflow → sets OVFF

            uint modeBeforeRead = t.Mode;
            Assert.AreNotEqual(0u, modeBeforeRead & 0x0C00u, "Both flags must be set before read");

            // Reading mode via ReadRegister clears sticky flags
            uint modeAfterRead = t.ReadRegister(IopTimer.REG_MODE);
            Assert.AreNotEqual(0u, modeAfterRead & 0x0C00u, "Returned value must have flags set");
            Assert.AreEqual(0u, t.Mode & 0x0C00u, "Flags must be cleared in register after read");
        }

        [TestMethod]
        public void IopTimer_WriteCount_ResetsCounter()
        {
            var t = new IopTimer(0);
            t.Tick(50);
            Assert.AreEqual(50u, t.Count);

            t.WriteRegister(IopTimer.REG_COUNT, 0u); // writing count resets to 0
            Assert.AreEqual(0u, t.Count, "Writing TN_COUNT must reset counter to 0");
        }

        [TestMethod]
        public void IopTimer_AltSource_DividesBy8()
        {
            var t = new IopTimer(2); // T2 supports alternate source
            t.WriteRegister(IopTimer.REG_MODE, 0x0100u); // ALT (÷8); no CUE needed

            t.Tick(8);   // 8 sysclock cycles → 1 timer increment
            Assert.AreEqual(1u, t.Count, "Alternate source (÷8) must count once per 8 cycles");

            t.Tick(7);   // 7 more → still 1 (accumulator not full)
            Assert.AreEqual(1u, t.Count);

            t.Tick(1);   // one more → 2 total
            Assert.AreEqual(2u, t.Count);
        }

        [TestMethod]
        public void IopTimer_Counter_Wraps16Bit()
        {
            var t = new IopTimer(0);
            // No special mode needed — timer always counts

            // Drive count to 0xFFFE
            t.Tick(0xFFFEu);
            Assert.AreEqual(0xFFFEu, t.Count);

            // Tick 3 more: 0xFFFE → 0xFFFF → 0x0000 (wrap) → 0x0001
            t.Tick(3u);
            Assert.AreEqual(1u, t.Count, "Counter must wrap at 16-bit boundary");
        }

        [TestMethod]
        public void IopTimer_IrqRepeat_FiresOnEveryMatch()
        {
            // MODE=0x0050: CMPE | IRQR (repeat mode). Timer fires on every compare match.
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x0050u); // CMPE | IRQR
            t.WriteRegister(IopTimer.REG_TARGET,  5u);

            int fireCount = 0;
            t.CompareInterruptPending += (_, _) => fireCount++;

            // Tick past the target twice (no ZRET, so count continues past target)
            // First pass at tick 5
            t.Tick(5);
            Assert.AreEqual(1, fireCount, "First compare must fire");

            // Clear EQUF by reading MODE (simulates IOP interrupt handler ACK)
            t.ReadRegister(IopTimer.REG_MODE);

            // Tick another 0x10000 so count wraps and passes target again
            t.Tick(0x10000u);
            Assert.IsTrue(fireCount >= 2, "Repeat mode must fire on subsequent matches after EQUF cleared");
        }

        [TestMethod]
        public void IopMemoryBus_WriteHalf_TimerMode_Reaches_Timer()
        {
            // Regression: SH (halfword store) to timer T5 MODE must reach the timer.
            // Previously WriteHalf fell through to MapWrite which discarded hardware writes.
            var bus = new IopMemoryBus();
            // Timer T5 MODE at 0x1F8014A4
            bus.WriteHalf(0x1F8014A4u, (ushort)0x0030u); // CMPE | OVFE
            Assert.AreEqual(0x0030u, bus.ReadHalf(0x1F8014A4u), "WriteHalf to T5 MODE must reach timer register");
        }

        [TestMethod]
        public void IopMemoryBus_WriteHalf_T5_Mode_Enables_Interrupt()
        {
            // Verify that after WriteHalf sets T5 MODE, the timer fires and raises I_STAT.
            var emulator = new IopEmulator();
            // T5 TARGET and MODE via bus (as the IOP BIOS does via SW and SH)
            emulator.Bus.WriteWord(0xBF8014A8u, 10u);                    // TARGET = 10
            emulator.Bus.WriteHalf(0xBF8014A4u, (ushort)0x0050u);        // MODE: CMPE | IRQR (SH path)
            emulator.Bus.WriteWord(0x1F801074u, 1u << 16);               // I_MASK: enable T5 (bit 16)

            // Tick the emulator hardware until T5 fires
            for (int i = 0; i < 20; i++)
                emulator.TickHardware(1);

            Assert.AreNotEqual(0u, emulator.Bus.IStat & (1u << 16),
                "T5 interrupt bit must be set in I_STAT after timer fires via WriteHalf MODE write");
        }

        // ── T3–T5 bus mapping tests ───────────────────────────────────────────

        [TestMethod]
        public void IopMemoryBus_TimerT3_BaseAddress_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // T3 at 0x1F801480; mode=+4, target=+8
            bus.WriteWord(0x1F801484u, 0x0080u); // T3 mode: CUE
            bus.WriteWord(0x1F801488u, 200u);    // T3 target = 200

            Assert.AreEqual(0x0080u, bus.ReadWord(0x1F801484u), "T3 mode read-back");
            Assert.AreEqual(200u,    bus.ReadWord(0x1F801488u), "T3 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_TimerT4_BaseAddress_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // T4 at 0x1F801490
            bus.WriteWord(0x1F801498u, 300u);
            Assert.AreEqual(300u, bus.ReadWord(0x1F801498u), "T4 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_TimerT5_BaseAddress_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // T5 at 0x1F8014A0
            bus.WriteWord(0x1F8014A8u, 400u);
            Assert.AreEqual(400u, bus.ReadWord(0x1F8014A8u), "T5 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_Timers_Property_ReturnsAllSix()
        {
            var bus = new IopMemoryBus();
            Assert.AreEqual(6, bus.Timers.Length, "IopMemoryBus.Timers must expose 6 timers");
            for (int i = 0; i < 6; i++)
                Assert.IsNotNull(bus.Timers[i], $"Timers[{i}] must not be null");
        }

        // ── TickHardware timer→INTC integration tests ─────────────────────────

        [TestMethod]
        public void IopEmulator_TickHardware_T0Compare_RaisesIntcBit4()
        {
            var emu = new IopEmulator();

            // Enable T0 with compare target=1, compare interrupt enabled
            emu.Bus.WriteWord(0x1F801104u, 0x0090u); // T0 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801108u, 1u);      // T0 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 4); // I_MASK: enable T0 interrupt

            emu.TickHardware(1); // T0 count becomes 1 == target → INTC bit 4 raised

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 4),
                "INTC bit 4 must be set when T0 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T1Compare_RaisesIntcBit5()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801114u, 0x0090u); // T1 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801118u, 1u);      // T1 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 5); // I_MASK: enable T1 interrupt

            emu.TickHardware(1);

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 5),
                "INTC bit 5 must be set when T1 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T2Compare_RaisesIntcBit6()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801124u, 0x0090u); // T2 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801128u, 1u);      // T2 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 6);

            emu.TickHardware(1);

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 6),
                "INTC bit 6 must be set when T2 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T3Compare_RaisesIntcBit14()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801484u, 0x0090u); // T3 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801488u, 1u);      // T3 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 14);

            emu.TickHardware(1);

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 14),
                "INTC bit 14 must be set when T3 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T0Overflow_RaisesIntcBit4()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801104u, 0x00A0u); // T0 mode: CUE | OVFE
            emu.Bus.WriteWord(0x1F801074u, 1u << 4);

            emu.TickHardware(0x10000u); // force 16-bit overflow

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 4),
                "INTC bit 4 must be set when T0 overflow interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_MultipleTimerTicks_Accumulate()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801104u, 0x0080u); // T0 CUE only

            emu.TickHardware(10);
            emu.TickHardware(5);

            Assert.AreEqual(15u, emu.Bus.Timers[0].Count,
                "Timer count must accumulate across multiple TickHardware calls");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_InterruptPendingFired_OnTimerInterrupt()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801104u, 0x0090u); // T0 CUE | CMPE
            emu.Bus.WriteWord(0x1F801108u, 1u);      // target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 4); // mask bit 4

            bool fired = false;
            emu.InterruptPending += (_, _) => fired = true;

            emu.TickHardware(1);

            Assert.IsTrue(fired,
                "InterruptPending must fire when a timer interrupt sets I_STAT & I_MASK");
        }

    }
}
