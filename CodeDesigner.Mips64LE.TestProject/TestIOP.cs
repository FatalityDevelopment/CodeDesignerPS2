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
    }
}
