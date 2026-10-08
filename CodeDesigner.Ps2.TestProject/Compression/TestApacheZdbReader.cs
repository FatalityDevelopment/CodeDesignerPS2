using System;
using System.IO;
using CodeDesigner.Ps2.FileFormats.Zipper.Apache;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Read-only decrypt of the retail APACHE00.ZDB. Keys are recovered from an EE RAM dump
    /// of the user's own game (no secrets embedded). Inputs are optional; the test is
    /// inconclusive when they are absent so it never breaks a clean checkout.
    /// </summary>
    [TestClass]
    public sealed class TestApacheZdbReader
    {
        // A full EE RAM dump (32 MB, address 0 == file offset 0), e.g. from pcsx2_extract_p2s.
        private const string EeDumpPath = @"C:\Users\tholu\Desktop\SocomPatch\EEmem.bin";
        private const string RetailZdbPath = @"C:\Users\tholu\Downloads\BASCUS-97275SOCOMII\APACHE00.ZDB";

        // Fixed RC6 key/IV that unwraps the +0x180 block; a constant in the game executable.
        private static readonly byte[] BulkWrapKey = Convert.FromHexString("D06AFA65F34C133ACD4B085319EEAE48");
        private static readonly byte[] BulkWrapIv = Convert.FromHexString("8747142" + "0D4291511F71C0233EF7E6D89");

        [TestMethod]
        public void DecryptRetailZdb()
        {
            if (!File.Exists(EeDumpPath) || !File.Exists(RetailZdbPath))
            {
                Assert.Inconclusive($"Need {EeDumpPath} and {RetailZdbPath} to run this read-only decrypt test.");
                return;
            }

            byte[] ee = File.ReadAllBytes(EeDumpPath);
            byte[] zdb = File.ReadAllBytes(RetailZdbPath);

            var keys = ApacheKeyTable.FromImage(ee, (int)ApacheKeyTable.DefaultTableEeAddress);
            Assert.AreEqual(ApacheKeyTable.EntryCount, keys.Count);

            var reader = new ApacheZdbReader(keys, BulkWrapKey, BulkWrapIv);
            var sections = reader.ReadAll(zdb);

            Assert.IsTrue(sections.Count >= 2, "Expected at least ftscore + zsealetc sections.");
            foreach (var s in sections)
            {
                Console.WriteLine($"{s.Name}: {s.Mwo3.Length:N0} bytes, file '{s.Mwo3FileName}', " +
                                  $"version '{s.VersionMagic}', tailDigestValid={s.TailDigestValid}");
                Assert.IsTrue(s.Mwo3.Length > 0x1000, $"{s.Name} produced no payload.");
                Assert.AreEqual(0x4D576F33u, BitConverter.ToUInt32(s.Mwo3, 0), $"{s.Name} is not MWo3.");
                Assert.IsTrue(s.TailDigestValid, $"{s.Name} tail digest mismatch.");
            }
        }
    }
}
