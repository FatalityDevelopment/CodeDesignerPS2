using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Memory scanning (live)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "scan_strings")]
    [DynamicDescription("scan_strings_desc")]
    [Description("Scan a region of live PS2 emulator memory for null-terminated ASCII strings. Returns strings that meet the minimum length threshold, with their addresses.")]
    public async Task<string> ScanStrings(
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to scan.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SCANSTRINGS", Address = parsedAddress.Value, Length = length, Region = region },
            cancellationToken);

        return FormatStrings(json);
    }

    [McpServerTool(Name = "scan_functions")]
    [DynamicDescription("scan_functions_desc")]
    [Description("Scan a region of live PS2 emulator memory and detect likely MIPS function entry points. Looks for ADDIU $sp, $sp, -N prologues and other calling-convention patterns.")]
    public async Task<string> ScanFunctions(
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to scan.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SCANFUNCTIONS", Address = parsedAddress.Value, Length = length, Region = region },
            cancellationToken);

        return FormatFunctions(json);
    }

    [McpServerTool(Name = "scan_pointers")]
    [DynamicDescription("scan_pointers_desc")]
    [Description("Scan a region of live PS2 emulator memory for 4-byte aligned values that are confirmed EE RAM pointers (in the range 0x00000000–0x01FFFFFF or 0x20000000–0x21FFFFFF).")]
    public async Task<string> ScanPointers(
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to scan.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SCANPOINTERS", Address = parsedAddress.Value, Length = length, Region = region },
            cancellationToken);

        return FormatPointers(json);
    }

    [McpServerTool(Name = "scan_floats")]
    [DynamicDescription("scan_floats_desc")]
    [Description("Scan a region of live PS2 emulator memory for plausible IEEE-754 single-precision floats. Returns addresses and float values that fall within a reasonable numeric range.")]
    public async Task<string> ScanFloats(
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to scan.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SCANFLOATS", Address = parsedAddress.Value, Length = length, Region = region },
            cancellationToken);

        return FormatFloats(json);
    }

    [McpServerTool(Name = "scan_integers")]
    [DynamicDescription("scan_integers_desc")]
    [Description("Scan a region of live PS2 emulator memory for non-zero 4-byte aligned 32-bit integers. Useful for locating counters, health values, and game state variables.")]
    public async Task<string> ScanIntegers(
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to scan.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SCANINTEGERS", Address = parsedAddress.Value, Length = length, Region = region },
            cancellationToken);

        return FormatIntegers(json);
    }

    // -------------------------------------------------------------------------
    // File-binary scanning (no live emulator required)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_strings")]
    [DynamicDescription("get_strings_desc")]
    [Description("Scan base64-encoded binary data for null-terminated printable ASCII strings of at least the specified minimum length. Returns strings and their byte offsets.")]
    public async Task<string> GetStrings(
        [Description("Virtual start address of the data (hex or decimal), used to compute result addresses.")] string address,
        [Description("Base64-encoded binary data to scan (from a READMEM response's Data field).")] string base64Data,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "GETSTRINGS", Address = parsedAddress.Value, Data = base64Data },
            cancellationToken);

        return FormatStrings(json);
    }

    [McpServerTool(Name = "get_functions")]
    [DynamicDescription("get_functions_desc")]
    [Description("Scan base64-encoded binary data for MIPS function entry points. Detects ADDIU $sp, $sp, -N prologues and classic PS2 function patterns.")]
    public async Task<string> GetFunctions(
        [Description("Virtual start address of the data (hex or decimal), used to compute result addresses.")] string address,
        [Description("Base64-encoded binary data to scan (from a READMEM response's Data field).")] string base64Data,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "GETFUNCTIONS", Address = parsedAddress.Value, Data = base64Data },
            cancellationToken);

        return FormatFunctions(json);
    }

    [McpServerTool(Name = "get_pointers")]
    [DynamicDescription("get_pointers_desc")]
    [Description("Scan base64-encoded binary data for 4-byte aligned values that are confirmed EE memory pointers. Returns offsets and pointer values.")]
    public async Task<string> GetPointers(
        [Description("Virtual start address of the data (hex or decimal), used to compute result addresses.")] string address,
        [Description("Base64-encoded binary data to scan (from a READMEM response's Data field).")] string base64Data,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "GETPOINTERS", Address = parsedAddress.Value, Data = base64Data },
            cancellationToken);

        return FormatPointers(json);
    }

    [McpServerTool(Name = "get_floats")]
    [DynamicDescription("get_floats_desc")]
    [Description("Scan base64-encoded binary data for plausible IEEE-754 single-precision floats. Filters out NaN, Inf, subnormals, and values outside a configurable range.")]
    public async Task<string> GetFloats(
        [Description("Virtual start address of the data (hex or decimal), used to compute result addresses.")] string address,
        [Description("Base64-encoded binary data to scan (from a READMEM response's Data field).")] string base64Data,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "GETFLOATS", Address = parsedAddress.Value, Data = base64Data },
            cancellationToken);

        return FormatFloats(json);
    }

    [McpServerTool(Name = "get_integers")]
    [DynamicDescription("get_integers_desc")]
    [Description("Scan base64-encoded binary data for non-zero 4-byte aligned 32-bit integers. Applies an optional value range filter.")]
    public async Task<string> GetIntegers(
        [Description("Virtual start address of the data (hex or decimal), used to compute result addresses.")] string address,
        [Description("Base64-encoded binary data to scan (from a READMEM response's Data field).")] string base64Data,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "GETINTEGERS", Address = parsedAddress.Value, Data = base64Data },
            cancellationToken);

        return FormatIntegers(json);
    }

    // -------------------------------------------------------------------------
    // Code reference search
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "find_code_references")]
    [DynamicDescription("find_code_references_desc")]
    [Description("Find all MIPS instructions in live PS2 EE RAM that reference a specific target address. Covers J/JAL jumps, conditional branches (BEQ, BNE, BLTZ, BGEZ, …), COP1 branches, and LUI+ADDIU/ORI/load/store pointer-load pairs. Each result is enriched with the disassembled instruction text. Useful for locating all callers of a function or all code that accesses a data address.")]
    public async Task<string> FindCodeReferences(
        [Description("Target address to find references to (hex or decimal, e.g. '0x00234F20').")] string targetAddress,
        [Description("Start of the memory range to scan (hex or decimal). Defaults to start of EE RAM (0x00000000).")] string startAddress = "0x00000000",
        [Description("Number of bytes to scan (hex or decimal, e.g. '0x2000000' for all 32 MB of EE RAM).")] string length = "0x2000000",
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? target = TryParseAddress(targetAddress);
        if (target is null)
            return $"Error: invalid targetAddress '{targetAddress}'.";

        uint? start = TryParseAddress(startAddress);
        if (start is null)
            return $"Error: invalid startAddress '{startAddress}'.";

        uint? len = TryParseAddress(length);
        if (len is null)
            return $"Error: invalid length '{length}'.";

        // Step 1: run the instruction-reference search.
        var searchJson = await bridge.SendAsync(
            new
            {
                Command    = "SEARCH",
                Query      = $"0x{target.Value:X8}",
                Address    = start.Value,
                Length     = (int)len.Value,
                SearchMode = "refs",
                Region     = region,
            },
            cancellationToken);

        List<uint> matchAddresses;
        try
        {
            using var doc = JsonDocument.Parse(searchJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error from reference search: {errProp.GetString()}";

            matchAddresses = [];
            if (root.TryGetProperty("Matches", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.TryGetProperty("address", out var addrProp))
                        matchAddresses.Add(addrProp.GetUInt32());
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error parsing reference search response: {ex.Message}";
        }

        if (matchAddresses.Count == 0)
            return WithRules($"No code references found to 0x{target.Value:X8}.");

        // Step 2: for each match, read 4 bytes and disassemble.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Code references to 0x{target.Value:X8} ({matchAddresses.Count} found):");
        sb.AppendLine();

        foreach (var addr in matchAddresses)
        {
            string instrText = "(disassembly unavailable)";
            try
            {
                var memJson = await bridge.SendAsync(
                    new { Command = "READMEM", Address = addr, Length = 4, Region = region },
                    cancellationToken);

                string? base64Data = null;
                using (var memDoc = JsonDocument.Parse(memJson))
                {
                    if (memDoc.RootElement.TryGetProperty("Data", out var dataProp))
                        base64Data = dataProp.GetString();
                }

                if (!string.IsNullOrEmpty(base64Data))
                {
                    var bytes = Convert.FromBase64String(base64Data);
                    if (bytes.Length >= 4)
                    {
                        uint word = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                        var disasmJson = await bridge.SendAsync(
                            new { Command = "DISASSEMBLE", Data = word.ToString("X8"), Address = addr },
                            cancellationToken);

                        using var disasmDoc = JsonDocument.Parse(disasmJson);
                        if (disasmDoc.RootElement.TryGetProperty("Results", out var results))
                        {
                            foreach (var result in results.EnumerateArray())
                            {
                                bool ok = result.TryGetProperty("Success", out var s) && s.GetBoolean();
                                if (ok && result.TryGetProperty("Assembly", out var asmProp))
                                {
                                    instrText = asmProp.GetString() ?? instrText;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Enrichment failure — still emit the address row.
            }

            sb.AppendLine($"  0x{addr:X8}  {instrText}");
        }

        return WithRules(sb.ToString().TrimEnd());
    }


    [DynamicDescription("search_desc")]
    [Description("Search live PS2 emulator memory or a disassembly listing for a text, regex, bytes, or string pattern. Returns all matching addresses and surrounding context.")]
    public async Task<string> Search(
        [Description("Text, regex, or hex byte pattern to search for.")] string query,
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to search (hex or decimal, e.g. '0x500000' or 5242880).")] string length,
        [Description("Search mode: 'text' (default), 'regex', 'bytes', or 'string'.")] string type = "text",
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        uint? parsedLength = TryParseAddress(length);
        if (parsedLength is null)
            return $"Error: invalid length '{length}'.";

        var json = await bridge.SendAsync(
            new
            {
                Command = "SEARCH",
                Query = query,
                Address = parsedAddress.Value,
                Length = (int)parsedLength.Value,
                SearchMode = type,
                Region = region
            },
            cancellationToken);

        return FormatSearchResults(json);
    }

    // -------------------------------------------------------------------------
    // Value scan (memory search by value)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "scan_value")]
    [DynamicDescription("scan_value_desc")]
    [Description("Scan EE RAM (or a custom address range) for a specific value of a given data type (u8, u16, u32, u64, f32). Returns all addresses where the value matches.")]
    public async Task<string> ScanValue(
        [Description("Value to scan for (decimal or 0x-prefixed hex string, e.g. '100' or '0x64' for uint32).")] string scanValue,
        [Description("Data type to interpret memory as (e.g. 'uint32', 'float32').")] string dataType,
        [Description("Comparison operator: 'eq', 'ne', 'lt', 'gt', 'le', 'ge' (default 'eq').")] string comparison = "eq",
        [Description("Scan start address (hex or decimal). Leave null to scan all of EE RAM.")] string? startAddress = null,
        [Description("Exclusive scan end address (hex or decimal). Leave null to scan to end of EE RAM.")] string? endAddress = null,
        [Description("Comma-separated list of hex addresses to re-check (filter/refine mode). Leave null for a full scan.")] string? previousAddresses = null,
        CancellationToken cancellationToken = default)
    {
        uint? start = startAddress != null ? TryParseAddress(startAddress) : null;
        uint? end   = endAddress   != null ? TryParseAddress(endAddress)   : null;

        List<uint>? prevList = null;
        if (!string.IsNullOrWhiteSpace(previousAddresses))
        {
            prevList = [];
            foreach (var part in previousAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                uint? a = TryParseAddress(part);
                if (a.HasValue) prevList.Add(a.Value);
            }
        }

        var json = await bridge.SendAsync(new
        {
            Command = "SCANVALUE",
            ScanValue = scanValue,
            DataType = dataType,
            Comparison = comparison,
            Address = start,
            EndAddress = end,
            ScanAddresses = prevList,
        }, cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("ScanResults", out var arr))
                return SimpleMessage(json);

            string? msg = root.TryGetProperty("Message", out var msgProp) ? msgProp.GetString() : null;
            var sb = new StringBuilder();
            int count = 0;
            foreach (var item in arr.EnumerateArray())
            {
                string addr = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string val  = item.TryGetProperty("value",   out var v) ? (v.GetString() ?? "?") : "?";
                sb.AppendLine($"  {addr}  {val}");
                count++;
            }

            string header = $"Found {count} match(es):\n";
            if (!string.IsNullOrEmpty(msg)) header += msg + "\n";
            return count > 0 ? header + sb.ToString().TrimEnd() : "(no matches found)";
        }
        catch { return json; }
    }
}
