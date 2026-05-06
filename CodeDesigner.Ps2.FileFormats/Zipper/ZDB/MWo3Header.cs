using System.Text;

namespace CodeDesigner.Ps2.FileFormats.Zipper.ZDB
{
    // =========================================================================

    // =========================================================================
    //  SOCOM II — APACHE00.ZDB crypto layer
    //
    //  Research findings (April 2026 live EE capture, SOCOM II BASCUS-97275):
    //
    //  File layout (Type-2 archive, two sections):
    //    0x000–0x09F   ZDB main header (0xFC bytes)
    //    0x0A0–0x0FB   Section-0 directory entry  ("ftscore",  0x5C bytes)
    //    0x0FC–0x157   Section-1 directory entry  ("zsealetc", 0x5C bytes)
    //    0x158–0x457   DNAS session context blob  (0x300 bytes, opaque encrypted)
    //    0x458–0x467   AES-128 KEY (16 bytes, embedded by Sony DNAS server at download time)
    //    0x468–0x477   AES-128 IV  (16 bytes)
    //    0x478+        per-section: RSA-2048 prefix (0x100 bytes) + AES-CBC encrypted payload
    //
    //  Two-layer encryption pipeline (decrypt direction):
    //    ZDB raw bytes
    //      Layer 2: outer vtable transforms (EE 0x00562198, 16 functions)
    //               Per 32-bit word, apply vtable[idx_0..count-1] from dispatch word
    //               Word exclusion list: certain word indices are skipped
    //      Layer 1: AES-128-CBC
    //               Key/IV: session-derived by IOP secrman from DNAS handshake.
    //               File bytes at 0x458/0x468 are DNAS session blob material, NOT the raw key/IV.
    //               No plaintext key appears in the ZDB file — offline decryption requires
    //               reversing the IOP secrman key derivation or using a live-capture route.
    //               IV resets to session IV per 0x80-byte chunk (NOT chained across chunks)
    //      Plaintext: MWo3-wrapped MIPS patch code
    //
    //  AES confirmed call chain:
    //    0x00534848 ZDBSectionParser (self-decrypting stub)
    //      -> 0x0053F8F0 BlockCipherOrchestrator
    //        -> 0x0053FC60 AES_CBC_Core
    //          -> 0x005153F0 AES_CBC_Wrapper
    //            -> 0x00515F00 AES block primitive
    //               0x00531730 CBC chaining
    //
    //  Dispatch word format (ZDBDispatchWordEncoder EE 0x005408D0):
    //    bits[31:28] = transform count N
    //    bits[i*4+3:i*4] = vtable index for pass i  (0-F)
    //  Known dispatch words:
    //    Chunk  0:  0x20332127 -> count=2,  indices=[7, 2]
    //    Chunk 26:  0xA03BA1AF -> count=10, indices=[F,A,1,A,B,3,0,...]
    //    Sec-block: 0xBAFA4B78 -> encoded 0x32724370, count=3, indices=[0,7,3]
    // =========================================================================

    // -------------------------------------------------------------------------
    //  MWo3 header (decrypted payload wrapper)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Header of a decrypted ZDB section payload (MWo3 format).
    /// Confirmed live at EE 0x001E7000 after FTScore.bin decryption.
    ///
    /// <code>
    ///   +0x00  uint  Magic = 0x4D576F33 "MWo3"
    ///   +0x04  uint  SegmentCount (1 for FTScore)
    ///   +0x08  uint  LoadAddress  (0x001E7000 for FTScore)
    ///   +0x0C  uint  TextSize     (0x00220158 for FTScore = 2,228,568 bytes)
    ///   +0x10  uint  DataSize     (0x0002DBB0 for FTScore = 187,312 bytes)
    ///   +0x14  uint  BssSize      (0x00084480 for FTScore = 541,824 bytes)
    ///   +0x18  uint  OverlayCallbacksStart  (0x004315A0)
    ///   +0x1C  uint  OverlayCallbacksEnd    (0x00431798)
    ///   +0x20  char[]  FileName null-terminated, padded to 0x40
    /// </code>
    /// </summary>
    public sealed class MWo3Header
    {
        public const uint ExpectedMagic = 0x4D576F33; // "MWo3"

        public uint Magic { get; init; }
        public uint SegmentCount { get; init; }
        public uint LoadAddress { get; init; }
        public uint TextSize { get; init; }
        public uint DataSize { get; init; }
        public uint BssSize { get; init; }
        public uint OverlayCallbacksStart { get; init; }
        public uint OverlayCallbacksEnd { get; init; }
        public string FileName { get; init; } = string.Empty;

        /// <summary>Total on-disk bytes = TextSize + DataSize = 0x24DD48 for FTScore.</summary>
        public uint TotalSize => TextSize + DataSize;

        public bool IsValid => Magic == ExpectedMagic;

        /// <summary>Parses an MWo3 header from the start of a decrypted payload.</summary>
        /// <exception cref="InvalidDataException"/>
        public static MWo3Header Parse(byte[] data)
        {
            if (data.Length < 0x40)
                throw new InvalidDataException("Buffer too short for MWo3 header.");

            uint magic = BitConverter.ToUInt32(data, 0x00);
            if (magic != ExpectedMagic)
                throw new InvalidDataException(
                    $"Bad MWo3 magic 0x{magic:X8} (expected 0x{ExpectedMagic:X8}).");

            var sb = new StringBuilder();
            for (int i = 0x20; i < Math.Min(data.Length, 0x40); i++)
            {
                if (data[i] == 0) break;
                sb.Append((char)data[i]);
            }

            return new MWo3Header
            {
                Magic = magic,
                SegmentCount = BitConverter.ToUInt32(data, 0x04),
                LoadAddress = BitConverter.ToUInt32(data, 0x08),
                TextSize = BitConverter.ToUInt32(data, 0x0C),
                DataSize = BitConverter.ToUInt32(data, 0x10),
                BssSize = BitConverter.ToUInt32(data, 0x14),
                OverlayCallbacksStart = BitConverter.ToUInt32(data, 0x18),
                OverlayCallbacksEnd = BitConverter.ToUInt32(data, 0x1C),
                FileName = sb.ToString(),
            };
        }

        /// <summary>
        /// Reads the build-version stamp embedded in a decrypted MWo3 payload.
        /// Format: <c>rNNNN</c> (e.g. <c>"r0004"</c>), followed immediately by a space and timestamp.
        /// Returns <c>null</c> if no version stamp is found.
        /// </summary>
        public static string? ReadBuildVersion(byte[] mwo3)
        {
            int idx = FindBuildVersionOffset(mwo3);
            return idx >= 0 ? Encoding.ASCII.GetString(mwo3, idx, 5) : null;
        }

        /// <summary>
        /// Returns a copy of <paramref name="mwo3"/> with the embedded build-version stamp
        /// replaced by <paramref name="newVersion"/> (must be in <c>rNNNN</c> format, e.g. <c>"r0005"</c>).
        /// Throws if the version stamp is not found or the format is invalid.
        /// </summary>
        public static byte[] PatchBuildVersion(byte[] mwo3, string newVersion)
        {
            if (newVersion is not { Length: 5 } || newVersion[0] != 'r' ||
                !int.TryParse(newVersion.AsSpan(1), out _))
                throw new ArgumentException("Version must be in 'rNNNN' format (e.g. 'r0005').", nameof(newVersion));

            int idx = FindBuildVersionOffset(mwo3);
            if (idx < 0)
                throw new InvalidDataException("No build-version stamp (rNNNN<space>) found in MWo3 payload.");

            byte[] result = (byte[])mwo3.Clone();
            byte[] stamp  = Encoding.ASCII.GetBytes(newVersion);
            Buffer.BlockCopy(stamp, 0, result, idx, stamp.Length);
            return result;
        }

        /// <summary>
        /// Returns a copy of <paramref name="mwo3"/> with the embedded build-version number
        /// incremented by one (e.g. <c>r0004</c> → <c>r0005</c>).
        /// Throws if the version stamp is not found.
        /// </summary>
        public static byte[] IncrementBuildVersion(byte[] mwo3)
        {
            string? current = ReadBuildVersion(mwo3)
                ?? throw new InvalidDataException("No build-version stamp (rNNNN<space>) found in MWo3 payload.");
            int number = int.Parse(current.AsSpan(1));
            string next = $"r{number + 1:D4}";
            return PatchBuildVersion(mwo3, next);
        }

        // Scans for the first occurrence of r[0-9]{4}<space> and returns its byte offset, or -1.
        private static int FindBuildVersionOffset(byte[] data)
        {
            int limit = data.Length - 6;
            for (int i = 0; i < limit; i++)
            {
                if (data[i]     == (byte)'r'  &&
                    data[i + 1] >= (byte)'0'  && data[i + 1] <= (byte)'9' &&
                    data[i + 2] >= (byte)'0'  && data[i + 2] <= (byte)'9' &&
                    data[i + 3] >= (byte)'0'  && data[i + 3] <= (byte)'9' &&
                    data[i + 4] >= (byte)'0'  && data[i + 4] <= (byte)'9' &&
                    data[i + 5] == (byte)' ')
                    return i;
            }
            return -1;
        }
    }
}
