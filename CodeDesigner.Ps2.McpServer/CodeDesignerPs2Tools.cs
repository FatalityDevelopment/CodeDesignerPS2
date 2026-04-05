using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Prompts;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

/// <summary>
/// MCP tools that expose the CodeDesigner MIPS64 Emotion Engine socket server
/// capabilities to GitHub Copilot.  Every tool forwards its request to the
/// socket server running on localhost:8765 and returns a human-readable result.
/// </summary>
[McpServerToolType]
public sealed class CodeDesignerPs2Tools(SocketBridgeClient bridge, McpDatabaseService db)
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Terse rules reminder prepended to every substantive tool response so
    /// the agent is continually nudged to follow operational guidelines without
    /// burning significant extra tokens.
    /// </summary>
    private const string RulesHeader =
        "📋 [Rules: call session_start at the beginning of every session · navigate_to after discoveries · label all findings · snapshot at milestones · update progress report when important]";

    /// <summary>Prepends the rules header to any non-empty result string.</summary>
    private static string WithRules(string result) =>
        string.IsNullOrEmpty(result) ? result : $"{RulesHeader}\n\n{result}";

    /// <summary>
    /// Return the "Message" or "Error" field from the JSON response, or the
    /// raw JSON if neither field is present.  Non-error results are prefixed
    /// with the short rules header to keep the agent on track.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Connectivity
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "ping")]
    [DynamicDescription("ping_desc")]
    public async Task<string> Ping(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PING" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "session_start")]
    [DynamicDescription("session_start_desc")]
    public async Task<string> SessionStart(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // Load the lead agent definition from the database (if available) to allow
        // the agent's rules and guidelines to be configured at runtime without
        // recompiling.  Fall back to the compiled-in prompt when no DB record exists.
        var agent = await db.GetLeadAgentAsync(cancellationToken);

        if (agent is not null)
        {
            if (!string.IsNullOrWhiteSpace(agent.Description))
                sb.AppendLine(agent.Description);

            if (!string.IsNullOrWhiteSpace(agent.Rules))
            {
                sb.AppendLine();
                sb.AppendLine("## Rules");
                sb.AppendLine(agent.Rules);
            }

            if (!string.IsNullOrWhiteSpace(agent.Guidelines))
            {
                sb.AppendLine();
                sb.AppendLine("## Guidelines");
                sb.AppendLine(agent.Guidelines);
            }

            if (!string.IsNullOrWhiteSpace(agent.RecommendedWorkflow))
            {
                sb.AppendLine();
                sb.AppendLine("## Recommended Workflow");
                sb.AppendLine(agent.RecommendedWorkflow);
            }

            if (agent.Tasks.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Session tasks");
                foreach (var task in agent.Tasks.OrderBy(t => t.Phase).ThenBy(t => t.SortOrder))
                    sb.AppendLine($"- [{task.Phase}] {task.Description}");
            }
        }
        else
        {
            sb.AppendLine(CodeDesignerPs2Prompts.CheatMasterSystemPrompt);
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var reportNames = await db.ListMemoriesAsync(cancellationToken);
        if (reportNames.Count == 0)
        {
            sb.AppendLine("📂 **No progress reports found.** This appears to be a new session.");
            sb.AppendLine("   Create one early with `save_progress_report` using a descriptive name.");
        }
        else
        {
            sb.AppendLine("📂 **Existing progress reports** (call `get_progress_report` to resume one):");
            foreach (var name in reportNames)
                sb.AppendLine($"   - {name}");
        }

        return sb.ToString().TrimEnd();
    }


    // -------------------------------------------------------------------------
    // Disassembly / Assembly
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "disassemble")]
    [DynamicDescription("disassemble_desc")]
    public async Task<string> Disassemble(
        [Description("Space- or newline-separated 32-bit hex opcodes to disassemble.")] string opcodes,
        [Description("Optional virtual start address in hex (e.g. '0x80010000') or decimal.")] string? address = null,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);

        var json = await bridge.SendAsync(
            new { Command = "DISASSEMBLE", Data = opcodes, Address = parsedAddress },
            cancellationToken);

        return FormatDisasmResults(json);
    }

    [McpServerTool(Name = "assemble")]
    [DynamicDescription("assemble_desc")]
    public async Task<string> Assemble(
        [Description("MIPS64 EE assembly instructions, one per line.")] string assembly,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ASSEMBLE", Data = assembly },
            cancellationToken);

        return FormatAsmResults(json);
    }

    [McpServerTool(Name = "compile_cds")]
    [DynamicDescription("compile_cds_desc")]
    public async Task<string> CompileCds(
        [Description("CDS source code to compile.")] string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "COMPILECDS", Data = sourceCode },
            cancellationToken);

        return FormatCompileCdsResults(json);
    }

    // -------------------------------------------------------------------------
    // Live emulator memory
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "read_memory")]
    [DynamicDescription("read_memory_desc")]
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
    [DynamicDescription("write_memory_desc")]
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
    [DynamicDescription("load_bin_desc")]
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
    [DynamicDescription("disassemble_memory_desc")]
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
    // Cheat codes
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "apply_cheats")]
    [DynamicDescription("apply_cheats_desc")]
    public async Task<string> ApplyCheats(
        [Description("Multi-line GameShark/Action Replay cheat codes in raw format (e.g. '20123456 000003E8').")] string cheatCodes,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "APPLYCHEATS", Data = cheatCodes },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "port_cheats")]
    [DynamicDescription("port_cheats_desc")]
    public async Task<string> PortCheats(
        [Description("Multi-line GameShark cheat codes to port (raw format).")] string cheatCodes,
        [Description("Absolute path to the source EE RAM dump (.bin file, typically EEmem.bin from a snapshot).")] string sourceDumpPath,
        [Description("Absolute path to the target EE RAM dump (.bin file) to port the codes into.")] string targetDumpPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PORTCHEATS", Data = cheatCodes, SourceDumpPath = sourceDumpPath, TargetDumpPath = targetDumpPath },
            cancellationToken);

        return FormatPortCheats(json);
    }

    // -------------------------------------------------------------------------
    // Emulator state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_pc")]
    [DynamicDescription("get_pc_desc")]
    public async Task<string> GetPc(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETPC" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) &&
                err.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(err.GetString()))
                return $"Error: {err.GetString()}";

            var pc = root.TryGetProperty("PC", out var pcProp) ? pcProp.GetUInt32() : 0u;
            var paused = root.TryGetProperty("Paused", out var pProp) && pProp.GetBoolean();
            var bpHit = root.TryGetProperty("BreakpointHitAddress", out var bpProp) && bpProp.ValueKind != JsonValueKind.Null
                ? $" | BreakpointHit = 0x{bpProp.GetUInt32():X8}"
                : "";
            return $"PC = 0x{pc:X8} | Paused = {paused}{bpHit}";
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "get_registers")]
    [DynamicDescription("get_registers_desc")]
    public async Task<string> GetRegisters(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETREGS" }, cancellationToken);
        return FormatRegisters(json);
    }

    [McpServerTool(Name = "set_register")]
    [DynamicDescription("set_register_desc")]
    public async Task<string> SetRegister(
        [Description("Register type: GPR, PC, HI, LO, HI1, LO1, SA, COP0, FPR, FCR, VU0I, or VU0F.")] string registerName,
        [Description("New register value (decimal or 0x-prefixed hex).")] string value,
        [Description("Register index (required for GPR 1–31, COP0 0–31, FPR 0–31, VU0I 0–15, VU0F 0–31).")] int? index = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SETREG", RegisterName = registerName, RegisterIndex = index, RegisterValue = value },
            cancellationToken);

        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Symbol labels
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_labels")]
    [DynamicDescription("get_labels_desc")]
    public async Task<string> GetLabels(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETLABELS" }, cancellationToken);
        return FormatLabels(json);
    }

    [McpServerTool(Name = "get_function_labels")]
    [DynamicDescription("get_function_labels_desc")]
    public async Task<string> GetFunctionLabels(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETLABELS" }, cancellationToken);
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

                // Only include labels that look like functions (FNC_ prefix from sync).
                if (!label.StartsWith("FNC_", StringComparison.Ordinal)) continue;

                string? cmt = item.TryGetProperty("comment", out var c) ? c.GetString() : null;
                sb.Append($"  {addr,-12}  {label}");
                if (!string.IsNullOrEmpty(cmt))
                    sb.Append($"  ; {cmt}");
                sb.AppendLine();
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no function labels defined)");
        }
        catch { return json; }
    }

    [McpServerTool(Name = "add_label")]
    [DynamicDescription("add_label_desc")]
    public async Task<string> AddLabel(
        [Description("Virtual address to label (hex or decimal).")] string address,
        [Description("Label name (e.g. 'scePadRead').")] string label,
        [Description("Optional comment to attach to the label.")] string? comment = null,
        [Description("Label type: Address (default), Struct, Function, Array, ArrayOfPointers, LinkedList, String.")] string? type = null,
        [Description("For Function: comma-separated argument registers, e.g. 'a0=playerPtr,a1=flags'.")] string? argRegs = null,
        [Description("For Function: comma-separated return registers, e.g. 'v0=result'.")] string? returnRegs = null,
        [Description("For Array: byte stride between elements, e.g. 4.")] int? elementStride = null,
        [Description("For Array: total number of elements (omit if unknown).")] int? elementCount = null,
        [Description("For LinkedList: byte offset within each node where the next-node pointer lives.")] int? nodeNextOffset = null,
        [Description("For LinkedList: total size of one node in bytes (informational).")] int? nodeSize = null,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new
            {
                Command = "ADDLABEL",
                Address = parsedAddress.Value,
                Label   = label,
                Comment = comment,
                LabelType = type,
                ArgRegs      = argRegs?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ReturnRegs   = returnRegs?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ElementStride = elementStride,
                ElementCount  = elementCount,
                NodeNextOffset = nodeNextOffset,
                NodeSize      = nodeSize,
            },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_label")]
    [DynamicDescription("delete_label_desc")]
    public async Task<string> DeleteLabel(
        [Description("Virtual address of the label to remove (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "DELETELABEL", Address = parsedAddress.Value },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resolve_strings")]
    [DynamicDescription("resolve_strings_desc")]
    public async Task<string> ResolveStrings(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESOLVESTRINGS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resolve_linked_list")]
    [DynamicDescription("resolve_linked_list_desc")]
    public async Task<string> ResolveLinkedList(
        [Description("Virtual address of the first node (hex or decimal).")] string address,
        [Description("Byte offset within each node of the next-node pointer (e.g. 0 or 4).")] int nextOffset,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "RESOLVELINKEDLIST", Address = parsedAddress.Value, MemberOffset = nextOffset },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.GetProperty("Success").GetBoolean())
                return $"Error: {(root.TryGetProperty("Error", out var e) ? e.GetString() : "RESOLVELINKEDLIST failed")}";

            string msg = root.TryGetProperty("Message", out var m) ? (m.GetString() ?? "") : "";
            string? data = root.TryGetProperty("Data", out var d) ? d.GetString() : null;
            if (data is not null)
            {
                var addrs = JsonSerializer.Deserialize<List<string>>(data) ?? [];
                return $"{msg}\n" + string.Join("\n", addrs.Select((a, i) => $"  [{i}] {a}"));
            }
            return msg;
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Memory scanning (live)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "scan_strings")]
    [DynamicDescription("scan_strings_desc")]
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
    // Search
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "search")]
    [DynamicDescription("search_desc")]
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
    // Execution control
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pause_emulator")]
    [DynamicDescription("pause_emulator_desc")]
    public async Task<string> PauseEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PAUSE" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resume_emulator")]
    [DynamicDescription("resume_emulator_desc")]
    public async Task<string> ResumeEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESUME" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step")]
    [DynamicDescription("step_desc")]
    public async Task<string> Step(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEP" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            var pc = root.TryGetProperty("PC", out var pcProp) ? pcProp.GetUInt32() : 0u;
            var paused = root.TryGetProperty("Paused", out var pProp) && pProp.GetBoolean();

            var sb = new StringBuilder();
            sb.AppendLine($"Stepped → PC = 0x{pc:X8} | Paused = {paused}");

            // Emit any per-instruction analysis comments produced by the step
            if (root.TryGetProperty("InstructionAnalysis", out var analysis) &&
                analysis.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in analysis.EnumerateObject())
                    sb.AppendLine($"  [0x{prop.Name}] {prop.Value.GetString()}");
            }

            return sb.ToString().TrimEnd();
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "restart_emulator")]
    [DynamicDescription("restart_emulator_desc")]
    public async Task<string> RestartEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESTART" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "set_breakpoint")]
    [DynamicDescription("set_breakpoint_desc")]
    public async Task<string> SetBreakpoint(
        [Description("Virtual address for the breakpoint (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SETBP", BreakpointAddress = parsedAddress.Value },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "clear_breakpoint")]
    [DynamicDescription("clear_breakpoint_desc")]
    public async Task<string> ClearBreakpoint(
        [Description("Virtual address of the breakpoint to remove (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "CLEARBP", BreakpointAddress = parsedAddress.Value },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "clear_all_breakpoints")]
    [DynamicDescription("clear_all_breakpoints_desc")]
    public async Task<string> ClearAllBreakpoints(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARBP_ALL" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Thread inspection
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_threads")]
    [DynamicDescription("get_threads_desc")]
    public async Task<string> GetThreads(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETTHREADS" }, cancellationToken);
        return FormatThreads(json);
    }

    // -------------------------------------------------------------------------
    // Snapshot / Save state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "snapshot")]
    [DynamicDescription("snapshot_desc")]
    public async Task<string> Snapshot(
        [Description("Absolute path to the output directory where snapshot files will be written.")] string outputPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SNAPSHOT", OutputPath = outputPath },
            cancellationToken);

        return FormatSnapshot(json);
    }

    [McpServerTool(Name = "load_snapshot")]
    [DynamicDescription("load_snapshot_desc")]
    public async Task<string> LoadSnapshot(
        [Description("Absolute path to the snapshot directory containing the .bin files to restore.")] string inputPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LOADSNAPSHOT", InputPath = inputPath },
            cancellationToken);

        return FormatLoadSnapshot(json);
    }

    // -------------------------------------------------------------------------
    // Disc image tools
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_disc_elfs")]
    [DynamicDescription("list_disc_elfs_desc")]
    public async Task<string> ListDiscElfs(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip archive containing an ISO/BIN).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LISTDISCELFS", IsoPath = isoPath },
            cancellationToken);

        return FormatDiscElfs(json);
    }

    [McpServerTool(Name = "recompile_elf")]
    [DynamicDescription("recompile_elf_desc")]
    public async Task<string> RecompileElf(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        [Description("Name of the ELF to recompile (e.g. 'SCUS_971.98'). Leave empty to use BOOT2.")] string? elfName = null,
        [Description("Output directory for generated C++ files. Defaults to 'recomp_out'.")] string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "RECOMPILE", IsoPath = isoPath, ElfName = elfName, OutputDirectory = outputDirectory },
            cancellationToken);

        return FormatRecompile(json);
    }

    // -------------------------------------------------------------------------
    // PCSX2 / PINE bridge queries
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_status")]
    [DynamicDescription("pcsx2_status_desc")]
    public async Task<string> Pcsx2Status(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PCSX2STATUS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_game_info")]
    [DynamicDescription("pcsx2_game_info_desc")]
    public async Task<string> Pcsx2GameInfo(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PCSX2GAMEINFO" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            // The Data field contains a JSON object with Title/ID/UUID/Version
            if (root.TryGetProperty("Data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String)
            {
                var inner = dataProp.GetString() ?? "";
                try
                {
                    using var innerDoc = JsonDocument.Parse(inner);
                    var r = innerDoc.RootElement;
                    var title   = r.TryGetProperty("Title",   out var t) ? t.GetString() : "";
                    var id      = r.TryGetProperty("ID",      out var i) ? i.GetString() : "";
                    var uuid    = r.TryGetProperty("UUID",    out var u) ? u.GetString() : "";
                    var version = r.TryGetProperty("Version", out var v) ? v.GetString() : "";
                    return $"Title:   {title}\nSerial:  {id}\nUUID:    {uuid}\nVersion: {version}";
                }
                catch { /* fall through */ }
            }

            return SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }

    // -------------------------------------------------------------------------
    // Cheat code conversion (no emulator required — pure offline processing)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "detect_cheat_format")]
    [DynamicDescription("detect_cheat_format_desc")]
    public Task<string> DetectCheatFormat(
        [Description("Multi-line block of PS2 cheat codes whose format should be identified.")] string codes)
    {
        if (string.IsNullOrWhiteSpace(codes))
            return Task.FromResult("Error: no codes provided");

        try
        {
            var format = CheatFormatDetector.Detect(codes);
            return Task.FromResult(format.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "convert_cheat")]
    [DynamicDescription("convert_cheat_desc")]
    public Task<string> ConvertCheat(
        [Description("Multi-line block of PS2 cheat codes to convert.")] string codes,
        [Description("Conversion direction: 'toRaw' (decrypt to raw hex) or 'toEngine' (encrypt to device format).")] string direction,
        [Description("Target/source cheat engine format. Required for 'toEngine'. " +
                     "Omit or pass empty string for 'toRaw' to auto-detect. " +
                     "Valid values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.")] string? targetFormat = null)
    {
        if (string.IsNullOrWhiteSpace(codes))
            return Task.FromResult("Error: no codes provided");
        if (string.IsNullOrWhiteSpace(direction))
            return Task.FromResult("Error: direction must be 'toRaw' or 'toEngine'");

        try
        {
            string result;

            if (string.Equals(direction, "toRaw", StringComparison.OrdinalIgnoreCase))
            {
                ICheatCodeConverter sourceConverter = string.IsNullOrWhiteSpace(targetFormat)
                    ? CheatFormatDetector.CreateConverter(CheatFormatDetector.Detect(codes))
                    : CheatFormatDetector.CreateConverter(ParseFormat(targetFormat));

                var rawCodes = sourceConverter.Decode(codes);
                result = new RawConverter().Encode(rawCodes);
            }
            else if (string.Equals(direction, "toEngine", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(targetFormat))
                    return Task.FromResult("Error: targetFormat is required when direction is 'toEngine'");

                var format = ParseFormat(targetFormat);
                var rawCodes = new RawConverter().Decode(codes);
                result = CheatFormatDetector.CreateConverter(format).Encode(rawCodes);
            }
            else
            {
                return Task.FromResult("Error: direction must be 'toRaw' or 'toEngine'");
            }

            return Task.FromResult(string.IsNullOrWhiteSpace(result)
                ? "(no output — check that the input codes are in the expected format)"
                : result);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Emulator control — extended
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "reset_emulator")]
    [DynamicDescription("reset_emulator_desc")]
    public async Task<string> ResetEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESET" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_into")]
    [DynamicDescription("step_into_desc")]
    public async Task<string> StepInto(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPINTO" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_over")]
    [DynamicDescription("step_over_desc")]
    public async Task<string> StepOver(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOVER" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_out")]
    [DynamicDescription("step_out_desc")]
    public async Task<string> StepOut(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOUT" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Framerate / limiter / frame advance (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_fps")]
    [DynamicDescription("get_fps_desc")]
    public async Task<string> GetFps(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETFPS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (root.TryGetProperty("FPS", out var fps))
                return $"FPS: {fps.GetSingle():F2}";
            return SimpleMessage(json);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "set_limiter_mode")]
    [DynamicDescription("set_limiter_mode_desc")]
    public async Task<string> SetLimiterMode(
        [Description("Limiter mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.")] byte mode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SETLIMITER", LimiterMode = mode }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "frame_advance")]
    [DynamicDescription("frame_advance_desc")]
    public async Task<string> FrameAdvance(
        [Description("Number of frames to advance (1–255).")] byte frameCount,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "FRAMEADVANCE", FrameCount = frameCount }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Breakpoint listing (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_breakpoints")]
    [DynamicDescription("list_breakpoints_desc")]
    public async Task<string> ListBreakpoints(
        [Description("CPU selector: 0=EE, 1=IOP, 255=both (default).")] byte cpuSel = 0xFF,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LISTBP", CpuSel = cpuSel }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Breakpoints", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== Active Breakpoints ===");
            foreach (var item in arr.EnumerateArray())
            {
                string addr    = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                bool   enabled = item.TryGetProperty("enabled", out var e) && e.GetBoolean();
                string cpu     = item.TryGetProperty("cpu",     out var c) ? (c.GetString() ?? "?") : "?";
                sb.AppendLine($"  {addr}  [{cpu}]  {(enabled ? "enabled" : "disabled")}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no breakpoints)";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Symbol / debug info (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_symbol")]
    [DynamicDescription("get_symbol_desc")]
    public async Task<string> GetSymbol(
        [Description("Virtual EE address to look up (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(new { Command = "GETSYMBOL", Address = parsedAddress.Value }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            string symAddr = root.TryGetProperty("SymbolAddress", out var sa) ? (sa.GetString() ?? "?") : "?";
            string symName = root.TryGetProperty("SymbolName",    out var sn) ? (sn.GetString() ?? "?") : "?";
            return $"0x{parsedAddress.Value:X8}  →  {symAddr}  {symName}";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_symbol_by_name")]
    [DynamicDescription("get_symbol_by_name_desc")]
    public async Task<string> GetSymbolByName(
        [Description("Demangled symbol name to look up.")] string symbolName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETSYMBOLBYNAME", SymbolName = symbolName }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            string symAddr = root.TryGetProperty("SymbolAddress", out var sa) ? (sa.GetString() ?? "?") : "?";
            uint   symSize = root.TryGetProperty("SymbolSize",    out var ss) ? ss.GetUInt32()           : 0u;
            return $"{symbolName}  →  {symAddr}  size={symSize}";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_call_stack")]
    [DynamicDescription("get_call_stack_desc")]
    public async Task<string> GetCallStack(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETSTACK" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("StackFrames", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== Call Stack ===");
            int frameIdx = 0;
            foreach (var item in arr.EnumerateArray())
            {
                string entry     = item.TryGetProperty("entry",     out var e)  ? (e.GetString()  ?? "?") : "?";
                string pc        = item.TryGetProperty("pc",        out var p)  ? (p.GetString()  ?? "?") : "?";
                string sp        = item.TryGetProperty("sp",        out var s)  ? (s.GetString()  ?? "?") : "?";
                uint   stackSize = item.TryGetProperty("stackSize", out var ss) ? ss.GetUInt32()           : 0u;
                sb.AppendLine($"  #{frameIdx,-2}  entry={entry}  pc={pc}  sp={sp}  frameSize={stackSize}");
                frameIdx++;
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(empty call stack)";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // IOP threads and modules (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_iop_threads")]
    [DynamicDescription("get_iop_threads_desc")]
    public async Task<string> GetIopThreads(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETIOPTHREADS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Threads", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== IOP Threads ===");
            foreach (var item in arr.EnumerateArray())
            {
                int    id     = item.TryGetProperty("id",       out var idProp)  ? idProp.GetInt32()   : -1;
                string status = item.TryGetProperty("status",   out var stProp)  ? (stProp.GetString() ?? "?") : "?";
                uint   pc     = item.TryGetProperty("pc",       out var pcProp)  ? pcProp.GetUInt32()  : 0u;
                uint   sp     = item.TryGetProperty("sp",       out var spProp)  ? spProp.GetUInt32()  : 0u;
                int    pri    = item.TryGetProperty("priority", out var priProp) ? priProp.GetInt32()  : 0;
                uint   entry  = item.TryGetProperty("entryPc",  out var eProp)   ? eProp.GetUInt32()   : 0u;
                sb.AppendLine($"  Thread {id,-3}  [{status,-9}]  PC=0x{pc:X8}  SP=0x{sp:X8}  Pri={pri,-3}  Entry=0x{entry:X8}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no IOP threads found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_modules")]
    [DynamicDescription("get_modules_desc")]
    public async Task<string> GetModules(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETMODULES" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Modules", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== IOP Modules ===");
            foreach (var item in arr.EnumerateArray())
            {
                string name     = item.TryGetProperty("name",     out var n)  ? (n.GetString()  ?? "?") : "?";
                ushort version  = item.TryGetProperty("version",  out var v)  ? v.GetUInt16()            : (ushort)0;
                string textAddr = item.TryGetProperty("textAddr", out var ta) ? (ta.GetString() ?? "?") : "?";
                uint   textSize = item.TryGetProperty("textSize", out var ts) ? ts.GetUInt32()           : 0u;
                uint   dataSize = item.TryGetProperty("dataSize", out var ds) ? ds.GetUInt32()           : 0u;
                sb.AppendLine($"  {name,-20}  v{version:X4}  text={textAddr}  +{textSize / 1024}KB code  +{dataSize / 1024}KB data");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no modules found)";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Fine-grained register access (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pine_get_register")]
    [DynamicDescription("pine_get_register_desc")]
    public async Task<string> PineGetRegister(
        [Description("CPU selector: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Register category (see tool description).")] byte registerCategory,
        [Description("Register index within the category.")] int registerIndex,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "GETREGISTER", CpuSel = cpuSel, RegisterCategory = registerCategory, RegisterIndex = registerIndex },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (root.TryGetProperty("RegisterValue", out var rv))
                return $"Register [{(cpuSel == 0 ? "EE" : "IOP")}] cat={registerCategory} idx={registerIndex} = 0x{rv.GetString()}";
            return SimpleMessage(json);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "pine_set_register")]
    [DynamicDescription("pine_set_register_desc")]
    public async Task<string> PineSetRegister(
        [Description("CPU selector: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Register category (see pine_get_register description).")] byte registerCategory,
        [Description("Register index within the category.")] int registerIndex,
        [Description("New value as a hex string (up to 32 hex chars = 128 bits, little-endian).")] string registerValue,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PINESETREG", CpuSel = cpuSel, RegisterCategory = registerCategory, RegisterIndex = registerIndex, RegisterValue = registerValue },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // PCSX2 symbol database (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_functions")]
    [DynamicDescription("list_functions_desc")]
    public async Task<string> ListFunctions(
        [Description("Pagination offset (0-based index of the first result to return).")] uint offset = 0,
        [Description("Maximum number of results to return (1–65535, default 256).")] ushort maxCount = 256,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LISTFUNCTIONS", Offset = offset, MaxCount = maxCount },
            cancellationToken);
        return FormatPineSymbols(json, "Functions", "PineSymbols");
    }

    [McpServerTool(Name = "list_globals")]
    [DynamicDescription("list_globals_desc")]
    public async Task<string> ListGlobals(
        [Description("Pagination offset (0-based index of the first result to return).")] uint offset = 0,
        [Description("Maximum number of results to return (1–65535, default 256).")] ushort maxCount = 256,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LISTGLOBALS", Offset = offset, MaxCount = maxCount },
            cancellationToken);
        return FormatPineSymbols(json, "Globals", "PineSymbols");
    }

    [McpServerTool(Name = "get_locals")]
    [DynamicDescription("get_locals_desc")]
    public async Task<string> GetLocals(
        [Description("Virtual EE address inside the function whose locals should be retrieved (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(new { Command = "GETLOCALS", Address = parsedAddress.Value }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("LocalVars", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Locals @ 0x{parsedAddress.Value:X8} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string storage = item.TryGetProperty("storageType", out var st) ? (st.GetString() ?? "?") : "?";
                int    value   = item.TryGetProperty("value",       out var v)  ? v.GetInt32()             : 0;
                string name    = item.TryGetProperty("name",        out var n)  ? (n.GetString()   ?? "?") : "?";
                sb.AppendLine($"  [{storage,-8}]  {value,8}  {name}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no locals found)";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // PINE live disassembly
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pine_disassemble")]
    [DynamicDescription("pine_disassemble_desc")]
    public async Task<string> PineDisassemble(
        [Description("CPU selector: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of instructions to disassemble (1–1000, default 32).")] ushort count = 32,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "PINEDISASSEMBLE", CpuSel = cpuSel, Address = parsedAddress.Value, Length = (int)count },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("DisassemblyLines", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string addr = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string text = item.TryGetProperty("text",    out var t) ? (t.GetString() ?? "")  : "";
                sb.AppendLine($"  {addr}  {text}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no disassembly)";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Save-state file management (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "save_state_file")]
    [DynamicDescription("save_state_file_desc")]
    public async Task<string> SaveStateFile(
        [Description("Absolute file path where the save state should be written (e.g. '/saves/slot1.p2s').")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SAVESTATETOFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "load_state_file")]
    [DynamicDescription("load_state_file_desc")]
    public async Task<string> LoadStateFile(
        [Description("Absolute path to the .p2s save-state file to load.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LOADSTATEFROMFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "extract_p2s")]
    [DynamicDescription("extract_p2s_desc")]
    public async Task<string> ExtractP2s(
        [Description("Absolute path to the .p2s PCSX2 save-state file to extract.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "EXTRACTP2S", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Direct PINE protocol commands (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pine_version")]
    [DynamicDescription("pine_version_desc")]
    public async Task<string> PineVersion(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PINEVERSION" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pine_save_state_slot")]
    [DynamicDescription("pine_save_state_slot_desc")]
    public async Task<string> PineSaveStateSlot(
        [Description("Save-state slot index (0–9).")] byte slot,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINESAVESTATE", StateSlot = slot }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pine_load_state_slot")]
    [DynamicDescription("pine_load_state_slot_desc")]
    public async Task<string> PineLoadStateSlot(
        [Description("Save-state slot index (0–9).")] byte slot,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINELOADSTATE", StateSlot = slot }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Memory card management (no emulator required — offline PS2 FAT)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "mc_list")]
    [DynamicDescription("mc_list_desc")]
    public async Task<string> McList(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MC_LIST", McPath = mcPath }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("McSaves", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Saves on {mcPath} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string name    = item.TryGetProperty("Name",       out var n)  ? (n.GetString()  ?? "?") : "?";
                uint   files   = item.TryGetProperty("FileCount",  out var fc) ? fc.GetUInt32()           : 0u;
                string created = item.TryGetProperty("Created",    out var cr) ? (cr.GetString() ?? "?") : "?";
                string modif   = item.TryGetProperty("Modified",   out var mo) ? (mo.GetString() ?? "?") : "?";
                uint   bytes   = item.TryGetProperty("TotalBytes", out var tb) ? tb.GetUInt32()           : 0u;
                sb.AppendLine($"  {name,-32}  {files} file(s)  {bytes / 1024} KB  created={created}  modified={modif}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no saves found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "mc_export")]
    [DynamicDescription("mc_export_desc")]
    public async Task<string> McExport(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Name of the save to export (as listed by mc_list, e.g. 'BASCUS-97133BUILTIN').")] string saveName,
        [Description("Absolute path to the directory where the save folder should be written.")] string exportDir,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_EXPORT", McPath = mcPath, SaveName = saveName, ExportDir = exportDir },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_import")]
    [DynamicDescription("mc_import_desc")]
    public async Task<string> McImport(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Absolute path to the save directory to import.")] string importDir,
        [Description("Whether to overwrite an existing save with the same name (default false).")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_IMPORT", McPath = mcPath, ImportDir = importDir, McOverwrite = overwrite },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_delete")]
    [DynamicDescription("mc_delete_desc")]
    public async Task<string> McDelete(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Name of the save to delete (as listed by mc_list).")] string saveName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_DELETE", McPath = mcPath, SaveName = saveName },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_format")]
    [DynamicDescription("mc_format_desc")]
    public async Task<string> McFormat(
        [Description("Absolute path where the new memory card image should be created.")] string mcPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MC_FORMAT", McPath = mcPath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_read_file")]
    [DynamicDescription("mc_read_file_desc")]
    public async Task<string> McReadFile(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Name of the save directory (as listed by mc_list, e.g. 'BASCUS-97133BUILTIN').")] string saveName,
        [Description("Name of the file within the save directory (e.g. 'ICON.SYS', 'SCUS-97133').")] string fileName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_READ_FILE", McPath = mcPath, SaveName = saveName, McFileName = fileName },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            string message = root.TryGetProperty("Message", out var msg) ? (msg.GetString() ?? "") : "";
            string data    = root.TryGetProperty("Data",    out var dat) ? (dat.GetString() ?? "") : "";
            return $"{message}\nbase64: {data}";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // HDD image manager (no emulator required — offline APA/PFS)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "hdd_list")]
    [DynamicDescription("hdd_list_desc")]
    public async Task<string> HddList(
        [Description("Absolute path to the PS2 HDD image file (e.g. 'hdd.raw').")] string hddPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "HDD_LIST", HddPath = hddPath }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("HddPartitions", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Partitions on {hddPath} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string id       = item.TryGetProperty("Id",         out var idProp)  ? (idProp.GetString()      ?? "?") : "?";
                string name     = item.TryGetProperty("Name",       out var n)       ? (n.GetString()           ?? "?") : "?";
                string type     = item.TryGetProperty("TypeName",   out var t)       ? (t.GetString()           ?? "?") : "?";
                ulong  bytes    = item.TryGetProperty("TotalBytes", out var tb)      ? tb.GetUInt64()                    : 0ul;
                string created  = item.TryGetProperty("Created",    out var cr)      ? (cr.GetString()          ?? "?") : "?";
                uint   files    = item.TryGetProperty("FileCount",  out var fc)      ? fc.GetUInt32()                    : 0u;
                sb.AppendLine($"  {id,-20}  {name,-30}  [{type}]  {bytes / (1024 * 1024)} MB  {files} file(s)  created={created}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no partitions found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "hdd_list_files")]
    [DynamicDescription("hdd_list_files_desc")]
    public async Task<string> HddListFiles(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID to list files from (as shown by hdd_list, e.g. '__common' or 'BASLUS-12345').")] string partitionId,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_LIST_FILES", HddPath = hddPath, PartitionId = partitionId },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("HddFiles", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Files in partition '{partitionId}' ===");
            foreach (var item in arr.EnumerateArray())
            {
                string name   = item.TryGetProperty("Name",        out var n)  ? (n.GetString()  ?? "?") : "?";
                ulong  size   = item.TryGetProperty("Size",        out var s)  ? s.GetUInt64()            : 0ul;
                bool   isDir  = item.TryGetProperty("IsDirectory", out var d)  && d.GetBoolean();
                string modif  = item.TryGetProperty("Modified",    out var mo) ? (mo.GetString() ?? "?") : "?";
                string tag    = isDir ? "[DIR]" : "     ";
                sb.AppendLine($"  {tag}  {name,-40}  {size,10} bytes  modified={modif}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no files found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "hdd_export")]
    [DynamicDescription("hdd_export_desc")]
    public async Task<string> HddExport(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID containing the file (as shown by hdd_list).")] string partitionId,
        [Description("File name to export (as shown by hdd_list_files).")] string fileName,
        [Description("Absolute path to the output directory.")] string exportDir,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_EXPORT", HddPath = hddPath, PartitionId = partitionId, FileName = fileName, ExportDir = exportDir },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "hdd_import")]
    [DynamicDescription("hdd_import_desc")]
    public async Task<string> HddImport(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID to import the file into (as shown by hdd_list).")] string partitionId,
        [Description("Absolute path to the file or directory to import.")] string importDir,
        [Description("Whether to overwrite an existing file with the same name (default false).")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_IMPORT", HddPath = hddPath, PartitionId = partitionId, ImportDir = importDir, McOverwrite = overwrite },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "hdd_delete")]
    [DynamicDescription("hdd_delete_desc")]
    public async Task<string> HddDelete(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID to delete (as shown by hdd_list).")] string partitionId,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_DELETE", HddPath = hddPath, PartitionId = partitionId },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "hdd_create")]
    [DynamicDescription("hdd_create_desc")]
    public async Task<string> HddCreate(
        [Description("Absolute path where the new HDD image should be created.")] string hddPath,
        [Description("Image size in gigabytes (default 40 GB).")] int sizeGb = 40,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_CREATE", HddPath = hddPath, HddSizeGb = sizeGb },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // ISO Manager — PS2 disc image operations
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "iso_list_files")]
    [DynamicDescription("iso_list_files_desc")]
    public async Task<string> IsoListFiles(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip archive containing an ISO/BIN).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_LIST_FILES", IsoPath = isoPath },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("IsoFiles", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Files on {isoPath} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string discPath = item.TryGetProperty("DiscPath", out var dp) ? (dp.GetString() ?? "?") : "?";
                string name     = item.TryGetProperty("Name",     out var n)  ? (n.GetString()  ?? "?") : "?";
                uint   lba      = item.TryGetProperty("Lba",      out var l)  ? l.GetUInt32()            : 0u;
                uint   size     = item.TryGetProperty("Size",     out var s)  ? s.GetUInt32()            : 0u;
                bool   isDir    = item.TryGetProperty("IsDirectory", out var d) && d.GetBoolean();
                string tag      = isDir ? "[DIR]" : "     ";
                sb.AppendLine($"  {tag}  {discPath,-50}  LBA:{lba,6}  {size,10} bytes");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no files found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "iso_read_file")]
    [DynamicDescription("iso_read_file_desc")]
    public async Task<string> IsoReadFile(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        [Description("Disc path to the file to extract (e.g. 'SYSTEM.CNF' or '/SCUS_123.45;1').")] string filePath,
        [Description("Optional: absolute path where the file should be written. If omitted, data is returned as base64.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_READ_FILE", IsoPath = isoPath, FilePath = filePath, OutputPath = outputPath },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "iso_get_info")]
    [DynamicDescription("iso_get_info_desc")]
    public async Task<string> IsoGetInfo(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_GET_INFO", IsoPath = isoPath },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Progress reports — persistent session memory (database-backed)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_progress_reports")]
    [DynamicDescription("list_progress_reports_desc")]
    public async Task<string> ListProgressReports(CancellationToken cancellationToken = default)
    {
        var names = await db.ListMemoriesAsync(cancellationToken);
        if (names.Count == 0)
            return WithRules("No progress reports found. This appears to be a new session.");

        var sb = new StringBuilder();
        sb.AppendLine("Progress reports (call get_progress_report to load one):");
        foreach (var name in names)
            sb.AppendLine($"  {name}");
        return WithRules(sb.ToString().TrimEnd());
    }

    [McpServerTool(Name = "get_progress_report")]
    [DynamicDescription("get_progress_report_desc")]
    public async Task<string> GetProgressReport(
        [Description(
            "The report name as returned by list_progress_reports " +
            "(e.g. 'socom2-player-health', 'gtavc-vehicle-struct').")] string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await db.GetMemoryAsync(name, cancellationToken);
            if (content is null)
                return $"Error: no progress report named '{McpDatabaseService.SanitiseName(name)}' found.";
            return WithRules(content);
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "save_progress_report")]
    [DynamicDescription("save_progress_report_desc")]
    public async Task<string> SaveProgressReport(
        [Description(
            "Short unique name for this report. Use hyphens as separators. " +
            "Examples: 'socom2-player-health', 'gtavc-vehicle-struct', 'mgs3-codec-addresses'.")] string name,
        [Description(
            "Full Markdown content of the progress report. Include: game name, current objective, " +
            "all discovered addresses with their labels, structure maps, watchpoint findings, " +
            "confirmed cheat codes, and a clear list of next steps.")] string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveMemoryAsync(name, content, cancellationToken);
            var sanitised = McpDatabaseService.SanitiseName(name);
            return WithRules($"Progress report '{sanitised}' saved to the database.");
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "delete_progress_report")]
    [DynamicDescription("delete_progress_report_desc")]
    public async Task<string> DeleteProgressReport(
        [Description("The report name to delete (as returned by list_progress_reports).")] string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool deleted = await db.DeleteMemoryAsync(name, cancellationToken);
            return deleted
                ? WithRules($"Progress report '{McpDatabaseService.SanitiseName(name)}' deleted.")
                : $"Error: no progress report named '{McpDatabaseService.SanitiseName(name)}' found.";
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------------
    // Settings — application configuration via the socket server database
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_setting")]
    [DynamicDescription("get_setting_desc")]
    public async Task<string> GetSetting(
        [Description("Setting key to retrieve (e.g. 'BiosPath', 'DiscPath', 'ui.selectedGame').")] string key,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETSETTING", SettingKey = key }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "set_setting")]
    [DynamicDescription("set_setting_desc")]
    public async Task<string> SetSetting(
        [Description("Setting key to insert or update (e.g. 'BiosPath', 'ui.selectedGame').")] string key,
        [Description("Value to store. Pass an empty string to clear the value.")] string value,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SETSETTING", SettingKey = key, SettingValue = value },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "get_all_settings")]
    [DynamicDescription("get_all_settings_desc")]
    public async Task<string> GetAllSettings(
        [Description("Optional key prefix to filter results (e.g. 'ui.' returns only UI settings). Leave blank for all settings.")] string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "GETALLSETTINGS", SettingPrefix = prefix ?? string.Empty },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("Data", out var data))
                return SimpleMessage(json);

            // Data is a JSON-encoded dictionary of key → value pairs.
            using var dataDoc = JsonDocument.Parse(data.GetString() ?? "{}");
            var sb = new StringBuilder();
            foreach (var prop in dataDoc.RootElement.EnumerateObject())
                sb.AppendLine($"  {prop.Name} = {prop.Value.GetString() ?? "(null)"}");

            return sb.Length > 0
                ? WithRules(sb.ToString().TrimEnd())
                : WithRules("(no settings found)");
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "delete_setting")]
    [DynamicDescription("delete_setting_desc")]
    public async Task<string> DeleteSetting(
        [Description("Setting key to remove (e.g. 'ui.selectedGame').")] string key,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "DELETESETTING", SettingKey = key },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Snapshot history — list saved snapshots from the database
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_snapshots")]
    [DynamicDescription("get_snapshots_desc")]
    public async Task<string> GetSnapshots(
        [Description("ISO filename or game title used to look up associated snapshots (e.g. 'GoW.iso' or 'God of War').")] string isoName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "GETSNAPSHOTS", IsoName = isoName },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("Snapshots", out var snapshots))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            int count = 0;
            foreach (var s in snapshots.EnumerateArray())
            {
                var title = s.TryGetProperty("Title", out var t)       ? t.GetString()  ?? "?"  : "?";
                var path  = s.TryGetProperty("SnapshotPath", out var p) ? p.GetString() ?? "?"  : "?";
                var date  = s.TryGetProperty("SnapshotDateTime", out var d) ? d.GetString() ?? "?" : "?";
                var desc  = s.TryGetProperty("Description", out var de) && de.ValueKind == JsonValueKind.String
                    ? de.GetString() : null;

                sb.AppendLine($"  [{count + 1}] {title}  ({date})");
                sb.AppendLine($"       Path: {path}");
                if (!string.IsNullOrWhiteSpace(desc))
                    sb.AppendLine($"       Note: {desc}");
                count++;
            }

            if (count == 0)
                return WithRules($"No snapshots found for '{isoName}'.");

            return WithRules($"Snapshots for '{isoName}' ({count}):\n" + sb.ToString().TrimEnd());
        }
        catch
        {
            return json;
        }
    }

    // -------------------------------------------------------------------------
    // Struct / memory-map viewer
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_structs")]
    [DynamicDescription("get_structs_desc")]
    public async Task<string> GetStructs(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETSTRUCTS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Structs", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string name = item.TryGetProperty("name",        out var n)  ? (n.GetString()  ?? "?") : "?";
                string addr = item.TryGetProperty("baseAddress", out var a)  ? (a.GetString()  ?? "?") : "?";
                sb.AppendLine($"struct {name}  @ {addr}");
                if (item.TryGetProperty("members", out var members))
                {
                    foreach (var m in members.EnumerateArray())
                    {
                        int    offset = m.TryGetProperty("offset", out var o)  ? o.GetInt32()            : 0;
                        string mname  = m.TryGetProperty("name",   out var mn) ? (mn.GetString() ?? "?") : "?";
                        sb.AppendLine($"  +0x{offset:X3}  {mname}");
                    }
                }
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no structs defined)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "add_struct")]
    [DynamicDescription("add_struct_desc")]
    public async Task<string> AddStruct(
        [Description("Unique name for this struct (e.g. 'PlayerState').")] string structName,
        [Description("Base address of the struct in EE memory (hex or decimal, e.g. '0x80200000').")] string baseAddress,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ADDSTRUCT", StructName = structName, StructBaseAddress = baseAddress },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_struct")]
    [DynamicDescription("delete_struct_desc")]
    public async Task<string> DeleteStruct(
        [Description("Name of the struct to delete.")] string structName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "DELETESTRUCT", StructName = structName },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "add_struct_member")]
    [DynamicDescription("add_struct_member_desc")]
    public async Task<string> AddStructMember(
        [Description("Name of the struct to add the member to.")] string structName,
        [Description("Byte offset of the member within the struct (e.g. 0 for the first field).")] int memberOffset,
        [Description("Display name for the member field (e.g. 'health' or 'posX').")] string memberName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ADDSTRUCTMEMBER", StructName = structName, MemberOffset = memberOffset, MemberName = memberName },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Navigation history & snapshot line history
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_nav_history")]
    [DynamicDescription("get_nav_history_desc")]
    public async Task<string> GetNavHistory(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETNAVHISTORY" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("NavHistory", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            var entries = arr.EnumerateArray().ToList();
            if (entries.Count == 0) return "(navigation history is empty)";
            sb.AppendLine($"Navigation history ({entries.Count} entries, newest last):");
            foreach (var item in entries)
            {
                string addr  = item.TryGetProperty("Address",   out var a)   ? (a.GetString()  ?? "?") : "?";
                string? lbl  = item.TryGetProperty("Label",     out var l) && l.ValueKind == JsonValueKind.String
                                   ? l.GetString() : null;
                string src   = item.TryGetProperty("Source",    out var s)   ? (s.GetString()  ?? "?") : "?";
                string ts    = item.TryGetProperty("Timestamp", out var t)   ? (t.GetString()  ?? "?") : "?";
                string name  = lbl != null ? $"{addr}  {lbl}" : addr;
                sb.AppendLine($"  {name,-40} [{src}]  {ts}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "clear_nav_history")]
    [DynamicDescription("clear_nav_history_desc")]
    public async Task<string> ClearNavHistory(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARNAVHISTORY" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "navigate_to")]
    [DynamicDescription("navigate_to_desc")]
    public async Task<string> NavigateTo(
        [Description("EE virtual address to navigate to (hex or decimal, e.g. '0x8054ABCD').")] string address,
        CancellationToken cancellationToken = default)
    {
        // Parse address to uint so the server can push an ai_activity notification.
        uint addr = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(address, 16)
            : address.StartsWith("0X", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(address, 16)
                : uint.Parse(address);

        var json = await bridge.SendAsync(
            new { Command = "NAVIGATETO", Address = addr },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "get_address_snapshot_history")]
    [DynamicDescription("get_address_snapshot_history_desc")]
    public async Task<string> GetAddressSnapshotHistory(
        [Description("EE virtual address to look up (hex or decimal, e.g. '0x80200000').")] string address,
        [Description("Root directory to scan for snapshots (e.g. '/workspace/snapshots/SOCOM2/').")] string snapshotDir,
        CancellationToken cancellationToken = default)
    {
        uint addr = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    address.StartsWith("0X", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(address, 16)
            : uint.Parse(address);

        var json = await bridge.SendAsync(
            new { Command = "GETADDRESSHISTORY", Address = addr, SnapshotDir = snapshotDir },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("AddressHistory", out var arr))
                return SimpleMessage(json);

            var entries = arr.EnumerateArray().ToList();
            if (entries.Count == 0)
                return $"No snapshot history found for {address} in {snapshotDir}";

            var sb = new StringBuilder();
            sb.AppendLine($"Snapshot history for {address} ({entries.Count} snapshot(s)):");
            foreach (var item in entries)
            {
                string name   = item.TryGetProperty("SnapshotName", out var n) ? (n.GetString() ?? "?") : "?";
                string value  = item.TryGetProperty("Value",        out var v) ? (v.GetString() ?? "?") : "?";
                string ts     = item.TryGetProperty("Timestamp",    out var t) ? (t.GetString() ?? "?") : "?";
                string? disasm = item.TryGetProperty("Disasm", out var d) && d.ValueKind == JsonValueKind.String
                                    ? d.GetString() : null;
                string dis = disasm != null ? $"  {disasm}" : string.Empty;
                sb.AppendLine($"  [{name}]  0x{value}{dis}  ({ts})");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Value scan (memory search by value)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "scan_value")]
    [DynamicDescription("scan_value_desc")]
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

    // -------------------------------------------------------------------------
    // Memory region read + search, watchpoints, call tracing, run-until-PC
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "read_memory_region")]
    [DynamicDescription("read_memory_region_desc")]
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
    [DynamicDescription("scan_pattern_desc")]
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

    [McpServerTool(Name = "set_memory_watchpoint")]
    [DynamicDescription("set_memory_watchpoint_desc")]
    public async Task<string> SetMemoryWatchpoint(
        [Description("EE virtual address to watch (hex or decimal).")] string address,
        [Description("Number of bytes to watch (1–256, default 4).")] int size = 4,
        [Description("Access type to monitor: 'write' (default). 'read' and 'readwrite' are reserved for future use.")] string type = "write",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SETWATCHPOINT", Address = parsedAddress.Value, Length = size, WatchpointType = type },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "clear_memory_watchpoint")]
    [DynamicDescription("clear_memory_watchpoint_desc")]
    public async Task<string> ClearMemoryWatchpoint(
        [Description("Watchpoint ID to remove (e.g. 'wp-0'), as returned by set_memory_watchpoint.")] string watchpointId,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "CLEARWATCHPOINT", WatchpointId = watchpointId },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "clear_all_memory_watchpoints")]
    [DynamicDescription("clear_all_memory_watchpoints_desc")]
    public async Task<string> ClearAllMemoryWatchpoints(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARALLWATCHPOINTS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "list_memory_watchpoints")]
    [DynamicDescription("list_memory_watchpoints_desc")]
    public async Task<string> ListMemoryWatchpoints(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LISTWATCHPOINTS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("Watchpoints", out var wps) ||
                wps.ValueKind != JsonValueKind.Array)
                return "No watchpoints registered.";

            int count = wps.GetArrayLength();
            if (count == 0) return "No watchpoints registered.";

            var sb = new StringBuilder();
            sb.AppendLine($"Active watchpoints ({count}):");
            foreach (var wp in wps.EnumerateArray())
            {
                string id   = wp.TryGetProperty("Id",      out var idP)   ? idP.GetString()   ?? "?" : "?";
                string addr = wp.TryGetProperty("Address", out var addrP) ? addrP.GetString() ?? "?" : "?";
                int    sz   = wp.TryGetProperty("Size",    out var szP)   ? szP.GetInt32()       : 0;
                string ty   = wp.TryGetProperty("Type",    out var tyP)   ? tyP.GetString()   ?? "?" : "?";
                sb.AppendLine($"  {id}: 0x{addr} size={sz} type={ty}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_watchpoint_hit")]
    [DynamicDescription("get_watchpoint_hit_desc")]
    public async Task<string> GetWatchpointHit(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETWATCHPOINTHIT" }, cancellationToken);
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

    [McpServerTool(Name = "trace_calls")]
    [DynamicDescription("trace_calls_desc")]
    public async Task<string> TraceCalls(
        [Description("Exclusive end EE virtual address at which tracing stops (hex or decimal).")] string endPc,
        [Description("Maximum number of EE instructions to step through (default 10 000).")] int maxInstructions = 10_000,
        CancellationToken cancellationToken = default)
    {
        uint? parsedEnd = TryParseAddress(endPc);
        if (parsedEnd is null)
            return $"Error: invalid endPc '{endPc}'.";

        var json = await bridge.SendAsync(
            new { Command = "TRACECALLS", EndAddress = parsedEnd.Value, MaxInstructions = maxInstructions },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("TracedCalls", out var calls) ||
                calls.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            int count = calls.GetArrayLength();
            if (count == 0) return "No call sites found in the specified range.";

            var sb = new StringBuilder();
            sb.AppendLine($"Traced {count} call(s):");
            foreach (var call in calls.EnumerateArray())
            {
                string from = call.TryGetProperty("From", out var f) ? f.GetString() ?? "?" : "?";
                string to   = call.TryGetProperty("To",   out var t) ? t.GetString() ?? "?" : "?";
                sb.AppendLine($"  0x{from} → 0x{to}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "run_until_pc")]
    [DynamicDescription("run_until_pc_desc")]
    public async Task<string> RunUntilPc(
        [Description("Target EE virtual address to run to (hex or decimal).")] string targetPc,
        [Description("Timeout in milliseconds before giving up (default 10 000).")] int timeoutMs = 10_000,
        CancellationToken cancellationToken = default)
    {
        uint? parsedTarget = TryParseAddress(targetPc);
        if (parsedTarget is null)
            return $"Error: invalid targetPc '{targetPc}'.";

        // Use a longer cancellation timeout than the server-side timeout so that
        // the TCP socket stays open until the server-side polling finishes.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs + 5_000);

        var json = await bridge.SendAsync(
            new { Command = "RUNUNTILPC", TargetPc = parsedTarget.Value, TimeoutMs = timeoutMs },
            cts.Token);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            // On success HandleRunUntilPc returns HandleGetRegisters() output.
            return FormatRegisters(json);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "run_until_watchpoint")]
    [DynamicDescription("run_until_watchpoint_desc")]
    public async Task<string> RunUntilWatchpoint(
        [Description("Timeout in milliseconds before giving up (default 15 000).")] int timeoutMs = 15_000,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs + 5_000);

        var json = await bridge.SendAsync(
            new { Command = "RUNUNTILWATCHPOINT", TimeoutMs = timeoutMs },
            cts.Token);

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

    // -------------------------------------------------------------------------
    // Private formatting helpers
    // -------------------------------------------------------------------------

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

            if (!root.TryGetProperty("Results", out var results))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            bool hasErrors = false;
            foreach (var item in results.EnumerateArray())
            {
                bool success = item.TryGetProperty("Success", out var s) && s.GetBoolean();
                int line = item.TryGetProperty("LineNumber", out var ln) ? ln.GetInt32() : 0;

                if (!success)
                {
                    hasErrors = true;
                    string errMsg = item.TryGetProperty("Error",    out var e) ? (e.GetString() ?? "?") : "?";
                    string srcAsm = item.TryGetProperty("Assembly", out var a) ? (a.GetString() ?? "") : "";
                    sb.AppendLine($"  [line {line} error] {srcAsm}: {errMsg}");
                    continue;
                }

                string hex = item.TryGetProperty("Hex",      out var h) ? (h.GetString() ?? "") : "";
                string asm = item.TryGetProperty("Assembly", out var aa) ? (aa.GetString() ?? "") : "";
                sb.AppendLine($"  {hex}  ; {asm}");
            }

            if (hasErrors)
                sb.Insert(0, "Compiled with errors:\n");

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
            sb.AppendLine("Snapshot written:");
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
