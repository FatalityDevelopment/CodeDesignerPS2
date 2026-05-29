using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeDesigner.AI.Abstractions;

/// <summary>Small shared helpers for parsing addresses and shaping bridge JSON responses.</summary>
public static class CdText
{
    /// <summary>Parse a hex (0x-prefixed) or decimal address. Returns null when invalid.</summary>
    public static uint? TryParseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        address = address.Trim();
        if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(address[2..], NumberStyles.HexNumber, null, out var hex))
            return hex;
        return uint.TryParse(address, out var dec) ? dec : null;
    }

    /// <summary>Render bytes as an offset-prefixed hex dump (16 bytes/line).</summary>
    public static string HexDump(byte[] bytes, uint baseAddress)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Memory @ 0x{baseAddress:X8}  ({bytes.Length} bytes):");
        for (int i = 0; i < bytes.Length; i += 16)
        {
            sb.Append($"  {baseAddress + (uint)i:X8}:  ");
            int len = Math.Min(16, bytes.Length - i);
            for (int j = 0; j < len; j++) sb.Append($"{bytes[i + j]:X2} ");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Extract a human-readable message from a bridge JSON response: the Error or
    /// Message field if present, otherwise the raw JSON.
    /// </summary>
    public static string Message(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (root.TryGetProperty("Message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString() ?? json;
            return json;
        }
        catch { return json; }
    }

    /// <summary>Return the base64 "Data" field from a bridge response, or null.</summary>
    public static string? TryGetData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Data", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() : null;
        }
        catch { return null; }
    }

    public static string Error(string message) => $"Error: {message}";
}
