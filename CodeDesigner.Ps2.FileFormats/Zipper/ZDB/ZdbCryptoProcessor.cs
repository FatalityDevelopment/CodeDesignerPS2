// =============================================================================
//  DEPRECATED MODEL — read before relying on anything in this file.
//
//  This file implements an AES-128-CBC + vtable-transform pipeline and an
//  RSA-2048 "Sony DNAS key in DNAS.BIN" verify. Live PCSX2 tracing (2026-10)
//  disproved that model for retail APACHE00.ZDB:
//    * The block cipher is RC6-32/20/16-CBC, not AES  (see Apache/Rc6.cs).
//    * Integrity is RSA-1024 PKCS#1 signatures from a 30-key table in the EE
//      executable at 0x00560A20, not a single RSA-2048 key in DNAS.BIN.
//    * The 16 "vtable word transforms" decrypt the loader's own JIT stub code,
//      not the ZDB file contents.
//  For read-only decrypt/extract/verify of real ZDB files use
//  Apache/ApacheZdbReader (with Apache/ApacheKeyTable + Apache/Rc6).
//  Retained only for the older research/history it documents.
//  See memory note reference_apache-zdb-format.
// =============================================================================
using CodeDesigner.Ps2.FileFormats.Zipper.Apache;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CodeDesigner.Ps2.FileFormats.Zipper.ZDB
{

    // -------------------------------------------------------------------------
    //  RSA helpers  (Sony public key + signing support)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sony DNAS RSA-2048 public key and signing helpers for ZDB section signatures.
    ///
    /// Signature scheme: RSA X9.31 / PKCS#1 v1.5 with SHA-1.
    /// Confirmed from DNAS_MC.ERX exports: rsasignx931, rsaverifyx931.
    ///
    /// Sony public key extracted from DNAS.BIN @ file offset 0x94920
    /// (SubjectPublicKeyInfo DER, E=65537, N=2048-bit).
    /// </summary>
    public static class ZdbRsa
    {
        // Sony DNAS RSA-2048 public key — extracted from DNAS.BIN @ 0x94920.
        // E = 65537 (0x10001).
        private static readonly byte[] SonyPublicModulus = Convert.FromHexString(
            "a89df0f2babbd4fa095cab1635cec9324b62e06438776b00e6ba50c40fead75" +
            "9b3030dcc9a84ea9e0434e49646cb0bfb0f9e93a6547f50be06f4477b810a1" +
            "aed4d98b31bb3bf7d93d80d6424ba8b866e55622840e5c35deeca8f1d59db2" +
            "042be8ba87c0f154aa99b911dc30c683f03f03b3186aacf355dcecd3cdec3b" +
            "d1c77aa559c1c0194c1da8956edd4681eb7cc9c0fa6a0010d09b18e70f927c" +
            "4ff99c6f13b36d216e8325312be714b41f539dbe35f3ff08d127a048e3abf4" +
            "9132cc93e562067624250ca8ffd94bc7af621b20b521fb6f2fd7274bacc388" +
            "5bb6d28cf960bd0795780c5d8061f335d22271e2b0a4f032830e594ee864d6" +
            "278c57fea61b241");

        private static readonly byte[] SonyPublicExponent = { 0x01, 0x00, 0x01 }; // 65537

        /// <summary>
        /// Verifies a 256-byte RSA-2048 / SHA-1 / PKCS#1 v1.5 signature against
        /// <paramref name="payload"/> using Sony's public key from DNAS.BIN.
        ///
        /// Returns true on success, false on failure.
        /// Throws <see cref="CryptographicException"/> only when called from the pipeline
        /// with <see cref="ZdbPipelineOptions.RsaVerifyThrowOnFailure"/> = true.
        /// </summary>
        public static bool VerifyWithSonyKey(ReadOnlySpan<byte> signature, byte[] payload)
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = SonyPublicModulus,
                Exponent = SonyPublicExponent,
            });
            return rsa.VerifyData(payload, signature.ToArray(),
                HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Signs <paramref name="payload"/> with <paramref name="privateKey"/> using
        /// RSA-2048 / SHA-1 / PKCS#1 v1.5 (closest .NET equivalent to X9.31).
        /// Returns a 256-byte signature ready to write as the ZDB section prefix.
        /// </summary>
        public static byte[] Sign(byte[] payload, RSA privateKey)
        {
            byte[] sig = privateKey.SignData(payload,
                HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);

            // Ensure exactly 256 bytes (RSA-2048 always produces 256, but be safe)
            if (sig.Length == 256) return sig;
            byte[] padded = new byte[256];
            int srcStart = Math.Max(0, sig.Length - 256);
            Buffer.BlockCopy(sig, srcStart, padded, 256 - (sig.Length - srcStart), sig.Length - srcStart);
            return padded;
        }

        /// <summary>
        /// Generates a new RSA-2048 signing keypair for revival server use.
        ///
        /// Workflow:
        ///   1. Call this once and persist both key and public key bytes.
        ///   2. Write the public key into DNAS.BIN @ offset 0x94941 (256 bytes).
        ///   3. Pass the returned RSA to ZdbPipelineOptions.SigningKey.
        ///   4. Distribute the patched DNAS.BIN with your revival server ZDB.
        /// </summary>
        public static RSA GenerateSigningKeyPair()
            => RSA.Create(2048);

        /// <summary>
        /// Exports the public modulus (256 bytes) for writing into DNAS.BIN @ 0x94941.
        /// </summary>
        public static byte[] ExportPublicModulus(RSA rsa)
        {
            RSAParameters p = rsa.ExportParameters(false);
            byte[] n = p.Modulus ?? throw new InvalidOperationException("No modulus.");
            if (n.Length == 256) return n;
            byte[] padded = new byte[256];
            Buffer.BlockCopy(n, 0, padded, 256 - n.Length, n.Length);
            return padded;
        }

        /// <summary>
        /// Patches DNAS.BIN in-memory: replaces Sony's public key N at offset 0x94941
        /// with <paramref name="newPublicModulus"/> (256 bytes).
        /// Write the returned bytes back to disk as your custom DNAS.BIN.
        /// </summary>
        public static byte[] PatchDnasBin(byte[] dnasBinBytes, byte[] newPublicModulus)
        {
            const int NOffset = 0x94941;
            const int NLength = 256;

            if (newPublicModulus.Length != NLength)
                throw new ArgumentException($"Public modulus must be exactly {NLength} bytes.", nameof(newPublicModulus));
            if (dnasBinBytes.Length < NOffset + NLength)
                throw new ArgumentException("DNAS.BIN too short — is this the right file?", nameof(dnasBinBytes));

            byte[] patched = (byte[])dnasBinBytes.Clone();
            Buffer.BlockCopy(newPublicModulus, 0, patched, NOffset, NLength);
            return patched;
        }
    }

    // -------------------------------------------------------------------------
    //  ZDB chunk crypto processor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Low-level helpers for APACHE00.ZDB research.
    ///
    /// Verified:
    ///   - embedded session key / IV extraction
    ///   - transform table primitives (all 16 confirmed)
    ///   - AES-128-CBC chunk decrypt/encrypt (IV resets per 0x80-byte chunk)
    ///   - zlib decompress/compress (78 DA format, wraps MWo3 payload)
    ///   - RSA-2048 signature verify (Sony DNAS key) and sign (custom keypair)
    ///
    /// All pipeline stages are individually togglable via <see cref="ZdbPipelineOptions"/>.
    ///
    /// Uses only BCL types — no NuGet packages required.
    ///   System.Security.Cryptography.Aes      — AES-128-CBC
    ///   System.Security.Cryptography.RSA       — RSA-2048 sign/verify
    ///   System.IO.Compression.ZLibStream       — zlib (requires .NET 6+)
    ///
    /// Quick re-encrypt (revival server — supply your own key/IV):
    /// <code>
    ///   byte[] key = RandomNumberGenerator.GetBytes(16);
    ///   byte[] iv  = RandomNumberGenerator.GetBytes(16);
    ///   byte[] encrypted = ZdbCryptoProcessor.EncryptSection(mwo3Bytes, key, iv,
    ///       i => ZdbDispatch.EncodeFromAddress(destAddr[i]));
    /// </code>
    ///
    /// NOTE — for original Sony ZDB files, the embedded entry+0x300/0x310 bytes are not the
    /// runtime AES key/IV. The working key material is session-derived by IOP secrman during
    /// DNAS flow. DecryptSection works when called with the runtime-captured session key/IV.
    /// </summary>
    public static class ZdbCryptoProcessor
    {
        // ------------------------------------------------------------------
        // Key / IV extraction
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the 16-byte values stored at the "key" (entry+0x300) and "IV" (entry+0x310)
        /// positions inside the named section of a raw APACHE00.ZDB byte array.
        ///
        /// <b>WARNING — these bytes are NOT the runtime AES-128 session key/IV.</b>
        /// They are part of a Sony DNAS-protected session blob. The real working key is derived
        /// by the IOP secrman module from the DNAS context during handshake.
        ///
        /// Confirmed empirically (live PCSX2 EE RAM capture, May 2025):
        ///   ZDB file 0x458/0x468  ≠  EE gMediusSessionKey 0x01E7EF30 / gMediusSessionIV 0x01E7EF40
        ///   ≠  AES_CBC_Core scratch key 0x01E7EA70 / chain 0x01E7EA60
        /// No combination of these values (with or without vtable transforms) decrypts
        /// the ftscore chunk stream to valid zlib data.
        ///
        /// Use this method to inspect raw session-blob material, or for custom re-encryption
        /// pipelines where you provide your own key/IV.
        /// </summary>
        /// <param name="sectionName">Section to read key material from. Defaults to <see cref="ApacheZdb.FTScore"/>.</param>
        public static (byte[] Key, byte[] Iv) ExtractKeyIv(byte[] zdbBytes, string sectionName = ApacheZdb.FTScore)
        {
            if (zdbBytes.Length < ApacheZdbLayout.DirectoryTableOffset + ApacheZdbLayout.DirectoryEntrySize)
                throw new InvalidDataException($"ZDB file too short to locate '{sectionName}' entry.");

            uint entryCount = BitConverter.ToUInt32(zdbBytes, 0x98);
            uint directoryEntryOffset = ApacheZdbLayout.DirectoryTableOffset;
            uint sectionDataOffset = 0;
            bool found = false;

            for (uint i = 0; i < entryCount && directoryEntryOffset + ApacheZdbLayout.DirectoryEntrySize <= zdbBytes.Length; i++)
            {
                uint entrySize = BitConverter.ToUInt32(zdbBytes, (int)directoryEntryOffset);
                string name = string.Empty;
                uint nameStart = directoryEntryOffset + ApacheZdbLayout.DirectoryEntryNameOffset;
                uint nameEnd = directoryEntryOffset + ApacheZdbLayout.DirectoryEntryDataOffsetFieldOffset;
                for (uint j = nameStart; j < nameEnd && zdbBytes[j] != 0; j++)
                    name += (char)zdbBytes[j];

                if (string.Equals(name, sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    sectionDataOffset = BitConverter.ToUInt32(
                        zdbBytes, (int)(directoryEntryOffset + ApacheZdbLayout.DirectoryEntryDataOffsetFieldOffset));
                    found = true;
                    break;
                }
                directoryEntryOffset += entrySize;
            }

            if (!found)
                throw new InvalidDataException(
                    $"Could not find '{sectionName}' section in ZDB — cannot extract key/IV.");

            int keyOffset = (int)sectionDataOffset + ApacheZdbLayout.EmbeddedSessionKeyOffsetInFtScoreEntry;
            int ivOffset  = (int)sectionDataOffset + ApacheZdbLayout.EmbeddedSessionIvOffsetInFtScoreEntry;

            if (zdbBytes.Length < ivOffset + 16)
                throw new InvalidDataException($"ZDB file too short to contain embedded key/IV for '{sectionName}'.");

            byte[] key = new byte[16];
            byte[] iv  = new byte[16];
            Buffer.BlockCopy(zdbBytes, keyOffset, key, 0, 16);
            Buffer.BlockCopy(zdbBytes, ivOffset,  iv,  0, 16);
            return (key, iv);
        }

        // ------------------------------------------------------------------
        // Single chunk  (0x80 bytes)
        // ------------------------------------------------------------------

        /// <summary>
        /// Decrypts one 0x80-byte ZDB chunk.
        ///
        /// Pipeline (decrypt):
        ///   ciphertext
        ///     -> vtable transforms [0..7]   (ZDBBlockProcessor)
        ///     -> vtable transforms [8..F]   (ZDBPostBlockMerge)
        ///     -> AES-128-CBC
        ///     -> plaintext chunk
        ///
        /// IV resets to the session IV for every chunk (confirmed from live capture).
        /// </summary>
        public static byte[] DecryptChunk(
            byte[] cipherChunk,
            byte[] key,
            byte[] iv,
            uint encodedDispatchWord,
            ISet<int>? excludedWordIndices = null,
            ZdbPipelineOptions? options = null)
        {
            options ??= ZdbPipelineOptions.Full;

            byte[] stage = cipherChunk;
            if (options.EnableOuterTransforms)
            {
                stage = ApplyForwardOuterTransforms(stage, encodedDispatchWord, excludedWordIndices, vtableOffset: 0);
                stage = ApplyForwardOuterTransforms(stage, encodedDispatchWord, excludedWordIndices, vtableOffset: 8);
            }
            return AesCbcDecrypt(stage, key, iv);
        }

        /// <summary>
        /// Encrypts one 0x80-byte plaintext chunk.
        ///
        /// Pipeline (encrypt):
        ///   plaintext
        ///     -> AES-128-CBC
        ///     -> inverse vtable transforms [0..7]
        ///     -> inverse vtable transforms [8..F]
        ///     -> ciphertext chunk
        /// </summary>
        public static byte[] EncryptChunk(
            byte[] plainChunk,
            byte[] key,
            byte[] iv,
            uint encodedDispatchWord,
            ISet<int>? excludedWordIndices = null,
            ZdbPipelineOptions? options = null)
        {
            options ??= ZdbPipelineOptions.Full;

            byte[] stage = AesCbcEncrypt(plainChunk, key, iv);
            if (options.EnableOuterTransforms)
            {
                stage = ApplyInverseOuterTransforms(stage, encodedDispatchWord, excludedWordIndices, vtableOffset: 0);
                stage = ApplyInverseOuterTransforms(stage, encodedDispatchWord, excludedWordIndices, vtableOffset: 8);
            }
            return stage;
        }

        // ------------------------------------------------------------------
        // Full section  (many chunks)
        // ------------------------------------------------------------------

        /// <summary>
        /// Decrypts a complete ZDB section payload (multiple 0x80-byte chunks).
        ///
        /// Full pipeline (decrypt):
        ///   ZDB raw bytes
        ///     -> RSA-2048 signature verify   [if options.EnableRsaVerify]
        ///     -> skip 256-byte RSA prefix
        ///     -> per-chunk: vtable transforms -> AES-128-CBC
        ///     -> zlib decompress             [if options.EnableZlib]
        ///     -> MWo3 plaintext
        ///
        /// Debug examples:
        /// <code>
        ///   // Full (default):
        ///   byte[] mwo3 = ZdbCryptoProcessor.DecryptSection(...);
        ///
        ///   // Skip zlib — inspect raw AES output (first bytes should be 0x78 0xDA):
        ///   byte[] zlib = ZdbCryptoProcessor.DecryptSection(..., options: ZdbPipelineOptions.NoZlib);
        ///
        ///   // Skip RSA verify — useful for modified ZDBs before key swap:
        ///   byte[] mwo3 = ZdbCryptoProcessor.DecryptSection(..., options: ZdbPipelineOptions.NoRsa);
        /// </code>
        /// </summary>
        public static byte[] DecryptSection(
            byte[] sectionPayload,
            byte[] key,
            byte[] iv,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null,
            ZdbPipelineOptions? options = null)
        {
            options ??= ZdbPipelineOptions.Full;
            int rsaPrefixSize = ApacheZdbLayout.RsaSignaturePrefixSize;

            if (sectionPayload.Length <= rsaPrefixSize)
                throw new InvalidDataException("Section payload shorter than RSA prefix.");

            // --- Stage 1: RSA signature verify ---
            if (options.EnableRsaVerify)
            {
                ReadOnlySpan<byte> sig = sectionPayload.AsSpan(0, rsaPrefixSize);
                byte[] payload = sectionPayload[rsaPrefixSize..];
                bool valid = ZdbRsa.VerifyWithSonyKey(sig, payload);
                if (!valid)
                {
                    string msg = "RSA-2048 signature verification FAILED. " +
                                 "The section was not signed by Sony's DNAS key. " +
                                 "Use ZdbPipelineOptions.NoRsa to skip (e.g. for custom-signed ZDBs).";
                    if (options.RsaVerifyThrowOnFailure)
                        throw new CryptographicException(msg);
                    // Non-throwing: log and continue
                    System.Diagnostics.Debug.WriteLine($"[ZdbCryptoProcessor] WARNING: {msg}");
                }
            }

            // --- Stage 2: vtable transforms + AES per chunk ---
            int dataStart = rsaPrefixSize;
            int dataLen = sectionPayload.Length - dataStart;
            byte[] aesOutput = ProcessSection(
                sectionPayload, dataStart, dataLen,
                key, iv, getDispatchWord, getExclusionSet,
                encrypt: false, options);

            if (!options.EnableZlib)
                return aesOutput;   // ← DEBUG: raw zlib stream (0x78 0xDA...)

            // --- Stage 3: zlib decompress ---
            ValidateZlibMagic(aesOutput);
            return ZlibDecompress(aesOutput);
        }

        /// <summary>
        /// Encrypts a plaintext MWo3 payload into a ZDB section payload.
        ///
        /// Full pipeline (encrypt):
        ///   MWo3 plaintext
        ///     -> zlib compress               [if options.EnableZlib]
        ///     -> pad to 0x80-byte boundary
        ///     -> per-chunk: AES-128-CBC -> inverse vtable transforms
        ///     -> prepend RSA prefix          (zeroed or signed)
        ///     -> ZDB section bytes
        ///
        /// To sign with a custom keypair:
        /// <code>
        ///   var key = ZdbRsa.GenerateSigningKeyPair();       // once
        ///   File.WriteAllBytes("DNAS_patched.BIN",
        ///       ZdbRsa.PatchDnasBin(dnasBin, ZdbRsa.ExportPublicModulus(key)));
        ///
        ///   var opts = new ZdbPipelineOptions { EnableRsaSign = true, SigningKey = key };
        ///   byte[] section = ZdbCryptoProcessor.EncryptSection(plaintext, ..., options: opts);
        /// </code>
        /// </summary>
        public static byte[] EncryptSection(
            byte[] plaintext,
            byte[] key,
            byte[] iv,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet = null,
            ZdbPipelineOptions? options = null)
        {
            options ??= ZdbPipelineOptions.Full;
            int stride = ApacheZdbLayout.RuntimeCipherChunkSize;
            int rsaPrefixSize = ApacheZdbLayout.RsaSignaturePrefixSize;

            // --- Stage 1: zlib compress ---
            byte[] toEncrypt;
            if (options.EnableZlib)
            {
                toEncrypt = ZlibCompress(plaintext);
            }
            else
            {
                // No zlib — encrypt the plaintext directly (debug / raw mode)
                toEncrypt = plaintext;
            }

            // --- Stage 2: pad to chunk boundary ---
            int pad = (stride - (toEncrypt.Length % stride)) % stride;
            if (pad > 0)
            {
                byte[] padded = new byte[toEncrypt.Length + pad];
                Buffer.BlockCopy(toEncrypt, 0, padded, 0, toEncrypt.Length);
                toEncrypt = padded;
            }

            // --- Stage 3: AES + vtable transforms per chunk ---
            byte[] body = ProcessSection(
                toEncrypt, 0, toEncrypt.Length,
                key, iv, getDispatchWord, getExclusionSet,
                encrypt: true, options);

            // --- Stage 4: RSA prefix ---
            byte[] rsaPrefix = new byte[rsaPrefixSize]; // zeroed by default
            if (options.EnableRsaSign)
            {
                if (options.SigningKey is null)
                    throw new InvalidOperationException(
                        "ZdbPipelineOptions.EnableRsaSign = true but SigningKey is null. " +
                        "Set SigningKey to an RSA-2048 private key, or generate one with " +
                        "ZdbRsa.GenerateSigningKeyPair().");

                // Sign the encrypted body (matches what the game verifies)
                rsaPrefix = ZdbRsa.Sign(body, options.SigningKey);
            }

            using var ms = new MemoryStream(rsaPrefixSize + body.Length);
            ms.Write(rsaPrefix, 0, rsaPrefixSize);
            ms.Write(body, 0, body.Length);
            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        // Private — section chunk loop
        // ------------------------------------------------------------------

        private static byte[] ProcessSection(
            byte[] data,
            int dataStart,
            int dataLen,
            byte[] key,
            byte[] iv,
            Func<int, uint> getDispatchWord,
            Func<int, ISet<int>?>? getExclusionSet,
            bool encrypt,
            ZdbPipelineOptions? options = null)
        {
            options ??= ZdbPipelineOptions.Full;
            int stride = ApacheZdbLayout.RuntimeCipherChunkSize;
            int chunkCount = dataLen / stride;
            int remainder = dataLen % stride;

            using var output = new MemoryStream(dataLen);

            for (int i = 0; i < chunkCount; i++)
            {
                int chunkOffset = dataStart + i * stride;
                byte[] chunk = new byte[stride];
                Buffer.BlockCopy(data, chunkOffset, chunk, 0, stride);

                uint dispatchWord = getDispatchWord(i);
                ISet<int>? excludedIndices = getExclusionSet?.Invoke(i);

                byte[] result = encrypt
                    ? EncryptChunk(chunk, key, iv, dispatchWord, excludedIndices, options)
                    : DecryptChunk(chunk, key, iv, dispatchWord, excludedIndices, options);

                output.Write(result, 0, result.Length);
            }

            // Partial final chunk — pad to 16-byte AES block boundary
            if (remainder > 0)
            {
                int chunkOffset = dataStart + chunkCount * stride;
                int padded = (remainder + 15) / 16 * 16;
                byte[] chunk = new byte[padded];
                Buffer.BlockCopy(data, chunkOffset, chunk, 0, remainder);

                uint dispatchWord = getDispatchWord(chunkCount);
                ISet<int>? excludedIndices = getExclusionSet?.Invoke(chunkCount);

                byte[] result = encrypt
                    ? EncryptChunk(chunk, key, iv, dispatchWord, excludedIndices, options)
                    : DecryptChunk(chunk, key, iv, dispatchWord, excludedIndices, options);

                output.Write(result, 0, encrypt ? result.Length : remainder);
            }

            return output.ToArray();
        }

        // ------------------------------------------------------------------
        // Private — outer transform application
        // ------------------------------------------------------------------

        private static byte[] ApplyForwardOuterTransforms(
            byte[] data, uint dispatchWord, ISet<int>? exclusionSet, int vtableOffset = 0)
        {
            int[] indices = ZdbDispatch.DecodeIndices(dispatchWord);
            byte[] result = (byte[])data.Clone();
            int wcount = result.Length / 4;

            for (int wi = 0; wi < wcount; wi++)
            {
                if (exclusionSet != null && exclusionSet.Contains(wi)) continue;
                uint w = BitConverter.ToUInt32(result, wi * 4);
                foreach (int idx in indices)
                    w = ZdbVtable.ApplyForward(vtableOffset + idx, w);
                WriteWordLE(result, wi * 4, w);
            }
            return result;
        }

        private static byte[] ApplyInverseOuterTransforms(
            byte[] data, uint dispatchWord, ISet<int>? exclusionSet, int vtableOffset = 0)
        {
            int[] indices = ZdbDispatch.DecodeIndices(dispatchWord);
            byte[] result = (byte[])data.Clone();
            int wcount = result.Length / 4;

            for (int wi = 0; wi < wcount; wi++)
            {
                if (exclusionSet != null && exclusionSet.Contains(wi)) continue;
                uint w = BitConverter.ToUInt32(result, wi * 4);
                for (int i = indices.Length - 1; i >= 0; i--)
                    w = ZdbVtable.ApplyInverse(vtableOffset + indices[i], w);
                WriteWordLE(result, wi * 4, w);
            }
            return result;
        }

        private static void WriteWordLE(byte[] buf, int off, uint w)
        {
            buf[off] = (byte)w;
            buf[off + 1] = (byte)(w >> 8);
            buf[off + 2] = (byte)(w >> 16);
            buf[off + 3] = (byte)(w >> 24);
        }

        // ------------------------------------------------------------------
        // Private — AES-128-CBC
        // ------------------------------------------------------------------

        private static byte[] AesCbcDecrypt(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            using var ms = new MemoryStream(data);
            using var cs = new CryptoStream(ms, dec, CryptoStreamMode.Read);
            using var out_ = new MemoryStream();
            cs.CopyTo(out_);
            return out_.ToArray();
        }

        private static byte[] AesCbcEncrypt(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;
            using var enc = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write);
            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        // Private — zlib  (System.IO.Compression.ZLibStream, .NET 6+)
        // ------------------------------------------------------------------

        private static void ValidateZlibMagic(byte[] data)
        {
            if (data.Length < 2 || data[0] != 0x78)
                throw new InvalidDataException(
                    $"Expected zlib magic 0x78 after AES decrypt, got 0x{(data.Length > 0 ? data[0] : 0):X2}. " +
                    "Key/IV or dispatch word may be incorrect. " +
                    "Use ZdbPipelineOptions.NoZlib to skip decompression and inspect the raw stream.");
        }

        /// <summary>
        /// Decompresses a zlib-wrapped deflate stream (0x78 0xDA header).
        /// Uses <see cref="ZLibStream"/> (.NET 6+).
        /// </summary>
        private static byte[] ZlibDecompress(byte[] zlibData)
        {
            using var input = new MemoryStream(zlibData);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>
        /// Compresses data into a zlib-wrapped deflate stream (0x78 0xDA header, optimal level).
        /// Uses <see cref="ZLibStream"/> (.NET 6+).
        /// </summary>
        private static byte[] ZlibCompress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(data, 0, data.Length);
            return output.ToArray();
        }
    }
}