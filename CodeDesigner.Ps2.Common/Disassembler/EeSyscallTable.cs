using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Common.Disassembler;

/// <summary>
/// In-memory lookup of PS2 R5900 EE BIOS syscall numbers → human-readable
/// names, mined from PCSX2's <c>R5900::bios[256]</c> table at build time
/// (see <c>tools/scripts/Build-EeSyscalls.ps1</c>). Used by the
/// <c>SYSCALL.Analyze</c> handler to annotate <c>syscall</c> instructions
/// with their resolved name.
/// </summary>
public static class EeSyscallTable
{
    private static readonly Dictionary<int, string> _byNumber = Load();

    private static Dictionary<int, string> Load()
    {
        var result = new Dictionary<int, string>();

        var asm  = typeof(EeSyscallTable).Assembly;
        var name = asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith("EeSyscalls.json",
                                                       StringComparison.OrdinalIgnoreCase));
        if (name is null) return result;

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("syscalls", out var syscallsEl)
            || syscallsEl.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in syscallsEl.EnumerateObject())
        {
            if (int.TryParse(prop.Name, out var num)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                result[num] = prop.Value.GetString()!;
            }
        }

        return result;
    }

    /// <summary>Returns the syscall name for <paramref name="number"/>, or null when unknown.</summary>
    public static string? Resolve(int number) =>
        _byNumber.TryGetValue(number, out var n) ? n : null;

    /// <summary>True for slots PCSX2 marks as RFU### (reserved-for-future / unused).</summary>
    public static bool IsReserved(int number)
    {
        var n = Resolve(number);
        return n is not null && n.StartsWith("RFU", StringComparison.Ordinal);
    }

    /// <summary>Count of known syscalls (PCSX2 ships 128 entries: 0x00–0x7F).</summary>
    public static int Count => _byNumber.Count;
}
