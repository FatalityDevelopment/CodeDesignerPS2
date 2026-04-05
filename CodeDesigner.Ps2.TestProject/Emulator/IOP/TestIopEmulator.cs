using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestIopEmulator
    {
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


        private static uint MakeAddiu(int rs, int rt, int imm)
            => (0x09u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        private static uint MakeOri(int rs, int rt, int imm)
            => (0x0Du << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        private static uint MakeLui(int rt, int imm)
            => (0x0Fu << 26) | ((uint)rt << 16) | (uint)(imm & 0xFFFF);

        private static uint MakeBEQ(int rs, int rt, int offset)
            => (0x04u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (uint)(offset & 0xFFFF);

        private static uint MakeOr(int rs, int rt, int rd)
            => ((uint)rs << 21) | ((uint)rt << 16) | ((uint)rd << 11) | 0x25u;
    }
}
