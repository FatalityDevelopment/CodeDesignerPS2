using CodeDesigner.Mips64LE.Common.CPU;

namespace CodeDesigner.Mips64LE.Common.CPU.COP0
{
    /// <summary>
    /// Exception codes for the COP0 Cause register ExcCode field (bits 6:2).
    /// Matches the MIPS R5900 / PS2 EE specification.
    /// </summary>
    public enum ExceptionCode : uint
    {
        Interrupt        = 0,   // External interrupt
        TlbModification  = 1,   // TLB modification
        TlbLoad          = 2,   // TLB load / instruction fetch miss
        TlbStore         = 3,   // TLB store miss
        AddressLoad      = 4,   // Address error on load or instruction fetch
        AddressStore     = 5,   // Address error on store
        InstructionBus   = 6,   // Instruction bus error
        DataBus          = 7,   // Data bus error
        Syscall          = 8,   // SYSCALL instruction
        Breakpoint       = 9,   // BREAK instruction
        ReservedInstr    = 10,  // Reserved / unrecognised instruction
        CopUnusable      = 11,  // Coprocessor unusable
        Overflow         = 12,  // Arithmetic overflow (ADD/ADDI/DADD/DADDI)
        Trap             = 13,  // TGE/TLT/TEQ/TNE and immediate variants
    }

    /// <summary>
    /// COP0 exception dispatcher for the PS2 Emotion Engine.
    ///
    /// On any exception this performs three steps, matching PCSX2 behaviour:
    ///   1. If not already in an exception (Status.EXL == 0):
    ///        • Write the faulting instruction address to EPC (COP0 reg 14).
    ///          If the instruction is in a branch delay slot, EPC is set to the
    ///          preceding branch instruction (epc − 4) and Cause.BD is set.
    ///        • Set Status.EXL = 1 to enter exception mode.
    ///   2. Write the exception code into Cause.ExcCode (bits 6:2).
    ///   3. Redirect cpu.PC to the appropriate exception vector:
    ///        • EXL was 0, BEV=0 → 0x80000080  (RAM handler, installed by kernel)
    ///        • EXL was 0, BEV=1 → 0xBFC00200  (BIOS ROM handler)
    ///        • EXL was already 1 (nested / double fault) → 0xBFC00200 always.
    ///
    /// The nested-exception escalation matches PCSX2
    /// pcsx2/R5900OpcodeImpl.cpp cpuException:
    ///   "if (Status.EXL || Status.BEV) vector = 0xBFC00200"
    ///
    /// This is critical for the BIOS boot sequence: the BEV=0 exception handler
    /// installed at 0x80000080 begins with a BREAK instruction to dispatch
    /// software traps via the BIOS ROM.  Without the EXL escalation the BREAK
    /// fires again (still at 0x80000080 with EXL=1), creating an infinite loop.
    /// With escalation the nested BREAK routes to 0xBFC00200 (BIOS ROM) which
    /// is the correct PS2 software trap dispatcher.
    /// </summary>
    public static class COP0ExceptionDispatcher
    {
        // General exception vector addresses (R5900 / PS2 EE).
        // Reference: PCSX2 R5900OpcodeImpl.cpp cpuException.
        private const uint ExceptionVectorBev0 = 0x80000080u;
        private const uint ExceptionVectorBev1 = 0xBFC00200u;  // EE BIOS dispatcher

        // TLB refill exception vector (used for TlbLoad/TlbStore/TlbModification when EXL=0)
        private const uint TlbRefillVectorBev0 = 0x80000000u;
        private const uint TlbRefillVectorBev1 = 0xBFC00200u;  // same as general for EE BEV=1

        // COP0 register indices
        private const int StatusReg = 12;
        private const int CauseReg  = 13;
        private const int EpcReg    = 14;

        // Status register bit masks
        private const uint StatusExl = 1u << 1;   // Exception Level
        private const uint StatusBev = 1u << 22;  // Bootstrap Exception Vectors

        // Cause register bit masks / shifts
        private const uint CauseBd           = 1u << 31; // Branch Delay slot
        private const int  CauseExcCodeShift = 2;
        private const uint CauseExcCodeMask  = 0x7Cu;    // bits 6:2

        /// <summary>
        /// Dispatch an exception: update COP0 state and redirect the program counter.
        /// </summary>
        /// <param name="cpu">CPU state to update.</param>
        /// <param name="code">Exception code to record in Cause.ExcCode.</param>
        /// <param name="epc">Address of the instruction that caused the exception.</param>
        /// <param name="inDelaySlot">
        ///   True if the faulting instruction resides in a branch delay slot.
        ///   In that case EPC is set to the branch instruction (epc − 4) and
        ///   Cause.BD is set.
        /// </param>
        public static void Dispatch(CpuState cpu, ExceptionCode code, ulong epc, bool inDelaySlot)
        {
            uint status = cpu.COP0.ReadRegister32(StatusReg);

            // Remember whether we were outside an exception before this fault.
            bool exlWasZero = (status & StatusExl) == 0;

            // Only save EPC and set EXL when not already handling an exception.
            if (exlWasZero)
            {
                uint cause = cpu.COP0.ReadRegister32(CauseReg);

                if (inDelaySlot)
                {
                    // EPC points to the branch instruction (epc − 4); BD flag set.
                    cpu.COP0.WriteRegister64(EpcReg, epc - 4);
                    cause |= CauseBd;
                }
                else
                {
                    cpu.COP0.WriteRegister64(EpcReg, epc);
                    cause &= ~CauseBd;
                }

                // Write ExcCode and BD together in a single read-modify-write.
                cause = (cause & ~CauseExcCodeMask) | ((uint)code << CauseExcCodeShift);
                cpu.COP0.WriteRegister32(CauseReg, cause);

                // Enter exception level.
                status |= StatusExl;
                cpu.COP0.WriteRegister32(StatusReg, status);
            }
            else
            {
                // Already in exception level — only update ExcCode in Cause.
                uint cause = cpu.COP0.ReadRegister32(CauseReg);
                cause = (cause & ~CauseExcCodeMask) | ((uint)code << CauseExcCodeShift);
                cpu.COP0.WriteRegister32(CauseReg, cause);
            }

            // Select exception vector.
            //
            // PCSX2 cpuException rule (R5900OpcodeImpl.cpp):
            //   if (EXL was already 1) OR (BEV=1) → vector = 0xBFC00200
            //   else                               → vector = 0x80000080
            //
            // The "EXL already 1" path is the nested-exception (double-fault) case.
            // When the BEV=0 kernel handler at 0x80000080 starts with BREAK (the PS2
            // BIOS software-trap convention), the resulting BREAK fires while EXL=1 and
            // must escalate to the BIOS ROM dispatcher at 0xBFC00200, not loop back.
            //
            // TLB refill exceptions use a different vector only for the first-level
            // fault (EXL was 0); double-faulting TLB exceptions also escalate.
            bool isTlbException = code == ExceptionCode.TlbLoad
                                || code == ExceptionCode.TlbStore
                                || code == ExceptionCode.TlbModification;

            // Re-read status to get the current BEV (may have been altered above).
            bool wasBev = (status & StatusBev) != 0;

            uint vector;
            if (!exlWasZero)
            {
                // Nested exception: always escalate to BIOS ROM handler.
                vector = ExceptionVectorBev1;
            }
            else if (isTlbException)
            {
                vector = wasBev ? TlbRefillVectorBev1 : TlbRefillVectorBev0;
            }
            else
            {
                vector = wasBev ? ExceptionVectorBev1 : ExceptionVectorBev0;
            }

            cpu.PC = vector;
        }
    }
}
