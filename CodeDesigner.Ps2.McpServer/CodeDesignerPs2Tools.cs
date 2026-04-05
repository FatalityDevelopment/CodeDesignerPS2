using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.McpServer.Prompts;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

/// <summary>
/// MCP tools that expose the CodeDesigner MIPS64 Emotion Engine socket server
/// capabilities to GitHub Copilot.  Every tool forwards its request to the
/// socket server running on localhost:8765 and returns a human-readable result.
/// </summary>
[McpServerToolType]
public sealed class CodeDesignerPs2Tools(SocketBridgeClient bridge, ProgressReportManager reports)
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
    [Description("Test connectivity to the MIPS64 Emotion Engine emulator backend. " +
                 "Returns PONG when the socket server is reachable on localhost:8765.")]
    public async Task<string> Ping(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PING" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "session_start")]
    [Description(
        "CALL THIS FIRST at the beginning of every session. " +
        "Returns the full Code Designer operational guidelines so the agent is properly " +
        "configured before taking any other action. " +
        "Also lists all existing progress reports so previous session work can be resumed. " +
        "This tool is the primary way to load guidelines — do not rely on prompts/get.")]
    public Task<string> SessionStart()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CodeDesignerPs2Prompts.CheatMasterSystemPrompt);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var reportNames = reports.ListReports();
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

        return Task.FromResult(sb.ToString().TrimEnd());
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
    [Description(
        "Compile a Code Designer Script (.cds) source file into PS2 GameShark/Action Replay cheat codes.\n\n" +

        "=== WHAT IS CDS? ===\n" +
        "Code Designer Script is a PS2 exploit/code-injection language. It lets you write real MIPS64 EE " +
        "assembly that is packaged as GameShark type-2 codes (2AAAAAAA VVVVVVVV). Each type-2 code writes " +
        "a 32-bit word to a target address when the cheat engine ticks, so a compiled CDS program installs " +
        "your machine code into RAM at the addresses you choose. A typical use-case is patching game logic, " +
        "hooking functions, or injecting a custom payload that runs via a crafted network packet, stack " +
        "overflow, or function pointer overwrite.\n\n" +

        "=== OUTPUT FORMAT ===\n" +
        "Every 4-byte word emitted by the compiler becomes one GameShark code:\n" +
        "  2AAAAAAA VVVVVVVV\n" +
        "where AAAAAAA is the PS2 EE physical address and VVVVVVVV is the 32-bit value to write. " +
        "The cheat engine applies these codes once per frame while the game is running.\n\n" +

        "=== DIRECTIVES ===\n" +
        "  address $XXXXXXXX   — Set the current write cursor to this virtual/physical EE address.\n" +
        "                        All following instructions/data are emitted from this address onward.\n" +
        "  hexcode $XXXXXXXX   — Emit exactly 4 raw bytes (big-endian 32-bit word) at the current address.\n" +
        "                        Used for hand-coded data, packet headers, filler patterns, or magic values.\n" +
        "  print \"text\"        — Emit the ASCII/UTF-8 bytes of the string as consecutive code words.\n" +
        "                        Each 4-byte chunk becomes one code. Pads with 0x00 if needed.\n" +
        "  setreg REG, $VALUE  — Emit a LUI+ADDIU/ORI pair that loads a full 32-bit immediate into REG.\n" +
        "  setreg REG, :label  — Same but uses the label's resolved address as the immediate.\n\n" +

        "=== LABELS ===\n" +
        "  Name:               — Define a label at the current address (e.g. fnc_ChatPacket:, str_Message:).\n" +
        "                        Label names may contain letters, digits, and underscores.\n" +
        "  :Name               — Reference a label in branch/jump instructions (j :loop, jal :fnc_Name)\n" +
        "                        or in setreg (setreg t2, :str_Message). Resolved in a two-pass compile.\n" +
        "  @Name               — Reference a label's address as a raw 32-bit value in hexcode/data context.\n\n" +

        "=== MIPS INSTRUCTION ASSEMBLY ===\n" +
        "All PS2 EE MIPS64 instructions are supported inline (addiu, sw, lw, jal, j, jr, lui, ori, " +
        "daddu, sb, lb, lh, sh, lw, ld, sw, sd, beq, bne, bgez, bltz, sll, srl, sra, nop, etc.). " +
        "Write one instruction per line. Branch delay slots must be filled manually (use nop if needed). " +
        "Register names: $zero/$0, $at/$1, $v0–$v1, $a0–$a3, $t0–$t9, $s0–$s7, $k0–$k1, $gp, $sp, $fp, $ra.\n\n" +

        "=== COMMENTS ===\n" +
        "  // single-line comment\n" +
        "  ;  single-line comment\n" +
        "  #  single-line comment\n" +
        "  /* multi-line block comment */\n\n" +

        "=== COMMON PATTERNS ===\n" +
        "Hook a function at $0022F4DC, redirect to custom code at $000A0000, then jump back:\n" +
        "  address $0022F4DC\n" +
        "  jal :fnc_MyHook       // replace the original jal\n" +
        "  nop\n" +
        "  j $0022F510           // skip rest of original function\n" +
        "  nop\n\n" +
        "Load a pointer and call a function:\n" +
        "  address $000A0000\n" +
        "  fnc_MyHook:\n" +
        "  addiu sp, sp, $fff0\n" +
        "  sw ra, $0000(sp)\n" +
        "  setreg a0, $0045D480  // load 32-bit address into $a0 via LUI+ORI\n" +
        "  jal $0032A420\n" +
        "  nop\n" +
        "  lw ra, $0000(sp)\n" +
        "  jr ra\n" +
        "  addiu sp, sp, $10\n\n" +
        "Embed a raw data payload after code:\n" +
        "  str_Payload:\n" +
        "  hexcode $140          // packet size field\n" +
        "  print \"AAAA...\"       // 136-byte filler\n" +
        "  hexcode $11111111     // overwrite stack slot 1\n" +
        "  hexcode $20E0EB70     // pointer to t9 in PacketDataPool\n\n" +

        "=== FULL EXAMPLE (network exploit payload) ===\n" +
        "/*\n" +
        " * Sends a crafted ChatMessage packet that causes the receiving client to\n" +
        " * execute the payload at str_Message. Player name must be exactly 3 chars.\n" +
        " */\n" +
        "// 1. Replace mute hook with call to our function\n" +
        "address $0022F4DC\n" +
        "jal :fnc_ChatPacket\n" +
        "nop\n" +
        "j $0022F510\n" +
        "nop\n\n" +
        "// 2. Overwrite 'Mute Off' string (used as packet trigger)\n" +
        "address $00411178\n" +
        "print \"TOGGLE\"\n\n" +
        "// 3. Our chat-packet builder (installed in scratch RAM)\n" +
        "address $000A0000\n" +
        "fnc_ChatPacket:\n" +
        "addiu sp, sp, $fff0\n" +
        "sw ra, $0000(sp)\n" +
        "setreg a0, $0045D480   // session master pointer\n" +
        "addiu a1, zero, $0040\n" +
        "lui a2, $0046\n" +
        "lw a2, $D574(a2)\n" +
        "lui a3, $0044\n" +
        "lw a3, $F9D8(a3)\n" +
        "lw a3, $0FC8(a3)       // player ID\n" +
        "ori t0, zero, $9       // chat packet ID\n" +
        "addiu t1, zero, $140   // data size (must not exceed $1D0)\n" +
        "setreg t2, :str_Message\n" +
        "jal $0032A420\n" +
        "daddu t3, zero, zero\n" +
        "lw ra, $0000(sp)\n" +
        "jr ra\n" +
        "addiu sp, sp, $10\n\n" +
        "// 4. Packet data — overwrites stack slots on remote client\n" +
        "str_Message:\n" +
        "hexcode $140            // data size header\n" +
        "print \"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"   // 136-byte filler\n" +
        "hexcode $11111111\n" +
        "hexcode $22222222\n" +
        "hexcode $33333333\n" +
        "hexcode $20E0EB70       // pointer to t9 slot in PacketDataPool\n" +
        "hexcode $00000000\n" +
        "hexcode $00000000\n" +
        "hexcode $00000000\n" +
        "hexcode $00000000\n" +
        "hexcode $20E0EB84       // pointer to payload entry (must end in $20, not $00)\n\n" +
        "// 5. Payload — executes on remote client (~30 instructions max before packet splits)\n" +
        "// address $00E0EB84   (recalculate if payload length changes)\n" +
        "lui t0, $0045\n" +
        "lw t0, $D648(t0)        // load player object pointer\n" +
        "addiu t1, zero, $1\n" +
        "sb t1, $0FCF(t0)        // set respawn flag\n" +
        "j $00644D50             // return to packet receiver (drops packet)\n" +
        "addiu sp, sp, $1d0      // restore SP to packet receiver frame\n")]
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
    // Emulator control — extended
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "reset_emulator")]
    [Description("Send a CPU reset signal to the PS2 emulator (PINE) or reset the internal scheduler. " +
                 "Unlike restart_emulator (which reloads the BIOS and disc), this performs a softer " +
                 "hardware-level reset. Works in both internal and PINE/PCSX2 modes.")]
    public async Task<string> ResetEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESET" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_into")]
    [Description("Step into the next PS2 EE instruction (follows calls into subroutines). " +
                 "In PINE/PCSX2 mode this uses the native PCSX2 step-into command; " +
                 "in internal emulator mode it behaves identically to step. " +
                 "The emulator must already be paused.")]
    public async Task<string> StepInto(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPINTO" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_over")]
    [Description("Step over the next PS2 EE instruction (skips into subroutines, executing them fully). " +
                 "In PINE/PCSX2 mode this uses the native PCSX2 step-over command; " +
                 "in internal emulator mode it behaves identically to step. " +
                 "The emulator must already be paused.")]
    public async Task<string> StepOver(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOVER" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_out")]
    [Description("Step out of the current PS2 EE subroutine, running until the next return instruction. " +
                 "In PINE/PCSX2 mode this uses the native PCSX2 step-out command; " +
                 "in internal emulator mode it behaves identically to step. " +
                 "The emulator must already be paused.")]
    public async Task<string> StepOut(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOUT" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Framerate / limiter / frame advance (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_fps")]
    [Description("Read the current emulated framerate from PCSX2 via the PINE socket. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Set the PCSX2 speed limiter mode via the PINE socket. " +
                 "mode: 0=Nominal (default 60 fps), 1=Turbo (maximum speed), 2=Slomo (slow motion), 3=Unlimited. " +
                 "Only available when the server was started with --pcsx2.")]
    public async Task<string> SetLimiterMode(
        [Description("Limiter mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.")] byte mode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SETLIMITER", LimiterMode = mode }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "frame_advance")]
    [Description("Advance the PCSX2 emulator by a fixed number of video frames then pause. " +
                 "Useful for frame-by-frame analysis. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("List all active PC breakpoints set in PCSX2 via the PINE socket. " +
                 "cpuSel: 0=EE only, 1=IOP only, 255=both (default). " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Look up the EE function symbol name for a given virtual address via PINE. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Look up an EE symbol address and size by its demangled name via PINE. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Walk the EE call stack and return each frame's entry point, current PC, " +
                 "stack pointer, and frame size. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("List all IOP (I/O Processor) OS threads via the PINE socket. " +
                 "Returns thread ID, status, PC, stack pointer, priority, and entry point for each IOP thread. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("List all loaded IOP modules (IRX files) via the PINE socket. " +
                 "Returns name, version, text/data addresses and sizes for each module. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Read any EE or IOP CPU register by category and index via the PINE socket. " +
                 "Returns a 128-bit value as a hex string (little-endian). " +
                 "cpuSel: 0=EE, 1=IOP. " +
                 "EE categories: 0=GPR (32 regs), 1=CP0 (32 regs), 2=FPR (32 regs), 3=FCR (32 regs), 4=VU0F (33 regs, vf0–vf31+ACC), 5=VU0I (32 regs), 6=GS (19 priv regs). " +
                 "IOP categories: 0=GPR (32 regs). " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Write any EE or IOP CPU register by category and index via the PINE socket. " +
                 "The emulator must be paused. " +
                 "cpuSel: 0=EE, 1=IOP. For categories see pine_get_register. " +
                 "registerValue: hex string up to 32 characters (128-bit little-endian). " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Retrieve a paginated list of EE function symbols from the PCSX2 symbol database via PINE. " +
                 "Each entry includes address, size in bytes, and name. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Retrieve a paginated list of EE global variable symbols from the PCSX2 symbol database via PINE. " +
                 "Each entry includes address, size in bytes, and name. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("List the parameters and local variables for the EE function that contains a given address via PINE. " +
                 "Each entry includes storage type (global/register/stack), value/offset, and name. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Disassemble instructions at a given address on EE or IOP via the PINE live-memory interface. " +
                 "Unlike the offline 'disassemble' tool, this reads directly from the running PCSX2 memory. " +
                 "cpuSel: 0=EE, 1=IOP. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("Save the current PCSX2 emulator state to a named file path via the PINE socket. " +
                 "Unlike snapshot (which dumps raw memory), this saves a full .p2s save-state file " +
                 "that can be loaded back with load_state_file. " +
                 "Only available when the server was started with --pcsx2.")]
    public async Task<string> SaveStateFile(
        [Description("Absolute file path where the save state should be written (e.g. '/saves/slot1.p2s').")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SAVESTATETOFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "load_state_file")]
    [Description("Load a PCSX2 save-state file (.p2s) into the running emulator via the PINE socket. " +
                 "Unlike load_snapshot (which restores raw memory), this loads a full PCSX2-format save state. " +
                 "Only available when the server was started with --pcsx2.")]
    public async Task<string> LoadStateFile(
        [Description("Absolute path to the .p2s save-state file to load.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LOADSTATEFROMFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "extract_p2s")]
    [Description("Extract the memory dump files (EEmem.bin, IOPmem.bin) from a PCSX2 .p2s save-state archive " +
                 "into a companion directory alongside the .p2s file. " +
                 "The extraction is cached — if the .p2s file has not changed since the last extraction it " +
                 "returns immediately. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Query the PINE IPC protocol version string from PCSX2. " +
                 "Useful for checking that the connected PCSX2 supports the expected PINE extension level. " +
                 "Only available when the server was started with --pcsx2.")]
    public async Task<string> PineVersion(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PINEVERSION" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pine_save_state_slot")]
    [Description("Save the current PCSX2 emulator state to a numbered save-state slot (0–9) via PINE. " +
                 "Unlike save_state_file, this uses the built-in numbered slots managed by PCSX2. " +
                 "Only available when the server was started with --pcsx2.")]
    public async Task<string> PineSaveStateSlot(
        [Description("Save-state slot index (0–9).")] byte slot,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINESAVESTATE", StateSlot = slot }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pine_load_state_slot")]
    [Description("Load a PCSX2 save-state from a numbered slot (0–9) via PINE. " +
                 "Unlike load_state_file, this uses the built-in numbered slots managed by PCSX2. " +
                 "Only available when the server was started with --pcsx2.")]
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
    [Description("List all save-game directories on a PS2 memory card image file. " +
                 "Returns save name, file count, creation/modification timestamps, and total size for each save. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Export a single save-game directory from a PS2 memory card image to a folder on disk. " +
                 "The exported folder can be inspected, backed up, or imported into another memory card. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Import a save-game directory into a PS2 memory card image. " +
                 "The save directory must follow PS2 memory card conventions (one subdirectory per save). " +
                 "Does not require the emulator to be running.")]
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
    [Description("Delete a single save-game directory from a PS2 memory card image. " +
                 "This operation cannot be undone. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Create a new blank PS2 memory card image file (8 MB). " +
                 "If a file already exists at the path it will be overwritten with a freshly formatted card. " +
                 "Does not require the emulator to be running.")]
    public async Task<string> McFormat(
        [Description("Absolute path where the new memory card image should be created.")] string mcPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MC_FORMAT", McPath = mcPath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_read_file")]
    [Description("Read the raw bytes of a specific file inside a PS2 memory card save directory. " +
                 "Returns the file contents as a base64-encoded string. " +
                 "Useful for inspecting save-game data, icon files (ICON.SYS), thumbnails, etc. " +
                 "Use mc_list to enumerate save names and the files each save contains. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Open a PS2 HDD image file and list all APA partitions on it. " +
                 "Returns partition ID, name, type, size, creation timestamp, and file count for each partition. " +
                 "Does not require the emulator to be running.")]
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
    [Description("List all files in a specific partition on a PS2 HDD image. " +
                 "Returns file name, size, type (file/directory), and modification timestamp. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Export a single file from a PS2 HDD partition to a directory on disk. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Import a file into a specific partition on a PS2 HDD image. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Delete a partition from a PS2 HDD image. " +
                 "This operation cannot be undone. " +
                 "Does not require the emulator to be running.")]
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
    [Description("Create a new blank PS2 HDD image file with an initialised APA partition table. " +
                 "If a file already exists at the path it will be overwritten. " +
                 "Does not require the emulator to be running.")]
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
    // Progress reports — persistent session memory
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_progress_reports")]
    [Description(
        "List all progress report files saved in the memories folder. " +
        "Call this at the start of every session to discover previous work. " +
        "Returns the sanitised file names (without .md extension) of all reports.")]
    public Task<string> ListProgressReports()
    {
        var names = reports.ListReports();
        if (names.Count == 0)
            return Task.FromResult(WithRules($"No progress reports found in: {reports.MemoriesPath}"));

        var sb = new StringBuilder();
        sb.AppendLine($"Progress reports in {reports.MemoriesPath}:");
        foreach (var name in names)
            sb.AppendLine($"  {name}");
        return Task.FromResult(WithRules(sb.ToString().TrimEnd()));
    }

    [McpServerTool(Name = "get_progress_report")]
    [Description(
        "Read a previously saved progress report from the memories folder. " +
        "Load this at the start of each session to resume from where you left off. " +
        "The report contains discovered addresses, structure maps, labels, and pending objectives.")]
    public Task<string> GetProgressReport(
        [Description(
            "The report name as returned by list_progress_reports " +
            "(e.g. 'socom2-player-health', 'gtavc-vehicle-struct').")] string name)
    {
        try
        {
            var content = reports.GetReport(name);
            if (content is null)
                return Task.FromResult($"Error: no progress report named '{ProgressReportManager.SanitiseName(name)}' found.");
            return Task.FromResult(WithRules(content));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "save_progress_report")]
    [Description(
        "Create or overwrite a progress report in the memories folder. " +
        "The name should be a short, descriptive summary unique to the task " +
        "(e.g. 'socom2-player-health', 'gtavc-vehicle-struct-session2'). " +
        "Save after every major milestone so future sessions can resume seamlessly. " +
        "Content should be Markdown and include: game/version, current objective, " +
        "discovered addresses with labels, structure maps, and next steps.")]
    public Task<string> SaveProgressReport(
        [Description(
            "Short unique name for this report. Use hyphens as separators. " +
            "Examples: 'socom2-player-health', 'gtavc-vehicle-struct', 'mgs3-codec-addresses'.")] string name,
        [Description(
            "Full Markdown content of the progress report. Include: game name, current objective, " +
            "all discovered addresses with their labels, structure maps, watchpoint findings, " +
            "confirmed cheat codes, and a clear list of next steps.")] string content)
    {
        try
        {
            reports.SaveReport(name, content);
            var sanitised = ProgressReportManager.SanitiseName(name);
            return Task.FromResult(WithRules(
                $"Progress report '{sanitised}' saved to {reports.MemoriesPath}/{sanitised}.md"));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "delete_progress_report")]
    [Description(
        "Delete a progress report from the memories folder. " +
        "Use only when a task is fully complete and the report is no longer needed.")]
    public Task<string> DeleteProgressReport(
        [Description("The report name to delete (as returned by list_progress_reports).")] string name)
    {
        try
        {
            bool deleted = reports.DeleteReport(name);
            return Task.FromResult(deleted
                ? WithRules($"Progress report '{ProgressReportManager.SanitiseName(name)}' deleted.")
                : $"Error: no progress report named '{ProgressReportManager.SanitiseName(name)}' found.");
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Struct / memory-map viewer
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_structs")]
    [Description("Return all user-defined PS2 memory struct definitions stored on the server. " +
                 "Each struct has a name, base address, and an ordered list of member fields with byte offsets. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Create or update a named PS2 memory struct definition with a base address. " +
                 "Use add_struct_member to add fields after creating the struct. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Remove a named PS2 memory struct definition. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Add or update a named member field in an existing PS2 memory struct definition. " +
                 "Use get_structs to browse current definitions. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Return the server-side navigation history: the list of EE addresses recently " +
                 "visited by AI commands (READMEM, DISASSEMBLE, SETBP …) and by VS Code user " +
                 "navigation events (RECORDNAV).  Entries are sorted oldest-first, newest-last. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Clear the server-side navigation history. " +
                 "Works in both internal emulator and PINE modes.")]
    public async Task<string> ClearNavHistory(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARNAVHISTORY" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "navigate_to")]
    [Description("Tell the VS Code disasm editor to jump to a specific EE virtual address. " +
                 "When the VS Code extension has 'Follow AI' mode enabled, the disasm view " +
                 "automatically scrolls to this address so the user can follow your research " +
                 "in real time.  Also records the address in the server-side navigation history. " +
                 "Call this whenever you move your focus to a new address during an investigation. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Read the historical 4-byte value at a specific EE virtual address across every " +
                 "memory snapshot in a directory tree.  Mirrors the History tab in the VS Code " +
                 "disasm editor — shows how the value at an address changed between snapshots. " +
                 "Scans all subdirectories of snapshotDir recursively for snapshot-info.json files. " +
                 "Works in both internal emulator and PINE modes.")]
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
    [Description("Scan EE main RAM (or a custom address range) for a specific value of a given data type. " +
                 "Supports numeric data types and comparison operators. " +
                 "Works in both internal emulator and PINE modes.\n\n" +
                 "dataType values: uint8, int8, uint16, int16, uint32, int32, uint64, int64, float32, float64.\n" +
                 "comparison values: eq (equal), ne (not equal), lt (less than), gt (greater than), le (≤), ge (≥).\n\n" +
                 "Returns up to 100,000 matching addresses. Narrow the range or use filter mode (pass previousAddresses) to refine results.")]
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
    [Description(
        "Read a contiguous region of EE RAM in one call and optionally search for a byte pattern within it.\n\n" +
        "Unlike read_memory (which is capped at 64 KB), this tool can read up to 32 MB in a single request " +
        "— useful for scanning large RAM windows (e.g. the full 32 MB EE main RAM at 0x00000000–0x02000000).\n\n" +
        "Returns:\n" +
        "  • 'bytes'   — the raw region as a hex string (two hex chars per byte, no spaces).\n" +
        "  • 'matches' — list of absolute EE virtual addresses where search_pattern was found " +
        "(empty list when no pattern is supplied or no match is found).\n\n" +
        "search_pattern format: comma-separated decimal byte values, e.g. '0x4D,0x57,0x6F,0x33' or " +
        "'77,87,111,51'.  Hex prefix '0x' is accepted per element.")]
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
    [Description(
        "Scan live EE RAM (or IOP RAM) for a byte pattern and return all matching addresses.\n\n" +
        "Unlike read_memory_region (which also returns raw bytes), this tool returns only the match " +
        "addresses — making it efficient for searching across the full 32 MB EE main RAM without " +
        "transferring megabytes of data.  Ideal for locating raw file buffers, magic signatures, " +
        "or any known byte sequence loaded into game memory.\n\n" +
        "Defaults to scanning all of EE main RAM (0x00000000–0x02000000, 32 MB) or IOP RAM " +
        "(0x00000000–0x00200000, 2 MB) when start/end are omitted.\n\n" +
        "In PINE/PCSX2 mode the scan is performed in 1 MB chunks to avoid large single requests.\n\n" +
        "search_pattern format: comma-separated byte values (decimal or 0x-hex), " +
        "e.g. '0x4D,0x57,0x6F,0x33' or '77,87,111,51'.")]
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
    [Description(
        "Set a write watchpoint on a range of EE RAM.\n\n" +
        "In PINE mode (PCSX2): a software watchpoint is registered — the current memory is snapshotted " +
        "and compared on each check.  Use run_until_watchpoint (blocking: resumes PCSX2 and waits until " +
        "a change is detected) or get_watchpoint_hit (non-blocking: checks once and returns immediately " +
        "whether the value has changed since the last check).\n\n" +
        "In internal-emulator mode: a hardware watchpoint fires synchronously inside EE.Step() and pauses " +
        "execution immediately; use get_watchpoint_hit or get_pc after the emulator pauses.\n\n" +
        "Returns a watchpoint ID (e.g. 'wp-0') that can be passed to clear_memory_watchpoint.\n\n" +
        "Note: only write watchpoints are currently supported.")]
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
    [Description("Remove a previously registered memory watchpoint by its ID (e.g. 'wp-0'). " +
                 "Use 'clear_all_memory_watchpoints' to remove all watchpoints at once.")]
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
    [Description("Remove every registered memory watchpoint.")]
    public async Task<string> ClearAllMemoryWatchpoints(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARALLWATCHPOINTS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "list_memory_watchpoints")]
    [Description("List all currently active memory watchpoints: ID, address, size, and access type.")]
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
    [Description(
        "Check whether any registered memory watchpoint has fired and return the details.\n\n" +
        "In PINE mode (PCSX2): non-blocking — reads current memory at all watched addresses once and " +
        "compares against the last snapshot.  Returns the first changed watchpoint with the approximate " +
        "PC at the moment the change was detected.  Use run_until_watchpoint for a blocking variant " +
        "that resumes PCSX2 and waits for a change automatically.\n\n" +
        "In internal-emulator mode: returns the most recently fired watchpoint recorded synchronously " +
        "inside EE.Step().\n\n" +
        "The watchpoint hit info is also included in the get_pc response (WatchpointHitAddress / " +
        "WatchpointHitPC / WatchpointHitId fields) so you do not always need a separate call.")]
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
    [Description(
        "Step the emulator instruction-by-instruction from the current PC to end_pc and record every " +
        "JAL / JALR call site hit along the way.\n\n" +
        "The emulator must already be paused before calling this tool. " +
        "Execution stops when end_pc is reached, max_instructions is exhausted, or another " +
        "breakpoint/watchpoint fires.\n\n" +
        "Because MIPS has branch delay slots, the call target is resolved after the delay-slot " +
        "instruction executes — the 'to' address in each entry is the actual entry point of the callee.\n\n" +
        "Returns a list of { from, to } hex address pairs.")]
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
    [Description(
        "Resume the emulator and block until the EE program counter reaches target_pc, then return " +
        "the full register state atomically at the hit.\n\n" +
        "Equivalent to: set_breakpoint(target_pc) + resume_emulator + wait + get_registers, " +
        "but performed atomically on the server side with a configurable timeout.\n\n" +
        "On timeout the emulator is paused at whatever PC it reached; an error message is returned " +
        "so the caller can still inspect state with get_registers / get_pc.")]
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
    [Description(
        "Resume PCSX2 and block until any registered memory watchpoint fires (value at the watched " +
        "address changes), then return the watchpoint hit details.\n\n" +
        "This is the PINE/PCSX2 equivalent of the internal emulator's automatic pause on a hardware " +
        "watchpoint.  Because the PINE protocol does not expose hardware watchpoints, detection is done " +
        "by polling memory every ~5 ms; the PC returned is approximate — sampled at the moment the " +
        "change is detected, which may be a few instructions after the actual write.\n\n" +
        "At least one watchpoint must be set via set_memory_watchpoint before calling this tool.\n\n" +
        "On timeout PCSX2 is paused so you can inspect state with get_registers / get_pc.")]
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
