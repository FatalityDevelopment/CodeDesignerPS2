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
    public class TestIopDmac
    {
        // =====================================================================
        // IopDmac unit tests
        // =====================================================================

        [TestMethod]
        public void IopDmac_ChannelCount_Is13()
        {
            Assert.AreEqual(13, IopDmac.ChannelCount,
                "IOP DMAC must have exactly 13 channels");
        }

        [TestMethod]
        public void IopDmacChannel_InitialState_IsNotRunning()
        {
            var ch = new IopDmacChannel(0);
            Assert.IsFalse(ch.IsRunning, "Channel must not be running after construction");
        }

        [TestMethod]
        public void IopDmacChannel_WriteChcr_SetsFields()
        {
            var ch = new IopDmacChannel(4);
            // CHCR: DIR=1 (bit 0), STR=1 (bit 24)
            ch.WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u);
            Assert.IsTrue(ch.IsRunning,          "STR=1 must make IsRunning true");
            Assert.IsTrue(ch.DirectionToMemory,  "DIR=1 must set DirectionToMemory true");
        }

        [TestMethod]
        public void IopDmacChannel_BurstMode_RamToDevice_AdvancesMadr()
        {
            var bus  = new IopMemoryBus();
            // Pre-fill some RAM with non-zero to verify reads are possible
            bus.Ram[0x1000] = 0xAA;

            var dmac = new IopDmac();
            // MADR=0x1000, BCR=4 words, CHCR: DIR=0 (RAM→device), SYNC=00, STR=1
            dmac.Channels[3].WriteRegister(IopDmacChannel.REG_MADR, 0x1000u);
            dmac.Channels[3].WriteRegister(IopDmacChannel.REG_BCR,  4u);
            dmac.Channels[3].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // STR only

            dmac.ExecuteChannel(3, bus);

            Assert.IsFalse(dmac.Channels[3].IsRunning,
                "STR must be cleared after transfer");
            Assert.AreEqual(0x1000u + 4u * 4u, dmac.Channels[3].Madr,
                "MADR must advance by wordCount*4 bytes");
        }

        [TestMethod]
        public void IopDmacChannel_BurstMode_DeviceToRam_ZeroFillsDest()
        {
            var bus  = new IopMemoryBus();
            // Pre-fill destination RAM with non-zero
            for (int i = 0; i < 8; i++) bus.Ram[0x2000 + i] = 0xFF;

            var dmac = new IopDmac();
            // MADR=0x2000, BCR=2 words, CHCR: DIR=1 (device→RAM), STR=1
            dmac.Channels[9].WriteRegister(IopDmacChannel.REG_MADR, 0x2000u);
            dmac.Channels[9].WriteRegister(IopDmacChannel.REG_BCR,  2u);
            dmac.Channels[9].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u); // STR|DIR

            dmac.ExecuteChannel(9, bus);

            Assert.IsFalse(dmac.Channels[9].IsRunning, "STR must be cleared");
            // Destination memory should be zero-filled (2 words = 8 bytes)
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(0, bus.Ram[0x2000 + i],
                    $"Byte at 0x{0x2000 + i:X} must be zero after device→RAM transfer");
        }

        [TestMethod]
        public void IopDmacChannel_ZeroBcr_Treats_As_0x10000_Words()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();
            // BCR=0 should transfer 0x10000 words = 0x40000 bytes
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0x00000000u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // DIR=0, STR=1

            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(dmac.Channels[0].IsRunning);
            Assert.AreEqual(0x40000u, dmac.Channels[0].Madr,
                "BCR=0 must transfer 0x10000 words (0x40000 bytes)");
        }

        [TestMethod]
        public void IopDmac_TransferComplete_EventFires()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  4u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            bool fired = false;
            dmac.Channels[0].TransferComplete += (_, _) => fired = true;

            dmac.ExecuteChannel(0, bus);

            Assert.IsTrue(fired, "TransferComplete event must fire after burst transfer");
        }

        [TestMethod]
        public void IopDmacChannel_LinkedListMode_TraversesNodes()
        {
            // SYNC=2 (linked-list): traverse GPU-style linked list
            // Node format: [31:24]=word count, [23:0]=next node address, 0xFFFFFF=end
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            uint node0 = 0x00001000u;   // first node
            uint node1 = 0x00002000u;   // second node

            // Node 0: 2 data words, next = node1
            bus.WriteWord(node0,        (2u << 24) | node1);  // header
            bus.WriteWord(node0 + 4u,   0xAABBCCDDu);          // data word 0
            bus.WriteWord(node0 + 8u,   0x11223344u);          // data word 1

            // Node 1: 1 data word, next = end
            bus.WriteWord(node1,        (1u << 24) | 0x00FFFFFFu); // header (end)
            bus.WriteWord(node1 + 4u,   0xDEADBEEFu);              // data word 0

            List<uint> received = new();
            dmac.Channels[2].DeviceDataConsumer = w => received.Add(w);

            // SYNC=2 (bits [9:8]=10), DIR=0 (RAM→device), STR=1 (bit24)
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, node0);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | (2u << 8)); // STR,SYNC=2

            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR must be cleared after linked-list DMA");
            Assert.AreEqual(3, received.Count, "Must receive 2+1=3 data words from two nodes");
            Assert.AreEqual(0xAABBCCDDu, received[0], "First word of node 0");
            Assert.AreEqual(0x11223344u, received[1], "Second word of node 0");
            Assert.AreEqual(0xDEADBEEFu, received[2], "First word of node 1");
        }

        [TestMethod]
        public void IopDmacChannel_LinkedListMode_SingleNodeEnd()
        {
            // Single node with end marker (next = 0xFFFFFF) and no data words
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            uint node0 = 0x00003000u;
            bus.WriteWord(node0, (0u << 24) | 0x00FFFFFFu); // 0 data words, end marker

            int callCount = 0;
            dmac.Channels[2].DeviceDataConsumer = _ => callCount++;

            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, node0);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | (2u << 8));

            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR cleared");
            Assert.AreEqual(0, callCount, "Empty single-node list must not invoke consumer");
        }

        [TestMethod]
        public void IopDmacChannel_LinkedListMode_TransferComplete_Fires()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            uint node0 = 0x00004000u;
            bus.WriteWord(node0, (0u << 24) | 0x00FFFFFFu); // end

            bool fired = false;
            dmac.Channels[2].TransferComplete += (_, _) => fired = true;

            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, node0);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | (2u << 8));

            dmac.ExecuteChannel(2, bus);

            Assert.IsTrue(fired, "TransferComplete must fire after linked-list DMA");
        }

        [TestMethod]
        public void IopDmac_Dicr_IrqFlag_SetOnCompletion()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Channel 2 (GPU), enable its IEN bit in DICR [18] and master MIE [23]
            // IEN bit for ch2 = bit 16+2 = 18
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 18));

            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            dmac.ExecuteChannel(2, bus);

            // IRQ flag for ch2 = bit 24+2 = 26 in DICR
            bool iqfSet = (dmac.Dicr & (1u << 26)) != 0;
            Assert.IsTrue(iqfSet, "DICR IQF bit for ch2 must be set after transfer");
        }

        [TestMethod]
        public void IopDmac_Dicr_MasterFlag_SetWhenEnabled()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Enable ch0 IEN and master MIE
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 16));

            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            dmac.ExecuteChannel(0, bus);

            Assert.IsTrue((dmac.Dicr & (1u << 31)) != 0,
                "DICR bit 31 (MIRQ) must be set when MIE=1, IEN=1, IQF=1");
        }

        [TestMethod]
        public void IopDmac_Dicr_WriteClearsIrqFlag()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Enable and complete a transfer on ch1
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 17));
            dmac.Channels[1].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[1].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[1].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);
            dmac.ExecuteChannel(1, bus);

            // Verify flag is set
            Assert.IsTrue((dmac.Dicr & (1u << 25)) != 0, "IQF bit for ch1 must be set");

            // Write 1 to bit 25 to clear the flag (W1C semantics)
            uint current = dmac.Dicr;
            dmac.WriteGlobal(IopDmac.AddrDicr, current | (1u << 25));

            Assert.IsFalse((dmac.Dicr & (1u << 25)) != 0,
                "IQF bit for ch1 must be cleared after W1C write");
        }

        [TestMethod]
        public void IopDmac_ChannelTransferComplete_EventFired_WhenEnabled()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // ch0 IEN=1, MIE=1
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 16));
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            int completedChannel = -1;
            dmac.ChannelTransferComplete += (_, ch) => completedChannel = ch;

            dmac.ExecuteChannel(0, bus);

            Assert.AreEqual(0, completedChannel,
                "ChannelTransferComplete must fire with channel index 0");
        }

        [TestMethod]
        public void IopDmac_ChannelTransferComplete_NotFired_WhenDisabled()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // IEN=0, MIE=0 — no interrupt enabled
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            bool fired = false;
            dmac.ChannelTransferComplete += (_, _) => fired = true;

            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(fired,
                "ChannelTransferComplete must NOT fire when IEN and MIE are both 0");
        }

        [TestMethod]
        public void IopDmac_Dicr2_Ch7_IrqFlag_SetOnCompletion()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Channel 7 (SPU2), enable its IEN bit in DICR2 [16] and master MIE [23]
            dmac.WriteGlobal(IopDmac.AddrDicr2, (1u << 23) | (1u << 16));
            dmac.Channels[7].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[7].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[7].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            dmac.ExecuteChannel(7, bus);

            // Local index 0 → IQF bit 24 in DICR2
            Assert.IsTrue((dmac.Dicr2 & (1u << 24)) != 0,
                "DICR2 IQF bit for ch7 (local 0) must be set after transfer");
            Assert.IsTrue((dmac.Dicr2 & (1u << 31)) != 0,
                "DICR2 MIRQ must be set when MIE=1 and ch7 IEN+IQF are both set");
        }

        [TestMethod]
        public void IopMemoryBus_DmacChannelReadWrite_RoundTrip()
        {
            var bus = new IopMemoryBus();

            // Write MADR via memory-mapped I/O for channel 3 (CDVD at 0x1F8010B0)
            bus.WriteWord(0x1F8010B0u, 0x00080000u); // MADR
            bus.WriteWord(0x1F8010B4u, 8u);           // BCR
            Assert.AreEqual(0x00080000u, bus.ReadWord(0x1F8010B0u), "MADR round-trip via bus");
            Assert.AreEqual(8u,          bus.ReadWord(0x1F8010B4u), "BCR round-trip via bus");
        }

        [TestMethod]
        public void IopMemoryBus_DmacChannelWrite_StartsTransfer()
        {
            var bus = new IopMemoryBus();
            // Fill destination RAM
            for (int i = 0; i < 8; i++) bus.Ram[0x3000 + i] = 0xFF;

            // Channel 10 (SIF1) at 0x1F801530: device→RAM, 2 words
            bus.WriteWord(0x1F801530u, 0x3000u);          // MADR
            bus.WriteWord(0x1F801534u, 2u);                // BCR
            bus.WriteWord(0x1F801538u, (1u << 24) | 1u); // CHCR: STR=1, DIR=1

            // Transfer should have executed synchronously on the CHCR write
            Assert.IsFalse(bus.Dmac.Channels[10].IsRunning,
                "STR must be auto-cleared after CHCR write triggers transfer");
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(0, bus.Ram[0x3000 + i],
                    $"RAM byte 0x{0x3000 + i:X} must be zero-filled by SIF1 transfer");
        }

        [TestMethod]
        public void IopMemoryBus_DmacGlobalReadWrite_RoundTrip()
        {
            var bus = new IopMemoryBus();
            bus.WriteWord(IopDmac.AddrDpcr,  0x08765432u);
            bus.WriteWord(IopDmac.AddrDpcr2, 0x0FEDCBA9u);
            Assert.AreEqual(0x08765432u, bus.ReadWord(IopDmac.AddrDpcr),  "DPCR round-trip");
            Assert.AreEqual(0x0FEDCBA9u, bus.ReadWord(IopDmac.AddrDpcr2), "DPCR2 round-trip");
        }

        [TestMethod]
        public void IopEmulator_DmacCompletion_RaisesIntcBit3()
        {
            var emu = new IopEmulator();

            // Enable DMAC interrupt in IOP INTC (bit 3) and ch9/SIF0 IEN in DICR2
            emu.Bus.WriteWord(0x1F801074u, 1u << 3);         // I_MASK bit 3
            // ch9 in IopDmac is channels[9], local idx in DICR2 = 9-7=2 → IEN bit 16+2=18, MIE bit 23
            emu.Bus.WriteWord(IopDmac.AddrDicr2, (1u << 23) | (1u << 18));

            // Set up a small SIF0 transfer (ch9 at 0x1F801520)
            emu.Bus.WriteWord(0x1F801520u, 0x00001000u);   // MADR
            emu.Bus.WriteWord(0x1F801524u, 2u);             // BCR
            emu.Bus.WriteWord(0x1F801528u, 1u << 24);      // CHCR: STR=1, DIR=0

            // TickHardware raises InterruptPending when I_STAT & I_MASK != 0
            emu.TickHardware(0); // just check current pending state

            Assert.IsTrue((emu.Bus.IStat & (1u << 3)) != 0,
                "I_STAT bit 3 must be set after DMAC transfer completes");
        }



        /// <summary>ADDIU rt, rs, imm  (opcode=0x09)</summary>
        private static uint MakeAddiu(int rs, int rt, int imm)
            => (0x09u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        /// <summary>ORI rt, rs, imm  (opcode=0x0D)</summary>
        private static uint MakeOri(int rs, int rt, int imm)
            => (0x0Du << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        /// <summary>LUI rt, imm  (opcode=0x0F, rs=0)</summary>
        private static uint MakeLui(int rt, int imm)
            => (0x0Fu << 26) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        /// <summary>BEQ rs, rt, offset  (opcode=0x04)</summary>
        private static uint MakeBEQ(int rs, int rt, int offset)
            => (0x04u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(offset & 0xFFFF);

        /// <summary>OR rd, rs, rt  (opcode=0, funct=0x25)</summary>
        private static uint MakeOr(int rs, int rt, int rd)
            => ((uint)rs << 21) | ((uint)rt << 16) | ((uint)rd << 11) | 0x25u;

    }
}
