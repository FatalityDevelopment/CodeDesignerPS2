using CodeDesigner.Ps2.Ps2Dis;

// ── Usage ──────────────────────────────────────────────────────────────────
// Extract EE RAM binary and labels CSV from a PS2Dis .pis snapshot:
//   Ps2Dis extract <file.pis> <outdir>
//
// Show .pis file info without extracting:
//   Ps2Dis info <file.pis>
// ───────────────────────────────────────────────────────────────────────────

if (args.Length < 2) { return PrintUsage(); }

return args[0].ToLowerInvariant() switch
{
    "info"    => RunInfo(args[1]),
    "extract" when args.Length >= 3 => RunExtract(args[1], args[2]),
    _         => PrintUsage()
};

// ── Commands ───────────────────────────────────────────────────────────────

static int RunInfo(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

    var pis = PisFile.Load(path);
    Console.WriteLine($"File   : {Path.GetFileName(path)}");
    Console.WriteLine($"Size   : 0x{new FileInfo(path).Length:X} ({new FileInfo(path).Length / 1024 / 1024} MB)");
    Console.WriteLine($"Version: {pis.VersionMajor}.{pis.VersionMinor}");
    Console.WriteLine($"Labels : {pis.LabelCount}");

    if (pis.LabelCount > 0)
    {
        var offsets = pis.LabelOffsets;
        Console.WriteLine($"  First label EE addr: 0x{offsets[0]:X8}");
        Console.WriteLine($"  Last  label EE addr: 0x{offsets[^1]:X8}");
    }

    Console.WriteLine($"EE RAM : 0x{Math.Min(pis.Raw.Length, PisFile.EeRamSize):X} bytes available");
    if (pis.Raw.Length > PisFile.EeRamSize)
        Console.WriteLine($"Extra  : 0x{pis.Raw.Length - PisFile.EeRamSize:X} bytes after EE RAM");

    return 0;
}

static int RunExtract(string path, string outDir)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

    Console.WriteLine($"Loading {Path.GetFileName(path)}...");
    var pis = PisFile.Load(path);

    Directory.CreateDirectory(outDir);

    // EE RAM
    var eePath = Path.Combine(outDir, "ee.bin");
    Console.Write($"Writing EE RAM ({PisFile.EeRamSize / 1024 / 1024} MB)... ");
    File.WriteAllBytes(eePath, pis.ExtractEeRam());
    Console.WriteLine("done");

    // Labels CSV (raw records — caller decodes / ports as needed)
    if (pis.LabelCount > 0)
    {
        var records = pis.GetLabelRecords();
        var csvPath = Path.Combine(outDir, "labels.csv");
        Console.Write($"Writing {records.Count} label records... ");

        var lines = new List<string>(records.Count + 1) { "# eeAddress,rawRecord(hex)" };
        foreach (var r in records)
            lines.Add($"0x{r.EeAddress:X8},{r.RawHex}");

        File.WriteAllLines(csvPath, lines);
        Console.WriteLine("done");
    }
    else
    {
        Console.WriteLine("No labels in this file.");
    }

    Console.WriteLine($"Output: {outDir}");
    return 0;
}

// ── Helpers ────────────────────────────────────────────────────────────────

static int PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Ps2Dis info    <file.pis>");
    Console.WriteLine("  Ps2Dis extract <file.pis> <outdir>");
    return 1;
}

return 0;
