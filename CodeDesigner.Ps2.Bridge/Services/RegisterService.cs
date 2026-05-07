using System;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class RegisterService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public RegisterService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleGetRegisters(),                                                          "GETREGS");
            reg.Add(c => HandleGetRegister(c.CpuSel, c.RegisterCategory, c.RegisterIndex),              "GETREGISTER", "GET_REGISTER", "PINEGETREGISTER");
            reg.Add(c => HandlePineSetRegister(c.CpuSel, c.RegisterCategory, c.RegisterIndex, c.RegisterValue), "PINESETREG", "PINE_SETREG", "PINESETREGISTER");
            reg.Add(c => HandleSetRegister(c.RegisterName, c.RegisterIndex, c.RegisterValue),           "SETREG");
        }

        /// <summary>
        /// Handle GETREGS — returns the full EE CPU register state.
        /// <para>
        /// Currently routed through PCSX2 PINE only. In built-in emulator mode
        /// this returns a "not implemented" error: the InternalPineSocket needs
        /// the <c>MsgGetRegisters</c> (0x17) opcode wired up to mirror the
        /// 145-byte snapshot PCSX2 PINE returns. Tracked as follow-up work.
        /// </para>
        /// </summary>
        /// <summary>
        /// Public entry-point for cross-service callers (e.g. WatchpointTraceService
        /// after a RUNUNTILPC settles) — returns the same JSON
        /// <see cref="CommandResponse"/> that GETREGS would.
        /// </summary>
        public string GetRegistersResponse() => HandleGetRegisters();

        private string HandleGetRegisters()
        {
            if (_ctx.PineClient == null)
            {
                if (_ctx.Client == null)
                    return Responses.Error("Emulator not running — no debug _ctx.Client attached");
                return Responses.Error(
                    "GETREGS is not yet implemented for the built-in emulator _ctx.Client; " +
                    "the InternalPineSocket needs the GetRegisters opcode (0x17) wired up.");
            }

            try
            {
                var snap = _ctx.PineClient.GetRegisters();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success    = true,
                    Command    = "GETREGS",
                    GPR        = snap.Gpr.ToList(),
                    GPR_Lo32   = snap.GprLo32.ToList(),
                    GPR_Hi32   = snap.GprHi32.ToList(),
                    GPR_UpLo32 = snap.GprUpLo32.ToList(),
                    GPR_UpHi32 = snap.GprUpHi32.ToList(),
                    GPR_PC     = snap.PC,
                    GPR_HI     = snap.HI,
                    GPR_LO     = snap.LO,
                    GPR_HI1    = snap.HI1,
                    GPR_LO1    = snap.LO1,
                    GPR_SA     = (int)snap.SA,
                    COP0_Status = snap.Cop0.Length > 12 ? snap.Cop0[12] : 0,
                    COP0_Cause  = snap.Cop0.Length > 13 ? snap.Cop0[13] : 0,
                    COP0_EPC    = snap.Cop0.Length > 14 ? snap.Cop0[14] : 0,
                    COP0_Count  = snap.Cop0.Length > 9  ? snap.Cop0[9]  : 0,
                    COP0_Regs   = snap.Cop0.Select(v => (ulong)v).ToList(),
                    FPR         = snap.Fpr.ToList(),
                    FPR_ACC     = snap.FprAcc,
                    FCR0        = snap.Fcr0,
                    FCR31       = snap.Fcr31,
                    VU0F_X      = snap.Vu0fX.ToList(),
                    VU0F_Y      = snap.Vu0fY.ToList(),
                    VU0F_Z      = snap.Vu0fZ.ToList(),
                    VU0F_W      = snap.Vu0fW.ToList(),
                    VU0F_ACC_X  = snap.Vu0AccX,
                    VU0F_ACC_Y  = snap.Vu0AccY,
                    VU0F_ACC_Z  = snap.Vu0AccZ,
                    VU0F_ACC_W  = snap.Vu0AccW,
                    VU0F_Q      = snap.Vu0Q,
                    VU0F_P      = snap.Vu0P,
                    VU0F_I      = snap.Vu0I,
                    VU0I        = snap.Vu0i.ToList(),
                    VU0_Status  = snap.Vu0i.Length > 16 ? snap.Vu0i[16] : 0,
                    VU0_MAC     = (ushort)(snap.Vu0i.Length > 17 ? snap.Vu0i[17] : 0),
                    VU0_Clip    = snap.Vu0i.Length > 18 ? snap.Vu0i[18] : 0,
                    GS_Priv     = snap.GsPriv.ToList(),
                });
            }
            catch (Exception ex) { return Responses.Error($"GetRegisters error: {ex.Message}"); }
        }

        /// <summary>Handle GETREGISTER — single-register read via PCSX2 PINE.</summary>
        private string HandleGetRegister(byte? cpuSel, byte? cat, int? regIndex)
        {
            if (cpuSel == null) return Responses.Error("CpuSel is required for GETREGISTER (0=EE, 1=IOP)");
            if (cat == null)    return Responses.Error("RegisterCategory is required for GETREGISTER");
            if (regIndex == null) return Responses.Error("RegisterIndex is required for GETREGISTER");
            if (_ctx.PineClient == null)
                return Responses.Error("GETREGISTER is currently PCSX2-only");
            try
            {
                byte[] val = _ctx.PineClient.GetRegister(cpuSel.Value, cat.Value, (byte)regIndex.Value);
                string hex = BitConverter.ToString(val).Replace("-", "");
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success       = true,
                    Command       = "GETREGISTER",
                    RegisterValue = hex,
                });
            }
            catch (Exception ex) { return Responses.Error($"GetRegister failed: {ex.Message}"); }
        }

        /// <summary>Handle PINESETREG — single-register write via PCSX2 PINE.</summary>
        private string HandlePineSetRegister(byte? cpuSel, byte? cat, int? regIndex, string? regValue)
        {
            if (cpuSel == null) return Responses.Error("CpuSel is required for PINESETREG (0=EE, 1=IOP)");
            if (cat == null)    return Responses.Error("RegisterCategory is required for PINESETREG");
            if (regIndex == null) return Responses.Error("RegisterIndex is required for PINESETREG");
            if (string.IsNullOrEmpty(regValue)) return Responses.Error("RegisterValue (32 hex chars = 128-bit LE) is required for PINESETREG");
            if (_ctx.PineClient == null)
                return Responses.Error("PINESETREG is currently PCSX2-only");

            string hex = regValue.Replace("0x", "").Replace("0X", "").Trim();
            if (hex.Length > 32 || hex.Length == 0 || hex.Length % 2 != 0)
                return Responses.Error("RegisterValue must be an even-length hex string of up to 32 characters (128 bits)");
            byte[] val128 = new byte[16];
            byte[] parsed = new byte[hex.Length / 2];
            for (int i = 0; i < parsed.Length; i++)
                parsed[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            Buffer.BlockCopy(parsed, 0, val128, 0, parsed.Length);

            try
            {
                _ctx.PineClient.SetRegister(cpuSel.Value, cat.Value, (byte)regIndex.Value, val128);
                return Responses.Success("Register set");
            }
            catch (Exception ex) { return Responses.Error($"SetRegister failed: {ex.Message}"); }
        }

        /// <summary>
        /// Handle SETREG — writes a new value to a named EE register.
        /// <para>
        /// Currently routes only through PCSX2 PINE via <see cref="HandlePineSetRegister"/>.
        /// Built-in emulator support requires the InternalPineSocket to wire up
        /// the SetRegister opcode (0x19) — tracked as follow-up work.
        /// </para>
        /// </summary>
        private string HandleSetRegister(string? regName, int? regIndex, string? regValue)
        {
            if (_ctx.PineClient == null)
                return Responses.Error(
                    "SETREG is currently PCSX2-only; built-in emulator support requires " +
                    "the InternalPineSocket to wire up the SetRegister opcode.");
            if (string.IsNullOrWhiteSpace(regName) || string.IsNullOrWhiteSpace(regValue))
                return Responses.Error("Missing parameters");

            // Map the named register to PINE category + index, then forward via PINESETREG.
            // For now, only the most common categories (GPR, COP0, FPR) are supported.
            byte cpuSel = 0; // EE
            byte cat;
            int  idx;

            switch (regName!.ToUpperInvariant())
            {
                case "GPR":
                    if (regIndex == null || regIndex < 1 || regIndex > 31)
                        return Responses.Error("GPR index must be 1-31");
                    cat = 0; idx = regIndex.Value; break;
                case "COP0":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return Responses.Error("COP0 index must be 0-31");
                    cat = 1; idx = regIndex.Value; break;
                case "FPR":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return Responses.Error("FPR index must be 0-31");
                    cat = 2; idx = regIndex.Value; break;
                default:
                    return Responses.Error(
                        $"SETREG via PINE only supports GPR, COP0 and FPR currently. " +
                        $"Use PINESETREG with explicit CpuSel/Category/Index for finer control.");
            }

            return HandlePineSetRegister(cpuSel, cat, idx, regValue);
        }
    }
}
