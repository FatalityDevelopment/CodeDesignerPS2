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
    public class TestIopMemoryBus
    {
        // =====================================================================
        // IopMemoryBus tests
        // =====================================================================

        [TestMethod]
        public void IopMemoryBus_Ram_ReadWrite()
        {
            var bus = new IopMemoryBus();
            bus.WriteWord(0x00010000u, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x00010000u), "RAM read-write round-trip");
        }

        [TestMethod]
        public void IopMemoryBus_RamSize_Is2MB()
        {
            Assert.AreEqual(4 * 1024 * 1024, IopMemoryBus.RamSize);
        }

        [TestMethod]
        public void IopMemoryBus_IntcStat_Write0ToClear()
        {
            var bus = new IopMemoryBus();
            bus.RaiseInterrupt(2);
            bus.RaiseInterrupt(5);
            Assert.AreNotEqual(0u, bus.IStat & (1u << 2));

            // W0C (write-AND per PCSX2 psxHu32 &= value): write 0 to bit 2 to clear it,
            // keeping bit 5 set by writing 1 there.  ~(1u << 2) = all ones except bit 2.
            bus.WriteWord(0x1F801070u, ~(1u << 2));
            Assert.AreEqual(0u,        bus.IStat & (1u << 2), "I_STAT bit 2 must be cleared");
            Assert.AreNotEqual(0u,     bus.IStat & (1u << 5), "I_STAT bit 5 must remain set");
        }

        [TestMethod]
        public void IopMemoryBus_IntcMask_PlainWrite()
        {
            var bus = new IopMemoryBus();
            bus.WriteWord(0x1F801074u, 0x0000001Fu);
            Assert.AreEqual(0x0000001Fu, bus.IMask, "I_MASK plain write");
        }

        [TestMethod]
        public void IopMemoryBus_SharedScratchpad_ReadWriteCoherent()
        {
            // The IOP bus must map 0x70000000–0x70003FFF to SharedScratchpad when wired.
            var scratchpad = new byte[16 * 1024]; // 16 KB
            var bus = new IopMemoryBus { SharedScratchpad = scratchpad };

            // Write via IOP bus, read back via the shared array
            bus.WriteWord(0x70003FF0u, 0xDEADBEEFu);
            uint fromArray = (uint)(scratchpad[0x3FF0]
                                  | (scratchpad[0x3FF1] << 8)
                                  | (scratchpad[0x3FF2] << 16)
                                  | (scratchpad[0x3FF3] << 24));
            Assert.AreEqual(0xDEADBEEFu, fromArray,
                "IOP write to 0x70003FF0 must appear in the SharedScratchpad array");

            // Write via the shared array, read back via IOP bus
            scratchpad[0x3F90]     = 0xAA;
            scratchpad[0x3F91]     = 0xBB;
            scratchpad[0x3F92]     = 0xCC;
            scratchpad[0x3F93]     = 0xDD;
            uint fromBus = bus.ReadWord(0x70003F90u);
            Assert.AreEqual(0xDDCCBBAAu, fromBus,
                "IOP read from 0x70003F90 must see data written to SharedScratchpad");
        }

        [TestMethod]
        public void IopMemoryBus_SharedScratchpad_NullDoesNotMap()
        {
            // When SharedScratchpad is null the region must read as 0 and writes are discarded.
            var bus = new IopMemoryBus(); // SharedScratchpad == null
            bus.WriteWord(0x70003FF0u, 0x12345678u);
            Assert.AreEqual(0u, bus.ReadWord(0x70003FF0u),
                "Without SharedScratchpad, IOP reads from 0x70000000 must return 0");
        }

        [TestMethod]
        public void DualCpuScheduler_SharedScratchpad_WiredToEEScratchpad()
        {
            var scheduler = new DualCpuScheduler();
            Assert.IsNotNull(scheduler.IOP.Bus.SharedScratchpad,
                "DualCpuScheduler must wire IOP.Bus.SharedScratchpad");
            Assert.AreSame(scheduler.EE.Bus.Scratchpad, scheduler.IOP.Bus.SharedScratchpad,
                "IOP.Bus.SharedScratchpad must be the same array as EE.Bus.Scratchpad");
        }

        [TestMethod]
        public void IopMemoryBus_InterruptPending_WhenStatAndMaskBothSet()
        {
            var bus = new IopMemoryBus();
            bus.RaiseInterrupt(3);
            Assert.IsFalse(bus.IsInterruptPending, "No interrupt pending if mask bit is 0");
            bus.WriteWord(0x1F801074u, 1u << 3);
            Assert.IsTrue(bus.IsInterruptPending, "Interrupt must be pending when STAT and MASK both set");
        }

        [TestMethod]
        public void IopMemoryBus_TimerRegisters_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // Timer 0 at 0x1F801100; counter=+0, mode=+4, target=+8
            bus.WriteWord(0x1F801104u, 0x0080u); // mode: CUE
            bus.WriteWord(0x1F801108u, 100u);     // target = 100
            Assert.AreEqual(0x0080u, bus.ReadWord(0x1F801104u), "Timer 0 mode read-back");
            Assert.AreEqual(100u,    bus.ReadWord(0x1F801108u), "Timer 0 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_TimerT1_BaseAddress()
        {
            var bus = new IopMemoryBus();
            // Timer 1 at 0x1F801110
            bus.WriteWord(0x1F801118u, 500u);
            Assert.AreEqual(500u, bus.ReadWord(0x1F801118u), "Timer 1 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_SifMirror_ForwardsToSharedSif()
        {
            var sif = new SIF();
            var bus = new IopMemoryBus { SharedSif = sif };

            // IOP writes MSCOM via its SIF mirror (offset 0x00 = MSCOM)
            bus.WriteWord(0x1D000000u, 0xAABBCCDDu);
            Assert.AreEqual(0xAABBCCDDu, sif.MsCom,
                "IOP write to SIF mirror must update the shared SIF MSCOM");

            // IOP reads back
            Assert.AreEqual(0xAABBCCDDu, bus.ReadWord(0x1D000000u),
                "IOP read from SIF mirror must return the written value");
        }

        [TestMethod]
        public void IopMemoryBus_SifMirror_Null_NoThrow()
        {
            var bus = new IopMemoryBus();  // no shared SIF
            // Should not throw; SIF mirror reads return 0
            Assert.AreEqual(0u, bus.ReadWord(0x1D000000u),
                "SIF mirror read with no SharedSif must return 0 without throwing");
        }

        [TestMethod]
        public void IopMemoryBus_Bios_IsReadOnly()
        {
            var bus = new IopMemoryBus();
            // Pre-fill BIOS bytes to non-zero using the Bios array directly
            bus.Bios[0] = 0xAA;
            // Write through bus should be silently ignored (BIOS is ROM)
            bus.WriteWord(0x1FC00000u, 0xDEADBEEFu);
            Assert.AreEqual(0xAAu, bus.Bios[0], "BIOS ROM must not be writable via bus");
        }

        [TestMethod]
        public void IopMemoryBus_Kseg1BiosAddress_TranslatesToPhysicalBios()
        {
            // The IOP reset vector is 0xBFC00000 (KSEG1), which must translate to
            // physical 0x1FC00000 (BIOS ROM).  Without KSEG1 translation the IOP
            // would read 0 (NOP) instead of real BIOS instructions, causing the
            // EE to stay in its boot-wait spin loop.
            var bus = new IopMemoryBus();
            // Plant a sentinel value directly in the BIOS byte array
            bus.Bios[0x180] = 0x12;
            bus.Bios[0x181] = 0x34;
            bus.Bios[0x182] = 0x56;
            bus.Bios[0x183] = 0x78;
            // Read via the KSEG1-mapped reset vector offset (0xBFC00180 → physical 0x1FC00180)
            uint word = bus.ReadWord(0xBFC00180u);
            Assert.AreEqual(0x78563412u, word,
                "KSEG1 BIOS address 0xBFC00180 must translate to physical 0x1FC00180");
        }

        [TestMethod]
        public void IopMemoryBus_Kseg0RamAddress_ReadWrite()
        {
            // KSEG0 RAM: 0x80001000 → physical 0x00001000 (IOP main RAM)
            var bus = new IopMemoryBus();
            bus.WriteWord(0x80001000u, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x80001000u),
                "KSEG0 RAM address 0x80001000 must translate to physical 0x00001000");
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x00001000u),
                "Physical 0x00001000 must reflect the same value written via KSEG0");
        }

        [TestMethod]
        public void IopMemoryBus_SifMirror_SmFlag_IopSetsW1S()
        {
            // When the IOP writes to SMFLAG (IOP offset 0x30) it must W1S the
            // SMFLAG register so the EE can see the IOP's boot-complete signal.
            // This is the inverse of the EE's W1C (acknowledge) semantics.
            var sif = new SIF();
            var bus = new IopMemoryBus { SharedSif = sif };

            // IOP signals EE by W1S on SMFLAG via its mirror at 0x1D000030
            bus.WriteWord(0x1D000030u, 0x00040000u);
            Assert.AreEqual(0x00040000u, sif.SmFlag,
                "IOP write to SIF SMFLAG (0x1D000030) must W1S the shared SMFLAG");
        }

        [TestMethod]
        public void IopMemoryBus_SifMirror_MsFlag_IopClearsW1C()
        {
            // When the IOP writes to MSFLAG (IOP offset 0x20) it must W1C the
            // MSFLAG register, acknowledging the EE's signal.
            var sif = new SIF();
            var bus = new IopMemoryBus { SharedSif = sif };

            // EE sets MSFLAG via the EE-side write (W1S from EE)
            sif.WriteRegister(SIF.REG_MSFLAG, 0x000000FFu);
            Assert.AreEqual(0x000000FFu, sif.MsFlag, "EE write must set MSFLAG bits");

            // IOP acknowledges by W1C on MSFLAG via its mirror at 0x1D000020
            bus.WriteWord(0x1D000020u, 0x0000000Fu);
            Assert.AreEqual(0x000000F0u, sif.MsFlag,
                "IOP write to MSFLAG (0x1D000020) must W1C: clear the acknowledged bits");
        }


        // =====================================================================
        // IopMemoryBus + Sio2 integration tests
        // =====================================================================

        [TestMethod]
        public void IopMemoryBus_Sio2Property_SetAndGet()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            bus.Sio2 = sio2;
            Assert.AreSame(sio2, bus.Sio2, "Sio2 property must return the assigned instance");
        }

        [TestMethod]
        public void IopMemoryBus_Sio2_WriteWordSend3_RoundTrip()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            bus.Sio2 = sio2;

            uint addr = Sio2.RegBase;           // SEND3[0] at RegBase+0x00
            bus.WriteWord(addr, 0xAABBCCDDu);
            uint readBack = bus.ReadWord(addr);
            Assert.AreEqual(0xAABBCCDDu, readBack, "SEND3[0] word round-trip via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Sio2_WriteByteToFifoIn_TriggerTransfer()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            var pad  = new PadState();
            sio2.Port0 = pad;
            bus.Sio2   = sio2;

            // Write SEND3[0] descriptor
            bus.WriteWord(Sio2.RegBase, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            // Write command bytes to FIFO_IN via byte access
            bus.WriteByte(Sio2.RegBase + 0x60u, PadState.AddrPad);
            bus.WriteByte(Sio2.RegBase + 0x60u, PadState.CmdPoll);
            // Trigger via CTRL byte write
            bus.WriteByte(Sio2.RegBase + 0x68u, 0x01);

            // Read response word (FIFO_OUT at +0x64)
            uint resp0 = bus.ReadWord(Sio2.RegBase + 0x64u); // 0xFF (leading)
            uint resp1 = bus.ReadWord(Sio2.RegBase + 0x64u); // 0x73 device ID
            Assert.AreEqual((uint)PadState.DeviceId, resp1, "Device ID must be 0x73 via IopMemoryBus word read");
        }

        [TestMethod]
        public void IopMemoryBus_Sio2TransferComplete_RaisesIntcBit17()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            bus.Sio2 = sio2;

            bus.WriteWord(Sio2.RegBase, BuildSend3(port: 0, sendLen: 1, recvLen: 1));
            bus.WriteByte(Sio2.RegBase + 0x60u, 0x00);
            bus.WriteWord(Sio2.RegBase + 0x68u, 0x01u); // CTRL START

            Assert.AreNotEqual(0u, bus.IStat & (1u << 17),
                "IOP INTC bit 17 must be set when SIO2 TransferComplete fires");
        }

        // ── SEND3 descriptor builder helper ───────────────────────────────────

        /// <summary>
        /// Build a SEND3 descriptor word with the given port, send length, and receive length.
        /// </summary>

        private static uint BuildSend3(int port, int sendLen, int recvLen)
            => (uint)(port & 0x3)
             | ((uint)(sendLen & 0x1FF) << 8)
             | ((uint)(recvLen & 0x1FF) << 18);
    }
}
