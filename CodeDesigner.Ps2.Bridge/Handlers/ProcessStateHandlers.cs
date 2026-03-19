using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Emulator;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Reads the current running thread from CPU state and attempts to walk
        /// the PS2 EE OS thread control block list in EE RAM.  Each TCB is 128
        /// bytes; status byte at offset 0: 0x01=running, 0x02=ready, 0x04=wait,
        /// 0x08=suspend, 0x10=dormant.  The thread table is located by reading
        /// the kernel's internal data starting at physical 0x00000000.
        /// </summary>
        private string HandleGetThreads()
        {
            if (IsPineMode)
            {
                try
                {
                    var pineThreads = PineClient!.GetEEThreads();
                    var pineThreadEntries = pineThreads.Select(t => new ThreadEntry
                    {
                        Id       = (int)t.TID,
                        Status   = t.Status switch
                        {
                            1 => "Running",
                            2 => "Ready",
                            4 => "Waiting",
                            8 => "Suspended",
                            16 => "Dormant",
                            _ => $"0x{t.Status:X2}",
                        },
                        Pc       = t.PC,
                        Sp       = t.StackTop,
                        Priority = (int)t.Priority,
                        EntryPc  = t.EntryPoint,
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETTHREADS",
                        Threads = pineThreadEntries,
                    });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"PINE GETTHREADS failed: {ex.Message}");
                }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var cpu     = Scheduler.EE.Cpu;
            var bus     = Scheduler.EE.Bus;
            var threads = new List<ThreadEntry>();

            uint currentPc = NormalizePcToKseg0((uint)(cpu.PC & 0xFFFFFFFFUL));
            uint currentSp = (uint)(cpu.RegsLo[29] & 0xFFFFFFFFUL);

            // ── Walk the PS2 EE OS thread control block list ─────────────────
            // The EE kernel places its thread pool at physical RAM.
            // Structure per TCB (128 bytes):
            //   +0x00  u32 status       (0x01=THS_RUN, 0x02=THS_READY,
            //                            0x04=THS_WAIT, 0x08=THS_SUSPEND,
            //                            0x10=THS_DORMANT)
            //   +0x04  u32 entry        (original entry PC)
            //   +0x08  u32 currentSp    (saved $sp when not running)
            //   +0x0C  u32 stackTop     (stack base)
            //   +0x10  u32 stackSize
            //   +0x14  u32 gp
            //   +0x18  u32 initPriority
            //   +0x1C  u32 priority
            //   +0x20  u32 waitType
            //   +0x24  u32 waitId
            //   +0x28  u32 wakeupCount
            //   +0x2C  u32 attr
            //   +0x30  u32 option
            //   +0x70  u64 savedPc      (saved EPC for non-running threads)
            //
            // The kernel exports a thread count at a well-known location.
            // We scan the first portion of EE RAM to find valid TCBs.

            const uint TcbSize      = 128u;
            const uint MaxTcbScan   = 256u;  // Cap scan to 256 threads
            // Typical EE OS thread pool base (physical).
            // The BIOS places the TCB array at approximately 0x00000100,
            // immediately after the 256-byte kernel exception vector area.
            const uint TcbPoolBase  = 0x00000100u;

            bool foundAny = false;
            for (uint i = 0; i < MaxTcbScan; i++)
            {
                uint physBase = TcbPoolBase + i * TcbSize;
                if (physBase + TcbSize > (uint)PS2MemoryBus.MainRamSize)
                    break;

                uint statusRaw = ReadEEWord(bus, physBase + 0x00);
                uint entry     = ReadEEWord(bus, physBase + 0x04);
                uint savedSp   = ReadEEWord(bus, physBase + 0x08);
                uint initPri   = ReadEEWord(bus, physBase + 0x18);
                uint curPri    = ReadEEWord(bus, physBase + 0x1C);

                // Valid status values are a single power-of-two bit in [0x01..0x10].
                // Zero means the slot is unallocated; anything else is garbage.
                if (statusRaw == 0 || (statusRaw & ~0x1Fu) != 0 ||
                    (statusRaw & (statusRaw - 1)) != 0)
                    continue;

                // Entry must be a plausible kernel/user code address or zero (idle)
                if (entry != 0 &&
                    !(entry >= 0x00100000u || entry >= 0x80000000u))
                    continue;

                string status = statusRaw switch
                {
                    0x01 => "Running",
                    0x02 => "Ready",
                    0x04 => "Waiting",
                    0x08 => "Suspended",
                    0x10 => "Dormant",
                    _    => $"0x{statusRaw:X2}",
                };

                // For the running thread use the live CPU PC/SP.
                bool isRunning = statusRaw == 0x01;
                uint tcbPc = isRunning ? currentPc : NormalizePcToKseg0(entry);
                uint tcbSp = isRunning ? currentSp : savedSp;

                threads.Add(new ThreadEntry
                {
                    Id       = (int)i,
                    Status   = status,
                    Pc       = tcbPc,
                    Sp       = tcbSp,
                    Priority = (int)curPri,
                    EntryPc  = NormalizePcToKseg0(entry),
                });
                foundAny = true;
            }

            // If the TCB scan found nothing (emulator not yet booted into OS, or
            // BIOS layout different), synthesise one entry from the live CPU state.
            if (!foundAny)
            {
                threads.Add(new ThreadEntry
                {
                    Id       = 0,
                    Status   = Scheduler.Paused ? "Suspended" : "Running",
                    Pc       = currentPc,
                    Sp       = currentSp,
                    Priority = 0,
                    EntryPc  = currentPc,
                });
            }

            var response = new CommandResponse
            {
                Success = true,
                Command = "GETTHREADS",
                Threads = threads,
            };
            return JsonSerializer.Serialize(response);
        }

        private string HandleGetIopThreads()
        {
            if (!IsPineMode)
                return CreateErrorResponse("GETIOPTHREADS requires PINE mode");
            try
            {
                var pineThreads = PineClient!.GetIOPThreads();
                var pineThreadEntries = pineThreads.Select(t => new ThreadEntry
                {
                    Id       = (int)t.TID,
                    Status   = t.Status switch
                    {
                        1  => "Running",
                        2  => "Ready",
                        4  => "Waiting",
                        8  => "Suspended",
                        16 => "Dormant",
                        _  => $"0x{t.Status:X2}",
                    },
                    Pc       = t.PC,
                    Sp       = t.StackTop,
                    Priority = (int)t.Priority,
                    EntryPc  = t.EntryPoint,
                }).ToList();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETIOPTHREADS",
                    Threads = pineThreadEntries,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE GETIOPTHREADS failed: {ex.Message}");
            }
        }

        private string HandleGetModules()
        {
            if (IsPineMode)
            {
                try
                {
                    var modules = PineClient!.GetModules();
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
            return CreateErrorResponse("GETMODULES requires PINE mode");
        }

        private string HandleGetStack()
        {
            if (IsPineMode)
            {
                try
                {
                    var frames = PineClient!.GetStack();
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
            return CreateErrorResponse("GETSTACK requires PINE mode");
        }
    }
}
