namespace CodeDesigner.Ps2.Save
{
    // -------------------------------------------------------------------------
    //  Save header (first 0x60 bytes of decrypted file)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Decrypted save file header fields.
    /// Offsets confirmed from live SaveGame0 analysis.
    /// </summary>
    public sealed class Socom2SaveHeader
    {
        /// <summary>[0x00] Always zero in observed files.</summary>
        public uint Unknown0 { get; set; }

        /// <summary>[0x04] Save type: 5 = mission save.</summary>
        public uint SaveType { get; set; }

        /// <summary>[0x08] Save slot / mission index (0x35 = slot 53 in observed file).</summary>
        public uint SlotIndex { get; set; }

        /// <summary>[0x0C] EE RAM pointer (runtime only, ignored on write).</summary>
        public uint RamPtr { get; set; }

        /// <summary>[0x10] Size field (0x10 in observed file).</summary>
        public uint Size { get; set; }

        /// <summary>[0x54] Data size (0x6B0 in observed file).</summary>
        public uint DataSize { get; set; }

        /// <summary>[0x58] Module count.</summary>
        public uint ModuleCount { get; set; }

        /// <summary>[0x5C] Always 0x0C in observed files.</summary>
        public uint Field5C { get; set; }

        /// <summary>[0x60] Always 0x00020002 in observed files.</summary>
        public uint Field60 { get; set; }

        public const int Size_Bytes = 0x60;

        public static Socom2SaveHeader Parse(ReadOnlySpan<byte> dec)
        {
            if (dec.Length < Size_Bytes)
                throw new InvalidDataException("Buffer too short for save header.");

            return new Socom2SaveHeader
            {
                Unknown0 = LE32(dec, 0x00),
                SaveType = LE32(dec, 0x04),
                SlotIndex = LE32(dec, 0x08),
                RamPtr = LE32(dec, 0x0C),
                Size = LE32(dec, 0x10),
                DataSize = LE32(dec, 0x54),
                ModuleCount = LE32(dec, 0x58),
                Field5C = LE32(dec, 0x5C),
                Field60 = LE32(dec, 0x60),
            };
        }

        private static uint LE32(ReadOnlySpan<byte> d, int o) =>
            (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
    }
}