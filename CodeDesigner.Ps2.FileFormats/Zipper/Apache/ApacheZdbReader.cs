using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    /// <summary>
    /// READ-ONLY decryptor for retail APACHE00.ZDB sections (SOCOM II, r0004 layout).
    ///
    /// Reverse-engineered by live PCSX2 trace. Per section (ftscore, zsealetc), from the
    /// section base:
    ///   +0x000 RSA(key7)  -> "0500 0200" + magic      (format/version marker)
    ///   +0x080 RSA(key16) -> 20-byte digest
    ///   +0x100 RSA(key15) -> layer-5 key[16] + IV[16]
    ///   +0x180 RC6(fixed) -> bulk key[16] + IV[16]
    ///   +0x200 / +0x400   -> duplicated 0x80 filler (first 0x40 signed by +0x380)
    ///   +0x280 RSA(key5)  -> sizes: layer5Len u32, zlibLen u32, b8, zlibOff/16, layer5Off/16
    ///   +0x300 RSA(key13) -> header key[16] + IV[16]
    ///   +0x380 RSA(key23) -> copy of +0x200[..0x40] (CBC-MAC input a)
    ///   +0x480 RSA(key26) -> CBC-MAC input b
    ///   +0x500 RC6(headerKey) 0x80 -> "[U_PKMS]…" server header
    ///   +0x580 .. end-0x80 RC6(bulkKey) in place -> bulk plaintext
    ///   tail   RSA(key10) -> SHA-1 over the layer-5 ciphertext
    /// layer-5 IV = RC6-CBC-MAC(layer5Key, layer5IV, body(+0x380) || body(+0x480)).
    /// layer-5 plaintext = RC6-CBC(layer5Key, layer5IV, bulk[layer5Off : +layer5Len]).
    /// MWo3 image = zlib-inflate(layer5[zlibOff : +zlibLen]).
    ///
    /// This class only reads: it decrypts, extracts and verifies. It performs no signing or
    /// repacking.
    /// </summary>
    public sealed class ApacheZdbReader
    {
        // Section-relative offsets of the signed/wrapped blocks.
        private const int OffVersion = 0x000, KeyVersion = 7;
        private const int OffDigest80 = 0x080, KeyDigest80 = 16;
        private const int OffLayer5 = 0x100, KeyLayer5 = 15;
        private const int OffBulkWrap = 0x180;
        private const int OffSizes = 0x280, KeySizes = 5;
        private const int OffHeaderKey = 0x300, KeyHeaderKey = 13;
        private const int OffMacA = 0x380, KeyMacA = 23;
        private const int OffMacB = 0x480, KeyMacB = 26;
        private const int OffHeaderText = 0x500;
        private const int OffBulk = 0x580;
        private const int RsaBlock = 0x80;
        private const int TailSize = 0x80, KeyTail = 10;

        private readonly ApacheKeyTable _keys;
        private readonly byte[] _bulkWrapKey; // the fixed +0x180 RC6 key, from the game executable
        private readonly byte[] _bulkWrapIv;

        /// <param name="keys">Key table recovered from the game image.</param>
        /// <param name="bulkWrapKey">16-byte fixed RC6 key that unwraps the +0x180 block (from the game executable).</param>
        /// <param name="bulkWrapIv">16-byte fixed RC6 IV paired with <paramref name="bulkWrapKey"/>.</param>
        public ApacheZdbReader(ApacheKeyTable keys, byte[] bulkWrapKey, byte[] bulkWrapIv)
        {
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            if (bulkWrapKey is null || bulkWrapKey.Length != 16) throw new ArgumentException("bulkWrapKey must be 16 bytes.", nameof(bulkWrapKey));
            if (bulkWrapIv is null || bulkWrapIv.Length != 16) throw new ArgumentException("bulkWrapIv must be 16 bytes.", nameof(bulkWrapIv));
            _bulkWrapKey = (byte[])bulkWrapKey.Clone();
            _bulkWrapIv = (byte[])bulkWrapIv.Clone();
        }

        public sealed class SectionResult
        {
            public string Name { get; init; } = "";
            public uint FileOffset { get; init; }
            public uint StoredSize { get; init; }
            public string VersionMagic { get; init; } = "";
            public string ServerHeader { get; init; } = "";
            public byte[] Mwo3 { get; init; } = Array.Empty<byte>();
            public bool TailDigestValid { get; init; }
            public string Mwo3FileName { get; init; } = "";
        }

        /// <summary>Decrypts and verifies every section of the ZDB.</summary>
        public IReadOnlyList<SectionResult> ReadAll(byte[] zdb)
        {
            if (zdb is null) throw new ArgumentNullException(nameof(zdb));
            var results = new List<SectionResult>();
            uint count = BitConverter.ToUInt32(zdb, 0x98);
            int dir = 0xA0;
            for (uint i = 0; i < count; i++)
            {
                uint entrySize = BitConverter.ToUInt32(zdb, dir);
                string name = ReadCName(zdb, dir + 0x04, dir + 0x44);
                uint dataOff = BitConverter.ToUInt32(zdb, dir + 0x44);
                uint stored = BitConverter.ToUInt32(zdb, dir + 0x48);
                byte[] sec = zdb[(int)dataOff..(int)(dataOff + stored)];
                results.Add(ReadSection(name, dataOff, stored, sec));
                dir += (int)entrySize;
            }
            return results;
        }

        private SectionResult ReadSection(string name, uint fileOff, uint stored, byte[] sec)
        {
            var (_, versionMagic) = _keys.RecoverSignedBlock(KeyVersion, sec.AsSpan(OffVersion, RsaBlock));
            var (layer5, _) = _keys.RecoverSignedBlock(KeyLayer5, sec.AsSpan(OffLayer5, RsaBlock));
            var (sizes, _) = _keys.RecoverSignedBlock(KeySizes, sec.AsSpan(OffSizes, RsaBlock));
            var (headerKey, _) = _keys.RecoverSignedBlock(KeyHeaderKey, sec.AsSpan(OffHeaderKey, RsaBlock));
            var (macA, _) = _keys.RecoverSignedBlock(KeyMacA, sec.AsSpan(OffMacA, RsaBlock));
            var (macB, _) = _keys.RecoverSignedBlock(KeyMacB, sec.AsSpan(OffMacB, RsaBlock));
            var (tailDigest, _) = _keys.RecoverSignedBlock(KeyTail, sec.AsSpan(sec.Length - TailSize, RsaBlock));

            // Server header (informational).
            var hdrCipher = new Rc6(headerKey.AsSpan(0, 16));
            byte[] header = hdrCipher.DecryptCbc(sec.AsSpan(OffHeaderText, RsaBlock), headerKey.AsSpan(16, 16));
            string serverHeader = ReadPrintable(header);

            // Bulk key via the fixed wrap, then the whole bulk body.
            var wrap = new Rc6(_bulkWrapKey);
            byte[] bulkKeyIv = wrap.DecryptCbc(sec.AsSpan(OffBulkWrap, 0x80), _bulkWrapIv);
            var bulkCipher = new Rc6(bulkKeyIv.AsSpan(0, 16));
            byte[] bulk = bulkCipher.DecryptCbc(sec.AsSpan(OffBulk, sec.Length - OffBulk - TailSize), bulkKeyIv.AsSpan(16, 16));

            uint layer5Len = BitConverter.ToUInt32(sizes, 0);
            uint zlibLen = BitConverter.ToUInt32(sizes, 4);
            int zlibOff = sizes[9] * 16;
            int layer5Off = sizes[10] * 16;

            // layer-5 IV is a CBC-MAC over the two signed MAC blocks.
            var l5Cipher = new Rc6(layer5.AsSpan(0, 16));
            byte[] macInput = Concat(macA, macB);
            byte[] layer5Iv = l5Cipher.CbcMac(macInput, layer5.AsSpan(16, 16));

            byte[] layer5Cipher = bulk[layer5Off..(layer5Off + (int)layer5Len)];
            bool tailOk = Sha1Equals(layer5Cipher, tailDigest);

            byte[] layer5Plain = l5Cipher.DecryptCbc(layer5Cipher, layer5Iv);
            byte[] zlibStream = layer5Plain[zlibOff..(zlibOff + (int)zlibLen)];
            byte[] mwo3 = Inflate(zlibStream);

            return new SectionResult
            {
                Name = name,
                FileOffset = fileOff,
                StoredSize = stored,
                VersionMagic = versionMagic,
                ServerHeader = serverHeader,
                Mwo3 = mwo3,
                TailDigestValid = tailOk,
                Mwo3FileName = ReadMwo3FileName(mwo3),
            };
        }

        private static bool Sha1Equals(byte[] data, byte[] expected20)
        {
            byte[] h = SHA1.HashData(data);
            if (expected20.Length < 20) return false;
            for (int i = 0; i < 20; i++) if (h[i] != expected20[i]) return false;
            return true;
        }

        private static byte[] Inflate(byte[] zlib)
        {
            using var input = new MemoryStream(zlib);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var o = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, o, 0, a.Length);
            Buffer.BlockCopy(b, 0, o, a.Length, b.Length);
            return o;
        }

        private static string ReadCName(byte[] buf, int start, int end)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < end && buf[i] != 0; i++) sb.Append((char)buf[i]);
            return sb.ToString();
        }

        private static string ReadPrintable(byte[] buf)
        {
            var sb = new System.Text.StringBuilder();
            foreach (byte b in buf) { if (b == 0) break; sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.'); }
            return sb.ToString();
        }

        private static string ReadMwo3FileName(byte[] mwo3)
        {
            if (mwo3.Length < 0x40) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0x20; i < 0x40 && mwo3[i] != 0; i++) sb.Append((char)mwo3[i]);
            return sb.ToString();
        }
    }
}
