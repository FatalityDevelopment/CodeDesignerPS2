namespace CodeDesigner.Ps2.Compression
{
    // -------------------------------------------------------------------------
    //  Dispatch word codec (ZDBDispatchWordEncoder EE 0x005408D0)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Encodes and decodes ZDB chunk dispatch words.
    /// Format: bits[31:28] = count N, bits[i*4+3:i*4] = vtable index for pass i.
    /// </summary>
    public static class ZdbDispatch
    {
        /// <summary>Decodes a dispatch word into an ordered array of vtable indices.</summary>
        public static int[] DecodeIndices(uint dispatchWord)
        {
            int count = (int)((dispatchWord >> 28) & 0xF);
            var indices = new int[count];
            for (int i = 0; i < count; i++)
                indices[i] = (int)((dispatchWord >> (i * 4)) & 0xF);
            return indices;
        }

        /// <summary>Encodes an ordered array of vtable indices into a dispatch word.</summary>
        public static uint EncodeIndices(int[] indices)
        {
            if (indices.Length > 15)
                throw new ArgumentException("Maximum 15 transform indices per dispatch word.");
            uint word = (uint)indices.Length << 28;
            for (int i = 0; i < indices.Length; i++)
                word |= ((uint)indices[i] & 0xF) << (i * 4);
            return word;
        }

        /// <summary>
        /// Address-derived dispatch encoding (default for most chunks).
        /// Uses the destination EE address nibbles to produce 3-bit packed indices (0–7 only).
        /// Full 4-bit indices (8–F) only appear in stream-table overrides.
        /// </summary>
        public static uint EncodeFromAddress(uint destAddr)
        {
            uint val = destAddr & 0xFFFFFFFF;
            uint top4 = (val >> 28) & 0xF;
            uint low3 = top4 & 7;
            int count = (low3 == 0) ? 5 : (int)(top4 - (top4 & 8));
            uint packed = 0;
            for (int i = 0; i < 7; i++)
                packed |= ((val >> (i * 4)) & 7u) << (i * 4);
            return ((uint)(count & 0xF) << 28) | (packed & 0x0FFFFFFFu);
        }
    }
}
