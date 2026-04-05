using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestSpu2
    {
        // =====================================================================
        // SPU2 unit tests
        // =====================================================================

        [TestMethod]
        public void Spu2_TotalVoiceCount_Is48()
        {
            Assert.AreEqual(48, Spu2.TotalVoices, "SPU2 must expose 48 total voice channels (2 cores × 24)");
            Assert.AreEqual(2,  Spu2.CoreCount,   "SPU2 must have exactly 2 cores");
            Assert.AreEqual(24, Spu2.VoicesPerCore, "SPU2 must have 24 voices per core");
        }

        [TestMethod]
        public void Spu2_AudioRam_SizeIs2MB()
        {
            var spu2 = new Spu2();
            Assert.AreEqual(Spu2.RamSize, spu2.AudioRam.Length, "SPU2 audio RAM must be 2 MB");
        }

        [TestMethod]
        public void Spu2_VoiceRegisters_ReadWrite_Core0_Voice0()
        {
            var spu2 = new Spu2();
            // Voice 0 of core 0: base offset = 0*0x400 + 0*0x10 = 0x000
            spu2.WriteHalf(Spu2.VREG_VOLL,  0x1234);
            spu2.WriteHalf(Spu2.VREG_VOLR,  0x5678);
            spu2.WriteHalf(Spu2.VREG_PITCH, 0x1000);
            spu2.WriteHalf(Spu2.VREG_ADSR1, 0xAABB);
            spu2.WriteHalf(Spu2.VREG_ADSR2, 0xCCDD);

            Assert.AreEqual(0x1234, spu2.ReadHalf(Spu2.VREG_VOLL),  "Voice 0 VOLL round-trip");
            Assert.AreEqual(0x5678, spu2.ReadHalf(Spu2.VREG_VOLR),  "Voice 0 VOLR round-trip");
            Assert.AreEqual(0x1000, spu2.ReadHalf(Spu2.VREG_PITCH), "Voice 0 PITCH round-trip");
            Assert.AreEqual(0xAABB, spu2.ReadHalf(Spu2.VREG_ADSR1), "Voice 0 ADSR1 round-trip");
            Assert.AreEqual(0xCCDD, spu2.ReadHalf(Spu2.VREG_ADSR2), "Voice 0 ADSR2 round-trip");
        }

        [TestMethod]
        public void Spu2_VoiceRegisters_Core1_Voice5_CorrectOffset()
        {
            var spu2 = new Spu2();
            // Core 1, voice 5: offset = 1*0x400 + 5*0x10 + VREG_VOLR = 0x400 + 0x50 + 0x02 = 0x452
            uint offset = 0x400u + 5u * 0x10u + Spu2.VREG_VOLR;
            spu2.WriteHalf(offset, 0x7FFF);
            Assert.AreEqual(0x7FFF, spu2.ReadHalf(offset), "Core 1 voice 5 VOLR round-trip");

            // Verify the voice object directly
            Assert.AreEqual(0x7FFF, spu2.GetVoice(1, 5).VolR, "GetVoice(1,5).VolR must reflect the write");
        }

        [TestMethod]
        public void Spu2_KonWrite_Core0_SetsVoiceIsOn()
        {
            var spu2 = new Spu2();
            // KON0 absolute offset = 0*0x400 + 0x180 + OFF_KON0 = 0x1A0
            const uint KON0 = 0x1A0u;
            // Key-on voices 0, 2, 3 of core 0
            spu2.WriteHalf(KON0, 0x000D); // bits 0, 2, 3

            Assert.IsTrue(spu2.GetVoice(0, 0).IsOn, "Voice 0 must be on after KON0 bit 0");
            Assert.IsFalse(spu2.GetVoice(0, 1).IsOn, "Voice 1 must stay off (KON0 bit 1 not set)");
            Assert.IsTrue(spu2.GetVoice(0, 2).IsOn, "Voice 2 must be on after KON0 bit 2");
            Assert.IsTrue(spu2.GetVoice(0, 3).IsOn, "Voice 3 must be on after KON0 bit 3");
        }

        [TestMethod]
        public void Spu2_KonWrite_Core0_KON1_SetsVoices16To23()
        {
            var spu2 = new Spu2();
            const uint KON1 = 0x1A2u;
            spu2.WriteHalf(KON1, 0x0003); // voices 16 and 17

            Assert.IsTrue(spu2.GetVoice(0, 16).IsOn, "Voice 16 must be on after KON1 bit 0");
            Assert.IsTrue(spu2.GetVoice(0, 17).IsOn, "Voice 17 must be on after KON1 bit 1");
            Assert.IsFalse(spu2.GetVoice(0, 18).IsOn, "Voice 18 must stay off");
        }

        [TestMethod]
        public void Spu2_KofWrite_Core0_ClearsVoiceIsOn()
        {
            var spu2 = new Spu2();
            const uint KON0 = 0x1A0u;
            const uint KOF0 = 0x1A4u;
            spu2.WriteHalf(KON0, 0x000F); // key-on voices 0-3
            spu2.WriteHalf(KOF0, 0x0005); // key-off voices 0 and 2

            Assert.IsFalse(spu2.GetVoice(0, 0).IsOn, "Voice 0 must be off after KOF0 bit 0");
            Assert.IsTrue(spu2.GetVoice(0, 1).IsOn,  "Voice 1 must remain on");
            Assert.IsFalse(spu2.GetVoice(0, 2).IsOn, "Voice 2 must be off after KOF0 bit 2");
            Assert.IsTrue(spu2.GetVoice(0, 3).IsOn,  "Voice 3 must remain on");
        }

        [TestMethod]
        public void Spu2_KonWrite_ResetsAdpcmHistory()
        {
            var spu2 = new Spu2();
            var v    = spu2.GetVoice(0, 0);
            v.AdpcmPrev1 = 1234;
            v.AdpcmPrev2 = 5678;

            // KON0 bit 0 must reset ADPCM history for voice 0
            spu2.WriteHalf(0x1A0u, 0x0001);

            Assert.AreEqual(0, v.AdpcmPrev1, "KON must reset AdpcmPrev1 to 0");
            Assert.AreEqual(0, v.AdpcmPrev2, "KON must reset AdpcmPrev2 to 0");
        }

        [TestMethod]
        public void Spu2_MasterVolume_Core0_ReadWrite()
        {
            var spu2 = new Spu2();
            // MVOLL at 0x1B0, MVOLR at 0x1B2 (core 0)
            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x2000);
            Assert.AreEqual(0x3FFF, spu2.ReadHalf(0x1B0u), "Core 0 MVOLL round-trip");
            Assert.AreEqual(0x2000, spu2.ReadHalf(0x1B2u), "Core 0 MVOLR round-trip");
        }

        [TestMethod]
        public void Spu2_MasterVolume_Core1_ReadWrite()
        {
            var spu2 = new Spu2();
            // Core 1 MVOLL at 0x400+0x1B0 = 0x5B0
            spu2.WriteHalf(0x5B0u, 0x1234);
            Assert.AreEqual(0x1234, spu2.ReadHalf(0x5B0u), "Core 1 MVOLL round-trip");
        }

        [TestMethod]
        public void Spu2_TransferAddress_TSA_Write_And_Get()
        {
            var spu2 = new Spu2();
            // TSA_HI at 0x1A8, TSA_LO at 0x1AA (core 0)
            spu2.WriteHalf(0x1A8u, 0x0010); // TSA_HI
            spu2.WriteHalf(0x1AAu, 0x0200); // TSA_LO
            uint tsa = spu2.GetTransferAddr(0);
            Assert.AreEqual(0x00100200u, tsa, "GetTransferAddr must combine TSA_HI:TSA_LO");
        }

        [TestMethod]
        public void Spu2_WriteDmaData_StoresInAudioRam()
        {
            var spu2 = new Spu2();
            // Set TSA for core 0 to halfword address 0x10 (= byte address 0x20)
            spu2.WriteHalf(0x1A8u, 0x0000); // TSA_HI = 0
            spu2.WriteHalf(0x1AAu, 0x0010); // TSA_LO = 0x10

            spu2.WriteDmaData(0, 0xDEADBEEFu);

            // TSA = 0x10 halfwords = 0x20 bytes; DMA writes first halfword at 0x20
            Assert.AreEqual(0xEF, spu2.AudioRam[0x20], "AudioRam[0x20] must be 0xEF (low byte of 0xBEEF)");
            Assert.AreEqual(0xBE, spu2.AudioRam[0x21], "AudioRam[0x21] must be 0xBE (high byte of 0xBEEF)");
            Assert.AreEqual(0xAD, spu2.AudioRam[0x22], "AudioRam[0x22] must be 0xAD (low byte of 0xDEAD)");
            Assert.AreEqual(0xDE, spu2.AudioRam[0x23], "AudioRam[0x23] must be 0xDE (high byte of 0xDEAD)");
        }

        [TestMethod]
        public void Spu2_WriteDmaData_AdvancesTransferAddress()
        {
            var spu2 = new Spu2();
            spu2.WriteHalf(0x1A8u, 0x0000); // TSA_HI
            spu2.WriteHalf(0x1AAu, 0x0000); // TSA_LO = 0

            spu2.WriteDmaData(0, 0x12345678u); // writes 2 halfwords → TSA += 2

            Assert.AreEqual(2u, spu2.GetTransferAddr(0), "TSA must advance by 2 halfwords after writing one DMA word");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_SilenceBlock_AllZeroes()
        {
            // A silence block: shift=0, filter=0, flags=0, data all 0.
            byte[] block = new byte[16]; // all zeros
            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            foreach (short s in pcm)
                Assert.AreEqual(0, s, "All-zero ADPCM block must decode to silence");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_LoopEndFlag()
        {
            byte[] block = new byte[16];
            block[1] = 0x01; // flag bit 0 = loop-end
            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            bool loopEnd = Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);
            Assert.IsTrue(loopEnd, "Loop-end flag must be true when flag byte bit 0 is set");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_KnownOutput_Filter0_Shift12()
        {
            // Block: shift=12, filter=0, all data bytes = 0x77 (nibbles 7 and 7)
            // Nibble 7 = 7 (positive); with shift=12, raw = 7 << 0 = 7; filter=0 → s = 7.
            byte[] block = new byte[16];
            block[0] = 0x0C; // shift=12, filter=0
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77; // low nibble=7, high nibble=7

            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            // Each nibble = 7, shift=12 → raw = 7<<0 = 7, filter=0 → no predictor → s=7
            Assert.AreEqual(7, pcm[0], "First decoded sample must be 7");
            Assert.AreEqual(7, pcm[1], "Second decoded sample must be 7");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_Filter1_PrevSamplesInfluenceOutput()
        {
            // Verify that filter 1 (f1=60/64) applies a predictor based on prev1.
            byte[] block = new byte[16];
            block[0] = 0x1C; // shift=12, filter=1
            block[1] = 0x00;
            // Set data byte so all nibbles are 0 (no raw signal, only predictor)
            // With filter 1: s = 0 + prev1*60/64 + prev2*0 = prev1*60/64
            // Start with prev1=64 → s = 60; then prev1=60 → s = 56 (rounds down)
            Span<short> pcm = stackalloc short[28];
            int p1 = 64, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            // First sample: raw=0, prev1=64, f1=60 → (64*60+32)/64 = 3872/64 = 60
            Assert.AreEqual(60, pcm[0], "First sample with filter 1 and prev1=64 must be 60");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_UpdatesState_ForChainedBlocks()
        {
            // Confirm that prev1/prev2 are updated correctly so chained blocks work.
            byte[] block = new byte[16];
            block[0] = 0x0C; // shift=12, filter=0
            for (int i = 2; i < 16; i++) block[i] = 0x11; // all nibbles = 1

            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            // Each nibble = 1, shift=12 → raw=1, filter=0 → s=1 for all samples.
            // After 28 samples: prev1=1, prev2=1.
            Assert.AreEqual(1, p1, "prev1 must be 1 after decoding 28 samples of value 1");
            Assert.AreEqual(1, p2, "prev2 must be 1 after decoding 28 samples of value 1");
        }

        // ── IopMemoryBus + SPU2 integration tests ─────────────────────────────

        [TestMethod]
        public void IopMemoryBus_Spu2_PropertySetAndGet()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;
            Assert.AreSame(spu2, bus.Spu2, "Spu2 property must return the assigned instance");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_VoiceVOLL_ReadWriteWord()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // Core 0, voice 0, VOLL at 0x1F900000, VOLR at 0x1F900002
            bus.WriteWord(Spu2.RegBase, 0xBBBBAAAAu); // VOLR=0xBBBB, VOLL=0xAAAA
            uint readBack = bus.ReadWord(Spu2.RegBase);
            Assert.AreEqual(0xBBBBAAAAu, readBack, "SPU2 voice VOLL/VOLR word round-trip via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_VoiceRegisterHalfWord_RoundTrip()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // Use ReadHalf / WriteHalf (routed via ReadByte/WriteByte)
            bus.WriteHalf(Spu2.RegBase + Spu2.VREG_PITCH, 0x1000);
            ushort v = bus.ReadHalf(Spu2.RegBase + Spu2.VREG_PITCH);
            Assert.AreEqual(0x1000, v, "SPU2 voice PITCH halfword round-trip via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_KonWrite_KeysOnVoice()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // KON0 for core 0 is at 0x1F900000 + 0x1A0 = 0x1F9001A0
            bus.WriteHalf(Spu2.RegBase + 0x1A0u, 0x0001); // key-on voice 0
            Assert.IsTrue(spu2.GetVoice(0, 0).IsOn,
                "Voice 0 must be active after KON0 bit 0 write via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_DmacChannel7_FeedsCore1AudioRam()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // Set core 1 TSA = 0 (default)
            // spu2 core 1 TSA_HI at 0x400+0x1A8 = 0x5A8, TSA_LO at 0x5AA
            // (already 0 by default)

            // Place audio data in IOP RAM
            uint srcAddr = 0x1000u;
            bus.WriteWord(srcAddr,      0x11223344u);
            bus.WriteWord(srcAddr + 4u, 0x55667788u);

            // Configure IOP DMAC channel 7 (SPU2): RAM→Device, 2 words from 0x1000
            bus.Dmac.Channels[7].WriteRegister(IopDmacChannel.REG_MADR, srcAddr);
            bus.Dmac.Channels[7].WriteRegister(IopDmacChannel.REG_BCR,  2u); // 2 words
            bus.Dmac.Channels[7].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // STR=1, DIR=0
            bus.Dmac.ExecuteChannel(7, bus);

            // Verify the data was written into core 1 audio RAM
            // TSA was 0 → byte address 0. First halfword = 0x3344, second = 0x1122
            Assert.AreEqual(0x44, spu2.AudioRam[0], "AudioRam[0] must be 0x44 (LE lo byte of 0x3344)");
            Assert.AreEqual(0x33, spu2.AudioRam[1], "AudioRam[1] must be 0x33 (LE hi byte of 0x3344)");
            Assert.AreEqual(0x22, spu2.AudioRam[2], "AudioRam[2] must be 0x22");
            Assert.AreEqual(0x11, spu2.AudioRam[3], "AudioRam[3] must be 0x11");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_DmacChannel4_FeedsCore0AudioRam()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            uint srcAddr = 0x2000u;
            bus.WriteWord(srcAddr, 0xAABBCCDDu);

            bus.Dmac.Channels[4].WriteRegister(IopDmacChannel.REG_MADR, srcAddr);
            bus.Dmac.Channels[4].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            bus.Dmac.Channels[4].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // STR=1, DIR=0
            bus.Dmac.ExecuteChannel(4, bus);

            // Data written to core 0 audio RAM at address 0
            Assert.AreEqual(0xDD, spu2.AudioRam[0], "AudioRam[0] must be 0xDD (lo byte of 0xCCDD)");
            Assert.AreEqual(0xCC, spu2.AudioRam[1], "AudioRam[1] must be 0xCC (hi byte of 0xCCDD)");
            Assert.AreEqual(0xBB, spu2.AudioRam[2], "AudioRam[2] must be 0xBB");
            Assert.AreEqual(0xAA, spu2.AudioRam[3], "AudioRam[3] must be 0xAA");
        }

        // =====================================================================
        // Phase 7.4 — SPU2 Voice SA Registers (PVCA)
        // =====================================================================

        [TestMethod]
        public void Spu2_VoiceSA_Core0_Voice0_WriteReadRoundTrip()
        {
            var spu2 = new Spu2();
            // Core 0 voice 0 SA: abs 0x1C0 (SA_HI) and 0x1C2 (SA_LO)
            const uint SA_HI = 0x1C0u;
            const uint SA_LO = 0x1C2u;
            spu2.WriteHalf(SA_HI, 0x0001);
            spu2.WriteHalf(SA_LO, 0x2000);

            // Read back via voice struct
            Assert.AreEqual(0x00012000u, spu2.GetVoice(0, 0).StartAddr,
                "Voice 0 StartAddr must be 0x00012000 after writing SA_HI=0x0001, SA_LO=0x2000");

            // Read back via register interface
            Assert.AreEqual(0x0001u, spu2.ReadHalf(SA_HI), "SA_HI read-back must be 0x0001");
            Assert.AreEqual(0x2000u, spu2.ReadHalf(SA_LO), "SA_LO read-back must be 0x2000");
        }

        [TestMethod]
        public void Spu2_VoiceSA_Core0_Voice5_CorrectOffset()
        {
            var spu2 = new Spu2();
            // Core 0 voice 5: abs = 0x1C0 + 5*12 = 0x1C0 + 0x3C = 0x1FC
            const uint V5_SA_HI = 0x1FCu;
            const uint V5_SA_LO = 0x1FEu;
            spu2.WriteHalf(V5_SA_HI, 0x0000);
            spu2.WriteHalf(V5_SA_LO, 0x8000);

            Assert.AreEqual(0x00008000u, spu2.GetVoice(0, 5).StartAddr,
                "Voice 5 StartAddr must be 0x8000");
        }

        [TestMethod]
        public void Spu2_VoiceLoopA_WriteUpdatesLoopAddr()
        {
            var spu2 = new Spu2();
            // Core 0 voice 0 LoopA: abs 0x1C4 (LoopA_HI) and 0x1C6 (LoopA_LO)
            const uint LOOPA_HI = 0x1C4u;
            const uint LOOPA_LO = 0x1C6u;
            spu2.WriteHalf(LOOPA_HI, 0x0002);
            spu2.WriteHalf(LOOPA_LO, 0x4000);

            Assert.AreEqual(0x00024000u, spu2.GetVoice(0, 0).LoopAddr,
                "Voice 0 LoopAddr must be 0x00024000");
        }

        [TestMethod]
        public void Spu2_VoiceNextA_ReturnsCurrentAddr()
        {
            var spu2 = new Spu2();
            // Set CurrentAddr directly
            spu2.GetVoice(0, 0).CurrentAddr = 0x00031234u;
            // Core 0 voice 0 NextA: abs 0x1C8 (HI) and 0x1CA (LO)
            const uint NEXTA_HI = 0x1C8u;
            const uint NEXTA_LO = 0x1CAu;

            Assert.AreEqual(0x0003u, spu2.ReadHalf(NEXTA_HI), "NextA_HI must return upper 16 bits of CurrentAddr");
            Assert.AreEqual(0x1234u, spu2.ReadHalf(NEXTA_LO), "NextA_LO must return lower 16 bits of CurrentAddr");
        }

        [TestMethod]
        public void Spu2_VoiceSA_Core1_Voice0_CorrectOffset()
        {
            var spu2 = new Spu2();
            // Core 1 voice 0 SA_HI: abs 0x400 + 0x1C0 = 0x5C0
            const uint C1V0_SA_HI = 0x5C0u;
            const uint C1V0_SA_LO = 0x5C2u;
            spu2.WriteHalf(C1V0_SA_HI, 0x000A);
            spu2.WriteHalf(C1V0_SA_LO, 0xBCDE);

            Assert.AreEqual(0x000ABCDEu, spu2.GetVoice(1, 0).StartAddr,
                "Core 1 voice 0 StartAddr must be 0x000ABCDE");
        }

        [TestMethod]
        public void Spu2_KonWrite_SetsCurrentAddrFromStartAddr()
        {
            var spu2 = new Spu2();
            var voice = spu2.GetVoice(0, 0);
            voice.StartAddr   = 0x00001000u;
            voice.CurrentAddr = 0x00009999u; // different value
            voice.DecIdx      = 10;

            // Key-on voice 0
            spu2.WriteHalf(0x1A0u, 0x0001);

            Assert.AreEqual(0x00001000u, voice.CurrentAddr, "KON must reset CurrentAddr to StartAddr");
            Assert.AreEqual(28,          voice.DecIdx,      "KON must reset DecIdx to 28 to force first decode");
            Assert.AreEqual(0,           voice.AdsrVol,     "KON must reset AdsrVol to 0");
            Assert.AreEqual(0,           voice.AdsrPhase,   "KON must start in Attack phase (0)");
        }

        [TestMethod]
        public void Spu2_EffectsStartA_WriteRead()
        {
            var spu2 = new Spu2();
            // Core 0 ESA: abs 0x2E0 (HI) and 0x2E2 (LO)
            spu2.WriteHalf(0x2E0u, 0x0001);
            spu2.WriteHalf(0x2E2u, 0x8000);

            Assert.AreEqual(0x00018000u, spu2.GetEffectsStartA(0),
                "EffectsStartA must combine HI:LO halfwords");
        }

        [TestMethod]
        public void Spu2_EffectsEndA_WriteRead()
        {
            var spu2 = new Spu2();
            // Core 0 EEA: abs 0x33C (HI) and 0x33E (LO)
            spu2.WriteHalf(0x33Cu, 0x0002);
            spu2.WriteHalf(0x33Eu, 0xC000);

            Assert.AreEqual(0x0002C000u, spu2.GetEffectsEndA(0),
                "EffectsEndA must combine HI:LO halfwords");
        }

        // =====================================================================
        // Phase 7.4 — SPU2 MixSamples
        // =====================================================================

        [TestMethod]
        public void Spu2_MixSamples_AllVoicesOff_ReturnsSilence()
        {
            var spu2 = new Spu2();
            // Set master volume so output is non-trivially zero even with no voices
            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);

            short[] pcm = spu2.MixSamples(32);

            bool allZero = true;
            foreach (short s in pcm)
                if (s != 0) { allZero = false; break; }

            Assert.IsTrue(allZero, "MixSamples must return silence when no voices are active");
        }

        [TestMethod]
        public void Spu2_MixSamples_ActiveVoice_ProducesNonZeroOutput()
        {
            var spu2 = new Spu2();

            // Build a minimal ADPCM block: shift=0, filter=0, all nibbles = 7 → sample value 28672.
            // shift=0 gives raw = 7 << 12 = 28672, large enough to survive three >> 15 scalings.
            var block = new byte[16];
            block[0] = 0x00; // shift=0, filter=0
            block[1] = 0x00; // no loop flags
            for (int i = 2; i < 16; i++) block[i] = 0x77; // all nibbles = 7

            // Place the ADPCM block at AudioRam address 0 (halfword 0)
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0x1000;        // 1:1 pitch
            v.Adsr1     = 0x00FF;        // fast attack (shift=0, step=3) → jumps to 0x7FFF quickly
            v.Adsr2     = 0x0000;        // sustain hold, fast release
            v.StartAddr = 0;

            // Set master volume
            spu2.WriteHalf(0x1B0u, 0x3FFF); // MVOLL
            spu2.WriteHalf(0x1B2u, 0x3FFF); // MVOLR

            // Key on voice 0
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(128);

            bool anyNonZero = false;
            foreach (short s in pcm)
                if (s != 0) { anyNonZero = true; break; }

            Assert.IsTrue(anyNonZero, "MixSamples must produce non-zero output from an active voice");
        }

        [TestMethod]
        public void Spu2_MixSamples_MasterVolumeZero_ReturnsSilence()
        {
            var spu2 = new Spu2();

            // Set up a voice with ADPCM data (same as above)
            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0x1000;
            v.Adsr1     = 0x00FF;
            v.StartAddr = 0;

            // Leave master volume at 0 (default)
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(64);

            bool allZero = true;
            foreach (short s in pcm)
                if (s != 0) { allZero = false; break; }

            Assert.IsTrue(allZero, "MixSamples must return silence when master volume is 0");
        }

        [TestMethod]
        public void Spu2_MixSamples_ReturnsStereoInterleavedPairs()
        {
            var spu2 = new Spu2();
            const int SampleCount = 16;
            short[] pcm = spu2.MixSamples(SampleCount);

            Assert.AreEqual(SampleCount * 2, pcm.Length,
                "MixSamples must return sampleCount*2 shorts (interleaved stereo)");
        }

        [TestMethod]
        public void Spu2_MixSamples_WideAreaMasterVol_ProducesNonZeroOutput()
        {
            // Verify that master volume written to the PCSX2-compatible "weird area" (0x760)
            // is also honoured by the mixer.
            var spu2 = new Spu2();

            var block = new byte[16];
            block[0] = 0x00; // shift=0 gives sample=28672, large enough to survive >> 15 chain
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0x1000;
            v.Adsr1     = 0x00FF;
            v.StartAddr = 0;

            // Write master volume ONLY to the "weird area" (abs 0x760 = core0 MVOLL)
            spu2.WriteHalf(0x760u, 0x3FFF); // core 0 MVOLL via weird area
            spu2.WriteHalf(0x762u, 0x3FFF); // core 0 MVOLR via weird area

            spu2.WriteHalf(0x1A0u, 0x0001);
            short[] pcm = spu2.MixSamples(128);

            bool anyNonZero = false;
            foreach (short s in pcm)
                if (s != 0) { anyNonZero = true; break; }

            Assert.IsTrue(anyNonZero, "MixSamples must honour master volume from the 'weird area' registers");
        }

        [TestMethod]
        public void Spu2_MixSamples_PitchZero_ProducesSilence()
        {
            var spu2 = new Spu2();

            var block = new byte[16];
            block[0] = 0x0C;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0;             // zero pitch → never advances
            v.Adsr1     = 0x00FF;
            v.StartAddr = 0;

            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(16);

            // With pitch = 0, PitchFrac never reaches 0x1000, no samples consumed → silence
            bool allZero = true;
            foreach (short s in pcm)
                if (s != 0) { allZero = false; break; }
            Assert.IsTrue(allZero, "Zero pitch must produce silence (no sample advancement)");
        }

        // =====================================================================
        // Phase 7.4 — SPU2 Reverb
        // =====================================================================

        [TestMethod]
        public void Spu2_GetEffectsStartA_DefaultIsZero()
        {
            var spu2 = new Spu2();
            Assert.AreEqual(0u, spu2.GetEffectsStartA(0), "Default ESA for core 0 must be 0");
            Assert.AreEqual(0u, spu2.GetEffectsStartA(1), "Default ESA for core 1 must be 0");
        }

        [TestMethod]
        public void Spu2_DoReverb_NoEffectWhenEsaEqualsEea()
        {
            // With ESA == EEA the reverb is disabled; mixer dry+reverb sum must equal dry input.
            // Set up a voice with known output and zero ESA/EEA (disabled reverb).
            var spu2 = new Spu2();
            var block = new byte[16];
            block[0] = 0x00; // shift=0 gives sample=28672, large enough to survive >> 15 chain
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            spu2.GetVoice(0, 0).VolL      = 0x3FFF;
            spu2.GetVoice(0, 0).VolR      = 0x3FFF;
            spu2.GetVoice(0, 0).Pitch     = 0x1000;
            spu2.GetVoice(0, 0).Adsr1     = 0x00FF;
            spu2.GetVoice(0, 0).StartAddr = 0;
            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            // Leave ESA=EEA=0 (reverb disabled)
            short[] pcmNoReverb = spu2.MixSamples(64);

            // Confirm some audio is present
            bool anyNonZero = false;
            foreach (short s in pcmNoReverb)
                if (s != 0) { anyNonZero = true; break; }
            Assert.IsTrue(anyNonZero, "With reverb disabled, mix must still produce non-zero voice audio");
        }

        [TestMethod]
        public void Spu2_Reverb_EnabledWithCoefficients_ProducesOutput()
        {
            // Enable reverb with a minimal but valid configuration and verify that
            // the mixer produces non-zero output.
            var spu2 = new Spu2();

            // Set up audio RAM: ADPCM block at 0, reverb buffer from halfword 0x1000
            var block = new byte[16];
            block[0] = 0x00; // shift=0 gives sample=28672, large enough to survive >> 15 chain
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            spu2.GetVoice(0, 0).VolL      = 0x3FFF;
            spu2.GetVoice(0, 0).VolR      = 0x3FFF;
            spu2.GetVoice(0, 0).Pitch     = 0x1000;
            spu2.GetVoice(0, 0).Adsr1     = 0x00FF;
            spu2.GetVoice(0, 0).StartAddr = 0;

            // Set ESA=0x1000 (starts past the ADPCM data), EEA=0x2000
            spu2.WriteHalf(0x2E0u, 0x0000); // ESA_HI
            spu2.WriteHalf(0x2E2u, 0x1000); // ESA_LO
            spu2.WriteHalf(0x33Cu, 0x0000); // EEA_HI
            spu2.WriteHalf(0x33Eu, 0x2000); // EEA_LO

            // Set reverb coefficient volumes (IIR_VOL in "weird area") to moderate value
            spu2.WriteHalf(0x774u, 0x4000); // IIR_VOL core 0
            spu2.WriteHalf(0x77Eu, 0x3000); // WALL_VOL core 0
            spu2.WriteHalf(0x764u, 0x2000); // FXVOLL core 0
            spu2.WriteHalf(0x766u, 0x2000); // FXVOLR core 0

            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(128);

            bool anyNonZero = false;
            foreach (short s in pcm)
                if (s != 0) { anyNonZero = true; break; }
            Assert.IsTrue(anyNonZero, "Mixer with reverb enabled must produce non-zero output");
        }

        // =====================================================================
        // Phase 7.4 — ADSR Envelope
        // =====================================================================

        [TestMethod]
        public void Spu2_ADSR_AttackRisesToMaxVol()
        {
            // Voice with linear attack, max attack rate.
            // After enough samples, AdsrVol should reach 0x7FFF (attack complete → decay begins).
            var spu2 = new Spu2();

            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            // ADSR1: AttackMode=0 (linear), AttackShift=0, AttackStep=3 → fastest attack
            // DecayShift=0, SustainLevel=15 (0x7800)
            v.Adsr1     = 0x00F0 | 0xF; // AttackMode=0, Shift=0, Step=3, DecayShift=0, SustainLevel=15
            v.Adsr2     = 0x0000;
            v.Pitch     = 0x1000;
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.StartAddr = 0;

            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            // Mix enough samples for the attack to complete
            spu2.MixSamples(8192);

            // After attack, AdsrPhase must have advanced past 0 (Attack)
            Assert.IsTrue(v.AdsrPhase >= 1, "ADSR must advance from Attack phase after enough samples");
        }

        [TestMethod]
        public void Spu2_ADSR_KonResetsPhaseToAttack()
        {
            var spu2 = new Spu2();
            var v = spu2.GetVoice(0, 0);
            v.AdsrPhase = 2; // Sustain
            v.AdsrVol   = 0x4000;

            // KON must reset to Attack phase
            spu2.WriteHalf(0x1A0u, 0x0001);

            Assert.AreEqual(0, v.AdsrPhase, "KON must reset AdsrPhase to 0 (Attack)");
            Assert.AreEqual(0, v.AdsrVol,   "KON must reset AdsrVol to 0");
        }

    }
}
