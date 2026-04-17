using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Pad / controller input (PINE opcodes 0x33–0x37)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pad_get_state")]
    [DynamicDescription("pad_get_state_desc")]
    [Description("Read the full input state of a PS2 controller pad. Returns buttons bitmask, analog stick positions, and 16 pressure values. Requires PCSX2 PINE mode.")]
    public async Task<string> PadGetState(
        [Description("Unified pad slot index (0–7, usually 0 for player 1).")] int padSlot = 0,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PADGETSTATE", PadSlot = (byte)padSlot },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("PadState", out var state))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"Pad {padSlot} state:");

            uint buttons = state.TryGetProperty("buttons", out var b) ? b.GetUInt32() : 0xFFFFu;
            sb.AppendLine($"  Buttons bitmask: 0x{buttons:X4} (clear bit = pressed)");

            byte lx = state.TryGetProperty("lx", out var lxp) ? lxp.GetByte() : (byte)0x7F;
            byte ly = state.TryGetProperty("ly", out var lyp) ? lyp.GetByte() : (byte)0x7F;
            byte rx = state.TryGetProperty("rx", out var rxp) ? rxp.GetByte() : (byte)0x7F;
            byte ry = state.TryGetProperty("ry", out var ryp) ? ryp.GetByte() : (byte)0x7F;
            sb.AppendLine($"  Left stick:  ({lx}, {ly})");
            sb.AppendLine($"  Right stick: ({rx}, {ry})");

            if (state.TryGetProperty("pressures", out var pressures) &&
                pressures.ValueKind == JsonValueKind.Array)
            {
                string[] names = [
                    "Up", "Right", "Down", "Left",
                    "Triangle", "Circle", "Cross", "Square",
                    "Select", "Start", "L1", "L2", "R1", "R2", "L3", "R3"
                ];
                sb.Append("  Pressures:");
                int i = 0;
                foreach (var p in pressures.EnumerateArray())
                {
                    byte val = p.GetByte();
                    if (val > 0)
                        sb.Append($" {(i < names.Length ? names[i] : $"[{i}]")}={val}");
                    i++;
                }
                if (i == 0 || !pressures.EnumerateArray().Any(p => p.GetByte() > 0))
                    sb.Append(" (none pressed)");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "pad_set_button")]
    [DynamicDescription("pad_set_button_desc")]
    [Description("Set a single button or axis on a PS2 controller to a normalized value. Routes through deadzone/pressure pipeline. Requires PCSX2 PINE mode.")]
    public async Task<string> PadSetButton(
        [Description("Unified pad slot index (0–7, usually 0 for player 1).")] int padSlot,
        [Description("Button index in PadDualshock2::Inputs (0=Up, 1=Right, 2=Down, 3=Left, 4=Triangle, 5=Circle, 6=Cross, 7=Square, 8=Select, 9=Start, 10=L1, 11=L2, 12=R1, 13=R2, 14=L3, 15=R3, 16=LStickRight, 17=LStickLeft, 18=LStickUp, 19=LStickDown, 20=RStickRight, 21=RStickLeft, 22=RStickUp, 23=RStickDown, 24=Pressure, 25=LargeMtr).")] int buttonIndex,
        [Description("Normalized value: 0.0 = fully released, 1.0 = fully pressed.")] float value,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PADSETBUTTON", PadSlot = (byte)padSlot, PadButtonIndex = (byte)buttonIndex, PadButtonValue = value },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pad_set_analog")]
    [DynamicDescription("pad_set_analog_desc")]
    [Description("Set the raw analog stick positions on a PS2 controller, bypassing deadzone processing. Requires PCSX2 PINE mode.")]
    public async Task<string> PadSetAnalog(
        [Description("Unified pad slot index (0–7, usually 0 for player 1).")] int padSlot,
        [Description("Left stick X axis (0–255, 0x7F = centred).")] int lx = 0x7F,
        [Description("Left stick Y axis (0–255, 0x7F = centred).")] int ly = 0x7F,
        [Description("Right stick X axis (0–255, 0x7F = centred).")] int rx = 0x7F,
        [Description("Right stick Y axis (0–255, 0x7F = centred).")] int ry = 0x7F,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PADSETANALOG", PadSlot = (byte)padSlot, PadLx = (byte)lx, PadLy = (byte)ly, PadRx = (byte)rx, PadRy = (byte)ry },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pad_set_state")]
    [DynamicDescription("pad_set_state_desc")]
    [Description("Atomically replace the complete input state of a PS2 controller in one message. Suitable for TAS playback. Requires PCSX2 PINE mode.")]
    public async Task<string> PadSetState(
        [Description("Unified pad slot index (0–7, usually 0 for player 1).")] int padSlot,
        [Description("Left stick X axis (0–255, 0x7F = centred).")] int lx = 0x7F,
        [Description("Left stick Y axis (0–255, 0x7F = centred).")] int ly = 0x7F,
        [Description("Right stick X axis (0–255, 0x7F = centred).")] int rx = 0x7F,
        [Description("Right stick Y axis (0–255, 0x7F = centred).")] int ry = 0x7F,
        [Description("16 pressure values (0–255) in order: Up,Right,Down,Left,Triangle,Circle,Cross,Square,Select,Start,L1,L2,R1,R2,L3,R3. Pressure>0 means pressed.")] string pressures = "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0",
        [Description("PS2 button bitmask (reserved, button state derived from pressures). Default 0xFFFF = all released.")] uint buttons = 0xFFFF,
        CancellationToken cancellationToken = default)
    {
        byte[] parsedPressures;
        try
        {
            parsedPressures = pressures.Split(',')
                .Select(s => byte.Parse(s.Trim()))
                .ToArray();
        }
        catch
        {
            return "Error: pressures must be 16 comma-separated byte values (0–255).";
        }

        if (parsedPressures.Length != 16)
            return "Error: pressures must contain exactly 16 values.";

        var json = await bridge.SendAsync(
            new
            {
                Command = "PADSETSTATE",
                PadSlot = (byte)padSlot,
                PadButtons = buttons,
                PadLx = (byte)lx,
                PadLy = (byte)ly,
                PadRx = (byte)rx,
                PadRy = (byte)ry,
                PadPressures = parsedPressures.ToList(),
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pad_get_type")]
    [DynamicDescription("pad_get_type_desc")]
    [Description("Query the controller type installed in a PS2 pad slot. Returns the type name (NotConnected, DualShock2, Guitar, etc.). Requires PCSX2 PINE mode.")]
    public async Task<string> PadGetType(
        [Description("Unified pad slot index (0–7, usually 0 for player 1).")] int padSlot = 0,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PADGETTYPE", PadSlot = (byte)padSlot },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            byte type = root.TryGetProperty("PadControllerType", out var t) ? t.GetByte() : (byte)0;
            string typeName = type switch
            {
                0 => "NotConnected",
                1 => "DualShock2",
                2 => "Guitar",
                3 => "Jogcon",
                4 => "Negcon",
                5 => "Popn",
                _ => $"Unknown({type})",
            };
            return $"Pad {padSlot}: {typeName} (type={type})";
        }
        catch { return json; }
    }
}
