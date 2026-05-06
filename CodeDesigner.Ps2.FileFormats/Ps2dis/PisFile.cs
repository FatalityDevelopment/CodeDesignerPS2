using System.Text;

namespace CodeDesigner.Ps2.FileFormats.Ps2Dis;

/// <summary>
/// Parses a PS2Dis .pis memory snapshot file.
///
/// File layout:
///   [0x00..0x1FFFFFFF]  EE RAM (32 MB). The PIS header overwrites the first ~0x70 bytes.
///   [0x2000000..]       Additional sections (IOP RAM etc.), all 0x11-filled in observed files.
///
/// Header (within EE RAM at file offset 0):
///   0x00  4B  magic "PIS\x1F"
///   0x20  u16 version major (4)
///   0x22  u16 version minor (1)
///   0x24  u32 unknown (always 1)
///   0x28  u32 labelCount
///   0x2C  u32[] label pointer table — groups of 8 uint32s:
///              7 EE-address file offsets (pointing to 0x18-byte label records)
///              followed by 1 separator uint32
///              (last group may have fewer than 7 valid entries)
/// </summary>
public sealed class PisFile
{
    public const int  EeRamSize  = 0x2000000; // 32 MB
    public const int  RecordSize = 0x18;      // bytes per label record

    public static readonly byte[] Magic = [(byte)'P', (byte)'I', (byte)'S', 0x1F];

    // ── Parsed header fields ──────────────────────────────────────────────

    public ushort VersionMajor { get; private init; }
    public ushort VersionMinor { get; private init; }
    public uint   LabelCount   { get; private init; }

    /// <summary>File offsets (= EE virtual addresses) of each label record.</summary>
    public IReadOnlyList<uint> LabelOffsets { get; private init; } = [];

    /// <summary>Full raw file bytes (may exceed 32 MB).</summary>
    public byte[] Raw { get; private init; } = [];

    // ── Factory ───────────────────────────────────────────────────────────

    public static PisFile Load(string path)
    {
        var raw = File.ReadAllBytes(path);

        if (raw.Length < 0x30)
            throw new InvalidDataException("File too small to be a .pis file.");

        for (int i = 0; i < Magic.Length; i++)
            if (raw[i] != Magic[i])
                throw new InvalidDataException($"Bad magic at offset {i}: 0x{raw[i]:X2}");

        ushort major = BitConverter.ToUInt16(raw, 0x20);
        ushort minor = BitConverter.ToUInt16(raw, 0x22);
        uint   count = BitConverter.ToUInt32(raw, 0x28);

        var offsets = ReadLabelOffsets(raw, (int)count);

        return new PisFile
        {
            Raw          = raw,
            VersionMajor = major,
            VersionMinor = minor,
            LabelCount   = count,
            LabelOffsets = offsets,
        };
    }

    // ── Extraction ────────────────────────────────────────────────────────

    /// <summary>Returns the 32 MB EE RAM slice.</summary>
    public byte[] ExtractEeRam()
    {
        int size = Math.Min(EeRamSize, Raw.Length);
        var ee   = new byte[EeRamSize];
        Array.Copy(Raw, 0, ee, 0, size);
        return ee;
    }

    /// <summary>
    /// Returns the raw 0x18-byte record for each label.
    /// Record bytes are read verbatim from EE RAM (PS2Dis internal format).
    /// </summary>
    public IReadOnlyList<PisLabelRecord> GetLabelRecords()
    {
        var result = new List<PisLabelRecord>((int)LabelCount);
        foreach (var off in LabelOffsets)
        {
            if (off + RecordSize > Raw.Length)
                continue;
            var rec = new byte[RecordSize];
            Array.Copy(Raw, (int)off, rec, 0, RecordSize);
            result.Add(new PisLabelRecord(off, rec));
        }
        return result;
    }

    /// <summary>
    /// Writes EE RAM and a labels CSV to outDir.
    /// Labels CSV columns: eeAddress(hex), rawRecord(hex)
    /// </summary>
    public void Extract(string outDir)
    {
        Directory.CreateDirectory(outDir);

        // EE RAM
        var eePath = Path.Combine(outDir, "ee.bin");
        File.WriteAllBytes(eePath, ExtractEeRam());

        // Labels CSV
        var records = GetLabelRecords();
        if (records.Count > 0)
        {
            var csv  = new StringBuilder();
            csv.AppendLine("# eeAddress,rawRecord");
            foreach (var r in records)
                csv.AppendLine($"0x{r.EeAddress:X8},{r.RawHex}");

            var csvPath = Path.Combine(outDir, "labels.csv");
            File.WriteAllText(csvPath, csv.ToString());
        }
    }

    // ── Private ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads up to <paramref name="count"/> label EE-address offsets from the
    /// pointer table at file offset 0x2C.
    ///
    /// The table is laid out in groups of 8 uint32s:
    ///   [7 EE offsets] [1 separator uint32]
    /// Only entries that look like plausible EE addresses are collected.
    /// </summary>
    private static List<uint> ReadLabelOffsets(byte[] raw, int count)
    {
        var offsets    = new List<uint>(count);
        int tableStart = 0x2C;
        int pos        = tableStart;
        int groupSlot  = 0;         // 0..7, resets after 8

        while (offsets.Count < count && pos + 4 <= raw.Length)
        {
            uint v = BitConverter.ToUInt32(raw, pos);
            pos += 4;
            groupSlot++;

            if (groupSlot == 8)
            {
                // 8th slot is a separator — skip it
                groupSlot = 0;
                continue;
            }

            // Accept as label offset if it looks like an EE address
            // (below 32 MB and not zero)
            if (v > 0 && v < (uint)EeRamSize)
                offsets.Add(v);
        }

        return offsets;
    }
}
