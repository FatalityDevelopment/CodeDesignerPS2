using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Symbol / debug info (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_get_symbol")]
    [DynamicDescription("pcsx2_get_symbol_desc")]
    [Description("Look up the EE function symbol name for a given virtual address via PINE. Only available when PCSX2 has a symbol database loaded.")]
    public async Task<string> Pcsx2GetSymbol(
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

    [McpServerTool(Name = "pcsx2_get_symbol_by_name")]
    [DynamicDescription("pcsx2_get_symbol_by_name_desc")]
    [Description("Look up an EE symbol address and size by its demangled name via PINE. Only available when PCSX2 has a symbol database loaded.")]
    public async Task<string> Pcsx2GetSymbolByName(
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

    [McpServerTool(Name = "pcsx2_get_call_stack")]
    [DynamicDescription("pcsx2_get_call_stack_desc")]
    [Description("Walk the EE call stack and return each frame's entry point, current PC, stack pointer, and resolved symbol name (if available).")]
    public async Task<string> Pcsx2GetCallStack(CancellationToken cancellationToken)
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

    [McpServerTool(Name = "pcsx2_get_iop_threads")]
    [DynamicDescription("pcsx2_get_iop_threads_desc")]
    [Description("List all IOP (I/O Processor) OS threads via the PINE socket. Returns thread ID, status, priority, and entry point for each thread.")]
    public async Task<string> Pcsx2GetIopThreads(CancellationToken cancellationToken)
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

    [McpServerTool(Name = "pcsx2_get_modules")]
    [DynamicDescription("pcsx2_get_modules_desc")]
    [Description("List all loaded IOP modules (IRX files) via the PINE socket. Returns name, version, start address, and size for each module.")]
    public async Task<string> Pcsx2GetModules(CancellationToken cancellationToken)
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

    [McpServerTool(Name = "pcsx2_get_register")]
    [DynamicDescription("pcsx2_get_register_desc")]
    [Description("Read any EE or IOP CPU register by category and index via the PINE socket. Returns the 64-bit or 128-bit value depending on register type.")]
    public async Task<string> Pcsx2GetRegister(
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

    [McpServerTool(Name = "pcsx2_set_register")]
    [DynamicDescription("pcsx2_set_register_desc")]
    [Description("Write any EE or IOP CPU register by category and index via the PINE socket. The value is supplied as a 64-bit unsigned integer.")]
    public async Task<string> Pcsx2SetRegister(
        [Description("CPU selector: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Register category (see pcsx2_get_register description).")] byte registerCategory,
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

    [McpServerTool(Name = "pcsx2_list_functions")]
    [DynamicDescription("pcsx2_list_functions_desc")]
    [Description("Retrieve a paginated list of EE function symbols from the PCSX2 symbol database via PINE. Returns function name, address, and size.")]
    public async Task<string> Pcsx2ListFunctions(
        [Description("Pagination offset (0-based index of the first result to return).")] uint offset = 0,
        [Description("Maximum number of results to return (1–65535, default 256).")] ushort maxCount = 256,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LISTFUNCTIONS", Offset = offset, MaxCount = maxCount },
            cancellationToken);
        return FormatPineSymbols(json, "Functions", "PineSymbols");
    }

    [McpServerTool(Name = "pcsx2_list_globals")]
    [DynamicDescription("pcsx2_list_globals_desc")]
    [Description("Retrieve a paginated list of EE global variable symbols from the PCSX2 symbol database via PINE. Returns variable name, address, and size.")]
    public async Task<string> Pcsx2ListGlobals(
        [Description("Pagination offset (0-based index of the first result to return).")] uint offset = 0,
        [Description("Maximum number of results to return (1–65535, default 256).")] ushort maxCount = 256,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LISTGLOBALS", Offset = offset, MaxCount = maxCount },
            cancellationToken);
        return FormatPineSymbols(json, "Globals", "PineSymbols");
    }

    [McpServerTool(Name = "pcsx2_get_locals")]
    [DynamicDescription("pcsx2_get_locals_desc")]
    [Description("List the parameters and local variables for the EE function that contains a given virtual address. Uses DWARF debug information loaded by PCSX2.")]
    public async Task<string> Pcsx2GetLocals(
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

    [McpServerTool(Name = "pcsx2_disassemble")]
    [DynamicDescription("pcsx2_disassemble_desc")]
    [Description("Disassemble instructions at a given address on EE or IOP via the PINE live-memory interface. Returns a block of disassembled instructions.")]
    public async Task<string> Pcsx2Disassemble(
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
    // Direct PINE protocol commands (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_version")]
    [DynamicDescription("pcsx2_version_desc")]
    [Description("Query the PINE IPC protocol version string from PCSX2. Useful for checking that the connected PCSX2 build supports required features.")]
    public async Task<string> Pcsx2Version(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PINEVERSION" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_save_state_slot")]
    [DynamicDescription("pcsx2_save_state_slot_desc")]
    [Description("Save the current PCSX2 emulator state to a numbered save-state slot (0–9) via PINE. Slots are persistent across PCSX2 sessions.")]
    public async Task<string> Pcsx2SaveStateSlot(
        [Description("Save-state slot index (0–9).")] byte slot,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINESAVESTATE", StateSlot = slot }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_load_state_slot")]
    [DynamicDescription("pcsx2_load_state_slot_desc")]
    [Description("Load a PCSX2 save-state from a numbered slot (0–9) via PINE. Unlike pcsx2_load_state_file, slot-based saves are managed entirely by PCSX2.")]
    public async Task<string> Pcsx2LoadStateSlot(
        [Description("Save-state slot index (0–9).")] byte slot,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINELOADSTATE", StateSlot = slot }, cancellationToken);
        return SimpleMessage(json);
    }

    // ── PINE hardware watchpoints ──────────────────────────────────────────────

    [McpServerTool(Name = "pcsx2_add_watch")]
    [DynamicDescription("pcsx2_add_watch_desc")]
    [Description("Add a hardware memory watchpoint in PCSX2 via the PINE protocol (opcode 0x2E). " +
                 "Backed by CBreakPoints MemCheck (MEMCHECK_BREAK). JIT-only — no effect under the interpreter or HLE. " +
                 "PINE mode required (connect to PCSX2 with PINE enabled).")]
    public async Task<string> Pcsx2AddWatch(
        [Description("Inclusive start address of the memory range to watch (hex or decimal).")] string start,
        [Description("Exclusive end address of the memory range to watch (hex or decimal).")] string end,
        [Description("Condition bitmask: 1 = read, 2 = write, 3 = read+write (default 3).")] byte cond = 3,
        [Description("CPU to watch: 0 = EE (default), 1 = IOP.")] byte cpu = 0,
        CancellationToken cancellationToken = default)
    {
        uint? parsedStart = TryParseAddress(start);
        uint? parsedEnd   = TryParseAddress(end);
        if (parsedStart is null) return $"Error: invalid start address '{start}'.";
        if (parsedEnd   is null) return $"Error: invalid end address '{end}'.";
        if (cond == 0 || cond > 3) return "Error: cond must be 1 (read), 2 (write), or 3 (read+write).";

        var json = await bridge.SendAsync(
            new { Command = "PINEADDWATCH", CpuSel = cpu, Address = parsedStart.Value, EndAddress = parsedEnd.Value, WatchCond = cond },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_remove_watch")]
    [DynamicDescription("pcsx2_remove_watch_desc")]
    [Description("Remove a hardware memory watchpoint from PCSX2 via the PINE protocol (opcode 0x2F). " +
                 "Returns an error if no matching range is found. PINE mode required.")]
    public async Task<string> Pcsx2RemoveWatch(
        [Description("Inclusive start address of the range to remove (hex or decimal).")] string start,
        [Description("Exclusive end address of the range to remove (hex or decimal).")] string end,
        [Description("CPU the watchpoint was registered on: 0 = EE (default), 1 = IOP.")] byte cpu = 0,
        CancellationToken cancellationToken = default)
    {
        uint? parsedStart = TryParseAddress(start);
        uint? parsedEnd   = TryParseAddress(end);
        if (parsedStart is null) return $"Error: invalid start address '{start}'.";
        if (parsedEnd   is null) return $"Error: invalid end address '{end}'.";

        var json = await bridge.SendAsync(
            new { Command = "PINEREMOVEWATCH", CpuSel = cpu, Address = parsedStart.Value, EndAddress = parsedEnd.Value },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_list_watches")]
    [DynamicDescription("pcsx2_list_watches_desc")]
    [Description("List all active hardware memory watchpoints in PCSX2 via the PINE protocol (opcode 0x30). " +
                 "Each entry shows CPU, start address, end address, and condition. PINE mode required.")]
    public async Task<string> Pcsx2ListWatches(
        [Description("CPU filter: 0 = EE only, 1 = IOP only, 255 = both (default).")] byte cpuSel = 0xFF,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PINELISTWATCHES", CpuSel = cpuSel },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("PineWatches", out var watches) ||
                watches.ValueKind != JsonValueKind.Array)
                return "No hardware watchpoints active.";

            int count = watches.GetArrayLength();
            if (count == 0) return "No hardware watchpoints active.";

            var sb = new StringBuilder();
            sb.AppendLine($"Active PINE hardware watchpoints ({count}):");
            foreach (var w in watches.EnumerateArray())
            {
                string cpu   = w.TryGetProperty("Cpu",   out var cp) ? cp.GetString() ?? "?" : "?";
                string wStart = w.TryGetProperty("Start", out var st) ? st.GetString() ?? "?" : "?";
                string wEnd   = w.TryGetProperty("End",   out var en) ? en.GetString() ?? "?" : "?";
                string wCond  = w.TryGetProperty("Cond",  out var co) ? co.GetString() ?? "?" : "?";
                sb.AppendLine($"  [{cpu}] {wStart}–{wEnd}  cond={wCond}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "pcsx2_clear_all_watches")]
    [DynamicDescription("pcsx2_clear_all_watches_desc")]
    [Description("Clear all active hardware memory watchpoints on all CPUs in PCSX2 via the PINE protocol (opcode 0x31). " +
                 "PINE mode required.")]
    public async Task<string> Pcsx2ClearAllWatches(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINECLEARALLWATCHES" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_save_screenshot")]
    [DynamicDescription("pcsx2_save_screenshot_desc")]
    [Description("Take a screenshot of the game's screen in PCSX2 via the PINE protocol (opcode 0x32). " +
                 "PINE mode required. PCSX2 saves the screenshot to its configured screenshots directory.")]
    public async Task<string> Pcsx2SaveScreenshot(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINESAVESCREENSHOT" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Emulator reset (moved from EmulatorState.cs)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_reset")]
    [DynamicDescription("pcsx2_reset_desc")]
    [Description("Send a CPU reset signal to the PS2 emulator (PINE) or reset the internal scheduler. Restarts game execution without reloading the BIOS ROM.")]
    public async Task<string> Pcsx2Reset(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESET" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Framerate / limiter / frame advance (PINE mode, moved from EmulatorState.cs)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_get_fps")]
    [DynamicDescription("pcsx2_get_fps_desc")]
    [Description("Read the current emulated framerate from PCSX2 via the PINE socket. Only available in PINE mode with a game running.")]
    public async Task<string> Pcsx2GetFps(CancellationToken cancellationToken)
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

    [McpServerTool(Name = "pcsx2_set_limiter_mode")]
    [DynamicDescription("pcsx2_set_limiter_mode_desc")]
    [Description("Set the PCSX2 speed limiter mode via the PINE socket. mode: 0=Nominal (default 60 fps), 1=Limit, 2=Turbo, 3=Slow-motion.")]
    public async Task<string> Pcsx2SetLimiterMode(
        [Description("Limiter mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.")] byte mode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SETLIMITER", LimiterMode = mode }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_frame_advance")]
    [DynamicDescription("pcsx2_frame_advance_desc")]
    [Description("Advance the PCSX2 emulator by a fixed number of video frames then pause. Useful for frame-by-frame analysis of game animations or timing-sensitive behaviour.")]
    public async Task<string> Pcsx2FrameAdvance(
        [Description("Number of frames to advance (1–255).")] byte frameCount,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "FRAMEADVANCE", FrameCount = frameCount }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Step variants and breakpoint listing (moved from Execution.cs)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_step_into")]
    [DynamicDescription("pcsx2_step_into_desc")]
    [Description("Step into the next PS2 EE instruction (follows calls into subroutines). In PINE/PCSX2 mode this issues a CPU step via the PINE debug interface.")]
    public async Task<string> Pcsx2StepInto(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPINTO" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_step_over")]
    [DynamicDescription("pcsx2_step_over_desc")]
    [Description("Step over the next PS2 EE instruction (skips into subroutines, executing them fully). Sets a temporary breakpoint at PC+8 (after the delay slot) and resumes.")]
    public async Task<string> Pcsx2StepOver(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOVER" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_step_out")]
    [DynamicDescription("pcsx2_step_out_desc")]
    [Description("Step out of the current PS2 EE subroutine, running until the next return instruction (JR $ra) or equivalent.")]
    public async Task<string> Pcsx2StepOut(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOUT" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_list_breakpoints")]
    [DynamicDescription("pcsx2_list_breakpoints_desc")]
    [Description("List all active PC breakpoints set in PCSX2 via the PINE socket. cpuSel: 0=EE only (default), 1=IOP only, 2=both.")]
    public async Task<string> Pcsx2ListBreakpoints(
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
    // Save-state file management (moved from Snapshots.cs)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_save_state_file")]
    [DynamicDescription("pcsx2_save_state_file_desc")]
    [Description("Save the current PCSX2 emulator state to a named file path via the PINE socket. The resulting .p2s file can be loaded with pcsx2_load_state_file.")]
    public async Task<string> Pcsx2SaveStateFile(
        [Description("Absolute file path where the save state should be written (e.g. '/saves/slot1.p2s').")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SAVESTATETOFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_load_state_file")]
    [DynamicDescription("pcsx2_load_state_file_desc")]
    [Description("Load a PCSX2 save-state file (.p2s) into the running emulator via the PINE socket. Restores CPU registers, memory, and hardware state.")]
    public async Task<string> Pcsx2LoadStateFile(
        [Description("Absolute path to the .p2s save-state file to load.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LOADSTATEFROMFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_extract_p2s")]
    [DynamicDescription("pcsx2_extract_p2s_desc")]
    [Description("Extract the memory dump files (EEmem.bin, IOPmem.bin) from a PCSX2 .p2s save-state archive to a directory for offline analysis.")]
    public async Task<string> Pcsx2ExtractP2s(
        [Description("Absolute path to the .p2s PCSX2 save-state file to extract.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "EXTRACTP2S", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }
}
