using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Pcsx2;

// PCSX2 PINE-mode tools (debugging, symbols, registers, state, watches).
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Pcsx2.cs.

[CdTool("pcsx2_get_paths", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Return the absolute on-disk directories PCSX2 is using for this session, captured from " +
    "the emu log on connection. Use this whenever you need to read or write a PCSX2-managed " +
    "file (memory cards, save states, screenshots, cheats, patches, logs, textures, ISO covers, " +
    "per-game .ini overrides, etc.) instead of guessing or hardcoding %DOCUMENTS%\\PCSX2.\n" +
    "Returned keys (each value is an absolute directory):\n" +
    "  pcsx2.path.dataRoot         — root of the user's PCSX2 data tree (parent of inis/, memcards/, sstates/, ...).\n" +
    "  pcsx2.path.bios             — BIOS image search path.\n" +
    "  pcsx2.path.snapshots        — in-game screenshot captures.\n" +
    "  pcsx2.path.savestates       — .p2s save-state files.\n" +
    "  pcsx2.path.memoryCards      — virtual PS2 memory card images (.ps2).\n" +
    "  pcsx2.path.logs             — emulog.txt and other diagnostic logs.\n" +
    "  pcsx2.path.cheats           — per-game .pnach cheat files.\n" +
    "  pcsx2.path.patches          — built-in compatibility patches.\n" +
    "  pcsx2.path.covers           — cover-art images shown in the game list.\n" +
    "  pcsx2.path.gameSettings     — per-disc .ini overrides keyed by serial+CRC.\n" +
    "  pcsx2.path.resources        — bundled, read-only resource tree shipped with PCSX2.\n" +
    "  pcsx2.path.userResources    — user-overridable copy of the Resources tree.\n" +
    "  pcsx2.path.cache            — shader/program/state caches (regenerated as needed).\n" +
    "  pcsx2.path.textures         — HD-texture replacement / dump folder.\n" +
    "  pcsx2.path.inputProfiles    — saved controller mapping profiles.\n" +
    "  pcsx2.path.videos           — GS-dump / video capture output.\n" +
    "  pcsx2.path.debuggerLayouts  — saved debugger window layouts.\n" +
    "  pcsx2.path.debuggerSettings — per-game persisted debugger state.\n" +
    "  pcsx2.path.discovered       — JSON array of every other path-shaped string seen in the emu log (diagnostics only).\n" +
    "Values are populated automatically the first time PCSX2 connects in a session; empty " +
    "values mean PCSX2 has not yet been launched (or the emu log line for that key has not " +
    "been seen). Risk: read-only.")]
public sealed class Pcsx2GetPathsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(
            new { Command = "GETALLSETTINGS", SettingPrefix = "pcsx2.path." }, ct));
}

[CdTool("pcsx2_get_symbol", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Look up EE function symbol name at an address via PINE. Risk: read-only.")]
public sealed class Pcsx2GetSymbolTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("EE virtual address (hex/decimal).")] string address, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETSYMBOL", Address = a.Value }, ct));
    }
}

[CdTool("pcsx2_get_symbol_by_name", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Look up an EE symbol address and size by demangled name via PINE. Risk: read-only.")]
public sealed class Pcsx2GetSymbolByNameTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Demangled symbol name.")] string symbolName, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETSYMBOLBYNAME", SymbolName = symbolName }, ct));
}

[CdTool("pcsx2_get_call_stack", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Walk the EE call stack and return each frame (entry, PC, SP, symbol). Requires the emulator " +
    "PAUSED (hit a breakpoint first) — meaningless on a running game. Risk: read-only.")]
public sealed class Pcsx2GetCallStackTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETSTACK" }, ct));
}

[CdTool("pcsx2_get_iop_threads", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "List all IOP OS threads via PINE. Risk: read-only.")]
public sealed class Pcsx2GetIopThreadsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdFormat.Threads(await bridge.SendAsync(new { Command = "GETIOPTHREADS" }, ct));
}

[CdTool("pcsx2_get_modules", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "List loaded IOP modules (IRX) via PINE. Risk: read-only.")]
public sealed class Pcsx2GetModulesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETMODULES" }, ct));
}

[CdTool("pcsx2_get_register", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Read any EE/IOP register by category and index via PINE. Risk: read-only.")]
public sealed class Pcsx2GetRegisterTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CPU: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Register category.")] byte registerCategory,
        [Description("Register index within category.")] int registerIndex,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETREGISTER", CpuSel = cpuSel, RegisterCategory = registerCategory, RegisterIndex = registerIndex }, ct));
}

[CdTool("pcsx2_set_register", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Write any EE/IOP register by category and index via PINE. Risk: writes register.")]
public sealed class Pcsx2SetRegisterTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CPU: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Register category.")] byte registerCategory,
        [Description("Register index within category.")] int registerIndex,
        [Description("Value as hex string (up to 32 hex chars).")] string registerValue,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINESETREG", CpuSel = cpuSel, RegisterCategory = registerCategory, RegisterIndex = registerIndex, RegisterValue = registerValue }, ct));
}

[CdTool("pcsx2_list_functions", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Paginated list of EE function symbols from PCSX2's symbol DB. Risk: read-only.")]
public sealed class Pcsx2ListFunctionsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Offset.")] uint offset = 0,
        [Description("Max results (1-65535, default 256).")] ushort maxCount = 256,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LISTFUNCTIONS", Offset = offset, MaxCount = maxCount }, ct));
}

[CdTool("pcsx2_list_globals", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Paginated list of EE global variable symbols from PCSX2's symbol DB. Risk: read-only.")]
public sealed class Pcsx2ListGlobalsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Offset.")] uint offset = 0,
        [Description("Max results (1-65535, default 256).")] ushort maxCount = 256,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LISTGLOBALS", Offset = offset, MaxCount = maxCount }, ct));
}

[CdTool("pcsx2_get_locals", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "List parameters + locals for the EE function containing an address (DWARF). Risk: read-only.")]
public sealed class Pcsx2GetLocalsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("EE virtual address inside the function (hex/decimal).")] string address, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETLOCALS", Address = a.Value }, ct));
    }
}

[CdTool("pcsx2_disassemble", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Disassemble instructions on EE/IOP via PINE live-memory. Risk: read-only.")]
public sealed class Pcsx2DisassembleTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CPU: 0=EE, 1=IOP.")] byte cpuSel,
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Instruction count (1-1000, default 32).")] ushort count = 32,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "PINEDISASSEMBLE", CpuSel = cpuSel, Address = a.Value, Length = (int)count }, ct));
    }
}

[CdTool("pcsx2_version", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Query the PINE IPC protocol version string. Risk: read-only.")]
public sealed class Pcsx2VersionTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINEVERSION" }, ct));
}

[CdTool("pcsx2_save_state_slot", ToolDomain.Pcsx2, ToolRisk.WritesDisk,
    "Save PCSX2 emulator state to a numbered slot (0-9). Risk: writes disk.")]
public sealed class Pcsx2SaveStateSlotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Slot index (0-9).")] byte slot, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINESAVESTATE", StateSlot = slot }, ct));
}

[CdTool("pcsx2_load_state_slot", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Load PCSX2 save state from a numbered slot (0-9). Risk: overwrites emulator state.")]
public sealed class Pcsx2LoadStateSlotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Slot index (0-9).")] byte slot, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINELOADSTATE", StateSlot = slot }, ct));
}

[CdTool("pcsx2_save_state_file", ToolDomain.Pcsx2, ToolRisk.WritesDisk,
    "Save PCSX2 state to a named .p2s file. Risk: writes disk.")]
public sealed class Pcsx2SaveStateFileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Path to .p2s file.")] string filePath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SAVESTATETOFILE", FilePath = filePath }, ct));
}

[CdTool("pcsx2_load_state_file", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Load a PCSX2 .p2s state file. Risk: overwrites emulator state.")]
public sealed class Pcsx2LoadStateFileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Path to .p2s file.")] string filePath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LOADSTATEFROMFILE", FilePath = filePath }, ct));
}

[CdTool("pcsx2_extract_p2s", ToolDomain.Pcsx2, ToolRisk.WritesDisk,
    "Extract EEmem.bin / IOPmem.bin from a .p2s archive. Risk: writes disk.")]
public sealed class Pcsx2ExtractP2sTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Path to .p2s file.")] string filePath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "EXTRACTP2S", FilePath = filePath }, ct));
}

[CdTool("pcsx2_add_watch", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Add a hardware memory watchpoint (PINE opcode 0x2E). JIT-only. Risk: writes debug state.")]
public sealed class Pcsx2AddWatchTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Inclusive start (hex/decimal).")] string start,
        [Description("Exclusive end (hex/decimal).")] string end,
        [Description("Cond: 1=read, 2=write, 3=read+write (default 3).")] byte cond = 3,
        [Description("CPU: 0=EE, 1=IOP.")] byte cpu = 0,
        CancellationToken ct = default)
    {
        var s = CdText.TryParseAddress(start); if (s is null) return CdText.Error($"invalid start '{start}'.");
        var e = CdText.TryParseAddress(end);   if (e is null) return CdText.Error($"invalid end '{end}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "PINEADDWATCH", CpuSel = cpu, Address = s.Value, EndAddress = e.Value, WatchCond = cond }, ct));
    }
}

[CdTool("pcsx2_remove_watch", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Remove a hardware memory watchpoint (PINE opcode 0x2F). Risk: writes debug state.")]
public sealed class Pcsx2RemoveWatchTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Inclusive start (hex/decimal).")] string start,
        [Description("Exclusive end (hex/decimal).")] string end,
        [Description("CPU: 0=EE, 1=IOP.")] byte cpu = 0,
        CancellationToken ct = default)
    {
        var s = CdText.TryParseAddress(start); if (s is null) return CdText.Error($"invalid start '{start}'.");
        var e = CdText.TryParseAddress(end);   if (e is null) return CdText.Error($"invalid end '{end}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "PINEREMOVEWATCH", CpuSel = cpu, Address = s.Value, EndAddress = e.Value }, ct));
    }
}

[CdTool("pcsx2_list_watches", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "List active hardware memory watchpoints (PINE opcode 0x30). Risk: read-only.")]
public sealed class Pcsx2ListWatchesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CPU filter: 0=EE only, 1=IOP only, 255=both (default).")] byte cpuSel = 0xFF,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINELISTWATCHES", CpuSel = cpuSel }, ct));
}

[CdTool("pcsx2_clear_all_watches", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Clear all hardware watchpoints on all CPUs (PINE opcode 0x31). Risk: writes debug state.")]
public sealed class Pcsx2ClearAllWatchesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINECLEARALLWATCHES" }, ct));
}

[CdTool("pcsx2_save_screenshot", ToolDomain.Pcsx2, ToolRisk.WritesDisk,
    "Preferred way to preview the screen: take a screenshot via PINE (saved to PCSX2's " +
    "configured screenshot directory). Use this over get_gs_frame for routine 'what's on " +
    "screen' checks. Risk: writes disk.")]
public sealed class Pcsx2SaveScreenshotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINESAVESCREENSHOT" }, ct));
}

[CdTool("pcsx2_reset", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Send a CPU reset to the emulator (no BIOS reload). Risk: resets emulator state.")]
public sealed class Pcsx2ResetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "RESET" }, ct));
}

[CdTool("pcsx2_get_fps", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Read current emulated framerate via PINE. Risk: read-only.")]
public sealed class Pcsx2GetFpsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETFPS" }, ct));
}

[CdTool("pcsx2_set_limiter_mode", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Set speed limiter mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited. Risk: changes timing.")]
public sealed class Pcsx2SetLimiterModeTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.")] byte mode, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SETLIMITER", LimiterMode = mode }, ct));
}

[CdTool("pcsx2_set_audio_muted", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Mute or unmute PCSX2's audio output (PINE opcode 0x45, same as the Toggle Mute hotkey). " +
    "Replies with the resulting state; PCSX2's 'Mute All Sound' setting blocks unmuting. Risk: changes emulator audio.")]
public sealed class Pcsx2SetAudioMutedTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("True to mute, false to unmute.")] bool muted, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINESETAUDIOMUTED", AudioMuted = muted }, ct));
}

[CdTool("pcsx2_get_audio_state", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "Read whether PCSX2's audio output is muted (PINE opcode 0x46). Risk: read-only.")]
public sealed class Pcsx2GetAudioStateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PINEGETAUDIOSTATE" }, ct));
}

[CdTool("pcsx2_frame_advance", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Advance a fixed number of video frames then pause. Risk: advances run state.")]
public sealed class Pcsx2FrameAdvanceTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync([Description("Frame count (1-255).")] byte frameCount, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "FRAMEADVANCE", FrameCount = frameCount }, ct));
}

[CdTool("pcsx2_step_into", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Step into the next EE instruction (follows calls). Risk: advances run state.")]
public sealed class Pcsx2StepIntoTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "STEPINTO" }, ct));
}

[CdTool("pcsx2_step_over", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Step over the next EE instruction (skips into calls). Risk: advances run state.")]
public sealed class Pcsx2StepOverTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "STEPOVER" }, ct));
}

[CdTool("pcsx2_step_out", ToolDomain.Pcsx2, ToolRisk.WritesMemory,
    "Step out of the current EE subroutine (run to JR $ra). Risk: advances run state.")]
public sealed class Pcsx2StepOutTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "STEPOUT" }, ct));
}

[CdTool("pcsx2_list_breakpoints", ToolDomain.Pcsx2, ToolRisk.ReadOnly,
    "List active PC breakpoints in PCSX2 via PINE. Risk: read-only.")]
public sealed class Pcsx2ListBreakpointsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CPU: 0=EE, 1=IOP, 255=both (default).")] byte cpuSel = 0xFF,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LISTBP", CpuSel = cpuSel }, ct));
}
