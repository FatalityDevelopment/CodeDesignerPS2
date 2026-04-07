using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;



using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Live emulator memory
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "read_memory")]
    [Description("Read raw bytes from the live PS2 emulator memory and return them as hex. Requires the emulator to be running with the socket server enabled. Address may be supplied in hex (0x prefix) or decimal.")]
    public async Task<string> ReadMemory(
        [Description("Virtual address to read from (hex or decimal).")] string address,
        [Description("Number of bytes to read (1–65536, default 256).")] int length = 256,
        [Description("Memory region: 'ee' for EE main RAM (default) or 'iop' for IOP RAM.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'. Use hex (0x80010000) or decimal.";

        var json = await bridge.SendAsync(
            new { Command = "READMEM", Address = parsedAddress.Value, Length = length, Region = region },
            cancellationToken);

        return FormatReadMemResult(json, parsedAddress.Value);
    }

    [McpServerTool(Name = "write_memory")]
    [Description("Write arbitrary bytes to live PS2 emulator memory. Bytes are supplied as a hex string (e.g. '27BDFFD0'). Requires the emulator to be running with the socket server enabled.")]
    public async Task<string> WriteMemory(
        [Description("Virtual address to write to (hex or decimal).")] string address,
        [Description("Hex-encoded bytes to write, e.g. '27BD FFD0 AFBF 001C' or '0x27,0xBD,0xFF,0xD0'.")] string hexData,
        [Description("Memory region: 'ee' for EE main RAM/scratchpad (default) or 'iop' for IOP RAM.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'. Use hex (0x80010000) or decimal.";

        var json = await bridge.SendAsync(
            new { Command = "WRITEMEM", Address = parsedAddress.Value, Data = hexData, Region = region },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "load_bin")]
    [Description("Load a binary file from disk directly into live emulator memory at a specified address. Useful for injecting custom code or data patches into a running game.")]
    public async Task<string> LoadBin(
        [Description("Absolute path to the binary file to load.")] string filePath,
        [Description("Virtual address to load the binary data to (hex or decimal).")] string address,
        [Description("Memory region: 'ee' for EE main RAM/scratchpad (default) or 'iop' for IOP RAM.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'. Use hex (0x80010000) or decimal.";

        var json = await bridge.SendAsync(
            new { Command = "LOADBIN", InputPath = filePath, Address = parsedAddress.Value, Region = region },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "disassemble_memory")]
    [Description("Read bytes from the live PS2 emulator and disassemble them to MIPS64 EE assembly text. Combines read_memory and disassemble in a single call.")]
    public async Task<string> DisassembleMemory(
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of instructions to disassemble (default 32).")] int count = 32,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'. Use hex (0x80010000) or decimal.";

        // Step 1: read raw bytes
        var memJson = await bridge.SendAsync(
            new { Command = "READMEM", Address = parsedAddress.Value, Length = count * 4, Region = region },
            cancellationToken);

        string? base64Data = null;
        try
        {
            using var doc = JsonDocument.Parse(memJson);
            if (doc.RootElement.TryGetProperty("Data", out var dataEl))
                base64Data = dataEl.GetString();
            if (doc.RootElement.TryGetProperty("Error", out var errEl) &&
                errEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errEl.GetString()))
                return $"Error reading memory: {errEl.GetString()}";
        }
        catch
        {
            return $"Error parsing memory response: {memJson}";
        }

        if (string.IsNullOrEmpty(base64Data))
            return "Error: no data returned from read_memory (emulator may not be running).";

        // Convert base64 → space-separated hex words
        var bytes = Convert.FromBase64String(base64Data);
        var words = new StringBuilder();
        for (int i = 0; i + 3 < bytes.Length; i += 4)
        {
            if (words.Length > 0) words.Append(' ');
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
            words.Append(word.ToString("X8"));
        }

        // Step 2: disassemble
        var disasmJson = await bridge.SendAsync(
            new { Command = "DISASSEMBLE", Data = words.ToString(), Address = parsedAddress.Value },
            cancellationToken);

        return FormatDisasmResults(disasmJson);
    }

    // -------------------------------------------------------------------------
    // Memory region read + search
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "read_memory_region")]
    [Description("Read a contiguous region of EE RAM in one call and optionally search for a byte pattern within it. Returns the region as base64 and any pattern match offsets.")]
    public async Task<string> ReadMemoryRegion(
        [Description("Inclusive start virtual EE address (hex or decimal).")] string startAddress,
        [Description("Exclusive end virtual EE address (hex or decimal). Must be greater than startAddress.")] string endAddress,
        [Description("Optional byte pattern to search for — comma-separated byte values (decimal or 0x-hex), " +
                     "e.g. '0x4D,0x57,0x6F,0x33'. Omit to skip the search.")] string? searchPattern = null,
        [Description("Memory region: 'ee' for EE RAM (default) or 'iop' for IOP RAM.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? start = TryParseAddress(startAddress);
        uint? end   = TryParseAddress(endAddress);
        if (start is null) return $"Error: invalid startAddress '{startAddress}'.";
        if (end   is null) return $"Error: invalid endAddress '{endAddress}'.";

        // Parse the optional search pattern ("0x4D,0x57" or "77,87").
        List<int>? pattern = null;
        if (!string.IsNullOrWhiteSpace(searchPattern))
        {
            pattern = new List<int>();
            foreach (var part in searchPattern.Split(','))
            {
                var trimmed = part.Trim();
                int value;
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out value))
                        return $"Error: invalid byte value '{trimmed}' in search_pattern.";
                }
                else
                {
                    if (!int.TryParse(trimmed, out value))
                        return $"Error: invalid byte value '{trimmed}' in search_pattern.";
                }
                if (value < 0 || value > 255)
                    return $"Error: byte value {value} is out of range (0–255) in search_pattern.";
                pattern.Add(value);
            }
        }

        var json = await bridge.SendAsync(
            new { Command = "READMEMREGION", Address = start.Value, EndAddress = end.Value,
                  SearchPattern = pattern, Region = region },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            var sb = new StringBuilder();
            sb.AppendLine($"Region: 0x{start.Value:X8} – 0x{end.Value:X8} ({end.Value - start.Value:N0} bytes)");

            if (root.TryGetProperty("Data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String)
            {
                var hex = dataProp.GetString() ?? string.Empty;
                // Show first 256 bytes as a formatted hex dump preview.
                int previewBytes = Math.Min(hex.Length / 2, 256);
                sb.AppendLine($"Bytes (first {previewBytes} of {hex.Length / 2} shown): {hex[..(previewBytes * 2)]}");
            }

            if (root.TryGetProperty("MatchAddresses", out var matchesProp) &&
                matchesProp.ValueKind == JsonValueKind.Array)
            {
                int count = matchesProp.GetArrayLength();
                sb.AppendLine($"Pattern matches ({count}):");
                foreach (var m in matchesProp.EnumerateArray())
                    sb.AppendLine($"  0x{m.GetUInt32():X8}");
                if (count == 0)
                    sb.AppendLine("  (none)");
            }
            else if (pattern != null)
            {
                sb.AppendLine("Pattern matches: (none)");
            }

            return sb.ToString().TrimEnd();
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "scan_pattern")]
    [Description("Scan live EE RAM (or IOP RAM) for a byte pattern and return all matching addresses. Supports wildcards in the pattern for flexible signature scanning.")]
    public async Task<string> ScanPattern(
        [Description("Byte pattern to search for — comma-separated values (decimal or 0x-hex), e.g. '0x4D,0x57,0x6F,0x33'.")] string searchPattern,
        [Description("Inclusive start address (hex or decimal). Omit or use '0' to start from the beginning of the region.")] string startAddress = "0",
        [Description("Exclusive end address (hex or decimal). Omit or use '0' to scan to the end of the region (32 MB for EE, 2 MB for IOP).")] string endAddress = "0",
        [Description("Memory region: 'ee' for EE main RAM (default) or 'iop' for IOP RAM.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? start = TryParseAddress(startAddress);
        uint? end   = TryParseAddress(endAddress);
        if (start is null) return $"Error: invalid startAddress '{startAddress}'.";
        if (end   is null) return $"Error: invalid endAddress '{endAddress}'.";

        if (string.IsNullOrWhiteSpace(searchPattern))
            return "Error: searchPattern is required.";

        var pattern = new List<int>();
        foreach (var part in searchPattern.Split(','))
        {
            var trimmed = part.Trim();
            int value;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out value))
                    return $"Error: invalid byte value '{trimmed}' in searchPattern.";
            }
            else
            {
                if (!int.TryParse(trimmed, out value))
                    return $"Error: invalid byte value '{trimmed}' in searchPattern.";
            }
            if (value < 0 || value > 255)
                return $"Error: byte value {value} is out of range (0–255) in searchPattern.";
            pattern.Add(value);
        }

        var json = await bridge.SendAsync(
            new { Command = "SCANPATTERN", Address = start.Value, EndAddress = end.Value,
                  SearchPattern = pattern, Region = region },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            var sb = new StringBuilder();
            if (root.TryGetProperty("Message", out var msgProp))
                sb.AppendLine(msgProp.GetString());

            if (root.TryGetProperty("MatchAddresses", out var matchesProp) &&
                matchesProp.ValueKind == JsonValueKind.Array)
            {
                int count = matchesProp.GetArrayLength();
                sb.AppendLine($"Matches ({count}):");
                foreach (var m in matchesProp.EnumerateArray())
                    sb.AppendLine($"  0x{m.GetUInt32():X8}");
                if (count == 0)
                    sb.AppendLine("  (none)");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }
}
