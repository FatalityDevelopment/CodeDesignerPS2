using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Turns an <see cref="AiActivityLog"/> row into a Discord embed. Tool arguments
/// and JSON results are rendered as readable "key: value" lines (addresses in
/// hex) rather than raw JSON; free-text results go in a code block.
/// </summary>
public static class AiActivityMessageFormatter
{
    private const int ColorSuccess = 0x57F287;
    private const int ColorFailure = 0xED4245;
    private const int ColorActivity = 0x5865F2;
    private const int ColorSession = 0xFEE75C;
    private const int ColorLabel = 0x1ABC9C;
    private const int ColorPacket = 0xEB459E;
    private const int ColorMessage = 0x99AAB5;

    /// <summary>Most top-level keys listed for an argument or result object.</summary>
    private const int MaxListedKeys = 15;

    /// <summary>Most items previewed from a JSON array.</summary>
    private const int MaxListedItems = 5;

    /// <summary>Keys whose integer values are shown as hex addresses.</summary>
    private static readonly string[] AddressKeyHints =
        { "addr", "address", "pc", "ptr", "pointer", "offset", "start", "end", "base", "target", "location" };

    /// <summary>Tool-name prefix → (emoji, category). First match wins.</summary>
    private static readonly (string Prefix, string Emoji, string Category)[] ToolCategories =
    {
        ("memory_", "🧠", "Memory"),
        ("scan_", "🔎", "Scanner"),
        ("get_floats", "🔎", "Scanner"),
        ("get_integers", "🔎", "Scanner"),
        ("get_pointers", "🔎", "Scanner"),
        ("get_strings", "🔎", "Scanner"),
        ("get_functions", "🔎", "Scanner"),
        ("label_", "🏷️", "Labels"),
        ("group_", "🏷️", "Labels"),
        ("debug_", "🐞", "Debugger"),
        ("pcsx2_", "🎮", "PCSX2"),
        ("pad_", "🎮", "Controller"),
        ("net_", "📡", "Network"),
        ("medius_", "📡", "Network"),
        ("cheat_", "🎯", "Cheats"),
        ("apply_cheats", "🎯", "Cheats"),
        ("convert_cheat", "🎯", "Cheats"),
        ("port_cheats", "🎯", "Cheats"),
        ("detect_cheat_format", "🎯", "Cheats"),
        ("inject_cheat_engine", "🎯", "Cheats"),
        ("remove_cheat_engine", "🎯", "Cheats"),
        ("update_cheat_engine", "🎯", "Cheats"),
        ("disassemble", "⚙️", "Code"),
        ("assemble", "⚙️", "Code"),
        ("compile_cds", "⚙️", "Code"),
        ("decompile_to_cds", "⚙️", "Code"),
        ("validate_cds", "⚙️", "Code"),
        ("explain_cds", "⚙️", "Code"),
        ("apply_cds_patch", "⚙️", "Code"),
        ("recompile_elf", "⚙️", "Code"),
        ("callgraph_", "🕸️", "Call Graph"),
        ("find_code_references", "🕸️", "Call Graph"),
        ("get_xrefs", "🕸️", "Call Graph"),
        ("get_function", "🕸️", "Call Graph"),
        ("rebuild_xrefs", "🕸️", "Call Graph"),
        ("mc_", "💾", "Memory Card"),
        ("hdd_", "💾", "HDD"),
        ("iso_", "💿", "ISO"),
        ("list_disc_elfs", "💿", "ISO"),
        ("file_", "📂", "Files"),
        ("snapshot", "📸", "Snapshots"),
        ("get_snapshots", "📸", "Snapshots"),
        ("load_snapshot", "📸", "Snapshots"),
        ("cd_", "🤖", "Agents"),
        ("compat_", "📋", "Compatibility"),
        ("progress_", "📝", "Progress"),
        ("navigate_to", "📍", "Navigation"),
        ("get_nav_history", "📍", "Navigation"),
        ("clear_nav_history", "📍", "Navigation"),
    };

    public static DiscordEmbed Format(AiActivityLog entry)
    {
        var embed = entry.Kind switch
        {
            "tool" => FormatToolCall(entry),
            "activity" => FormatAddressActivity(entry),
            "session" => new DiscordEmbed
            {
                Title = "🚀 AI session started",
                Description = "An AI agent connected to CodeDesigner and started a new session.",
                Color = ColorSession,
            },
            "label" => FormatLabelUpdate(entry),
            "packet" => FormatPacketCapture(entry),
            "message" => FormatAssistantMessage(entry),
            _ => new DiscordEmbed
            {
                Title = $"🔔 {entry.Name ?? entry.Kind}",
                Description = entry.Detail,
                Color = ColorActivity,
            },
        };

        embed.Timestamp = DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
        embed.Footer ??= new DiscordEmbedFooter { Text = $"CodeDesigner AI · #{entry.Id}" };
        return embed;
    }

    // ── Per-kind formatting ──────────────────────────────────────────────────

    private static DiscordEmbed FormatToolCall(AiActivityLog entry)
    {
        var tool = string.IsNullOrWhiteSpace(entry.Name) ? "unknown_tool" : entry.Name;
        var (emoji, category) = Categorise(tool);
        bool failed = entry.Ok == false;

        var status = failed ? "❌ **Failed**" : "✅ **Succeeded**";
        if (entry.ElapsedMs is int ms)
        {
            status += failed ? $" after {FormatDuration(ms)}" : $" in {FormatDuration(ms)}";
        }

        var description = new StringBuilder(status);
        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            // The AI's stated reason for the call.
            description.Append("\n> 💬 ").Append(EscapeMarkdown(entry.Detail.Replace('\n', ' ')));
        }

        var attachments = new List<DiscordAttachment>();
        description.Append("\n\n**Arguments**\n").Append(FormatArguments(tool, entry.Id, entry.ArgsJson, attachments));

        if (!string.IsNullOrWhiteSpace(entry.Result))
        {
            var output = ToolOutputRenderer.Render(tool, entry.Result, failed);
            description.Append("\n\n").Append(failed ? "**Error**" : "**Result**").Append('\n');

            // The result gets whatever is left of the description, which (at 4096)
            // holds far more than an embed field (1024). If it still doesn't fit,
            // show the head and attach the full output as a file.
            var fileName = $"{tool}-{entry.Id}.{output.FileExtension}";
            var truncatedNote = $"\n*Output truncated — full result attached as `{fileName}`.*";
            int budget = DiscordEmbed.MaxDescription - description.Length;

            if (DiscordText.FitsInCodeBlock(output.Text, output.Language, budget))
            {
                description.Append(DiscordText.CodeBlock(output.Text, output.Language, budget));
            }
            else
            {
                description.Append(DiscordText.CodeBlock(output.Text, output.Language, budget - truncatedNote.Length))
                           .Append(truncatedNote);
                attachments.Add(new DiscordAttachment(fileName, output.Text));
            }
        }

        return new DiscordEmbed
        {
            Title = DiscordText.Truncate($"{emoji} {tool}", DiscordEmbed.MaxTitle),
            Description = description.ToString(),
            Color = failed ? ColorFailure : ColorSuccess,
            Footer = new DiscordEmbedFooter { Text = $"{category} · CodeDesigner AI · #{entry.Id}" },
            Attachments = attachments,
        };
    }

    /// <summary>
    /// The AI's reply text for a turn. It is already Markdown, which Discord
    /// renders natively; a reply longer than one card is attached in full.
    /// </summary>
    private static DiscordEmbed FormatAssistantMessage(AiActivityLog entry)
    {
        var text = (entry.Result ?? string.Empty).Trim();
        var fileName = $"ai-response-{entry.Id}.md";
        var attachments = new List<DiscordAttachment>();

        if (text.Length > DiscordEmbed.MaxDescription)
        {
            var note = $"\n\n*Response truncated — full text attached as `{fileName}`.*";
            text = DiscordText.Truncate(text, DiscordEmbed.MaxDescription - note.Length);

            // Don't leave a code fence open where the text was cut.
            if (CountOccurrences(text, "```") % 2 == 1) { text += "\n```"; }

            text += note;
            attachments.Add(new DiscordAttachment(fileName, entry.Result!));
        }

        return new DiscordEmbed
        {
            Title = "💬 AI response",
            Description = text,
            Color = ColorMessage,
            Attachments = attachments,
        };
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static DiscordEmbed FormatAddressActivity(AiActivityLog entry)
    {
        var command = entry.Name ?? "?";
        var region = (entry.Region ?? "ee").ToUpperInvariant();
        var address = entry.Address is long a ? $"0x{a:X8}" : "?";

        return new DiscordEmbed
        {
            Title = command.Equals("GETPC", StringComparison.OrdinalIgnoreCase)
                ? "📍 Program counter read"
                : "📍 Address activity",
            Description = $"{DiscordText.InlineCode(command)} → {DiscordText.InlineCode(address)} in **{region}** memory",
            Color = ColorActivity,
        };
    }

    private static DiscordEmbed FormatLabelUpdate(AiActivityLog entry)
    {
        var description = DiscordText.InlineCode(entry.Name ?? "?");
        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            description += $" · set **{EscapeMarkdown(entry.Detail)}**";
        }

        return new DiscordEmbed
        {
            Title = "🏷️ Labels updated",
            Description = description,
            Color = ColorLabel,
        };
    }

    private static DiscordEmbed FormatPacketCapture(AiActivityLog entry)
    {
        var description = DiscordText.InlineCode(entry.Name ?? "?");
        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            description += $" · {EscapeMarkdown(entry.Detail)}";
        }

        return new DiscordEmbed
        {
            Title = "📡 Packet capture",
            Description = description,
            Color = ColorPacket,
        };
    }

    // ── Arguments / result rendering ─────────────────────────────────────────

    /// <summary>Most of the description the arguments may use, leaving the rest for the result.</summary>
    private const int ArgumentsBudget = 1500;

    /// <summary>
    /// Simple arguments (a few short scalars) read best as "**Key:** value" lines;
    /// anything structured — nested objects, lists, JSON passed as a string — is
    /// shown as a pretty-printed json block, attached in full if it overflows.
    /// </summary>
    private static string FormatArguments(string tool, long id, string? argsJson, List<DiscordAttachment> attachments)
    {
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson.Trim() == "{}") { return "*none*"; }

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && IsSimpleObject(doc.RootElement))
            {
                return FitLines(ObjectLines(doc.RootElement), DiscordEmbed.MaxFieldValue);
            }
        }
        catch (JsonException)
        {
            // Truncated — pretty-printed leniently below.
        }

        var pretty = ToolOutputRenderer.PrettyPrintJson(argsJson);
        if (DiscordText.FitsInCodeBlock(pretty, "json", ArgumentsBudget))
        {
            return DiscordText.CodeBlock(pretty, "json", ArgumentsBudget);
        }

        var fileName = $"{tool}-{id}-args.json";
        var note = $"\n*Arguments truncated — full arguments attached as `{fileName}`.*";
        attachments.Add(new DiscordAttachment(fileName, pretty));
        return DiscordText.CodeBlock(pretty, "json", ArgumentsBudget - note.Length) + note;
    }

    private static bool IsSimpleObject(JsonElement obj)
    {
        int count = 0;
        foreach (var property in obj.EnumerateObject())
        {
            if (++count > 12) { return false; }
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    return false;
                case JsonValueKind.String:
                    var text = property.Value.GetString() ?? string.Empty;
                    if (text.Length > 100 || text.Contains('\n') || text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                    {
                        return false;
                    }
                    break;
            }
        }
        return true;
    }

    private static List<string> ObjectLines(JsonElement obj)
    {
        var lines = new List<string>();
        int total = 0;
        foreach (var property in obj.EnumerateObject())
        {
            if (IsNoise(property)) { continue; }

            total++;
            if (lines.Count >= MaxListedKeys) { continue; }

            var name = EscapeMarkdown(DiscordText.Humanize(property.Name));
            if (IsObjectArray(property.Value))
            {
                // Result lists (ScanResults, McSaves, …) expand into bullet items
                // under their own heading rather than collapsing to "N items".
                var items = ArrayLines(property.Value);
                items[0] = $"**{name}** — {items[0].Trim('*')}";
                lines.AddRange(items);
            }
            else
            {
                lines.Add($"**{name}:** {FormatValue(property.Name, property.Value)}");
            }
        }

        if (total == 0) { lines.Add("*empty*"); }
        if (total > MaxListedKeys) { lines.Add($"*… +{total - MaxListedKeys} more*"); }
        return lines;
    }

    /// <summary>
    /// Properties that carry no information in a card: nulls (bridge responses
    /// serialise every field), "Success: true" (the card is already green) and
    /// the echoed bridge "Command" name.
    /// </summary>
    private static bool IsNoise(JsonProperty property) =>
        property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || (property.Value.ValueKind == JsonValueKind.True && property.NameEquals("Success"))
        || (property.Value.ValueKind == JsonValueKind.String && property.NameEquals("Command"));

    private static bool IsObjectArray(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
        && value.GetArrayLength() > 0
        && value.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Object);

    private static List<string> ArrayLines(JsonElement array)
    {
        int count = array.GetArrayLength();
        var lines = new List<string> { count == 1 ? "**1 item**" : $"**{count} items**" };

        foreach (var item in array.EnumerateArray().Take(MaxListedItems))
        {
            lines.Add($"• {SummariseItem(item)}");
        }

        if (count > MaxListedItems) { lines.Add($"*… +{count - MaxListedItems} more*"); }
        return lines;
    }

    /// <summary>One-line summary of an array item: an object shows its first few scalar fields.</summary>
    private static string SummariseItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) { return FormatValue(string.Empty, item); }

        // Identifying fields (name, address, …) first, then the rest in order.
        var parts = item.EnumerateObject()
            .Where(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null))
            .Select((p, index) => (Property: p, Rank: SummaryRank(p.Name), Index: index))
            .OrderBy(x => x.Rank).ThenBy(x => x.Index)
            .Select(x => x.Property)
            .Take(4)
            .Select(p => $"{EscapeMarkdown(DiscordText.Humanize(p.Name))} {FormatValue(p.Name, p.Value, maxLength: 40)}")
            .ToList();

        return parts.Count > 0 ? string.Join(" · ", parts) : DiscordText.InlineCode(item.GetRawText());
    }

    private static readonly string[] SummaryKeyPriority = { "name", "title", "address", "value", "type", "size" };

    private static int SummaryRank(string key)
    {
        int index = Array.FindIndex(SummaryKeyPriority, k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? SummaryKeyPriority.Length : index;
    }

    private static string FormatValue(string key, JsonElement value, int maxLength = 80)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                return text.Length == 0 ? "*empty*" : DiscordText.InlineCode(text, maxLength);

            case JsonValueKind.Number:
                if (LooksLikeAddress(key) && value.TryGetInt64(out var number) && number >= 0)
                {
                    return DiscordText.InlineCode($"0x{number:X8}");
                }
                return DiscordText.InlineCode(value.GetRawText());

            case JsonValueKind.True:
                return "`true`";

            case JsonValueKind.False:
                return "`false`";

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return "*null*";

            case JsonValueKind.Array:
                int count = value.GetArrayLength();
                if (count == 0) { return "*empty list*"; }
                if (value.EnumerateArray().All(v => v.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
                {
                    var items = value.EnumerateArray().Take(8).Select(v => FormatValue(key, v, maxLength: 24));
                    var joined = string.Join(", ", items);
                    return count > 8 ? $"{joined} *+{count - 8} more*" : joined;
                }
                return count == 1 ? "*1 item*" : $"*{count} items*";

            case JsonValueKind.Object:
                return DiscordText.InlineCode(value.GetRawText(), maxLength);

            default:
                return DiscordText.InlineCode(value.GetRawText(), maxLength);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Joins lines, dropping trailing ones (with a marker) so the result fits in <paramref name="maxLength"/>.</summary>
    private static string FitLines(List<string> lines, int maxLength)
    {
        const string more = "\n*…*";
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            int needed = (sb.Length > 0 ? 1 : 0) + line.Length;
            if (sb.Length + needed > maxLength - more.Length)
            {
                if (sb.Length == 0) { return DiscordText.Truncate(line, maxLength); }
                sb.Append(more);
                break;
            }

            if (sb.Length > 0) { sb.Append('\n'); }
            sb.Append(line);
        }

        return sb.ToString();
    }

    /// <summary>True when any word of the key ("startAddress" → start, address) is an address hint.</summary>
    private static bool LooksLikeAddress(string key) =>
        DiscordText.Humanize(key)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => AddressKeyHints.Contains(word.ToLowerInvariant()));

    private static (string Emoji, string Category) Categorise(string tool)
    {
        foreach (var (prefix, emoji, category) in ToolCategories)
        {
            if (tool.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return (emoji, category); }
        }
        return ("🔧", "Tools");
    }

    private static string FormatDuration(int ms) =>
        ms < 1000 ? $"{ms} ms" : $"{ms / 1000.0:0.0} s";

    private static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '*' or '_' or '~' or '`' or '|' or '>' or '\\') { sb.Append('\\'); }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
