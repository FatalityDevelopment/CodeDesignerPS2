using System.Text;
using System.Text.Json;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    private static string FormatDisasmResults(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Results", out var results))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in results.EnumerateArray())
            {
                bool success = item.TryGetProperty("Success", out var s) && s.GetBoolean();
                if (!success)
                {
                    string errMsg = item.TryGetProperty("Error", out var e) ? (e.GetString() ?? "?") : "?";
                    sb.AppendLine($"  [error: {errMsg}]");
                    continue;
                }

                string hex  = item.TryGetProperty("Hex",      out var h) ? (h.GetString() ?? "????????") : "????????";
                string asm  = item.TryGetProperty("Assembly", out var a) ? (a.GetString() ?? "")         : "";
                string? lbl = item.TryGetProperty("Label",    out var l) ? l.GetString()                 : null;
                string? cmt = item.TryGetProperty("Comment",  out var c) ? c.GetString()                 : null;

                if (!string.IsNullOrEmpty(lbl))
                    sb.AppendLine($"{lbl}:");

                string line = $"  {hex}  {asm,-32}";
                if (!string.IsNullOrEmpty(cmt))
                    line += $"  ; {cmt}";
                sb.AppendLine(line);
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no results)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatAsmResults(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Results", out var results))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in results.EnumerateArray())
            {
                bool success = item.TryGetProperty("Success", out var s) && s.GetBoolean();
                if (!success)
                {
                    string errMsg = item.TryGetProperty("Error", out var e) ? (e.GetString() ?? "?") : "?";
                    sb.AppendLine($"  [error: {errMsg}]");
                    continue;
                }

                string asm = item.TryGetProperty("Assembly", out var a) ? (a.GetString() ?? "") : "";
                string hex = item.TryGetProperty("Hex",      out var h) ? (h.GetString() ?? "") : "";
                sb.AppendLine($"  {asm,-32}  ->  {hex}");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no results)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatCompileCdsResults(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            var sb = new StringBuilder();
            bool hasErrors = false;

            // ── Main instruction results ──────────────────────────────────────────
            if (root.TryGetProperty("Results", out var results))
            {
                foreach (var item in results.EnumerateArray())
                {
                    bool success = item.TryGetProperty("Success", out var s) && s.GetBoolean();

                    // LineNumber is int? — may be JSON null or absent
                    int line = item.TryGetProperty("LineNumber", out var ln) &&
                               ln.ValueKind == JsonValueKind.Number
                               ? ln.GetInt32() : 0;

                    if (!success)
                    {
                        hasErrors = true;
                        string errMsg = item.TryGetProperty("Error",    out var e) ? (e.GetString() ?? "?") : "?";
                        string srcAsm = item.TryGetProperty("Assembly", out var a) ? (a.GetString() ?? "") : "";
                        string prefix = line > 0 ? $"[line {line}] " : "";
                        sb.AppendLine(string.IsNullOrEmpty(srcAsm)
                            ? $"  {prefix}error: {errMsg}"
                            : $"  {prefix}error: {srcAsm}: {errMsg}");
                        continue;
                    }

                    string hex = item.TryGetProperty("Hex",      out var h)  ? (h.GetString()  ?? "") : "";
                    string asm = item.TryGetProperty("Assembly", out var aa) ? (aa.GetString() ?? "") : "";
                    sb.AppendLine(string.IsNullOrEmpty(asm)
                        ? $"  {hex}"
                        : $"  {hex}  ; {asm}");
                }
            }

            if (hasErrors)
                sb.Insert(0, "Compiled with errors:\n");

            // ── Sidecar commands ──────────────────────────────────────────────────
            if (root.TryGetProperty("Sidecars", out var sidecars) &&
                sidecars.ValueKind == JsonValueKind.Array)
            {
                var sidecarSb = new StringBuilder();
                foreach (var sc in sidecars.EnumerateArray())
                {
                    string kind = sc.TryGetProperty("Kind", out var k) ? (k.GetString() ?? "") : "";
                    uint   addr = sc.TryGetProperty("Address", out var a) ? a.GetUInt32() : 0;

                    switch (kind)
                    {
                        case "assert":
                        {
                            bool?   passed = sc.TryGetProperty("AssertPassed", out var p) && p.ValueKind != JsonValueKind.Null
                                             ? p.GetBoolean() : null;
                            uint?   expected = sc.TryGetProperty("ExpectedValue", out var ev) && ev.ValueKind == JsonValueKind.Number
                                               ? ev.GetUInt32() : null;
                            uint?   actual   = sc.TryGetProperty("ActualValue",   out var av) && av.ValueKind == JsonValueKind.Number
                                               ? av.GetUInt32() : null;
                            string  op       = sc.TryGetProperty("Operator", out var op2) ? (op2.GetString() ?? "==") : "==";

                            string passStr = passed.HasValue ? (passed.Value ? "✓ passed" : "✗ FAILED") : "(not checked — no live backend)";
                            string expected_ = expected.HasValue ? $"0x{expected.Value:X8}" : "?";
                            string actual_   = actual.HasValue   ? $"0x{actual.Value:X8}"   : "";
                            string actualPart = actual.HasValue ? $", got {actual_}" : "";
                            sidecarSb.AppendLine($"  assert 0x{addr:X8} {op} {expected_}{actualPart} — {passStr}");
                            break;
                        }
                        case "breakpoint":
                            sidecarSb.AppendLine($"  breakpoint @ 0x{addr:X8}");
                            break;
                        case "watchpoint":
                        {
                            string access = sc.TryGetProperty("AccessType", out var at) ? (at.GetString() ?? "write") : "write";
                            uint   size   = sc.TryGetProperty("WatchSize",   out var ws) && ws.ValueKind == JsonValueKind.Number
                                            ? ws.GetUInt32() : 4;
                            sidecarSb.AppendLine($"  watchpoint {access} @ 0x{addr:X8} ({size} bytes)");
                            break;
                        }
                    }
                }

                if (sidecarSb.Length > 0)
                {
                    sb.AppendLine();
                    sb.Append("Sidecars:\n");
                    sb.Append(sidecarSb);
                }
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no output)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatReadMemResult(string json, uint address)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Data", out var dataProp))
                return SimpleMessage(json);

            var base64 = dataProp.GetString() ?? string.Empty;
            var bytes = Convert.FromBase64String(base64);

            var sb = new StringBuilder();
            sb.AppendLine($"Memory @ 0x{address:X8}  ({bytes.Length} bytes):");
            for (int i = 0; i < bytes.Length; i += 16)
            {
                sb.Append($"  {address + (uint)i:X8}:  ");
                int lineLen = Math.Min(16, bytes.Length - i);
                for (int j = 0; j < lineLen; j++)
                    sb.Append($"{bytes[i + j]:X2} ");
                sb.AppendLine();
            }

            return WithRules(sb.ToString().TrimEnd());
        }
        catch
        {
            return json;
        }
    }

    private static string FormatRegisters(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            var sb = new StringBuilder();

            // GPRs
            if (root.TryGetProperty("GPR", out var gprArr))
            {
                var names = new[]
                {
                    "$zero","$at","$v0","$v1","$a0","$a1","$a2","$a3",
                    "$t0","$t1","$t2","$t3","$t4","$t5","$t6","$t7",
                    "$s0","$s1","$s2","$s3","$s4","$s5","$s6","$s7",
                    "$t8","$t9","$k0","$k1","$gp","$sp","$fp","$ra"
                };
                sb.AppendLine("=== GPRs ===");
                int idx = 0;
                foreach (var v in gprArr.EnumerateArray())
                {
                    string name = idx < names.Length ? names[idx] : $"${idx}";
                    sb.AppendLine($"  {name,-6} = 0x{v.GetUInt64():X16}");
                    idx++;
                }
            }

            // PC
            if (root.TryGetProperty("GPR_PC", out var pcProp))
                sb.AppendLine($"\n  PC     = 0x{pcProp.GetUInt64():X16}");

            // HI/LO
            if (root.TryGetProperty("GPR_HI", out var hiProp))
                sb.AppendLine($"  HI     = 0x{hiProp.GetUInt64():X16}");
            if (root.TryGetProperty("GPR_LO", out var loProp))
                sb.AppendLine($"  LO     = 0x{loProp.GetUInt64():X16}");
            if (root.TryGetProperty("GPR_HI1", out var hi1Prop))
                sb.AppendLine($"  HI1    = 0x{hi1Prop.GetUInt64():X16}");
            if (root.TryGetProperty("GPR_LO1", out var lo1Prop))
                sb.AppendLine($"  LO1    = 0x{lo1Prop.GetUInt64():X16}");
            if (root.TryGetProperty("GPR_SA", out var saProp))
                sb.AppendLine($"  SA     = {saProp.GetInt32()}");

            // COP0 Status / Cause / EPC
            if (root.TryGetProperty("COP0_Status", out var statusProp))
                sb.AppendLine($"\n=== COP0 ===\n  Status = 0x{statusProp.GetUInt32():X8}");
            if (root.TryGetProperty("COP0_Cause",  out var causeProp))
                sb.AppendLine($"  Cause  = 0x{causeProp.GetUInt32():X8}");
            if (root.TryGetProperty("COP0_EPC",    out var epcProp))
                sb.AppendLine($"  EPC    = 0x{epcProp.GetUInt64():X16}");
            if (root.TryGetProperty("COP0_Count",  out var cntProp))
                sb.AppendLine($"  Count  = {cntProp.GetUInt32()}");

            // COP1 FPU summary
            if (root.TryGetProperty("FCR31", out var fcr31Prop))
                sb.AppendLine($"\n=== COP1 FPU ===\n  FCR31  = 0x{fcr31Prop.GetUInt32():X8}");
            if (root.TryGetProperty("FCR0", out var fcr0Prop))
                sb.AppendLine($"  FCR0   = 0x{fcr0Prop.GetUInt32():X8}");

            // VU0 status flags
            if (root.TryGetProperty("VU0_Status", out var vu0stProp))
                sb.AppendLine($"\n=== VU0 ===\n  Status = 0x{vu0stProp.GetUInt32():X8}");
            if (root.TryGetProperty("VU0_MAC", out var vu0macProp))
                sb.AppendLine($"  MACflag= 0x{vu0macProp.GetUInt16():X4}");
            if (root.TryGetProperty("VU0_Clip", out var vu0clipProp))
                sb.AppendLine($"  Clip   = 0x{vu0clipProp.GetUInt32():X8}");

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : json);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatLabels(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Labels", out var labelsArr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in labelsArr.EnumerateArray())
            {
                string addr  = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string label = item.TryGetProperty("label",   out var l) ? (l.GetString() ?? "?") : "?";
                string? cmt  = item.TryGetProperty("comment", out var c) ? c.GetString()           : null;

                sb.Append($"  {addr,-12}  {label}");
                if (!string.IsNullOrEmpty(cmt))
                    sb.Append($"  ; {cmt}");
                sb.AppendLine();
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no labels)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatStrings(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            var key = root.TryGetProperty("Strings", out _) ? "Strings" : "strings";
            if (!root.TryGetProperty(key, out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr  = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string value = item.TryGetProperty("value",   out var v) ? (v.GetString() ?? "")  : "";
                sb.AppendLine($"  {addr}  \"{value}\"");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no strings found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatFunctions(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Functions", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr  = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string label = item.TryGetProperty("label",   out var l) ? (l.GetString() ?? "?") : "?";
                sb.AppendLine($"  {addr}  {label}");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no functions found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatPointers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Pointers", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr     = item.TryGetProperty("address",  out var a) ? (a.GetString() ?? "?") : "?";
                string pointsTo = item.TryGetProperty("pointsTo", out var p) ? (p.GetString() ?? "?") : "?";
                sb.AppendLine($"  {addr}  →  {pointsTo}");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no pointers found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatFloats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Floats", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr  = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                float  value = item.TryGetProperty("value",   out var v) ? v.GetSingle()           : 0f;
                string raw   = item.TryGetProperty("raw",     out var r) ? (r.GetString() ?? "")   : "";
                sb.AppendLine($"  {addr}  {value,14:G6}  ({raw})");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no floats found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatIntegers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Integers", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                int    val  = item.TryGetProperty("value",   out var v) ? v.GetInt32()            : 0;
                string hex  = item.TryGetProperty("hex",     out var h) ? (h.GetString() ?? "")   : "";
                sb.AppendLine($"  {addr}  {val,12}  ({hex})");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no integers found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatSearchResults(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Matches", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                uint addr  = item.TryGetProperty("address", out var a) ? a.GetUInt32() : 0u;
                string fld = item.TryGetProperty("field",   out var f) ? (f.GetString() ?? "") : "";
                int start  = item.TryGetProperty("start",   out var s) ? s.GetInt32()  : 0;
                int len    = item.TryGetProperty("length",  out var l) ? l.GetInt32()  : 0;
                sb.AppendLine($"  0x{addr:X8}  [{fld}] offset={start} len={len}");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no matches)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatThreads(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Threads", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== EE Threads ===");
            foreach (var item in arr.EnumerateArray())
            {
                int id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : -1;
                string status = item.TryGetProperty("status", out var stProp) ? (stProp.GetString() ?? "?") : "?";
                uint pc = item.TryGetProperty("pc", out var pcProp) ? pcProp.GetUInt32() : 0u;
                uint sp = item.TryGetProperty("sp", out var spProp) ? spProp.GetUInt32() : 0u;
                int pri = item.TryGetProperty("priority", out var priProp) ? priProp.GetInt32() : 0;
                uint entry = item.TryGetProperty("entryPc", out var eProp) ? eProp.GetUInt32() : 0u;
                sb.AppendLine($"  Thread {id,-3}  [{status,-9}]  PC=0x{pc:X8}  SP=0x{sp:X8}  Pri={pri,-3}  Entry=0x{entry:X8}");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no threads found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatSnapshot(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Regions", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            string savedPath = root.TryGetProperty("Message", out var msgProp) ? (msgProp.GetString() ?? "") : "";
            sb.AppendLine("Snapshot written" + (string.IsNullOrEmpty(savedPath) ? ":" : $" to: {savedPath}"));
            foreach (var item in arr.EnumerateArray())
            {
                string label = item.TryGetProperty("Label", out var lProp) ? (lProp.GetString() ?? "?") : "?";
                string file  = item.TryGetProperty("File",  out var fProp) ? (fProp.GetString() ?? "?") : "?";
                uint   baseA = item.TryGetProperty("BaseAddress", out var bProp) ? bProp.GetUInt32() : 0u;
                int    size  = item.TryGetProperty("Size",  out var sProp) ? sProp.GetInt32() : 0;
                sb.AppendLine($"  [{label}]  base=0x{baseA:X8}  size={size / 1024} KB  → {file}");
            }

            return sb.Length > 0 ? WithRules(sb.ToString().TrimEnd()) : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatLoadSnapshot(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Regions", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("Snapshot restored (emulator paused):");
            foreach (var item in arr.EnumerateArray())
            {
                string label = item.TryGetProperty("Label", out var lProp) ? (lProp.GetString() ?? "?") : "?";
                string file  = item.TryGetProperty("File",  out var fProp) ? (fProp.GetString() ?? "?") : "?";
                int    size  = item.TryGetProperty("Size",  out var sProp) ? sProp.GetInt32() : 0;
                sb.AppendLine($"  [{label}]  size={size / 1024} KB  ← {file}");
            }

            return sb.Length > 0 ? WithRules(sb.ToString().TrimEnd()) : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatDiscElfs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            string bootElf = root.TryGetProperty("BootElf", out var beProp) ? (beProp.GetString() ?? "") : "";

            if (!root.TryGetProperty("ElfFiles", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(bootElf))
                sb.AppendLine($"BOOT2: {bootElf}");
            sb.AppendLine();

            foreach (var item in arr.EnumerateArray())
            {
                string path    = item.TryGetProperty("Path",      out var p)  ? (p.GetString() ?? "?")  : "?";
                string name    = item.TryGetProperty("Name",      out var n)  ? (n.GetString() ?? "?")  : "?";
                uint   size    = item.TryGetProperty("Size",      out var sz) ? sz.GetUInt32()           : 0u;
                bool   isBoot  = item.TryGetProperty("IsBootElf", out var ib) && ib.GetBoolean();
                string tag     = isBoot ? " [BOOT2]" : "";
                sb.AppendLine($"  {name,-20}  {size / 1024,6} KB  {path}{tag}");
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no ELF/IRX files found)");
        }
        catch
        {
            return json;
        }
    }

    private static string FormatRecompile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            var sb = new StringBuilder();

            string msg = root.TryGetProperty("Message", out var mProp) ? (mProp.GetString() ?? "") : "";
            if (!string.IsNullOrEmpty(msg))
                sb.AppendLine(msg);

            if (root.TryGetProperty("BootElf", out var beProp))
                sb.AppendLine($"ELF: {beProp.GetString()}");

            if (root.TryGetProperty("FunctionCount", out var fcProp))
                sb.AppendLine($"Functions: {fcProp.GetInt32()}");

            if (root.TryGetProperty("WrittenFiles", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine("Written files:");
                foreach (var f in files.EnumerateArray())
                    sb.AppendLine($"  {f.GetString()}");
            }

            return sb.Length > 0 ? WithRules(sb.ToString().TrimEnd()) : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatPortCheats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            var sb = new StringBuilder();

            // Log output (the raw strategy log)
            if (root.TryGetProperty("Data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String)
            {
                string log = dataProp.GetString() ?? "";
                // Only include the summary lines to keep output concise
                foreach (var line in log.Split('\n'))
                {
                    if (line.StartsWith("[PORTCHEATS]") || line.StartsWith("---") ||
                        (line.TrimStart().StartsWith("2") && line.Contains(" ")))
                        sb.AppendLine(line);
                }
            }

            // Per-cheat results
            if (root.TryGetProperty("PortCheatResults", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine();
                sb.AppendLine("=== Ported codes ===");
                string? lastBlock = null;
                foreach (var item in results.EnumerateArray())
                {
                    string block = item.TryGetProperty("BlockName",    out var bn) ? (bn.GetString() ?? "") : "";
                    string orig  = item.TryGetProperty("OriginalCode", out var oc) ? (oc.GetString() ?? "") : "";
                    string? newC = item.TryGetProperty("NewCode",      out var nc) ? nc.GetString()         : null;
                    bool found   = item.TryGetProperty("Found",        out var fn) && fn.GetBoolean();
                    string method= item.TryGetProperty("Method",       out var mt) ? (mt.GetString() ?? "") : "";
                    int conf     = item.TryGetProperty("Confidence",   out var cf) ? cf.GetInt32()          : 0;
                    bool ambig   = item.TryGetProperty("Ambiguous",    out var am) && am.GetBoolean();

                    if (block != lastBlock)
                    {
                        sb.AppendLine($"\n[{(string.IsNullOrEmpty(block) ? "unnamed" : block)}]");
                        lastBlock = block;
                    }

                    if (found && newC != null)
                    {
                        string ambigTag = ambig ? " (AMBIGUOUS)" : "";
                        sb.AppendLine($"  {orig}  →  {newC}  ({conf}% via {method}){ambigTag}");
                    }
                    else
                    {
                        sb.AppendLine($"  {orig}  →  NOT FOUND");
                    }
                }
            }

            return sb.Length > 0 ? WithRules(sb.ToString().TrimEnd()) : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatPineSymbols(string json, string label, string arrayKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            uint total = root.TryGetProperty("TotalSymbols", out var totalProp) ? totalProp.GetUInt32() : 0u;

            if (!root.TryGetProperty(arrayKey, out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine(total > 0 ? $"=== {label} (total: {total}) ===" : $"=== {label} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string addr = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                uint   size = item.TryGetProperty("size",    out var s) ? s.GetUInt32()           : 0u;
                string name = item.TryGetProperty("name",    out var n) ? (n.GetString()   ?? "?") : "?";
                sb.AppendLine($"  {addr}  size={size,6}  {name}");
            }
            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : $"(no {label.ToLowerInvariant()} found)");
        }
        catch
        {
            return json;
        }
    }
}
