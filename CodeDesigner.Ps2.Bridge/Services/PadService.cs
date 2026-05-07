using System;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class PadService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public PadService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandlePadGetState(c.PadSlot),                                                "PADGETSTATE");
            reg.Add(c => HandlePadSetButton(c.PadSlot, c.PadButtonIndex, c.PadButtonValue),           "PADSETBUTTON");
            reg.Add(c => HandlePadSetAnalog(c.PadSlot, c.PadLx, c.PadLy, c.PadRx, c.PadRy),           "PADSETANALOG");
            reg.Add(c => HandlePadSetState(c.PadSlot, c.PadButtons, c.PadLx, c.PadLy, c.PadRx, c.PadRy, c.PadPressures), "PADSETSTATE");
            reg.Add(c => HandlePadGetType(c.PadSlot),                                                 "PADGETTYPE");
        }

        // ── PADGETSTATE ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle PADGETSTATE — reads the full input state of one pad slot via
        /// the PINE MsgPadGetState (0x33) opcode.
        /// Returns buttons bitmask, analog stick positions, and 16 pressure values.
        /// </summary>
        private string HandlePadGetState(byte? padSlot)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PADGETSTATE requires PINE mode (connect to PCSX2 first)");
            if (padSlot == null)
                return Responses.Error("PadSlot is required for PADGETSTATE");

            try
            {
                var state = _ctx.PineClient!.PadGetState(padSlot.Value);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "PADGETSTATE",
                    PadState = new PadStateInfo
                    {
                        Buttons   = state.Buttons,
                        Lx        = state.Lx,
                        Ly        = state.Ly,
                        Rx        = state.Rx,
                        Ry        = state.Ry,
                        Pressures = state.Pressures,
                    },
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE PadGetState failed: {ex.Message}");
            }
        }

        // ── PADSETBUTTON ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle PADSETBUTTON — sets a single button/axis on a pad by index and
        /// normalized float value via the PINE MsgPadSetButton (0x34) opcode.
        /// Routes through Pad::SetControllerState() so deadzone/pressure logic applies.
        /// </summary>
        private string HandlePadSetButton(byte? padSlot, byte? buttonIndex, float? value)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PADSETBUTTON requires PINE mode (connect to PCSX2 first)");
            if (padSlot == null)
                return Responses.Error("PadSlot is required for PADSETBUTTON");
            if (buttonIndex == null)
                return Responses.Error("PadButtonIndex is required for PADSETBUTTON");
            if (value == null)
                return Responses.Error("PadButtonValue is required for PADSETBUTTON");

            try
            {
                _ctx.PineClient!.PadSetButton(padSlot.Value, buttonIndex.Value, value.Value);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PADSETBUTTON",
                    Message = $"Button {buttonIndex.Value} on pad {padSlot.Value} set to {value.Value:F2}",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE PadSetButton failed: {ex.Message}");
            }
        }

        // ── PADSETANALOG ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle PADSETANALOG — sets raw analog stick positions on a pad,
        /// bypassing deadzone processing, via the PINE MsgPadSetAnalog (0x35) opcode.
        /// </summary>
        private string HandlePadSetAnalog(byte? padSlot, byte? lx, byte? ly, byte? rx, byte? ry)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PADSETANALOG requires PINE mode (connect to PCSX2 first)");
            if (padSlot == null)
                return Responses.Error("PadSlot is required for PADSETANALOG");

            byte lxVal = lx ?? 0x7F;
            byte lyVal = ly ?? 0x7F;
            byte rxVal = rx ?? 0x7F;
            byte ryVal = ry ?? 0x7F;

            try
            {
                _ctx.PineClient!.PadSetAnalog(padSlot.Value, lxVal, lyVal, rxVal, ryVal);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PADSETANALOG",
                    Message = $"Analog sticks set on pad {padSlot.Value}: L({lxVal},{lyVal}) R({rxVal},{ryVal})",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE PadSetAnalog failed: {ex.Message}");
            }
        }

        // ── PADSETSTATE ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle PADSETSTATE — atomically replaces the complete input state of one
        /// pad via the PINE MsgPadSetState (0x36) opcode.
        /// </summary>
        private string HandlePadSetState(byte? padSlot, uint? buttons,
            byte? lx, byte? ly, byte? rx, byte? ry,
            System.Collections.Generic.List<byte>? pressures)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PADSETSTATE requires PINE mode (connect to PCSX2 first)");
            if (padSlot == null)
                return Responses.Error("PadSlot is required for PADSETSTATE");
            if (pressures == null || pressures.Count != 16)
                return Responses.Error("PadPressures must contain exactly 16 values for PADSETSTATE");

            uint btns  = buttons ?? 0xFFFF; // all released by default
            byte lxVal = lx ?? 0x7F;
            byte lyVal = ly ?? 0x7F;
            byte rxVal = rx ?? 0x7F;
            byte ryVal = ry ?? 0x7F;

            try
            {
                _ctx.PineClient!.PadSetState(padSlot.Value, btns, lxVal, lyVal, rxVal, ryVal, pressures.ToArray());
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PADSETSTATE",
                    Message = $"Pad {padSlot.Value} state updated atomically",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE PadSetState failed: {ex.Message}");
            }
        }

        // ── PADGETTYPE ───────────────────────────────────────────────────────

        /// <summary>
        /// Handle PADGETTYPE — queries the controller type installed in a pad slot
        /// via the PINE MsgPadGetType (0x37) opcode.
        /// </summary>
        private string HandlePadGetType(byte? padSlot)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PADGETTYPE requires PINE mode (connect to PCSX2 first)");
            if (padSlot == null)
                return Responses.Error("PadSlot is required for PADGETTYPE");

            try
            {
                byte controllerType = _ctx.PineClient!.PadGetType(padSlot.Value);
                string typeName = controllerType switch
                {
                    0 => "NotConnected",
                    1 => "DualShock2",
                    2 => "Guitar",
                    3 => "Jogcon",
                    4 => "Negcon",
                    5 => "Popn",
                    _ => $"Unknown({controllerType})",
                };
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success           = true,
                    Command           = "PADGETTYPE",
                    PadControllerType = controllerType,
                    Message           = $"Pad {padSlot.Value} controller type: {typeName}",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE PadGetType failed: {ex.Message}");
            }
        }
    }
}
