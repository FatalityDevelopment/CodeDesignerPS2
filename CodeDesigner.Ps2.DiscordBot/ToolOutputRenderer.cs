using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Prepares a tool's text output for a Discord code block: picks the
/// highlight.js language Discord should colour it with, and pretty-prints JSON
/// (dropping nulls and bridge boilerplate) so it reads as structured data
/// rather than one minified line.
/// </summary>
public static class ToolOutputRenderer
{
    /// <param name="Text">Output ready to place inside a code block.</param>
    /// <param name="Language">Code-block language tag (empty for plain text).</param>
    /// <param name="FileExtension">Extension to use if the output is attached as a file.</param>
    public sealed record RenderedOutput(string Text, string Language, string FileExtension);

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Tools whose output is MIPS disassembly.</summary>
    private static readonly string[] DisassemblyTools =
        { "disassemble", "memory_disasm", "pcsx2_disassemble", "net_disassemble" };

    // "  0x8E140000  lw s4, …" / "003BF8F0  addiu sp, sp, -0x20" / "0x003BF8F0: jal …"
    private static readonly Regex DisasmLine = new(
        @"^\s*(0x)?[0-9A-Fa-f]{8}:?\s+[a-z][a-z0-9.]*(\s|$)", RegexOptions.Compiled);

    // "003BF8F0:  00 00 14 8E …"
    private static readonly Regex HexDumpLine = new(
        @"^\s*(0x)?[0-9A-Fa-f]{6,8}:\s+([0-9A-Fa-f]{2}\s+){3,}", RegexOptions.Compiled);

    // "$a0    = 0x…" / "PC = 0x…" / "Status: Running"
    private static readonly Regex KeyValueLine = new(
        @"^\s*[\w$.\[\]-]+\s*(=|:)\s*\S", RegexOptions.Compiled);

    // A null-valued property, for lenient cleanup of truncated JSON.
    private static readonly Regex NullProperty = new(
        @"""(?:[^""\\]|\\.)*""\s*:\s*null\s*,?", RegexOptions.Compiled);

    private static readonly Regex DanglingComma = new(@",(\s*[}\]])", RegexOptions.Compiled);

    public static RenderedOutput Render(string tool, string output, bool failed)
    {
        var text = output.Trim();

        if (failed)
        {
            // diff highlighting renders "- " lines in red.
            var lines = text.Split('\n').Select(l => "- " + l.TrimEnd('\r'));
            return new RenderedOutput(string.Join('\n', lines), "diff", "txt");
        }

        if (text.StartsWith('{') || text.StartsWith('['))
        {
            return new RenderedOutput(PrettyPrintJson(text), "json", "json");
        }

        if (DisassemblyTools.Contains(tool, StringComparer.OrdinalIgnoreCase) || MostLinesMatch(text, DisasmLine))
        {
            return new RenderedOutput(text, "mipsasm", "asm");
        }

        if (MostLinesMatch(text, HexDumpLine))
        {
            // yaml colours the "address:" prefix and leaves the bytes plain.
            return new RenderedOutput(text, "yaml", "txt");
        }

        if (MostLinesMatch(text, KeyValueLine))
        {
            // Register dumps and "Key=Value" reports.
            return new RenderedOutput(text, "ini", "txt");
        }

        return new RenderedOutput(text, string.Empty, "txt");
    }

    /// <summary>
    /// Indents JSON and removes noise (nulls, "Success": true, the echoed
    /// "Command"). Output cut off by the activity logger isn't valid JSON, so it
    /// falls back to a lenient re-indent that tolerates the missing tail.
    /// </summary>
    public static string PrettyPrintJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                Clean(node, isRoot: true);
                return node.ToJsonString(PrettyJson);
            }
        }
        catch (JsonException)
        {
            // Truncated — fall through.
        }

        var stripped = DanglingComma.Replace(NullProperty.Replace(json, string.Empty), "$1");
        return LenientIndent(DecodeAsciiEscapes(stripped));
    }

    /// <summary>
    /// Removes noise and expands string values that are themselves JSON (tools
    /// such as label_add_batch take their entries as a JSON-encoded string).
    /// </summary>
    private static void Clean(JsonNode node, bool isRoot)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToList())
            {
                bool noise = value is null
                    || (isRoot && key == "Success" && value is JsonValue success && success.TryGetValue<bool>(out var ok) && ok)
                    || (isRoot && key == "Command" && value is JsonValue);

                if (noise) { obj.Remove(key); }
                else if (TryParseEmbeddedJson(value!) is { } expanded) { obj[key] = expanded; Clean(expanded, isRoot: false); }
                else { Clean(value!, isRoot: false); }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not { } child) { continue; }
                if (TryParseEmbeddedJson(child) is { } expanded) { arr[i] = expanded; Clean(expanded, isRoot: false); }
                else { Clean(child, isRoot: false); }
            }
        }
    }

    private static JsonNode? TryParseEmbeddedJson(JsonNode node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) { return null; }

        var trimmed = text.Trim();
        if (trimmed.Length < 2 || !(trimmed.StartsWith('{') || trimmed.StartsWith('['))) { return null; }

        try { return JsonNode.Parse(trimmed); }
        catch (JsonException) { return null; }
    }

    // "\u0022" → "\"", "\u0027" → "'", … — rows logged before the relaxed encoder.
    private static readonly Regex AsciiEscape = new(@"\\u00([0-7][0-9A-Fa-f])", RegexOptions.Compiled);

    private static string DecodeAsciiEscapes(string json) =>
        AsciiEscape.Replace(json, m =>
        {
            char c = (char)Convert.ToInt32(m.Groups[1].Value, 16);
            return c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' => m.Value, // control characters stay escaped
                _ => c.ToString(),
            };
        });

    /// <summary>Re-indents JSON text character by character (string-aware), without requiring it to be complete.</summary>
    private static string LenientIndent(string json)
    {
        var sb = new StringBuilder(json.Length * 2);
        int depth = 0;
        bool inString = false, escaped = false;

        void NewLine() => sb.Append('\n').Append(' ', depth * 2);

        foreach (char c in json)
        {
            if (inString)
            {
                sb.Append(c);
                if (escaped) { escaped = false; }
                else if (c == '\\') { escaped = true; }
                else if (c == '"') { inString = false; }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    break;
                case '{' or '[':
                    sb.Append(c);
                    depth++;
                    NewLine();
                    break;
                case '}' or ']':
                    depth = Math.Max(0, depth - 1);
                    NewLine();
                    sb.Append(c);
                    break;
                case ',':
                    sb.Append(c);
                    NewLine();
                    break;
                case ':':
                    sb.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(c)) { sb.Append(c); }
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool MostLinesMatch(string text, Regex pattern)
    {
        var lines = text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) { return false; }
        int matches = lines.Count(l => pattern.IsMatch(l));
        return matches * 2 > lines.Count;
    }
}
