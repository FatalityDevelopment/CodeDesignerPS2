using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandleListBreakpoints(byte? cpuSel)
        {
            if (IsPineMode)
            {
                try
                {
                    var bps = PineClient!.ListBreakpoints(cpuSel ?? 0xFF);
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
                catch (Exception ex) { return CreateErrorResponse($"PINE ListBreakpoints failed: {ex.Message}"); }
            }
            // Internal emulator — just use the existing breakpoint set
            return CreateErrorResponse("LISTBP requires PINE mode (use SETBP / CLEARBP for the internal emulator)");
        }

        private string HandlePineDisassemble(byte? cpuSel, uint? address, ushort? count)
        {
            if (cpuSel  == null) return CreateErrorResponse("CpuSel is required for PINEDISASSEMBLE (0=EE, 1=IOP)");
            if (address == null) return CreateErrorResponse("Address is required for PINEDISASSEMBLE");
            if (!IsPineMode)     return CreateErrorResponse("PINEDISASSEMBLE requires PINE mode; use DISASSEMBLE for static disassembly");
            try
            {
                var lines = PineClient!.Disassemble(cpuSel.Value, address.Value, count ?? 32);
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
                return CreateErrorResponse($"PINE Disassemble failed: {ex.Message}");
            }
        }
    }
}
