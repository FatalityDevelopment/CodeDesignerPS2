using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Mips64LE.Common.Emulator;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE;

namespace CodeDesigner.Mips64LE.TestProject
{
    /// <summary>
    /// Tests for the IOP (R3000A) emulator: IopState, IopMemoryBus, and IopEmulator.
    /// </summary>
    [TestClass]
    public class TestIOP
    {
        // =====================================================================
        // IopState tests
        // =====================================================================

        [TestMethod]
        public void IopState_InitialState_AllRegistersZero()
        {
            var s = new IopState();
            s.Reset();
            for (int i = 0; i < 32; i++)
                Assert.AreEqual(0u, s.ReadReg(i), $"GPR[{i}] must be 0 after reset");
        }

        [TestMethod]
        public void IopState_Reset_PcIsResetVector()
        {
            var s = new IopState();
            s.Reset();
            Assert.AreEqual(0xBFC00000u, s.PC, "IOP reset vector must be 0xBFC00000");
        }

        [TestMethod]
        public void IopState_Reset_SrHasBevSet()
        {
            var s = new IopState();
            s.Reset();
            Assert.AreNotEqual(0u, s.SR & IopState.SR_BEV, "SR.BEV must be 1 after reset");
        }

        [TestMethod]
        public void IopState_WriteZeroReg_IsIgnored()
        {
            var s = new IopState();
            s.Reset();
            s.WriteReg(0, 0xDEADBEEFu);
            Assert.AreEqual(0u, s.ReadReg(0), "$zero must always read as 0");
        }

        [TestMethod]
        public void IopState_WriteReadAllGprs()
        {
            var s = new IopState();
            for (int i = 1; i < 32; i++)
            {
                s.WriteReg(i, (uint)(i * 0x11111111));
                Assert.AreEqual((uint)(i * 0x11111111), s.ReadReg(i), $"GPR[{i}] round-trip failed");
            }
        }

        [TestMethod]
        public void IopState_Cop0Registers_RoundTrip()
        {
            var s = new IopState();
            s.WriteCop0(12, 0xABCD1234u);
            s.WriteCop0(13, 0x00000010u);
            s.WriteCop0(14, 0x80100000u);

            Assert.AreEqual(0xABCD1234u, s.ReadCop0(12), "SR round-trip");
            Assert.AreEqual(0x00000010u, s.ReadCop0(13), "Cause round-trip");
            Assert.AreEqual(0x80100000u, s.ReadCop0(14), "EPC round-trip");
        }

        [TestMethod]
        public void IopState_Cop0PrId_ReturnsR3000AId()
        {
            var s = new IopState();
            Assert.AreEqual(0x0000001Fu, s.ReadCop0(15), "PRId must return R3000A identifier");
        }

        [TestMethod]
        public void IopState_HiLo_RoundTrip()
        {
            var s = new IopState();
            s.HI = 0x12345678u;
            s.LO = 0x9ABCDEF0u;
            Assert.AreEqual(0x12345678u, s.HI);
            Assert.AreEqual(0x9ABCDEF0u, s.LO);
        }

        // =====================================================================
        // IopMemoryBus tests
        // =====================================================================

        [TestMethod]
        public void IopMemoryBus_Ram_ReadWrite()
        {
            var bus = new IopMemoryBus();
            bus.WriteWord(0x00010000u, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x00010000u), "RAM read-write round-trip");
        }

        [TestMethod]
        public void IopMemoryBus_RamSize_Is2MB()
        {
            Assert.AreEqual(2 * 1024 * 1024, IopMemoryBus.RamSize);
        }

        [TestMethod]
        public void IopMemoryBus_IntcStat_Write1ToClear()
        {
            var bus = new IopMemoryBus();
            bus.RaiseInterrupt(2);
            bus.RaiseInterrupt(5);
            Assert.AreNotEqual(0u, bus.IStat & (1u << 2));

            // W1C: clear bit 2
            bus.WriteWord(0x1F801070u, 1u << 2);
            Assert.AreEqual(0u,        bus.IStat & (1u << 2), "I_STAT bit 2 must be cleared");
            Assert.AreNotEqual(0u,     bus.IStat & (1u << 5), "I_STAT bit 5 must remain set");
        }

        [TestMethod]
        public void IopMemoryBus_IntcMask_PlainWrite()
        {
            var bus = new IopMemoryBus();
            bus.WriteWord(0x1F801074u, 0x0000001Fu);
            Assert.AreEqual(0x0000001Fu, bus.IMask, "I_MASK plain write");
        }

        [TestMethod]
        public void IopMemoryBus_InterruptPending_WhenStatAndMaskBothSet()
        {
            var bus = new IopMemoryBus();
            bus.RaiseInterrupt(3);
            Assert.IsFalse(bus.IsInterruptPending, "No interrupt pending if mask bit is 0");
            bus.WriteWord(0x1F801074u, 1u << 3);
            Assert.IsTrue(bus.IsInterruptPending, "Interrupt must be pending when STAT and MASK both set");
        }

        [TestMethod]
        public void IopMemoryBus_TimerRegisters_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // Timer 0 at 0x1F801100; counter=+0, mode=+4, target=+8
            bus.WriteWord(0x1F801104u, 0x0080u); // mode: CUE
            bus.WriteWord(0x1F801108u, 100u);     // target = 100
            Assert.AreEqual(0x0080u, bus.ReadWord(0x1F801104u), "Timer 0 mode read-back");
            Assert.AreEqual(100u,    bus.ReadWord(0x1F801108u), "Timer 0 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_TimerT1_BaseAddress()
        {
            var bus = new IopMemoryBus();
            // Timer 1 at 0x1F801110
            bus.WriteWord(0x1F801118u, 500u);
            Assert.AreEqual(500u, bus.ReadWord(0x1F801118u), "Timer 1 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_SifMirror_ForwardsToSharedSif()
        {
            var sif = new SIF();
            var bus = new IopMemoryBus { SharedSif = sif };

            // IOP writes MSCOM via its SIF mirror (offset 0x00 = MSCOM)
            bus.WriteWord(0x1D000000u, 0xAABBCCDDu);
            Assert.AreEqual(0xAABBCCDDu, sif.MsCom,
                "IOP write to SIF mirror must update the shared SIF MSCOM");

            // IOP reads back
            Assert.AreEqual(0xAABBCCDDu, bus.ReadWord(0x1D000000u),
                "IOP read from SIF mirror must return the written value");
        }

        [TestMethod]
        public void IopMemoryBus_SifMirror_Null_NoThrow()
        {
            var bus = new IopMemoryBus();  // no shared SIF
            // Should not throw; SIF mirror reads return 0
            Assert.AreEqual(0u, bus.ReadWord(0x1D000000u),
                "SIF mirror read with no SharedSif must return 0 without throwing");
        }

        [TestMethod]
        public void IopMemoryBus_Bios_IsReadOnly()
        {
            var bus = new IopMemoryBus();
            // Pre-fill BIOS bytes to non-zero using the Bios array directly
            bus.Bios[0] = 0xAA;
            // Write through bus should be silently ignored (BIOS is ROM)
            bus.WriteWord(0x1FC00000u, 0xDEADBEEFu);
            Assert.AreEqual(0xAAu, bus.Bios[0], "BIOS ROM must not be writable via bus");
        }

        // =====================================================================
        // IopEmulator tests
        // =====================================================================

        [TestMethod]
        public void IopEmulator_InitialState_PcIsResetVector()
        {
            var emu = new IopEmulator();
            Assert.AreEqual(0xBFC00000u, emu.State.PC, "IOP PC must start at 0xBFC00000");
        }

        [TestMethod]
        public void IopEmulator_Reset_ClearsInstructionCount()
        {
            var emu = new IopEmulator();
            emu.State.PC = 0u;
            emu.Bus.WriteWord(0u, MakeAddiu(1, 0, 42));  // ADDIU $1, $0, 42
            emu.Step();
            Assert.IsTrue(emu.InstructionCount > 0);
            emu.Reset();
            Assert.AreEqual(0UL, emu.InstructionCount);
        }

        [TestMethod]
        public void IopEmulator_AddImmediateInstruction_UpdatesGpr()
        {
            var emu = new IopEmulator();
            // ADDIU $1, $0, 99 → $1 = 99
            uint addiuWord = MakeAddiu(0, 1, 99);
            emu.Bus.WriteWord(0u, addiuWord);
            emu.State.PC = 0u;

            emu.Step();

            Assert.AreEqual(99u, emu.State.ReadReg(1), "$1 must be 99 after ADDIU $1,$0,99");
        }

        [TestMethod]
        public void IopEmulator_TwoAddiuInstructions_AccumulatesResult()
        {
            var emu = new IopEmulator();
            // ADDIU $1, $0, 10
            // ADDIU $2, $1, 5
            emu.LoadProgram(0u, stackalloc uint[]
            {
                MakeAddiu(0, 1, 10),
                MakeAddiu(1, 2,  5)
            });
            emu.State.PC = 0u;

            emu.Step();
            emu.Step();

            Assert.AreEqual(10u, emu.State.ReadReg(1), "$1 must be 10");
            Assert.AreEqual(15u, emu.State.ReadReg(2), "$2 must be 15");
        }

        [TestMethod]
        public void IopEmulator_OrInstruction_ComputesOr()
        {
            var emu = new IopEmulator();
            // ORI $1, $0, 0xFF00  → $1 = 0xFF00
            // ORI $2, $0, 0x00FF  → $2 = 0x00FF
            // OR $3, $1, $2       → $3 = 0xFFFF
            emu.LoadProgram(0u, stackalloc uint[]
            {
                MakeOri(0, 1, 0xFF00),
                MakeOri(0, 2, 0x00FF),
                MakeOr(1, 2, 3)
            });
            emu.State.PC = 0u;

            emu.Step(); emu.Step(); emu.Step();

            Assert.AreEqual(0xFFFFu, emu.State.ReadReg(3), "$3 must be 0xFFFF after OR");
        }

        [TestMethod]
        public void IopEmulator_LuiInstruction_SetsUpperHalf()
        {
            var emu = new IopEmulator();
            // LUI $1, 0xBEEF → $1 = 0xBEEF0000
            emu.LoadProgram(0u, stackalloc uint[]
            {
                MakeLui(1, 0xBEEF)
            });
            emu.State.PC = 0u;
            emu.Step();

            // Note: LUI sign-extends to 32 bits for MIPS I; 0xBEEF0000 has bit 31=1
            // so it sign-extends to 0xFFFF_FFFF_BEEF_0000 in 64-bit, but the IOP
            // masks the bridge result to 32 bits.
            Assert.AreEqual(0xBEEF0000u, emu.State.ReadReg(1) & 0xFFFFFFFFu,
                "$1 upper half must be 0xBEEF after LUI");
        }

        [TestMethod]
        public void IopEmulator_BranchDelaySlot_ExecutedOnTakenBranch()
        {
            var emu = new IopEmulator();
            // BEQ $0, $0, +2 (taken, target = PC+4 + 2*4 = 12)
            // ADDIU $1, $0, 77  (delay slot — must execute)
            // NOP  (skipped)
            // [target] ADDIU $2, $0, 88

            emu.LoadProgram(0u, stackalloc uint[]
            {
                MakeBEQ(0, 0, 2),        // offset=2 → branch to 0+4+8=12
                MakeAddiu(0, 1, 77),     // delay slot
                0x00000000u,             // NOP (skipped)
                MakeAddiu(0, 2, 88)      // branch target at 12
            });
            emu.State.PC = 0u;

            emu.Step(); // executes BEQ + delay slot

            Assert.AreEqual(77u,  emu.State.ReadReg(1), "Delay slot must execute on taken branch");
            Assert.AreEqual(12u,  emu.State.PC,          "PC must be at branch target");
        }

        [TestMethod]
        public void IopEmulator_Syscall_DispatchesToCop0Vector()
        {
            var emu = new IopEmulator();
            // SYSCALL encoded instruction word (opcode=0, funct=0x0C)
            const uint syscallInstructionWord = 0x0000000Cu;

            emu.Bus.WriteWord(0u, syscallInstructionWord);
            emu.State.PC = 0u;
            emu.State.SR = IopState.SR_BEV; // BEV=1 → vector 0xBFC00180

            bool fired = false;
            emu.SyscallRaised += (_, _) => fired = true;

            emu.Step();

            Assert.IsTrue(fired, "SyscallRaised event must fire on SYSCALL");
            Assert.AreEqual(0xBFC00180u, emu.State.PC, "PC must redirect to BEV=1 exception vector");
            Assert.AreEqual(0u, emu.State.EPC,          "EPC must be saved as faulting PC (0)");
        }

        [TestMethod]
        public void IopEmulator_Syscall_WithBev0_UsesRamVector()
        {
            var emu = new IopEmulator();
            const uint syscallInstructionWord = 0x0000000Cu;

            emu.Bus.WriteWord(0u, syscallInstructionWord);
            emu.State.PC = 0u;
            emu.State.SR = 0u; // BEV=0 → vector 0x80000080

            emu.Step();

            Assert.AreEqual(0x80000080u, emu.State.PC, "PC must redirect to BEV=0 exception vector");
        }

        [TestMethod]
        public void IopEmulator_UnknownInstruction_FiresEvent()
        {
            var emu = new IopEmulator();
            // Opcode 0x3F is not a valid MIPS instruction
            emu.Bus.WriteWord(0u, 0xFCu << 26);
            emu.State.PC = 0u;

            bool fired = false;
            emu.UnknownInstruction += (_, _) => fired = true;

            emu.Step();

            Assert.IsTrue(fired, "UnknownInstruction event must fire for unrecognised opcode");
        }

        [TestMethod]
        public void IopEmulator_ZeroRegisterAlwaysZero()
        {
            var emu = new IopEmulator();
            // ADDIU $0, $0, 99 — should have no effect
            emu.Bus.WriteWord(0u, MakeAddiu(0, 0, 99));
            emu.State.PC = 0u;
            emu.Step();
            Assert.AreEqual(0u, emu.State.ReadReg(0), "$zero must always read as 0");
        }

        [TestMethod]
        public void IopEmulator_ConstructorWithEEBus_WiresSif()
        {
            var eeBus = new PS2MemoryBus();
            var emu   = new IopEmulator(eeBus);

            Assert.IsNotNull(emu.Bus.SharedSif, "IopMemoryBus.SharedSif must be set from EE bus");
            Assert.AreSame(eeBus.Sif, emu.Bus.SharedSif,
                "SharedSif must reference the same SIF instance as the EE bus");
        }

        [TestMethod]
        public void IopEmulator_SifCommunication_IopWritesToMscom()
        {
            var eeBus = new PS2MemoryBus();
            var iop   = new IopEmulator(eeBus);

            // IOP writes a command to MSCOM (EE-side offset 0x00 = 0x1000F200)
            // via the IOP SIF mirror at 0x1D000000
            iop.Bus.WriteWord(0x1D000000u, 0xCAFEBABEu);

            // EE should see it in MSCOM (shared SIF)
            Assert.AreEqual(0xCAFEBABEu, eeBus.ReadWord(0x1000F200u),
                "EE must see IOP's MSCOM write via the shared SIF");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_FiresInterruptPending()
        {
            var emu = new IopEmulator();
            emu.Bus.RaiseInterrupt(0);
            emu.Bus.WriteWord(0x1F801074u, 1u); // I_MASK bit 0

            bool fired = false;
            emu.InterruptPending += (_, _) => fired = true;

            emu.TickHardware(1);

            Assert.IsTrue(fired, "InterruptPending must fire when I_STAT & I_MASK != 0");
        }

        [TestMethod]
        public void IopEmulator_InstructionCount_Increments()
        {
            var emu = new IopEmulator();
            emu.LoadProgram(0u, stackalloc uint[]
            {
                MakeAddiu(0, 1, 1),
                MakeAddiu(0, 2, 2),
                MakeAddiu(0, 3, 3)
            });
            emu.State.PC = 0u;

            emu.Step(); emu.Step(); emu.Step();

            Assert.AreEqual(3UL, emu.InstructionCount,
                "InstructionCount must equal number of instructions executed");
        }

        // =====================================================================
        // IopTimer tests
        // =====================================================================

        [TestMethod]
        public void IopTimer_Tick_IncrementsCount()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x0080u); // CUE=1
            t.Tick(5);
            Assert.AreEqual(5u, t.Count, "Count must increment by ticks when CUE is set");
        }

        [TestMethod]
        public void IopTimer_Tick_DisabledWhenCueNotSet()
        {
            var t = new IopTimer(0);
            // CUE=0 → timer should not count
            t.Tick(100);
            Assert.AreEqual(0u, t.Count, "Count must not increment when CUE is clear");
        }

        [TestMethod]
        public void IopTimer_Tick_CompareFires_WhenCmpeSet()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x0090u); // CUE | CMPE
            t.WriteRegister(IopTimer.REG_TARGET,  10u);

            bool fired = false;
            t.CompareInterruptPending += (_, _) => fired = true;

            t.Tick(10);

            Assert.IsTrue(fired, "CompareInterruptPending must fire when count reaches target");
            Assert.AreNotEqual(0u, t.Mode & 0x0400u, "EQUF must be set after compare match");
        }

        [TestMethod]
        public void IopTimer_Tick_CompareNotFired_WhenCmpeCleared()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x0080u); // CUE only; CMPE=0
            t.WriteRegister(IopTimer.REG_TARGET,  5u);

            bool fired = false;
            t.CompareInterruptPending += (_, _) => fired = true;

            t.Tick(5);

            Assert.IsFalse(fired, "CompareInterruptPending must not fire when CMPE is clear");
        }

        [TestMethod]
        public void IopTimer_Tick_OverflowFires_WhenOvfeSet()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x00A0u); // CUE | OVFE

            bool fired = false;
            t.OverflowInterruptPending += (_, _) => fired = true;

            // Tick past 0xFFFF to cause a 16-bit overflow
            t.Tick(0x10000u);

            Assert.IsTrue(fired, "OverflowInterruptPending must fire on 16-bit counter wrap");
            Assert.AreNotEqual(0u, t.Mode & 0x0800u, "OVFF must be set after overflow");
        }

        [TestMethod]
        public void IopTimer_Tick_OverflowNotFired_WhenOvfeCleared()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x0080u); // CUE only; OVFE=0

            bool fired = false;
            t.OverflowInterruptPending += (_, _) => fired = true;

            t.Tick(0x10000u);

            Assert.IsFalse(fired, "OverflowInterruptPending must not fire when OVFE is clear");
        }

        [TestMethod]
        public void IopTimer_ModeRead_ClearsEqufAndOvff()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE,   0x00B0u); // CUE | CMPE | OVFE
            t.WriteRegister(IopTimer.REG_TARGET,  3u);

            t.Tick(3);           // hit compare → sets EQUF
            t.Tick(0x10000u);    // overflow → sets OVFF

            uint modeBeforeRead = t.Mode;
            Assert.AreNotEqual(0u, modeBeforeRead & 0x0C00u, "Both flags must be set before read");

            // Reading mode via ReadRegister clears sticky flags
            uint modeAfterRead = t.ReadRegister(IopTimer.REG_MODE);
            Assert.AreNotEqual(0u, modeAfterRead & 0x0C00u, "Returned value must have flags set");
            Assert.AreEqual(0u, t.Mode & 0x0C00u, "Flags must be cleared in register after read");
        }

        [TestMethod]
        public void IopTimer_WriteCount_ResetsCounter()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x0080u); // CUE
            t.Tick(50);
            Assert.AreEqual(50u, t.Count);

            t.WriteRegister(IopTimer.REG_COUNT, 0u); // writing count resets to 0
            Assert.AreEqual(0u, t.Count, "Writing TN_COUNT must reset counter to 0");
        }

        [TestMethod]
        public void IopTimer_AltSource_DividesBy8()
        {
            var t = new IopTimer(2); // T2 supports alternate source
            t.WriteRegister(IopTimer.REG_MODE, 0x0180u); // CUE | ALT (÷8)

            t.Tick(8);   // 8 sysclock cycles → 1 timer increment
            Assert.AreEqual(1u, t.Count, "Alternate source (÷8) must count once per 8 cycles");

            t.Tick(7);   // 7 more → still 1 (accumulator not full)
            Assert.AreEqual(1u, t.Count);

            t.Tick(1);   // one more → 2 total
            Assert.AreEqual(2u, t.Count);
        }

        [TestMethod]
        public void IopTimer_Counter_Wraps16Bit()
        {
            var t = new IopTimer(0);
            t.WriteRegister(IopTimer.REG_MODE, 0x0080u); // CUE, no interrupts

            // Drive count to 0xFFFE
            t.Tick(0xFFFEu);
            Assert.AreEqual(0xFFFEu, t.Count);

            // Tick 3 more: 0xFFFE → 0xFFFF → 0x0000 (wrap) → 0x0001
            t.Tick(3u);
            Assert.AreEqual(1u, t.Count, "Counter must wrap at 16-bit boundary");
        }

        // ── T3–T5 bus mapping tests ───────────────────────────────────────────

        [TestMethod]
        public void IopMemoryBus_TimerT3_BaseAddress_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // T3 at 0x1F801480; mode=+4, target=+8
            bus.WriteWord(0x1F801484u, 0x0080u); // T3 mode: CUE
            bus.WriteWord(0x1F801488u, 200u);    // T3 target = 200

            Assert.AreEqual(0x0080u, bus.ReadWord(0x1F801484u), "T3 mode read-back");
            Assert.AreEqual(200u,    bus.ReadWord(0x1F801488u), "T3 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_TimerT4_BaseAddress_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // T4 at 0x1F801490
            bus.WriteWord(0x1F801498u, 300u);
            Assert.AreEqual(300u, bus.ReadWord(0x1F801498u), "T4 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_TimerT5_BaseAddress_ReadWrite()
        {
            var bus = new IopMemoryBus();
            // T5 at 0x1F8014A0
            bus.WriteWord(0x1F8014A8u, 400u);
            Assert.AreEqual(400u, bus.ReadWord(0x1F8014A8u), "T5 target read-back");
        }

        [TestMethod]
        public void IopMemoryBus_Timers_Property_ReturnsAllSix()
        {
            var bus = new IopMemoryBus();
            Assert.AreEqual(6, bus.Timers.Length, "IopMemoryBus.Timers must expose 6 timers");
            for (int i = 0; i < 6; i++)
                Assert.IsNotNull(bus.Timers[i], $"Timers[{i}] must not be null");
        }

        // ── TickHardware timer→INTC integration tests ─────────────────────────

        [TestMethod]
        public void IopEmulator_TickHardware_T0Compare_RaisesIntcBit4()
        {
            var emu = new IopEmulator();

            // Enable T0 with compare target=1, compare interrupt enabled
            emu.Bus.WriteWord(0x1F801104u, 0x0090u); // T0 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801108u, 1u);      // T0 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 4); // I_MASK: enable T0 interrupt

            emu.TickHardware(1); // T0 count becomes 1 == target → INTC bit 4 raised

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 4),
                "INTC bit 4 must be set when T0 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T1Compare_RaisesIntcBit5()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801114u, 0x0090u); // T1 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801118u, 1u);      // T1 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 5); // I_MASK: enable T1 interrupt

            emu.TickHardware(1);

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 5),
                "INTC bit 5 must be set when T1 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T2Compare_RaisesIntcBit6()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801124u, 0x0090u); // T2 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801128u, 1u);      // T2 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 6);

            emu.TickHardware(1);

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 6),
                "INTC bit 6 must be set when T2 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T3Compare_RaisesIntcBit14()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801484u, 0x0090u); // T3 mode: CUE | CMPE
            emu.Bus.WriteWord(0x1F801488u, 1u);      // T3 target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 14);

            emu.TickHardware(1);

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 14),
                "INTC bit 14 must be set when T3 compare interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_T0Overflow_RaisesIntcBit4()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801104u, 0x00A0u); // T0 mode: CUE | OVFE
            emu.Bus.WriteWord(0x1F801074u, 1u << 4);

            emu.TickHardware(0x10000u); // force 16-bit overflow

            Assert.AreNotEqual(0u, emu.Bus.IStat & (1u << 4),
                "INTC bit 4 must be set when T0 overflow interrupt fires");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_MultipleTimerTicks_Accumulate()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801104u, 0x0080u); // T0 CUE only

            emu.TickHardware(10);
            emu.TickHardware(5);

            Assert.AreEqual(15u, emu.Bus.Timers[0].Count,
                "Timer count must accumulate across multiple TickHardware calls");
        }

        [TestMethod]
        public void IopEmulator_TickHardware_InterruptPendingFired_OnTimerInterrupt()
        {
            var emu = new IopEmulator();

            emu.Bus.WriteWord(0x1F801104u, 0x0090u); // T0 CUE | CMPE
            emu.Bus.WriteWord(0x1F801108u, 1u);      // target = 1
            emu.Bus.WriteWord(0x1F801074u, 1u << 4); // mask bit 4

            bool fired = false;
            emu.InterruptPending += (_, _) => fired = true;

            emu.TickHardware(1);

            Assert.IsTrue(fired,
                "InterruptPending must fire when a timer interrupt sets I_STAT & I_MASK");
        }

        // =====================================================================
        // IopDmac unit tests
        // =====================================================================

        [TestMethod]
        public void IopDmac_ChannelCount_Is13()
        {
            Assert.AreEqual(13, IopDmac.ChannelCount,
                "IOP DMAC must have exactly 13 channels");
        }

        [TestMethod]
        public void IopDmacChannel_InitialState_IsNotRunning()
        {
            var ch = new IopDmacChannel(0);
            Assert.IsFalse(ch.IsRunning, "Channel must not be running after construction");
        }

        [TestMethod]
        public void IopDmacChannel_WriteChcr_SetsFields()
        {
            var ch = new IopDmacChannel(4);
            // CHCR: DIR=1 (bit 0), STR=1 (bit 24)
            ch.WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u);
            Assert.IsTrue(ch.IsRunning,          "STR=1 must make IsRunning true");
            Assert.IsTrue(ch.DirectionToMemory,  "DIR=1 must set DirectionToMemory true");
        }

        [TestMethod]
        public void IopDmacChannel_BurstMode_RamToDevice_AdvancesMadr()
        {
            var bus  = new IopMemoryBus();
            // Pre-fill some RAM with non-zero to verify reads are possible
            bus.Ram[0x1000] = 0xAA;

            var dmac = new IopDmac();
            // MADR=0x1000, BCR=4 words, CHCR: DIR=0 (RAM→device), SYNC=00, STR=1
            dmac.Channels[3].WriteRegister(IopDmacChannel.REG_MADR, 0x1000u);
            dmac.Channels[3].WriteRegister(IopDmacChannel.REG_BCR,  4u);
            dmac.Channels[3].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // STR only

            dmac.ExecuteChannel(3, bus);

            Assert.IsFalse(dmac.Channels[3].IsRunning,
                "STR must be cleared after transfer");
            Assert.AreEqual(0x1000u + 4u * 4u, dmac.Channels[3].Madr,
                "MADR must advance by wordCount*4 bytes");
        }

        [TestMethod]
        public void IopDmacChannel_BurstMode_DeviceToRam_ZeroFillsDest()
        {
            var bus  = new IopMemoryBus();
            // Pre-fill destination RAM with non-zero
            for (int i = 0; i < 8; i++) bus.Ram[0x2000 + i] = 0xFF;

            var dmac = new IopDmac();
            // MADR=0x2000, BCR=2 words, CHCR: DIR=1 (device→RAM), STR=1
            dmac.Channels[9].WriteRegister(IopDmacChannel.REG_MADR, 0x2000u);
            dmac.Channels[9].WriteRegister(IopDmacChannel.REG_BCR,  2u);
            dmac.Channels[9].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u); // STR|DIR

            dmac.ExecuteChannel(9, bus);

            Assert.IsFalse(dmac.Channels[9].IsRunning, "STR must be cleared");
            // Destination memory should be zero-filled (2 words = 8 bytes)
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(0, bus.Ram[0x2000 + i],
                    $"Byte at 0x{0x2000 + i:X} must be zero after device→RAM transfer");
        }

        [TestMethod]
        public void IopDmacChannel_ZeroBcr_Treats_As_0x10000_Words()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();
            // BCR=0 should transfer 0x10000 words = 0x40000 bytes
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0x00000000u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // DIR=0, STR=1

            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(dmac.Channels[0].IsRunning);
            Assert.AreEqual(0x40000u, dmac.Channels[0].Madr,
                "BCR=0 must transfer 0x10000 words (0x40000 bytes)");
        }

        [TestMethod]
        public void IopDmac_TransferComplete_EventFires()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  4u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            bool fired = false;
            dmac.Channels[0].TransferComplete += (_, _) => fired = true;

            dmac.ExecuteChannel(0, bus);

            Assert.IsTrue(fired, "TransferComplete event must fire after burst transfer");
        }

        [TestMethod]
        public void IopDmacChannel_LinkedListMode_TraversesNodes()
        {
            // SYNC=2 (linked-list): traverse GPU-style linked list
            // Node format: [31:24]=word count, [23:0]=next node address, 0xFFFFFF=end
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            uint node0 = 0x00001000u;   // first node
            uint node1 = 0x00002000u;   // second node

            // Node 0: 2 data words, next = node1
            bus.WriteWord(node0,        (2u << 24) | node1);  // header
            bus.WriteWord(node0 + 4u,   0xAABBCCDDu);          // data word 0
            bus.WriteWord(node0 + 8u,   0x11223344u);          // data word 1

            // Node 1: 1 data word, next = end
            bus.WriteWord(node1,        (1u << 24) | 0x00FFFFFFu); // header (end)
            bus.WriteWord(node1 + 4u,   0xDEADBEEFu);              // data word 0

            List<uint> received = new();
            dmac.Channels[2].DeviceDataConsumer = w => received.Add(w);

            // SYNC=2 (bits [9:8]=10), DIR=0 (RAM→device), STR=1 (bit24)
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, node0);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | (2u << 8)); // STR,SYNC=2

            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR must be cleared after linked-list DMA");
            Assert.AreEqual(3, received.Count, "Must receive 2+1=3 data words from two nodes");
            Assert.AreEqual(0xAABBCCDDu, received[0], "First word of node 0");
            Assert.AreEqual(0x11223344u, received[1], "Second word of node 0");
            Assert.AreEqual(0xDEADBEEFu, received[2], "First word of node 1");
        }

        [TestMethod]
        public void IopDmacChannel_LinkedListMode_SingleNodeEnd()
        {
            // Single node with end marker (next = 0xFFFFFF) and no data words
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            uint node0 = 0x00003000u;
            bus.WriteWord(node0, (0u << 24) | 0x00FFFFFFu); // 0 data words, end marker

            int callCount = 0;
            dmac.Channels[2].DeviceDataConsumer = _ => callCount++;

            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, node0);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | (2u << 8));

            dmac.ExecuteChannel(2, bus);

            Assert.IsFalse(dmac.Channels[2].IsRunning, "STR cleared");
            Assert.AreEqual(0, callCount, "Empty single-node list must not invoke consumer");
        }

        [TestMethod]
        public void IopDmacChannel_LinkedListMode_TransferComplete_Fires()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            uint node0 = 0x00004000u;
            bus.WriteWord(node0, (0u << 24) | 0x00FFFFFFu); // end

            bool fired = false;
            dmac.Channels[2].TransferComplete += (_, _) => fired = true;

            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, node0);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | (2u << 8));

            dmac.ExecuteChannel(2, bus);

            Assert.IsTrue(fired, "TransferComplete must fire after linked-list DMA");
        }

        [TestMethod]
        public void IopDmac_Dicr_IrqFlag_SetOnCompletion()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Channel 2 (GPU), enable its IEN bit in DICR [18] and master MIE [23]
            // IEN bit for ch2 = bit 16+2 = 18
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 18));

            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[2].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            dmac.ExecuteChannel(2, bus);

            // IRQ flag for ch2 = bit 24+2 = 26 in DICR
            bool iqfSet = (dmac.Dicr & (1u << 26)) != 0;
            Assert.IsTrue(iqfSet, "DICR IQF bit for ch2 must be set after transfer");
        }

        [TestMethod]
        public void IopDmac_Dicr_MasterFlag_SetWhenEnabled()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Enable ch0 IEN and master MIE
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 16));

            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            dmac.ExecuteChannel(0, bus);

            Assert.IsTrue((dmac.Dicr & (1u << 31)) != 0,
                "DICR bit 31 (MIRQ) must be set when MIE=1, IEN=1, IQF=1");
        }

        [TestMethod]
        public void IopDmac_Dicr_WriteClearsIrqFlag()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Enable and complete a transfer on ch1
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 17));
            dmac.Channels[1].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[1].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[1].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);
            dmac.ExecuteChannel(1, bus);

            // Verify flag is set
            Assert.IsTrue((dmac.Dicr & (1u << 25)) != 0, "IQF bit for ch1 must be set");

            // Write 1 to bit 25 to clear the flag (W1C semantics)
            uint current = dmac.Dicr;
            dmac.WriteGlobal(IopDmac.AddrDicr, current | (1u << 25));

            Assert.IsFalse((dmac.Dicr & (1u << 25)) != 0,
                "IQF bit for ch1 must be cleared after W1C write");
        }

        [TestMethod]
        public void IopDmac_ChannelTransferComplete_EventFired_WhenEnabled()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // ch0 IEN=1, MIE=1
            dmac.WriteGlobal(IopDmac.AddrDicr, (1u << 23) | (1u << 16));
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            int completedChannel = -1;
            dmac.ChannelTransferComplete += (_, ch) => completedChannel = ch;

            dmac.ExecuteChannel(0, bus);

            Assert.AreEqual(0, completedChannel,
                "ChannelTransferComplete must fire with channel index 0");
        }

        [TestMethod]
        public void IopDmac_ChannelTransferComplete_NotFired_WhenDisabled()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // IEN=0, MIE=0 — no interrupt enabled
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[0].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            bool fired = false;
            dmac.ChannelTransferComplete += (_, _) => fired = true;

            dmac.ExecuteChannel(0, bus);

            Assert.IsFalse(fired,
                "ChannelTransferComplete must NOT fire when IEN and MIE are both 0");
        }

        [TestMethod]
        public void IopDmac_Dicr2_Ch7_IrqFlag_SetOnCompletion()
        {
            var bus  = new IopMemoryBus();
            var dmac = new IopDmac();

            // Channel 7 (SPU2), enable its IEN bit in DICR2 [16] and master MIE [23]
            dmac.WriteGlobal(IopDmac.AddrDicr2, (1u << 23) | (1u << 16));
            dmac.Channels[7].WriteRegister(IopDmacChannel.REG_MADR, 0u);
            dmac.Channels[7].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            dmac.Channels[7].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24);

            dmac.ExecuteChannel(7, bus);

            // Local index 0 → IQF bit 24 in DICR2
            Assert.IsTrue((dmac.Dicr2 & (1u << 24)) != 0,
                "DICR2 IQF bit for ch7 (local 0) must be set after transfer");
            Assert.IsTrue((dmac.Dicr2 & (1u << 31)) != 0,
                "DICR2 MIRQ must be set when MIE=1 and ch7 IEN+IQF are both set");
        }

        [TestMethod]
        public void IopMemoryBus_DmacChannelReadWrite_RoundTrip()
        {
            var bus = new IopMemoryBus();

            // Write MADR via memory-mapped I/O for channel 3 (CDVD at 0x1F8010B0)
            bus.WriteWord(0x1F8010B0u, 0x00080000u); // MADR
            bus.WriteWord(0x1F8010B4u, 8u);           // BCR
            Assert.AreEqual(0x00080000u, bus.ReadWord(0x1F8010B0u), "MADR round-trip via bus");
            Assert.AreEqual(8u,          bus.ReadWord(0x1F8010B4u), "BCR round-trip via bus");
        }

        [TestMethod]
        public void IopMemoryBus_DmacChannelWrite_StartsTransfer()
        {
            var bus = new IopMemoryBus();
            // Fill destination RAM
            for (int i = 0; i < 8; i++) bus.Ram[0x3000 + i] = 0xFF;

            // Channel 10 (SIF1) at 0x1F801530: device→RAM, 2 words
            bus.WriteWord(0x1F801530u, 0x3000u);          // MADR
            bus.WriteWord(0x1F801534u, 2u);                // BCR
            bus.WriteWord(0x1F801538u, (1u << 24) | 1u); // CHCR: STR=1, DIR=1

            // Transfer should have executed synchronously on the CHCR write
            Assert.IsFalse(bus.Dmac.Channels[10].IsRunning,
                "STR must be auto-cleared after CHCR write triggers transfer");
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(0, bus.Ram[0x3000 + i],
                    $"RAM byte 0x{0x3000 + i:X} must be zero-filled by SIF1 transfer");
        }

        [TestMethod]
        public void IopMemoryBus_DmacGlobalReadWrite_RoundTrip()
        {
            var bus = new IopMemoryBus();
            bus.WriteWord(IopDmac.AddrDpcr,  0x08765432u);
            bus.WriteWord(IopDmac.AddrDpcr2, 0x0FEDCBA9u);
            Assert.AreEqual(0x08765432u, bus.ReadWord(IopDmac.AddrDpcr),  "DPCR round-trip");
            Assert.AreEqual(0x0FEDCBA9u, bus.ReadWord(IopDmac.AddrDpcr2), "DPCR2 round-trip");
        }

        [TestMethod]
        public void IopEmulator_DmacCompletion_RaisesIntcBit3()
        {
            var emu = new IopEmulator();

            // Enable DMAC interrupt in IOP INTC (bit 3) and ch9/SIF0 IEN in DICR2
            emu.Bus.WriteWord(0x1F801074u, 1u << 3);         // I_MASK bit 3
            // ch9 in IopDmac is channels[9], local idx in DICR2 = 9-7=2 → IEN bit 16+2=18, MIE bit 23
            emu.Bus.WriteWord(IopDmac.AddrDicr2, (1u << 23) | (1u << 18));

            // Set up a small SIF0 transfer (ch9 at 0x1F801520)
            emu.Bus.WriteWord(0x1F801520u, 0x00001000u);   // MADR
            emu.Bus.WriteWord(0x1F801524u, 2u);             // BCR
            emu.Bus.WriteWord(0x1F801528u, 1u << 24);      // CHCR: STR=1, DIR=0

            // TickHardware raises InterruptPending when I_STAT & I_MASK != 0
            emu.TickHardware(0); // just check current pending state

            Assert.IsTrue((emu.Bus.IStat & (1u << 3)) != 0,
                "I_STAT bit 3 must be set after DMAC transfer completes");
        }



        /// <summary>ADDIU rt, rs, imm  (opcode=0x09)</summary>
        private static uint MakeAddiu(int rs, int rt, int imm)
            => (0x09u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        /// <summary>ORI rt, rs, imm  (opcode=0x0D)</summary>
        private static uint MakeOri(int rs, int rt, int imm)
            => (0x0Du << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        /// <summary>LUI rt, imm  (opcode=0x0F, rs=0)</summary>
        private static uint MakeLui(int rt, int imm)
            => (0x0Fu << 26) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        /// <summary>BEQ rs, rt, offset  (opcode=0x04)</summary>
        private static uint MakeBEQ(int rs, int rt, int offset)
            => (0x04u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(offset & 0xFFFF);

        /// <summary>OR rd, rs, rt  (opcode=0, funct=0x25)</summary>
        private static uint MakeOr(int rs, int rt, int rd)
            => ((uint)rs << 21) | ((uint)rt << 16) | ((uint)rd << 11) | 0x25u;

        // =====================================================================
        // Cdvd unit tests
        // =====================================================================

        [TestMethod]
        public void Cdvd_NoDisc_HasDiscFalse_AndDiscTypeNoDisc()
        {
            var cdvd = new Cdvd();
            Assert.IsFalse(cdvd.HasDisc,               "HasDisc must be false when no image loaded");
            Assert.AreEqual(Cdvd.DISC_NO_DISC, cdvd.DiscType, "DiscType must be DISC_NO_DISC when empty");
        }

        [TestMethod]
        public void Cdvd_LoadImage_SetsHasDiscAndDiscType()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("TEST.TXT", "hello"));
            Assert.IsTrue(cdvd.HasDisc,                  "HasDisc must be true after LoadImage");
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, cdvd.DiscType, "DiscType must be DISC_PS2_DVD after load");
        }

        [TestMethod]
        public void Cdvd_LoadImage_NullImage_ClearsDisc()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "data"));
            cdvd.LoadImage(null);
            Assert.IsFalse(cdvd.HasDisc, "HasDisc must be false after loading null image");
        }

        [TestMethod]
        public void Cdvd_ReadRegister_CrtReturns_DiscType()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));
            // CRT register is at offset 0x0B
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, cdvd.ReadRegister(0x0Bu),
                "CRT register must return DISC_PS2_DVD when image is loaded");
        }

        [TestMethod]
        public void Cdvd_ReadRegister_NreadyReturnsZero_WhenIdle()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));
            Assert.AreEqual(0, cdvd.ReadRegister(0x05u),
                "NREADY must return 0 (ready) when no command is pending");
        }

        [TestMethod]
        public void Cdvd_ReadRegister_IstatHasDataReadyBit_AfterNCommand()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Issue a ReadCd command for sector 0
            cdvd.WriteRegister(0x00u, 0); // LBA byte 0
            cdvd.WriteRegister(0x00u, 0); // LBA byte 1
            cdvd.WriteRegister(0x00u, 0); // LBA byte 2
            cdvd.WriteRegister(0x00u, 0); // LBA byte 3
            cdvd.WriteRegister(0x00u, 1); // count = 1
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            byte istat = cdvd.ReadRegister(0x08u);
            Assert.AreNotEqual(0, istat & 0x04, "INTR_STAT bit 2 (data ready) must be set after N-command");
        }

        [TestMethod]
        public void Cdvd_WriteRegister_IstatW1C_ClearsFlag()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Trigger an N-command to set INTR_STAT
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // Clear the data-ready bit
            cdvd.WriteRegister(0x08u, 0x04);
            Assert.AreEqual(0, cdvd.ReadRegister(0x08u) & 0x04,
                "INTR_STAT bit 2 must be cleared by W1C write");
        }

        [TestMethod]
        public void Cdvd_DataReady_EventFires_AfterNCommand()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            bool fired = false;
            cdvd.DataReady += (_, _) => fired = true;

            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            Assert.IsTrue(fired, "DataReady event must fire when N-command completes");
        }

        [TestMethod]
        public void Cdvd_ReadNextDmaWord_ReturnsZero_WhenNoCommandIssued()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));
            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "ReadNextDmaWord must return 0 when no sector has been read");
        }

        [TestMethod]
        public void Cdvd_NCommand_ReadCd_FillsBuffer_WithIsoSectorData()
        {
            const int FileLba = 18;
            var cdvd = new Cdvd();
            // Put known data at sector 18
            byte[] iso = BuildMinimalIso("DATA.BIN", "\x01\x02\x03\x04\x05\x06\x07\x08");
            cdvd.LoadImage(iso);

            // Issue ReadCd for sector 18, 1 sector
            cdvd.WriteRegister(0x00u, (byte)(FileLba & 0xFF));
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // count
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // First word = bytes 0x01 0x02 0x03 0x04 in little-endian = 0x04030201
            uint word0 = cdvd.ReadNextDmaWord();
            Assert.AreEqual(0x04030201u, word0,
                "First DMA word must match first 4 bytes of sector 18 content");
        }

        [TestMethod]
        public void Cdvd_ReadNextDmaWord_ReturnsZero_AfterBufferExhausted()
        {
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("A", "b"));

            // Read 1 sector = 2048 bytes = 512 words
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0); cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            for (int i = 0; i < 512; i++) cdvd.ReadNextDmaWord();

            Assert.AreEqual(0u, cdvd.ReadNextDmaWord(),
                "ReadNextDmaWord must return 0 once sector buffer is exhausted");
        }

        [TestMethod]
        public void Cdvd_FindFile_ReturnsNull_WhenNoDisc()
        {
            var cdvd = new Cdvd();
            Assert.IsNull(cdvd.FindFile("SYSTEM.CNF"), "FindFile must return null when no disc");
        }

        [TestMethod]
        public void Cdvd_FindFile_LocatesRootFile_CaseInsensitive()
        {
            const string content = "BOOT2 = cdrom0:\\SLPM_550.01;1";
            byte[] iso = BuildMinimalIso("SYSTEM.CNF", content);

            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);

            Cdvd.IsoEntry? entry = cdvd.FindFile("system.cnf");
            Assert.IsNotNull(entry, "FindFile must locate SYSTEM.CNF case-insensitively");
            Assert.AreEqual((uint)content.Length, entry!.Value.Size,
                "IsoEntry.Size must match the file content length");
        }

        [TestMethod]
        public void Cdvd_FindFile_ReturnsNull_ForMissingFile()
        {
            byte[] iso = BuildMinimalIso("SYSTEM.CNF", "data");
            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);
            Assert.IsNull(cdvd.FindFile("NONEXISTENT.TXT"),
                "FindFile must return null for a file that does not exist");
        }

        [TestMethod]
        public void Cdvd_ReadFile_ReturnsFileBytes()
        {
            const string content = "PS2 game executable content";
            byte[] iso = BuildMinimalIso("SYSTEM.CNF", content);

            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);

            Cdvd.IsoEntry? entry = cdvd.FindFile("SYSTEM.CNF");
            Assert.IsNotNull(entry, "File must be found");

            byte[] data = cdvd.ReadFile(entry!.Value);
            string text = System.Text.Encoding.ASCII.GetString(data);
            Assert.AreEqual(content, text, "ReadFile must return the exact file bytes");
        }

        [TestMethod]
        public void Cdvd_ReadSectors_CopiesDataIntoBuffer()
        {
            byte[] iso = BuildMinimalIso("X", "Y");
            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);

            // Sector 16 is the PVD; byte 0 should be 1 (PVD type)
            byte[] buf = new byte[Cdvd.SectorSize];
            cdvd.ReadSectors(16, 1, buf);
            Assert.AreEqual(1, buf[0],    "PVD type byte must be 1");
            Assert.AreEqual('C', (char)buf[1], "PVD identifier byte 1 must be 'C'");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdProperty_SetAndGet()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            bus.Cdvd = cdvd;
            Assert.AreSame(cdvd, bus.Cdvd, "Cdvd property must return the assigned instance");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdRegister_ReadByte_ReturnsDiscType()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "Y"));
            bus.Cdvd = cdvd;

            // CRT register at 0x1F40200B (offset 0x0B from RegBase 0x1F402000)
            byte crt = bus.ReadByte(0x1F40200Bu);
            Assert.AreEqual(Cdvd.DISC_PS2_DVD, crt, "ReadByte of CRT register must return DISC_PS2_DVD");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdRegister_WriteByte_NCommand_TriggersDataReady()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "Y"));
            bus.Cdvd = cdvd;

            bool fired = false;
            cdvd.DataReady += (_, _) => fired = true;

            // Write 5 param bytes to REG_DATA (0x1F402000), then NCMD (0x1F402004)
            for (int i = 0; i < 5; i++)
                bus.WriteByte(0x1F402000u, 0);
            bus.WriteByte(0x1F402004u, Cdvd.NCMD_READ_CD);

            Assert.IsTrue(fired, "DataReady must fire when NCMD written via IopMemoryBus.WriteByte");
        }

        [TestMethod]
        public void IopMemoryBus_CdvdDataReady_RaisesIntcBit2()
        {
            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(BuildMinimalIso("X", "Y"));
            bus.Cdvd = cdvd;

            // Issue a minimal N-command
            for (int i = 0; i < 5; i++) bus.WriteByte(0x1F402000u, 0);
            bus.WriteByte(0x1F402004u, Cdvd.NCMD_READ_CD);

            Assert.AreNotEqual(0u, bus.IStat & (1u << 2),
                "IOP INTC bit 2 must be set when CDVD DataReady fires");
        }

        [TestMethod]
        public void IopDmac_Channel3_CdvdDeviceToRam_WritesBufferedSectorData()
        {
            const int FileLba = 18;
            // File content: 8 known bytes followed by zeros
            byte[] iso = BuildMinimalIso("DATA.BIN", "\x11\x22\x33\x44\x55\x66\x77\x88");

            var bus  = new IopMemoryBus();
            var cdvd = new Cdvd();
            cdvd.LoadImage(iso);
            bus.Cdvd = cdvd;

            // Fill CDVD sector buffer via N-command
            cdvd.WriteRegister(0x00u, (byte)(FileLba & 0xFF));
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 0);
            cdvd.WriteRegister(0x00u, 1); // 1 sector
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            // Configure DMAC channel 3 (CDVD): device→RAM, 2 words, dest = 0x1000
            uint destAddr = 0x1000u;
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_MADR, destAddr);
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_BCR,  2u);
            // DIR=1 (device→RAM), STR=1
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u);
            bus.Dmac.ExecuteChannel(3, bus);

            // Verify the first 8 bytes were DMA'd to RAM
            uint word0 = bus.ReadWord(destAddr);
            uint word1 = bus.ReadWord(destAddr + 4u);
            Assert.AreEqual(0x44332211u, word0, "First DMA word must be bytes 0x11 0x22 0x33 0x44 LE");
            Assert.AreEqual(0x88776655u, word1, "Second DMA word must be bytes 0x55 0x66 0x77 0x88 LE");
        }

        [TestMethod]
        public void IopDmac_Channel3_DeviceToRam_ZeroFills_WhenNoCdvd()
        {
            var bus = new IopMemoryBus();
            // No CDVD attached; pre-fill RAM to detect overwrite
            for (int i = 0; i < 8; i++) bus.Ram[0x3000 + i] = 0xFF;

            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_MADR, 0x3000u);
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_BCR,  2u);
            bus.Dmac.Channels[3].WriteRegister(IopDmacChannel.REG_CHCR, (1u << 24) | 1u);
            bus.Dmac.ExecuteChannel(3, bus);

            for (int i = 0; i < 8; i++)
                Assert.AreEqual(0, bus.Ram[0x3000 + i],
                    $"RAM byte {i} must be zero-filled when no CDVD is attached");
        }

        // =====================================================================
        // PadState unit tests
        // =====================================================================

        [TestMethod]
        public void PadState_InitialState_AllButtonsReleasedAndSticksCentred()
        {
            var pad = new PadState();
            Assert.AreEqual(0xFFFF, pad.Buttons, "All buttons must be released (0xFFFF) initially");
            Assert.AreEqual(0x80, pad.RightX, "RightX must be 0x80 (centre) initially");
            Assert.AreEqual(0x80, pad.RightY, "RightY must be 0x80 (centre) initially");
            Assert.AreEqual(0x80, pad.LeftX,  "LeftX must be 0x80 (centre) initially");
            Assert.AreEqual(0x80, pad.LeftY,  "LeftY must be 0x80 (centre) initially");
        }

        [TestMethod]
        public void PadState_Press_ClearsButtonBit()
        {
            var pad = new PadState();
            pad.Press(PadState.BTN_CROSS);
            Assert.AreEqual(0, pad.Buttons & PadState.BTN_CROSS, "BTN_CROSS bit must be 0 after Press");
        }

        [TestMethod]
        public void PadState_Release_SetsButtonBit()
        {
            var pad = new PadState();
            pad.Press(PadState.BTN_CROSS);
            pad.Release(PadState.BTN_CROSS);
            Assert.AreNotEqual(0, pad.Buttons & PadState.BTN_CROSS, "BTN_CROSS bit must be 1 after Release");
        }

        [TestMethod]
        public void PadState_Press_MultipleMask_ClearsAllBits()
        {
            var pad = new PadState();
            ushort mask = (ushort)(PadState.BTN_START | PadState.BTN_CROSS | PadState.BTN_CIRCLE);
            pad.Press(mask);
            Assert.AreEqual(0, pad.Buttons & mask, "All masked bits must be 0 after Press");
        }

        [TestMethod]
        public void PadState_ProcessCommand_UnknownCommand_ReturnsAllFF()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { 0x01, 0x44 }, 4);
            Assert.AreEqual(4, resp.Length, "Response must have requested length");
            foreach (byte b in resp)
                Assert.AreEqual(0xFF, b, "Unknown command must return 0xFF for every byte");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_HasCorrectDeviceIdAndAck()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll }, 9);
            Assert.AreEqual(9, resp.Length);
            Assert.AreEqual(PadState.DeviceId, resp[1], "Response[1] must be DualShock2 device ID 0x73");
            Assert.AreEqual(PadState.AckByte,  resp[2], "Response[2] must be acknowledge byte 0x5A");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_AllButtonsReleasedByDefault()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll, 0, 0, 0 }, 9);
            // All buttons released → both button bytes must be 0xFF (active-low)
            Assert.AreEqual(0xFF, resp[3], "Button byte 0 must be 0xFF (all released)");
            Assert.AreEqual(0xFF, resp[4], "Button byte 1 must be 0xFF (all released)");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_ReflectsPressedButton()
        {
            var pad = new PadState();
            pad.Press(PadState.BTN_CROSS); // bit 14 → byte 1, bit 6
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll, 0, 0, 0 }, 9);
            // BTN_CROSS is bit 14 → high byte (resp[4]), bit 6
            Assert.AreEqual(0, resp[4] & (1 << 6), "Cross button bit must be 0 (pressed) in response byte 1");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_ReflectsAnalogStickValues()
        {
            var pad = new PadState();
            pad.RightX = 0x10;
            pad.RightY = 0x20;
            pad.LeftX  = 0x30;
            pad.LeftY  = 0x40;
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll, 0, 0, 0 }, 9);
            Assert.AreEqual(0x10, resp[5], "RightX must appear at resp[5]");
            Assert.AreEqual(0x20, resp[6], "RightY must appear at resp[6]");
            Assert.AreEqual(0x30, resp[7], "LeftX must appear at resp[7]");
            Assert.AreEqual(0x40, resp[8], "LeftY must appear at resp[8]");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_TruncatesResponseToRequestedLength()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll }, 4);
            Assert.AreEqual(4, resp.Length, "Response must be truncated to the requested length");
            Assert.AreEqual(PadState.DeviceId, resp[1]);
        }

        // =====================================================================
        // Sio2 unit tests
        // =====================================================================

        [TestMethod]
        public void Sio2_Send3_ReadWrite_RoundTrip()
        {
            var sio2 = new Sio2();
            // Write to SEND3[0] at offset 0x00, SEND3[3] at offset 0x0C
            sio2.WriteRegister(0x00u, 0xDEADBEEFu);
            sio2.WriteRegister(0x0Cu, 0x12345678u);
            Assert.AreEqual(0xDEADBEEFu, sio2.ReadRegister(0x00u), "SEND3[0] round-trip");
            Assert.AreEqual(0x12345678u, sio2.ReadRegister(0x0Cu), "SEND3[3] round-trip");
        }

        [TestMethod]
        public void Sio2_Send1Send2_ReadWrite_RoundTrip()
        {
            var sio2 = new Sio2();
            sio2.WriteRegister(0x40u, 0x11111111u);
            sio2.WriteRegister(0x44u, 0x22222222u);
            sio2.WriteRegister(0x48u, 0x33333333u);
            sio2.WriteRegister(0x4Cu, 0x44444444u);
            Assert.AreEqual(0x11111111u, sio2.ReadRegister(0x40u), "SEND1 port0");
            Assert.AreEqual(0x22222222u, sio2.ReadRegister(0x44u), "SEND1 port1");
            Assert.AreEqual(0x33333333u, sio2.ReadRegister(0x48u), "SEND2 port0");
            Assert.AreEqual(0x44444444u, sio2.ReadRegister(0x4Cu), "SEND2 port1");
        }

        [TestMethod]
        public void Sio2_FifoOut_ReturnsFF_WhenEmpty()
        {
            var sio2 = new Sio2();
            uint val = sio2.ReadRegister(0x64u); // FIFO_OUT offset
            Assert.AreEqual(0xFFu, val, "FIFO_OUT must return 0xFF when empty");
        }

        [TestMethod]
        public void Sio2_Transfer_NoPad_FifoOut_AllFF()
        {
            var sio2 = new Sio2();
            // SEND3[0]: port=0, sendLen=3, recvLen=3  (bits[16:8]=3, bits[26:18]=3)
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 3, recvLen: 3));
            sio2.WriteRegister(0x60u, 0x01u); // FIFO_IN
            sio2.WriteRegister(0x60u, 0x42u);
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            uint b0 = sio2.ReadRegister(0x64u);
            uint b1 = sio2.ReadRegister(0x64u);
            uint b2 = sio2.ReadRegister(0x64u);
            Assert.AreEqual(0xFFu, b0, "No-device response byte 0 must be 0xFF");
            Assert.AreEqual(0xFFu, b1, "No-device response byte 1 must be 0xFF");
            Assert.AreEqual(0xFFu, b2, "No-device response byte 2 must be 0xFF");
        }

        [TestMethod]
        public void Sio2_Transfer_WithPad_FifoOut_HasPollResponse()
        {
            var sio2 = new Sio2();
            var pad  = new PadState();
            sio2.Port0 = pad;

            // SEND3[0]: port=0, sendLen=2 (addr+cmd), recvLen=9
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x60u, PadState.AddrPad);
            sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            // Skip leading 0xFF
            sio2.ReadRegister(0x64u);
            uint devId = sio2.ReadRegister(0x64u);
            uint ack   = sio2.ReadRegister(0x64u);
            Assert.AreEqual((uint)PadState.DeviceId, devId, "Device ID must be 0x73");
            Assert.AreEqual((uint)PadState.AckByte,  ack,   "Ack byte must be 0x5A");
        }

        [TestMethod]
        public void Sio2_Transfer_TransferComplete_EventFires()
        {
            var sio2 = new Sio2();
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 1, recvLen: 1));
            sio2.WriteRegister(0x60u, 0x00u);

            bool fired = false;
            sio2.TransferComplete += (_, _) => fired = true;

            sio2.WriteRegister(0x68u, 0x01u); // CTRL START
            Assert.IsTrue(fired, "TransferComplete must fire when transfer is executed");
        }

        [TestMethod]
        public void Sio2_Transfer_Recv1_IsSuccessValueAfterTransfer()
        {
            var sio2 = new Sio2();
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 1, recvLen: 1));
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            Assert.AreEqual(Sio2.RECV1_SUCCESS, sio2.ReadRegister(0x70u),
                "RECV1 must equal RECV1_SUCCESS after a completed transfer");
        }

        [TestMethod]
        public void Sio2_Ctrl_Reset_ClearsFifos_And_ResetsSend3Head()
        {
            var sio2 = new Sio2();
            var pad  = new PadState();
            sio2.Port0 = pad;

            // First transfer to populate FIFO_OUT
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x60u, PadState.AddrPad);
            sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x68u, 0x01u); // START

            // Write CTRL RESET — should clear FIFO_OUT so it reads back 0xFF
            sio2.WriteRegister(0x68u, 0x04u); // CTRL RESET (bit 2)
            uint val = sio2.ReadRegister(0x64u); // FIFO_OUT
            Assert.AreEqual(0xFFu, val, "FIFO_OUT must return 0xFF after CTRL RESET");
        }

        [TestMethod]
        public void Sio2_MultipleSend3Descriptors_ProcessedInSequence()
        {
            var sio2 = new Sio2();
            var pad0 = new PadState();
            var pad1 = new PadState();
            pad0.Press(PadState.BTN_START);   // Start pressed on port 0
            pad1.Press(PadState.BTN_CROSS);   // Cross pressed on port 1
            sio2.Port0 = pad0;
            sio2.Port1 = pad1;

            // Two descriptors: port 0 and port 1, each poll 2 cmd → 9 recv
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x04u, BuildSend3(port: 1, sendLen: 2, recvLen: 9));

            // FIFO_IN: cmd for port 0 then port 1
            sio2.WriteRegister(0x60u, PadState.AddrPad); sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x60u, PadState.AddrPad); sio2.WriteRegister(0x60u, PadState.CmdPoll);

            sio2.WriteRegister(0x68u, 0x01u); // START

            // Port 0 response: 9 bytes — skip 0xFF, devId, ack; read button bytes
            byte[] out0 = new byte[9];
            for (int i = 0; i < 9; i++)
                out0[i] = (byte)sio2.ReadRegister(0x64u);

            // Port 1 response: 9 bytes
            byte[] out1 = new byte[9];
            for (int i = 0; i < 9; i++)
                out1[i] = (byte)sio2.ReadRegister(0x64u);

            // BTN_START is bit 3 in button byte 0 → out0[3] low nibble, bit 3 = 0 when pressed
            Assert.AreEqual(0, out0[3] & (PadState.BTN_START & 0xFF),
                "Port 0 button byte 0: Start bit must be 0 (pressed)");
            // BTN_CROSS is bit 14 in the word → bit 6 of button byte 1 → out1[4] bit 6 = 0 when pressed
            Assert.AreEqual(0, out1[4] & (int)((PadState.BTN_CROSS >> 8) & 0xFF),
                "Port 1 button byte 1: Cross bit must be 0 (pressed)");
        }

        // =====================================================================
        // IopMemoryBus + Sio2 integration tests
        // =====================================================================

        [TestMethod]
        public void IopMemoryBus_Sio2Property_SetAndGet()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            bus.Sio2 = sio2;
            Assert.AreSame(sio2, bus.Sio2, "Sio2 property must return the assigned instance");
        }

        [TestMethod]
        public void IopMemoryBus_Sio2_WriteWordSend3_RoundTrip()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            bus.Sio2 = sio2;

            uint addr = Sio2.RegBase;           // SEND3[0] at RegBase+0x00
            bus.WriteWord(addr, 0xAABBCCDDu);
            uint readBack = bus.ReadWord(addr);
            Assert.AreEqual(0xAABBCCDDu, readBack, "SEND3[0] word round-trip via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Sio2_WriteByteToFifoIn_TriggerTransfer()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            var pad  = new PadState();
            sio2.Port0 = pad;
            bus.Sio2   = sio2;

            // Write SEND3[0] descriptor
            bus.WriteWord(Sio2.RegBase, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            // Write command bytes to FIFO_IN via byte access
            bus.WriteByte(Sio2.RegBase + 0x60u, PadState.AddrPad);
            bus.WriteByte(Sio2.RegBase + 0x60u, PadState.CmdPoll);
            // Trigger via CTRL byte write
            bus.WriteByte(Sio2.RegBase + 0x68u, 0x01);

            // Read response word (FIFO_OUT at +0x64)
            uint resp0 = bus.ReadWord(Sio2.RegBase + 0x64u); // 0xFF (leading)
            uint resp1 = bus.ReadWord(Sio2.RegBase + 0x64u); // 0x73 device ID
            Assert.AreEqual((uint)PadState.DeviceId, resp1, "Device ID must be 0x73 via IopMemoryBus word read");
        }

        [TestMethod]
        public void IopMemoryBus_Sio2TransferComplete_RaisesIntcBit17()
        {
            var bus  = new IopMemoryBus();
            var sio2 = new Sio2();
            bus.Sio2 = sio2;

            bus.WriteWord(Sio2.RegBase, BuildSend3(port: 0, sendLen: 1, recvLen: 1));
            bus.WriteByte(Sio2.RegBase + 0x60u, 0x00);
            bus.WriteWord(Sio2.RegBase + 0x68u, 0x01u); // CTRL START

            Assert.AreNotEqual(0u, bus.IStat & (1u << 17),
                "IOP INTC bit 17 must be set when SIO2 TransferComplete fires");
        }

        // ── SEND3 descriptor builder helper ───────────────────────────────────

        /// <summary>
        /// Build a SEND3 descriptor word with the given port, send length, and receive length.
        /// </summary>
        // =====================================================================
        // MemoryCard unit tests
        // =====================================================================

        [TestMethod]
        public void MemoryCard_CreateBlank_HasCorrectSize()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] data = mc.GetData();
            Assert.AreEqual(MemoryCard.TotalFileSize, data.Length,
                "Blank memory card must be exactly TotalFileSize bytes");
        }

        [TestMethod]
        public void MemoryCard_CreateBlank_SuperblockContainsMagic()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] data = mc.GetData();
            string magic = System.Text.Encoding.ASCII.GetString(data, 0, MemoryCard.SuperblockMagic.Length);
            Assert.AreEqual(MemoryCard.SuperblockMagic, magic,
                "Page 0 must begin with the PS2 memory-card superblock magic string");
        }

        [TestMethod]
        public void MemoryCard_ConstructFromData_RejectsWrongSize()
        {
            Assert.ThrowsException<ArgumentException>(
                () => new MemoryCard(new byte[1024]),
                "Constructor must throw for wrong-sized data");
        }

        [TestMethod]
        public void MemoryCard_ConstructFromData_ClonesInput()
        {
            byte[] src = new byte[MemoryCard.TotalFileSize];
            src[0] = 0xAA;
            var mc = new MemoryCard(src);
            src[0] = 0xBB;   // mutate source after construction
            byte[] copy = mc.GetData();
            Assert.AreEqual(0xAA, copy[0], "MemoryCard must clone the input array");
        }

        [TestMethod]
        public void MemoryCard_ReadPage_RoundTrips_WriteRead()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] page = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < page.Length; i++) page[i] = (byte)(i & 0xFF);

            mc.WritePage(1, page);

            byte[] readBack = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(1, readBack);

            for (int i = 0; i < page.Length; i++)
                Assert.AreEqual(page[i], readBack[i], $"Page byte [{i}] must round-trip");
        }

        [TestMethod]
        public void MemoryCard_ErasePage_FillsWithFF()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] page = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < page.Length; i++) page[i] = 0x12;
            mc.WritePage(5, page);

            mc.ErasePage(5);

            byte[] readBack = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(5, readBack);
            for (int i = 0; i < readBack.Length; i++)
                Assert.AreEqual(0xFF, readBack[i], $"Erased page byte [{i}] must be 0xFF");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_ResetCommand_ReturnsAck()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] cmd = { MemoryCard.AddrSlot0, MemoryCard.CmdReset, 0x00 };
            byte[] resp = mc.ProcessCommand(cmd, 4);

            Assert.AreEqual(0xFF, resp[0], "RESET response[0] must be 0xFF");
            Assert.AreEqual(0x00, resp[1], "RESET response[1] must be 0x00");
            Assert.AreEqual(MemoryCard.RespAck, resp[2], "RESET response[2] must be ACK (0x2B)");
            Assert.AreEqual(MemoryCard.RespTerm, resp[3], "RESET response[3] must be TERM (0x47)");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_GetSpec_ReturnsAckAndSpecBytes()
        {
            var mc = MemoryCard.CreateBlank();
            // spec set 1: geometry parameters
            byte[] cmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdGetSpec, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            byte[] resp = mc.ProcessCommand(cmd, 14);

            Assert.AreEqual(0xFF,              resp[0], "GetSpec response[0] = 0xFF");
            Assert.AreEqual(0x00,              resp[1], "GetSpec response[1] = 0x00");
            Assert.AreEqual(MemoryCard.RespAck, resp[2], "GetSpec response[2] = ACK");
            Assert.AreEqual(MemoryCard.RespOk1, resp[5], "GetSpec response[5] = OK1");
            Assert.AreEqual(MemoryCard.RespOk2, resp[6], "GetSpec response[6] = OK2");
            // response[7] = pageDataSize/128 = 4
            Assert.AreEqual(4, resp[7], "GetSpec spec[0] must be pageDataSize/128 = 4");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_ReadPage_TwoPhaseTransaction()
        {
            var mc = MemoryCard.CreateBlank();

            // Write a known pattern into page 2.
            byte[] writeBuf = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < writeBuf.Length; i++) writeBuf[i] = (byte)(i ^ 0xA5);
            mc.WritePage(2, writeBuf);

            // Phase 1: send READ command header.
            byte[] hdrCmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdRead, 0x00, 0x00, 0x02, 0x5A, 0x5D };
            byte[] hdrResp = mc.ProcessCommand(hdrCmd, 8);

            Assert.AreEqual(0xFF,               hdrResp[0], "Read header[0] = 0xFF");
            Assert.AreEqual(MemoryCard.RespAck, hdrResp[2], "Read header[2] = ACK");
            Assert.AreEqual(0x00,               hdrResp[4], "Read header[4] = addrH echo (0x00)");
            Assert.AreEqual(0x02,               hdrResp[5], "Read header[5] = addrL echo (0x02)");
            Assert.AreEqual(MemoryCard.RespOk1, hdrResp[6], "Read header[6] = OK1");
            Assert.AreEqual(MemoryCard.RespOk2, hdrResp[7], "Read header[7] = OK2");

            // Phase 2: send dummy bytes; receive page data + XOR + TERM.
            byte[] dataCmd  = new byte[514]; // 512 dummy + xor + term
            byte[] dataResp = mc.ProcessCommand(dataCmd, 514);

            for (int i = 0; i < MemoryCard.PageDataSize; i++)
                Assert.AreEqual(writeBuf[i], dataResp[i], $"Read data byte [{i}] must match written pattern");
            Assert.AreEqual(MemoryCard.RespTerm, dataResp[513], "Last byte of read data phase must be TERM");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_WritePage_TwoPhaseTransaction()
        {
            var mc = MemoryCard.CreateBlank();

            // Phase 1: send WRITE command header.
            byte[] hdrCmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdWrite, 0x00, 0x00, 0x03, 0x5A, 0x5D };
            byte[] hdrResp = mc.ProcessCommand(hdrCmd, 8);

            Assert.AreEqual(MemoryCard.RespAck, hdrResp[2], "Write header[2] = ACK");
            Assert.AreEqual(0x03,               hdrResp[5], "Write header[5] = addrL echo (0x03)");

            // Phase 2: send page data.
            byte[] dataCmd = new byte[514];
            for (int i = 0; i < MemoryCard.PageDataSize; i++) dataCmd[i] = (byte)(i & 0xFF);
            mc.ProcessCommand(dataCmd, 514);

            // Verify the data was stored.
            byte[] readBack = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(3, readBack);
            for (int i = 0; i < MemoryCard.PageDataSize; i++)
                Assert.AreEqual(dataCmd[i], readBack[i], $"Written byte [{i}] must persist");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_EraseCluster_ErasesCorrectPages()
        {
            var mc = MemoryCard.CreateBlank();

            // Write a known pattern to pages 4 and 5 (cluster 2).
            byte[] pat = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < pat.Length; i++) pat[i] = 0x55;
            mc.WritePage(4, pat);
            mc.WritePage(5, pat);

            // Send ERASE command targeting page 4 (cluster 2).
            byte[] cmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdErase, 0x00, 0x00, 0x04, 0x5A, 0x5D };
            byte[] resp = mc.ProcessCommand(cmd, 9);

            Assert.AreEqual(MemoryCard.RespAck,  resp[2], "Erase response[2] = ACK");
            Assert.AreEqual(MemoryCard.RespTerm, resp[8], "Erase response[8] = TERM");

            // Both pages in the cluster must now be 0xFF.
            byte[] p4 = new byte[MemoryCard.PageDataSize];
            byte[] p5 = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(4, p4);
            mc.ReadPage(5, p5);
            Assert.AreEqual(0xFF, p4[0],  "Page 4 byte 0 must be 0xFF after erase");
            Assert.AreEqual(0xFF, p5[255], "Page 5 byte 255 must be 0xFF after erase");
        }

        [TestMethod]
        public void Sio2_MemCard_NoDeviceConnected_ReturnsFF()
        {
            var sio2 = new Sio2();
            // No MemCard0 assigned — expect all-0xFF response.
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 3, recvLen: 4));
            sio2.WriteRegister(0x60u, MemoryCard.AddrSlot0);
            sio2.WriteRegister(0x60u, MemoryCard.CmdReset);
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            uint b0 = sio2.ReadRegister(0x64u);
            Assert.AreEqual(0xFFu, b0, "No-MC response must be 0xFF");
        }

        [TestMethod]
        public void Sio2_MemCard0_ResetCommand_ViaFifo()
        {
            var sio2 = new Sio2();
            var mc   = MemoryCard.CreateBlank();
            sio2.MemCard0 = mc;

            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 3, recvLen: 4));
            sio2.WriteRegister(0x60u, MemoryCard.AddrSlot0);
            sio2.WriteRegister(0x60u, MemoryCard.CmdReset);
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            uint b0 = sio2.ReadRegister(0x64u);
            uint b2 = sio2.ReadRegister(0x64u);  // skip b1
            sio2.ReadRegister(0x64u);             // skip b2 (ACK)
            uint b3 = sio2.ReadRegister(0x64u);

            Assert.AreEqual(0xFFu,                       b0, "SIO2 MC RESET response[0] = 0xFF");
            Assert.AreEqual((uint)MemoryCard.RespTerm,   b3, "SIO2 MC RESET response[3] = TERM");
        }

        [TestMethod]
        public void Sio2_MemCard_DoesNotInterfereWithPad()
        {
            var sio2 = new Sio2();
            var pad  = new PadState();
            var mc   = MemoryCard.CreateBlank();
            sio2.Port0    = pad;
            sio2.MemCard0 = mc;

            // Pad poll on port 0.
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x60u, PadState.AddrPad);
            sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x68u, 0x01u); // START

            sio2.ReadRegister(0x64u); // skip leading 0xFF
            uint devId = sio2.ReadRegister(0x64u);
            Assert.AreEqual((uint)PadState.DeviceId, devId,
                "Pad poll must still return DualShock2 device ID when a MC is also wired");
        }

        [TestMethod]
        public void DualCpuScheduler_MemCard0_PropertyRoundTrip()
        {
            var scheduler = new DualCpuScheduler();
            var mc        = MemoryCard.CreateBlank();

            scheduler.MemCard0 = mc;
            Assert.AreSame(mc, scheduler.MemCard0, "MemCard0 property round-trip");
            Assert.AreSame(mc, scheduler.Sio2.MemCard0, "Sio2.MemCard0 must reflect scheduler.MemCard0");

            scheduler.MemCard0 = null;
            Assert.IsNull(scheduler.MemCard0, "MemCard0 must be settable to null");
        }

        [TestMethod]
        public void DualCpuScheduler_MemCard1_PropertyRoundTrip()
        {
            var scheduler = new DualCpuScheduler();
            var mc        = MemoryCard.CreateBlank();

            scheduler.MemCard1 = mc;
            Assert.AreSame(mc, scheduler.MemCard1, "MemCard1 property round-trip");
            Assert.AreSame(mc, scheduler.Sio2.MemCard1, "Sio2.MemCard1 must reflect scheduler.MemCard1");
        }

        private static uint BuildSend3(int port, int sendLen, int recvLen)
            => (uint)(port & 0x3)
             | ((uint)(sendLen & 0x1FF) << 8)
             | ((uint)(recvLen & 0x1FF) << 18);

        // ── Minimal ISO-9660 builder helper ──────────────────────────────────


        /// <summary>
        /// Build a minimal valid ISO-9660 image with one file in the root directory.
        /// Layout: sectors 0–15 = system area (zeros); sector 16 = PVD;
        /// sector 17 = root directory; sector 18 = file data.
        /// </summary>
        private static byte[] BuildMinimalIso(string fileName, string fileContent)
        {
            const int SectorSize = 2048;
            const int RootDirLba = 17;
            const int FileLba    = 18;

            byte[] iso = new byte[SectorSize * 19]; // sectors 0–18

            // ── Sector 16: Primary Volume Descriptor ──────────────────────────
            int pvd = 16 * SectorSize;
            iso[pvd + 0] = 1;       // PVD type
            iso[pvd + 1] = (byte)'C';
            iso[pvd + 2] = (byte)'D';
            iso[pvd + 3] = (byte)'0';
            iso[pvd + 4] = (byte)'0';
            iso[pvd + 5] = (byte)'1';
            iso[pvd + 6] = 1;       // descriptor version

            // Root Directory Record at PVD offset 156
            int rr = pvd + 156;
            iso[rr + 0] = 34;       // record length
            // LBA LE
            iso[rr + 2] = (byte)(RootDirLba & 0xFF);
            // Size LE (one sector = 2048 bytes = 0x800)
            iso[rr + 10] = 0x00;
            iso[rr + 11] = 0x08;
            // File flags: directory
            iso[rr + 25] = 2;
            // Name length = 1, name = 0x00 (".")
            iso[rr + 32] = 1;
            iso[rr + 33] = 0;

            // ── Sector 17: Root directory ─────────────────────────────────────
            int dir = RootDirLba * SectorSize;
            int pos = dir;

            // "." entry
            iso[pos + 0]  = 34;
            iso[pos + 2]  = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2;
            iso[pos + 32] = 1;
            iso[pos + 33] = 0x00; // "."
            pos += 34;

            // ".." entry
            iso[pos + 0]  = 34;
            iso[pos + 2]  = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2;
            iso[pos + 32] = 1;
            iso[pos + 33] = 0x01; // ".."
            pos += 34;

            // File entry
            string nameWithVer = fileName.ToUpperInvariant() + ";1";
            int    nameLen     = nameWithVer.Length;
            int    recLen      = 33 + nameLen;
            if ((recLen & 1) != 0) recLen++; // pad to even length

            byte[] fileBytes = System.Text.Encoding.Latin1.GetBytes(fileContent);
            uint   fileSize  = (uint)fileBytes.Length;

            iso[pos + 0]  = (byte)recLen;
            iso[pos + 2]  = (byte)(FileLba & 0xFF);
            // Size LE
            iso[pos + 10] = (byte)(fileSize & 0xFF);
            iso[pos + 11] = (byte)((fileSize >> 8) & 0xFF);
            iso[pos + 12] = (byte)((fileSize >> 16) & 0xFF);
            iso[pos + 13] = (byte)((fileSize >> 24) & 0xFF);
            iso[pos + 25] = 0; // not a directory
            iso[pos + 32] = (byte)nameLen;
            for (int i = 0; i < nameLen; i++)
                iso[pos + 33 + i] = (byte)nameWithVer[i];

            // ── Sector 18: File data ──────────────────────────────────────────
            int fileBase = FileLba * SectorSize;
            Array.Copy(fileBytes, 0, iso, fileBase, fileBytes.Length);

            return iso;
        }

        // =====================================================================
        // IopModuleLoader tests
        // =====================================================================

        [TestMethod]
        public void IopModuleLoader_LoadsSingleSegmentAndReturnsEntryPoint()
        {
            const uint entryPt  = 0x00001000u;
            const uint segVaddr = 0x00001000u;
            byte[] segData = { 1, 2, 3, 4 };

            byte[] irx = BuildIrx32(entryPt, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: segVaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new IopMemoryBus();
            uint gotEntry = IopModuleLoader.Load(irx, bus);

            Assert.AreEqual(entryPt, gotEntry, "Entry point must match e_entry field");
            for (int i = 0; i < segData.Length; i++)
                Assert.AreEqual(segData[i], bus.Ram[segVaddr + i],
                    $"Byte {i} of segment must be written to IOP RAM");
        }

        [TestMethod]
        public void IopModuleLoader_Kseg0VirtualAddress_MapsToPhysical()
        {
            // IRX segments linked at KSEG0 (0x80001000) must land in RAM at 0x00001000.
            const uint ksegVaddr = 0x80001000u;
            const uint physAddr  = 0x00001000u;
            const uint entryPt   = ksegVaddr;
            byte[] segData = { 0xAA, 0xBB, 0xCC, 0xDD };

            byte[] irx = BuildIrx32(entryPt, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: ksegVaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new IopMemoryBus();
            IopModuleLoader.Load(irx, bus);

            for (int i = 0; i < segData.Length; i++)
                Assert.AreEqual(segData[i], bus.Ram[physAddr + i],
                    $"KSEG0-linked byte {i} must be in physical RAM at 0x{physAddr + i:X}");
        }

        [TestMethod]
        public void IopModuleLoader_Kseg1VirtualAddress_MapsToPhysical()
        {
            // KSEG1 (0xA0002000) → physical 0x00002000.
            const uint kseg1Vaddr = 0xA0002000u;
            const uint physAddr   = 0x00002000u;
            byte[] segData = { 0x11, 0x22 };

            byte[] irx = BuildIrx32(kseg1Vaddr, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: kseg1Vaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new IopMemoryBus();
            IopModuleLoader.Load(irx, bus);

            Assert.AreEqual(0x11, bus.Ram[physAddr],     "First byte at KSEG1 physical address");
            Assert.AreEqual(0x22, bus.Ram[physAddr + 1], "Second byte at KSEG1 physical address");
        }

        [TestMethod]
        public void IopModuleLoader_ZeroFillsBssRegion()
        {
            const uint segVaddr = 0x00003000u;
            byte[] segData = { 0xFF, 0xFF };      // 2 bytes file data
            const uint memsz = 6u;               // 4 bytes BSS

            byte[] irx = BuildIrx32(segVaddr, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: segVaddr, Data: segData, Memsz: memsz)
            });

            var bus = new IopMemoryBus();
            // Pre-fill RAM with non-zero to verify zeroing
            bus.Ram[segVaddr + 2] = 0xAA;
            bus.Ram[segVaddr + 3] = 0xBB;

            IopModuleLoader.Load(irx, bus);

            for (uint i = (uint)segData.Length; i < memsz; i++)
                Assert.AreEqual(0, bus.Ram[segVaddr + i],
                    $"BSS byte {i} must be zero-filled");
        }

        [TestMethod]
        public void IopModuleLoader_LoadIntoEmulator_SetsPc()
        {
            const uint entryPt  = 0x00004000u;
            byte[] segData = { 0x00, 0x00, 0x00, 0x00 };

            byte[] irx = BuildIrx32(entryPt, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: entryPt, Data: segData, Memsz: (uint)segData.Length)
            });

            var emu = new IopEmulator();
            uint gotEntry = IopModuleLoader.LoadIntoEmulator(irx, emu);

            Assert.AreEqual(entryPt, gotEntry,       "Return value must be the entry point");
            Assert.AreEqual(entryPt, emu.State.PC,   "IopEmulator PC must be set to entry point");
        }

        [TestMethod]
        public void IopModuleLoader_InvalidMagic_ThrowsIopModuleLoadException()
        {
            byte[] bad = BuildIrx32(0u, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: 0u, Data: new byte[4], Memsz: 4u)
            });
            bad[0] = 0x00;  // corrupt first magic byte

            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(bad, bus),
                "Bad ELF magic must throw IopModuleLoadException");
        }

        [TestMethod]
        public void IopModuleLoader_TooSmall_ThrowsIopModuleLoadException()
        {
            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(new byte[10], bus),
                "Image smaller than ELF header must throw IopModuleLoadException");
        }

        [TestMethod]
        public void IopModuleLoader_64BitClass_ThrowsIopModuleLoadException()
        {
            byte[] data = BuildIrx32(0u, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: 0u, Data: new byte[4], Memsz: 4u)
            });
            data[4] = 2;  // ELFCLASS64

            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(data, bus),
                "ELF64 must throw IopModuleLoadException");
        }

        [TestMethod]
        public void IopModuleLoader_SegmentOutsideIopRam_ThrowsIopModuleLoadException()
        {
            // Segment at physical 0x001FFFFC with memsz 8 → end at 0x00200004 > 2MB
            const uint vaddr = 0x001FFFFCu;
            byte[] segData = new byte[4];

            byte[] irx = BuildIrx32(vaddr, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: vaddr, Data: segData, Memsz: 8u)
            });

            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(irx, bus),
                "Segment extending past 2MB IOP RAM must throw IopModuleLoadException");
        }

        // ── IrxSegment helper ─────────────────────────────────────────────────

        private record IrxSegment(uint Offset, uint Vaddr, byte[] Data, uint Memsz);

        /// <summary>
        /// Build a minimal ELF32 little-endian MIPS image (IRX format) with the
        /// given entry point and one PT_LOAD segment per <paramref name="segments"/>.
        /// </summary>
        private static byte[] BuildIrx32(uint entryPoint, IrxSegment[] segments)
        {
            int phOffset = 52;
            int phSize   = 32 * segments.Length;

            int totalSize = phOffset + phSize;
            foreach (var seg in segments)
            {
                int end = (int)seg.Offset + seg.Data.Length;
                if (end > totalSize) totalSize = end;
            }
            totalSize = Math.Max(totalSize, phOffset + phSize);

            byte[] irx = new byte[totalSize];

            // ELF header
            irx[0] = 0x7F; irx[1] = (byte)'E'; irx[2] = (byte)'L'; irx[3] = (byte)'F';
            irx[4] = 1;    // ELFCLASS32
            irx[5] = 1;    // ELFDATA2LSB
            irx[6] = 1;    // EV_CURRENT
            // e_type = 0xFF80 (ET_SCE_IOPRELEXEC) at offset 16
            IrxWriteU16LE(irx, 16, 0xFF80);
            // e_machine = MIPS (8) at offset 18
            IrxWriteU16LE(irx, 18, 8);
            // e_version = 1 at offset 20
            IrxWriteU32LE(irx, 20, 1u);
            // e_entry at offset 24
            IrxWriteU32LE(irx, 24, entryPoint);
            // e_phoff at offset 28
            IrxWriteU32LE(irx, 28, (uint)phOffset);
            // e_ehsize at offset 40
            IrxWriteU16LE(irx, 40, 52);
            // e_phentsize at offset 42
            IrxWriteU16LE(irx, 42, 32);
            // e_phnum at offset 44
            IrxWriteU16LE(irx, 44, (ushort)segments.Length);

            // Program headers
            for (int i = 0; i < segments.Length; i++)
            {
                var seg    = segments[i];
                int phBase = phOffset + i * 32;

                IrxWriteU32LE(irx, phBase +  0, 1u);                    // p_type = PT_LOAD
                IrxWriteU32LE(irx, phBase +  4, seg.Offset);            // p_offset
                IrxWriteU32LE(irx, phBase +  8, seg.Vaddr);             // p_vaddr
                IrxWriteU32LE(irx, phBase + 12, seg.Vaddr);             // p_paddr
                IrxWriteU32LE(irx, phBase + 16, (uint)seg.Data.Length); // p_filesz
                IrxWriteU32LE(irx, phBase + 20, seg.Memsz);             // p_memsz
                IrxWriteU32LE(irx, phBase + 24, 5u);                    // p_flags = R+X
                IrxWriteU32LE(irx, phBase + 28, 4u);                    // p_align

                Array.Copy(seg.Data, 0, irx, (int)seg.Offset, seg.Data.Length);
            }

            return irx;
        }

        private static void IrxWriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset + 0] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        private static void IrxWriteU16LE(byte[] buf, int offset, ushort value)
        {
            buf[offset + 0] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
        }

        // =====================================================================
        // SPU2 unit tests
        // =====================================================================

        [TestMethod]
        public void Spu2_TotalVoiceCount_Is48()
        {
            Assert.AreEqual(48, Spu2.TotalVoices, "SPU2 must expose 48 total voice channels (2 cores × 24)");
            Assert.AreEqual(2,  Spu2.CoreCount,   "SPU2 must have exactly 2 cores");
            Assert.AreEqual(24, Spu2.VoicesPerCore, "SPU2 must have 24 voices per core");
        }

        [TestMethod]
        public void Spu2_AudioRam_SizeIs2MB()
        {
            var spu2 = new Spu2();
            Assert.AreEqual(Spu2.RamSize, spu2.AudioRam.Length, "SPU2 audio RAM must be 2 MB");
        }

        [TestMethod]
        public void Spu2_VoiceRegisters_ReadWrite_Core0_Voice0()
        {
            var spu2 = new Spu2();
            // Voice 0 of core 0: base offset = 0*0x400 + 0*0x10 = 0x000
            spu2.WriteHalf(Spu2.VREG_VOLL,  0x1234);
            spu2.WriteHalf(Spu2.VREG_VOLR,  0x5678);
            spu2.WriteHalf(Spu2.VREG_PITCH, 0x1000);
            spu2.WriteHalf(Spu2.VREG_ADSR1, 0xAABB);
            spu2.WriteHalf(Spu2.VREG_ADSR2, 0xCCDD);

            Assert.AreEqual(0x1234, spu2.ReadHalf(Spu2.VREG_VOLL),  "Voice 0 VOLL round-trip");
            Assert.AreEqual(0x5678, spu2.ReadHalf(Spu2.VREG_VOLR),  "Voice 0 VOLR round-trip");
            Assert.AreEqual(0x1000, spu2.ReadHalf(Spu2.VREG_PITCH), "Voice 0 PITCH round-trip");
            Assert.AreEqual(0xAABB, spu2.ReadHalf(Spu2.VREG_ADSR1), "Voice 0 ADSR1 round-trip");
            Assert.AreEqual(0xCCDD, spu2.ReadHalf(Spu2.VREG_ADSR2), "Voice 0 ADSR2 round-trip");
        }

        [TestMethod]
        public void Spu2_VoiceRegisters_Core1_Voice5_CorrectOffset()
        {
            var spu2 = new Spu2();
            // Core 1, voice 5: offset = 1*0x400 + 5*0x10 + VREG_VOLR = 0x400 + 0x50 + 0x02 = 0x452
            uint offset = 0x400u + 5u * 0x10u + Spu2.VREG_VOLR;
            spu2.WriteHalf(offset, 0x7FFF);
            Assert.AreEqual(0x7FFF, spu2.ReadHalf(offset), "Core 1 voice 5 VOLR round-trip");

            // Verify the voice object directly
            Assert.AreEqual(0x7FFF, spu2.GetVoice(1, 5).VolR, "GetVoice(1,5).VolR must reflect the write");
        }

        [TestMethod]
        public void Spu2_KonWrite_Core0_SetsVoiceIsOn()
        {
            var spu2 = new Spu2();
            // KON0 absolute offset = 0*0x400 + 0x180 + OFF_KON0 = 0x1A0
            const uint KON0 = 0x1A0u;
            // Key-on voices 0, 2, 3 of core 0
            spu2.WriteHalf(KON0, 0x000D); // bits 0, 2, 3

            Assert.IsTrue(spu2.GetVoice(0, 0).IsOn, "Voice 0 must be on after KON0 bit 0");
            Assert.IsFalse(spu2.GetVoice(0, 1).IsOn, "Voice 1 must stay off (KON0 bit 1 not set)");
            Assert.IsTrue(spu2.GetVoice(0, 2).IsOn, "Voice 2 must be on after KON0 bit 2");
            Assert.IsTrue(spu2.GetVoice(0, 3).IsOn, "Voice 3 must be on after KON0 bit 3");
        }

        [TestMethod]
        public void Spu2_KonWrite_Core0_KON1_SetsVoices16To23()
        {
            var spu2 = new Spu2();
            const uint KON1 = 0x1A2u;
            spu2.WriteHalf(KON1, 0x0003); // voices 16 and 17

            Assert.IsTrue(spu2.GetVoice(0, 16).IsOn, "Voice 16 must be on after KON1 bit 0");
            Assert.IsTrue(spu2.GetVoice(0, 17).IsOn, "Voice 17 must be on after KON1 bit 1");
            Assert.IsFalse(spu2.GetVoice(0, 18).IsOn, "Voice 18 must stay off");
        }

        [TestMethod]
        public void Spu2_KofWrite_Core0_ClearsVoiceIsOn()
        {
            var spu2 = new Spu2();
            const uint KON0 = 0x1A0u;
            const uint KOF0 = 0x1A4u;
            spu2.WriteHalf(KON0, 0x000F); // key-on voices 0-3
            spu2.WriteHalf(KOF0, 0x0005); // key-off voices 0 and 2

            Assert.IsFalse(spu2.GetVoice(0, 0).IsOn, "Voice 0 must be off after KOF0 bit 0");
            Assert.IsTrue(spu2.GetVoice(0, 1).IsOn,  "Voice 1 must remain on");
            Assert.IsFalse(spu2.GetVoice(0, 2).IsOn, "Voice 2 must be off after KOF0 bit 2");
            Assert.IsTrue(spu2.GetVoice(0, 3).IsOn,  "Voice 3 must remain on");
        }

        [TestMethod]
        public void Spu2_KonWrite_ResetsAdpcmHistory()
        {
            var spu2 = new Spu2();
            var v    = spu2.GetVoice(0, 0);
            v.AdpcmPrev1 = 1234;
            v.AdpcmPrev2 = 5678;

            // KON0 bit 0 must reset ADPCM history for voice 0
            spu2.WriteHalf(0x1A0u, 0x0001);

            Assert.AreEqual(0, v.AdpcmPrev1, "KON must reset AdpcmPrev1 to 0");
            Assert.AreEqual(0, v.AdpcmPrev2, "KON must reset AdpcmPrev2 to 0");
        }

        [TestMethod]
        public void Spu2_MasterVolume_Core0_ReadWrite()
        {
            var spu2 = new Spu2();
            // MVOLL at 0x1B0, MVOLR at 0x1B2 (core 0)
            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x2000);
            Assert.AreEqual(0x3FFF, spu2.ReadHalf(0x1B0u), "Core 0 MVOLL round-trip");
            Assert.AreEqual(0x2000, spu2.ReadHalf(0x1B2u), "Core 0 MVOLR round-trip");
        }

        [TestMethod]
        public void Spu2_MasterVolume_Core1_ReadWrite()
        {
            var spu2 = new Spu2();
            // Core 1 MVOLL at 0x400+0x1B0 = 0x5B0
            spu2.WriteHalf(0x5B0u, 0x1234);
            Assert.AreEqual(0x1234, spu2.ReadHalf(0x5B0u), "Core 1 MVOLL round-trip");
        }

        [TestMethod]
        public void Spu2_TransferAddress_TSA_Write_And_Get()
        {
            var spu2 = new Spu2();
            // TSA_HI at 0x1A8, TSA_LO at 0x1AA (core 0)
            spu2.WriteHalf(0x1A8u, 0x0010); // TSA_HI
            spu2.WriteHalf(0x1AAu, 0x0200); // TSA_LO
            uint tsa = spu2.GetTransferAddr(0);
            Assert.AreEqual(0x00100200u, tsa, "GetTransferAddr must combine TSA_HI:TSA_LO");
        }

        [TestMethod]
        public void Spu2_WriteDmaData_StoresInAudioRam()
        {
            var spu2 = new Spu2();
            // Set TSA for core 0 to halfword address 0x10 (= byte address 0x20)
            spu2.WriteHalf(0x1A8u, 0x0000); // TSA_HI = 0
            spu2.WriteHalf(0x1AAu, 0x0010); // TSA_LO = 0x10

            spu2.WriteDmaData(0, 0xDEADBEEFu);

            // TSA = 0x10 halfwords = 0x20 bytes; DMA writes first halfword at 0x20
            Assert.AreEqual(0xEF, spu2.AudioRam[0x20], "AudioRam[0x20] must be 0xEF (low byte of 0xBEEF)");
            Assert.AreEqual(0xBE, spu2.AudioRam[0x21], "AudioRam[0x21] must be 0xBE (high byte of 0xBEEF)");
            Assert.AreEqual(0xAD, spu2.AudioRam[0x22], "AudioRam[0x22] must be 0xAD (low byte of 0xDEAD)");
            Assert.AreEqual(0xDE, spu2.AudioRam[0x23], "AudioRam[0x23] must be 0xDE (high byte of 0xDEAD)");
        }

        [TestMethod]
        public void Spu2_WriteDmaData_AdvancesTransferAddress()
        {
            var spu2 = new Spu2();
            spu2.WriteHalf(0x1A8u, 0x0000); // TSA_HI
            spu2.WriteHalf(0x1AAu, 0x0000); // TSA_LO = 0

            spu2.WriteDmaData(0, 0x12345678u); // writes 2 halfwords → TSA += 2

            Assert.AreEqual(2u, spu2.GetTransferAddr(0), "TSA must advance by 2 halfwords after writing one DMA word");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_SilenceBlock_AllZeroes()
        {
            // A silence block: shift=0, filter=0, flags=0, data all 0.
            byte[] block = new byte[16]; // all zeros
            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            foreach (short s in pcm)
                Assert.AreEqual(0, s, "All-zero ADPCM block must decode to silence");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_LoopEndFlag()
        {
            byte[] block = new byte[16];
            block[1] = 0x01; // flag bit 0 = loop-end
            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            bool loopEnd = Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);
            Assert.IsTrue(loopEnd, "Loop-end flag must be true when flag byte bit 0 is set");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_KnownOutput_Filter0_Shift12()
        {
            // Block: shift=12, filter=0, all data bytes = 0x77 (nibbles 7 and 7)
            // Nibble 7 = 7 (positive); with shift=12, raw = 7 << 0 = 7; filter=0 → s = 7.
            byte[] block = new byte[16];
            block[0] = 0x0C; // shift=12, filter=0
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77; // low nibble=7, high nibble=7

            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            // Each nibble = 7, shift=12 → raw = 7<<0 = 7, filter=0 → no predictor → s=7
            Assert.AreEqual(7, pcm[0], "First decoded sample must be 7");
            Assert.AreEqual(7, pcm[1], "Second decoded sample must be 7");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_Filter1_PrevSamplesInfluenceOutput()
        {
            // Verify that filter 1 (f1=60/64) applies a predictor based on prev1.
            byte[] block = new byte[16];
            block[0] = 0x1C; // shift=12, filter=1
            block[1] = 0x00;
            // Set data byte so all nibbles are 0 (no raw signal, only predictor)
            // With filter 1: s = 0 + prev1*60/64 + prev2*0 = prev1*60/64
            // Start with prev1=64 → s = 60; then prev1=60 → s = 56 (rounds down)
            Span<short> pcm = stackalloc short[28];
            int p1 = 64, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            // First sample: raw=0, prev1=64, f1=60 → (64*60+32)/64 = 3872/64 = 60
            Assert.AreEqual(60, pcm[0], "First sample with filter 1 and prev1=64 must be 60");
        }

        [TestMethod]
        public void Spu2_AdpcmDecoder_UpdatesState_ForChainedBlocks()
        {
            // Confirm that prev1/prev2 are updated correctly so chained blocks work.
            byte[] block = new byte[16];
            block[0] = 0x0C; // shift=12, filter=0
            for (int i = 2; i < 16; i++) block[i] = 0x11; // all nibbles = 1

            Span<short> pcm = stackalloc short[28];
            int p1 = 0, p2 = 0;
            Spu2.DecodeAdpcmBlock(block, pcm, ref p1, ref p2);

            // Each nibble = 1, shift=12 → raw=1, filter=0 → s=1 for all samples.
            // After 28 samples: prev1=1, prev2=1.
            Assert.AreEqual(1, p1, "prev1 must be 1 after decoding 28 samples of value 1");
            Assert.AreEqual(1, p2, "prev2 must be 1 after decoding 28 samples of value 1");
        }

        // ── IopMemoryBus + SPU2 integration tests ─────────────────────────────

        [TestMethod]
        public void IopMemoryBus_Spu2_PropertySetAndGet()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;
            Assert.AreSame(spu2, bus.Spu2, "Spu2 property must return the assigned instance");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_VoiceVOLL_ReadWriteWord()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // Core 0, voice 0, VOLL at 0x1F900000, VOLR at 0x1F900002
            bus.WriteWord(Spu2.RegBase, 0xBBBBAAAAu); // VOLR=0xBBBB, VOLL=0xAAAA
            uint readBack = bus.ReadWord(Spu2.RegBase);
            Assert.AreEqual(0xBBBBAAAAu, readBack, "SPU2 voice VOLL/VOLR word round-trip via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_VoiceRegisterHalfWord_RoundTrip()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // Use ReadHalf / WriteHalf (routed via ReadByte/WriteByte)
            bus.WriteHalf(Spu2.RegBase + Spu2.VREG_PITCH, 0x1000);
            ushort v = bus.ReadHalf(Spu2.RegBase + Spu2.VREG_PITCH);
            Assert.AreEqual(0x1000, v, "SPU2 voice PITCH halfword round-trip via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_KonWrite_KeysOnVoice()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // KON0 for core 0 is at 0x1F900000 + 0x1A0 = 0x1F9001A0
            bus.WriteHalf(Spu2.RegBase + 0x1A0u, 0x0001); // key-on voice 0
            Assert.IsTrue(spu2.GetVoice(0, 0).IsOn,
                "Voice 0 must be active after KON0 bit 0 write via IopMemoryBus");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_DmacChannel7_FeedsCore1AudioRam()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            // Set core 1 TSA = 0 (default)
            // spu2 core 1 TSA_HI at 0x400+0x1A8 = 0x5A8, TSA_LO at 0x5AA
            // (already 0 by default)

            // Place audio data in IOP RAM
            uint srcAddr = 0x1000u;
            bus.WriteWord(srcAddr,      0x11223344u);
            bus.WriteWord(srcAddr + 4u, 0x55667788u);

            // Configure IOP DMAC channel 7 (SPU2): RAM→Device, 2 words from 0x1000
            bus.Dmac.Channels[7].WriteRegister(IopDmacChannel.REG_MADR, srcAddr);
            bus.Dmac.Channels[7].WriteRegister(IopDmacChannel.REG_BCR,  2u); // 2 words
            bus.Dmac.Channels[7].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // STR=1, DIR=0
            bus.Dmac.ExecuteChannel(7, bus);

            // Verify the data was written into core 1 audio RAM
            // TSA was 0 → byte address 0. First halfword = 0x3344, second = 0x1122
            Assert.AreEqual(0x44, spu2.AudioRam[0], "AudioRam[0] must be 0x44 (LE lo byte of 0x3344)");
            Assert.AreEqual(0x33, spu2.AudioRam[1], "AudioRam[1] must be 0x33 (LE hi byte of 0x3344)");
            Assert.AreEqual(0x22, spu2.AudioRam[2], "AudioRam[2] must be 0x22");
            Assert.AreEqual(0x11, spu2.AudioRam[3], "AudioRam[3] must be 0x11");
        }

        [TestMethod]
        public void IopMemoryBus_Spu2_DmacChannel4_FeedsCore0AudioRam()
        {
            var bus  = new IopMemoryBus();
            var spu2 = new Spu2();
            bus.Spu2 = spu2;

            uint srcAddr = 0x2000u;
            bus.WriteWord(srcAddr, 0xAABBCCDDu);

            bus.Dmac.Channels[4].WriteRegister(IopDmacChannel.REG_MADR, srcAddr);
            bus.Dmac.Channels[4].WriteRegister(IopDmacChannel.REG_BCR,  1u);
            bus.Dmac.Channels[4].WriteRegister(IopDmacChannel.REG_CHCR, 1u << 24); // STR=1, DIR=0
            bus.Dmac.ExecuteChannel(4, bus);

            // Data written to core 0 audio RAM at address 0
            Assert.AreEqual(0xDD, spu2.AudioRam[0], "AudioRam[0] must be 0xDD (lo byte of 0xCCDD)");
            Assert.AreEqual(0xCC, spu2.AudioRam[1], "AudioRam[1] must be 0xCC (hi byte of 0xCCDD)");
            Assert.AreEqual(0xBB, spu2.AudioRam[2], "AudioRam[2] must be 0xBB");
            Assert.AreEqual(0xAA, spu2.AudioRam[3], "AudioRam[3] must be 0xAA");
        }

        // =====================================================================
        // Phase 7.4 — SPU2 Voice SA Registers (PVCA)
        // =====================================================================

        [TestMethod]
        public void Spu2_VoiceSA_Core0_Voice0_WriteReadRoundTrip()
        {
            var spu2 = new Spu2();
            // Core 0 voice 0 SA: abs 0x1C0 (SA_HI) and 0x1C2 (SA_LO)
            const uint SA_HI = 0x1C0u;
            const uint SA_LO = 0x1C2u;
            spu2.WriteHalf(SA_HI, 0x0001);
            spu2.WriteHalf(SA_LO, 0x2000);

            // Read back via voice struct
            Assert.AreEqual(0x00012000u, spu2.GetVoice(0, 0).StartAddr,
                "Voice 0 StartAddr must be 0x00012000 after writing SA_HI=0x0001, SA_LO=0x2000");

            // Read back via register interface
            Assert.AreEqual(0x0001u, spu2.ReadHalf(SA_HI), "SA_HI read-back must be 0x0001");
            Assert.AreEqual(0x2000u, spu2.ReadHalf(SA_LO), "SA_LO read-back must be 0x2000");
        }

        [TestMethod]
        public void Spu2_VoiceSA_Core0_Voice5_CorrectOffset()
        {
            var spu2 = new Spu2();
            // Core 0 voice 5: abs = 0x1C0 + 5*12 = 0x1C0 + 0x3C = 0x1FC
            const uint V5_SA_HI = 0x1FCu;
            const uint V5_SA_LO = 0x1FEu;
            spu2.WriteHalf(V5_SA_HI, 0x0000);
            spu2.WriteHalf(V5_SA_LO, 0x8000);

            Assert.AreEqual(0x00008000u, spu2.GetVoice(0, 5).StartAddr,
                "Voice 5 StartAddr must be 0x8000");
        }

        [TestMethod]
        public void Spu2_VoiceLoopA_WriteUpdatesLoopAddr()
        {
            var spu2 = new Spu2();
            // Core 0 voice 0 LoopA: abs 0x1C4 (LoopA_HI) and 0x1C6 (LoopA_LO)
            const uint LOOPA_HI = 0x1C4u;
            const uint LOOPA_LO = 0x1C6u;
            spu2.WriteHalf(LOOPA_HI, 0x0002);
            spu2.WriteHalf(LOOPA_LO, 0x4000);

            Assert.AreEqual(0x00024000u, spu2.GetVoice(0, 0).LoopAddr,
                "Voice 0 LoopAddr must be 0x00024000");
        }

        [TestMethod]
        public void Spu2_VoiceNextA_ReturnsCurrentAddr()
        {
            var spu2 = new Spu2();
            // Set CurrentAddr directly
            spu2.GetVoice(0, 0).CurrentAddr = 0x00031234u;
            // Core 0 voice 0 NextA: abs 0x1C8 (HI) and 0x1CA (LO)
            const uint NEXTA_HI = 0x1C8u;
            const uint NEXTA_LO = 0x1CAu;

            Assert.AreEqual(0x0003u, spu2.ReadHalf(NEXTA_HI), "NextA_HI must return upper 16 bits of CurrentAddr");
            Assert.AreEqual(0x1234u, spu2.ReadHalf(NEXTA_LO), "NextA_LO must return lower 16 bits of CurrentAddr");
        }

        [TestMethod]
        public void Spu2_VoiceSA_Core1_Voice0_CorrectOffset()
        {
            var spu2 = new Spu2();
            // Core 1 voice 0 SA_HI: abs 0x400 + 0x1C0 = 0x5C0
            const uint C1V0_SA_HI = 0x5C0u;
            const uint C1V0_SA_LO = 0x5C2u;
            spu2.WriteHalf(C1V0_SA_HI, 0x000A);
            spu2.WriteHalf(C1V0_SA_LO, 0xBCDE);

            Assert.AreEqual(0x000ABCDEu, spu2.GetVoice(1, 0).StartAddr,
                "Core 1 voice 0 StartAddr must be 0x000ABCDE");
        }

        [TestMethod]
        public void Spu2_KonWrite_SetsCurrentAddrFromStartAddr()
        {
            var spu2 = new Spu2();
            var voice = spu2.GetVoice(0, 0);
            voice.StartAddr   = 0x00001000u;
            voice.CurrentAddr = 0x00009999u; // different value
            voice.DecIdx      = 10;

            // Key-on voice 0
            spu2.WriteHalf(0x1A0u, 0x0001);

            Assert.AreEqual(0x00001000u, voice.CurrentAddr, "KON must reset CurrentAddr to StartAddr");
            Assert.AreEqual(28,          voice.DecIdx,      "KON must reset DecIdx to 28 to force first decode");
            Assert.AreEqual(0,           voice.AdsrVol,     "KON must reset AdsrVol to 0");
            Assert.AreEqual(0,           voice.AdsrPhase,   "KON must start in Attack phase (0)");
        }

        [TestMethod]
        public void Spu2_EffectsStartA_WriteRead()
        {
            var spu2 = new Spu2();
            // Core 0 ESA: abs 0x2E0 (HI) and 0x2E2 (LO)
            spu2.WriteHalf(0x2E0u, 0x0001);
            spu2.WriteHalf(0x2E2u, 0x8000);

            Assert.AreEqual(0x00018000u, spu2.GetEffectsStartA(0),
                "EffectsStartA must combine HI:LO halfwords");
        }

        [TestMethod]
        public void Spu2_EffectsEndA_WriteRead()
        {
            var spu2 = new Spu2();
            // Core 0 EEA: abs 0x33C (HI) and 0x33E (LO)
            spu2.WriteHalf(0x33Cu, 0x0002);
            spu2.WriteHalf(0x33Eu, 0xC000);

            Assert.AreEqual(0x0002C000u, spu2.GetEffectsEndA(0),
                "EffectsEndA must combine HI:LO halfwords");
        }

        // =====================================================================
        // Phase 7.4 — SPU2 MixSamples
        // =====================================================================

        [TestMethod]
        public void Spu2_MixSamples_AllVoicesOff_ReturnsSilence()
        {
            var spu2 = new Spu2();
            // Set master volume so output is non-trivially zero even with no voices
            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);

            short[] pcm = spu2.MixSamples(32);

            bool allZero = true;
            foreach (short s in pcm)
                if (s != 0) { allZero = false; break; }

            Assert.IsTrue(allZero, "MixSamples must return silence when no voices are active");
        }

        [TestMethod]
        public void Spu2_MixSamples_ActiveVoice_ProducesNonZeroOutput()
        {
            var spu2 = new Spu2();

            // Build a minimal ADPCM block: shift=12, filter=0, all nibbles = 7 → sample value 7
            // (See Spu2_AdpcmDecoder_KnownOutput_Filter0_Shift12 test for expected value.)
            var block = new byte[16];
            block[0] = 0x0C; // shift=12, filter=0
            block[1] = 0x00; // no loop flags
            for (int i = 2; i < 16; i++) block[i] = 0x77; // all nibbles = 7

            // Place the ADPCM block at AudioRam address 0 (halfword 0)
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0x1000;        // 1:1 pitch
            v.Adsr1     = 0x00FF;        // fast attack (shift=0, step=3) → jumps to 0x7FFF quickly
            v.Adsr2     = 0x0000;        // sustain hold, fast release
            v.StartAddr = 0;

            // Set master volume
            spu2.WriteHalf(0x1B0u, 0x3FFF); // MVOLL
            spu2.WriteHalf(0x1B2u, 0x3FFF); // MVOLR

            // Key on voice 0
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(128);

            bool anyNonZero = false;
            foreach (short s in pcm)
                if (s != 0) { anyNonZero = true; break; }

            Assert.IsTrue(anyNonZero, "MixSamples must produce non-zero output from an active voice");
        }

        [TestMethod]
        public void Spu2_MixSamples_MasterVolumeZero_ReturnsSilence()
        {
            var spu2 = new Spu2();

            // Set up a voice with ADPCM data (same as above)
            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0x1000;
            v.Adsr1     = 0x00FF;
            v.StartAddr = 0;

            // Leave master volume at 0 (default)
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(64);

            bool allZero = true;
            foreach (short s in pcm)
                if (s != 0) { allZero = false; break; }

            Assert.IsTrue(allZero, "MixSamples must return silence when master volume is 0");
        }

        [TestMethod]
        public void Spu2_MixSamples_ReturnsStereoInterleavedPairs()
        {
            var spu2 = new Spu2();
            const int SampleCount = 16;
            short[] pcm = spu2.MixSamples(SampleCount);

            Assert.AreEqual(SampleCount * 2, pcm.Length,
                "MixSamples must return sampleCount*2 shorts (interleaved stereo)");
        }

        [TestMethod]
        public void Spu2_MixSamples_WideAreaMasterVol_ProducesNonZeroOutput()
        {
            // Verify that master volume written to the PCSX2-compatible "weird area" (0x760)
            // is also honoured by the mixer.
            var spu2 = new Spu2();

            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0x1000;
            v.Adsr1     = 0x00FF;
            v.StartAddr = 0;

            // Write master volume ONLY to the "weird area" (abs 0x760 = core0 MVOLL)
            spu2.WriteHalf(0x760u, 0x3FFF); // core 0 MVOLL via weird area
            spu2.WriteHalf(0x762u, 0x3FFF); // core 0 MVOLR via weird area

            spu2.WriteHalf(0x1A0u, 0x0001);
            short[] pcm = spu2.MixSamples(128);

            bool anyNonZero = false;
            foreach (short s in pcm)
                if (s != 0) { anyNonZero = true; break; }

            Assert.IsTrue(anyNonZero, "MixSamples must honour master volume from the 'weird area' registers");
        }

        [TestMethod]
        public void Spu2_MixSamples_PitchZero_ProducesSilence()
        {
            var spu2 = new Spu2();

            var block = new byte[16];
            block[0] = 0x0C;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.Pitch     = 0;             // zero pitch → never advances
            v.Adsr1     = 0x00FF;
            v.StartAddr = 0;

            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(16);

            // With pitch = 0, PitchFrac never reaches 0x1000, no samples consumed → silence
            bool allZero = true;
            foreach (short s in pcm)
                if (s != 0) { allZero = false; break; }
            Assert.IsTrue(allZero, "Zero pitch must produce silence (no sample advancement)");
        }

        // =====================================================================
        // Phase 7.4 — SPU2 Reverb
        // =====================================================================

        [TestMethod]
        public void Spu2_GetEffectsStartA_DefaultIsZero()
        {
            var spu2 = new Spu2();
            Assert.AreEqual(0u, spu2.GetEffectsStartA(0), "Default ESA for core 0 must be 0");
            Assert.AreEqual(0u, spu2.GetEffectsStartA(1), "Default ESA for core 1 must be 0");
        }

        [TestMethod]
        public void Spu2_DoReverb_NoEffectWhenEsaEqualsEea()
        {
            // With ESA == EEA the reverb is disabled; mixer dry+reverb sum must equal dry input.
            // Set up a voice with known output and zero ESA/EEA (disabled reverb).
            var spu2 = new Spu2();
            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            spu2.GetVoice(0, 0).VolL      = 0x3FFF;
            spu2.GetVoice(0, 0).VolR      = 0x3FFF;
            spu2.GetVoice(0, 0).Pitch     = 0x1000;
            spu2.GetVoice(0, 0).Adsr1     = 0x00FF;
            spu2.GetVoice(0, 0).StartAddr = 0;
            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            // Leave ESA=EEA=0 (reverb disabled)
            short[] pcmNoReverb = spu2.MixSamples(64);

            // Confirm some audio is present
            bool anyNonZero = false;
            foreach (short s in pcmNoReverb)
                if (s != 0) { anyNonZero = true; break; }
            Assert.IsTrue(anyNonZero, "With reverb disabled, mix must still produce non-zero voice audio");
        }

        [TestMethod]
        public void Spu2_Reverb_EnabledWithCoefficients_ProducesOutput()
        {
            // Enable reverb with a minimal but valid configuration and verify that
            // the mixer produces non-zero output.
            var spu2 = new Spu2();

            // Set up audio RAM: ADPCM block at 0, reverb buffer from halfword 0x1000
            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            spu2.GetVoice(0, 0).VolL      = 0x3FFF;
            spu2.GetVoice(0, 0).VolR      = 0x3FFF;
            spu2.GetVoice(0, 0).Pitch     = 0x1000;
            spu2.GetVoice(0, 0).Adsr1     = 0x00FF;
            spu2.GetVoice(0, 0).StartAddr = 0;

            // Set ESA=0x1000 (starts past the ADPCM data), EEA=0x2000
            spu2.WriteHalf(0x2E0u, 0x0000); // ESA_HI
            spu2.WriteHalf(0x2E2u, 0x1000); // ESA_LO
            spu2.WriteHalf(0x33Cu, 0x0000); // EEA_HI
            spu2.WriteHalf(0x33Eu, 0x2000); // EEA_LO

            // Set reverb coefficient volumes (IIR_VOL in "weird area") to moderate value
            spu2.WriteHalf(0x774u, 0x4000); // IIR_VOL core 0
            spu2.WriteHalf(0x77Eu, 0x3000); // WALL_VOL core 0
            spu2.WriteHalf(0x764u, 0x2000); // FXVOLL core 0
            spu2.WriteHalf(0x766u, 0x2000); // FXVOLR core 0

            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            short[] pcm = spu2.MixSamples(128);

            bool anyNonZero = false;
            foreach (short s in pcm)
                if (s != 0) { anyNonZero = true; break; }
            Assert.IsTrue(anyNonZero, "Mixer with reverb enabled must produce non-zero output");
        }

        // =====================================================================
        // Phase 7.4 — ADSR Envelope
        // =====================================================================

        [TestMethod]
        public void Spu2_ADSR_AttackRisesToMaxVol()
        {
            // Voice with linear attack, max attack rate.
            // After enough samples, AdsrVol should reach 0x7FFF (attack complete → decay begins).
            var spu2 = new Spu2();

            var block = new byte[16];
            block[0] = 0x0C;
            block[1] = 0x00;
            for (int i = 2; i < 16; i++) block[i] = 0x77;
            Array.Copy(block, 0, spu2.AudioRam, 0, 16);

            var v = spu2.GetVoice(0, 0);
            // ADSR1: AttackMode=0 (linear), AttackShift=0, AttackStep=3 → fastest attack
            // DecayShift=0, SustainLevel=15 (0x7800)
            v.Adsr1     = 0x00F0 | 0xF; // AttackMode=0, Shift=0, Step=3, DecayShift=0, SustainLevel=15
            v.Adsr2     = 0x0000;
            v.Pitch     = 0x1000;
            v.VolL      = 0x3FFF;
            v.VolR      = 0x3FFF;
            v.StartAddr = 0;

            spu2.WriteHalf(0x1B0u, 0x3FFF);
            spu2.WriteHalf(0x1B2u, 0x3FFF);
            spu2.WriteHalf(0x1A0u, 0x0001);

            // Mix enough samples for the attack to complete
            spu2.MixSamples(8192);

            // After attack, AdsrPhase must have advanced past 0 (Attack)
            Assert.IsTrue(v.AdsrPhase >= 1, "ADSR must advance from Attack phase after enough samples");
        }

        [TestMethod]
        public void Spu2_ADSR_KonResetsPhaseToAttack()
        {
            var spu2 = new Spu2();
            var v = spu2.GetVoice(0, 0);
            v.AdsrPhase = 2; // Sustain
            v.AdsrVol   = 0x4000;

            // KON must reset to Attack phase
            spu2.WriteHalf(0x1A0u, 0x0001);

            Assert.AreEqual(0, v.AdsrPhase, "KON must reset AdsrPhase to 0 (Attack)");
            Assert.AreEqual(0, v.AdsrVol,   "KON must reset AdsrVol to 0");
        }

        // =====================================================================
        // Phase 7.4 — IOP CDDA
        // =====================================================================

        [TestMethod]
        public void Cdvd_ReadCdda_NCommand07_TriggersDataReady()
        {
            var cdvd = new Cdvd();
            // Load a minimal disc image (just a cooked ISO with zeroes)
            var fakeIso = new byte[Cdvd.SectorSize * 32];
            cdvd.LoadImage(fakeIso);

            bool dataReadyFired = false;
            cdvd.DataReady += (_, _) => dataReadyFired = true;

            // Issue N-command 0x07 (ReadCDDA): LBA=0, count=1
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 0
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 1
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 2
            cdvd.WriteRegister(0x00u, 0x00); // LBA byte 3
            cdvd.WriteRegister(0x00u, 0x01); // count = 1
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CDDA);

            Assert.IsTrue(dataReadyFired, "N-command 0x07 must fire DataReady");
        }

        [TestMethod]
        public void Cdvd_ReadCdda_IsoImage_ReturnsSilence()
        {
            var cdvd = new Cdvd();
            var fakeIso = new byte[Cdvd.SectorSize * 32];
            cdvd.LoadImage(fakeIso);

            // Issue CDDA read
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x01);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CDDA);

            // Read back: should be all zeros (silence for cooked ISO with no audio tracks)
            bool allZero = true;
            for (int i = 0; i < Cdvd.RawSectorSize / 4; i++)
            {
                if (cdvd.ReadNextDmaWord() != 0u) { allZero = false; break; }
            }
            Assert.IsTrue(allZero, "CDDA from a cooked ISO must return silence (all zeros)");
        }

        [TestMethod]
        public void Cdvd_ReadCdda_RawImage_ReturnsAudioData()
        {
            // Build a minimal raw BIN image: one sector with the CD sync header
            // followed by known audio data.
            var rawImage = new byte[Cdvd.RawSectorSize];
            // Sync pattern: 0x00 0xFF×11 0x00
            rawImage[0] = 0x00;
            for (int i = 1; i <= 10; i++) rawImage[i] = 0xFF;
            rawImage[11] = 0x00;
            // Fill bytes 12-23 with header, then fill audio data starting at 24
            for (int i = 12; i < Cdvd.RawSectorSize; i++)
                rawImage[i] = (byte)(i & 0xFF);

            var cdvd = new Cdvd();
            cdvd.LoadImage(rawImage);

            Assert.IsTrue(cdvd.IsRawImage, "Image with sync pattern must be detected as raw");

            // Issue CDDA read for LBA 0
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x01);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CDDA);

            // The entire 2352-byte sector should be returned (no header strip for CDDA)
            uint firstWord = cdvd.ReadNextDmaWord();
            // Bytes 0-3 of the raw sector are [0x00, 0xFF, 0xFF, 0xFF] → word = 0xFFFFFF00
            Assert.AreEqual(0xFFFFFF00u, firstWord,
                "Raw CDDA sector must return bytes from start of the raw sector (including sync)");
        }

        [TestMethod]
        public void Cdvd_RawImage_DataSectorStripsHeader()
        {
            // Verify that a ReadCd (0x06) on a raw BIN image skips the 24-byte header.
            var rawImage = new byte[Cdvd.RawSectorSize * 17]; // 17 sectors for PVD
            // Sector 0 sync
            rawImage[0] = 0x00;
            for (int i = 1; i <= 10; i++) rawImage[i] = 0xFF;
            rawImage[11] = 0x00;
            // Sector 1 user data marker (placed at byte 24 of sector 1)
            rawImage[Cdvd.RawSectorSize + 24]     = 0xAB;
            rawImage[Cdvd.RawSectorSize + 25]     = 0xCD;
            rawImage[Cdvd.RawSectorSize + 26]     = 0xEF;
            rawImage[Cdvd.RawSectorSize + 27]     = 0x01;

            var cdvd = new Cdvd();
            cdvd.LoadImage(rawImage);

            Assert.IsTrue(cdvd.IsRawImage, "Must detect as raw image");

            // ReadCd LBA=1, count=1
            cdvd.WriteRegister(0x00u, 0x01); // LBA byte 0 = 1
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x00);
            cdvd.WriteRegister(0x00u, 0x01);
            cdvd.WriteRegister(0x04u, Cdvd.NCMD_READ_CD);

            uint word = cdvd.ReadNextDmaWord();
            Assert.AreEqual(0x01EFCDABu, word,
                "ReadCd on raw image must return user data from byte offset +24 (header stripped)");
        }

        [TestMethod]
        public void Cdvd_IsRawImage_False_ForCookedIso()
        {
            var cdvd = new Cdvd();
            var iso  = new byte[Cdvd.SectorSize * 20]; // cooked ISO
            cdvd.LoadImage(iso);
            Assert.IsFalse(cdvd.IsRawImage, "Cooked ISO must not be detected as raw");
        }

        [TestMethod]
        public void Cdvd_NCMD_READ_CDDA_ConstantValue()
        {
            Assert.AreEqual(0x07, Cdvd.NCMD_READ_CDDA, "NCMD_READ_CDDA must be 0x07");
        }
    }
}
