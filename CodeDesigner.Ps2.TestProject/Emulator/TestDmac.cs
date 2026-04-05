using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using CodeDesigner.Ps2.Common.VectorUnits.VU1;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestDmac
    {
        // =====================================================================
        // DMAC unit tests
        // =====================================================================

        [TestMethod]
        public void DmacChannel_InitialState_IsNotRunning()
        {
            var ch = new DmacChannel(0);
            Assert.IsFalse(ch.IsRunning, "Channel must not be running at startup");
            Assert.AreEqual(0u, ch.Qwc);
            Assert.AreEqual(0u, ch.Madr);
        }

        [TestMethod]
        public void DmacChannel_WriteChcr_SetsFields()
        {
            var ch = new DmacChannel(2);
            // DIR=1, MOD=00, STR=1 → CHCR = 0x0101
            ch.WriteRegister(DmacChannel.REG_CHCR, 0x0101u);
            Assert.IsTrue(ch.IsRunning,             "STR bit must be reflected in IsRunning");
            Assert.IsTrue(ch.DirectionToMemory,     "DIR bit must be reflected in DirectionToMemory");
            Assert.AreEqual(0, ch.Mode,             "MOD must be 0 (normal) when bits [3:2] are 0");
        }

        [TestMethod]
        public void DmacChannel_NormalMode_RamToFifo_AdvancesMadr()
        {
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();
            // Populate source memory
            bus.WriteWord(0x00100000u, 0xDEADBEEFu);

            dmac.Channels[2].WriteRegister(DmacChannel.REG_MADR, 0x00100000u);
            dmac.Channels[2].WriteRegister(DmacChannel.REG_QWC,  2u);      // 2 QW = 32 bytes
            dmac.Channels[2].WriteRegister(DmacChannel.REG_CHCR, 0x0100u); // STR=1, DIR=0

            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR must be cleared after transfer");
            Assert.AreEqual(0u,          dmac.Channels[2].Qwc,  "QWC must be 0 after transfer");
            Assert.AreEqual(0x00100020u, dmac.Channels[2].Madr, "MADR must advance by QWC*16");
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x00100000u),
                "Source RAM must be unchanged after RAM→FIFO transfer");
        }

        [TestMethod]
        public void DmacChannel_NormalMode_FifoToRam_ZeroFillsDestination()
        {
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();
            bus.WriteWord(0x00200000u, 0xCAFEBABEu);
            bus.WriteWord(0x00200004u, 0x12345678u);

            dmac.Channels[5].WriteRegister(DmacChannel.REG_MADR, 0x00200000u);
            dmac.Channels[5].WriteRegister(DmacChannel.REG_QWC,  1u);      // 1 QW = 16 bytes
            dmac.Channels[5].WriteRegister(DmacChannel.REG_CHCR, 0x0101u); // STR=1, DIR=1

            dmac.ExecuteChannel(5, bus);

            Assert.IsFalse(dmac.Channels[5].IsRunning, "STR must be cleared");
            Assert.AreEqual(0u, bus.ReadWord(0x00200000u), "FIFO→RAM must zero-fill destination");
            Assert.AreEqual(0u, bus.ReadWord(0x00200004u), "FIFO→RAM must zero-fill all 16 bytes");
        }

        [TestMethod]
        public void DmacChannel_TransferComplete_EventFires()
        {
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();
            dmac.Channels[0].WriteRegister(DmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_QWC,  4u);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_CHCR, 0x0100u);

            bool fired = false;
            dmac.Channels[0].TransferComplete += (_, _) => fired = true;

            dmac.ExecuteChannel(0, bus);

            Assert.IsTrue(fired, "TransferComplete event must fire on normal-mode completion");
        }

        [TestMethod]
        public void DmacChannel_ReadWriteRegisters_RoundTrip()
        {
            var ch = new DmacChannel(1);
            ch.WriteRegister(DmacChannel.REG_MADR, 0xAABBCCDDu);
            ch.WriteRegister(DmacChannel.REG_QWC,  0x0000007Fu);
            ch.WriteRegister(DmacChannel.REG_TADR, 0x11223344u);
            ch.WriteRegister(DmacChannel.REG_SADR, 0x70001000u);

            Assert.AreEqual(0xAABBCCDDu, ch.ReadRegister(DmacChannel.REG_MADR));
            Assert.AreEqual(0x0000007Fu, ch.ReadRegister(DmacChannel.REG_QWC));
            Assert.AreEqual(0x11223344u, ch.ReadRegister(DmacChannel.REG_TADR));
            Assert.AreEqual(0x70001000u, ch.ReadRegister(DmacChannel.REG_SADR));
        }

        [TestMethod]
        public void DMAC_InitialState_AllChannelsIdle()
        {
            var dmac = new DMAC();
            for (int i = 0; i < DMAC.ChannelCount; i++)
                Assert.IsFalse(dmac.Channels[i].IsRunning,
                    $"Channel {i} must not be running at startup");
        }

        [TestMethod]
        public void DMAC_DStatWrite_W1CandToggle()
        {
            var dmac = new DMAC();
            var bus  = new PS2MemoryBus();

            // Simulate two channel completions (zero-QWC normal transfers set D_STAT bits)
            dmac.Channels[0].WriteRegister(DmacChannel.REG_CHCR, 0x0100u); // STR=1, QWC=0
            dmac.ExecuteChannel(0, bus);
            dmac.Channels[1].WriteRegister(DmacChannel.REG_CHCR, 0x0100u);
            dmac.ExecuteChannel(1, bus);

            Assert.AreNotEqual(0u, dmac.Stat & 0x3u, "D_STAT bits 0 and 1 must be set after completions");

            // W1C: clear bit 0 only
            dmac.WriteGlobal(DMAC.REG_STAT, 0x0001u);
            Assert.AreEqual(0u, dmac.Stat & 0x1u, "D_STAT bit 0 must be W1C cleared");
            Assert.AreNotEqual(0u, dmac.Stat & 0x2u, "D_STAT bit 1 must remain set");

            // Toggle upper half: enable channel 1 interrupt (bit 17)
            dmac.WriteGlobal(DMAC.REG_STAT, 0x00020000u);
            Assert.AreNotEqual(0u, dmac.Stat & 0x00020000u, "D_STAT bit 17 must be toggled on");
            dmac.WriteGlobal(DMAC.REG_STAT, 0x00020000u);
            Assert.AreEqual(0u, dmac.Stat & 0x00020000u, "D_STAT bit 17 must toggle back to 0");
        }

        [TestMethod]
        public void DMAC_ChannelTransferComplete_EventFiredWhenEnabled()
        {
            var dmac = new DMAC();
            var bus  = new PS2MemoryBus();

            // Enable channel 2 interrupt in D_STAT (toggle bit 18)
            dmac.WriteGlobal(DMAC.REG_STAT, 1u << 18);

            int completedChannel = -1;
            dmac.ChannelTransferComplete += (_, ch) => completedChannel = ch;

            dmac.Channels[2].WriteRegister(DmacChannel.REG_CHCR, 0x0100u);
            dmac.ExecuteChannel(2, bus);

            Assert.AreEqual(2, completedChannel,
                "ChannelTransferComplete must fire with the correct channel index");
        }

        [TestMethod]
        public void DMAC_ChannelTransferComplete_NotFiredWhenDisabled()
        {
            var dmac = new DMAC();
            var bus  = new PS2MemoryBus();

            // D_STAT enable bit NOT set for channel 3
            bool fired = false;
            dmac.ChannelTransferComplete += (_, _) => fired = true;

            dmac.Channels[3].WriteRegister(DmacChannel.REG_CHCR, 0x0100u);
            dmac.ExecuteChannel(3, bus);

            Assert.IsFalse(fired,
                "ChannelTransferComplete must NOT fire when channel enable bit is unset");
            Assert.AreNotEqual(0u, dmac.Stat & (1u << 3),
                "D_STAT pending bit must still be set even when enable bit is unset");
        }

        [TestMethod]
        public void DMAC_GlobalRegisters_RoundTrip()
        {
            var dmac = new DMAC();
            dmac.WriteGlobal(DMAC.REG_CTRL,  0x00000001u);  // DMAE=1
            dmac.WriteGlobal(DMAC.REG_PCR,   0x80000001u);
            dmac.WriteGlobal(DMAC.REG_SQWC,  0x00010001u);
            dmac.WriteGlobal(DMAC.REG_RBSR,  0x00100000u);
            dmac.WriteGlobal(DMAC.REG_RBOR,  0x00200000u);
            dmac.WriteGlobal(DMAC.REG_STADR, 0x00300000u);

            Assert.AreEqual(0x00000001u, dmac.ReadGlobal(DMAC.REG_CTRL));
            Assert.AreEqual(0x80000001u, dmac.ReadGlobal(DMAC.REG_PCR));
            Assert.AreEqual(0x00010001u, dmac.ReadGlobal(DMAC.REG_SQWC));
            Assert.AreEqual(0x00100000u, dmac.ReadGlobal(DMAC.REG_RBSR));
            Assert.AreEqual(0x00200000u, dmac.ReadGlobal(DMAC.REG_RBOR));
            Assert.AreEqual(0x00300000u, dmac.ReadGlobal(DMAC.REG_STADR));
        }

        // =====================================================================
        // DMAC I/O register routing via PS2MemoryBus
        // =====================================================================

        [TestMethod]
        public void MemoryBus_DmacChannel0_MadrQwcReadWrite()
        {
            var bus = new PS2MemoryBus();
            // CH0 VIF0 base = 0x10008000; MADR at +0x10, QWC at +0x20
            bus.WriteWord(0x10008010u, 0x00500000u);
            bus.WriteWord(0x10008020u, 8u);

            Assert.AreEqual(0x00500000u, bus.ReadWord(0x10008010u), "CH0 MADR read-back");
            Assert.AreEqual(8u,          bus.ReadWord(0x10008020u), "CH0 QWC read-back");
        }

        [TestMethod]
        public void MemoryBus_DmacChannel2_GIF_BaseAddress()
        {
            var bus = new PS2MemoryBus();
            // CH2 GIF base = 0x1000A000
            bus.WriteWord(0x1000A010u, 0x00A00000u);
            Assert.AreEqual(0x00A00000u, bus.ReadWord(0x1000A010u), "CH2 (GIF) MADR read-back");
        }

        [TestMethod]
        public void MemoryBus_DmacChannel5_SIF0_BaseAddress()
        {
            var bus = new PS2MemoryBus();
            // CH5 SIF0 base = 0x1000C000
            bus.WriteWord(0x1000C010u, 0x00C00000u);
            Assert.AreEqual(0x00C00000u, bus.ReadWord(0x1000C010u), "CH5 (SIF0) MADR read-back");
        }

        [TestMethod]
        public void MemoryBus_DmacChcr_Write_ExecutesTransfer()
        {
            var bus = new PS2MemoryBus();
            // CH0 VIF0: set MADR, QWC, then write CHCR with STR=1 to trigger transfer
            bus.WriteWord(0x10008010u, 0x00100000u); // MADR
            bus.WriteWord(0x10008020u, 2u);           // QWC = 2 quadwords
            // Write CHCR with STR=1, DIR=0 (RAM→FIFO)
            bus.WriteWord(0x10008000u, 0x0100u);

            // After WriteWord to CHCR the bus calls ExecuteChannel synchronously.
            // STR should be cleared and D_STAT bit 0 should be set.
            Assert.IsFalse(bus.Dmac.Channels[0].IsRunning, "STR must be cleared after transfer");
            Assert.AreNotEqual(0u, bus.Dmac.Stat & 1u,    "D_STAT bit 0 must be set after CH0 completion");
        }

        [TestMethod]
        public void MemoryBus_DmacGlobal_Ctrl_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x1000E000u, 0x00000001u); // D_CTRL DMAE=1
            Assert.AreEqual(0x00000001u, bus.ReadWord(0x1000E000u), "D_CTRL read-back via bus");
        }

        [TestMethod]
        public void MemoryBus_DmacGlobal_Stat_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            // Execute a zero-QWC transfer on CH0 to set D_STAT bit 0
            bus.WriteWord(0x10008000u, 0x0100u);
            uint stat = bus.ReadWord(0x1000E010u);
            Assert.AreNotEqual(0u, stat & 1u, "D_STAT bit 0 must be readable via bus");
        }

        [TestMethod]
        public void Emulator_DmaChannelComplete_EventFired()
        {
            var emu = new PS2Emulator();

            // Enable CH2 (GIF) interrupt in D_STAT (toggle bit 18)
            emu.Bus.WriteWord(0x1000E010u, 1u << 18);

            int completedCh = -1;
            emu.DmaChannelComplete += (_, ch) => completedCh = ch;

            // Program CH2 and start DMA via CHCR write
            emu.Bus.WriteWord(0x1000A010u, 0x00500000u);  // MADR
            emu.Bus.WriteWord(0x1000A020u, 4u);            // QWC
            emu.Bus.WriteWord(0x1000A000u, 0x0100u);       // CHCR STR=1, DIR=0

            Assert.AreEqual(2, completedCh, "DmaChannelComplete must fire with channel index 2");
        }

        // =====================================================================
        // DMAC chain mode tests
        // =====================================================================

        [TestMethod]
        public void DmacChain_CntTag_TransfersDataAfterTag()
        {
            // CNT (id=1): data immediately follows the tag QW; next tag = data_end
            // Layout at 0x00200000:
            //   [+0x00] tag_lo = QWC=2 | ID=1<<24
            //   [+0x04] tag_hi = 0 (unused ADDR for CNT)
            //   [+0x08..0x0F] unused tag pad
            //   [+0x10] first data QW (4 words)
            //   [+0x20] second data QW = END tag to terminate
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tagBase = 0x00200000u;
            // CNT tag: QWC=1, ID=1 (CNT)
            bus.WriteWord(tagBase + 0x00u, (1u << 24) | 1u);  // ID=CNT, QWC=1
            bus.WriteWord(tagBase + 0x04u, 0u);                // ADDR unused for CNT
            // data word at tagBase+0x10
            bus.WriteWord(tagBase + 0x10u, 0xAABBCCDDu);
            bus.WriteWord(tagBase + 0x14u, 0x11223344u);
            bus.WriteWord(tagBase + 0x18u, 0x55667788u);
            bus.WriteWord(tagBase + 0x1Cu, 0x99AABBCCu);
            // END tag at tagBase+0x20 (data ends here; CNT next = tagBase+0x20)
            bus.WriteWord(tagBase + 0x20u, (7u << 24) | 0u);  // ID=END, QWC=0
            bus.WriteWord(tagBase + 0x24u, 0u);

            List<uint> received = new();
            dmac.Channels[2].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            dmac.Channels[2].WriteRegister(DmacChannel.REG_TADR, tagBase);
            dmac.Channels[2].WriteRegister(DmacChannel.REG_CHCR, 0x0104u); // STR=1, DIR=0, MOD=01 (chain)
            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR must be cleared after chain transfer");
            Assert.AreEqual(4, received.Count, "CNT tag must deliver 1 QW (4 words)");
            Assert.AreEqual(0xAABBCCDDu, received[0]);
            Assert.AreEqual(0x11223344u, received[1]);
        }

        [TestMethod]
        public void DmacChain_RefTag_TransfersFromAddr()
        {
            // REF (id=3): data at ADDR; next tag = TADR+16
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tagBase  = 0x00300000u;
            uint dataAddr = 0x00400000u;

            // Place data at the separate data address
            bus.WriteWord(dataAddr + 0x00u, 0xDEADBEEFu);
            bus.WriteWord(dataAddr + 0x04u, 0x12345678u);
            bus.WriteWord(dataAddr + 0x08u, 0xCAFEBABEu);
            bus.WriteWord(dataAddr + 0x0Cu, 0xFEEDFACEu);

            // REF tag: QWC=1, ID=3 (REF), ADDR=dataAddr
            bus.WriteWord(tagBase + 0x00u, (3u << 24) | 1u);  // ID=REF, QWC=1
            bus.WriteWord(tagBase + 0x04u, dataAddr);           // ADDR

            // END tag immediately following (at tagBase+0x10)
            bus.WriteWord(tagBase + 0x10u, (7u << 24) | 0u);  // ID=END, QWC=0
            bus.WriteWord(tagBase + 0x14u, 0u);

            List<uint> received = new();
            dmac.Channels[2].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            dmac.Channels[2].WriteRegister(DmacChannel.REG_TADR, tagBase);
            dmac.Channels[2].WriteRegister(DmacChannel.REG_CHCR, 0x0104u); // chain, DIR=0, STR=1
            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR must be cleared");
            Assert.AreEqual(4, received.Count, "REF tag must deliver 1 QW from separate address");
            Assert.AreEqual(0xDEADBEEFu, received[0]);
            Assert.AreEqual(0x12345678u, received[1]);
        }

        [TestMethod]
        public void DmacChain_RefeTag_TransfersAndTerminates()
        {
            // REFE (id=0): data at ADDR; terminates immediately
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tagBase  = 0x00500000u;
            uint dataAddr = 0x00600000u;

            bus.WriteWord(dataAddr, 0xBEEFCAFEu);

            // REFE tag: QWC=1, ID=0, ADDR=dataAddr
            bus.WriteWord(tagBase + 0x00u, (0u << 24) | 1u);  // ID=REFE, QWC=1
            bus.WriteWord(tagBase + 0x04u, dataAddr);

            List<uint> received = new();
            dmac.Channels[1].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            dmac.Channels[1].WriteRegister(DmacChannel.REG_TADR, tagBase);
            dmac.Channels[1].WriteRegister(DmacChannel.REG_CHCR, 0x0104u); // chain, DIR=0, STR=1
            dmac.ExecuteChannel(1, bus);

            Assert.IsFalse(dmac.Channels[1].IsRunning, "REFE must terminate transfer");
            Assert.AreEqual(4, received.Count, "REFE must transfer exactly 1 QW");
        }

        [TestMethod]
        public void DmacChain_EndTag_ZeroQwcTerminates()
        {
            // END tag with QWC=0: nothing to transfer, just terminate
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tagBase = 0x00700000u;
            bus.WriteWord(tagBase + 0x00u, (7u << 24) | 0u);  // ID=END, QWC=0
            bus.WriteWord(tagBase + 0x04u, 0u);

            int callCount = 0;
            dmac.Channels[0].DataConsumer = (_, _, _) => callCount++;

            dmac.Channels[0].WriteRegister(DmacChannel.REG_TADR, tagBase);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_CHCR, 0x0104u); // chain
            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(dmac.Channels[0].IsRunning);
            Assert.AreEqual(0, callCount, "END tag with QWC=0 must not invoke DataConsumer");
        }

        [TestMethod]
        public void DmacChain_NextTag_JumpsToAddr()
        {
            // NEXT (id=2): data at TADR+16; next tag at ADDR (a different location)
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tag0Base  = 0x00800000u;   // first tag
            uint tag1Base  = 0x00900000u;   // jumped-to tag

            // First tag: NEXT, QWC=1, ADDR=tag1Base
            bus.WriteWord(tag0Base + 0x00u, (2u << 24) | 1u); // ID=NEXT, QWC=1
            bus.WriteWord(tag0Base + 0x04u, tag1Base);          // ADDR=tag1Base
            // data: 1 QW at tag0Base+0x10
            bus.WriteWord(tag0Base + 0x10u, 0xAA000000u);
            bus.WriteWord(tag0Base + 0x14u, 0u);
            bus.WriteWord(tag0Base + 0x18u, 0u);
            bus.WriteWord(tag0Base + 0x1Cu, 0u);

            // Second tag: END, QWC=0
            bus.WriteWord(tag1Base + 0x00u, (7u << 24) | 0u); // ID=END, QWC=0
            bus.WriteWord(tag1Base + 0x04u, 0u);

            List<uint> received = new();
            dmac.Channels[2].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            dmac.Channels[2].WriteRegister(DmacChannel.REG_TADR, tag0Base);
            dmac.Channels[2].WriteRegister(DmacChannel.REG_CHCR, 0x0104u);
            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning);
            Assert.AreEqual(4, received.Count, "NEXT tag must deliver 1 QW then jump to END");
            Assert.AreEqual(0xAA000000u, received[0]);
        }

        [TestMethod]
        public void DmacChain_TieIrq_StopsChain()
        {
            // When TIE=1 and the tag's IRQ bit is set, the chain stops after that segment.
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tagBase = 0x00A00000u;
            // CNT tag with IRQ bit set
            uint irqBit = 1u << 27;
            bus.WriteWord(tagBase + 0x00u, irqBit | (1u << 24) | 1u); // IRQ=1, ID=CNT, QWC=1
            bus.WriteWord(tagBase + 0x04u, 0u);
            // data (1 QW at +0x10)
            bus.WriteWord(tagBase + 0x10u, 0x12345678u);
            bus.WriteWord(tagBase + 0x14u, 0u);
            bus.WriteWord(tagBase + 0x18u, 0u);
            bus.WriteWord(tagBase + 0x1Cu, 0u);
            // Another CNT tag right after (should NOT be reached)
            bus.WriteWord(tagBase + 0x20u, (1u << 24) | 1u); // ID=CNT, QWC=1
            bus.WriteWord(tagBase + 0x24u, 0u);
            bus.WriteWord(tagBase + 0x30u, 0xDEADBEEFu); // data of second tag (must NOT appear)
            bus.WriteWord(tagBase + 0x34u, 0u);
            bus.WriteWord(tagBase + 0x38u, 0u);
            bus.WriteWord(tagBase + 0x3Cu, 0u);

            List<uint> received = new();
            dmac.Channels[1].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            // TIE=1 is bit 7 of CHCR; chain mode MOD=01; STR=1; DIR=0
            dmac.Channels[1].WriteRegister(DmacChannel.REG_TADR, tagBase);
            dmac.Channels[1].WriteRegister(DmacChannel.REG_CHCR, 0x0184u); // TIE=1,TTE=0,chain,DIR=0,STR=1
            dmac.ExecuteChannel(1, bus);

            Assert.IsFalse(dmac.Channels[1].IsRunning, "STR must be cleared");
            // Only the first tag's data must appear; second tag skipped due to IRQ stop
            Assert.AreEqual(4, received.Count, "TIE+IRQ must stop chain after 1 segment");
            Assert.AreEqual(0x12345678u, received[0]);
            Assert.IsFalse(received.Contains(0xDEADBEEFu), "Second tag data must not be received");
        }

        [TestMethod]
        public void DmacChain_CallRet_SubroutineReturns()
        {
            // CALL (id=5): push return addr, jump to ADDR
            // RET  (id=6): pop return addr and continue
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint mainBase = 0x00B00000u;
            uint subBase  = 0x00C00000u;

            // CALL tag: QWC=0, ID=5, ADDR=subBase
            bus.WriteWord(mainBase + 0x00u, (5u << 24) | 0u); // ID=CALL, QWC=0
            bus.WriteWord(mainBase + 0x04u, subBase);

            // Subroutine: CNT tag, QWC=1, data at sub+0x10
            bus.WriteWord(subBase + 0x00u, (1u << 24) | 1u);  // ID=CNT, QWC=1
            bus.WriteWord(subBase + 0x04u, 0u);
            bus.WriteWord(subBase + 0x10u, 0xCAFEBABEu);
            bus.WriteWord(subBase + 0x14u, 0u);
            bus.WriteWord(subBase + 0x18u, 0u);
            bus.WriteWord(subBase + 0x1Cu, 0u);
            // RET tag at sub+0x20 (next after CNT data)
            bus.WriteWord(subBase + 0x20u, (6u << 24) | 0u);  // ID=RET, QWC=0
            bus.WriteWord(subBase + 0x24u, 0u);

            // Return lands at mainBase+0x10 (mainBase+16 = after the CALL tag QW)
            // END tag there
            bus.WriteWord(mainBase + 0x10u, (7u << 24) | 0u); // ID=END, QWC=0
            bus.WriteWord(mainBase + 0x14u, 0u);

            List<uint> received = new();
            dmac.Channels[0].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            dmac.Channels[0].WriteRegister(DmacChannel.REG_TADR, mainBase);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_CHCR, 0x0104u); // chain, DIR=0, STR=1
            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(dmac.Channels[0].IsRunning, "STR must be cleared after CALL/RET chain");
            Assert.AreEqual(4, received.Count, "CALL/RET subroutine must transfer exactly 1 QW");
            Assert.AreEqual(0xCAFEBABEu, received[0], "Subroutine data must be transferred");
        }

        [TestMethod]
        public void DmacChain_Asr_Registers_RoundTrip()
        {
            var ch = new DmacChannel(0);
            ch.WriteRegister(DmacChannel.REG_ASR0, 0x12345678u);
            ch.WriteRegister(DmacChannel.REG_ASR1, 0xABCDEF00u);
            Assert.AreEqual(0x12345678u, ch.ReadRegister(DmacChannel.REG_ASR0));
            Assert.AreEqual(0xABCDEF00u, ch.ReadRegister(DmacChannel.REG_ASR1));
            Assert.AreEqual(0x12345678u, ch.Asr0);
            Assert.AreEqual(0xABCDEF00u, ch.Asr1);
        }

        [TestMethod]
        public void DmacChain_TteEnabled_TagQwSentToDevice()
        {
            // TTE=1: the tag QW (all 4 words) must be forwarded to the device
            // before the data QWs.
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            uint tagBase = 0x00D00000u;
            // REFE tag: QWC=1, ID=0, ADDR=tagBase+0x10 (data in the same QW area for simplicity)
            uint tagLo = (0u << 24) | 1u;           // ID=REFE, QWC=1
            uint tagHi = tagBase + 0x10u;            // ADDR
            bus.WriteWord(tagBase + 0x00u, tagLo);
            bus.WriteWord(tagBase + 0x04u, tagHi);
            bus.WriteWord(tagBase + 0x08u, 0xDEADu);  // pad words
            bus.WriteWord(tagBase + 0x0Cu, 0xBEEFu);
            // data at ADDR
            bus.WriteWord(tagBase + 0x10u, 0x11111111u);
            bus.WriteWord(tagBase + 0x14u, 0x22222222u);
            bus.WriteWord(tagBase + 0x18u, 0x33333333u);
            bus.WriteWord(tagBase + 0x1Cu, 0x44444444u);

            List<uint> received = new();
            dmac.Channels[0].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            // TTE=1 is bit 6 of CHCR; chain mode; DIR=0; STR=1
            dmac.Channels[0].WriteRegister(DmacChannel.REG_TADR, tagBase);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_CHCR, 0x0144u); // TTE=1,chain,DIR=0,STR=1
            dmac.ExecuteChannel(0, bus);

            // Expected: 4 tag words + 4 data words = 8 words total
            Assert.AreEqual(8, received.Count, "TTE=1 must send tag QW (4 words) then data QW (4 words)");
            // Tag words first
            Assert.AreEqual(tagLo, received[0], "First word of tag QW");
            Assert.AreEqual(tagHi, received[1], "Second word of tag QW");
            Assert.AreEqual(0xDEADu, received[2], "Third word of tag QW");
            Assert.AreEqual(0xBEEFu, received[3], "Fourth word of tag QW");
            // Then data words
            Assert.AreEqual(0x11111111u, received[4], "First data word");
            Assert.AreEqual(0x44444444u, received[7], "Last data word");
        }

        // =====================================================================
        // DMAC interleave mode tests
        // =====================================================================

        [TestMethod]
        public void DmacInterleave_TransfersSlicesAndSkips()
        {
            // D_SQWC: TQWC=2 (bits[23:16]), SQWC=1 (bits[7:0])
            // Transfer 2 QWs, skip 1 QW, transfer 2 QWs, done (total QWC=4)
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            // Lay out 8 QWs at 0x00E00000 (each QW = 16 bytes, first word is the marker):
            // QW0: should be transferred (slice 1, word 0)
            // QW1: should be transferred (slice 1, word 1)
            // QW2: should be SKIPPED
            // QW3: should be transferred (slice 2, word 0)
            // QW4: should be transferred (slice 2, word 1)
            uint baseAddr = 0x00E00000u;
            bus.WriteWord(baseAddr + 0 * 16u, 0xAA000000u);  // QW0 marker
            bus.WriteWord(baseAddr + 1 * 16u, 0xBB000000u);  // QW1 marker
            bus.WriteWord(baseAddr + 2 * 16u, 0xCC000000u);  // QW2 (skipped)
            bus.WriteWord(baseAddr + 3 * 16u, 0xDD000000u);  // QW3 marker
            bus.WriteWord(baseAddr + 4 * 16u, 0xEE000000u);  // QW4 marker

            List<uint> received = new();
            dmac.Channels[2].DataConsumer = (buf, off, len) =>
            {
                for (int i = off; i < off + len; i++) received.Add(buf[i]);
            };

            // D_SQWC: TQWC=2 (transfer 2 per slice), SQWC=1 (skip 1)
            uint sqwc = (2u << 16) | 1u;
            dmac.WriteGlobal(DMAC.REG_SQWC, sqwc);

            dmac.Channels[2].WriteRegister(DmacChannel.REG_MADR, baseAddr);
            dmac.Channels[2].WriteRegister(DmacChannel.REG_QWC,  4u);       // 4 QWs total
            dmac.Channels[2].WriteRegister(DmacChannel.REG_CHCR, 0x0108u);  // MOD=10 (interleave), DIR=0, STR=1
            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR cleared after interleave");
            Assert.AreEqual(0u, dmac.Channels[2].Qwc, "QWC must be 0 after transfer");

            // Only QW0, QW1, QW3, QW4 should be received (16 words = 4*4)
            Assert.AreEqual(16, received.Count, "Must receive exactly 4 QWs (16 words)");

            // First two QWs transferred (markers in word 0 of each QW)
            Assert.AreEqual(0xAA000000u, received[0],  "QW0 word0 must be first");
            Assert.AreEqual(0xBB000000u, received[4],  "QW1 word0 must be second QW");
            // Third QW in received[] is QW3 (skipped QW2)
            Assert.AreEqual(0xDD000000u, received[8],  "QW3 word0 must follow after skip");
            Assert.AreEqual(0xEE000000u, received[12], "QW4 word0 must be last");

            // Skipped QW2 must NOT appear
            Assert.IsFalse(received.Contains(0xCC000000u), "Skipped QW2 must not be in received data");
        }

        [TestMethod]
        public void DmacInterleave_ZeroTqwc_CompletesImmediately()
        {
            var bus  = new PS2MemoryBus();
            var dmac = new DMAC();

            // D_SQWC with TQWC=0 — nothing to transfer
            dmac.WriteGlobal(DMAC.REG_SQWC, 0u);  // TQWC=0, SQWC=0

            dmac.Channels[0].WriteRegister(DmacChannel.REG_MADR, 0x100000u);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_QWC,  4u);
            dmac.Channels[0].WriteRegister(DmacChannel.REG_CHCR, 0x0108u); // interleave
            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(dmac.Channels[0].IsRunning);
            Assert.AreEqual(0u, dmac.Channels[0].Qwc, "QWC must be zeroed even on TQWC=0");
        }

    }
}
