using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CheatConverter;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

/// <summary>
/// MCP tools that expose the CodeDesigner MIPS64 Emotion Engine socket server
/// capabilities to GitHub Copilot.  Every tool forwards its request to the
/// socket server running on localhost:8765 and returns a human-readable result.
/// </summary>
[McpServerToolType]
public sealed class CodeDesignerPs2Tools(SocketBridgeClient bridge)
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Return the "Message" or "Error" field from the JSON response, or the
    /// raw JSON if neither field is present.
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
                return msg.GetString() ?? json;

            return json;
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
    [Description("Test connectivity to the MIPS64 Emotion Engine emulator backend. " +
                 "Returns PONG when the socket server is reachable on localhost:8765.")]
    public async Task<string> Ping(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PING" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Disassembly / Assembly
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "disassemble")]
    [Description("Disassemble PS2 Emotion Engine MIPS64 machine-code opcodes into assembly text. " +
                 "Provide 32-bit hex opcodes separated by spaces or newlines (e.g. \"27BDFFD0 AFBF001C\"). " +
                 "Optionally supply a virtual start address so labels and branch targets are resolved correctly.")]
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
    [Description("Assemble PS2 Emotion Engine MIPS64 assembly instructions into machine-code hex opcodes. " +
                 "Provide one instruction per line (e.g. \"addiu $sp,$sp,-0x30\").")]
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
    [Description("Compile a Code Designer Script (.cds) source file into PS2 GameShark/Action Replay cheat codes. " +
                 "Supports: address/hexcode/print/setreg directives, label definitions (Name:), " +
                 "branch label references (:Name), absolute address references (@Name), " +
                 "two-pass label resolution, and full MIPS instruction assembly. " +
                 "Each assembled instruction is emitted as a type-2 GameShark code (2AAAAAAA VVVVVVVV). " +
                 "Comments: // ; # and /* */ block comments are supported.")]
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
    [Description("Read raw bytes from the live PS2 emulator memory and return them as hex. " +
                 "Requires the emulator to be running (start with `dotnet run -- --run`). " +
                 "Address supports hex (0x prefix) or decimal. Region is 'ee' (main EE RAM, default) or 'iop'.")]
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
    [Description("Write arbitrary bytes to live PS2 emulator memory. " +
                 "Bytes are supplied as a hex string (spaces, commas, and '0x' prefixes are accepted). " +
                 "Supports EE main RAM, scratchpad, and IOP RAM.")]
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
    [Description("Load a binary file from disk directly into live emulator memory at a specified address. " +
                 "More efficient than write_memory for large files because the data is read locally by the " +
                 "server rather than transferred over the socket. " +
                 "Supports EE main RAM, BIOS ROM, scratchpad, and IOP RAM.")]
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
    [Description("Read bytes from the live PS2 emulator and disassemble them to MIPS64 EE assembly. " +
                 "Convenience wrapper around read_memory + disassemble. " +
                 "Each instruction is 4 bytes; 'count' defaults to 32 instructions.")]
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
            if (doc.RootElement.TryGetProperty("Error", out var errEl))
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
    [Description("Apply PS2 GameShark/Action Replay cheat codes to the live emulator memory. " +
                 "Parses the supplied cheat code text (raw TTAAAAAA VVVVVVVV format) and writes " +
                 "each patch directly into EE RAM. Works in both internal emulator mode and PINE/PCSX2 mode.")]
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
    [Description("Port PS2 GameShark cheat codes from one game revision to another by scanning two raw " +
                 "EE-RAM dump files and relocating addresses using hex-pattern matching. " +
                 "Four strategies are tried in order: before-window hex pattern, MIPS code-reference " +
                 "fingerprint, extended anchor search, and pointer scan. " +
                 "Returns ported codes with confidence scores and method names.")]
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
    [Description("Get the current program counter (PC) of the PS2 EE CPU and the emulator pause state. " +
                 "Returns the PC as a hex address and whether execution is paused.")]
    public async Task<string> GetPc(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETPC" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
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
    [Description("Get a full register dump of the PS2 EE CPU: all 32 general-purpose registers (GPRs), " +
                 "HI/LO, COP0 system registers, COP1 FPU floating-point registers, and VU0 vector registers.")]
    public async Task<string> GetRegisters(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETREGS" }, cancellationToken);
        return FormatRegisters(json);
    }

    [McpServerTool(Name = "set_register")]
    [Description("Write a new value to a named EE CPU register. " +
                 "RegisterName: GPR (requires RegisterIndex 1–31), PC, HI, LO, HI1, LO1, SA, " +
                 "COP0 (requires RegisterIndex 0–31), FPR (requires RegisterIndex 0–31), " +
                 "FCR (only FCR31/FCSR is writable), VU0I (requires RegisterIndex 0–15), " +
                 "VU0F (requires RegisterIndex 0–31, writes the X component). " +
                 "Values may be decimal or 0x-prefixed hex.")]
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
    [Description("Return all symbol labels and optional comments from the PS2 symbol database (ps2-labels.json). " +
                 "Useful for resolving addresses to known function or variable names.")]
    public async Task<string> GetLabels(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETLABELS" }, cancellationToken);
        return FormatLabels(json);
    }

    [McpServerTool(Name = "add_label")]
    [Description("Insert or update a symbol label in the PS2 label database (ps2-labels.json). " +
                 "If a label already exists at the given address it is overwritten. " +
                 "Changes are persisted to disk immediately.")]
    public async Task<string> AddLabel(
        [Description("Virtual address to label (hex or decimal).")] string address,
        [Description("Label name (e.g. 'scePadRead' or 'FNC_0x80011234').")] string label,
        [Description("Optional comment to attach to the label.")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "ADDLABEL", Address = parsedAddress.Value, Label = label, Comment = comment },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_label")]
    [Description("Remove a symbol label from the PS2 label database (ps2-labels.json). " +
                 "Changes are persisted to disk immediately.")]
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

    // -------------------------------------------------------------------------
    // Memory scanning (live)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "scan_strings")]
    [Description("Scan a region of live PS2 emulator memory for null-terminated ASCII strings. " +
                 "Returns each string found together with its virtual address.")]
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
    [Description("Scan a region of live PS2 emulator memory and detect likely MIPS function entry points " +
                 "(heuristic: ADDIU $sp,$sp,-N prologue pattern and JAL targets). " +
                 "Returns address and auto-generated or database label for each candidate.")]
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
    [Description("Scan a region of live PS2 emulator memory for 4-byte aligned values that are confirmed " +
                 "EE memory pointers. Three filters are applied: non-zero and 4-byte aligned, physical " +
                 "address ≥ 0x10000, and referenced by MIPS code in EE main RAM. " +
                 "Returns each pointer's address and the value it points to.")]
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
    [Description("Scan a region of live PS2 emulator memory for plausible IEEE-754 single-precision floats. " +
                 "Returns 4-byte aligned values whose absolute magnitude falls between 0.001 and 1,000,000. " +
                 "NaN, ±Inf, subnormals, and PS2 flush-to-zero values are excluded.")]
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
    [Description("Scan a region of live PS2 emulator memory for non-zero 4-byte aligned 32-bit integers. " +
                 "Common fill/sentinel values (0x00000000, 0xFFFFFFFF) are excluded to reduce noise. " +
                 "Returns each value as both signed decimal and hex.")]
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
    [Description("Scan base64-encoded binary data for null-terminated printable ASCII strings of at least 4 characters. " +
                 "Use read_memory to obtain the base64 data, then pass it here for analysis. " +
                 "Does not require the emulator to be running if data is already in hand.")]
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
    [Description("Scan base64-encoded binary data for MIPS function entry points. " +
                 "Detects ADDIU $sp,$sp,-N prologues and JAL targets within the block. " +
                 "Auto-labels each candidate 'FNC_0xADDRESS', respecting any existing database labels.")]
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
    [Description("Scan base64-encoded binary data for 4-byte aligned values that are confirmed EE memory pointers. " +
                 "Applies the same three-stage filter as scan_pointers. " +
                 "Use read_memory to obtain the base64 data.")]
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
    [Description("Scan base64-encoded binary data for plausible IEEE-754 single-precision floats. " +
                 "Applies the same magnitude filter as scan_floats (0.001–1,000,000 absolute). " +
                 "Use read_memory to obtain the base64 data.")]
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
    [Description("Scan base64-encoded binary data for non-zero 4-byte aligned 32-bit integers. " +
                 "Applies the same filter as scan_integers (excludes 0x00000000 and 0xFFFFFFFF). " +
                 "Use read_memory to obtain the base64 data.")]
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
    [Description("Search live PS2 emulator memory or a disassembly listing for a text, regex, bytes, or string pattern. " +
                 "Modes: 'text' (case-insensitive substring, default), 'regex' (case-insensitive), " +
                 "'bytes' (hex byte pattern, e.g. '27 BD FF D0'), 'string' (raw UTF-8 byte match). " +
                 "Returns matching instruction addresses and the matched field (addr/bytes/label/op/args/comment).")]
    public async Task<string> Search(
        [Description("Text, regex, or hex byte pattern to search for.")] string query,
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to search.")] int length,
        [Description("Search mode: 'text' (default), 'regex', 'bytes', or 'string'.")] string searchMode = "text",
        [Description("Memory region: 'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new
            {
                Command = "SEARCH",
                Query = query,
                Address = parsedAddress.Value,
                Length = length,
                SearchMode = searchMode,
                Region = region
            },
            cancellationToken);

        return FormatSearchResults(json);
    }

    // -------------------------------------------------------------------------
    // Execution control
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pause_emulator")]
    [Description("Pause PS2 emulator execution. The emulator must be running via `dotnet run -- --run`.")]
    public async Task<string> PauseEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PAUSE" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resume_emulator")]
    [Description("Resume PS2 emulator execution after a pause or breakpoint hit. " +
                 "If stopped at a breakpoint, automatically single-steps past it before releasing execution.")]
    public async Task<string> ResumeEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESUME" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step")]
    [Description("Execute exactly one EE instruction while keeping the emulator paused. " +
                 "The emulator must already be paused (use pause_emulator or set a breakpoint first). " +
                 "Returns the new program counter, pause state, and any instruction analysis comment.")]
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
    [Description("Perform a full hardware reset of the PS2 emulator without dropping the socket connection. " +
                 "The BIOS and disc image are reloaded, existing breakpoints are preserved, " +
                 "and the emulator is left paused at the reset vector (0xBFC00000).")]
    public async Task<string> RestartEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESTART" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "set_breakpoint")]
    [Description("Set a breakpoint at a virtual address in the PS2 emulator. " +
                 "Execution pauses when the CPU reaches this address. " +
                 "Addresses are normalized to physical form internally.")]
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
    [Description("Remove a previously set breakpoint at a virtual address.")]
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
    [Description("Remove all breakpoints currently set in the PS2 emulator.")]
    public async Task<string> ClearAllBreakpoints(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARBP_ALL" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Thread inspection
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_threads")]
    [Description("Enumerate PS2 EE OS threads by walking the kernel thread control block (TCB) pool. " +
                 "Returns thread ID, status (Running/Ready/Waiting/Suspended/Dormant), " +
                 "current PC, stack pointer, priority, and entry point for each thread found. " +
                 "If no OS threads are found a single synthetic entry for the live CPU state is returned. " +
                 "Only available in internal emulator mode (not PINE/PCSX2 mode).")]
    public async Task<string> GetThreads(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETTHREADS" }, cancellationToken);
        return FormatThreads(json);
    }

    // -------------------------------------------------------------------------
    // Snapshot / Save state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "snapshot")]
    [Description("Save a full memory snapshot of the running PS2 emulator to a directory on disk. " +
                 "Writes four binary files: EEmem.bin (32 MB EE RAM), BIOSrom.bin (4 MB BIOS), " +
                 "Scratchpad.bin (16 KB), and IOPmem.bin (IOP RAM). " +
                 "The output directory is created if it does not exist. Works in both internal and PINE modes.")]
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
    [Description("Reload a previously saved memory snapshot as a save state into the running PS2 emulator. " +
                 "Reads EEmem.bin, BIOSrom.bin, Scratchpad.bin, and IOPmem.bin from the given directory " +
                 "and restores them into the live emulator memory. " +
                 "The emulator is paused automatically before the restore and remains paused afterwards.")]
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
    [Description("Open a PS2 disc image (ISO/BIN or ZIP containing one) and list all ELF and IRX files found. " +
                 "Also reads SYSTEM.CNF to identify the BOOT2 executable. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Extract a named ELF from a PS2 disc image and statically recompile it to C++ source files " +
                 "using the MIPS64 EE static recompiler. " +
                 "When elfName is omitted the BOOT2 executable from SYSTEM.CNF is used. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Query the running PCSX2 emulator status via the PINE IPC socket. " +
                 "Returns Running, Paused, or Shutdown. Only available when the server was started with --pcsx2.")]
    public async Task<string> Pcsx2Status(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PCSX2STATUS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_game_info")]
    [Description("Query the running game's title, serial ID, UUID, and version from PCSX2 via the PINE IPC socket. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Heuristically detect the encryption format of a block of PS2 cheat codes. " +
                 "Returns the detected format name (Raw, ActionReplay1, ActionReplay2, " +
                 "ActionReplayMax, GameSharkV3, CodeBreakerV1, or CodeBreakerV7). " +
                 "Does not require the emulator to be running.")]
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
    [Description("Convert PS2 cheat codes between the unencrypted raw format and a " +
                 "device-specific encrypted format. Does not require the emulator to be running.\n\n" +
                 "Supported formats: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, " +
                 "GameSharkV3, CodeBreakerV1, CodeBreakerV7.\n\n" +
                 "direction='toRaw'    — decrypt device codes → unencrypted hex pairs (TTAAAAAA VVVVVVVV). " +
                 "The source format is auto-detected when targetFormat is omitted.\n" +
                 "direction='toEngine' — encrypt raw hex pairs → the chosen device format. " +
                 "targetFormat is required when direction is 'toEngine'.")]
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no results)";
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no results)";
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no output)";
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.ToString().TrimEnd();
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : json;
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no labels)";
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no strings found)";
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

            if (root.TryGetProperty("Error", out var errProp))
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no functions found)";
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no pointers found)";
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no floats found)";
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no integers found)";
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no matches)";
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no threads found)";
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
                string label = item.TryGetProperty("label", out var lProp) ? (lProp.GetString() ?? "?") : "?";
                string file = item.TryGetProperty("file", out var fProp) ? (fProp.GetString() ?? "?") : "?";
                uint baseA = item.TryGetProperty("baseAddress", out var bProp) ? bProp.GetUInt32() : 0u;
                int size = item.TryGetProperty("size", out var sProp) ? sProp.GetInt32() : 0;
                sb.AppendLine($"  [{label}]  base=0x{baseA:X8}  size={size / 1024} KB  → {file}");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : SimpleMessage(json);
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : SimpleMessage(json);
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no ELF/IRX files found)";
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : SimpleMessage(json);
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

            return sb.Length > 0 ? sb.ToString().TrimEnd() : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }
}
