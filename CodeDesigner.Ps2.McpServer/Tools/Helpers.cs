using System.ComponentModel;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private const string RulesHeader =
        "📋 [Rules: call session_start at the beginning of every session · navigate_to after discoveries · label all findings · snapshot at milestones · update progress report when important]";

    private static string WithRules(string result) =>
        string.IsNullOrEmpty(result) ? result : $"{RulesHeader}\n\n{result}";

    private static string SimpleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (root.TryGetProperty("Message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return WithRules(msg.GetString() ?? json);

            return WithRules(json);
        }
        catch
        {
            return json;
        }
    }

    private static uint? TryParseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        address = address.Trim();
        if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(address[2..],
                    System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                return hexVal;
        }

        if (uint.TryParse(address, out var decVal))
            return decVal;

        return null;
    }

    private static CheatFormat ParseFormat(string name)
    {
        if (!Enum.TryParse<CheatFormat>(name, ignoreCase: true, out var fmt))
            throw new ArgumentException(
                $"Unknown cheat format '{name}'. Valid values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.");
        return fmt;
    }
}
