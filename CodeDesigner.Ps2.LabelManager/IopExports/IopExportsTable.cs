using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace CodeDesigner.Ps2.LabelManager.IopExports;

/// <summary>
/// In-memory lookup of IOP module exports (module name + export index → function name).
/// Backed by the embedded resource <c>Resources/IopModuleExports.json</c>, which is
/// generated from PCSX2's <c>pcsx2/pcsx2/IopModuleNames.cpp</c> plus a small
/// ps2sdk-sourced overlay (see <c>tools/scripts/Build-IopExports.ps1</c>).
/// </summary>
public static class IopExportsTable
{
    private static readonly Dictionary<string, IReadOnlyDictionary<int, string>> _byModule
        = Load();

    private static Dictionary<string, IReadOnlyDictionary<int, string>> Load()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<int, string>>(
            StringComparer.OrdinalIgnoreCase);

        var asm  = typeof(IopExportsTable).Assembly;
        var name = asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith("IopModuleExports.json",
                                                       StringComparison.OrdinalIgnoreCase));
        if (name is null) return result;

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("modules", out var modulesEl)
            || modulesEl.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var modProp in modulesEl.EnumerateObject())
        {
            var exports = new Dictionary<int, string>();
            foreach (var entry in modProp.Value.EnumerateObject())
            {
                if (int.TryParse(entry.Name, out var idx)
                    && entry.Value.ValueKind == JsonValueKind.String)
                {
                    exports[idx] = entry.Value.GetString()!;
                }
            }
            if (exports.Count > 0)
                result[modProp.Name] = exports;
        }

        return result;
    }

    /// <summary>Names of every module the table has entries for.</summary>
    public static IEnumerable<string> ModuleNames => _byModule.Keys;

    /// <summary>True when the table contains any exports for <paramref name="moduleName"/>.</summary>
    public static bool HasModule(string moduleName) => _byModule.ContainsKey(moduleName);

    /// <summary>Returns the function name for <paramref name="index"/>, or null when unknown.</summary>
    public static string? Resolve(string moduleName, int index) =>
        _byModule.TryGetValue(moduleName, out var m) && m.TryGetValue(index, out var n) ? n : null;

    /// <summary>Returns the full export table for a module, or null when unknown.</summary>
    public static IReadOnlyDictionary<int, string>? GetModule(string moduleName) =>
        _byModule.TryGetValue(moduleName, out var m) ? m : null;
}
