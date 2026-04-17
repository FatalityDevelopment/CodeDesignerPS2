using CodeDesigner.Ps2.Ps2Dis;
using System.Text;

// ── Usage ──────────────────────────────────────────────────────────────────
// Show .pis file info:
//   Ps2Dis info    <file.pis>
//
// Extract EE RAM binary and labels CSV:
//   Ps2Dis extract <file.pis> <outdir>
//
// Write a .map file (pis2map format):
//   Ps2Dis map     <file.pis> <out.map>
// ───────────────────────────────────────────────────────────────────────────

if (args.Length < 2) { return PrintUsage(); }

return args[0].ToLowerInvariant() switch
{
    "info"    => RunInfo(args[1]),
    "extract" when args.Length >= 3 => RunExtract(args[1], args[2]),
    "map"     when args.Length >= 3 => RunMap(args[1], args[2]),
    _         => PrintUsage()
};

// ── Commands ───────────────────────────────────────────────────────────────

static int RunInfo(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

    var pis = PisFile.Load(path);
    Console.WriteLine($"File   : {Path.GetFileName(path)}");
    Console.WriteLine($"Size   : 0x{new FileInfo(path).Length:X} ({new FileInfo(path).Length / 1024 / 1024} MB)");
    Console.WriteLine($"Labels : {pis.LabelCount}");

    if (pis.LabelCount > 0)
    {
        var records = pis.GetLabelRecords();
        if (records.Count > 0)
        {
            Console.WriteLine($"  First label EE addr: 0x{records[0].EeAddress:X8}  ({records[0].Name})");
            Console.WriteLine($"  Last  label EE addr: 0x{records[^1].EeAddress:X8}  ({records[^1].Name})");
        }
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

    // Labels CSV
    if (pis.LabelCount > 0)
    {
        var records = pis.GetLabelRecords();
        var csvPath = Path.Combine(outDir, "labels.csv");
        Console.Write($"Writing {records.Count} label records... ");

        var lines = new List<string>(records.Count + 1) { "# eeAddress,labelName" };
        foreach (var r in records)
            lines.Add($"0x{r.EeAddress:X8},{r.Name}");

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

static int RunMap(string path, string mapPath)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

    Console.WriteLine($"Loading {Path.GetFileName(path)}...");
    var pis     = PisFile.Load(path);
    var records = pis.GetLabelRecords();

    Console.Write($"Writing {records.Count} labels to map file... ");

    var crlf = new byte[] { 0x0D, 0x0A };
    using var fs = File.Open(mapPath, FileMode.Create, FileAccess.Write);

    void WriteLine(string text)
    {
        fs.Write(Encoding.ASCII.GetBytes(text));
        fs.Write(crlf);
    }

    WriteLine("# pis2map v1.0");
    WriteLine("# Created by: pis2map");

    foreach (var r in records)
    {
        WriteLine($"{r.EeAddress:X8}:");
        WriteLine($".code\t{r.Name}");
    }

    Console.WriteLine("done");
    Console.WriteLine($"Output: {mapPath}");
    return 0;
}

// ── Helpers ────────────────────────────────────────────────────────────────

static int PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Ps2Dis info    <file.pis>");
    Console.WriteLine("  Ps2Dis extract <file.pis> <outdir>");
    Console.WriteLine("  Ps2Dis map     <file.pis> <out.map>");
    return 1;
}
