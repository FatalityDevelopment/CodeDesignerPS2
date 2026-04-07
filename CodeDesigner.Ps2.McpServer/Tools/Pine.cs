using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Prompts;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
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

    // ── PINE hardware watchpoints (MsgAddWatch/RemoveWatch/ListWatches/ClearAllWatches) ──

    [McpServerTool(Name = "pine_add_watch")]
    [Description("Add a hardware memory watchpoint in PCSX2 via the PINE protocol (opcode 0x2E). " +
                 "Backed by CBreakPoints MemCheck (MEMCHECK_BREAK). JIT-only — no effect under the interpreter or HLE. " +
                 "PINE mode required (connect to PCSX2 with PINE enabled).")]
    public async Task<string> PineAddWatch(
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

    [McpServerTool(Name = "pine_remove_watch")]
    [Description("Remove a hardware memory watchpoint from PCSX2 via the PINE protocol (opcode 0x2F). " +
                 "Returns an error if no matching range is found. PINE mode required.")]
    public async Task<string> PineRemoveWatch(
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

    [McpServerTool(Name = "pine_list_watches")]
    [Description("List all active hardware memory watchpoints in PCSX2 via the PINE protocol (opcode 0x30). " +
                 "Each entry shows CPU, start address, end address, and condition. PINE mode required.")]
    public async Task<string> PineListWatches(
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

    [McpServerTool(Name = "pine_clear_all_watches")]
    [Description("Clear all active hardware memory watchpoints on all CPUs in PCSX2 via the PINE protocol (opcode 0x31). " +
                 "PINE mode required.")]
    public async Task<string> PineClearAllWatches(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINECLEARALLWATCHES" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pine_save_screenshot")]
    [Description("Take a screenshot of the game's screen in PCSX2 via the PINE protocol (opcode 0x32). " +
                 "PINE mode required. PCSX2 saves the screenshot to its configured screenshots directory.")]
    public async Task<string> PineSaveScreenshot(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PINESAVESCREENSHOT" }, cancellationToken);
        return SimpleMessage(json);
    }
}
