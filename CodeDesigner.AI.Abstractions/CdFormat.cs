using System.Text;
using System.Text.Json;

namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Rich JSON-response formatters ported from the legacy server's Formatters.cs.
/// Tools forward a bridge command and pass the raw JSON to one of these helpers
/// to get a compact, human-readable rendering with a consistent rules header.
/// </summary>
public static class CdFormat
{
    public const string RulesHeader =
        "📋 [Rules: call cd_session_init · label findings · prefer summaries · save progress]";

    public static string WithRules(string body) =>
        string.IsNullOrEmpty(body) ? body : $"{RulesHeader}\n\n{body}";

    // ── Labels ────────────────────────────────────────────────────────────
    public static string Labels(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Labels", out var arr))
                return CdText.Message(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr  = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string label = item.TryGetProperty("label",   out var l) ? (l.GetString() ?? "?") : "?";
                string? cmt  = item.TryGetProperty("comment", out var c) ? c.GetString()           : null;
                sb.Append($"  {addr,-12}  {label}");
                if (!string.IsNullOrEmpty(cmt)) sb.Append($"  ; {cmt}");
                sb.AppendLine();
            }
            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no labels)");
        }
        catch { return json; }
    }

    public static string LabelsFiltered(string json, Func<string, bool> labelPredicate)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Labels", out var arr))
                return CdText.Message(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string label = item.TryGetProperty("label", out var l) ? (l.GetString() ?? "") : "";
                if (!labelPredicate(label)) continue;
                string addr = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string? cmt = item.TryGetProperty("comment", out var c) ? c.GetString() : null;
                sb.Append($"  {addr,-12}  {label}");
                if (!string.IsNullOrEmpty(cmt)) sb.Append($"  ; {cmt}");
                sb.AppendLine();
            }
            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no matching labels)");
        }
        catch { return json; }
    }

    // ── Threads ───────────────────────────────────────────────────────────
    public static string Threads(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Threads", out var arr))
                return CdText.Message(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== EE Threads ===");
            foreach (var item in arr.EnumerateArray())
            {
                int id = item.TryGetProperty("id", out var idP) ? idP.GetInt32() : -1;
                string status = item.TryGetProperty("status", out var st) ? (st.GetString() ?? "?") : "?";
                uint pc = item.TryGetProperty("pc", out var p) ? p.GetUInt32() : 0u;
                uint sp = item.TryGetProperty("sp", out var s) ? s.GetUInt32() : 0u;
                int pri = item.TryGetProperty("priority", out var pr) ? pr.GetInt32() : 0;
                uint entry = item.TryGetProperty("entryPc", out var e) ? e.GetUInt32() : 0u;
                sb.AppendLine($"  Thread {id,-3}  [{status,-9}]  PC=0x{pc:X8}  SP=0x{sp:X8}  Pri={pri,-3}  Entry=0x{entry:X8}");
            }
            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no threads)");
        }
        catch { return json; }
    }

    // ── EE registers ──────────────────────────────────────────────────────
    public static string Registers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            var sb = new StringBuilder();
            if (root.TryGetProperty("GPR", out var gpr))
            {
                var names = new[] { "$zero","$at","$v0","$v1","$a0","$a1","$a2","$a3","$t0","$t1","$t2","$t3","$t4","$t5","$t6","$t7","$s0","$s1","$s2","$s3","$s4","$s5","$s6","$s7","$t8","$t9","$k0","$k1","$gp","$sp","$fp","$ra" };
                sb.AppendLine("=== GPRs ===");
                int i = 0;
                foreach (var v in gpr.EnumerateArray())
                {
                    string n = i < names.Length ? names[i] : $"${i}";
                    sb.AppendLine($"  {n,-6} = 0x{v.GetUInt64():X16}");
                    i++;
                }
            }
            if (root.TryGetProperty("GPR_PC", out var pcP))   sb.AppendLine($"\n  PC     = 0x{pcP.GetUInt64():X16}");
            if (root.TryGetProperty("GPR_HI", out var hiP))   sb.AppendLine($"  HI     = 0x{hiP.GetUInt64():X16}");
            if (root.TryGetProperty("GPR_LO", out var loP))   sb.AppendLine($"  LO     = 0x{loP.GetUInt64():X16}");
            if (root.TryGetProperty("COP0_Status", out var s))sb.AppendLine($"\n=== COP0 ===\n  Status = 0x{s.GetUInt32():X8}");
            if (root.TryGetProperty("COP0_Cause", out var c)) sb.AppendLine($"  Cause  = 0x{c.GetUInt32():X8}");
            if (root.TryGetProperty("COP0_EPC", out var e))   sb.AppendLine($"  EPC    = 0x{e.GetUInt64():X16}");
            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : json);
        }
        catch { return json; }
    }

    // ── Disassembly results ───────────────────────────────────────────────
    public static string Disasm(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Results", out var results))
                return CdText.Message(json);

            var sb = new StringBuilder();
            foreach (var item in results.EnumerateArray())
            {
                bool ok = item.TryGetProperty("Success", out var sP) && sP.GetBoolean();
                if (!ok)
                {
                    string errMsg = item.TryGetProperty("Error", out var ep) ? (ep.GetString() ?? "?") : "?";
                    sb.AppendLine($"  [error: {errMsg}]"); continue;
                }
                string hex = item.TryGetProperty("Hex", out var h) ? (h.GetString() ?? "????????") : "????????";
                string asm = item.TryGetProperty("Assembly", out var a) ? (a.GetString() ?? "") : "";
                string? lbl = item.TryGetProperty("Label", out var l) ? l.GetString() : null;
                string? cmt = item.TryGetProperty("Comment", out var c) ? c.GetString() : null;
                if (!string.IsNullOrEmpty(lbl)) sb.AppendLine($"{lbl}:");
                string line = $"  {hex}  {asm,-32}";
                if (!string.IsNullOrEmpty(cmt)) line += $"  ; {cmt}";
                sb.AppendLine(line);
            }
            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no results)");
        }
        catch { return json; }
    }
}
