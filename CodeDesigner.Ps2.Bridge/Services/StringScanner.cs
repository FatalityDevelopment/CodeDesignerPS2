using System.Collections.Generic;
using System.Text;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Shared null-terminated ASCII string scanner. Used by both the
    /// dedicated SCANSTRINGS / GETSTRINGS command handlers and by the
    /// disassembler render path for per-page string detection.
    /// </summary>
    internal static class StringScanner
    {
        public const int MinStringLength = 4;

        /// <summary>
        /// True when <paramref name="b"/> can appear inside a printable
        /// null-terminated string. Includes the standard printable ASCII range
        /// (0x20–0x7E) plus common whitespace control chars (\t, \n, \r) that
        /// regularly appear in PS2 firmware diagnostic strings — without these,
        /// a single embedded newline in a string like "invalid type\n" would
        /// abort the scan and leave the rest of the string formatted as MIPS
        /// instruction garbage.
        /// </summary>
        public static bool IsAsciiStringChar(byte b) =>
            (b >= 0x20 && b <= 0x7E) || b == 0x09 || b == 0x0A || b == 0x0D;

        public readonly struct Hit
        {
            public Hit(uint address, int byteOffset, int length, string value)
            {
                Address = address;
                ByteOffset = byteOffset;
                Length = length;
                Value = value;
            }

            public uint Address { get; }
            public int ByteOffset { get; }
            public int Length { get; }
            public string Value { get; }

            // End offset (exclusive) within the source buffer, including the null terminator.
            public int EndByteOffset => ByteOffset + Length + 1;
        }

        /// <summary>
        /// Scan <paramref name="bytes"/> (loaded at virtual base
        /// <paramref name="baseAddress"/>) for null-terminated printable
        /// ASCII strings whose length is at least <paramref name="minLength"/>.
        /// </summary>
        public static List<Hit> FindStrings(byte[] bytes, uint baseAddress, int minLength = MinStringLength)
        {
            var results = new List<Hit>();
            int i = 0;
            while (i < bytes.Length)
            {
                if (IsAsciiStringChar(bytes[i]))
                {
                    int start = i;
                    while (i < bytes.Length && IsAsciiStringChar(bytes[i]))
                        i++;

                    if (i < bytes.Length && bytes[i] == 0x00 && (i - start) >= minLength)
                    {
                        int length = i - start;
                        results.Add(new Hit(
                            address: baseAddress + (uint)start,
                            byteOffset: start,
                            length: length,
                            value: Encoding.ASCII.GetString(bytes, start, length)));
                    }
                    i++;
                }
                else
                {
                    i++;
                }
            }
            return results;
        }
    }
}
