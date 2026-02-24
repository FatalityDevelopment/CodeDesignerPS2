using System;
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
    }
}
