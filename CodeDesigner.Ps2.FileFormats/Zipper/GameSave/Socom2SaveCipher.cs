namespace CodeDesigner.Ps2.Save
{
    // =========================================================================
    //  SOCOM II Save File Format (BASCUS-97275SOCOMII)
    //
    //  Each file on the memory card is XOR-encrypted with a two-pass scheme:
    //    Decrypt:
    //      Pass 1 (backward, seed = last byte):  data[i] = data[i] ^ key
    //      Pass 2 (forward,  seed = 0x96):       data[i] = data[i] ^ key
    //      Key update after each byte: key = (key ^ ((key + byte) & 0xFF)) & 0xFF
    //
    //  Decrypted layout (confirmed from live SaveGame0 analysis):
    //    0x000  SaveHeader    (0x60 bytes)
    //    0x060  ModuleNames   (null-terminated strings, variable)
    //    0x0A0  PointerTable  (8-byte pairs of EE RAM ptrs, variable)
    //    0x0FC  Title         (null-terminated ASCII, max ~64 bytes)
    //    0x200  FieldTable    (string table: null-separated field names)
    //    0x480  RecordTable   (16-byte records describing each field)
    //    ...
    //
    //  Verified: encrypt(decrypt(file)) == file  (lossless roundtrip)
    // =========================================================================

    // -------------------------------------------------------------------------
    //  Save cipher
    // -------------------------------------------------------------------------

    /// <summary>
    /// SOCOM II memory card save file XOR cipher.
    ///
    /// The same key-update function drives both passes:
    ///   UpdateKey(key, byte) = (key ^ ((key + byte) &amp; 0xFF)) &amp; 0xFF
    ///
    /// Decrypt:
    ///   Pass 1 backward (seed = last ciphertext byte): plain[i] = cipher[i] ^ key
    ///   Pass 2 forward  (seed = 0x96):                 plain[i] = inter[i]  ^ key
    ///
    /// Encrypt (exact inverse):
    ///   Pass A forward  (seed = 0x96):  inter[i] = plain[i] ^ key  (key driven by plain)
    ///   Pass B backward (seed = inter[n-1]): cipher[i] = inter[i] ^ key (key driven by inter)
    /// </summary>
    public static class Socom2SaveCipher
    {
        private const byte ForwardSeed = 0x96;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static byte UpdateKey(byte key, byte dataByte)
            => (byte)(key ^ ((key + dataByte) & 0xFF));

        /// <summary>
        /// Decrypt a raw memory card save file in-place and return the result.
        /// A new array is always returned — the input is not modified.
        /// </summary>
        public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
        {
            if (ciphertext.IsEmpty)
                return Array.Empty<byte>();

            byte[] buf = ciphertext.ToArray();
            int n = buf.Length;

            // Pass 1: backward, seed = last ciphertext byte
            byte key = buf[n - 1];
            for (int i = n - 2; i >= 0; i--)
            {
                byte nb = (byte)(buf[i] ^ key);
                buf[i] = nb;
                key = UpdateKey(key, nb);
            }

            // Pass 2: forward, seed = 0x96
            key = ForwardSeed;
            for (int i = 0; i < n; i++)
            {
                byte nb = (byte)(buf[i] ^ key);
                buf[i] = nb;
                key = UpdateKey(key, nb);
            }

            return buf;
        }

        /// <summary>
        /// Encrypt plaintext data ready for writing to the memory card.
        /// A new array is always returned — the input is not modified.
        /// </summary>
        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext)
        {
            if (plaintext.IsEmpty)
                return Array.Empty<byte>();

            int n = plaintext.Length;
            byte[] inter = new byte[n];
            byte[] cipher = new byte[n];

            // Inverse of Pass 2: forward, key driven by plaintext value
            byte key = ForwardSeed;
            for (int i = 0; i < n; i++)
            {
                inter[i] = (byte)(plaintext[i] ^ key);
                key = UpdateKey(key, plaintext[i]);   // driven by original plaintext
            }

            // Inverse of Pass 1: backward, seed = inter[n-1], key driven by inter value
            cipher[n - 1] = inter[n - 1];
            key = inter[n - 1];
            for (int i = n - 2; i >= 0; i--)
            {
                cipher[i] = (byte)(inter[i] ^ key);
                key = UpdateKey(key, inter[i]);
            }

            return cipher;
        }

        /// <summary>
        /// Verifies that Encrypt(Decrypt(data)) == data.
        /// Useful for debugging; throws on mismatch.
        /// </summary>
        public static void VerifyRoundtrip(byte[] original)
        {
            byte[] dec = Decrypt(original);
            byte[] reEnc = Encrypt(dec);

            if (reEnc.Length != original.Length)
                throw new InvalidDataException(
                    $"Roundtrip length mismatch: orig={original.Length} re-enc={reEnc.Length}");

            for (int i = 0; i < original.Length; i++)
            {
                if (reEnc[i] != original[i])
                    throw new InvalidDataException(
                        $"Roundtrip byte mismatch at offset 0x{i:X}: " +
                        $"orig=0x{original[i]:X2} re-enc=0x{reEnc[i]:X2}");
            }
        }
    }
}