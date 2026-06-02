using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Pad;

// PS2 controller (pad) input simulation via PINE.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Pad.cs.

[CdTool("pad_get_state", ToolDomain.Pad, ToolRisk.ReadOnly,
    "Read full input state of a pad: buttons bitmask, sticks, pressures. Risk: read-only.")]
public sealed class PadGetStateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pad slot (0-7, usually 0).")] int padSlot = 0,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PADGETSTATE", PadSlot = (byte)padSlot }, ct));
}

[CdTool("pad_set_button", ToolDomain.Pad, ToolRisk.WritesMemory,
    "Set a single button/axis to a normalized value. PRESS THEN RELEASE: set 1, advance a few " +
    "frames, then set 0 — a held value with no release never fires as a button event in-game. " +
    "Risk: writes pad state.")]
public sealed class PadSetButtonTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pad slot (0-7).")] int padSlot,
        [Description("Button index 0-25 (see legacy PadDualshock2::Inputs enum).")] int buttonIndex,
        [Description("Normalized value 0.0 (released) to 1.0 (fully pressed). Send 1 to press, then 0 to release after a few frames.")] float value,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PADSETBUTTON", PadSlot = (byte)padSlot, PadButtonIndex = (byte)buttonIndex, PadButtonValue = value }, ct));
}

[CdTool("pad_set_analog", ToolDomain.Pad, ToolRisk.WritesMemory,
    "Set raw analog stick positions (bypasses deadzone). Risk: writes pad state.")]
public sealed class PadSetAnalogTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pad slot (0-7).")] int padSlot,
        [Description("Left X (0-255, 0x7F=centre).")] int lx = 0x7F,
        [Description("Left Y (0-255).")] int ly = 0x7F,
        [Description("Right X (0-255).")] int rx = 0x7F,
        [Description("Right Y (0-255).")] int ry = 0x7F,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PADSETANALOG", PadSlot = (byte)padSlot, PadLx = (byte)lx, PadLy = (byte)ly, PadRx = (byte)rx, PadRy = (byte)ry }, ct));
}

[CdTool("pad_set_state", ToolDomain.Pad, ToolRisk.WritesMemory,
    "Atomically replace the complete pad input state (suitable for TAS). Risk: writes pad state.")]
public sealed class PadSetStateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pad slot (0-7).")] int padSlot,
        [Description("Left X (0-255).")] int lx = 0x7F,
        [Description("Left Y (0-255).")] int ly = 0x7F,
        [Description("Right X (0-255).")] int rx = 0x7F,
        [Description("Right Y (0-255).")] int ry = 0x7F,
        [Description("16 pressure values 0-255 comma-separated (Up,Right,Down,Left,Triangle,Circle,Cross,Square,Select,Start,L1,L2,R1,R2,L3,R3).")] string pressures = "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0",
        [Description("Button bitmask (default 0xFFFF = all released).")] uint buttons = 0xFFFF,
        CancellationToken ct = default)
    {
        byte[] parsed;
        try { parsed = pressures.Split(',').Select(s => byte.Parse(s.Trim())).ToArray(); }
        catch { return CdText.Error("pressures must be 16 comma-separated byte values 0-255."); }
        if (parsed.Length != 16) return CdText.Error("pressures must contain exactly 16 values.");
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "PADSETSTATE", PadSlot = (byte)padSlot, PadButtons = buttons,
            PadLx = (byte)lx, PadLy = (byte)ly, PadRx = (byte)rx, PadRy = (byte)ry,
            PadPressures = parsed.ToList(),
        }, ct));
    }
}

[CdTool("pad_get_type", ToolDomain.Pad, ToolRisk.ReadOnly,
    "Query the controller type installed in a pad slot (NotConnected/DualShock2/Guitar/...). Risk: read-only.")]
public sealed class PadGetTypeTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pad slot (0-7).")] int padSlot = 0,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PADGETTYPE", PadSlot = (byte)padSlot }, ct));
}
