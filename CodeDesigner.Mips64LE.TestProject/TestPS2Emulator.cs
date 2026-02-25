using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.CPU.COP0;
using CodeDesigner.Mips64LE.Common.Emulator;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE.TestProject
{
    /// <summary>
    /// Tests for the PS2 emulator components: PS2MemoryBus and PS2Emulator.
    /// </summary>
    [TestClass]
    public class TestPS2Emulator
    {
        // =====================================================================
        // PS2MemoryBus tests
        // =====================================================================

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteByte()
        {
            var bus = new PS2MemoryBus();
            bus.WriteByte(0x00001000, 0xAB);
            Assert.AreEqual(0xAB, bus.ReadByte(0x00001000));
        }

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteWord()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00002000, 0xDEADBEEF);
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x00002000));
        }

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteDoubleWord()
        {
            var bus = new PS2MemoryBus();
            bus.WriteDoubleWord(0x00003000, 0x0123456789ABCDEFuL);
            Assert.AreEqual(0x0123456789ABCDEFuL, bus.ReadDoubleWord(0x00003000));
        }

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteQuadWord()
        {
            var bus = new PS2MemoryBus();
            bus.WriteQuadWord(0x00004000, 0xAABBCCDDEEFF0011uL, 0x1122334455667788uL);
            bus.ReadQuadWord(0x00004000, out ulong lo, out ulong hi);
            Assert.AreEqual(0xAABBCCDDEEFF0011uL, lo);
            Assert.AreEqual(0x1122334455667788uL, hi);
        }

        [TestMethod]
        public void MemoryBus_Kseg0_TranslatesCorrectly()
        {
            // KSEG0: 0x80000000 maps to physical 0x00000000
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00000100, 0x12345678);
            // Read back via KSEG0 virtual address
            Assert.AreEqual(0x12345678u, bus.ReadWord(0x80000100));
        }

        [TestMethod]
        public void MemoryBus_Kseg1_TranslatesCorrectly()
        {
            // KSEG1: 0xA0000000 maps to physical 0x00000000
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00000200, 0xCAFEBABE);
            Assert.AreEqual(0xCAFEBABEu, bus.ReadWord(0xA0000200));
        }

        [TestMethod]
        public void MemoryBus_Scratchpad_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x70000000, 0x55AA55AA);
            Assert.AreEqual(0x55AA55AAu, bus.ReadWord(0x70000000));
        }

        [TestMethod]
        public void MemoryBus_Bios_IsReadOnly()
        {
            var bus = new PS2MemoryBus();
            // Seed BIOS via the exposed array
            bus.Bios[0] = 0x11;
            bus.Bios[1] = 0x22;
            bus.Bios[2] = 0x33;
            bus.Bios[3] = 0x44;

            // A write through the bus should be silently ignored (read-only)
            bus.WriteWord(0x1FC00000, 0xDEADBEEF);
            Assert.AreEqual(0x44332211u, bus.ReadWord(0x1FC00000),
                "BIOS should be read-only; write must be ignored");
        }

        [TestMethod]
        public void MemoryBus_Unmapped_ThrowsException()
        {
            var bus = new PS2MemoryBus();
            Assert.ThrowsException<InvalidOperationException>(() => bus.ReadByte(0x50000000));
        }

        // =====================================================================
        // PS2Emulator tests
        // =====================================================================

        [TestMethod]
        public void Emulator_ResetState()
        {
            var emu = new PS2Emulator();
            // Power-on PC should be the BIOS entry point
            Assert.AreEqual(0xBFC00000UL, emu.Cpu.PC);
            Assert.AreEqual(0UL, emu.InstructionCount);
        }

        [TestMethod]
        public void Emulator_MemoryWiredToRam()
        {
            var emu = new PS2Emulator();
            // CpuState.Memory must reference bus RAM so load/store instructions work
            Assert.AreSame(emu.Bus.Ram, emu.Cpu.Memory);
        }

        [TestMethod]
        public void Emulator_Step_NopAdvancesPC()
        {
            var emu = new PS2Emulator();
            // Load a NOP (0x00000000 = SLL $zero,$zero,0) at the reset vector
            emu.Bus.Bios[0] = 0x00; // NOP at 0xBFC00000 (offset 0 from BIOS base)
            emu.Bus.Bios[1] = 0x00;
            emu.Bus.Bios[2] = 0x00;
            emu.Bus.Bios[3] = 0x00;

            emu.Step();

            Assert.AreEqual(0xBFC00004UL, emu.Cpu.PC, "PC should advance by 4 after NOP");
            Assert.AreEqual(1UL, emu.InstructionCount);
        }

        [TestMethod]
        public void Emulator_Step_ADDIU()
        {
            // addiu $t0, $zero, 42   (opcode=0x09, rs=0, rt=8, imm=42)
            uint addiu = (0x09u << 26) | (0u << 21) | (8u << 16) | 42;
            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { addiu });
            emu.Cpu.PC = 0x00000000;

            emu.Step();

            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8], "$t0 should be 42");
            Assert.AreEqual(4UL, emu.Cpu.PC);
        }

        [TestMethod]
        public void Emulator_Step_MultipleArithmetic()
        {
            // Program: addi $t0,$zero,10  / addi $t1,$zero,20 / add $t2,$t0,$t1
            // addi $t0,$zero,10 : opcode=0x08, rs=0, rt=8, imm=10
            uint addiT0 = (0x08u << 26) | (0u << 21) | (8u << 16) | 10;
            // addi $t1,$zero,20 : opcode=0x08, rs=0, rt=9, imm=20
            uint addiT1 = (0x08u << 26) | (0u << 21) | (9u << 16) | 20;
            // add $t2,$t0,$t1 : opcode=0x00, rs=8, rt=9, rd=10, funct=0x20
            uint addT2 = (0u << 26) | (8u << 21) | (9u << 16) | (10u << 11) | 0x20;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { addiT0, addiT1, addT2 });
            emu.Cpu.PC = 0x00000000;

            emu.Run(3);

            Assert.AreEqual(10UL, emu.Cpu.RegsLo[8], "$t0 = 10");
            Assert.AreEqual(20UL, emu.Cpu.RegsLo[9], "$t1 = 20");
            Assert.AreEqual(30UL, emu.Cpu.RegsLo[10], "$t2 = $t0 + $t1 = 30");
            Assert.AreEqual(12UL, emu.Cpu.PC, "PC should be at instruction 4 (offset 12)");
        }

        [TestMethod]
        public void Emulator_Step_BranchNotTaken()
        {
            // BEQ $t0,$t1,+2  (taken only if $t0==$t1, which they aren't)
            // $t0=5, $t1=3 => not taken => PC += 4
            // beq rs=8, rt=9, offset=2 (imm field = 2 → branch to PC+4+8)
            uint beq = (0x04u << 26) | (8u << 21) | (9u << 16) | 2;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { beq });
            emu.Cpu.PC    = 0x00000000;
            emu.Cpu.RegsLo[8] = 5;
            emu.Cpu.RegsLo[9] = 3;

            emu.Step();

            // Not taken: BEQ implementation sets cpu.PC = X+4
            Assert.AreEqual(4UL, emu.Cpu.PC, "BEQ not taken: PC should be 4");
        }

        [TestMethod]
        public void Emulator_Step_BranchTaken()
        {
            // BEQ $t0,$t1,+2  (taken because $t0==$t1=5)
            // Target = 0 + 4 + 2*4 = 12
            uint beq = (0x04u << 26) | (8u << 21) | (9u << 16) | 2;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { beq });
            emu.Cpu.PC    = 0x00000000;
            emu.Cpu.RegsLo[8] = 5;
            emu.Cpu.RegsLo[9] = 5;

            emu.Step();

            // Taken: target = 0 + 4 + 8 = 12 (0xC)
            Assert.AreEqual(12UL, emu.Cpu.PC, "BEQ taken: PC should be 12");
        }

        [TestMethod]
        public void Emulator_Step_JR()
        {
            // jr $ra  (funct=0x08, rs=31)
            uint jr = (0u << 26) | (31u << 21) | 0x08;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { jr });
            emu.Cpu.PC = 0x00000000;
            emu.Cpu.RegsLo[31] = 0x00000020; // $ra = 0x20

            emu.Step();

            Assert.AreEqual(0x20UL, emu.Cpu.PC, "JR: PC should be value of $ra");
        }

        [TestMethod]
        public void Emulator_UnknownInstruction_RaisesEvent()
        {
            uint unknownWord = 0;
            var emu = new PS2Emulator();

            // Write a clearly invalid opcode at a RAM address.
            // Opcode 0x1D is reserved and not used by any PS2 instruction,
            // guaranteeing GetInstructionObject returns (null, null).
            uint badWord = 0x74000000u; // opcode = 0x1D (= 29)
            emu.LoadProgram(0x00000000, new[] { badWord });
            emu.Cpu.PC = 0x00000000;

            bool eventFired = false;
            emu.UnknownInstruction += (_, w) => { unknownWord = w; eventFired = true; };

            emu.Step();

            Assert.IsTrue(eventFired, "UnknownInstruction event should fire");
            Assert.AreEqual(badWord, unknownWord);
            Assert.AreEqual(4UL, emu.Cpu.PC, "PC should still advance after unknown instruction");
        }

        [TestMethod]
        public void Emulator_LoadProgram_StoresWordsCorrectly()
        {
            var emu = new PS2Emulator();
            uint[] prog = { 0x11223344, 0x55667788 };
            emu.LoadProgram(0x00000100, prog);

            Assert.AreEqual(0x11223344u, emu.Bus.ReadWord(0x00000100));
            Assert.AreEqual(0x55667788u, emu.Bus.ReadWord(0x00000104));
        }

        [TestMethod]
        public void Emulator_LoadStoreWordRoundTrip()
        {
            // sw $t0, 0($zero)  then  lw $t1, 0($zero)
            // sw: opcode=0x2B, rs=0(base), rt=8(src), imm=0
            uint sw = (0x2Bu << 26) | (0u << 21) | (8u << 16) | 0;
            // lw: opcode=0x23, rs=0(base), rt=9(dst), imm=0
            uint lw = (0x23u << 26) | (0u << 21) | (9u << 16) | 0;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000100, new[] { sw, lw });
            emu.Cpu.PC        = 0x00000100;
            emu.Cpu.RegsLo[8] = 0x12345678;  // bit-31 clear avoids sign-extension difference on LW read-back

            emu.Run(2);

            Assert.AreEqual(0x12345678UL, emu.Cpu.RegsLo[9],
                "LW should read back the value written by SW");
        }

        // =====================================================================
        // Branch delay slot tests
        // =====================================================================

        [TestMethod]
        public void Emulator_BranchDelaySlot_TakenExecutesDelaySlot()
        {
            // Program:
            //   0x00: beq $t0,$t0,+1  (taken, target = 0 + 4 + 4 = 8)
            //   0x04: addiu $t1,$zero,99  (delay slot — must execute)
            //   0x08: addiu $t2,$zero,55  (should NOT execute before the delay slot)
            //
            // beq rs=8, rt=8, offset=1  →  taken (t0==t0)
            uint beq     = (0x04u << 26) | (8u << 21) | (8u << 16) | 1u;
            // addiu $t1,$zero,99
            uint addiu99 = (0x09u << 26) | (0u << 21) | (9u << 16) | 99u;
            // addiu $t2,$zero,55
            uint addiu55 = (0x09u << 26) | (0u << 21) | (10u << 16) | 55u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { beq, addiu99, addiu55 });
            emu.Cpu.PC = 0x00000000;

            // Single Step() should execute beq AND its delay slot (addiu99).
            emu.Step();

            Assert.AreEqual(8UL, emu.Cpu.PC,
                "After taken BEQ, PC should be at the branch target (0x8)");
            Assert.AreEqual(99UL, emu.Cpu.RegsLo[9],
                "Delay-slot instruction (addiu $t1,99) must have executed");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[10],
                "Instruction after delay slot must NOT have executed yet");
        }

        [TestMethod]
        public void Emulator_BranchDelaySlot_NotTakenDelaySlotRunsNextStep()
        {
            // Program:
            //   0x00: beq $t0,$t1,+5  (not taken: $t0=1, $t1=2)
            //   0x04: addiu $t2,$zero,77  (delay slot — runs as next sequential instr)
            //   0x08: nop
            //
            // beq rs=8, rt=9, offset=5 (not taken)
            uint beq     = (0x04u << 26) | (8u << 21) | (9u << 16) | 5u;
            uint addiu77 = (0x09u << 26) | (0u << 21) | (10u << 16) | 77u;
            uint nop     = 0x00000000u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { beq, addiu77, nop });
            emu.Cpu.PC        = 0x00000000;
            emu.Cpu.RegsLo[8] = 1; // $t0
            emu.Cpu.RegsLo[9] = 2; // $t1

            // Step 1: execute BEQ (not taken) — PC should move to 0x4 (the delay slot).
            emu.Step();
            Assert.AreEqual(4UL, emu.Cpu.PC,
                "Not-taken BEQ: PC should advance to delay-slot address (0x4)");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[10],
                "Delay slot not yet executed after not-taken branch");

            // Step 2: execute the delay slot instruction.
            emu.Step();
            Assert.AreEqual(8UL, emu.Cpu.PC,
                "After delay slot, PC should be at 0x8");
            Assert.AreEqual(77UL, emu.Cpu.RegsLo[10],
                "Delay slot instruction (addiu $t2,77) must have executed");
        }

        [TestMethod]
        public void Emulator_BranchDelaySlot_JumpExecutesDelaySlot()
        {
            // Program:
            //   0x100: j 0x110    (jump to 0x110, delay slot at 0x104)
            //   0x104: addiu $t0,$zero,42  (delay slot — must execute)
            //   0x108: addiu $t1,$zero,11  (must NOT run before target)
            //   0x110: nop
            //
            // J: opcode=2, instr_index = 0x110 >> 2 = 0x44
            uint j       = (0x02u << 26) | (0x110u >> 2);
            uint addiu42 = (0x09u << 26) | (0u << 21) | (8u << 16) | 42u;
            uint addiu11 = (0x09u << 26) | (0u << 21) | (9u << 16) | 11u;
            uint nop     = 0x00000000u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000100, new[] { j, addiu42, addiu11 });
            emu.LoadProgram(0x00000110, new[] { nop });
            emu.Cpu.PC = 0x00000100;

            emu.Step();

            Assert.AreEqual(0x110UL, emu.Cpu.PC,
                "J: PC should be at jump target (0x110)");
            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8],
                "J delay-slot instruction must have executed");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[9],
                "Instruction at 0x108 (past delay slot) must not have run");
        }

        [TestMethod]
        public void Emulator_BranchDelaySlot_BranchLikelyNotTaken_NullifiesDelaySlot()
        {
            // Program:
            //   0x00: beql $t0,$t1,+5  (not taken: $t0=1, $t1=2) → nullifies delay slot
            //   0x04: addiu $t2,$zero,99  (delay slot — must NOT execute)
            //   0x08: nop
            //
            // beql rs=8, rt=9, offset=5, opcode=0x14
            uint beql    = (0x14u << 26) | (8u << 21) | (9u << 16) | 5u;
            uint addiu99 = (0x09u << 26) | (0u << 21) | (10u << 16) | 99u;
            uint nop     = 0x00000000u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000000, new[] { beql, addiu99, nop });
            emu.Cpu.PC        = 0x00000000;
            emu.Cpu.RegsLo[8] = 1; // $t0
            emu.Cpu.RegsLo[9] = 2; // $t1

            emu.Step();

            Assert.AreEqual(8UL, emu.Cpu.PC,
                "BEQL not-taken: PC should skip delay slot to 0x8");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[10],
                "BEQL not-taken: delay slot must have been nullified (not executed)");
        }

        // =====================================================================
        // COP0 exception dispatch tests
        // =====================================================================

        [TestMethod]
        public void Emulator_COP0_SyscallSetsEPC()
        {
            // syscall encodes as opcode=0, funct=0x0C
            uint syscall = 0x0000000Cu; // syscall 0

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000200, new[] { syscall });
            emu.Cpu.PC = 0x00000200;

            // Clear BEV so vector is 0x80000080
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status &= ~(1u << 22); // Clear BEV
            emu.Cpu.COP0.WriteRegister32(12, status);

            emu.Step(); // SYSCALL is consumed by COP0 dispatch — no exception thrown

            // EPC should be the address of the SYSCALL instruction
            Assert.AreEqual(0x200UL, emu.Cpu.COP0.ReadRegister64(14),
                "EPC must be set to the address of the SYSCALL instruction");

            // Status.EXL should be set
            uint newStatus = emu.Cpu.COP0.ReadRegister32(12);
            Assert.IsTrue((newStatus & (1u << 1)) != 0,
                "Status.EXL must be set after SYSCALL");

            // Cause.ExcCode should be 8 (Syscall)
            uint cause = emu.Cpu.COP0.ReadRegister32(13);
            uint excCode = (cause >> 2) & 0x1Fu;
            Assert.AreEqual(8u, excCode, "Cause.ExcCode must be 8 (Syscall)");

            // PC should be at the general exception vector (BEV=0 → 0x80000080)
            Assert.AreEqual(0x80000080UL, emu.Cpu.PC,
                "PC must be redirected to the general exception vector");
        }

        [TestMethod]
        public void Emulator_COP0_SyscallBev1UsesRomVector()
        {
            // syscall with BEV=1 (BIOS ROM exception vector)
            uint syscall = 0x0000000Cu;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000300, new[] { syscall });
            emu.Cpu.PC = 0x00000300;

            // Ensure BEV=1 (default after reset)
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status |= (1u << 22); // Set BEV
            emu.Cpu.COP0.WriteRegister32(12, status);

            emu.Step();

            Assert.AreEqual(0xBFC00180UL, emu.Cpu.PC,
                "With BEV=1 the exception vector must be 0xBFC00180");
        }

        [TestMethod]
        public void Emulator_COP0_OverflowDispatch()
        {
            // ADD $t0, $t0, $t1 with values that overflow a 32-bit signed integer.
            // add $t0,$t0,$t1 : opcode=0, rs=8, rt=9, rd=8, funct=0x20
            uint add = (0u << 26) | (8u << 21) | (9u << 16) | (8u << 11) | 0x20u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000400, new[] { add });
            emu.Cpu.PC        = 0x00000400;
            emu.Cpu.RegsLo[8] = 0x7FFFFFFFu; // INT32_MAX
            emu.Cpu.RegsLo[9] = 1u;           // +1 → overflow

            // Clear BEV
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status &= ~(1u << 22);
            emu.Cpu.COP0.WriteRegister32(12, status);

            emu.Step(); // Should not throw — overflow routed through COP0

            // Cause.ExcCode should be 12 (Overflow)
            uint cause   = emu.Cpu.COP0.ReadRegister32(13);
            uint excCode = (cause >> 2) & 0x1Fu;
            Assert.AreEqual(12u, excCode, "Cause.ExcCode must be 12 (Overflow)");

            Assert.AreEqual(0x80000080UL, emu.Cpu.PC,
                "PC must be at the exception vector after overflow");
        }

        [TestMethod]
        public void Emulator_COP0_SyscallEventFired()
        {
            uint syscall = 0x0000000Cu;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000500, new[] { syscall });
            emu.Cpu.PC = 0x00000500;

            // Clear BEV
            uint s = emu.Cpu.COP0.ReadRegister32(12);
            s &= ~(1u << 22);
            emu.Cpu.COP0.WriteRegister32(12, s);

            uint firedAt = 0;
            bool eventRaised = false;
            emu.SyscallRaised += (_, addr) => { firedAt = addr; eventRaised = true; };

            emu.Step();

            Assert.IsTrue(eventRaised, "SyscallRaised event must fire on SYSCALL");
            Assert.AreEqual(0x500u, firedAt,
                "SyscallRaised must report the address of the SYSCALL instruction");
        }

        [TestMethod]
        public void COP0Dispatcher_Dispatch_WritesEpcAndExcCode()
        {
            // Unit-test the dispatcher directly (no emulator loop needed).
            var cpu = new CodeDesigner.Mips64LE.Common.CPU.CpuState();

            // BEV=0 so vector is 0x80000080
            cpu.COP0.WriteRegister32(12, 0x00000000u); // Status: BEV=0, EXL=0

            COP0ExceptionDispatcher.Dispatch(cpu, ExceptionCode.Syscall, 0x1000UL, inDelaySlot: false);

            Assert.AreEqual(0x1000UL, cpu.COP0.ReadRegister64(14), "EPC must equal the faulting PC");
            Assert.AreEqual(0x80000080UL, cpu.PC, "PC must be redirected to exception vector");

            uint cause   = cpu.COP0.ReadRegister32(13);
            uint excCode = (cause >> 2) & 0x1Fu;
            Assert.AreEqual(8u, excCode, "Cause.ExcCode must be 8 (Syscall)");

            uint status = cpu.COP0.ReadRegister32(12);
            Assert.IsTrue((status & 2u) != 0, "Status.EXL must be set");
        }

        [TestMethod]
        public void COP0Dispatcher_Dispatch_DelaySlot_SetsBDAndAdjustsEpc()
        {
            var cpu = new CodeDesigner.Mips64LE.Common.CPU.CpuState();
            cpu.COP0.WriteRegister32(12, 0x00000000u); // BEV=0, EXL=0

            // Faulting instruction is in the delay slot of an instruction at 0x2000.
            // epc passed in is the delay-slot address (0x2004), inDelaySlot=true.
            COP0ExceptionDispatcher.Dispatch(cpu, ExceptionCode.Overflow, 0x2004UL, inDelaySlot: true);

            // EPC must point to the branch at 0x2000.
            Assert.AreEqual(0x2000UL, cpu.COP0.ReadRegister64(14),
                "EPC must point to the branch instruction (epc - 4) when in delay slot");

            // Cause.BD must be set.
            uint cause = cpu.COP0.ReadRegister32(13);
            Assert.IsTrue((cause & (1u << 31)) != 0, "Cause.BD must be set for delay-slot exception");
        }

        // =====================================================================
        // EE hardware interrupt delivery tests
        // =====================================================================

        /// <summary>
        /// When the INTC has a pending interrupt, Status.IE=1, EXL=0, ERL=0,
        /// EIE=1, and IM2=1, Step() must dispatch an interrupt exception
        /// (ExcCode=0) and set PC to the exception vector instead of executing
        /// the instruction at PC.
        /// </summary>
        [TestMethod]
        public void Emulator_InterruptDelivery_TakesExceptionWhenEnabled()
        {
            var emu = new PS2Emulator();

            // Load a NOP at address 0 so we can detect whether the instruction ran.
            emu.LoadProgram(0x00000000, new uint[] { 0x00000000 }); // NOP
            emu.Cpu.PC = 0x00000000;

            // Configure Status: IE=1, EXL=0, ERL=0, EIE=1 (bit16), IM2=1 (bit10), BEV=0
            // 0x00010401 = IE(0) | IM2(10) | EIE(16) = 1 | 0x400 | 0x10000
            emu.Cpu.COP0.WriteRegister32(12, 0x00010401u);

            // Raise an INTC interrupt (timer 0 = bit 9 of INTC_STAT and INTC_MASK)
            emu.Bus.Intc.RaiseInterrupt(INTC.INT_TIMER0);
            // Enable that bit in INTC_MASK via the register interface (write-1-to-toggle)
            emu.Bus.Intc.WriteRegister(INTC.REG_MASK, 1u << INTC.INT_TIMER0);

            emu.Step();

            // Should have taken the interrupt exception vector (BEV=0 → 0x80000080)
            Assert.AreEqual(0x80000080UL, emu.Cpu.PC,
                "Interrupt: PC must redirect to exception vector 0x80000080");

            // Cause.ExcCode must be 0 (Interrupt)
            uint c = emu.Cpu.COP0.ReadRegister32(13);
            uint excCode = (c >> 2) & 0x1Fu;
            Assert.AreEqual(0u, excCode, "Cause.ExcCode must be 0 for a hardware interrupt");

            // Status.EXL must be set
            uint s = emu.Cpu.COP0.ReadRegister32(12);
            Assert.IsTrue((s & 2u) != 0, "Status.EXL must be set after interrupt exception");

            // Cause.IP2 (bit 10) must be set to indicate the INTC source
            Assert.IsTrue((c & 0x400u) != 0, "Cause.IP2 must be set to reflect INTC interrupt");
        }

        /// <summary>
        /// When Status.IE=0 (global interrupts disabled) the interrupt must NOT be
        /// delivered even though the INTC reports a pending interrupt.
        /// </summary>
        [TestMethod]
        public void Emulator_InterruptDelivery_NotDeliveredWhenIEClear()
        {
            var emu = new PS2Emulator();
            uint nop = 0x00000000u;
            emu.LoadProgram(0x00000100, new[] { nop });
            emu.Cpu.PC = 0x00000100;

            // Status: IE=0, EXL=0, ERL=0, EIE=1, IM2=1, BEV=0
            emu.Cpu.COP0.WriteRegister32(12, 0x00010400u); // IE clear

            emu.Bus.Intc.RaiseInterrupt(INTC.INT_TIMER0);
            emu.Bus.Intc.WriteRegister(INTC.REG_MASK, 1u << INTC.INT_TIMER0);

            emu.Step(); // NOP must execute normally

            // PC should have advanced past NOP, not jumped to exception vector
            Assert.AreEqual(0x00000104UL, emu.Cpu.PC,
                "With IE=0, interrupt must not be delivered; NOP must execute normally");
        }

        /// <summary>
        /// When Status.EXL=1 (already in exception mode) the interrupt must NOT be
        /// delivered.
        /// </summary>
        [TestMethod]
        public void Emulator_InterruptDelivery_NotDeliveredWhenEXLSet()
        {
            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000200, new uint[] { 0x00000000 }); // NOP
            emu.Cpu.PC = 0x00000200;

            // Status: IE=1, EXL=1 (already in exception), EIE=1, IM2=1, BEV=0
            emu.Cpu.COP0.WriteRegister32(12, 0x00010403u); // IE=1 | EXL=1 | IM2 | EIE

            emu.Bus.Intc.RaiseInterrupt(INTC.INT_TIMER0);
            emu.Bus.Intc.WriteRegister(INTC.REG_MASK, 1u << INTC.INT_TIMER0);

            emu.Step();

            Assert.AreEqual(0x00000204UL, emu.Cpu.PC,
                "With EXL=1, interrupt must not be delivered; NOP must execute normally");
        }

        /// <summary>
        /// When EIE (bit 16, PS2-specific interrupt gate) is clear the interrupt
        /// must NOT be delivered even though IE=1.
        /// </summary>
        [TestMethod]
        public void Emulator_InterruptDelivery_NotDeliveredWhenEIEClear()
        {
            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000300, new uint[] { 0x00000000 }); // NOP
            emu.Cpu.PC = 0x00000300;

            // Status: IE=1, EXL=0, ERL=0, EIE=0 (bit16 clear), IM2=1, BEV=0
            emu.Cpu.COP0.WriteRegister32(12, 0x00000401u); // IE | IM2 (no EIE)

            emu.Bus.Intc.RaiseInterrupt(INTC.INT_TIMER0);
            emu.Bus.Intc.WriteRegister(INTC.REG_MASK, 1u << INTC.INT_TIMER0);

            emu.Step();

            Assert.AreEqual(0x00000304UL, emu.Cpu.PC,
                "With EIE=0, interrupt must not be delivered; NOP must execute normally");
        }

        // =====================================================================
        // DualCpuScheduler tests
        // =====================================================================

        [TestMethod]
        public void DualCpuScheduler_Constructor_CreatesBothCpus()
        {
            var sched = new DualCpuScheduler();
            Assert.IsNotNull(sched.EE,  "EE emulator must not be null");
            Assert.IsNotNull(sched.IOP, "IOP emulator must not be null");
        }

        [TestMethod]
        public void DualCpuScheduler_Constructor_AcceptsExternalEE()
        {
            var ee    = new PS2Emulator();
            var sched = new DualCpuScheduler(ee);
            Assert.AreSame(ee, sched.EE, "Scheduler must use the supplied EE instance");
        }

        [TestMethod]
        public void DualCpuScheduler_EeAndIop_ShareSif()
        {
            var sched = new DualCpuScheduler();
            // Write MSCOM from the EE side and read it from the IOP bus
            sched.EE.Bus.Sif.WriteRegister(SIF.REG_MSCOM, 0xCAFEBABEu);
            Assert.AreEqual(0xCAFEBABEu,
                sched.IOP.Bus.SharedSif!.ReadRegister(SIF.REG_MSCOM),
                "EE and IOP must share the same SIF instance");
        }

        [TestMethod]
        public void DualCpuScheduler_Reset_ClearsCounters()
        {
            var sched = new DualCpuScheduler();

            // Load NOPs so RunSlice doesn't fault
            uint nop = 0x00000000u;
            for (int i = 0; i < 8; i++)
                sched.EE.LoadProgram((uint)(i * 4), new[] { nop });
            sched.EE.Cpu.PC = 0;

            // IOP needs some code too — LoadProgram writes to IOP RAM
            for (int i = 0; i < 4; i++)
                sched.IOP.LoadProgram((uint)(i * 4), new[] { nop });
            sched.IOP.State.PC = 0;

            sched.RunSlice(4);

            Assert.IsTrue(sched.TotalEeSteps > 0);
            Assert.IsTrue(sched.TotalIopSteps > 0);

            sched.Reset();

            Assert.AreEqual(0UL, sched.TotalEeSteps,  "TotalEeSteps must be 0 after Reset");
            Assert.AreEqual(0UL, sched.TotalIopSteps, "TotalIopSteps must be 0 after Reset");
        }

        [TestMethod]
        public void DualCpuScheduler_RunSlice_RunsProportionalSteps()
        {
            var sched = new DualCpuScheduler();

            // Load NOPs into EE main RAM
            uint nop = 0x00000000u;
            for (int i = 0; i < 64; i++)
                sched.EE.LoadProgram((uint)(i * 4), new[] { nop });
            sched.EE.Cpu.PC = 0;

            // Load NOPs into IOP RAM
            for (int i = 0; i < 16; i++)
                sched.IOP.LoadProgram((uint)(i * 4), new[] { nop });
            sched.IOP.State.PC = 0;

            // Run 8 EE steps → should produce 2 IOP steps (8 / 4 = 2)
            sched.RunSlice(8);

            Assert.AreEqual(8UL,  sched.TotalEeSteps,  "Must have run 8 EE steps");
            Assert.AreEqual(2UL,  sched.TotalIopSteps, "Must have run 2 IOP steps (8/4)");
        }

        [TestMethod]
        public void DualCpuScheduler_RunSlice_AccumulatesIopFraction()
        {
            // Run 2 EE steps at a time (< EeToIopRatio=4).
            // The IOP should not run until 4 EE steps have accumulated.
            var sched = new DualCpuScheduler();

            uint nop = 0x00000000u;
            for (int i = 0; i < 64; i++)
                sched.EE.LoadProgram((uint)(i * 4), new[] { nop });
            sched.EE.Cpu.PC = 0;

            for (int i = 0; i < 16; i++)
                sched.IOP.LoadProgram((uint)(i * 4), new[] { nop });
            sched.IOP.State.PC = 0;

            sched.RunSlice(2); // 2 EE steps, 0 IOP steps (accumulator=2)
            Assert.AreEqual(2UL, sched.TotalEeSteps);
            Assert.AreEqual(0UL, sched.TotalIopSteps);

            sched.RunSlice(2); // 2 more EE steps, 1 IOP step (accumulator reaches 4)
            Assert.AreEqual(4UL, sched.TotalEeSteps);
            Assert.AreEqual(1UL, sched.TotalIopSteps);
        }

        [TestMethod]
        public void DualCpuScheduler_EeToIopRatio_IsFour()
        {
            Assert.AreEqual(4, DualCpuScheduler.EeToIopRatio,
                "EE:IOP ratio must be 4 (147.456 MHz / 36.864 MHz)");
        }

        // =====================================================================
        // SIF DMA transfer (IOP↔EE handshake) tests
        // =====================================================================

        [TestMethod]
        public void SifDma_EeCh6_Ram_To_Sif1Fifo()
        {
            // EE DMAC CH6 (SIF1, RAM→FIFO) should push EE RAM contents into SIF1 FIFO.
            var emu = new PS2Emulator();

            // Write one quadword (4 words) of test data into EE main RAM at address 0x1000.
            emu.Bus.WriteWord(0x1000u, 0x11111111u);
            emu.Bus.WriteWord(0x1004u, 0x22222222u);
            emu.Bus.WriteWord(0x1008u, 0x33333333u);
            emu.Bus.WriteWord(0x100Cu, 0x44444444u);

            // Set up DMAC CH6 for a normal-mode RAM→FIFO transfer of 1 quadword.
            // CHCR: DIR=0 (RAM→FIFO), MOD=0 (normal), STR=1.
            emu.Bus.WriteWord(0x1000C400u + DmacChannel.REG_MADR, 0x00001000u);  // source = 0x1000
            emu.Bus.WriteWord(0x1000C400u + DmacChannel.REG_QWC,  0x00000001u);  // 1 QW
            emu.Bus.WriteWord(0x1000C400u + DmacChannel.REG_CHCR, 0x00000100u);  // STR=1, DIR=0

            Assert.AreEqual(4, emu.Bus.Sif.Sif1Count, "SIF1 FIFO must contain 4 words after CH6 transfer");
            Assert.AreEqual(0x11111111u, emu.Bus.Sif.ReadSif1(), "Word 0");
            Assert.AreEqual(0x22222222u, emu.Bus.Sif.ReadSif1(), "Word 1");
            Assert.AreEqual(0x33333333u, emu.Bus.Sif.ReadSif1(), "Word 2");
            Assert.AreEqual(0x44444444u, emu.Bus.Sif.ReadSif1(), "Word 3");
        }

        [TestMethod]
        public void SifDma_EeCh5_Sif0Fifo_To_Ram()
        {
            // EE DMAC CH5 (SIF0, FIFO→RAM) should drain SIF0 FIFO into EE main RAM.
            var emu = new PS2Emulator();

            // Pre-fill SIF0 FIFO with one quadword of data (simulating IOP push).
            emu.Bus.Sif.WriteSif0(0xAABBCCDDu);
            emu.Bus.Sif.WriteSif0(0x11223344u);
            emu.Bus.Sif.WriteSif0(0xDEADBEEFu);
            emu.Bus.Sif.WriteSif0(0xCAFEBABEu);

            // Set up DMAC CH5 for a normal-mode FIFO→RAM transfer of 1 quadword.
            // CHCR: DIR=1 (FIFO→RAM), MOD=0 (normal), STR=1.
            emu.Bus.WriteWord(0x1000C000u + DmacChannel.REG_MADR, 0x00002000u);  // dest = 0x2000
            emu.Bus.WriteWord(0x1000C000u + DmacChannel.REG_QWC,  0x00000001u);  // 1 QW
            emu.Bus.WriteWord(0x1000C000u + DmacChannel.REG_CHCR, 0x00000101u);  // STR=1, DIR=1

            Assert.AreEqual(0, emu.Bus.Sif.Sif0Count, "SIF0 FIFO must be drained after CH5 transfer");
            Assert.AreEqual(0xAABBCCDDu, emu.Bus.ReadWord(0x2000u), "EE RAM[0x2000] word 0");
            Assert.AreEqual(0x11223344u, emu.Bus.ReadWord(0x2004u), "EE RAM[0x2004] word 1");
            Assert.AreEqual(0xDEADBEEFu, emu.Bus.ReadWord(0x2008u), "EE RAM[0x2008] word 2");
            Assert.AreEqual(0xCAFEBABEu, emu.Bus.ReadWord(0x200Cu), "EE RAM[0x200C] word 3");
        }

        [TestMethod]
        public void SifDma_IopCh9_IopRam_To_Sif0Fifo()
        {
            // IOP DMAC CH9 (SIF0, RAM→device) should push IOP RAM contents into SIF0 FIFO.
            var sched = new DualCpuScheduler();

            // Write test words into IOP RAM at address 0x100.
            sched.IOP.Bus.WriteWord(0x100u, 0xFEDC9876u);
            sched.IOP.Bus.WriteWord(0x104u, 0x13572468u);

            // Set up IOP DMAC CH9 for a burst RAM→SIF0 transfer of 2 words.
            // CHCR: DIR=0 (RAM→device), SYNC=0 (burst), STR=bit24.
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_MADR, 0x00000100u);
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_BCR,  0x00000002u);  // 2 words
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_CHCR, 0x01000000u);  // STR=1, DIR=0

            Assert.AreEqual(2, sched.EE.Bus.Sif.Sif0Count, "SIF0 FIFO must contain 2 words after IOP CH9 transfer");
            Assert.AreEqual(0xFEDC9876u, sched.EE.Bus.Sif.ReadSif0(), "SIF0 FIFO word 0");
            Assert.AreEqual(0x13572468u, sched.EE.Bus.Sif.ReadSif0(), "SIF0 FIFO word 1");
        }

        [TestMethod]
        public void SifDma_IopCh10_Sif1Fifo_To_IopRam()
        {
            // IOP DMAC CH10 (SIF1, device→RAM) should drain SIF1 FIFO into IOP RAM.
            var sched = new DualCpuScheduler();

            // Pre-fill SIF1 FIFO with 2 words (simulating EE push).
            sched.EE.Bus.Sif.WriteSif1(0xABCD1234u);
            sched.EE.Bus.Sif.WriteSif1(0x56789ABCu);

            // Set up IOP DMAC CH10 for a burst device→RAM transfer of 2 words.
            // CHCR: DIR=1 (device→RAM), SYNC=0 (burst), STR=bit24.
            sched.IOP.Bus.WriteWord(0x1F801530u + IopDmacChannel.REG_MADR, 0x00000200u);
            sched.IOP.Bus.WriteWord(0x1F801530u + IopDmacChannel.REG_BCR,  0x00000002u);  // 2 words
            sched.IOP.Bus.WriteWord(0x1F801530u + IopDmacChannel.REG_CHCR, 0x01000001u);  // STR=1, DIR=1

            Assert.AreEqual(0, sched.EE.Bus.Sif.Sif1Count, "SIF1 FIFO must be drained after IOP CH10 transfer");
            Assert.AreEqual(0xABCD1234u, sched.IOP.Bus.ReadWord(0x200u), "IOP RAM[0x200] word 0");
            Assert.AreEqual(0x56789ABCu, sched.IOP.Bus.ReadWord(0x204u), "IOP RAM[0x204] word 1");
        }

        [TestMethod]
        public void SifDma_FullRoundTrip_EeToIop()
        {
            // Full round-trip: EE writes data via CH6 → SIF1 FIFO → IOP reads via CH10.
            var sched = new DualCpuScheduler();

            // 1. Write payload into EE main RAM.
            sched.EE.Bus.WriteWord(0x3000u, 0xC0FFEE00u);
            sched.EE.Bus.WriteWord(0x3004u, 0xC0FFEE01u);
            sched.EE.Bus.WriteWord(0x3008u, 0xC0FFEE02u);
            sched.EE.Bus.WriteWord(0x300Cu, 0xC0FFEE03u);

            // 2. EE DMAC CH6 (SIF1, RAM→FIFO): push 1 QW into SIF1 FIFO.
            sched.EE.Bus.WriteWord(0x1000C400u + DmacChannel.REG_MADR, 0x00003000u);
            sched.EE.Bus.WriteWord(0x1000C400u + DmacChannel.REG_QWC,  0x00000001u);
            sched.EE.Bus.WriteWord(0x1000C400u + DmacChannel.REG_CHCR, 0x00000100u);  // STR=1, DIR=0

            Assert.AreEqual(4, sched.EE.Bus.Sif.Sif1Count, "SIF1 FIFO must have 4 words after EE push");

            // 3. IOP DMAC CH10 (SIF1, device→RAM): drain SIF1 FIFO into IOP RAM.
            sched.IOP.Bus.WriteWord(0x1F801530u + IopDmacChannel.REG_MADR, 0x00000400u);
            sched.IOP.Bus.WriteWord(0x1F801530u + IopDmacChannel.REG_BCR,  0x00000004u);  // 4 words
            sched.IOP.Bus.WriteWord(0x1F801530u + IopDmacChannel.REG_CHCR, 0x01000001u);  // STR=1, DIR=1

            Assert.AreEqual(0, sched.EE.Bus.Sif.Sif1Count, "SIF1 FIFO must be empty after IOP drain");
            Assert.AreEqual(0xC0FFEE00u, sched.IOP.Bus.ReadWord(0x400u), "IOP word 0");
            Assert.AreEqual(0xC0FFEE01u, sched.IOP.Bus.ReadWord(0x404u), "IOP word 1");
            Assert.AreEqual(0xC0FFEE02u, sched.IOP.Bus.ReadWord(0x408u), "IOP word 2");
            Assert.AreEqual(0xC0FFEE03u, sched.IOP.Bus.ReadWord(0x40Cu), "IOP word 3");
        }

        [TestMethod]
        public void SifDma_FullRoundTrip_IopToEe()
        {
            // Full round-trip: IOP writes data via CH9 → SIF0 FIFO → EE reads via CH5.
            var sched = new DualCpuScheduler();

            // 1. Write payload into IOP RAM.
            sched.IOP.Bus.WriteWord(0x500u, 0xB00B0000u);
            sched.IOP.Bus.WriteWord(0x504u, 0xB00B0001u);
            sched.IOP.Bus.WriteWord(0x508u, 0xB00B0002u);
            sched.IOP.Bus.WriteWord(0x50Cu, 0xB00B0003u);

            // 2. IOP DMAC CH9 (SIF0, RAM→device): push 4 words into SIF0 FIFO.
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_MADR, 0x00000500u);
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_BCR,  0x00000004u);  // 4 words
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_CHCR, 0x01000000u);  // STR=1, DIR=0

            Assert.AreEqual(4, sched.EE.Bus.Sif.Sif0Count, "SIF0 FIFO must have 4 words after IOP push");

            // 3. EE DMAC CH5 (SIF0, FIFO→RAM): drain SIF0 FIFO into EE main RAM.
            sched.EE.Bus.WriteWord(0x1000C000u + DmacChannel.REG_MADR, 0x00006000u);
            sched.EE.Bus.WriteWord(0x1000C000u + DmacChannel.REG_QWC,  0x00000001u);  // 1 QW = 4 words
            sched.EE.Bus.WriteWord(0x1000C000u + DmacChannel.REG_CHCR, 0x00000101u);  // STR=1, DIR=1

            Assert.AreEqual(0, sched.EE.Bus.Sif.Sif0Count, "SIF0 FIFO must be empty after EE drain");
            Assert.AreEqual(0xB00B0000u, sched.EE.Bus.ReadWord(0x6000u), "EE word 0");
            Assert.AreEqual(0xB00B0001u, sched.EE.Bus.ReadWord(0x6004u), "EE word 1");
            Assert.AreEqual(0xB00B0002u, sched.EE.Bus.ReadWord(0x6008u), "EE word 2");
            Assert.AreEqual(0xB00B0003u, sched.EE.Bus.ReadWord(0x600Cu), "EE word 3");
        }
    }
}
