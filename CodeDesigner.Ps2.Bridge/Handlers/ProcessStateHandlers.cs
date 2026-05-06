using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // EE main RAM size — 32 MB. Used to bound the TCB scan.
        private const uint EeMainRamSize = 32 * 1024 * 1024;

        /// <summary>
        /// Handle GETTHREADS — return current EE threads. PCSX2 PINE mode uses
        /// the native <c>GetEEThreads()</c> opcode. Built-in mode walks the EE
        /// kernel's TCB pool in RAM via the active <see cref="IDebugClient"/>.
        /// </summary>
        private string HandleGetThreads()
        {
            if (PineClient != null)
            {
                try
                {
                    var pineThreads = PineClient.GetEEThreads();
                    var entries = pineThreads.Select(t => new ThreadEntry
                    {
                        Id       = (int)t.TID,
                        Status   = StatusName(t.Status),
                        Pc       = t.PC,
                        Sp       = t.StackTop,
                        Priority = (int)t.Priority,
                        EntryPc  = t.EntryPoint,
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETTHREADS",
                        Threads = entries,
                    });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"PINE GETTHREADS failed: {ex.Message}");
                }
            }

            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");

            // ── Walk the PS2 EE OS TCB pool via the debug client ─────────────
            // Same algorithm as the previous in-process version, but reads
            // memory through Client.ReadBytes() so it works for both PCSX2 and
            // the built-in InternalPineSocket.

            const uint TcbSize     = 128u;
            const uint MaxTcbScan  = 256u;
            const uint TcbPoolBase = 0x00000100u;
            const uint TcbPoolKseg0 = TcbPoolBase | 0x80000000u;

            uint scanBytes = MaxTcbScan * TcbSize;
            byte[] poolBytes;
            try { poolBytes = Client.ReadBytes(TcbPoolKseg0, (int)scanBytes); }
            catch { poolBytes = Array.Empty<byte>(); }

            uint currentPc;
            try { currentPc = NormalizePcToKseg0(Client.GetProgramCounter()); }
            catch { currentPc = 0; }

            // SP comes from $sp (GPR 29). Without GETREGS support yet we can
            // only fall back to the running TCB's PC/Sp from the table itself.
            uint currentSp = 0;

            var threads = new List<ThreadEntry>();
            bool foundAny = false;

            for (uint i = 0; i < MaxTcbScan; i++)
            {
                uint physBase = TcbPoolBase + i * TcbSize;
                if (physBase + TcbSize > EeMainRamSize)
                    break;
                int slotOffset = (int)(physBase - TcbPoolBase);
                if (slotOffset + 0x20 > poolBytes.Length)
                    break;

                uint statusRaw = ReadU32(poolBytes, slotOffset + 0x00);
                uint entry     = ReadU32(poolBytes, slotOffset + 0x04);
                uint savedSp   = ReadU32(poolBytes, slotOffset + 0x08);
                uint curPri    = ReadU32(poolBytes, slotOffset + 0x1C);

                if (statusRaw == 0 || (statusRaw & ~0x1Fu) != 0 ||
                    (statusRaw & (statusRaw - 1)) != 0)
                    continue;

                if (entry != 0 &&
                    !(entry >= 0x00100000u || entry >= 0x80000000u))
                    continue;

                bool isRunning = statusRaw == 0x01;
                uint tcbPc = isRunning ? currentPc : NormalizePcToKseg0(entry);
                uint tcbSp = isRunning ? currentSp : savedSp;

                threads.Add(new ThreadEntry
                {
                    Id       = (int)i,
                    Status   = StatusName(statusRaw),
                    Pc       = tcbPc,
                    Sp       = tcbSp,
                    Priority = (int)curPri,
                    EntryPc  = NormalizePcToKseg0(entry),
                });
                foundAny = true;
            }

            if (!foundAny)
            {
                threads.Add(new ThreadEntry
                {
                    Id       = 0,
                    Status   = "Running",
                    Pc       = currentPc,
                    Sp       = currentSp,
                    Priority = 0,
                    EntryPc  = currentPc,
                });
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GETTHREADS",
                Threads = threads,
            });
        }

        /// <summary>Handle GETIOPTHREADS — PCSX2 PINE only.</summary>
        private string HandleGetIopThreads()
        {
            if (PineClient == null)
                return CreateErrorResponse("GETIOPTHREADS requires PCSX2 PINE mode");
            try
            {
                var pineThreads = PineClient.GetIOPThreads();
                var entries = pineThreads.Select(t => new ThreadEntry
                {
                    Id       = (int)t.TID,
                    Status   = StatusName(t.Status),
                    Pc       = t.PC,
                    Sp       = t.StackTop,
                    Priority = (int)t.Priority,
                    EntryPc  = t.EntryPoint,
                }).ToList();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETIOPTHREADS",
                    Threads = entries,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE GETIOPTHREADS failed: {ex.Message}"); }
        }

        /// <summary>Handle GETMODULES — PCSX2 PINE only.</summary>
        private string HandleGetModules()
        {
            if (PineClient == null)
                return CreateErrorResponse("GETMODULES requires PCSX2 PINE mode");
            try
            {
                var modules = PineClient.GetModules();
                var entries = modules.Select(m => new IopModuleInfo
                {
                    Name      = m.Name,
                    Version   = m.Version,
                    TextAddr  = $"0x{m.TextAddr:X8}",
                    Entry     = $"0x{m.Entry:X8}",
                    GP        = $"0x{m.GP:X8}",
                    TextSize  = m.TextSize,
                    DataSize  = m.DataSize,
                    BssSize   = m.BssSize,
                }).ToList();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETMODULES",
                    Modules = entries,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE GetModules failed: {ex.Message}"); }
        }

        /// <summary>Handle GETSTACK — PCSX2 PINE only.</summary>
        private string HandleGetStack()
        {
            if (PineClient == null)
                return CreateErrorResponse("GETSTACK requires PCSX2 PINE mode");
            try
            {
                var frames = PineClient.GetStack();
                var entries = frames.Select(f => new PineStackFrameInfo
                {
                    Entry     = $"0x{f.Entry:X8}",
                    PC        = $"0x{f.PC:X8}",
                    SP        = $"0x{f.SP:X8}",
                    StackSize = f.StackSize,
                }).ToList();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "GETSTACK",
                    StackFrames = entries,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE GetStack failed: {ex.Message}"); }
        }

        private static string StatusName(uint status) => status switch
        {
            0x01 => "Running",
            0x02 => "Ready",
            0x04 => "Waiting",
            0x08 => "Suspended",
            0x10 => "Dormant",
            _    => $"0x{status:X2}",
        };

        private static uint ReadU32(byte[] buf, int offset)
        {
            if (offset + 4 > buf.Length) return 0;
            return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }
    }
}
