using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Handle GETREGS command — returns the full EE CPU register state including
        /// GPR, all 32 COP0 registers, FPR/FCR (COP1), VU0 float/integer registers,
        /// and key GS privileged registers.
        /// In PINE mode, returns the register snapshot from PCSX2 via the extended
        /// PINE protocol (MsgGetRegisters — GPRs, PC, HI, LO only).
        /// </summary>
        private string HandleGetRegisters()
        {
            if (IsPineMode)
            {
                try
                {
                    var snap = PineClient!.GetRegisters();
                    var pineGpr     = snap.Gpr.ToList();
                    var pineGprLo32 = snap.GprLo32.ToList();
                    var pineGprHi32 = snap.GprHi32.ToList();
                    var pineGprUpLo = snap.GprUpLo32.ToList();
                    var pineGprUpHi = snap.GprUpHi32.ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success   = true,
                        Command   = "GETREGS",
                        GPR       = pineGpr,
                        GPR_Lo32  = pineGprLo32,
                        GPR_Hi32  = pineGprHi32,
                        GPR_UpLo32 = pineGprUpLo,
                        GPR_UpHi32 = pineGprUpHi,
                        GPR_PC    = snap.PC,
                        GPR_HI    = snap.HI,
                        GPR_LO    = snap.LO,
                        GPR_HI1   = snap.HI1,
                        GPR_LO1   = snap.LO1,
                        GPR_SA    = (int)snap.SA,
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
                catch (Exception ex) { return CreateErrorResponse($"PINE GetRegisters error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var cpu  = Scheduler.EE.Cpu;
            var cop0 = cpu.COP0;
            var cop1 = cpu.COP1;
            var vu0  = cpu.VU0;
            var gs   = Scheduler.EE.Bus.Gs;

            // GPR (32 × 64-bit lower lane)
            var gpr = new List<ulong>(32);
            for (int i = 0; i < 32; i++)
                gpr.Add(cpu.RegsLo[i]);

            // GPR precise 32-bit arrays (avoids JavaScript floating-point precision loss
            // when representing 64-bit values > Number.MAX_SAFE_INTEGER).
            // Each 128-bit GPR is split into four uint32 fields:
            //   Lo32 / Hi32  = lower and upper halves of RegsLo[i]  (integer lane)
            //   UpLo32 / UpHi32 = lower and upper halves of RegsHi[i] (upper 64-bit lane)
            var gprLo32  = new List<uint>(32);
            var gprHi32  = new List<uint>(32);
            var gprUpLo32 = new List<uint>(32);
            var gprUpHi32 = new List<uint>(32);
            for (int i = 0; i < 32; i++)
            {
                gprLo32.Add((uint)(cpu.RegsLo[i] & 0xFFFFFFFFUL));
                gprHi32.Add((uint)(cpu.RegsLo[i] >> 32));
                gprUpLo32.Add((uint)(cpu.RegsHi[i] & 0xFFFFFFFFUL));
                gprUpHi32.Add((uint)(cpu.RegsHi[i] >> 32));
            }

            // COP0 – all 32 registers (read as 64-bit to preserve high bits where relevant)
            var cop0Regs = new List<ulong>(32);
            for (int i = 0; i < 32; i++)
                cop0Regs.Add(cop0.ReadRegister64(i));

            // COP1 FPR – 32 × 32-bit floats, transmitted as raw IEEE-754 bit patterns (uint)
            // so the client can display them as both float and hex without precision loss.
            var fpr = new List<uint>(32);
            for (int i = 0; i < 32; i++)
                fpr.Add(BitConverter.SingleToUInt32Bits(cpu.FPR[i]));
            uint faccBits = BitConverter.SingleToUInt32Bits(cpu.FACC);

            // FCR – only FCR0 (implementation) and FCR31 (FCSR) are architecturally
            // defined on the PS2 EE; the rest are reserved/unused.
            uint fcr0  = cop1.ReadControlRegister(0);
            uint fcr31 = cop1.ReadControlRegister(31);

            // VU0 float registers – 32 × 4-component (x,y,z,w), each component as uint bits
            var vu0fX = new List<uint>(32);
            var vu0fY = new List<uint>(32);
            var vu0fZ = new List<uint>(32);
            var vu0fW = new List<uint>(32);
            for (int i = 0; i < 32; i++)
            {
                var v = vu0.VF[i];
                vu0fX.Add(BitConverter.SingleToUInt32Bits(v.X));
                vu0fY.Add(BitConverter.SingleToUInt32Bits(v.Y));
                vu0fZ.Add(BitConverter.SingleToUInt32Bits(v.Z));
                vu0fW.Add(BitConverter.SingleToUInt32Bits(v.W));
            }
            // VU0 ACC register
            uint vu0accX = BitConverter.SingleToUInt32Bits(vu0.ACC.X);
            uint vu0accY = BitConverter.SingleToUInt32Bits(vu0.ACC.Y);
            uint vu0accZ = BitConverter.SingleToUInt32Bits(vu0.ACC.Z);
            uint vu0accW = BitConverter.SingleToUInt32Bits(vu0.ACC.W);
            uint vu0Q = BitConverter.SingleToUInt32Bits(vu0.Q);
            uint vu0P = BitConverter.SingleToUInt32Bits(vu0.P);
            uint vu0I = BitConverter.SingleToUInt32Bits(vu0.I);

            // VU0 integer registers (vi00–vi15) plus the 16 COP2 control registers (vi16–vi31).
            // This matches PCSX2's COP2_REG_CTL layout:
            //   [0-15]  vi00–vi15  (integer registers, 16-bit each)
            //   [16]    Status     vu0.StatusFlags
            //   [17]    MACflag    vu0.MACFlags
            //   [18]    ClipFlag   vu0.ClipFlags
            //   [19]    c2c19      (reserved, always 0)
            //   [20]    R          vu0.RngState (LFSR state, 0x3F800000 | mantissa)
            //   [21]    I          vu0.I (as raw IEEE-754 bits)
            //   [22]    Q          vu0.Q (as raw IEEE-754 bits)
            //   [23-25] c2c23–c2c25 (reserved, always 0)
            //   [26]    TPC        vu0.PC (trigger program counter)
            //   [27]    CMSAR0     0 (not implemented)
            //   [28]    FBRST      0 (not implemented)
            //   [29]    VPU-STAT   IsRunning ? 1 : 0
            //   [30-31] c2c30–CMSAR1 (reserved, always 0)
            var vu0i = new List<uint>(32);
            for (int i = 0; i < 16; i++)
                vu0i.Add(vu0.VI[i]);
            vu0i.Add(vu0.StatusFlags);                                       // [16] Status
            vu0i.Add(vu0.MACFlags);                                          // [17] MACflag
            vu0i.Add(vu0.ClipFlags);                                         // [18] ClipFlag
            vu0i.Add(0);                                                     // [19] c2c19
            vu0i.Add(vu0.RngState);                                          // [20] R (LFSR state)
            vu0i.Add(BitConverter.SingleToUInt32Bits(vu0.I));                // [21] I
            vu0i.Add(BitConverter.SingleToUInt32Bits(vu0.Q));                // [22] Q
            vu0i.Add(0); vu0i.Add(0); vu0i.Add(0);                         // [23-25] c2c23–c2c25
            vu0i.Add(vu0.PC);                                                // [26] TPC
            vu0i.Add(0); vu0i.Add(0);                                        // [27-28] CMSAR0/FBRST
            vu0i.Add(vu0.IsRunning ? 1u : 0u);                              // [29] VPU-STAT
            vu0i.Add(0); vu0i.Add(0);                                        // [30-31] c2c30/CMSAR1

            // GS privileged registers
            var gsPriv = new List<ulong>
            {
                gs.ReadPriv64(GS.PRIV_PMODE),
                gs.ReadPriv64(GS.PRIV_SMODE1),
                gs.ReadPriv64(GS.PRIV_SMODE2),
                gs.ReadPriv64(GS.PRIV_SRFSH),
                gs.ReadPriv64(GS.PRIV_SYNCH1),
                gs.ReadPriv64(GS.PRIV_SYNCH2),
                gs.ReadPriv64(GS.PRIV_SYNCV),
                gs.ReadPriv64(GS.PRIV_DISPFB1),
                gs.ReadPriv64(GS.PRIV_DISPLAY1),
                gs.ReadPriv64(GS.PRIV_DISPFB2),
                gs.ReadPriv64(GS.PRIV_DISPLAY2),
                gs.ReadPriv64(GS.PRIV_EXTBUF),
                gs.ReadPriv64(GS.PRIV_EXTDATA),
                gs.ReadPriv64(GS.PRIV_EXTWRITE),
                gs.ReadPriv64(GS.PRIV_BGCOLOR),
                gs.ReadPriv64(GS.PRIV_CSR),
                gs.ReadPriv64(GS.PRIV_IMR),
                gs.ReadPriv64(GS.PRIV_BUSDIR),
                gs.ReadPriv64(GS.PRIV_SIGLBLID),
            };

            var response = new CommandResponse
            {
                Success  = true,
                Command  = "GETREGS",
                GPR      = gpr,
                GPR_Lo32  = gprLo32,
                GPR_Hi32  = gprHi32,
                GPR_UpLo32 = gprUpLo32,
                GPR_UpHi32 = gprUpHi32,
                GPR_PC   = NormalizePcToKseg0((uint)(cpu.PC & 0xFFFFFFFFUL)),
                GPR_HI   = cpu.HI,
                GPR_LO   = cpu.LO,
                GPR_HI1  = cpu.HI1,
                GPR_LO1  = cpu.LO1,
                GPR_SA   = cpu.SA,
                // Legacy COP0 fields (kept for backward compatibility)
                COP0_Status = cop0.ReadRegister32(12),
                COP0_Cause  = cop0.ReadRegister32(13),
                COP0_EPC    = cop0.ReadRegister64(14),
                COP0_Count  = cop0.ReadRegister32(9),
                // Full register sets
                COP0_Regs   = cop0Regs,
                FPR         = fpr,
                FPR_ACC     = faccBits,
                FCR0        = fcr0,
                FCR31       = fcr31,
                VU0F_X      = vu0fX,
                VU0F_Y      = vu0fY,
                VU0F_Z      = vu0fZ,
                VU0F_W      = vu0fW,
                VU0F_ACC_X  = vu0accX,
                VU0F_ACC_Y  = vu0accY,
                VU0F_ACC_Z  = vu0accZ,
                VU0F_ACC_W  = vu0accW,
                VU0F_Q      = vu0Q,
                VU0F_P      = vu0P,
                VU0F_I      = vu0I,
                VU0I        = vu0i,
                VU0_Status  = vu0.StatusFlags,
                VU0_MAC     = vu0.MACFlags,
                VU0_Clip    = vu0.ClipFlags,
                GS_Priv     = gsPriv,
            };
            return JsonSerializer.Serialize(response);
        }

        private string HandleGetRegister(byte? cpuSel, byte? cat, int? regIndex)
        {
            if (cpuSel == null) return CreateErrorResponse("CpuSel is required for GETREGISTER (0=EE, 1=IOP)");
            if (cat == null)    return CreateErrorResponse("RegisterCategory is required for GETREGISTER");
            if (regIndex == null) return CreateErrorResponse("RegisterIndex is required for GETREGISTER");
            if (IsPineMode)
            {
                try
                {
                    byte[] val = PineClient!.GetRegister(cpuSel.Value, cat.Value, (byte)regIndex.Value);
                    string hex = BitConverter.ToString(val).Replace("-", "");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETREGISTER",
                        RegisterValue = hex,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetRegister failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETREGISTER requires PINE mode");
        }

        private string HandlePineSetRegister(byte? cpuSel, byte? cat, int? regIndex, string? regValue)
        {
            if (cpuSel == null) return CreateErrorResponse("CpuSel is required for PINESETREG (0=EE, 1=IOP)");
            if (cat == null)    return CreateErrorResponse("RegisterCategory is required for PINESETREG");
            if (regIndex == null) return CreateErrorResponse("RegisterIndex is required for PINESETREG");
            if (string.IsNullOrEmpty(regValue)) return CreateErrorResponse("RegisterValue (32 hex chars = 128-bit LE) is required for PINESETREG");
            if (!IsPineMode) return CreateErrorResponse("PINESETREG requires PINE mode; use SETREG for the internal emulator");

            // Accept 32 hex chars (128-bit) or up to 16 hex chars (64-bit, zero-extended)
            string hex = regValue.Replace("0x", "").Replace("0X", "").Trim();
            if (hex.Length > 32 || hex.Length == 0 || hex.Length % 2 != 0)
                return CreateErrorResponse("RegisterValue must be an even-length hex string of up to 32 characters (128 bits)");
            byte[] val128 = new byte[16];
            byte[] parsed = new byte[hex.Length / 2];
            for (int i = 0; i < parsed.Length; i++)
                parsed[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            // Copy little-endian (pad high bytes with zero)
            Buffer.BlockCopy(parsed, 0, val128, 0, parsed.Length);

            try
            {
                PineClient!.SetRegister(cpuSel.Value, cat.Value, (byte)regIndex.Value, val128);
                return CreateSuccessResponse($"Register set");
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE SetRegister failed: {ex.Message}"); }
        }

        /// <summary>
        /// Handle SETREG command — writes a new value to a named EE register.
        /// <para>Supported register names: GPR (index 1-31), PC, HI, LO, HI1, LO1, SA, COP0 (index 0-31), FPR (index 0-31).</para>
        /// <para>Values may be supplied as decimal integers or as 0x-prefixed hexadecimal strings.</para>
        /// </summary>
        private string HandleSetRegister(string? regName, int? regIndex, string? regValue)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (string.IsNullOrWhiteSpace(regName) || string.IsNullOrWhiteSpace(regValue))
                return CreateErrorResponse("Missing parameters");

            if (!TryParseUInt64(regValue!, out ulong value))
                return CreateErrorResponse("Invalid register value — expected decimal or 0x-prefixed hex");

            var cpu = Scheduler.EE.Cpu;
            switch (regName!.ToUpperInvariant())
            {
                case "GPR":
                    if (regIndex == null || regIndex < 1 || regIndex > 31)
                        return CreateErrorResponse("GPR index must be 1–31 ($zero is read-only)");
                    cpu.RegsLo[regIndex.Value] = value;
                    // Writing via the integer (lower) lane always clears the upper lane
                    // so the visible 128-bit value matches the written 64-bit value.
                    cpu.RegsHi[regIndex.Value] = 0;
                    break;
                case "PC":
                    cpu.PC = value;
                    break;
                case "HI":  cpu.HI  = value; break;
                case "LO":  cpu.LO  = value; break;
                case "HI1": cpu.HI1 = value; break;
                case "LO1": cpu.LO1 = value; break;
                case "SA":  cpu.SA  = (int)(uint)value; break;
                case "COP0":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return CreateErrorResponse("COP0 index must be 0–31");
                    cpu.COP0.WriteRegister64(regIndex.Value, value);
                    break;
                case "FPR":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return CreateErrorResponse("FPR index must be 0–31");
                    cpu.FPR[regIndex.Value] = BitConverter.UInt32BitsToSingle((uint)value);
                    break;
                case "FCR":
                    // PS2 EE FPU only has FCR0 (FIR, read-only) and FCR31 (FCSR).
                    if (regIndex == 31)
                        cpu.COP1.WriteControlRegister(31, (uint)value);
                    else
                        return CreateErrorResponse("FCR: only FCR31 (FCSR) is writable on the PS2 EE");
                    break;
                // VU0 integer registers (vi00–vi15) — PCSX2 category EECAT_VU0I index 0-15
                case "VU0I":
                    if (regIndex == null || regIndex < 0 || regIndex > 15)
                        return CreateErrorResponse("VU0I index must be 0–15");
                    if (regIndex > 0) // vi0 is read-only (hardwired to 0)
                        cpu.VU0.VI[regIndex.Value] = (ushort)(value & 0xFFFF);
                    break;
                // VU0 float registers (vf00–vf31) — PCSX2 category EECAT_VU0F
                // Value is the X component raw IEEE-754 bits; Y/Z/W remain unchanged.
                case "VU0F":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return CreateErrorResponse("VU0F index must be 0–31");
                    if (regIndex > 0) // vf0 is read-only (hardwired)
                    {
                        var vf = cpu.VU0.VF[regIndex.Value];
                        cpu.VU0.VF[regIndex.Value] = new System.Numerics.Vector4(
                            BitConverter.UInt32BitsToSingle((uint)value),
                            vf.Y, vf.Z, vf.W);
                    }
                    break;
                default:
                    return CreateErrorResponse(
                        $"Unknown register type '{regName}'. " +
                        "Supported: GPR, PC, HI, LO, HI1, LO1, SA, COP0, FPR, FCR, VU0I, VU0F");
            }
            return CreateSuccessResponse("Register updated");
        }

        /// <summary>
        /// Parse a register value string that may be decimal or 0x-prefixed hex.
        /// </summary>
        private static bool TryParseUInt64(string s, out ulong value)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ulong.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber,
                                      System.Globalization.CultureInfo.InvariantCulture, out value);
            return ulong.TryParse(s, System.Globalization.NumberStyles.None,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Read a little-endian 32-bit word from the EE physical RAM.</summary>
        private static uint ReadEEWord(PS2MemoryBus bus, uint physAddr)
        {
            if (physAddr + 4 > (uint)PS2MemoryBus.MainRamSize) return 0;
            return (uint)(bus.Ram[physAddr]
                        | (bus.Ram[physAddr + 1] << 8)
                        | (bus.Ram[physAddr + 2] << 16)
                        | (bus.Ram[physAddr + 3] << 24));
        }
    }
}
