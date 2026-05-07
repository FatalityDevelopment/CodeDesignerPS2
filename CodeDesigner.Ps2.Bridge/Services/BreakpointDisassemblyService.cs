using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class BreakpointDisassemblyService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public BreakpointDisassemblyService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleListBreakpoints(c.CpuSel),                                                        "LISTBP", "LIST_BP", "LISTBREAKPOINTS");
            reg.Add(c => HandlePineDisassemble(c.CpuSel, c.Address, c.Length.HasValue ? (ushort?)c.Length.Value : null), "PINEDISASSEMBLE", "PINE_DISASSEMBLE", "PINE_DISASM");
        }

        private string HandleListBreakpoints(byte? cpuSel)
        {
            if (_ctx.PineClient != null)
            {
                try
                {
                    var bps = _ctx.PineClient!.ListBreakpoints(cpuSel ?? 0xFF);
                    var entries = bps.Select(b => new PineBreakpointInfo
                    {
                        Address = $"0x{b.Address:X8}",
                        Enabled = b.Enabled,
                        Cpu     = b.Cpu == 0 ? "EE" : "IOP",
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success     = true,
                        Command     = "LISTBP",
                        Breakpoints = entries,
                    });
                }
                catch (Exception ex) { return Responses.Error($"PINE ListBreakpoints failed: {ex.Message}"); }
            }
            // Internal emulator — just use the existing breakpoint set
            return Responses.Error("LISTBP requires PINE mode (use SETBP / CLEARBP for the internal emulator)");
        }

        private string HandlePineDisassemble(byte? cpuSel, uint? address, ushort? count)
        {
            if (cpuSel  == null) return Responses.Error("CpuSel is required for PINEDISASSEMBLE (0=EE, 1=IOP)");
            if (address == null) return Responses.Error("Address is required for PINEDISASSEMBLE");
            if (_ctx.PineClient == null)     return Responses.Error("PINEDISASSEMBLE requires PINE mode; use DISASSEMBLE for static disassembly");
            try
            {
                var lines = _ctx.PineClient!.Disassemble(cpuSel.Value, address.Value, count ?? 32);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "PINEDISASSEMBLE",
                    DisassemblyLines = lines.Select(l => new PineDisassemblyLineInfo
                    {
                        Address = $"0x{l.Address:X8}",
                        Text    = l.Text,
                    }).ToList(),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE Disassemble failed: {ex.Message}");
            }
        }
    }
}
