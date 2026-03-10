using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CodeDesigner.Mips64LE.McpServer.Tools;

/// <summary>
/// MCP tools that expose the CodeDesigner MIPS64 Emotion Engine socket server
/// capabilities to GitHub Copilot.  Every tool forwards its request to the
/// socket server running on localhost:8765 and returns a human-readable result.
/// </summary>
[McpServerToolType]
public sealed class Mips64LeTools(SocketBridgeClient bridge)
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
            return $"PC = 0x{pc:X8} | Paused = {paused}";
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

    // -------------------------------------------------------------------------
    // Memory scanning
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
                 "(heuristic: ADDIU $sp,$sp,-N prologue pattern). Returns address and auto-generated label.")]
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

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "search")]
    [Description("Search live PS2 emulator memory or a disassembly listing for a text or regex pattern. " +
                 "SearchMode: 'text' (default) or 'regex'. Returns matching instruction addresses and context.")]
    public async Task<string> Search(
        [Description("Text or regex pattern to search for.")] string query,
        [Description("Virtual start address (hex or decimal).")] string address,
        [Description("Number of bytes to search.")] int length,
        [Description("Search mode: 'text' (default) or 'regex'.")] string searchMode = "text",
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
    [Description("Resume PS2 emulator execution after a pause or breakpoint hit.")]
    public async Task<string> ResumeEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESUME" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "set_breakpoint")]
    [Description("Set a breakpoint at a virtual address in the PS2 emulator. " +
                 "Execution pauses when the CPU reaches this address.")]
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

    [McpServerTool(Name = "step")]
    [Description("Execute exactly one EE instruction while keeping the emulator paused. " +
                 "The emulator must already be paused (use pause_emulator or set a breakpoint first). " +
                 "Returns the new program counter and pause state after the step.")]
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
            return $"Stepped → PC = 0x{pc:X8} | Paused = {paused}";
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

    // -------------------------------------------------------------------------
    // Register and memory writes
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Label management
    // -------------------------------------------------------------------------

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
    // Thread inspection
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_threads")]
    [Description("Enumerate PS2 EE OS threads by walking the kernel thread control block (TCB) pool. " +
                 "Returns thread ID, status (Running/Ready/Waiting/Suspended/Dormant), " +
                 "current PC, stack pointer, priority, and entry point for each thread found. " +
                 "If no OS threads are found a single synthetic entry for the live CPU state is returned.")]
    public async Task<string> GetThreads(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETTHREADS" }, cancellationToken);
        return FormatThreads(json);
    }

    // -------------------------------------------------------------------------
    // Snapshot
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "snapshot")]
    [Description("Save a full memory snapshot of the running PS2 emulator to a directory on disk. " +
                 "Writes four binary files: EEmem.bin (32 MB EE RAM), BIOSrom.bin (4 MB BIOS), " +
                 "Scratchpad.bin (16 KB), and IOPmem.bin (IOP RAM). " +
                 "The output directory is created if it does not exist.")]
    public async Task<string> Snapshot(
        [Description("Absolute path to the output directory where snapshot files will be written.")] string outputPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SNAPSHOT", OutputPath = outputPath },
            cancellationToken);

        return FormatSnapshot(json);
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

            // COP0 Status / Cause / EPC
            if (root.TryGetProperty("COP0_Status", out var statusProp))
                sb.AppendLine($"\n=== COP0 ===\n  Status = 0x{statusProp.GetUInt32():X8}");
            if (root.TryGetProperty("COP0_Cause",  out var causeProp))
                sb.AppendLine($"  Cause  = 0x{causeProp.GetUInt32():X8}");
            if (root.TryGetProperty("COP0_EPC",    out var epcProp))
                sb.AppendLine($"  EPC    = 0x{epcProp.GetUInt64():X16}");
            if (root.TryGetProperty("COP0_Count",  out var cntProp))
                sb.AppendLine($"  Count  = {cntProp.GetUInt32()}");

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

                sb.Append($"  0x{addr,-10}  {label}");
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

            if (!root.TryGetProperty("Strings", out var arr))
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
                sb.AppendLine($"  0x{addr:X8}  [{fld}]");
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
}
