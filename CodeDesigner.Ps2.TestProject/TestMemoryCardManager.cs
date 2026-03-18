using System;
using System.IO;
using System.Linq;
using CodeDesigner.Ps2.MemoryCardManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestMemoryCardManager
    {
        // ── helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a temporary directory with a set of dummy files that can be
        /// imported as a save game.
        /// </summary>
        private static string CreateSaveDir(string tmpRoot, string saveName, params (string name, int size)[] files)
        {
            string dir = Path.Combine(tmpRoot, saveName);
            Directory.CreateDirectory(dir);
            foreach (var (name, size) in files)
                File.WriteAllBytes(Path.Combine(dir, name), new byte[size]);
            return dir;
        }

        // ── Create / Open roundtrip ──────────────────────────────────────────────

        [TestMethod]
        public void MemoryCardManager_CreateAndOpen_ProducesValidFile()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string mcPath = Path.Combine(tmp, "card.ps2");
                Ps2MemoryCard.Create(mcPath);
                Assert.IsTrue(File.Exists(mcPath), "card.ps2 should exist after Create()");

                using var mc = Ps2MemoryCard.Open(mcPath);
                var saves = mc.ListSaves();
                Assert.AreEqual(0, saves.Length, "Blank card should have no saves");
            }
            finally { Directory.Delete(tmp, true); }
        }

        // ── Import / ListSaves roundtrip ─────────────────────────────────────────

        [TestMethod]
        public void MemoryCardManager_ImportAndList_SingleSave()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string mcPath  = Path.Combine(tmp, "card.ps2");
                string saveDir = CreateSaveDir(tmp, "BESLES-12345",
                    ("icon.sys", 964), ("data.bin", 1024));

                Ps2MemoryCard.Create(mcPath);
                using (var mc = Ps2MemoryCard.Open(mcPath))
                {
                    mc.ImportSave(saveDir);
                    var saves = mc.ListSaves();
                    Assert.AreEqual(1, saves.Length, "Should find 1 save after import");
                    Assert.AreEqual("BESLES-12345", saves[0].Name);
                    Assert.AreEqual(2, saves[0].Files.Count);
                }

                // re-open and verify persistence
                using (var mc = Ps2MemoryCard.Open(mcPath))
                {
                    var saves = mc.ListSaves();
                    Assert.AreEqual(1, saves.Length, "Save should survive re-open");
                    Assert.AreEqual("BESLES-12345", saves[0].Name);
                }
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void MemoryCardManager_ImportAndList_MultipleSaves()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string mcPath = Path.Combine(tmp, "card.ps2");
                // Create enough saves so the root directory spans more than one cluster
                // (2 dir entries per 1024-byte cluster → 3+ saves need 2 root clusters)
                string[] names = ["SLES-12345", "SLES-23456", "SLES-34567", "SLES-45678"];
                string[] dirs  = names.Select(n => CreateSaveDir(tmp, n,
                    ("icon.sys", 256), ("data.bin", 512))).ToArray();

                Ps2MemoryCard.Create(mcPath);
                using (var mc = Ps2MemoryCard.Open(mcPath))
                {
                    foreach (string d in dirs)
                        mc.ImportSave(d);

                    var saves = mc.ListSaves();
                    Assert.AreEqual(names.Length, saves.Length, "All saves should be listed");
                    foreach (string n in names)
                        Assert.IsTrue(saves.Any(s => s.Name == n), $"{n} should be present");
                }

                // Verify after re-open (FAT chain traversal must handle multi-cluster root dir)
                using (var mc = Ps2MemoryCard.Open(mcPath))
                {
                    var saves = mc.ListSaves();
                    Assert.AreEqual(names.Length, saves.Length,
                        "All saves should persist after re-open (FAT chain must be readable)");
                }
            }
            finally { Directory.Delete(tmp, true); }
        }

        // ── FAT format verification ──────────────────────────────────────────────

        [TestMethod]
        public void MemoryCardManager_FatChainLinks_UseAllocatedBit()
        {
            // Verify that chain-link FAT values have bit 31 set (real PS2 format).
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string mcPath = Path.Combine(tmp, "card.ps2");
                // Import 4 saves so root dir needs 2 clusters; that creates a chain.
                string[] names = ["SLES-00001", "SLES-00002", "SLES-00003", "SLES-00004"];
                string[] dirs  = names.Select(n => CreateSaveDir(tmp, n,
                    ("icon.sys", 64))).ToArray();

                Ps2MemoryCard.Create(mcPath);
                using (var mc = Ps2MemoryCard.Open(mcPath))
                    foreach (string d in dirs)
                        mc.ImportSave(d);

                // Read the raw FAT from the file and verify chain entries have bit 31 set.
                byte[] raw = File.ReadAllBytes(mcPath);
                uint pageSize   = ReadU16(raw, 40);
                uint ppc        = ReadU16(raw, 42);
                uint ifcCluster = ReadU32(raw, 80);

                long ifcOff         = (long)(ifcCluster * ppc) * pageSize;
                uint fatClusterPtr  = ReadU32(raw, (int)ifcOff);

                long fatOff = (long)(fatClusterPtr * ppc) * pageSize;

                // FAT[0] is the root-dir cluster; with 4 saves it must chain to another cluster.
                uint fat0 = ReadU32(raw, (int)fatOff);
                Assert.AreNotEqual(0xFFFF_FFFFu, fat0, "FAT[0] should not be end-of-chain (root dir spans >1 cluster)");
                Assert.IsTrue((fat0 & 0x8000_0000u) != 0,
                    $"FAT[0] chain link 0x{fat0:X8} must have bit 31 set (real PS2 format)");
            }
            finally { Directory.Delete(tmp, true); }
        }

        // ── Delete ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void MemoryCardManager_DeleteSave_RemovesEntry()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string mcPath = Path.Combine(tmp, "card.ps2");
                string d1 = CreateSaveDir(tmp, "GAME-AA", ("data", 256));
                string d2 = CreateSaveDir(tmp, "GAME-BB", ("data", 256));

                Ps2MemoryCard.Create(mcPath);
                using (var mc = Ps2MemoryCard.Open(mcPath))
                {
                    mc.ImportSave(d1);
                    mc.ImportSave(d2);
                }

                using (var mc = Ps2MemoryCard.Open(mcPath))
                    mc.DeleteSave("GAME-AA");

                using (var mc = Ps2MemoryCard.Open(mcPath))
                {
                    var saves = mc.ListSaves();
                    Assert.AreEqual(1, saves.Length, "One save should remain");
                    Assert.AreEqual("GAME-BB", saves[0].Name);
                }
            }
            finally { Directory.Delete(tmp, true); }
        }

        // ── Export ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void MemoryCardManager_ExportSave_ProducesCorrectFiles()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string mcPath  = Path.Combine(tmp, "card.ps2");
                byte[] payload = new byte[256];
                new Random(42).NextBytes(payload);
                string saveDir = CreateSaveDir(tmp, "EXPORT-01", ("icon.sys", 964));
                File.WriteAllBytes(Path.Combine(saveDir, "save.bin"), payload);

                Ps2MemoryCard.Create(mcPath);
                using (var mc = Ps2MemoryCard.Open(mcPath))
                    mc.ImportSave(saveDir);

                string outDir = Path.Combine(tmp, "out");
                Directory.CreateDirectory(outDir);
                using (var mc = Ps2MemoryCard.Open(mcPath))
                    mc.ExportSave("EXPORT-01", outDir);

                byte[] exported = File.ReadAllBytes(Path.Combine(outDir, "save.bin"));
                CollectionAssert.AreEqual(payload, exported, "Exported file must match original");
            }
            finally { Directory.Delete(tmp, true); }
        }

        // ── raw-read helpers ─────────────────────────────────────────────────────

        private static uint ReadU32(byte[] buf, int off) =>
            (uint)(buf[off] | (buf[off+1] << 8) | (buf[off+2] << 16) | (buf[off+3] << 24));

        private static uint ReadU16(byte[] buf, int off) =>
            (uint)(buf[off] | (buf[off+1] << 8));
    }
}
