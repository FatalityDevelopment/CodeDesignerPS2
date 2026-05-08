using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.EmulatorCore;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestPS2ByteEditor
    {
        [TestMethod]
        public void PS2ByteEditor_Constructor_NullScheduler_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new PS2ByteEditor(null!));
        }

        [TestMethod]
        public void PS2ByteEditor_ReadWrite_MainRam_UsesPhysicalAddresses()
        {
            var scheduler = new DualCpuScheduler();
            var editor = new PS2ByteEditor(scheduler);

            editor.Write(0x00000100, new byte[] { 0x11, 0x22, 0x33, 0x44 });

            CollectionAssert.AreEqual(
                new byte[] { 0x11, 0x22, 0x33, 0x44 },
                editor.Read(0x00000100, 4));
            CollectionAssert.AreEqual(
                new byte[] { 0x11, 0x22, 0x33, 0x44 },
                scheduler.EE.Bus.Ram.Skip(0x100).Take(4).ToArray());
        }

        [TestMethod]
        public void PS2ByteEditor_ReadWrite_KsegAndMirrorAliases_TargetSameRamBytes()
        {
            var scheduler = new DualCpuScheduler();
            var editor = new PS2ByteEditor(scheduler);

            scheduler.EE.Bus.Ram[0x120] = 0x5A;

            Assert.AreEqual(0x5A, editor.Read(unchecked((int)0x80000120u), 1)[0], "KSEG0 alias must read main RAM");
            Assert.AreEqual(0x5A, editor.Read(unchecked((int)0xA0000120u), 1)[0], "KSEG1 alias must read main RAM");
            Assert.AreEqual(0x5A, editor.Read(0x20000120, 1)[0], "Uncached RAM mirror must read main RAM");

            editor.Write(unchecked((int)0xA0000120u), new byte[] { 0xAA, 0xBB });
            editor.Write(0x20000122, new byte[] { 0xCC });

            CollectionAssert.AreEqual(
                new byte[] { 0xAA, 0xBB, 0xCC },
                scheduler.EE.Bus.Ram.Skip(0x120).Take(3).ToArray());
        }

        [TestMethod]
        public void PS2ByteEditor_Read_CanAccessBiosAndScratchpad()
        {
            var scheduler = new DualCpuScheduler();
            var editor = new PS2ByteEditor(scheduler);

            scheduler.EE.Bus.Bios[0x10] = 0xDE;
            scheduler.EE.Bus.Bios[0x11] = 0xAD;
            scheduler.EE.Bus.Bios[0x12] = 0xBE;
            scheduler.EE.Bus.Bios[0x13] = 0xEF;
            scheduler.EE.Bus.Scratchpad[0x20] = 0x12;
            scheduler.EE.Bus.Scratchpad[0x21] = 0x34;

            CollectionAssert.AreEqual(
                new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                editor.Read(unchecked((int)0xBFC00010u), 4));
            CollectionAssert.AreEqual(
                new byte[] { 0x12, 0x34 },
                editor.Read(0x70000020, 2));
        }

        [TestMethod]
        public void PS2ByteEditor_Write_UpdatesScratchpadButLeavesBiosUnchanged()
        {
            var scheduler = new DualCpuScheduler();
            var editor = new PS2ByteEditor(scheduler);

            scheduler.EE.Bus.Bios[0x30] = 0x99;

            editor.Write(0x70000040, new byte[] { 0x66, 0x77 });
            editor.Write(0x1FC00030, new byte[] { 0x00 });

            CollectionAssert.AreEqual(
                new byte[] { 0x66, 0x77 },
                scheduler.EE.Bus.Scratchpad.Skip(0x40).Take(2).ToArray());
            Assert.AreEqual(0x99, scheduler.EE.Bus.Bios[0x30], "BIOS writes must be ignored");
        }

        [TestMethod]
        public void PS2ByteEditor_StringAddressApis_ParseHexPrefixesAndLeadingZeroes()
        {
            var scheduler = new DualCpuScheduler();
            var editor = new PS2ByteEditor(scheduler);

            editor.Write("0x00000140", new byte[] { 0xAB, 0xCD });

            CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, editor.Read("00000140", 2));
        }

        [TestMethod]
        public void PS2ByteEditor_EmptyAndUnmappedOperations_ReturnExpectedDefaults()
        {
            var scheduler = new DualCpuScheduler();
            var editor = new PS2ByteEditor(scheduler);

            CollectionAssert.AreEqual(Array.Empty<byte>(), editor.Read(0x00000000, 0));

            editor.Write(0x50000000, Array.Empty<byte>());
            editor.Write(0x50000000, null!);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x00 }, editor.Read(0x50000000, 2));
        }
    }
}
