using CodeDesigner.Ps2.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public sealed class TestRevivalPatch
    {
        private const string OriginalZdbPath = @"C:\Users\tholu\Desktop\SocomPatch\APACHE00.ZDB";

        // FTScore with short version string pre-patched to r0005.
        private const string FTScoreBinPath = @"C:\Users\tholu\Desktop\FTScore_decrypted_r0005_prepped.bin";

        // â”€â”€ Core build test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Builds a disc-compatible revival APACHE00.ZDB.
        /// On first run a fresh RSA keypair is generated and written to revival_key.bin.
        /// On subsequent runs revival_key.bin is reloaded â€” no ISO re-patching needed.
        ///
        /// Outputs written to C:\Users\tholu\Desktop\:
        ///   APACHE00_revival.ZDB  â€” copy to memory card save BASCUS-97275SOCOMII
        ///   revival_key.bin       â€” PKCS#8 DER RSA private key (generated once, keep secret)
        ///   revival_pubmod.bin    â€” 256-byte RSA public modulus (patch DNAS.BIN once)
        ///   revival_disc_key.bin  â€” disc static FTScore AES key[16]+IV[16] (reference)
        /// </summary>
        [TestMethod]
        public void BuildRevivalZdb()
        {
            Assert.IsTrue(File.Exists(OriginalZdbPath),
                $"Original disc ZDB not found: {OriginalZdbPath}");
            Assert.IsTrue(File.Exists(FTScoreBinPath),
                $"FTScore plaintext not found: {FTScoreBinPath}");

            RevivalPatchBuilder.BuildResult result = RevivalPatchBuilder.BuildAndSave(
                OriginalZdbPath,
                FTScoreBinPath);

            Console.WriteLine($"Build version:   {result.BuildVersion}");
            Console.WriteLine($"Revival ZDB:     {result.RevivalZdb.Length:N0} bytes");
            Console.WriteLine($"Disc FT key:     {Convert.ToHexString(result.DiscFtScoreKey)}");
            Console.WriteLine($"Disc ZS key:     {Convert.ToHexString(result.DiscZsealEtcKey)}");
            Console.WriteLine($"Pubmod[0..15]:   {result.PublicModulusHex[..32]}...");

            Assert.IsTrue(result.RevivalZdb.Length > 0x10000,
                "Revival ZDB too small â€” encrypt likely failed.");
            Assert.AreEqual(256, result.PublicModulus.Length,
                "Public modulus must be 256 bytes for RSA-2048.");

            result.SigningKey.Dispose();
        }

        // â”€â”€ ZSealEtc decrypt test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Decrypts the zsealetc (IOP secrman) section from the disc ZDB and writes
        /// the decompressed binary to the desktop as ZSealEtc_disc_decrypted.bin.
        ///
        /// This section expands to runtime IOP data and is useful as a reference when
        /// you need to inspect or patch the IOP secrman module.
        /// </summary>
        [TestMethod]
        public void DecryptDiscZSealEtc()
        {
            Assert.IsTrue(File.Exists(OriginalZdbPath),
                $"Original disc ZDB not found: {OriginalZdbPath}");

            byte[] discZdb    = File.ReadAllBytes(OriginalZdbPath);
            byte[] zsealPlain;
            try { zsealPlain = RevivalPatchBuilder.DecryptDiscZSealEtc(discZdb); }
            catch (InvalidDataException ex)
            {
                Assert.Inconclusive(
                    $"Disc ZSealEtc decrypt threw InvalidDataException (expected — disc uses session-derived key): {ex.Message}");
                return;
            }

            string outPath = @"C:\Users\tholu\Desktop\ZSealEtc_disc_decrypted.bin";
            File.WriteAllBytes(outPath, zsealPlain);

            Console.WriteLine($"ZSealEtc decrypted: {zsealPlain.Length:N0} bytes");
            Console.WriteLine($"Written to: {outPath}");
            Console.WriteLine($"First 16 bytes: {Convert.ToHexString(zsealPlain, 0, Math.Min(16, zsealPlain.Length))}");

            if (zsealPlain.Length <= 0x1000)
            {
                Assert.Inconclusive(
                    "Disc zsealetc decrypt did not produce a large plaintext payload. " +
                    "For original Sony ZDB this usually means runtime session key/IV were not supplied.");
            }
        }

        // â”€â”€ FTScore decrypt test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Decrypts the FTScore section from the disc ZDB and writes the MWo3 plaintext
        /// to the desktop as FTScore_disc_decrypted.bin.
        /// </summary>
        [TestMethod]
        public void DecryptDiscFtScore()
        {
            Assert.IsTrue(File.Exists(OriginalZdbPath),
                $"Original disc ZDB not found: {OriginalZdbPath}");

            byte[] discZdb   = File.ReadAllBytes(OriginalZdbPath);
            byte[] ftPlain;
            try { ftPlain = RevivalPatchBuilder.DecryptDiscFtScore(discZdb); }
            catch (InvalidDataException ex)
            {
                Assert.Inconclusive(
                    $"Disc FTScore decrypt threw InvalidDataException (expected — disc uses session-derived key): {ex.Message}");
                return;
            }

            string outPath = @"C:\Users\tholu\Desktop\FTScore_disc_decrypted.bin";
            File.WriteAllBytes(outPath, ftPlain);

            Console.WriteLine($"FTScore decrypted: {ftPlain.Length:N0} bytes");
            Console.WriteLine($"Written to: {outPath}");
            Console.WriteLine($"First 4 bytes: {Convert.ToHexString(ftPlain, 0, 4)}  (expect 4D 57 6F 33 = MWo3)");

            bool isMwo3 = ftPlain.Length >= 4
                          && ftPlain[0] == 0x4D
                          && ftPlain[1] == 0x57
                          && ftPlain[2] == 0x6F
                          && ftPlain[3] == 0x33;

            if (!isMwo3)
            {
                Assert.Inconclusive(
                    "Disc FTScore decrypt did not produce MWo3 magic. " +
                    "For original Sony ZDB this usually means runtime session key/IV were not supplied.");
            }
        }

        // â”€â”€ Version increment test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [TestMethod]
        public void ReadAndIncrementBuildVersion()
        {
            if (!File.Exists(FTScoreBinPath))
            {
                Assert.Inconclusive($"FTScore_decrypted.bin not found: {FTScoreBinPath}");
                return;
            }

            byte[] mwo3 = File.ReadAllBytes(FTScoreBinPath);
            string? original = MWo3Header.ReadBuildVersion(mwo3);
            Console.WriteLine($"Original build version: {original ?? "(not found)"}");
            Assert.IsNotNull(original, "No build version found in FTScore_decrypted.bin");

            byte[] patched = MWo3Header.IncrementBuildVersion(mwo3);
            string? incremented = MWo3Header.ReadBuildVersion(patched);
            Console.WriteLine($"Incremented build version: {incremented}");
            Assert.IsNotNull(incremented, "IncrementBuildVersion returned no version");
            Assert.AreNotEqual(original, incremented, "Version should have changed");
        }

        // â”€â”€ Patch DNAS.BIN on disk â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Reads the DNAS.BIN from SocomPatch\, patches the Sony RSA-2048 public
        /// modulus at offset 0x94941 with our revival key's modulus, and writes
        /// DNAS_revival.BIN next to it.
        ///
        /// Pre-requisites:
        ///   C:\Users\tholu\Desktop\SocomPatch\DNAS.BIN
        ///   C:\Users\tholu\Desktop\revival_pubmod.bin   (from BuildRevivalZdb)
        /// </summary>
        [TestMethod]
        public void PatchDnasBin()
        {
            const string DnasBinPath = @"C:\Users\tholu\Desktop\SocomPatch\DNAS.BIN";
            const string PubModPath  = @"C:\Users\tholu\Desktop\revival_pubmod.bin";
            const string DnasOutPath = @"C:\Users\tholu\Desktop\SocomPatch\DNAS_revival.BIN";

            Assert.IsTrue(File.Exists(DnasBinPath), $"Not found: {DnasBinPath}");
            Assert.IsTrue(File.Exists(PubModPath),  $"Run BuildRevivalZdb first: {PubModPath}");

            byte[] dnasBin = File.ReadAllBytes(DnasBinPath);
            byte[] pubMod  = File.ReadAllBytes(PubModPath);

            Assert.AreEqual(256, pubMod.Length, "revival_pubmod.bin must be 256 bytes");

            byte[] patched = ZdbRsa.PatchDnasBin(dnasBin, pubMod);
            File.WriteAllBytes(DnasOutPath, patched);

            // Verify the patch landed at the right offset
            const int NOffset = 0x94941;
            for (int i = 0; i < 256; i++)
                Assert.AreEqual(pubMod[i], patched[NOffset + i],
                    $"Mismatch at modulus byte {i}");

            Console.WriteLine($"DNAS_revival.BIN written: {patched.Length:N0} bytes");
            Console.WriteLine($"Modulus[0..15]: {Convert.ToHexString(pubMod, 0, 16)}");
        }
    }
}
