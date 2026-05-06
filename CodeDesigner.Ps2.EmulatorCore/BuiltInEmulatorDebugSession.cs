using System;
using System.Collections.Generic;
using System.Text;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.EmulatorCore.Hardware;
using CodeDesigner.Ps2.EmulatorCore.IOP;

namespace CodeDesigner.Ps2.EmulatorCore
{
    public sealed class BuiltInEmulatorDebugSession
    {
        private const int ReportEvery = 1_000_000;
        private const int SpinReportInterval = 5;
        private const int MaxSpinReminders = 3;

        private DualCpuScheduler _scheduler;

        private long _nextReport;
        private uint _lastEePc;
        private uint _lastIopPc;
        private int _spinCount;
        private int _iopSpinCount;
        private bool _eeLeftBios;
        private bool _iopBootDumped;
        private bool _iopLeftBootZone;
        private bool _eeSyscallDetected;
        private bool _eeInterruptsEnabled;
        private uint _lastEeRegion;

        private readonly HashSet<uint> _eeSpinDumpedPcs = new HashSet<uint>();
        private readonly HashSet<uint> _iopSpinDumpedPcs = new HashSet<uint>();
        private readonly Dictionary<uint, int> _eeSpinReminderCounts = new Dictionary<uint, int>();

        // IOP per-step trace state.
        private uint _iopRamFirstPc;
        private bool _iopRamEntryDumped;
        private int _iopMemmoveCallCount;
        private int _iopICtrlPollCount;
        private readonly HashSet<uint> _iopSeenPcs = new HashSet<uint>();

        public BuiltInEmulatorDebugSession(DualCpuScheduler scheduler)
        {
            _scheduler = scheduler;
            ResetState();
        }

        public void Attach()
        {
            _scheduler.EE.UnknownInstruction += (_, opcode) =>
            {
                uint eePc = (uint)(_scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                Console.WriteLine($"EE: Unknown opcode 0x{opcode:x8}  pc=0x{eePc:x8}");
            };

            _scheduler.EE.SyscallRaised += (_, pc) =>
            {
                uint v1 = (uint)(_scheduler.EE.Cpu.RegsLo[3] & 0xFF);
                string fn = BiosCallName((int)v1);
                Console.WriteLine($"EE: HLE bios[0x{v1:x}]: {fn}  (pc=0x{pc:x8})");
            };

            _scheduler.IOP.StepHook = OnIopStep;
        }

        public void OnSchedulerReplaced(DualCpuScheduler scheduler)
        {
            _scheduler = scheduler;
            Attach();
        }

        public void ResetState()
        {
            _nextReport = ReportEvery;
            _lastEePc = uint.MaxValue;
            _lastIopPc = uint.MaxValue;
            _spinCount = 0;
            _iopSpinCount = 0;
            _eeLeftBios = false;
            _iopBootDumped = false;
            _iopLeftBootZone = false;
            _eeSyscallDetected = false;
            _eeInterruptsEnabled = false;
            _lastEeRegion = uint.MaxValue;

            _eeSpinDumpedPcs.Clear();
            _iopSpinDumpedPcs.Clear();
            _eeSpinReminderCounts.Clear();

            _iopRamEntryDumped = false;
            _iopRamFirstPc = uint.MaxValue;
            _iopMemmoveCallCount = 0;
            _iopICtrlPollCount = 0;
            _iopSeenPcs.Clear();
        }

        public void TryReport(long totalSteps, DateTime startTime)
        {
            if (totalSteps < _nextReport)
            {
                return;
            }

            double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            double mips = elapsed > 0 ? totalSteps / elapsed / 1_000_000.0 : 0;

            uint eePc32 = (uint)(_scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
            uint iopPc = _scheduler.IOP.State.PC;

            if (!_eeLeftBios)
            {
                uint eePhys = PS2MemoryBus.TranslateAddress(eePc32);
                if (eePhys < 0x02000000u)
                {
                    _eeLeftBios = true;
                    Console.WriteLine($"*** EE left BIOS ROM - now executing from RAM at 0x{eePc32:x8} (cycle:{totalSteps:N0}) ***");
                }
            }

            uint eePcRegion = eePc32 & 0xFF000000u;
            if (_eeLeftBios && eePcRegion != _lastEeRegion)
            {
                _lastEeRegion = eePcRegion;
                string eeInsn0 = DisassembleAt(_scheduler.EE.Bus, eePc32);
                Console.WriteLine($"*** EE moved to new region 0x{eePc32:x8} (cycle:{totalSteps:N0}) {eeInsn0} ***");
            }

            if (!_eeSyscallDetected && _eeLeftBios)
            {
                uint eeStatus = _scheduler.EE.Cpu.COP0.ReadRegister32(12);
                if ((eeStatus & 0x2u) != 0)
                {
                    _eeSyscallDetected = true;
                    Console.WriteLine($"*** EE entered exception mode (EXL=1) at 0x{eePc32:x8} (cycle:{totalSteps:N0}) COP0.Status=0x{eeStatus:x8} ***");
                }
            }

            if (!_eeInterruptsEnabled && _eeLeftBios)
            {
                uint eeStatus = _scheduler.EE.Cpu.COP0.ReadRegister32(12);
                if ((eeStatus & 0x10007u) == 0x10001u)
                {
                    _eeInterruptsEnabled = true;
                    Console.WriteLine($"*** EE interrupts ENABLED (IE=1,EIE=1) at 0x{eePc32:x8} (cycle:{totalSteps:N0}) Status=0x{eeStatus:x8} ***");
                    DumpInterruptState(_scheduler);
                }
            }

            if (!_iopBootDumped && iopPc < 0x1000u && iopPc >= 0x100u)
            {
                _iopBootDumped = true;
                Console.WriteLine($"*** IOP entered boot zone 0x{iopPc:x8} (IOP cycle:{_scheduler.TotalIopSteps:N0}) - dumping boot zone disasm ***");
                DumpIopRamRegion(_scheduler.IOP.Bus, 0x0u, 0x200u);
            }

            if (!_iopLeftBootZone && iopPc > 0x400u && iopPc < 0x1FC00000u)
            {
                _iopLeftBootZone = true;
                string iopInsn0 = DisassembleIopAt(_scheduler.IOP.Bus, iopPc);
                Console.WriteLine($"*** IOP left boot zone - now at 0x{iopPc:x8} (IOP cycle:{_scheduler.TotalIopSteps:N0}) {iopInsn0} ***");
                DumpInterruptState(_scheduler);
            }

            if (eePc32 == _lastEePc)
            {
                _spinCount++;
                if (_spinCount == 1 && !_eeSpinDumpedPcs.Contains(eePc32))
                {
                    _eeSpinDumpedPcs.Add(eePc32);
                    Console.WriteLine($"*** EE is spinning at 0x{eePc32:x8} [{GetPcRegion(eePc32)}] ***");
                    DumpInterruptState(_scheduler);
                }
                else if (_spinCount % SpinReportInterval == 0)
                {
                    _eeSpinReminderCounts.TryGetValue(eePc32, out int remCount);
                    if (remCount < MaxSpinReminders)
                    {
                        _eeSpinReminderCounts[eePc32] = remCount + 1;
                        uint intcSt = _scheduler.EE.Bus.Intc.Stat;
                        uint intcMk = _scheduler.EE.Bus.Intc.Mask;
                        uint eeStatus = _scheduler.EE.Cpu.COP0.ReadRegister32(12);
                        ulong eeV1 = (ulong)_scheduler.EE.Cpu.RegsLo[3];
                        ulong eeS1 = (ulong)_scheduler.EE.Cpu.RegsLo[17];
                        Console.WriteLine(
                            $"  [spin@{totalSteps:N0}] " +
                            $"INTC STAT=0x{intcSt:x8} MASK=0x{intcMk:x8} " +
                            $"COP0.Status=0x{eeStatus:x8} " +
                            $"SIF.F240=0x{_scheduler.EE.Bus.Sif.F240:x8} " +
                            $"v1=0x{eeV1:x16} s1=0x{eeS1:x16}");
                        if (remCount + 1 == MaxSpinReminders)
                        {
                            Console.WriteLine($"  [spin@0x{eePc32:x8}] further spin reminders suppressed.");
                        }
                    }
                }
            }
            else
            {
                _spinCount = 0;
                _lastEePc = eePc32;
            }

            if (iopPc == _lastIopPc)
            {
                _iopSpinCount++;
                if (_iopSpinCount == 1 && !_iopSpinDumpedPcs.Contains(iopPc))
                {
                    _iopSpinDumpedPcs.Add(iopPc);
                    Console.WriteLine($"*** IOP is spinning at 0x{iopPc:x8} [{GetPcRegion(iopPc)}] ***");
                    DumpInterruptState(_scheduler);
                }
            }
            else
            {
                _iopSpinCount = 0;
                _lastIopPc = iopPc;
            }

            string eeInsn = DisassembleAt(_scheduler.EE.Bus, eePc32);
            string iopInsn = DisassembleIopAt(_scheduler.IOP.Bus, iopPc);

            Console.WriteLine($"(EE  pc:0x{eePc32:x8} [{GetPcRegion(eePc32)}], cycle:{totalSteps:N0}) {eeInsn}");
            Console.WriteLine($"(IOP pc:0x{iopPc:x8} [{GetPcRegion(iopPc)}], cycle:{_scheduler.TotalIopSteps:N0}) {iopInsn}");
            Console.WriteLine(
                $"     IOP INTC: I_STAT=0x{_scheduler.IOP.Bus.IStat:x8} " +
                $"I_MASK=0x{_scheduler.IOP.Bus.IMask:x8} I_CTRL=0x{_scheduler.IOP.Bus.ICtrl:x8} " +
                $"pending={(_scheduler.IOP.Bus.IStat & _scheduler.IOP.Bus.IMask) != 0}");
            Console.WriteLine($"     ~{mips:F2} MIPS");
            Console.WriteLine();

            _nextReport += ReportEvery;
        }

        public void PrintFinalPcSummary()
        {
            uint finalEePc = (uint)(_scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
            Console.WriteLine($"(EE  pc:0x{finalEePc:x8} [{GetPcRegion(finalEePc)}]) {DisassembleAt(_scheduler.EE.Bus, finalEePc)}");
            Console.WriteLine($"(IOP pc:0x{_scheduler.IOP.State.PC:x8} [{GetPcRegion(_scheduler.IOP.State.PC)}])");
        }

        private void OnIopStep(IopState state)
        {
            uint pc = state.PC;

            if (!_iopRamEntryDumped && pc < 0x1FC00000u && pc >= 0x800u)
            {
                _iopRamEntryDumped = true;
                _iopRamFirstPc = pc;
                Console.WriteLine($"*** IOP first entered IOP RAM at PC=0x{pc:x8} - dumping all 32 GPRs ***");
                DumpAllIopRegs(state);
            }

            if (_iopRamEntryDumped && _iopMemmoveCallCount == 0
                && pc < 0x20000u && _iopSeenPcs.Count < 64 && _iopSeenPcs.Add(pc))
            {
                string[] rn = {"zero","at","v0","v1","a0","a1","a2","a3",
                                "t0","t1","t2","t3","t4","t5","t6","t7",
                                "s0","s1","s2","s3","s4","s5","s6","s7",
                                "t8","t9","k0","k1","gp","sp","fp","ra"};
                var sb = new StringBuilder();
                sb.Append($"  IOP trace PC=0x{pc:x8}  ");
                for (int ri = 0; ri < 32; ri++)
                {
                    if (state.ReadReg(ri) != 0)
                    {
                        sb.Append($"{rn[ri]}=0x{state.ReadReg(ri):x8} ");
                    }
                }

                Console.WriteLine(sb.ToString());
            }

            if (pc == 0x2820u && _iopMemmoveCallCount < 20)
            {
                _iopMemmoveCallCount++;
                uint a2 = state.ReadReg(6);
                if (a2 > 0x1000u)
                {
                    Console.WriteLine($"*** IOP memmove call #{_iopMemmoveCallCount} (LARGE COUNT!) PC=0x2820 ***");
                    DumpAllIopRegs(state);
                }
                else
                {
                    Console.WriteLine(
                        $"  IOP memmove call #{_iopMemmoveCallCount}: a0=0x{state.ReadReg(4):x8} a1=0x{state.ReadReg(5):x8}" +
                        $" a2=0x{a2:x8} a3=0x{state.ReadReg(7):x8} ra=0x{state.ReadReg(31):x8}");
                }
            }

            if (pc == 0x2810u)
            {
                uint a0 = state.ReadReg(4);
                uint a1 = state.ReadReg(5);
                uint a2 = state.ReadReg(6);
                uint a3 = state.ReadReg(7);
                uint ra31 = state.ReadReg(31);
                Console.WriteLine(
                    $"  IOP 0x2810 call: a0=0x{a0:x8} a1=0x{a1:x8} a2=0x{a2:x8} a3=0x{a3:x8} ra=0x{ra31:x8}");
            }

            if (pc == 0x4078u && _iopICtrlPollCount < 10)
            {
                _iopICtrlPollCount++;
                Console.WriteLine(
                    $"  IOP 0x4078 (I_CTRL poll #{_iopICtrlPollCount}): " +
                    $"I_CTRL=0x{_scheduler.IOP.Bus.ICtrl:x8} I_STAT=0x{_scheduler.IOP.Bus.IStat:x8} " +
                    $"I_MASK=0x{_scheduler.IOP.Bus.IMask:x8} SR=0x{state.SR:x8} " +
                    $"a0=0x{state.ReadReg(4):x8} ra=0x{state.ReadReg(31):x8}");
                if (_iopICtrlPollCount == 10)
                {
                    Console.WriteLine("  IOP 0x4078: further I_CTRL poll traces suppressed.");
                }
            }
        }

        private static void DumpInterruptState(DualCpuScheduler sched)
        {
            uint eeStatus = sched.EE.Cpu.COP0.ReadRegister32(12);
            uint eeCause = sched.EE.Cpu.COP0.ReadRegister32(13);
            bool eeIE = (eeStatus & 0x00001u) != 0;
            bool eeEXL = (eeStatus & 0x00002u) != 0;
            bool eeERL = (eeStatus & 0x00004u) != 0;
            bool eeEIE = (eeStatus & 0x10000u) != 0;
            bool eeIM2 = (eeStatus & 0x00400u) != 0;
            bool eeIM7 = (eeStatus & 0x08000u) != 0;
            bool eeIP2 = (eeCause & 0x00400u) != 0;
            bool eeIP7 = (eeCause & 0x08000u) != 0;

            uint intcStat = sched.EE.Bus.Intc.Stat;
            uint intcMask = sched.EE.Bus.Intc.Mask;

            uint iopSR = sched.IOP.State.SR;
            uint iopCause = sched.IOP.State.Cause;
            bool iopIEc = (iopSR & 0x001u) != 0;
            bool iopBEV = (iopSR & 0x00400000u) != 0;
            bool iopIM2 = (iopSR & 0x400u) != 0;
            bool iopIP2 = (iopCause & 0x400u) != 0;
            uint iopIStat = sched.IOP.Bus.IStat;
            uint iopIMask = sched.IOP.Bus.IMask;

            Console.WriteLine("=== interrupt diagnostics ===");
            Console.WriteLine(
                $"  EE  COP0.Status: 0x{eeStatus:x8}  " +
                $"IE={B(eeIE)} EXL={B(eeEXL)} ERL={B(eeERL)} EIE={B(eeEIE)} IM2={B(eeIM2)} IM7={B(eeIM7)}");
            Console.WriteLine($"  EE  COP0.Cause:  0x{eeCause:x8}  IP2={B(eeIP2)} IP7={B(eeIP7)}");
            Console.WriteLine(
                $"  EE  INTC_STAT:   0x{intcStat:x8}  INTC_MASK: 0x{intcMask:x8}  pending={sched.EE.Bus.Intc.IsInterruptPending}");
            Console.WriteLine($"  IOP SR:    0x{iopSR:x8}  IEc={B(iopIEc)} BEV={B(iopBEV)} IM2={B(iopIM2)}");
            Console.WriteLine($"  IOP Cause: 0x{iopCause:x8}  IP2={B(iopIP2)}");
            Console.WriteLine(
                $"  IOP I_STAT: 0x{iopIStat:x8}  I_MASK: 0x{iopIMask:x8}  I_CTRL: 0x{sched.IOP.Bus.ICtrl:x8}  pending={sched.IOP.Bus.IsInterruptPending}");
            Console.WriteLine("=============================");
        }

        private static string B(bool v) => v ? "1" : "0";

        private static void DumpIopRamRegion(IopMemoryBus bus, uint startAddr, uint endAddr)
        {
            Console.WriteLine($"  IOP RAM disasm 0x{startAddr:x8}-0x{endAddr:x8}:");
            for (uint addr = startAddr; addr < endAddr; addr += 4)
            {
                try
                {
                    uint iword = bus.ReadWord(addr);
                    string dis = MipsDisassembler.Disassemble(iword, addr);
                    Console.WriteLine($"    0x{addr:x8}: 0x{iword:x8}  {dis}");
                }
                catch
                {
                    Console.WriteLine($"    0x{addr:x8}: ??");
                }
            }
        }

        private static void DumpAllIopRegs(IopState state)
        {
            string[] rn = {"zero","at  ","v0  ","v1  ","a0  ","a1  ","a2  ","a3  ",
                            "t0  ","t1  ","t2  ","t3  ","t4  ","t5  ","t6  ","t7  ",
                            "s0  ","s1  ","s2  ","s3  ","s4  ","s5  ","s6  ","s7  ",
                            "t8  ","t9  ","k0  ","k1  ","gp  ","sp  ","fp  ","ra  "};
            for (int i = 0; i < 32; i += 4)
            {
                Console.WriteLine(
                    $"  ${i:D2} {rn[i]}=0x{state.ReadReg(i):x8}  " +
                    $"${i + 1:D2} {rn[i + 1]}=0x{state.ReadReg(i + 1):x8}  " +
                    $"${i + 2:D2} {rn[i + 2]}=0x{state.ReadReg(i + 2):x8}  " +
                    $"${i + 3:D2} {rn[i + 3]}=0x{state.ReadReg(i + 3):x8}");
            }

            Console.WriteLine($"  HI=0x{state.HI:x8}  LO=0x{state.LO:x8}  PC=0x{state.PC:x8}  SR=0x{state.SR:x8}");
        }

        private static string DisassembleIopAt(IopMemoryBus bus, uint pc)
        {
            try
            {
                uint word = bus.ReadWord(pc);
                return MipsDisassembler.Disassemble(word, pc);
            }
            catch
            {
                return "??";
            }
        }

        private static string GetPcRegion(uint vaddr)
        {
            uint phys = PS2MemoryBus.TranslateAddress(vaddr);
            if (phys >= 0x1FC00000u && phys < 0x20000000u)
            {
                return (vaddr & 0xC0000000u) == 0xA0000000u ? "KSEG1/BIOS" : "KSEG0/BIOS";
            }

            if (phys < 0x02000000u)
            {
                return "RAM";
            }

            if (vaddr >= 0x70000000u && vaddr < 0x70004000u)
            {
                return "SPad";
            }

            if (phys >= 0x10000000u && phys < 0x10010000u)
            {
                return "I/O";
            }

            if (phys >= 0x12000000u && phys < 0x12002000u)
            {
                return "GS";
            }

            return "?";
        }

        private static string DisassembleAt(PS2MemoryBus bus, uint pc)
        {
            try
            {
                uint word = bus.ReadWord(pc);
                return MipsDisassembler.Disassemble(word, pc);
            }
            catch
            {
                return "??";
            }
        }

        private static string BiosCallName(int code)
        {
            string[] bios =
            {
                "RFU000_FullReset", "ResetEE",             "SetGsCrt",             "RFU003",
                "Exit",             "RFU005",               "LoadExecPS2",          "ExecPS2",
                "RFU008",           "RFU009",               "AddSbusIntcHandler",   "RemoveSbusIntcHandler",
                "Interrupt2Iop",    "SetVTLBRefillHandler", "SetVCommonHandler",    "SetVInterruptHandler",
                "AddIntcHandler",   "RemoveIntcHandler",    "AddDmacHandler",       "RemoveDmacHandler",
                "_EnableIntc",      "_DisableIntc",         "_EnableDmac",          "_DisableDmac",
                "_SetAlarm",        "_ReleaseAlarm",        "_iEnableIntc",         "_iDisableIntc",
                "_iEnableDmac",     "_iDisableDmac",        "_iSetAlarm",           "_iReleaseAlarm",
                "CreateThread",          "DeleteThread",      "StartThread",              "ExitThread",
                "ExitDeleteThread",      "TerminateThread",   "iTerminateThread",         "DisableDispatchThread",
                "EnableDispatchThread",  "ChangeThreadPriority", "iChangeThreadPriority", "RotateThreadReadyQueue",
                "iRotateThreadReadyQueue","ReleaseWaitThread","iReleaseWaitThread",        "GetThreadId",
                "ReferThreadStatus","iReferThreadStatus",   "SleepThread",          "WakeupThread",
                "_iWakeupThread",   "CancelWakeupThread",   "iCancelWakeupThread",  "SuspendThread",
                "iSuspendThread",   "ResumeThread",         "iResumeThread",        "JoinThread",
                "RFU060",           "RFU061",               "EndOfHeap",            "RFU063",
                "CreateSema",       "DeleteSema",           "SignalSema",           "iSignalSema",
                "WaitSema",         "PollSema",             "iPollSema",            "ReferSemaStatus",
                "iReferSemaStatus", "RFU073",               "SetOsdConfigParam",    "GetOsdConfigParam",
                "GetGsHParam",      "GetGsVParam",          "SetGsHParam",          "SetGsVParam",
                "RFU080_CreateEventFlag",  "RFU081_DeleteEventFlag",
                "RFU082_SetEventFlag",     "RFU083_iSetEventFlag",
                "RFU084_ClearEventFlag",   "RFU085_iClearEventFlag",
                "RFU086_WaitEventFlag",    "RFU087_PollEventFlag",
                "RFU088_iPollEventFlag",   "RFU089_ReferEventFlagStatus",
                "RFU090_iReferEventFlagStatus", "RFU091_GetEntryAddress",
                "EnableIntcHandler_iEnableIntcHandler",
                "DisableIntcHandler_iDisableIntcHandler",
                "EnableDmacHandler_iEnableDmacHandler",
                "DisableDmacHandler_iDisableDmacHandler",
                "KSeg0",            "EnableCache",          "DisableCache",         "GetCop0",
                "FlushCache",       "RFU101",               "CpuConfig",            "iGetCop0",
                "iFlushCache",      "RFU105",               "iCpuConfig",           "sceSifStopDma",
                "SetCPUTimerHandler","SetCPUTimer",          "SetOsdConfigParam2",   "GetOsdConfigParam2",
                "GsGetIMR_iGsGetIMR",               "GsGetIMR_iGsPutIMR",   "SetPgifHandler",               "SetVSyncFlag",
                "RFU116",                           "print",                "sceSifDmaStat_isceSifDmaStat", "sceSifSetDma_isceSifSetDma",
                "sceSifSetDChain_isceSifSetDChain", "sceSifSetReg",         "sceSifGetReg",                 "ExecOSD",
                "Deci2Call",                        "PSMode",               "MachineType",                  "GetMemorySize",
            };

            int idx = code & 0xFF;
            return idx < bios.Length ? bios[idx] : $"syscall_{idx:x2}";
        }
    }
}
