using System.Text;

namespace CodeDesigner.Ps2.Ps2Dis;

/// <summary>
/// Parses a PS2Dis .pis memory snapshot file.
///
/// File layout:
///   [0x00..0x03]  Magic "PIS\x1F"
///   ...data...
///   [near end]    8 × 0x11 bytes — label section sentinel
///   [+0]          u32  lblSize   – total size of label data (little-endian)
///   [+4]          u32  unknown   – reserved / ignored
///   [+8]          u32  lblCount  – number of labels (little-endian)
///   [+12..]       sequential label records, each:
///                   u32  eeAddress  – EE virtual address (little-endian)
///                   u8   nameLength – length of the label name in bytes
///                   u8[] name       – ASCII label name (nameLength bytes, no NUL)
/// </summary>
public sealed class PisFile
{
    public const int EeRamSize = 0x2000000; // 32 MB

    public static readonly byte[] Magic    = [(byte)'P', (byte)'I', (byte)'S', 0x1F];
    private static readonly byte  Sentinel = 0x11;
    private const           int   SentinelLen = 8;

    // ── Parsed fields ──────────────────────────────────────────────────────

    /// <summary>Number of labels stored in the label section.</summary>
    public uint LabelCount { get; private init; }

    /// <summary>Full raw file bytes.</summary>
    public byte[] Raw { get; private init; } = [];

    /// <summary>File offset of the first label record (after the sentinel + header).</summary>
    private int LabelSectionStart { get; init; }

    // ── Factory ───────────────────────────────────────────────────────────

    public static PisFile Load(string path)
    {
        var raw = File.ReadAllBytes(path);

        if (raw.Length < Magic.Length)
            throw new InvalidDataException("File too small to be a .pis file.");

        for (int i = 0; i < Magic.Length; i++)
            if (raw[i] != Magic[i])
                throw new InvalidDataException($"Bad magic at offset {i}: 0x{raw[i]:X2}");

        // Scan backwards for 8 consecutive 0x11 bytes (label section sentinel).
        int sentinelPos = FindSentinel(raw);
        if (sentinelPos < 0)
            throw new InvalidDataException("Label section sentinel (8×0x11) not found.");

        // Layout after sentinel:
        //   [+0]  lblSize  (u32 LE)  — not needed but we skip it
        //   [+4]  unknown  (u32 LE)  — ignored
        //   [+8]  lblCount (u32 LE)
        //   [+12] label records start
        int s = sentinelPos + SentinelLen;

        if (s + 12 > raw.Length)
            throw new InvalidDataException("File truncated after label sentinel.");

        uint lblCount = ReadUInt32LE(raw, s + 8);

        return new PisFile
        {
            Raw               = raw,
            LabelCount        = lblCount,
            LabelSectionStart = s + 12,
        };
    }

    // ── Extraction ────────────────────────────────────────────────────────

    /// <summary>Returns the 32 MB EE RAM slice (first 32 MB of the file).</summary>
    public byte[] ExtractEeRam()
    {
        int size = Math.Min(EeRamSize, Raw.Length);
        var ee   = new byte[EeRamSize];
        Array.Copy(Raw, 0, ee, 0, size);
        return ee;
    }

    /// <summary>
    /// Parses and returns all label records from the .pis label section.
    /// Each record provides the EE virtual address and the ASCII label name.
    /// </summary>
    public IReadOnlyList<PisLabelRecord> GetLabelRecords()
    {
        var result = new List<PisLabelRecord>((int)LabelCount);
        int s = LabelSectionStart;

        for (int i = 0; i < (int)LabelCount; i++)
        {
            if (s + 5 > Raw.Length) break;          // need at least addr(4) + len(1)

            uint addr    = ReadUInt32LE(Raw, s);
            s += 4;

            int nameLen = Raw[s];
            s++;

            if (s + nameLen > Raw.Length) break;

            string name = Encoding.ASCII.GetString(Raw, s, nameLen);
            s += nameLen;

            result.Add(new PisLabelRecord(addr, name));
        }

        return result;
    }

    /// <summary>
    /// Writes EE RAM and a labels CSV to outDir.
    /// Labels CSV columns: eeAddress(hex), labelName
    /// </summary>
    public void Extract(string outDir)
    {
        Directory.CreateDirectory(outDir);

        File.WriteAllBytes(Path.Combine(outDir, "ee.bin"), ExtractEeRam());

        var records = GetLabelRecords();
        if (records.Count > 0)
        {
            var csv = new StringBuilder();
            csv.AppendLine("# eeAddress,labelName");
            foreach (var r in records)
                csv.AppendLine($"0x{r.EeAddress:X8},{r.Name}");

            File.WriteAllText(Path.Combine(outDir, "labels.csv"), csv.ToString());
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Scans backwards for SentinelLen consecutive 0x11 bytes.
    /// Returns the index of the first byte of the sentinel, or -1 if not found.
    /// </summary>
    private static int FindSentinel(byte[] data)
    {
        for (int i = data.Length - SentinelLen; i >= 0; i--)
        {
            bool match = true;
            for (int j = 0; j < SentinelLen; j++)
            {
                if (data[i + j] != Sentinel) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static uint ReadUInt32LE(byte[] data, int offset) =>
        (uint)(data[offset] |
              (data[offset + 1] << 8) |
              (data[offset + 2] << 16) |
              (data[offset + 3] << 24));
}
