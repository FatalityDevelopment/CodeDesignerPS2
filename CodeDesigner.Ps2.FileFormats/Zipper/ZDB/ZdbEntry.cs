namespace CodeDesigner.Ps2.FileFormats.Zipper.ZDB
{
    // =========================================================================
    // Public entry type
    // =========================================================================

    /// <summary>
    /// Represents a single file stored inside a ZDB archive.
    /// </summary>
    public sealed class ZdbEntry
    {
        /// <summary>File name without path (e.g. "texture.bin").</summary>
        public string Name { get; internal set; } = string.Empty;

        /// <summary>Full internal archive path as stored on disk (backslash-separated).</summary>
        public string FullPath { get; internal set; } = string.Empty;

        /// <summary>Raw file data.</summary>
        public byte[] Data { get; internal set; } = Array.Empty<byte>();

        /// <summary>File size in bytes.</summary>
        public uint Size => (uint)Data.Length;

        // Type-1 only
        internal uint VarName { get; set; }
        internal uint DbId { get; set; }
        internal uint Checksum { get; set; }

        // Type-2 only — preserved for lossless round-trip.
        // The 0x40-byte name field contains non-zero garbage after the null terminator
        // in real archives (confirmed in M51.ZDB). The word at entry+0x58 is also
        // non-zero. We store both raw so Save() writes back exactly what was read.
        internal byte[]? RawNameField { get; set; }   // 0x40 bytes verbatim from file
        internal uint TrailingWord { get; set; }   // word at entry+0x58
    }
}
