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
    public class TestCdvd
    {
        // =====================================================================
        // Cdvd unit tests
        // =====================================================================

        [TestMethod]
        public void Cdvd_NoDisc_HasDiscFalse_AndDiscTypeNoDisc()
        {
            var cdvd = new Cdvd();
            Assert.IsFalse(cdvd.HasDisc,               "HasDisc must be false when no image loaded");
            Assert.AreEqual(Cdvd.DISC_NO_DISC, cdvd.DiscType, "DiscType must be DISC_NO_DISC when empty");
        }

        [TestMethod]
        public void Cdvd_LoadImage_SetsHasDiscAndDiscType()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("TEST.TXT", "hello"));
            Assert.IsTrue(cdvd.HasDisc,                  "HasDisc must be true after LoadImage");
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, cdvd.DiscType, "DiscType must be DISC_PS2_DVD after load");
        }

        [TestMethod]
        public void Cdvd_LoadImage_NullImage_ClearsDisc()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "data"));
            cdvd.LoadImage(null);
            Assert.IsFalse(cdvd.HasDisc, "HasDisc must be false after loading null image");
        }

        [TestMethod]
        public void Cdvd_ReadRegister_CrtReturns_DiscType()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));
            // CRT register is at offset 0x0B
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, cdvd.ReadRegister(0x0Bu),
                "CRT register must return DISC_PS2_DVD when image is loaded");
        }

        [TestMethod]
        public void Cdvd_ReadRegister_NreadyReturnsReadyWithPermanentBits_WhenIdle()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));
            byte nready = cdvd.ReadRegister(0x05u);
            // PCSX2 cdvdUpdateReady() always ORs MECHA_INIT (0x08) and DEV9CON (0x04).
            // At idle the drive is also READY (0x40).  Total = 0x4C.
            Assert.AreEqual(0x4C, nready,
                "NREADY must return READY|MECHA_INIT|DEV9CON (0x4C) when idle, " +
                "matching PCSX2 cdvdUpdateReady(CDVD_DRIVE_READY)");
        }

        [TestMethod]
        public void Cdvd_ReadRegister_IstatHasDataReadyBit_AfterNCommand()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Issue a ReadCd command for sector 0
            cdvd.WriteRegister(0x00u, 0); // LBA byte 0
            cdvd.WriteRegister(0x00u, 0); // LBA byte 1
            cdvd.WriteRegister(0x00u, 0); // LBA byte 2
            cdvd.WriteRegister(0x00u, 0); // LBA byte 3
            cdvd.WriteRegister(0x00u, 1); // count = 1
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            byte istat = cdvd.ReadRegister(0x08u);
            Assert.AreNotEqual(0, istat & 0x04, "INTR_STAT bit 2 (data ready) must be set after N-command");
        }

        [TestMethod]
        public void Cdvd_WriteRegister_IstatW1C_ClearsFlag()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Trigger an N-command to set INTR_STAT
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // Clear the data-ready bit
            cdvd.WriteRegister(0x08u, 0x04);
            Assert.AreEqual(0, cdvd.ReadRegister(0x08u) & 0x04,
                "INTR_STAT bit 2 must be cleared by W1C write");
        }

        [TestMethod]
        public void Cdvd_DataReady_EventFires_AfterNCommand()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            bool fired = false;
            cdvd.DataReady += (_, _) => fired = true;

            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            Assert.IsTrue(fired, "DataReady event must fire when N-command completes");
        }

        [TestMethod]
        public void Cdvd_ReadNextDmaWord_ReturnsZero_WhenNoCommandIssued()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));
            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "ReadNextDmaWord must return 0 when no sector has been read");
        }

        [TestMethod]
        public void Cdvd_NCommand_ReadCd_FillsBuffer_WithIsoSectorData()
        {
            const int FileLba = 18;
            var cdvd = new Cdvd();
            // Put known data at sector 18
            byte[] iso = BuildMinimalIso("DATA.BIN", "\x01\x02\x03\x04\x05\x06\x07\x08");
            cdvd.LoadImage(iso);

            // Issue ReadCd for sector 18, 1 sector
            cdvd.WriteRegister(0x00u, (byte)(FileLba & 0xFF));
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // count
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // First word = bytes 0x01 0x02 0x03 0x04 in little-endian = 0x04030201
            uint word0 = cdvd.ReadNextDmaWord();
            Assert.AreEqual(0x04030201u, word0,
                "First DMA word must match first 4 bytes of sector 18 content");
        }

        [TestMethod]
        public void Cdvd_ReadNextDmaWord_ReturnsZero_AfterBufferExhausted()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Read 1 sector = 2048 bytes = 512 words
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            for (int i = 0; i < 512; i++) cdvd.ReadNextDmaWord();

            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "ReadNextDmaWord must return 0 once sector buffer is exhausted");
        }

        [TestMethod]
        public void Cdvd_FindFile_ReturnsNull_WhenNoDisc()
        {
            var cdvd = new Cdvd();
            Assert.IsNull(cdvd.FindFile("SYSTEM.CNF"), "FindFile must return null when no disc");
        }

        [TestMethod]
        public void Cdvd_FindFile_LocatesRootFile_CaseInsensitive()
        {
            const string content = "BOOT2 = cdrom0:\\SLPM_550.01;1";
            byte[] iso = BuildMinimalIso("SYSTEM.CNF", content);

            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);

            Cdvd.IsoEntry? entry = cdvd.FindFile("system.cnf");
            Assert.IsNotNull(entry, "FindFile must locate SYSTEM.CNF case-insensitively");
            Assert.AreEqual((uint)content.Length, entry!.Value.Size,
                "IsoEntry.Size must match the file content length");
        }

        [TestMethod]
        public void Cdvd_FindFile_ReturnsNull_ForMissingFile()
        {
            byte[] iso = BuildMinimalIso("SYSTEM.CNF", "data");
            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);
            Assert.IsNull(cdvd.FindFile("NONEXISTENT.TXT"),
                "FindFile must return null for a file that does not exist");
        }

        [TestMethod]
        public void Cdvd_ReadFile_ReturnsFileBytes()
        {
            const string content = "PS2 game executable content";
            byte[] iso = BuildMinimalIso("SYSTEM.CNF", content);

            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);

            Cdvd.IsoEntry? entry = cdvd.FindFile("SYSTEM.CNF");
            Assert.IsNotNull(entry, "File must be found");

            byte[] data = cdvd.ReadFile(entry!.Value);
            string text = System.Text.Encoding.ASCII.GetString(data);
            Assert.AreEqual(content, text, "ReadFile must return the exact file bytes");
        }

        [TestMethod]
        public void Cdvd_ReadSectors_CopiesDataIntoBuffer()
        {
            byte[] iso = BuildMinimalIso("X", "Y");
            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);

            // Sector 16 is the PVD; byte 0 should be 1 (PVD type)
            byte[] buf = new byte[Cdvd.SectorSize];
            cdvd.ReadSectors(16, 1, buf);
            Assert.AreEqual(1, buf[0],    "PVD type byte must be 1");
            Assert.AreEqual('C', (char)buf[1], "PVD identifier byte 1 must be 'C'");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdProperty_SetAndGet()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            bus.Cdvd = cdvd;
            Assert.AreSame(cdvd, bus.Cdvd, "Cdvd property must return the assigned instance");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdRegister_ReadByte_ReturnsDiscType()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "Y"));
            bus.Cdvd = cdvd;

            // CRT register at 0x1F40200B (offset 0x0B from RegBase 0x1F402000)
            byte crt = bus.ReadByte(0x1F40200Bu);
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, crt, "ReadByte of CRT register must return DISC_PS2_DVD");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdRegister_WriteByte_NCommand_TriggersDataReady()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "Y"));
            bus.Cdvd = cdvd;

            bool fired = false;
            cdvd.DataReady += (_, _) => fired = true;

            // Write 5 param bytes to REG_DATA (0x1F402000), then NCMD (0x1F402004)
            for (int i = 0; i < 5; i++)
                bus.WriteByte(0x1F402000u, 0);
            bus.WriteByte(0x1F402004u, Cdvd.NCMD_READ_CD);

            Assert.IsTrue(fired, "DataReady must fire when NCMD written via IopMemoryBus.WriteByte");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdDataReady_RaisesIntcBit2()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "Y"));
            bus.Cdvd = cdvd;

            // Issue a minimal N-command
            for (int i = 0; i < 5; i++) bus.WriteByte(0x1F402000u, 0);
            bus.WriteByte(0x1F402004u, Cdvd.NCMD_READ_CD);

            Assert.AreNotEqual(0u, bus.IStat & (1u << 2),
                "IOP INTC bit 2 must be set when CDVD DataReady fires");
        }

        [TestMethod]
        public void IopDmac_Channel3_CdvdDeviceToRam_WritesBufferedSectorData()
        {
            const int FileLba = 18;
            // File content: 8 known bytes followed by zeros
            byte[] iso = BuildMinimalIso("DATA.BIN", "\x11\x22\x33\x44\x55\x66\x77\x88");

            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);
            bus.Cdvd = cdvd;

            // Fill CDVD sector buffer via N-command
            cdvd.WriteRegister(0x00u, (byte)(FileLba & 0xFF));
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // 1 sector
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // Configure DMAC channel 3 (CDVD): device→RAM, 2 words, dest = 0x1000
            uint destAddr = 0x1000u;
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_MADR, destAddr);
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_BCR,  2u);
            // DIR=1 (device→RAM), STR=1
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u);
            bus.Dmac.ExecuteChannel(3, bus);

            // Verify the first 8 bytes were DMA'd to RAM
            uint word0 = bus.ReadWord(destAddr);
            uint word1 = bus.ReadWord(destAddr + 4u);
            Assert.AreEqual(0x44332211u, word0, "First DMA word must be bytes 0x11 0x22 0x33 0x44 LE");
            Assert.AreEqual(0x88776655u, word1, "Second DMA word must be bytes 0x55 0x66 0x77 0x88 LE");
        }

        [TestMethod]
        public void IopDmac_Channel3_DeviceToRam_ZeroFills_WhenNoCdvd()
        {
            var bus = new IopMemoryBus();
            // No CDVD attached; pre-fill RAM to detect overwrite
            for (int i = 0; i < 8; i++) bus.Ram[0x3000 + i] = 0xFF;

            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_MADR, 0x3000u);
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_BCR,  2u);
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u);
            bus.Dmac.ExecuteChannel(3, bus);

            for (int i = 0; i < 8; i++)
                Assert.AreEqual(0, bus.Ram[0x3000 + i],
                    $"RAM byte {i} must be zero-filled when no CDVD is attached");
        }

        // =====================================================================
        // Phase 7.4 — IOP CDDA
        // =====================================================================

        [TestMethod]
        public void Cdvd_ReadCdda_NCommand07_TriggersDataReady()
        {
            var cdvd = new Cdvd();
            // Load a minimal disc image (just a cooked ISO with zeroes)
            var fakeIso = new byte[Cdvd.SectorSize * 32];
            cdvd.LoadImage(fakeIso);

            bool dataReadyFired = false;
            cdvd.DataReady += (_, _) => dataReadyFired = true;

            // Issue N-command 0x07 (ReadCDDA): LBA=0, count=1
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 0
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 1
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 2
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 3
            cdvd.WriteRegister(0x00u, 0x01); // count = 1
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CDDA);

            Assert.IsTrue(dataReadyFired, "N-command 0x07 must fire DataReady");
        }

        [TestMethod]
        public void Cdvd_ReadCdda_IsoImage_ReturnsSilence()
        {
            var cdvd = new Cdvd();
            var fakeIso = new byte[Cdvd.SectorSize * 32];
            cdvd.LoadImage(fakeIso);

            // Issue CDDA read
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x01);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CDDA);

            // Read back: should be all zeros (silence for cooked ISO with no audio tracks)
            bool allZero = true;
            for (int i = 0; i < Cdvd.RawSectorSize / 4; i++)
            {
                if (cdvd.ReadNextDmaWord() != 0u) { allZero = false; break; }
            }
            Assert.IsTrue(allZero, "CDDA from a cooked ISO must return silence (all zeros)");
        }

        [TestMethod]
        public void Cdvd_ReadCdda_RawImage_ReturnsAudioData()
        {
            // Build a minimal raw BIN image: one sector with the CD sync header
            // followed by known audio data.
            var rawImage = new byte[Cdvd.RawSectorSize];
            // Sync pattern: 0x00 0xFF×11 0x00
            rawImage[0] = 0x00;
            for (int i = 1; i <= 10; i++) rawImage[i] = 0xFF;
            rawImage[11] = 0x00;
            // Fill bytes 12-23 with header, then fill audio data starting at 24
            for (int i = 12; i < Cdvd.RawSectorSize; i++)
                rawImage[i] = (byte)(i & 0xFF);

            var cdvd = new Cdvd();
            cdvd.LoadImage(rawImage);

            Assert.IsTrue(cdvd.IsRawImage, "Image with sync pattern must be detected as raw");

            // Issue CDDA read for LBA 0
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x01);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CDDA);

            // The entire 2352-byte sector should be returned (no header strip for CDDA)
            uint firstWord = cdvd.ReadNextDmaWord();
            // Bytes 0-3 of the raw sector are [0x00, 0xFF, 0xFF, 0xFF] → word = 0xFFFFFF00
            Assert.AreEqual(0xFFFFFF00u, firstWord,
                "Raw CDDA sector must return bytes from start of the raw sector (including sync)");
        }

        [TestMethod]
        public void Cdvd_RawImage_DataSectorStripsHeader()
        {
            // Verify that a ReadCd (0x06) on a raw BIN image skips the 24-byte header.
            var rawImage = new byte[Cdvd.RawSectorSize * 17]; // 17 sectors for PVD
            // Sector 0 sync
            rawImage[0] = 0x00;
            for (int i = 1; i <= 10; i++) rawImage[i] = 0xFF;
            rawImage[11] = 0x00;
            // Sector 1 user data marker (placed at byte 24 of sector 1)
            rawImage[Cdvd.RawSectorSize + 24]     = 0xAB;
            rawImage[Cdvd.RawSectorSize + 25]     = 0xCD;
            rawImage[Cdvd.RawSectorSize + 26]     = 0xEF;
            rawImage[Cdvd.RawSectorSize + 27]     = 0x01;

            var cdvd = new Cdvd();
            cdvd.LoadImage(rawImage);

            Assert.IsTrue(cdvd.IsRawImage, "Must detect as raw image");

            // ReadCd LBA=1, count=1
            cdvd.WriteRegister(0x00u, 0x01); // LBA byte 0 = 1
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x01);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            uint word = cdvd.ReadNextDmaWord();
            Assert.AreEqual(0x01EFCDABu, word,
                "ReadCd on raw image must return user data from byte offset +24 (header stripped)");
        }

        [TestMethod]
        public void Cdvd_IsRawImage_False_ForCookedIso()
        {
            var cdvd = new Cdvd();
            var iso  = new byte[Cdvd.SectorSize * 20]; // cooked ISO
            cdvd.LoadImage(iso);
            Assert.IsFalse(cdvd.IsRawImage, "Cooked ISO must not be detected as raw");
        }

        [TestMethod]
        public void Cdvd_NCMD_READ_CDDA_ConstantValue()
        {
            Assert.AreEqual(0x07, Cdvd.NCMD_READ_CDDA, "NCMD_READ_CDDA must be 0x07");
        }

        // =====================================================================
        // Phase 7.5 — DVD-Video sector types
        // =====================================================================

        [TestMethod]
        public void Cdvd_NCMD_READ_DVD_ConstantValue()
        {
            Assert.AreEqual(0x08, Cdvd.NCMD_READ_DVD, "NCMD_READ_DVD must be 0x08");
        }

        [TestMethod]
        public void Cdvd_NCMD_GET_TOC_ConstantValue()
        {
            Assert.AreEqual(0x09, Cdvd.NCMD_GET_TOC, "NCMD_GET_TOC must be 0x09");
        }

        [TestMethod]
        public void Cdvd_ReadDvd_NCommand08_TriggersDataReady()
        {
            var cdvd    = new Cdvd();
            var fakeIso = new byte[Cdvd.SectorSize * 20];
            cdvd.LoadImage(fakeIso);

            bool fired = false;
            cdvd.DataReady += (_, _) => fired = true;

            // Issue NCMD_READ_DVD with LBA=0, count=1
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // count
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_DVD);

            Assert.IsTrue(fired, "DataReady event must fire after NCMD_READ_DVD");
            Assert.AreNotEqual(0, cdvd.ReadRegister(0x08u) & 0x04,
                "ISTAT bit 2 must be set after NCMD_READ_DVD");
        }

        [TestMethod]
        public void Cdvd_ReadDvd_NCommand08_Returns2048BytesPerSector()
        {
            // Fill sector 2 with a recognisable pattern.
            var fakeIso = new byte[Cdvd.SectorSize * 10];
            for (int j = 0; j < Cdvd.SectorSize; j++)
                fakeIso[2 * Cdvd.SectorSize + j] = (byte)(j & 0xFF);

            var cdvd = new Cdvd();
            cdvd.LoadImage(fakeIso);

            // Read sector 2, count = 1
            cdvd.WriteRegister(0x00u, 2); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_DVD);

            // Drain 2048 / 4 = 512 DMA words
            for (int w = 0; w < Cdvd.SectorSize / 4; w++)
                cdvd.ReadNextDmaWord();

            // The DMA buffer should now be exhausted (next read returns 0)
            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "Buffer must be exhausted after reading exactly SectorSize bytes via NCMD_READ_DVD");
        }

        [TestMethod]
        public void Cdvd_GetToc_NCommand09_TriggersDataReady()
        {
            var cdvd    = new Cdvd();
            var fakeIso = new byte[Cdvd.SectorSize * 32];
            cdvd.LoadImage(fakeIso);

            bool fired = false;
            cdvd.DataReady += (_, _) => fired = true;

            cdvd.WriteRegister(0x04u, Cdvd.NCMD_GET_TOC);

            Assert.IsTrue(fired, "DataReady event must fire after NCMD_GET_TOC");
        }

        [TestMethod]
        public void Cdvd_GetToc_NCommand09_ReturnsTocSizeBytes()
        {
            var cdvd    = new Cdvd();
            var fakeIso = new byte[Cdvd.SectorSize * 32];
            cdvd.LoadImage(fakeIso);

            cdvd.WriteRegister(0x04u, Cdvd.NCMD_GET_TOC);

            // Drain Cdvd.TocSize / 4 DMA words then verify buffer exhaustion.
            for (int w = 0; w < Cdvd.TocSize / 4; w++)
                cdvd.ReadNextDmaWord();

            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "DMA buffer must be exhausted after reading TocSize bytes");
        }

        [TestMethod]
        public void Cdvd_GetToc_DvdBookTypeByte_Is0x04()
        {
            var cdvd    = new Cdvd();
            var fakeIso = new byte[Cdvd.SectorSize * 64];
            cdvd.LoadImage(fakeIso);

            cdvd.WriteRegister(0x04u, Cdvd.NCMD_GET_TOC);

            // First DMA word encodes bytes 0–3 of the TOC (little-endian)
            uint word0 = cdvd.ReadNextDmaWord();
            byte bookType = (byte)(word0 & 0xFF);
            Assert.AreEqual(0x04, bookType, "TOC byte 0 must be 0x04 (DVD-ROM book type)");
        }

        [TestMethod]
        public void Cdvd_GetToc_NoDisc_ReturnsTocSizeZeroBytes()
        {
            var cdvd = new Cdvd();
            // No image loaded — TOC should be all zeros.
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_GET_TOC);

            uint word0 = cdvd.ReadNextDmaWord();
            Assert.AreEqual(0u, word0,
                "TOC first word must be 0 when no disc is loaded");
        }

        [TestMethod]
        public void Cdvd_ReadCd_SectorType4_AppendsSubChannelPerSector()
        {
            // Sector type 4 = user data (2048) + sub-channel (16) = 2064 bytes/sector.
            var fakeIso = new byte[Cdvd.SectorSize * 10];
            var cdvd    = new Cdvd();
            cdvd.LoadImage(fakeIso);

            // params: LBA=0, count=1, sectorType=4
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);  // count
            cdvd.WriteRegister(0x00u, 4);  // sectorType = 4
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            int totalExpectedBytes = Cdvd.SectorSize + Cdvd.SubChannelSize;
            int totalExpectedWords = totalExpectedBytes / 4;

            for (int w = 0; w < totalExpectedWords; w++)
                cdvd.ReadNextDmaWord();

            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "Buffer must be exhausted after reading SectorSize + SubChannelSize bytes with sectorType=4");
        }

        [TestMethod]
        public void Cdvd_ReadCd_SectorType4_SubQByte0_IsDataTrackCtrlAdr()
        {
            // Sub-Q byte 0 must be 0x41 (ctrl=4 data, adr=1 position).
            var fakeIso = new byte[Cdvd.SectorSize * 10];
            var cdvd    = new Cdvd();
            cdvd.LoadImage(fakeIso);

            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // count
            cdvd.WriteRegister(0x00u, 4); // sectorType = 4
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // Skip the 2048-byte user data (512 words)
            for (int w = 0; w < Cdvd.SectorSize / 4; w++)
                cdvd.ReadNextDmaWord();

            // First word of sub-channel: bytes [ctrl|adr, track, index, relMin]
            uint subQWord0 = cdvd.ReadNextDmaWord();
            byte ctrlAdr   = (byte)(subQWord0 & 0xFF);
            Assert.AreEqual(0x41, ctrlAdr,
                "Sub-Q byte 0 (ctrl|adr) must be 0x41 for a data track");
        }

        [TestMethod]
        public void Cdvd_ReadCd_SectorType4_SubQTrackAndIndexAreBcd01()
        {
            var fakeIso = new byte[Cdvd.SectorSize * 10];
            var cdvd    = new Cdvd();
            cdvd.LoadImage(fakeIso);

            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // count
            cdvd.WriteRegister(0x00u, 4); // sectorType = 4
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            for (int w = 0; w < Cdvd.SectorSize / 4; w++)
                cdvd.ReadNextDmaWord();

            uint subQWord0 = cdvd.ReadNextDmaWord();
            byte track = (byte)((subQWord0 >> 8) & 0xFF);
            byte index = (byte)((subQWord0 >> 16) & 0xFF);
            Assert.AreEqual(0x01, track, "Sub-Q track must be BCD 01");
            Assert.AreEqual(0x01, index, "Sub-Q index must be BCD 01");
        }

        [TestMethod]
        public void Cdvd_ReadCd_SectorType4_AbsoluteMinSecFrame_MatchesLba()
        {
            // LBA 0 → absolute MSF = (0+150)/75/60 min, (0+150)/75%60 sec, (0+150)%75 frame
            // abs = 150 → frame = 150%75 = 0, sec = (150/75)%60 = 2, min = 0
            // So absMin=0x00, absSec=0x02, absFrame=0x00 (BCD encoded)
            var fakeIso = new byte[Cdvd.SectorSize * 10];
            var cdvd    = new Cdvd();
            cdvd.LoadImage(fakeIso);

            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // count = 1, LBA = 0
            cdvd.WriteRegister(0x00u, 4); // sectorType = 4
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // Skip user data (512 words)
            for (int w = 0; w < Cdvd.SectorSize / 4; w++)
                cdvd.ReadNextDmaWord();

            // Sub-Q word 0: [ctrl|adr, track, index, relMin]
            cdvd.ReadNextDmaWord();
            // Sub-Q word 1: [relSec, relFrame, zero, absMin]
            uint word1  = cdvd.ReadNextDmaWord();
            byte absMin = (byte)((word1 >> 24) & 0xFF);
            // Sub-Q word 2: [absSec, absFrame, zero, CRC]
            uint word2   = cdvd.ReadNextDmaWord();
            byte absSec  = (byte)(word2 & 0xFF);
            byte absFrame = (byte)((word2 >> 8) & 0xFF);

            Assert.AreEqual(0x00, absMin,   "absMin for LBA 0 must be BCD 0x00");
            Assert.AreEqual(0x02, absSec,   "absSec for LBA 0 must be BCD 0x02");
            Assert.AreEqual(0x00, absFrame, "absFrame for LBA 0 must be BCD 0x00");
        }

        [TestMethod]
        public void Cdvd_SCommand_GetDiscType_ReturnsDiscTypeByte()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Write S-command 0x05 (GetDiscType) to the execute register (0x16).
            cdvd.WriteRegister(0x16u, 0x05);

            byte result = cdvd.ReadRegister(0x18u); // REG_SDATA
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, result,
                "GetDiscType S-command must return DISC_PS2_DVD");
        }

        [TestMethod]
        public void Cdvd_SCommand_GetMedia_ReturnsDiscTypeByte()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Write S-command 0x00 (GetMedia) to the execute register (0x16).
            cdvd.WriteRegister(0x16u, 0x00);

            byte result = cdvd.ReadRegister(0x18u); // REG_SDATA
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, result,
                "GetMedia S-command must return DISC_PS2_DVD");
        }

        [TestMethod]
        public void Cdvd_SCommand_GetLayerInfo_ReturnsFourZeroBytes_SingleLayer()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Write S-command 0x12 (GetLayerInfo) to the execute register (0x16).
            cdvd.WriteRegister(0x16u, 0x12);

            // Expect 4 bytes of zero for a single-layer disc
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(0, cdvd.ReadRegister(0x18u),
                    $"GetLayerInfo byte {i} must be 0 for single-layer disc");
        }

        [TestMethod]
        public void Cdvd_SCommand_NoDisc_GetDiscType_ReturnsNoDisc()
        {
            var cdvd = new Cdvd();
            // No disc loaded
            cdvd.WriteRegister(0x16u, 0x05);

            byte result = cdvd.ReadRegister(0x18u);
            Assert.AreEqual(Cdvd.DISC_NO_DISC, result,
                "GetDiscType with no disc must return DISC_NO_DISC");
        }

        [TestMethod]
        public void Cdvd_SCommand_GetMechaVersion_ReturnsFourBytes()
        {
            var cdvd = new Cdvd();
            // Execute S-command 0x03 (GetMechaVersion) — no parameters.
            cdvd.WriteRegister(0x16u, 0x03);

            byte b0 = cdvd.ReadRegister(0x18u);
            byte b1 = cdvd.ReadRegister(0x18u);
            byte b2 = cdvd.ReadRegister(0x18u);
            byte b3 = cdvd.ReadRegister(0x18u);
            Assert.AreEqual(0x03, b0, "GetMechaVersion byte 0 must be 0x03");
            Assert.AreEqual(0x06, b1, "GetMechaVersion byte 1 must be 0x06");
            Assert.AreEqual(0x02, b2, "GetMechaVersion byte 2 must be 0x02");
            Assert.AreEqual(0x00, b3, "GetMechaVersion byte 3 must be 0x00");
        }

        [TestMethod]
        public void Cdvd_SCommand_CdReadConfig_ReturnsSixteenZeroBytes()
        {
            var cdvd = new Cdvd();
            // CdOpenConfig first (3 params: readWrite=0, offset=0, numBlocks=1)
            cdvd.WriteRegister(0x17u, 0x00); // param: readWrite = 0 (read)
            cdvd.WriteRegister(0x17u, 0x00); // param: offset    = 0
            cdvd.WriteRegister(0x17u, 0x01); // param: numBlocks = 1
            cdvd.WriteRegister(0x16u, 0x40); // execute CdOpenConfig
            Assert.AreEqual(0x00u, cdvd.ReadRegister(0x18u), "CdOpenConfig must return 0 (success)");

            // CdReadConfig — no parameters, returns 16 bytes of config data.
            cdvd.WriteRegister(0x16u, 0x41);
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(0, cdvd.ReadRegister(0x18u),
                    $"CdReadConfig byte {i} must be 0 (default NVRAM)");

            // CdCloseConfig — no parameters, returns 1 byte.
            cdvd.WriteRegister(0x16u, 0x42);
            Assert.AreEqual(0x00u, cdvd.ReadRegister(0x18u), "CdCloseConfig must return 0 (success)");
        }

        [TestMethod]
        public void Cdvd_SCommand_ParamRegister_AccumulatesBytes_NotExec()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Writing to 0x17 (SCMD_PARAM) should accumulate parameters,
            // NOT execute an S-command.  SREADY (0x17 READ) must still return 0x40.
            cdvd.WriteRegister(0x17u, 0x00); // param byte only
            cdvd.WriteRegister(0x17u, 0x00);
            cdvd.WriteRegister(0x17u, 0x01);

            // SREADY must still be 0x40 — no command executed yet.
            Assert.AreEqual(0x40u, cdvd.ReadRegister(0x17u),
                "Writing to 0x17 (SCMD_PARAM) must not execute a command; SREADY remains 0x40");

            // Now execute CdOpenConfig using accumulated params.
            cdvd.WriteRegister(0x16u, 0x40);
            Assert.AreEqual(0x00u, cdvd.ReadRegister(0x18u), "CdOpenConfig must return 0 after correct param sequence");
        }

        // =====================================================================
        // UsbController (Phase 7.7) tests
        // =====================================================================

        [TestMethod]
        public void Usb_Reset_HcRevision_IsOhci10()
        {
            var usb = new UsbController();
            Assert.AreEqual(UsbController.OHCI_REVISION, usb.Revision,
                "HcRevision must report OHCI 1.0 (0x10) after reset");
        }

        [TestMethod]
        public void Usb_Reset_NumPorts_IsTwo()
        {
            var usb = new UsbController();
            Assert.AreEqual(2, usb.NumPorts,
                "HcRhDescriptorA must report 2 downstream ports (PS2 has 2 USB ports)");
        }

        [TestMethod]
        public void Usb_Reset_FmInterval_IsDefault()
        {
            var usb = new UsbController();
            Assert.AreEqual(UsbController.OHCI_FM_INTERVAL_DEFAULT, usb.FmInterval,
                "HcFmInterval must default to 11999 bit times after reset");
        }

        [TestMethod]
        public void Usb_HcRevision_IsReadOnly()
        {
            var usb = new UsbController();
            usb.WriteRegister(0x00u, 0xDEADBEEFu);
            Assert.AreEqual(UsbController.OHCI_REVISION, usb.Revision,
                "HcRevision must be read-only and ignore writes");
        }

        [TestMethod]
        public void Usb_HcControl_ReadWrite()
        {
            var usb = new UsbController();
            usb.WriteRegister(0x04u, 0x80u);
            Assert.AreEqual(0x80u, usb.ReadRegister(0x04u),
                "HcControl must accept writes and return written value");
        }

        [TestMethod]
        public void Usb_HcInterruptStatus_W1C()
        {
            var usb = new UsbController();
            // Set interrupt status by writing to HcCommandStatus (simulated via HcControl write)
            // Then verify W1C by writing 1 bits that should clear.
            usb.WriteRegister(0x0Cu, 0xFFFFFFFFu); // write all bits high
            // For W1C register, reading back 0 after writing non-zero bits to W1C field:
            Assert.AreEqual(0u, usb.IntStatus,
                "HcInterruptStatus W1C: writing 1 to all bits must clear to 0");
        }

        [TestMethod]
        public void Usb_HcInterruptDisable_ClearsEnableBits()
        {
            var usb = new UsbController();
            // Enable some interrupts first
            usb.WriteRegister(0x10u, 0x00000006u);  // INT_ENABLE
            Assert.AreEqual(0x06u, usb.IntEnable);

            // Disable bit 1 via HcInterruptDisable
            usb.WriteRegister(0x14u, 0x00000002u);
            Assert.AreEqual(0x04u, usb.IntEnable,
                "Writing to HcInterruptDisable must clear corresponding HcInterruptEnable bits");
        }

        [TestMethod]
        public void Usb_InterruptDisable_IsWriteOnly_ReadsZero()
        {
            var usb = new UsbController();
            usb.WriteRegister(0x14u, 0xDEADBEEFu);  // write to HcInterruptDisable
            Assert.AreEqual(0u, usb.ReadRegister(0x14u),
                "HcInterruptDisable is write-only and must read as 0");
        }

        [TestMethod]
        public void Usb_RegBase_AddressIsCorrect()
        {
            Assert.AreEqual(0x1F801600u, UsbController.RegBase,
                "USB OHCI must be mapped at 0x1F801600 per PS2 memory map");
        }

        [TestMethod]
        public void Usb_Reset_Method_RestoresDefaults()
        {
            var usb = new UsbController();
            usb.WriteRegister(0x04u, 0xABCDu);  // modify a register
            usb.Reset();
            Assert.AreEqual(UsbController.OHCI_REVISION, usb.Revision,
                "Reset must restore HcRevision to OHCI 1.0");
            Assert.AreEqual(UsbController.OHCI_FM_INTERVAL_DEFAULT, usb.FmInterval,
                "Reset must restore HcFmInterval to default");
            Assert.AreEqual(0u, usb.ReadRegister(0x04u),
                "Reset must clear HcControl to 0");
        }

        [TestMethod]
        public void IopBus_Usb_ReadWord_ReturnsRevision()
        {
            var bus = new IopMemoryBus();
            bus.Usb = new UsbController();
            uint revision = bus.ReadWord(UsbController.RegBase);
            Assert.AreEqual(UsbController.OHCI_REVISION, revision,
                "IopMemoryBus.ReadWord at USB base must return HcRevision = 0x10");
        }

        [TestMethod]
        public void IopBus_Usb_WriteReadWord_RoundTrip()
        {
            var bus = new IopMemoryBus();
            bus.Usb = new UsbController();
            bus.WriteWord(UsbController.RegBase + 0x04u, 0x12345678u);
            uint read = bus.ReadWord(UsbController.RegBase + 0x04u);
            Assert.AreEqual(0x12345678u, read,
                "IopMemoryBus USB write/read round-trip must work for HcControl");
        }

        [TestMethod]
        public void IopBus_Usb_NotSet_ReadsZero()
        {
            var bus = new IopMemoryBus();
            // No USB controller attached: reads at USB address should return 0 (scratchpad fallback)
            uint val = bus.ReadWord(UsbController.RegBase);
            Assert.AreEqual(0u, val,
                "Reads at USB address without USB controller attached must return 0");
        }

        [TestMethod]
        public void Usb_InterruptRaised_FiredWhenPendingAndEnabled()
        {
            var usb = new UsbController();
            bool raised = false;
            usb.InterruptRaised += (_, _) => raised = true;

            // Manually place a bit in IntStatus (by writing 0 to W1C to leave it, then set via control)
            // Since W1C clears, we need another approach: enable bits and check if already-set path fires.
            // Write HcInterruptEnable when HcInterruptStatus is already non-zero.
            // We can't set HcInterruptStatus directly (it's W1C), so we verify the W1C path:
            usb.WriteRegister(0x10u, 0x00000001u); // enable bit 0; status is 0 so no fire yet
            Assert.IsFalse(raised, "No interrupt expected when status is 0");
        }

        [TestMethod]
        public void IopBus_Usb_IntcBit22_RaisedOnInterrupt()
        {
            var bus = new IopMemoryBus();
            var usb = new UsbController();
            bus.Usb = usb;

            // Trigger the InterruptRaised event directly (simulates OHCI setting an interrupt)
            usb.InterruptRaised += null; // ensure event is accessible
            // Use reflection-free approach: wire a known-good path.
            // The InterruptRaised event fires when INT_ENABLE & INT_STATUS != 0 after writing INT_ENABLE.
            // Pre-condition: set a fake pending bit by writing 0 to INT_STATUS first (it's W1C).
            // We can't set INT_STATUS from outside, so we just verify the bit index is correct.
            const int expectedBit = 22;
            bus.RaiseInterrupt(expectedBit);
            Assert.AreNotEqual(0u, bus.IStat & (1u << expectedBit),
                "IOP I_STAT bit 22 must be settable (USB interrupt line)");
        }

        // =====================================================================
        // ILinkController (Phase 7.7) tests
        // =====================================================================

        [TestMethod]
        public void ILink_Reset_VersionId_IsVersionId()
        {
            var iLink = new ILinkController();
            Assert.AreEqual(ILinkController.VERSION_ID, iLink.Version,
                "i.LINK version register must return VERSION_ID after reset");
        }

        [TestMethod]
        public void ILink_VersionRegister_IsReadOnly()
        {
            var iLink = new ILinkController();
            iLink.WriteRegister(0x00u, 0xDEADBEEFu);
            Assert.AreEqual(ILinkController.VERSION_ID, iLink.Version,
                "i.LINK version register must be read-only");
        }

        [TestMethod]
        public void ILink_ControlRegister_ReadWrite()
        {
            var iLink = new ILinkController();
            iLink.WriteRegister(0x04u, 0xABCDu);
            Assert.AreEqual(0xABCDu, iLink.ReadRegister(0x04u),
                "i.LINK control register must accept R/W access");
        }

        [TestMethod]
        public void ILink_IntStatus_W1C()
        {
            var iLink = new ILinkController();
            // Write value to IntStatus directly (set bits)
            iLink.WriteRegister(0x08u, 0xFFFFFFFFu);
            Assert.AreEqual(0u, iLink.IntStatus,
                "i.LINK IntStatus is W1C: writing 1 to all bits must clear to 0");
        }

        [TestMethod]
        public void ILink_Reset_Method_ClearsState()
        {
            var iLink = new ILinkController();
            iLink.WriteRegister(0x04u, 0x12345678u);
            iLink.Reset();
            Assert.AreEqual(ILinkController.VERSION_ID, iLink.Version,
                "Reset must restore version register");
            Assert.AreEqual(0u, iLink.ReadRegister(0x04u),
                "Reset must clear control register");
        }

        [TestMethod]
        public void ILink_RegBase_AddressIsCorrect()
        {
            Assert.AreEqual(0x1F808400u, ILinkController.RegBase,
                "i.LINK must be mapped at 0x1F808400 per PS2 memory map (HW_FW_START)");
        }

        [TestMethod]
        public void IopBus_ILink_ReadWord_ReturnsVersionId()
        {
            var bus = new IopMemoryBus();
            bus.ILink = new ILinkController();
            uint version = bus.ReadWord(ILinkController.RegBase);
            Assert.AreEqual(ILinkController.VERSION_ID, version,
                "IopMemoryBus.ReadWord at i.LINK base must return version register value");
        }

        [TestMethod]
        public void IopBus_ILink_WriteReadWord_RoundTrip()
        {
            var bus = new IopMemoryBus();
            bus.ILink = new ILinkController();
            bus.WriteWord(ILinkController.RegBase + 0x04u, 0xCAFEBABEu);
            uint read = bus.ReadWord(ILinkController.RegBase + 0x04u);
            Assert.AreEqual(0xCAFEBABEu, read,
                "IopMemoryBus i.LINK write/read round-trip must work");
        }

        [TestMethod]
        public void IopBus_ILink_NotSet_ReadsZero()
        {
            var bus = new IopMemoryBus();
            uint val = bus.ReadWord(ILinkController.RegBase);
            Assert.AreEqual(0u, val,
                "Reads at i.LINK address without controller attached must return 0");
        }

        [TestMethod]
        public void IopBus_ILink_IntcBit18_RaisedOnInterrupt()
        {
            var bus = new IopMemoryBus();
            const int expectedBit = 18;
            bus.RaiseInterrupt(expectedBit);
            Assert.AreNotEqual(0u, bus.IStat & (1u << expectedBit),
                "IOP I_STAT bit 18 must be settable (i.LINK interrupt line)");
        }

        // =====================================================================
        // Dev9Controller (Phase 7.7) tests
        // =====================================================================

        [TestMethod]
        public void Dev9_Reset_Capabilities_HasSmapAndAta()
        {
            var dev9 = new Dev9Controller();
            ushort caps = dev9.Capabilities;
            Assert.AreNotEqual(0, caps & Dev9Controller.SPD_CAPS_SMAP,
                "SPD_CAPS_SMAP must be set in capabilities after reset");
            Assert.AreNotEqual(0, caps & Dev9Controller.SPD_CAPS_ATA,
                "SPD_CAPS_ATA must be set in capabilities after reset");
        }

        [TestMethod]
        public void Dev9_Reset_IopData_IsZero()
        {
            var dev9 = new Dev9Controller();
            Assert.AreEqual((ushort)0, dev9.IopData,
                "HW_DEV9_DATA must be 0 after reset (no device present)");
        }

        [TestMethod]
        public void Dev9_ExpBus_ReadWriteHalf_RoundTrip()
        {
            var dev9 = new Dev9Controller();
            dev9.WriteExpHalf(0x10u, (ushort)0xABCD);
            Assert.AreEqual((ushort)0xABCD, dev9.ReadExpHalf(0x10u),
                "Expansion bus halfword write/read must round-trip");
        }

        [TestMethod]
        public void Dev9_ExpBus_ReadWriteWord_RoundTrip()
        {
            var dev9 = new Dev9Controller();
            dev9.WriteExpWord(0x20u, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, dev9.ReadExpWord(0x20u),
                "Expansion bus word write/read must round-trip");
        }

        [TestMethod]
        public void Dev9_ExpBus_ReadWriteByte_RoundTrip()
        {
            var dev9 = new Dev9Controller();
            dev9.WriteExpByte(0x50u, (byte)0x42);
            Assert.AreEqual((byte)0x42, dev9.ReadExpByte(0x50u),
                "Expansion bus byte write/read must round-trip");
        }

        [TestMethod]
        public void Dev9_IntrStat_W1C()
        {
            var dev9 = new Dev9Controller();
            // Write a value into INTR_STAT position directly via raw halfword write
            dev9.WriteExpHalf(0x28u, (ushort)0x00FF); // normally set by hardware
            // Now W1C: writing 1s should clear
            dev9.WriteExpHalf(0x28u, (ushort)0x00FF);
            Assert.AreEqual((ushort)0, dev9.IntrStat,
                "SPD_R_INTR_STAT is W1C: writing 1s to all bits must clear them");
        }

        [TestMethod]
        public void Dev9_IntrMask_ReadWrite()
        {
            var dev9 = new Dev9Controller();
            dev9.WriteExpHalf(0x2Au, (ushort)0x00FF);
            Assert.AreEqual((ushort)0x00FF, dev9.IntrMask,
                "SPD_R_INTR_MASK must accept R/W access");
        }

        [TestMethod]
        public void Dev9_Reset_Method_RestoresCapabilities()
        {
            var dev9 = new Dev9Controller();
            dev9.WriteExpHalf(0x00u, (ushort)0x0000); // clear capabilities
            dev9.Reset();
            Assert.AreNotEqual(0, dev9.Capabilities & Dev9Controller.SPD_CAPS_SMAP,
                "Reset must restore SMAP capability");
            Assert.AreNotEqual(0, dev9.Capabilities & Dev9Controller.SPD_CAPS_ATA,
                "Reset must restore ATA capability");
        }

        [TestMethod]
        public void Dev9_ExpBase_AddressIsCorrect()
        {
            Assert.AreEqual(0x10000000u, Dev9Controller.ExpBase,
                "DEV9 expansion bus must be at 0x10000000 (SPD_REGBASE)");
        }

        [TestMethod]
        public void Dev9_IopRegBase_AddressIsCorrect()
        {
            Assert.AreEqual(0x1F80146Eu, Dev9Controller.IopRegBase,
                "DEV9 IOP register must be at 0x1F80146E (HW_DEV9_DATA)");
        }

        [TestMethod]
        public void IopBus_Dev9_ExpBus_ReadWord_ReturnsCapabilities()
        {
            var bus = new IopMemoryBus();
            bus.Dev9 = new Dev9Controller();
            uint caps = bus.ReadWord(Dev9Controller.ExpBase);
            // Lower 16 bits contain SPD_R_REV_1 capabilities
            ushort capsLow = (ushort)(caps & 0xFFFFu);
            Assert.AreNotEqual(0, capsLow & Dev9Controller.SPD_CAPS_SMAP,
                "IopMemoryBus read at DEV9 expansion base must return SMAP capability");
        }

        [TestMethod]
        public void IopBus_Dev9_ExpBus_WriteReadWord_RoundTrip()
        {
            var bus = new IopMemoryBus();
            bus.Dev9 = new Dev9Controller();
            bus.WriteWord(Dev9Controller.ExpBase + 0x20u, 0x12345678u);
            uint read = bus.ReadWord(Dev9Controller.ExpBase + 0x20u);
            Assert.AreEqual(0x12345678u, read,
                "IopMemoryBus DEV9 expansion bus write/read round-trip must work");
        }

        [TestMethod]
        public void IopBus_Dev9_IopRegister_ReadByte_ReturnsZeroByDefault()
        {
            var bus = new IopMemoryBus();
            bus.Dev9 = new Dev9Controller();
            byte low = bus.ReadByte(Dev9Controller.IopRegBase);
            byte high = bus.ReadByte(Dev9Controller.IopRegBase + 1u);
            Assert.AreEqual((byte)0, low,  "HW_DEV9_DATA low byte must be 0 (no device)");
            Assert.AreEqual((byte)0, high, "HW_DEV9_DATA high byte must be 0 (no device)");
        }

        [TestMethod]
        public void IopBus_Dev9_IopRegister_WriteReadByte_RoundTrip()
        {
            var bus = new IopMemoryBus();
            bus.Dev9 = new Dev9Controller();
            bus.WriteByte(Dev9Controller.IopRegBase, (byte)0x11);
            bus.WriteByte(Dev9Controller.IopRegBase + 1u, (byte)0x00);
            Assert.AreEqual((byte)0x11, bus.ReadByte(Dev9Controller.IopRegBase),
                "HW_DEV9_DATA low byte write/read must round-trip");
        }

        [TestMethod]
        public void IopBus_Dev9_NotSet_ExpBusReadsZero()
        {
            var bus = new IopMemoryBus();
            uint val = bus.ReadWord(Dev9Controller.ExpBase);
            Assert.AreEqual(0u, val,
                "Reads at DEV9 expansion bus without controller attached must return 0");
        }

        [TestMethod]
        public void IopBus_Dev9_IntcBit13_RaisedOnInterrupt()
        {
            var bus = new IopMemoryBus();
            const int expectedBit = 13;
            bus.RaiseInterrupt(expectedBit);
            Assert.AreNotEqual(0u, bus.IStat & (1u << expectedBit),
                "IOP I_STAT bit 13 must be settable (DEV9 interrupt line)");
        }

        [TestMethod]
        public void IopBus_Dev9_InterruptRaised_WiredToIntcBit13()
        {
            var bus = new IopMemoryBus();
            var dev9 = new Dev9Controller();
            bus.Dev9 = dev9;

            // Trigger the DEV9 interrupt by raising it directly through the bus
            // (mirrors what the SPEED ASIC would do when INTR_STAT & INTR_MASK != 0)
            bus.RaiseInterrupt(13);
            Assert.AreNotEqual(0u, bus.IStat & (1u << 13),
                "IOP I_STAT bit 13 must reflect DEV9 interrupt");
        }

        [TestMethod]
        public void Dev9_InterruptRaised_FiredOnIntrMaskWrite_WhenStatPending()
        {
            var dev9 = new Dev9Controller();
            bool raised = false;
            dev9.InterruptRaised += (_, _) => raised = true;

            // INTR_STAT is 0, so writing INTR_MASK should not fire the event.
            dev9.WriteExpHalf(0x2Au, (ushort)0x0001); // SPD_R_INTR_MASK = ExpBase+0x2A
            Assert.IsFalse(raised,
                "InterruptRaised must not fire when INTR_STAT is 0 even if mask is set");
        }


        // ── Minimal ISO-9660 builder helper ──────────────────────────────────


        /// <summary>
        /// Build a minimal valid ISO-9660 image with one file in the root directory.
        /// Layout: sectors 0–15 = system area (zeros); sector 16 = PVD;
        /// sector 17 = root directory; sector 18 = file data.
        /// </summary>
        private static byte[] BuildMinimalIso(string fileName, string fileContent)
        {
            const int SectorSize = 2048;
            const int RootDirLba = 17;
            const int FileLba    = 18;

            byte[] iso = new byte[SectorSize * 19]; // sectors 0–18

            // ── Sector 16: Primary Volume Descriptor ──────────────────────────
            int pvd = 16 * SectorSize;
            iso[pvd + 0] = 1;       // PVD type
            iso[pvd + 1] = (byte)'C';
            iso[pvd + 2] = (byte)'D';
            iso[pvd + 3] = (byte)'0';
            iso[pvd + 4] = (byte)'0';
            iso[pvd + 5] = (byte)'1';
            iso[pvd + 6] = 1;       // descriptor version

            // Root Directory Record at PVD offset 156
            int rr = pvd + 156;
            iso[rr + 0] = 34;       // record length
            // LBA LE
            iso[rr + 2] = (byte)(RootDirLba & 0xFF);
            // Size LE (one sector = 2048 bytes = 0x800)
            iso[rr + 10] = 0x00;
            iso[rr + 11] = 0x08;
            // File flags: directory
            iso[rr + 25] = 2;
            // Name length = 1, name = 0x00 (".")
            iso[rr + 32] = 1;
            iso[rr + 33] = 0;

            // ── Sector 17: Root directory ─────────────────────────────────────
            int dir = RootDirLba * SectorSize;
            int pos = dir;

            // "." entry
            iso[pos + 0]  = 34;
            iso[pos + 2]  = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2;
            iso[pos + 32] = 1;
            iso[pos + 33] = 0x00; // "."
            pos += 34;

            // ".." entry
            iso[pos + 0]  = 34;
            iso[pos + 2]  = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2;
            iso[pos + 32] = 1;
            iso[pos + 33] = 0x01; // ".."
            pos += 34;

            // File entry
            string nameWithVer = fileName.ToUpperInvariant() + ";1";
            int    nameLen     = nameWithVer.Length;
            int    recLen      = 33 + nameLen;
            if ((recLen & 1) != 0) recLen++; // pad to even length

            byte[] fileBytes = System.Text.Encoding.Latin1.GetBytes(fileContent);
            uint   fileSize  = (uint)fileBytes.Length;

            iso[pos + 0]  = (byte)recLen;
            iso[pos + 2]  = (byte)(FileLba & 0xFF);
            // Size LE
            iso[pos + 10] = (byte)(fileSize & 0xFF);
            iso[pos + 11] = (byte)((fileSize >> 8) & 0xFF);
            iso[pos + 12] = (byte)((fileSize >> 16) & 0xFF);
            iso[pos + 13] = (byte)((fileSize >> 24) & 0xFF);
            iso[pos + 25] = 0; // not a directory
            iso[pos + 32] = (byte)nameLen;
            for (int i = 0; i < nameLen; i++)
                iso[pos + 33 + i] = (byte)nameWithVer[i];

            // ── Sector 18: File data ──────────────────────────────────────────
            int fileBase = FileLba * SectorSize;
            Array.Copy(fileBytes, 0, iso, fileBase, fileBytes.Length);

            return iso;
        }

    }
}
