using System.Text;

namespace CodeDesigner.Ps2.Save
{
    // -------------------------------------------------------------------------
    //  Data record (16-byte entries at 0x480+)
    // -------------------------------------------------------------------------

    /// <summary>
    /// One 16-byte entry from the save record table (starts at 0x480).
    /// Each record maps a field name (via str_offset into the string table at 0x20C)
    /// to its value location and type.
    ///
    /// Layout (confirmed from SaveGame0 — all 48 records verified):
    ///   [0x00] byte   type        — 0x04=int, 0x03=float, 0x01=bool
    ///   [0x01] byte   flags       — 0x04=table lookup, 0x00=direct
    ///   [0x02] ushort count       — 1 for scalars; >1 for arrays (e.g. 42='state', 26=career stats)
    ///   [0x04] uint   val_offset  — byte offset of value within the value block
    ///   [0x08] uint   table_offset
    ///   [0x0C] uint   str_offset  — byte offset into string table at 0x20C
    /// </summary>
    public sealed class Socom2DataRecord
    {
        public byte Type { get; set; }
        public byte Flags { get; set; }
        /// <summary>Element count — 1 for scalar fields, >1 for arrays.</summary>
        public ushort Count { get; set; }
        public uint ValOffset { get; set; }
        public uint TableOffset { get; set; }
        public uint StrOffset { get; set; }

        /// <summary>Field name resolved from the string table, or empty if out of range.</summary>
        public string FieldName { get; set; } = string.Empty;

        public const int RecordSize = 16;

        public static Socom2DataRecord Parse(ReadOnlySpan<byte> dec, int recordOffset, int strTableBase)
        {
            static uint LE32(ReadOnlySpan<byte> d, int o) =>
                (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
            static ushort LE16(ReadOnlySpan<byte> d, int o) =>
                (ushort)(d[o] | (d[o + 1] << 8));

            var r = new Socom2DataRecord
            {
                Type = dec[recordOffset],
                Flags = dec[recordOffset + 1],
                Count = LE16(dec, recordOffset + 2),
                ValOffset = LE32(dec, recordOffset + 4),
                TableOffset = LE32(dec, recordOffset + 8),
                StrOffset = LE32(dec, recordOffset + 12),
            };

            int nameOff = strTableBase + (int)r.StrOffset;
            if (nameOff >= 0 && nameOff < dec.Length)
                r.FieldName = ReadCStr(dec, nameOff);

            return r;
        }

        private static string ReadCStr(ReadOnlySpan<byte> dec, int offset)
        {
            int end = offset;
            while (end < dec.Length && dec[end] != 0) end++;
            return Encoding.Latin1.GetString(dec.Slice(offset, end - offset));
        }
    }
}