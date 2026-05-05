namespace CodeDesigner.Ps2.Compression
{
    /// <summary>
    /// Practical dispatch/exclusion profiles for APACHE00 section crypto.
    ///
    /// These defaults come from mapped FTScore behavior and let callers decrypt
    /// without manually constructing per-chunk delegates.
    /// </summary>
    public static class ApacheZdbProfiles
    {
        /// <summary>
        /// FTScore output base address in EE RAM (gMWo3FTScoreOutput).
        /// Used for address-derived dispatch words.
        /// </summary>
        public const uint FTScoreLoadAddress = 0x001E7000;

        /// <summary>
        /// Known word exclusions for FTScore chunk processing from mapped stream behavior.
        /// Word indices are 0-based within a 0x80-byte chunk.
        /// </summary>
        public static readonly int[] FTScoreWordExclusions = { 8, 10, 11 };

        /// <summary>
        /// ZSealEtc code load base used by existing live-disasm tooling.
        /// This is used for address-derived dispatch words for zsealetc.
        /// </summary>
        public const uint ZSealEtcLoadAddress = 0x00188938;

        /// <summary>
        /// No confirmed fixed exclusion words for zsealetc yet.
        /// Keep this empty unless stream-table mapping proves otherwise.
        /// </summary>
        public static readonly int[] ZSealEtcWordExclusions = Array.Empty<int>();

        /// <summary>
        /// Builds an encoded dispatch word for a given chunk by using the destination
        /// EE address (loadAddress + chunkIndex * 0x80).
        /// </summary>
        public static uint GetAddressDerivedDispatchWord(uint loadAddress, int chunkIndex)
        {
            if (chunkIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex));

            uint destinationAddress = loadAddress + (uint)(chunkIndex * ApacheZdbLayout.RuntimeCipherChunkSize);
            return ZdbDispatchWordCodec.EncodeFromDestinationAddress(destinationAddress);
        }

        /// <summary>
        /// Creates a per-chunk dispatch provider using address-derived encoding.
        /// </summary>
        public static Func<int, uint> CreateAddressDerivedDispatchProvider(uint loadAddress)
            => chunkIndex => GetAddressDerivedDispatchWord(loadAddress, chunkIndex);

        /// <summary>
        /// Creates a fixed exclusion provider (same exclusion set for every chunk).
        /// A cloned set is returned per call so callers cannot mutate shared state.
        /// </summary>
        public static Func<int, ISet<int>?> CreateFixedExclusionProvider(params int[] excludedWordIndices)
            => _ => new HashSet<int>(excludedWordIndices);
    }
}
