using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    /// <summary>
    /// The APACHE00.ZDB verification key table, as it lives inside the SOCOM II main
    /// executable / EE RAM. Thirty RSA-1024 public keys (e = 65537), each DES-CBC wrapped
    /// with its own key and IV so the table is self-describing — nothing secret is embedded
    /// in this repository; the moduli are recovered from the user's own game image.
    ///
    /// Layout (EE RAM, r0004): entry <c>i</c> at <c>0x00560A20 + i*0xC8</c>:
    ///   [DES key 8][DES IV 8][DES-CBC ciphertext 0xB8 → 8-byte header + DER SubjectPublicKeyInfo]
    ///
    /// This type is READ-ONLY infrastructure: it recovers public moduli so the on-disc
    /// format can be decrypted and its signatures verified. It does not sign or pack.
    /// </summary>
    public sealed class ApacheKeyTable
    {
        /// <summary>Default EE address of entry 0's DES key (r0004).</summary>
        public const uint DefaultTableEeAddress = 0x00560A20;
        public const int EntryCount = 30;
        public const int EntryStride = 0xC8;
        private const int WrappedSize = 0xB8;

        private readonly BigInteger[] _moduli;   // index 0..29
        private static readonly BigInteger Exponent = 65537;

        private ApacheKeyTable(BigInteger[] moduli) => _moduli = moduli;

        /// <summary>Number of keys recovered.</summary>
        public int Count => _moduli.Length;

        /// <summary>
        /// Recovers the key table from a block of game memory/executable bytes.
        /// </summary>
        /// <param name="image">EE RAM dump or main-executable bytes that contain the table.</param>
        /// <param name="tableOffset">Offset within <paramref name="image"/> of entry 0's DES key.</param>
        public static ApacheKeyTable FromImage(byte[] image, int tableOffset)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            int need = tableOffset + EntryCount * EntryStride;
            if (image.Length < need)
                throw new ArgumentException($"Image too small for key table (need 0x{need:X}).", nameof(image));

            var moduli = new BigInteger[EntryCount];
            using var des = DES.Create();
            des.Mode = CipherMode.CBC;
            des.Padding = PaddingMode.None;

            for (int i = 0; i < EntryCount; i++)
            {
                int baseOff = tableOffset + i * EntryStride;
                byte[] key = image[baseOff..(baseOff + 8)];
                byte[] iv = image[(baseOff + 8)..(baseOff + 16)];
                byte[] ct = image[(baseOff + 16)..(baseOff + 16 + WrappedSize)];
                des.Key = key;
                des.IV = iv;
                byte[] pt = des.CreateDecryptor().TransformFinalBlock(ct, 0, ct.Length);
                byte[] modulus = ExtractModulus(pt)
                    ?? throw new InvalidDataException($"Key-table entry {i} did not unwrap to a valid RSA SubjectPublicKeyInfo.");
                moduli[i] = new BigInteger(modulus, isUnsigned: true, isBigEndian: true);
            }
            return new ApacheKeyTable(moduli);
        }

        /// <summary>Builds a table directly from already-recovered 128-byte big-endian moduli (index order).</summary>
        public static ApacheKeyTable FromModuli(IReadOnlyList<byte[]> moduliBigEndian)
        {
            if (moduliBigEndian is null) throw new ArgumentNullException(nameof(moduliBigEndian));
            var m = new BigInteger[moduliBigEndian.Count];
            for (int i = 0; i < m.Length; i++)
                m[i] = new BigInteger(moduliBigEndian[i], isUnsigned: true, isBigEndian: true);
            return new ApacheKeyTable(m);
        }

        /// <summary>
        /// Applies the RSA public operation to a 128-byte block and strips PKCS#1 type-1 padding.
        /// Returns the recovered message body (without the trailing 16-char hex "magic").
        /// </summary>
        public (byte[] Body, string Magic) RecoverSignedBlock(int keyIndex, ReadOnlySpan<byte> block128)
        {
            if (block128.Length != 128) throw new ArgumentException("RSA block must be 128 bytes.", nameof(block128));
            var c = new BigInteger(block128, isUnsigned: true, isBigEndian: true);
            BigInteger n = _moduli[keyIndex];
            if (c >= n) throw new InvalidDataException("RSA ciphertext >= modulus.");
            byte[] m = ToFixedBigEndian(BigInteger.ModPow(c, Exponent, n), 128);
            if (m[0] != 0x00 || m[1] != 0x01)
                throw new InvalidDataException("Bad PKCS#1 type-1 padding.");
            int sep = Array.IndexOf(m, (byte)0x00, 2);
            if (sep < 0) throw new InvalidDataException("No PKCS#1 separator.");
            byte[] payload = m[(sep + 1)..];
            if (payload.Length < 16) throw new InvalidDataException("Recovered payload too short.");
            byte[] body = payload[..^16];
            string magic = System.Text.Encoding.ASCII.GetString(payload[^16..]);
            return (body, magic);
        }

        private static byte[]? ExtractModulus(byte[] spkiPlusHeader)
        {
            // SubjectPublicKeyInfo for RSA-1024 with e=65537 contains the sequence tag
            // "02 81 81 00" immediately before the 128-byte modulus.
            byte[] tag = { 0x02, 0x81, 0x81, 0x00 };
            int idx = IndexOf(spkiPlusHeader, tag);
            if (idx < 0 || idx + 4 + 128 > spkiPlusHeader.Length) return null;
            return spkiPlusHeader[(idx + 4)..(idx + 4 + 128)];
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++) if (haystack[i + j] != needle[j]) break;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static byte[] ToFixedBigEndian(BigInteger v, int len)
        {
            byte[] raw = v.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (raw.Length == len) return raw;
            var o = new byte[len];
            Buffer.BlockCopy(raw, 0, o, len - raw.Length, raw.Length);
            return o;
        }
    }
}
