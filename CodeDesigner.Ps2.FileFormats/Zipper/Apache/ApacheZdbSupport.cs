using CodeDesigner.Ps2.FileFormats.Zipper.ZDB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    /// <summary>
    /// Named layout constants for APACHE00.ZDB.
    /// These names are preferred over the older generic offset identifiers.
    /// </summary>
    public static class ApacheZdbLayout
    {
        public const int MainHeaderOffset = ApacheZdbOffsets.MainHeader;
        public const int DirectoryTableOffset = ApacheZdbOffsets.Section0Entry;
        public const int DirectoryEntrySize = 0x5C;

        public const int FtScoreDirectoryEntryOffset = ApacheZdbOffsets.Section0Entry;
        public const int ZSealEtcDirectoryEntryOffset = ApacheZdbOffsets.Section1Entry;

        public const int DirectoryEntryNameOffset = 0x04;
        public const int DirectoryEntryDataOffsetFieldOffset = 0x44;
        public const int DirectoryEntryStoredSizeFieldOffset = 0x48;

        public const int FtScoreEntryFileOffset = ApacheZdbOffsets.FTScorePayload;
        public const int ZSealEtcEntryFileOffset = ApacheZdbOffsets.ZSealEtcPayload;

        public const int FtScoreSessionBlobOffsetInEntry = 0x000;
        public const int FtScoreSessionBlobSize = 0x300;
        public const int EmbeddedSessionKeyOffsetInFtScoreEntry = ApacheZdbOffsets.SessionKeyOffset;
        public const int EmbeddedSessionIvOffsetInFtScoreEntry = ApacheZdbOffsets.SessionIvOffset;

        public const int EmbeddedSessionKeyAbsoluteFileOffset = ApacheZdbOffsets.AesKey;
        public const int EmbeddedSessionIvAbsoluteFileOffset = ApacheZdbOffsets.AesIv;

        public const int RsaSignaturePrefixSize = ApacheZdbOffsets.RsaSigSize;
        public const int RuntimeCipherChunkSize = ApacheZdbOffsets.AesChunkStride;
        public const int VerifiedSecurityBlockSize = 0x50;
    }

    /// <summary>
    /// Named runtime defaults gathered from live SOCOM II analysis.
    /// These are clearer than the older generic "profiles" terminology.
    /// </summary>
    public static class ApacheZdbRuntimeDefaults
    {
        public const uint FtScoreLoadAddress = ApacheZdbProfiles.FTScoreLoadAddress;
        public const uint ZSealEtcLoadAddress = ApacheZdbProfiles.ZSealEtcLoadAddress;

        public static readonly int[] FtScoreExcludedWordIndices = ApacheZdbProfiles.FTScoreWordExclusions;
        public static readonly int[] ZSealEtcExcludedWordIndices = ApacheZdbProfiles.ZSealEtcWordExclusions;
    }

    /// <summary>
    /// Clearer API for the default address-derived dispatch/exclusion providers used by APACHE sections.
    /// </summary>
    public static class ApacheZdbDispatchProfiles
    {
        public static Func<int, uint> CreateLoadAddressDispatchProvider(uint sectionLoadAddress) =>
            ApacheZdbProfiles.CreateAddressDerivedDispatchProvider(sectionLoadAddress);

        public static Func<int, ISet<int>?> CreateFixedExcludedWordProvider(params int[] excludedWordIndices) =>
            ApacheZdbProfiles.CreateFixedExclusionProvider(excludedWordIndices);
    }

    /// <summary>
    /// Clearer API for the verified dispatch-word packing logic used by runtime transforms.
    /// </summary>
    public static class ZdbDispatchWordCodec
    {
        public static int[] DecodeTransformIndices(uint encodedDispatchWord) =>
            ZdbDispatch.DecodeIndices(encodedDispatchWord);

        public static uint EncodeTransformIndices(params int[] transformIndices) =>
            ZdbDispatch.EncodeIndices(transformIndices);

        /// <summary>
        /// Encodes the raw 32-bit runtime dispatch/config value into the compact transform word.
        /// Verified example: 0xBAFA4B78 -> 0x32724370.
        /// </summary>
        public static uint EncodeFromRuntimeWord(uint rawRuntimeValue) =>
            ZdbDispatch.EncodeFromAddress(rawRuntimeValue);

        public static uint EncodeFromDestinationAddress(uint destinationAddress) =>
            EncodeFromRuntimeWord(destinationAddress);
    }

    /// <summary>
    /// Clearer API for the verified transform table behavior.
    /// </summary>
    public static class ZdbTransformTable
    {
        public const uint XorConstant1 = ZdbVtable.XOR1;
        public const uint XorConstant2 = ZdbVtable.XOR2;

        public static uint ApplyDecryptTransform(int transformIndex, uint word) =>
            ZdbVtable.ApplyForward(transformIndex, word);

        public static uint ApplyEncryptTransform(int transformIndex, uint word) =>
            ZdbVtable.ApplyInverse(transformIndex, word);

        public static uint ApplyDecryptSequence(uint word, IReadOnlyList<int> transformIndices)
        {
            uint result = word;
            for (int i = 0; i < transformIndices.Count; i++)
                result = ApplyDecryptTransform(transformIndices[i], result);
            return result;
        }

        public static uint ApplyEncryptSequence(uint word, IReadOnlyList<int> transformIndices)
        {
            uint result = word;
            for (int i = transformIndices.Count - 1; i >= 0; i--)
                result = ApplyEncryptTransform(transformIndices[i], result);
            return result;
        }
    }

    /// <summary>
    /// Verified transform-only crypto for the embedded FTScore security block.
    ///
    /// This is the runtime block decrypted by ZDBSectionDecrypt after FTScore is already in memory.
    /// It is separate from the larger APACHE00.ZDB section encryption path.
    /// </summary>
    public static class ApacheSecurityBlockCrypto
    {
        public static byte[] DecryptBlock(
            byte[] encryptedBlock,
            uint rawRuntimeDispatchWord,
            IEnumerable<int>? excludedWordIndices = null) =>
            DecryptBlockWithEncodedDispatch(
                encryptedBlock,
                ZdbDispatchWordCodec.EncodeFromRuntimeWord(rawRuntimeDispatchWord),
                excludedWordIndices);

        public static byte[] EncryptBlock(
            byte[] plaintextBlock,
            uint rawRuntimeDispatchWord,
            IEnumerable<int>? excludedWordIndices = null) =>
            EncryptBlockWithEncodedDispatch(
                plaintextBlock,
                ZdbDispatchWordCodec.EncodeFromRuntimeWord(rawRuntimeDispatchWord),
                excludedWordIndices);

        public static byte[] DecryptBlockWithEncodedDispatch(
            byte[] encryptedBlock,
            uint encodedDispatchWord,
            IEnumerable<int>? excludedWordIndices = null)
        {
            ArgumentNullException.ThrowIfNull(encryptedBlock);
            return TransformBlock(encryptedBlock, encodedDispatchWord, excludedWordIndices, decrypt: true);
        }

        public static byte[] EncryptBlockWithEncodedDispatch(
            byte[] plaintextBlock,
            uint encodedDispatchWord,
            IEnumerable<int>? excludedWordIndices = null)
        {
            ArgumentNullException.ThrowIfNull(plaintextBlock);
            return TransformBlock(plaintextBlock, encodedDispatchWord, excludedWordIndices, decrypt: false);
        }

        private static byte[] TransformBlock(
            byte[] input,
            uint encodedDispatchWord,
            IEnumerable<int>? excludedWordIndices,
            bool decrypt)
        {
            if ((input.Length % 4) != 0)
                throw new ArgumentException("Security block length must be a multiple of 4 bytes.", nameof(input));

            int[] transformIndices = ZdbDispatchWordCodec.DecodeTransformIndices(encodedDispatchWord);
            HashSet<int>? excludedSet = excludedWordIndices is null
                ? null
                : new HashSet<int>(excludedWordIndices);

            byte[] output = (byte[])input.Clone();
            for (int wordIndex = 0; wordIndex < output.Length / 4; wordIndex++)
            {
                if (excludedSet != null && excludedSet.Contains(wordIndex))
                    continue;

                uint word = BitConverter.ToUInt32(output, wordIndex * 4);
                uint transformed = decrypt
                    ? ZdbTransformTable.ApplyDecryptSequence(word, transformIndices)
                    : ZdbTransformTable.ApplyEncryptSequence(word, transformIndices);

                WriteWordLittleEndian(output, wordIndex * 4, transformed);
            }

            return output;
        }

        private static void WriteWordLittleEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset + 0] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
