using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CodeDesigner.Ps2.Compression;
using CodeDesigner.Ps2.FileFormats.Zipper.ZDB;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    /// <summary>
    /// Builds a disc-compatible revival APACHE00.ZDB that runs on a factory console
    /// with no IOP secrman patches and no PNACH cheats — requiring only a one-time
    /// RSA public-key swap in DNAS.BIN (via <see cref="ZdbRsa.PatchDnasBin"/>).
    ///
    /// Disc-compatible section format (preserved verbatim for both sections):
    ///   [DNAS prefix  0x320 bytes]  = [Sony DNAS blob 0x300][AES key 0x10][AES IV 0x10]
    ///   [RSA-2048 sig 0x100 bytes]  = signed with our keypair (replaces Sony sig)
    ///   [AES+vtable encrypted chunks]
    ///
    /// Build workflow (per release):
    ///   1. Call <see cref="BuildAndSave"/> with the original disc ZDB + new FTScore plaintext.
    ///   2. On first run, a new RSA keypair is generated and saved to revival_key.bin.
    ///      Patch DNAS.BIN with the matching pubmod, then run patch_iso.py ONCE.
    ///   3. On subsequent runs, the saved keypair is reloaded — no ISO re-patching needed.
    ///   4. Copy APACHE00_revival.ZDB to the memory card save folder.
    ///
    /// What each build does:
    ///   ftscore  : version-increment ftscorePlaintext → encrypt with revival AES key → sign with our key
    ///   zsealetc : keep disc-encrypted body unchanged → replace Sony RSA sig with ours
    /// </summary>
    public static class ApachePatchBuilder
    {
        // DNAS prefix size: 0x300-byte blob + 16-byte key + 16-byte IV = 0x320 bytes.
        // Key is at prefix[0x300], IV is at prefix[0x310] (ApacheZdbOffsets.SessionKey/IvOffset).
        private const int DnasPrefixSize = ApacheZdbOffsets.SessionIvOffset + 16; // 0x310 + 16 = 0x320

        // Fixed revival AES key used for FTScore re-encryption.
        // Must match the runtime key-injection patch path (RSAKeyPatcher Patch 4).
        private static readonly byte[] RevivalFtScoreKey =
        {
            0xD5, 0xF6, 0xC6, 0xD5, 0x7C, 0x66, 0x49, 0x76,
            0x68, 0x25, 0x23, 0xD7, 0x57, 0xA8, 0xF6, 0x4B,
        };

        // Fixed revival IV used for FTScore re-encryption.
        // Captured live from AES_CBC_Core a1 ptr (0x01E7EB00) on first chunk after fresh boot.
        // Must match the IV injected by RSAKeyPatcher Patch 4.
        private static readonly byte[] RevivalFtScoreIv =
        {
            0xA4, 0xD3, 0x03, 0x46, 0x7A, 0x2B, 0x9B, 0x3C,
            0x4A, 0x3E, 0x38, 0xE2, 0x75, 0x92, 0x90, 0x15,
        };

        // ── Result ──────────────────────────────────────────────────────────

        /// <summary>
        /// Output from <see cref="Build"/>.
        /// Write <see cref="RevivalZdb"/> to the memory card as APACHE00.ZDB.
        /// Use <see cref="PublicModulus"/> to patch DNAS.BIN (one time only).
        /// </summary>
        public sealed class BuildResult
        {
            /// <summary>Full rebuilt ZDB bytes. Write as APACHE00.ZDB on the memory card.</summary>
            public byte[] RevivalZdb { get; init; } = Array.Empty<byte>();

            /// <summary>RSA-2048 private key used to sign both sections.</summary>
            public RSA SigningKey { get; init; } = RSA.Create(2048);

            /// <summary>RSA-2048 public modulus (256 bytes). Patch into DNAS.BIN once.</summary>
            public byte[] PublicModulus { get; init; } = Array.Empty<byte>();

            /// <summary>Public modulus as uppercase hex (no spaces).</summary>
            public string PublicModulusHex => Convert.ToHexString(PublicModulus);

            /// <summary>FTScore disc-embedded AES-128 key extracted from DNAS prefix.</summary>
            public byte[] DiscFtScoreKey { get; init; } = Array.Empty<byte>();

            /// <summary>FTScore disc-embedded AES-128 IV extracted from DNAS prefix.</summary>
            public byte[] DiscFtScoreIv { get; init; } = Array.Empty<byte>();

            /// <summary>ZSealEtc disc-embedded AES-128 key extracted from DNAS prefix.</summary>
            public byte[] DiscZsealEtcKey { get; init; } = Array.Empty<byte>();

            /// <summary>FTScore MWo3 build version after incrementing (e.g. "r0005").</summary>
            public string BuildVersion { get; init; } = string.Empty;
        }

        // ── Public entry point ───────────────────────────────────────────────

        /// <summary>
        /// Builds a disc-compatible revival ZDB.
        ///
        /// <para>
        /// Pass <paramref name="signingKey"/> to reuse an existing RSA-2048 keypair (loaded
        /// from revival_key.bin).  When null, a fresh keypair is generated — save
        /// <see cref="BuildResult.SigningKey"/> to revival_key.bin and patch DNAS.BIN once.
        /// </para>
        /// </summary>
        /// <param name="originalZdb">Raw bytes of the original (disc) APACHE00.ZDB.</param>
        /// <param name="ftscorePlaintext">
        ///   Decrypted FTScore MWo3 bytes (magic 4D 57 6F 33 at offset 0).
        ///   Captured live from EE RAM at 0x001E7000, or FTScore_decrypted_r0005_prepped.bin.
        /// </param>
        /// <param name="signingKey">
        ///   RSA-2048 private key to sign with. Pass null to generate a fresh keypair.
        ///   Caller retains ownership; the key is NOT disposed by this method.
        /// </param>
        public static BuildResult Build(
            byte[] originalZdb,
            byte[] ftscorePlaintext,
            RSA? signingKey = null)
        {
            // ── Extract DNAS prefixes and disc keys ───────────────────────
            using var archive = ZdbArchive.FromBytes(originalZdb);

            int ftIdx  = FindEntryIndex(archive, ApacheZdb.FTScore);
            int zsIdx  = FindEntryIndex(archive, ApacheZdb.ZSealEtc);

            byte[] ftRaw = archive.GetEntry(ftIdx).Data;
            byte[] zsRaw = archive.GetEntry(zsIdx).Data;

            ValidateDiscSection(ftRaw, ApacheZdb.FTScore);
            ValidateDiscSection(zsRaw, ApacheZdb.ZSealEtc);

            byte[] ftDnasPrefix = ftRaw[..DnasPrefixSize];
            byte[] ftDiscKey    = ftRaw[ApacheZdbOffsets.SessionKeyOffset..(ApacheZdbOffsets.SessionKeyOffset + 16)];
            byte[] ftDiscIv     = ftRaw[ApacheZdbOffsets.SessionIvOffset..(ApacheZdbOffsets.SessionIvOffset + 16)];

            byte[] zsDnasPrefix  = zsRaw[..DnasPrefixSize];
            byte[] zsDiscKey     = zsRaw[ApacheZdbOffsets.SessionKeyOffset..(ApacheZdbOffsets.SessionKeyOffset + 16)];

            // ── Patch build version ───────────────────────────────────────
            string? originalVersion = MWo3Header.ReadBuildVersion(ftscorePlaintext);
            byte[]  patchedFt       = MWo3Header.IncrementBuildVersion(ftscorePlaintext);
            string  newVersion      = MWo3Header.ReadBuildVersion(patchedFt) ?? originalVersion ?? "(unknown)";

            // ── Signing key ───────────────────────────────────────────────
            bool ownKey = (signingKey == null);
            if (ownKey) signingKey = ZdbRsa.GenerateSigningKeyPair();

            var signOpts = new ZdbPipelineOptions
            {
                EnableOuterTransforms = true,
                EnableZlib            = true,
                EnableRsaVerify       = false,
                EnableRsaSign         = true,
                SigningKey            = signingKey,
            };

            // ── Encrypt new FTScore with fixed revival key ────────────────
            Func<int, uint> ftDispatch =
                ApacheZdbDispatchProfiles.CreateLoadAddressDispatchProvider(
                    ApacheZdbRuntimeDefaults.FtScoreLoadAddress);
            Func<int, ISet<int>?> ftExclusions =
                ApacheZdbDispatchProfiles.CreateFixedExcludedWordProvider(
                    ApacheZdbRuntimeDefaults.FtScoreExcludedWordIndices);

            byte[] ftEncrypted = ZdbCryptoProcessor.EncryptSection(
                patchedFt, RevivalFtScoreKey, RevivalFtScoreIv, ftDispatch, ftExclusions, signOpts);

            // ── Re-sign ZSealEtc: keep disc-encrypted body, swap RSA sig ──
            // zsRaw layout: [DNAS prefix 0x320][Sony RSA sig 0x100][encrypted body ...]
            // We keep the encrypted body unchanged (same disc AES key) and replace the sig.
            int       zsBodyStart     = DnasPrefixSize + ApacheZdbLayout.RsaSignaturePrefixSize;
            byte[]    zsEncryptedBody = zsRaw[zsBodyStart..];
            byte[]    zsOurSig        = ZdbRsa.Sign(zsEncryptedBody, signingKey!);

            byte[] zsSection = new byte[zsBodyStart + zsEncryptedBody.Length];
            Buffer.BlockCopy(zsDnasPrefix,    0, zsSection, 0,           DnasPrefixSize);
            Buffer.BlockCopy(zsOurSig,        0, zsSection, DnasPrefixSize, ApacheZdbLayout.RsaSignaturePrefixSize);
            Buffer.BlockCopy(zsEncryptedBody, 0, zsSection, zsBodyStart,    zsEncryptedBody.Length);

            // ── Build new FTScore section: DNAS prefix + encrypted payload ──
            byte[] ftSection = new byte[DnasPrefixSize + ftEncrypted.Length];
            Buffer.BlockCopy(ftDnasPrefix, 0, ftSection, 0,           DnasPrefixSize);
            Buffer.BlockCopy(ftEncrypted,  0, ftSection, DnasPrefixSize, ftEncrypted.Length);

            // ── Replace sections and serialise ────────────────────────────
            archive.ReplaceEntryData(ftIdx, ftSection);
            archive.ReplaceEntryData(zsIdx, zsSection);
            byte[] revivalZdb = archive.ToBytes();

            // ── Export signing key for caller ──────────────────────────────
            RSA exportedKey = RSA.Create();
            exportedKey.ImportParameters(signingKey!.ExportParameters(includePrivateParameters: true));
            if (ownKey) signingKey.Dispose();

            Console.WriteLine($"[RevivalPatchBuilder] FTScore built:   {ftEncrypted.Length:N0} bytes encrypted (version {newVersion})");
            Console.WriteLine($"[RevivalPatchBuilder] ZSealEtc re-signed: {zsEncryptedBody.Length:N0} bytes body kept, Sony sig replaced.");

            return new BuildResult
            {
                RevivalZdb      = revivalZdb,
                SigningKey      = exportedKey,
                PublicModulus   = ZdbRsa.ExportPublicModulus(exportedKey),
                DiscFtScoreKey  = ftDiscKey,
                DiscFtScoreIv   = ftDiscIv,
                DiscZsealEtcKey = zsDiscKey,
                BuildVersion    = newVersion,
            };
        }

        /// <summary>
        /// Decrypts the FTScore section from a disc ZDB using its embedded static key.
        /// Returns the raw MWo3 plaintext (starts with magic 4D 57 6F 33).
        /// Useful for roundtrip tests and for extracting FTScore content before patching.
        /// </summary>
        public static byte[] DecryptDiscFtScore(byte[] discZdb)
        {
            using var archive = ZdbArchive.FromBytes(discZdb);
            int    ftIdx     = FindEntryIndex(archive, ApacheZdb.FTScore);
            byte[] ftRaw     = archive.GetEntry(ftIdx).Data;

            ValidateDiscSection(ftRaw, ApacheZdb.FTScore);

            byte[] key = ftRaw[ApacheZdbOffsets.SessionKeyOffset..(ApacheZdbOffsets.SessionKeyOffset + 16)];
            byte[] iv  = ftRaw[ApacheZdbOffsets.SessionIvOffset..(ApacheZdbOffsets.SessionIvOffset + 16)];

            Func<int, uint>        dispatch   = ApacheZdbDispatchProfiles.CreateLoadAddressDispatchProvider(ApacheZdbRuntimeDefaults.FtScoreLoadAddress);
            Func<int, ISet<int>?>  exclusions = ApacheZdbDispatchProfiles.CreateFixedExcludedWordProvider(ApacheZdbRuntimeDefaults.FtScoreExcludedWordIndices);

            var decryptOpts = new ZdbPipelineOptions
            {
                EnableOuterTransforms = true,
                EnableZlib            = true,
                EnableRsaVerify       = false,
            };

            // Strip DNAS prefix — DecryptSection expects [RSA sig][encrypted chunks].
            return ZdbCryptoProcessor.DecryptSection(
                ftRaw[DnasPrefixSize..], key, iv, dispatch, exclusions, decryptOpts);
        }

        // ── Convenience overload: load from files ────────────────────────────

        /// <summary>
        /// Loads files from disk, calls <see cref="Build"/>, and writes outputs to the
        /// same directory as <paramref name="originalZdbPath"/>.
        ///
        /// RSA keypair persistence:
        ///   - On first run: generates a fresh keypair, writes revival_key.bin.
        ///     Patch DNAS.BIN once with the resulting revival_pubmod.bin.
        ///   - On subsequent runs: loads revival_key.bin so the same signature key is
        ///     reused — no ISO re-patching needed.
        ///
        /// Output files:
        ///   APACHE00_revival.ZDB — copy to the memory card save folder
        ///   revival_key.bin      — PKCS#8 DER RSA private key (generated once, keep secret)
        ///   revival_pubmod.bin   — RSA public modulus 256 bytes (patch into DNAS.BIN once)
        ///   revival_disc_key.bin — disc static AES key[16] + IV[16] (reference only)
        /// </summary>
        public static BuildResult BuildAndSave(
            string  originalZdbPath,
            string  ftscoreBinPath,
            string? signingKeyPath = null)
        {
            byte[] originalZdb      = File.ReadAllBytes(originalZdbPath);
            byte[] ftscorePlaintext = File.ReadAllBytes(ftscoreBinPath);

            string dir = Path.GetDirectoryName(Path.GetFullPath(originalZdbPath))
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            signingKeyPath ??= Path.Combine(dir, "revival_key.bin");

            // Load existing keypair if available; generate fresh on first run.
            RSA? loadedKey = null;
            if (File.Exists(signingKeyPath))
            {
                try
                {
                    loadedKey = RSA.Create();
                    loadedKey.ImportPkcs8PrivateKey(File.ReadAllBytes(signingKeyPath), out _);
                    Console.WriteLine($"[RevivalPatchBuilder] Loaded signing key from {signingKeyPath}");
                }
                catch (Exception ex)
                {
                    loadedKey?.Dispose();
                    loadedKey = null;
                    Console.WriteLine($"[RevivalPatchBuilder] WARNING: Could not load key from {signingKeyPath} ({ex.Message}); generating fresh key.");
                }
            }

            BuildResult result = Build(originalZdb, ftscorePlaintext, loadedKey);
            loadedKey?.Dispose();

            // Save key on first run (file didn't exist before Build()).
            if (!File.Exists(signingKeyPath))
                File.WriteAllBytes(signingKeyPath, result.SigningKey.ExportPkcs8PrivateKey());

            // ZDB
            string revivalZdbPath = Path.Combine(dir, "APACHE00_revival.ZDB");
            File.WriteAllBytes(revivalZdbPath, result.RevivalZdb);

            // RSA public modulus (256 bytes — patch into DNAS.BIN once)
            string pubModPath = Path.Combine(dir, "revival_pubmod.bin");
            File.WriteAllBytes(pubModPath, result.PublicModulus);

            // Disc AES key + IV (reference copy; not needed for PS2-side patches)
            string discKeyPath = Path.Combine(dir, "revival_disc_key.bin");
            byte[] discKeyBlob = new byte[32];
            Buffer.BlockCopy(result.DiscFtScoreKey, 0, discKeyBlob,  0, 16);
            Buffer.BlockCopy(result.DiscFtScoreIv,  0, discKeyBlob, 16, 16);
            File.WriteAllBytes(discKeyPath, discKeyBlob);

            Console.WriteLine($"[RevivalPatchBuilder] Build version:   {result.BuildVersion}");
            Console.WriteLine($"[RevivalPatchBuilder] Revival ZDB:     {revivalZdbPath} ({result.RevivalZdb.Length:N0} bytes)");
            Console.WriteLine($"[RevivalPatchBuilder] Signing key:     {signingKeyPath}");
            Console.WriteLine($"[RevivalPatchBuilder] Public modulus:  {pubModPath}");
            Console.WriteLine($"[RevivalPatchBuilder] Revival FT key:  {Convert.ToHexString(RevivalFtScoreKey)}");
            Console.WriteLine($"[RevivalPatchBuilder] Disc FT key:     {Convert.ToHexString(result.DiscFtScoreKey)}");
            Console.WriteLine($"[RevivalPatchBuilder] Disc ZS key:     {Convert.ToHexString(result.DiscZsealEtcKey)}");
            Console.WriteLine($"[RevivalPatchBuilder] Pubmod first 32 bytes: {result.PublicModulusHex[..64]}...");

            return result;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static int FindEntryIndex(ZdbArchive archive, string sectionName)
        {
            for (int i = 0; i < archive.EntryCount; i++)
            {
                if (string.Equals(archive.GetEntry(i).Name, sectionName,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            throw new KeyNotFoundException(
                $"Section '{sectionName}' not found in archive.");
        }

        /// <summary>
        /// Decrypts the ZSealEtc (IOP secrman) section from a disc ZDB using its
        /// embedded static key.  Returns the raw decompressed plaintext bytes.
        ///
        /// The zsealetc payload expands to runtime IOP data.
        /// Do not assume MWo3 magic in this section.
        /// The disc static AES key/IV are embedded in the DNAS prefix at section+0x300/0x310.
        /// </summary>
        public static byte[] DecryptDiscZSealEtc(byte[] discZdb)
        {
            using var archive = ZdbArchive.FromBytes(discZdb);
            int    zsIdx = FindEntryIndex(archive, ApacheZdb.ZSealEtc);
            byte[] zsRaw = archive.GetEntry(zsIdx).Data;

            ValidateDiscSection(zsRaw, ApacheZdb.ZSealEtc);

            byte[] key = zsRaw[ApacheZdbOffsets.SessionKeyOffset..(ApacheZdbOffsets.SessionKeyOffset + 16)];
            byte[] iv  = zsRaw[ApacheZdbOffsets.SessionIvOffset..(ApacheZdbOffsets.SessionIvOffset + 16)];

            Func<int, uint>       dispatch   = ApacheZdbDispatchProfiles.CreateLoadAddressDispatchProvider(ApacheZdbRuntimeDefaults.ZSealEtcLoadAddress);
            Func<int, ISet<int>?> exclusions = ApacheZdbDispatchProfiles.CreateFixedExcludedWordProvider(ApacheZdbRuntimeDefaults.ZSealEtcExcludedWordIndices);

            var decryptOpts = new ZdbPipelineOptions
            {
                EnableOuterTransforms = true,
                EnableZlib            = true,
                EnableRsaVerify       = false,
            };

            // Strip DNAS prefix — DecryptSection expects [RSA sig][encrypted chunks].
            return ZdbCryptoProcessor.DecryptSection(
                zsRaw[DnasPrefixSize..], key, iv, dispatch, exclusions, decryptOpts);
        }

        private static void ValidateDiscSection(byte[] sectionData, string sectionName)
        {
            if (sectionData.Length < DnasPrefixSize + ApacheZdbLayout.RsaSignaturePrefixSize)
                throw new InvalidDataException(
                    $"Section '{sectionName}' is too short to be a disc-format section " +
                    $"(len={sectionData.Length}, need >{DnasPrefixSize + ApacheZdbLayout.RsaSignaturePrefixSize}).");
        }

        /// <summary>
        /// Generates the PNACH-format lines needed to overwrite the Sony RSA-2048
        /// public key modulus in EE RAM with <paramref name="newModulus"/>.
        ///
        /// Each PNACH line writes 4 bytes (word) at a time.
        ///
        ///   patch=1,EE,XXXXXXXX,word,YYYYYYYY
        ///
        /// <paramref name="eeBaseAddress"/> is the EE RAM address where the first byte
        /// of the 256-byte Sony modulus is stored at runtime.  Find it by scanning EE RAM
        /// for the first 16 bytes of the Sony public key:
        ///   A8 9D F0 F2 BA BB D4 FA 09 5C AB 16 35 CE C9 32
        ///
        /// Note: PNACH EE writes are BIG-ENDIAN (the PS2 is big-endian for EE memory word
        /// operations even though the R5900 is generally little-endian for data memory).
        /// However, PNACH 'word' writes store the value with the address byte first.
        /// Confirm endianness with your cheat device if writes appear byte-swapped.
        /// </summary>
        public static string GeneratePnachLines(byte[] newModulus, uint eeBaseAddress)
        {
            if (newModulus.Length != 256)
                throw new ArgumentException("Modulus must be 256 bytes.", nameof(newModulus));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// Revival RSA public key patch — replaces Sony DNAS modulus in EE RAM");
            sb.AppendLine($"// Base address: 0x{eeBaseAddress:X8}  (256 bytes)");
            sb.AppendLine();

            for (int i = 0; i < 256; i += 4)
            {
                // PNACH word = 4 bytes at address; PS2 EE is big-endian for read/write.
                uint word = ((uint)newModulus[i]     << 24)
                          | ((uint)newModulus[i + 1] << 16)
                          | ((uint)newModulus[i + 2] <<  8)
                          |  (uint)newModulus[i + 3];
                uint addr = eeBaseAddress + (uint)i;
                sb.AppendLine($"patch=1,EE,{addr:X8},word,{word:X8}");
            }
            return sb.ToString();
        }
    }
}
