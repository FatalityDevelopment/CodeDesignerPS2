using CodeDesigner.Ps2.FileFormats.Zipper.ZDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    // -------------------------------------------------------------------------
    //  High-level APACHE00.ZDB helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// High-level helper for APACHE00.ZDB.
    ///
    /// This class is intentionally archive-shaped because APACHE00.ZDB itself is a ZDB archive.
    /// The embedded FTScore runtime security-block transforms are exposed separately through
    /// <see cref="ApacheSecurityBlockCrypto"/>.
    ///
    /// Decrypt FTScore:
    /// <code>
    ///   using var apache = ApacheZdb.Open("APACHE00.ZDB");
    ///   byte[] plain = apache.DecryptSection(ApacheZdb.FTScore,
    ///       chunk => ZdbDispatch.EncodeFromAddress(destAddrs[chunk]));
    ///   var header = MWo3Header.Parse(plain);
    ///   Console.WriteLine(header.FileName); // "FTScore.bin"
    /// </code>
    ///
    /// Encrypt patched FTScore for revival server:
    /// <code>
    ///   using var apache = ApacheZdb.Open("APACHE00.ZDB");
    ///   byte[] patched  = File.ReadAllBytes("FTScore_patched.bin");
    ///   byte[] newBytes = apache.EncryptSection(ApacheZdb.FTScore, patched,
    ///       chunk => ZdbDispatch.EncodeFromAddress(destAddrs[chunk]));
    ///   apache.SetSectionPayload(ApacheZdb.FTScore, newBytes);
    ///   apache.Archive.Save("APACHE00_revival.ZDB");
    /// </code>
    /// </summary>
    [Obsolete("DEPRECATED — wrong cipher model. Retail APACHE00.ZDB is protected with RC6-CBC + " +
              "RSA-1024 signatures (not AES), as confirmed by live trace. This AES/vtable codec cannot " +
              "decrypt a retail ZDB. For read-only decrypt/extract/verify use ApacheZdbReader. " +
              "See memory note reference_apache-zdb-format.")]
    public sealed class ApacheZdb : IDisposable
    {
        // Section name constants (lowercase, as stored in ZDB directory)
        public const string FTScore = "ftscore";
        public const string ZSealEtc = "zsealetc";

        private readonly byte[] _key;
        private readonly byte[] _iv;
        private readonly byte[] _zsealKey;
        private readonly byte[] _zsealIv;

        /// <summary>Underlying archive for direct entry access, save, etc.</summary>
        public ZdbArchive Archive { get; }

        /// <summary>Archive-backed APACHE sections as stored in the ZDB.</summary>
        public IReadOnlyList<ZdbEntry> Sections => Archive.Entries;

        /// <summary>Number of archive-backed APACHE sections.</summary>
        public int SectionCount => Archive.EntryCount;

        private ApacheZdb(ZdbArchive archive, byte[] key, byte[] iv, byte[] zsealKey, byte[] zsealIv)
        {
            Archive  = archive;
            _key     = key;
            _iv      = iv;
            _zsealKey = zsealKey;
            _zsealIv  = zsealIv;
        }

        // ------------------------------------------------------------------
        // Factory
        // ------------------------------------------------------------------

        public static ApacheZdb Open(string filePath)
        {
            byte[] raw = File.ReadAllBytes(filePath);
            var archive = ZdbArchive.FromBytes(raw);
            var (key,     iv)     = ZdbCryptoProcessor.ExtractKeyIv(raw, FTScore);
            var (zsealKey, zsealIv) = ZdbCryptoProcessor.ExtractKeyIv(raw, ZSealEtc);
            return new ApacheZdb(archive, key, iv, zsealKey, zsealIv);
        }

        public static ApacheZdb FromBytes(byte[] data)
        {
            var archive = ZdbArchive.FromBytes(data);
            var (key,     iv)     = ZdbCryptoProcessor.ExtractKeyIv(data, FTScore);
            var (zsealKey, zsealIv) = ZdbCryptoProcessor.ExtractKeyIv(data, ZSealEtc);
            return new ApacheZdb(archive, key, iv, zsealKey, zsealIv);
        }

        // ------------------------------------------------------------------
        // Key / IV
        // ------------------------------------------------------------------

        /// <summary>
        /// Raw bytes at ftscore entry+0x300 (file 0x458).
        /// <b>NOT the actual AES-128 key</b> — see <see cref="ZdbCryptoProcessor.ExtractKeyIv"/> remarks.
        /// </summary>
        public ReadOnlySpan<byte> Key => _key;

        /// <summary>
        /// Raw bytes at ftscore entry+0x310 (file 0x468).
        /// <b>NOT the actual AES-128 IV</b> — see <see cref="ZdbCryptoProcessor.ExtractKeyIv"/> remarks.
        /// </summary>
        public ReadOnlySpan<byte> Iv => _iv;

        /// <summary>
        /// Raw bytes at zsealetc entry+0x300.
        /// <b>NOT the actual AES-128 key</b> — see <see cref="ZdbCryptoProcessor.ExtractKeyIv"/> remarks.
        /// </summary>
        public ReadOnlySpan<byte> ZSealEtcKey => _zsealKey;

        /// <summary>
        /// Raw bytes at zsealetc entry+0x310.
        /// <b>NOT the actual AES-128 IV</b> — see <see cref="ZdbCryptoProcessor.ExtractKeyIv"/> remarks.
        /// </summary>
        public ReadOnlySpan<byte> ZSealEtcIv => _zsealIv;

        /// <summary>Returns clones of the raw DNAS session-blob material at the ftscore key/IV positions.</summary>
        public (byte[] Key, byte[] Iv) GetEmbeddedSessionKeyMaterial() =>
            ((byte[])_key.Clone(), (byte[])_iv.Clone());

        /// <summary>Returns clones of the raw DNAS session-blob material at the zsealetc key/IV positions.</summary>
        public (byte[] Key, byte[] Iv) GetEmbeddedZSealEtcSessionKeyMaterial() =>
            ((byte[])_zsealKey.Clone(), (byte[])_zsealIv.Clone());

        /// <summary>Returns clones of the key/IV material for the named section (ftscore or zsealetc).</summary>
        public (byte[] Key, byte[] Iv) GetEmbeddedSessionKeyMaterial(string sectionName) =>
            string.Equals(sectionName, ZSealEtc, StringComparison.OrdinalIgnoreCase)
                ? ((byte[])_zsealKey.Clone(), (byte[])_zsealIv.Clone())
                : ((byte[])_key.Clone(), (byte[])_iv.Clone());

        /// <summary>Returns a clone of the FTScore session blob prefix stored ahead of the encrypted payload.</summary>
        public byte[] GetFtScoreSessionBlob()
        {
            ZdbEntry ftScoreEntry = RequireEntry(FTScore);
            if (ftScoreEntry.Data.Length < ApacheZdbLayout.FtScoreSessionBlobSize)
                throw new InvalidDataException("FTScore entry is shorter than the expected session blob.");

            byte[] blob = new byte[ApacheZdbLayout.FtScoreSessionBlobSize];
            Buffer.BlockCopy(ftScoreEntry.Data, 0, blob, 0, blob.Length);
            return blob;
        }

        /// <summary>Finds a section entry by name (case-insensitive).</summary>
        public ZdbEntry? FindSection(string sectionName) =>
            Archive.FindEntry(sectionName) ?? Archive.FindByName(sectionName).FirstOrDefault();

        /// <summary>Returns a section entry by name or throws when it does not exist.</summary>
        public ZdbEntry GetSection(string sectionName) => RequireEntry(sectionName);

        /// <summary>Returns a clone of the raw section bytes exactly as stored in the archive.</summary>
        public byte[] GetRawSectionData(string sectionName)
        {
            ZdbEntry section = RequireEntry(sectionName);
            return (byte[])section.Data.Clone();
        }

        // ------------------------------------------------------------------
        // Decrypt / Encrypt (section name = FTScore or ZSealEtc constant)
        // ------------------------------------------------------------------

        /// <summary>
        /// Best-effort decrypts a named section using the current chunk/AES pipeline.
        /// Each section uses its own embedded key/IV material (ftscore and zsealetc have independent values).
        /// For the separately verified runtime security-block transform, use <see cref="ApacheSecurityBlockCrypto"/>.
        /// </summary>
        public byte[] DecryptSection(
            string sectionName,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null)
        {
            ZdbEntry entry = RequireEntry(sectionName);
            byte[] k  = KeyForSection(sectionName);
            byte[] iv = IvForSection(sectionName);
            // Strip the 0x320-byte DNAS prefix ([DNAS blob 0x300][key 0x10][IV 0x10])
            // before passing to ZdbCryptoProcessor which expects [RSA sig][chunks].
            const int DnasPrefixSize = ApacheZdbOffsets.SessionIvOffset + 16; // 0x320
            if (entry.Data.Length <= DnasPrefixSize)
                throw new InvalidDataException($"Section '{sectionName}' is too short to contain a DNAS prefix (len={entry.Data.Length}).");
            byte[] payload = entry.Data[DnasPrefixSize..];
            return ZdbCryptoProcessor.DecryptSection(
                payload, k, iv, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// Decrypts a named section using address-derived dispatch words.
        /// Optional exclusion words can be applied to every chunk.
        /// </summary>
        public byte[] DecryptSectionAddressDerived(
            string sectionName,
            uint sectionLoadAddress,
            int[]? fixedExclusionWordIndices = null)
        {
            Func<int, uint> getDispatchWord =
                ApacheZdbDispatchProfiles.CreateLoadAddressDispatchProvider(sectionLoadAddress);

            Func<int, ISet<int>?>? getExclusionSet = null;
            if (fixedExclusionWordIndices != null && fixedExclusionWordIndices.Length > 0)
            {
                getExclusionSet = ApacheZdbDispatchProfiles.CreateFixedExcludedWordProvider(fixedExclusionWordIndices);
            }

            return DecryptSection(sectionName, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// One-call FTScore decrypt using mapped defaults:
        /// - load address: 0x001E7000
        /// - exclusions: {8, 10, 11}
        ///
        /// Throws if the output does not parse as MWo3 (useful for quick validation).
        /// </summary>
        public byte[] DecryptFTScorePatch(bool validateMwo3 = true)
        {
            byte[] plain = DecryptSectionAddressDerived(
                FTScore,
                ApacheZdbRuntimeDefaults.FtScoreLoadAddress,
                ApacheZdbRuntimeDefaults.FtScoreExcludedWordIndices);

            if (validateMwo3)
                _ = MWo3Header.Parse(plain);

            return plain;
        }

        /// <summary>
        /// One-call zsealetc decrypt using mapped defaults:
        /// - load address: 0x00188938
        /// - exclusions: none (current best-known profile)
        ///
        /// This section is typically raw code/data, not MWo3.
        /// </summary>
        public byte[] DecryptZSealEtcPatch()
        {
            return DecryptSectionAddressDerived(
                ZSealEtc,
                ApacheZdbRuntimeDefaults.ZSealEtcLoadAddress,
                ApacheZdbRuntimeDefaults.ZSealEtcExcludedWordIndices);
        }

        /// <summary>
        /// Decrypts all known APACHE00 payload sections with mapped defaults.
        /// Returns a dictionary keyed by section name (ftscore, zsealetc).
        /// </summary>
        public IReadOnlyDictionary<string, byte[]> DecryptAllKnownPatchSections(bool validateFtScoreMwo3 = true)
        {
            byte[] ft = DecryptFTScorePatch(validateFtScoreMwo3);
            byte[] zs = DecryptZSealEtcPatch();

            return new Dictionary<string, byte[]>
            {
                [FTScore] = ft,
                [ZSealEtc] = zs,
            };
        }

        /// <summary>
        /// Best-effort encrypts a plaintext payload for the named section using the current chunk/AES pipeline.
        /// Each section uses its own embedded key/IV material (ftscore and zsealetc have independent values).
        /// MWo3 magic is validated only for the ftscore section.
        /// </summary>
        public byte[] EncryptSection(
            string sectionName,
            byte[] plaintext,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null)
        {
            // Validate MWo3 magic for ftscore; zsealetc is raw code/data and has no MWo3 wrapper.
            if (string.Equals(sectionName, FTScore, StringComparison.OrdinalIgnoreCase))
                _ = MWo3Header.Parse(plaintext);

            byte[] k  = KeyForSection(sectionName);
            byte[] iv = IvForSection(sectionName);
            // EncryptSection returns [RSA sig][chunks] — prepend the DNAS prefix so the
            // output matches the on-disc section format [DNAS prefix 0x320][RSA sig][chunks].
            const int DnasPrefixSize = ApacheZdbOffsets.SessionIvOffset + 16; // 0x320
            ZdbEntry entry = RequireEntry(sectionName);
            if (entry.Data.Length < DnasPrefixSize)
                throw new InvalidDataException($"Section '{sectionName}' is too short to read DNAS prefix (len={entry.Data.Length}).");
            byte[] dnasPrefix = entry.Data[..DnasPrefixSize];
            byte[] encrypted  = ZdbCryptoProcessor.EncryptSection(
                plaintext, k, iv, getDispatchWord, getExclusionSet);
            byte[] result = new byte[DnasPrefixSize + encrypted.Length];
            Buffer.BlockCopy(dnasPrefix, 0, result, 0,              DnasPrefixSize);
            Buffer.BlockCopy(encrypted,  0, result, DnasPrefixSize, encrypted.Length);
            return result;
        }

        /// <summary>
        /// Encrypts a named section using address-derived dispatch words.
        /// Optional exclusion words can be applied to every chunk.
        /// </summary>
        public byte[] EncryptSectionAddressDerived(
            string sectionName,
            byte[] plaintext,
            uint sectionLoadAddress,
            int[]? fixedExclusionWordIndices = null)
        {
            Func<int, uint> getDispatchWord =
                ApacheZdbDispatchProfiles.CreateLoadAddressDispatchProvider(sectionLoadAddress);

            Func<int, ISet<int>?>? getExclusionSet = null;
            if (fixedExclusionWordIndices != null && fixedExclusionWordIndices.Length > 0)
            {
                getExclusionSet = ApacheZdbDispatchProfiles.CreateFixedExcludedWordProvider(fixedExclusionWordIndices);
            }

            return EncryptSection(sectionName, plaintext, getDispatchWord, getExclusionSet);
        }

        /// <summary>
        /// One-call FTScore encrypt using mapped defaults:
        /// - load address: 0x001E7000
        /// - exclusions: {8, 10, 11}
        /// </summary>
        public byte[] EncryptFTScorePatch(byte[] plaintext)
        {
            return EncryptSectionAddressDerived(
                FTScore,
                plaintext,
                ApacheZdbRuntimeDefaults.FtScoreLoadAddress,
                ApacheZdbRuntimeDefaults.FtScoreExcludedWordIndices);
        }

        /// <summary>
        /// One-call zsealetc encrypt using mapped defaults:
        /// - load address: 0x00188938
        /// - exclusions: none
        /// </summary>
        public byte[] EncryptZSealEtcPatch(byte[] plaintext)
        {
            return EncryptSectionAddressDerived(
                ZSealEtc,
                plaintext,
                ApacheZdbRuntimeDefaults.ZSealEtcLoadAddress,
                ApacheZdbRuntimeDefaults.ZSealEtcExcludedWordIndices);
        }

        /// <summary>
        /// Replaces the payload of a named section with already-encrypted bytes.
        /// Call Archive.Save() afterwards to write the updated ZDB to disk.
        /// </summary>
        public void SetSectionPayload(string sectionName, byte[] encryptedPayload)
        {
            ZdbEntry entry = RequireEntry(sectionName);
            int idx = IndexOfEntry(entry);
            Archive.ReplaceEntryData(idx, encryptedPayload);
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private byte[] KeyForSection(string sectionName) =>
            string.Equals(sectionName, ZSealEtc, StringComparison.OrdinalIgnoreCase) ? _zsealKey : _key;

        private byte[] IvForSection(string sectionName) =>
            string.Equals(sectionName, ZSealEtc, StringComparison.OrdinalIgnoreCase) ? _zsealIv : _iv;

        // ------------------------------------------------------------------
        // IDisposable
        // ------------------------------------------------------------------

        public void Dispose() => Archive.Dispose();

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------

        private ZdbEntry RequireEntry(string name)
        {
            ZdbEntry? e = FindSection(name);
            if (e is null)
                throw new KeyNotFoundException(
                    $"Section '{name}' not found in archive.");
            return e;
        }

        /// <summary>
        /// Returns the 0-based index of <paramref name="entry"/> in the archive entry list.
        /// Needed because <see cref="IReadOnlyList{T}"/> does not expose IndexOf.
        /// </summary>
        private int IndexOfEntry(ZdbEntry entry)
        {
            IReadOnlyList<ZdbEntry> entries = Archive.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i], entry)) return i;
            throw new InvalidOperationException("Entry not found in archive.");
        }
    }
}
