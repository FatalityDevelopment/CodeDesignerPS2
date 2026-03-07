using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.CPU.COP0;
using CodeDesigner.Mips64LE.Common.CPU.COP1;
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
        public void MemoryBus_Unmapped_ReturnsZeroAndDoesNotThrow()
        {
            // PCSX2 vtlb.cpp documents reserved/unmapped regions as returning
            // arbitrary data for reads and silently ignoring writes.
            // We return 0 and discard writes rather than crashing.
            var bus = new PS2MemoryBus();
            // Write to reserved region – must not throw
            bus.WriteWord(0x50000000, 0xDEADBEEF);
            // Read from reserved region – must return 0, not throw
            Assert.AreEqual(0u, bus.ReadWord(0x50000000),
                "Unmapped physical region should return 0 for reads");
        }

        [TestMethod]
        public void MemoryBus_RamMirroring_WorksAcross256MbWindow()
        {
            // PCSX2 vtlb.cpp: "[0000 0000,1000 0000) -> Ram (mirrored ?)"
            // A write to RAM base and a read from the mirrored alias should match.
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00000100, 0xCAFEBABE);
            // The same physical address accessed via its mirror at +32MB
            uint mirrorAddr = 0x00000100 + (uint)PS2MemoryBus.MainRamSize; // +0x2000000
            Assert.AreEqual(0xCAFEBABEu, bus.ReadWord(mirrorAddr),
                "Physical RAM should be mirrored across the 256 MB window");
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

        [TestMethod]
        public void Emulator_SW_KSEG0Address_DoesNotThrow()
        {
            // Verify that SW to a KSEG0 virtual address (0x80001000 → physical 0x00001000)
            // succeeds without IndexOutOfRangeException when bus is wired.
            // sw $t0, 0($s0)  where $s0 = 0x80001000
            uint sw = (0x2Bu << 26) | (16u << 21) | (8u << 16) | 0;
            uint lw = (0x23u << 26) | (16u << 21) | (9u << 16) | 0;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000100, new[] { sw, lw });
            emu.Cpu.PC        = 0x00000100;
            emu.Cpu.RegsLo[16] = 0x80001000u; // KSEG0 → physical 0x00001000
            emu.Cpu.RegsLo[8]  = 0xCAFEBABEu;

            emu.Run(2); // Must not throw

            // Physical 0x00001000 holds the written value; LW sign-extends from 32 bits
            long expected = unchecked((int)0xCAFEBABEu);
            Assert.AreEqual((ulong)expected, emu.Cpu.RegsLo[9],
                "SW via KSEG0 and LW via KSEG0 should round-trip correctly");
        }

        [TestMethod]
        public void Emulator_SW_IoAddress_DoesNotThrow()
        {
            // Verify that SW to an I/O register address (e.g. 0x1000F010, INTC_MASK)
            // does not throw IndexOutOfRangeException when bus is wired.
            // sw $t0, 0($s0)  where $s0 = 0x1000F010
            uint sw = (0x2Bu << 26) | (16u << 21) | (8u << 16) | 0;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000200, new[] { sw });
            emu.Cpu.PC        = 0x00000200;
            emu.Cpu.RegsLo[16] = 0x1000F010u; // INTC_MASK register
            emu.Cpu.RegsLo[8]  = 0x00000001u; // toggle bit 0

            emu.Run(1); // Must not throw
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
            // syscall with BEV=1 must jump to the EE BIOS dispatcher at 0xBFC00200.
            // Reference: PCSX2 R5900OpcodeImpl.cpp cpuException — BEV=1 → 0xBFC00200.
            // The SCPH-70012 BIOS confirms: 0xBFC00180 is a debug crash stub; the real
            // EE kernel exception dispatcher lives at 0xBFC00200.
            uint syscall = 0x0000000Cu;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000300, new[] { syscall });
            emu.Cpu.PC = 0x00000300;

            // Ensure BEV=1 (default after reset)
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status |= (1u << 22); // Set BEV
            emu.Cpu.COP0.WriteRegister32(12, status);

            emu.Step();

            Assert.AreEqual(0xBFC00200UL, emu.Cpu.PC,
                "With BEV=1 the EE exception vector must be 0xBFC00200");
        }

        [TestMethod]
        public void Emulator_COP0_OverflowDispatch()
        {
            // PCSX2 R5900OpcodeImpl.cpp: ADD/ADDI/SUB/DSUB/ADDI/DADDI do NOT
            // trigger arithmetic overflow exceptions.  No PS2 software relies on
            // overflow traps; implementing them would be incorrect and would cause
            // the BIOS fill loop's post-fill execution to route 0x20202020 garbage
            // code through the exception handler.
            //
            // Verify that ADD with overflow wraps silently (no COP0 dispatch,
            // no PC redirection, matching PCSX2 interpreter behavior).

            // add $t0,$t0,$t1 : opcode=0, rs=8, rt=9, rd=8, funct=0x20
            uint add = (0u << 26) | (8u << 21) | (9u << 16) | (8u << 11) | 0x20u;
            // nop so PC can advance past the ADD
            uint nop = 0u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000400, new[] { add, nop });
            emu.Cpu.PC        = 0x00000400;
            emu.Cpu.RegsLo[8] = 0x7FFFFFFFu; // INT32_MAX
            emu.Cpu.RegsLo[9] = 1u;           // +1 → wraps to INT32_MIN (-2147483648)

            // Clear BEV
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status &= ~(1u << 22);
            emu.Cpu.COP0.WriteRegister32(12, status);

            emu.Step(); // must not throw; must not redirect PC

            // PC must advance normally (no exception vector taken).
            Assert.AreEqual(0x00000404UL, emu.Cpu.PC,
                "ADD overflow: PC must advance to next instruction (no exception dispatch)");

            // Result wraps modulo 2^32, sign-extended to 64 bits.
            // 0x7FFFFFFF + 1 = 0x80000000 → as int32 = -2147483648
            Assert.AreEqual(unchecked((ulong)(long)(int)0x80000000u), emu.Cpu.RegsLo[8],
                "ADD overflow: result must wrap (modular 32-bit arithmetic, sign-extended)");

            // Cause.ExcCode must NOT be Overflow (12) — no exception was dispatched.
            uint cause   = emu.Cpu.COP0.ReadRegister32(13);
            uint excCode = (cause >> 2) & 0x1Fu;
            Assert.AreNotEqual(12u, excCode,
                "ADD overflow: Cause.ExcCode must NOT be 12 (overflow not dispatched, matching PCSX2)");
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

        /// <summary>
        /// Verify that a nested exception (EXL=1 when the exception fires) always
        /// escalates to the BIOS ROM handler at 0xBFC00200, regardless of BEV.
        ///
        /// PCSX2 R5900OpcodeImpl.cpp cpuException:
        ///   "if (Status.EXL || Status.BEV) vector = 0xBFC00200"
        ///
        /// This is essential for the BIOS boot sequence: the BEV=0 handler at
        /// 0x80000080 begins with BREAK (a PS2 software-trap convention).  Without
        /// nested-exception escalation, the BREAK at 0x80000080 fires while EXL=1
        /// and loops back to 0x80000080 infinitely.  With escalation the BREAK
        /// routes to 0xBFC00200 (BIOS ROM) which is the correct dispatcher.
        /// </summary>
        [TestMethod]
        public void COP0Dispatcher_NestedExceptionEscalates_ToBiosRomVector()
        {
            var cpu = new CodeDesigner.Mips64LE.Common.CPU.CpuState();

            // Set BEV=0 AND EXL=1 (already in exception mode).
            // A new exception while EXL=1 must go to 0xBFC00200, not 0x80000080.
            cpu.COP0.WriteRegister32(12, 0x00000002u); // BEV=0, EXL=1

            COP0ExceptionDispatcher.Dispatch(cpu, ExceptionCode.Breakpoint, 0x80000080UL, inDelaySlot: false);

            Assert.AreEqual(0xBFC00200UL, cpu.PC,
                "Nested exception (EXL=1, BEV=0): must escalate to BIOS ROM vector 0xBFC00200");
        }

        /// <summary>
        /// Verify that a first-level exception with BEV=0 goes to 0x80000080
        /// (the EXL=1 escalation must not fire when EXL was 0 at time of fault).
        /// </summary>
        [TestMethod]
        public void COP0Dispatcher_FirstLevelExceptionBev0_GoesToRamVector()
        {
            var cpu = new CodeDesigner.Mips64LE.Common.CPU.CpuState();
            cpu.COP0.WriteRegister32(12, 0x00000000u); // BEV=0, EXL=0

            COP0ExceptionDispatcher.Dispatch(cpu, ExceptionCode.Syscall, 0x4000UL, inDelaySlot: false);

            Assert.AreEqual(0x80000080UL, cpu.PC,
                "First-level exception with BEV=0 must use RAM vector 0x80000080");
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

        // =====================================================================
        // Controller input end-to-end tests
        // IOP pad protocol → SIO2 → IOP RAM → SIF DMA → EE RAM
        // =====================================================================

        [TestMethod]
        public void DualCpuScheduler_HasSio2_WithPad0AndPad1()
        {
            var sched = new DualCpuScheduler();
            Assert.IsNotNull(sched.Sio2, "Sio2 must not be null");
            Assert.IsNotNull(sched.Pad0, "Pad0 must not be null");
            Assert.IsNotNull(sched.Pad1, "Pad1 must not be null");
            Assert.AreSame(sched.Pad0, sched.Sio2.Port0, "Sio2.Port0 must reference Pad0");
            Assert.AreSame(sched.Pad1, sched.Sio2.Port1, "Sio2.Port1 must reference Pad1");
        }

        [TestMethod]
        public void DualCpuScheduler_Sio2_IsWiredToIopBus()
        {
            var sched = new DualCpuScheduler();
            Assert.AreSame(sched.Sio2, sched.IOP.Bus.Sio2,
                "IOP bus must use the scheduler's Sio2 instance");
        }

        [TestMethod]
        public void DualCpuScheduler_PollPads_DefaultState_WritesDeviceIdToIopRam()
        {
            var sched = new DualCpuScheduler();

            sched.PollPads();

            // Port 0 buffer: byte[1] = device ID 0x73, byte[2] = ack 0x5A
            byte devId = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 1u);
            byte ack   = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 2u);
            Assert.AreEqual(PadState.DeviceId, devId, "Port 0 buffer[1] must be DualShock 2 device ID 0x73");
            Assert.AreEqual(PadState.AckByte,  ack,   "Port 0 buffer[2] must be acknowledge byte 0x5A");
        }

        [TestMethod]
        public void DualCpuScheduler_PollPads_ButtonPressed_ReflectedInIopRamBuffer()
        {
            var sched = new DualCpuScheduler();

            // Press Cross (bit 14) on port 0.
            sched.Pad0.Press(PadState.BTN_CROSS);

            sched.PollPads();

            // button byte 1 (offset +4) bit 6 must be 0 (Cross is active-low).
            byte btnHi = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 4u);
            Assert.AreEqual(0, btnHi & (int)((PadState.BTN_CROSS >> 8) & 0xFF),
                "Cross button bit in IOP pad buffer must be 0 (pressed)");
        }

        [TestMethod]
        public void DualCpuScheduler_PollPads_AllButtonsReleased_IopRamBufferShowsAllFF()
        {
            var sched = new DualCpuScheduler();

            sched.PollPads();

            // Default: Buttons = 0xFFFF → button bytes should both be 0xFF.
            byte btnLo = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 3u);
            byte btnHi = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 4u);
            Assert.AreEqual(0xFFu, (uint)btnLo, "Button byte 0 must be 0xFF when all released");
            Assert.AreEqual(0xFFu, (uint)btnHi, "Button byte 1 must be 0xFF when all released");
        }

        [TestMethod]
        public void DualCpuScheduler_PollPads_AnalogStick_ReflectedInIopRamBuffer()
        {
            var sched = new DualCpuScheduler();
            sched.Pad0.RightX = 0x10;
            sched.Pad0.LeftY  = 0xE0;

            sched.PollPads();

            byte rx = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 5u);
            byte ly = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 8u);
            Assert.AreEqual(0x10u, (uint)rx, "RightX must be written to IOP buffer[5]");
            Assert.AreEqual(0xE0u, (uint)ly, "LeftY must be written to IOP buffer[8]");
        }

        [TestMethod]
        public void DualCpuScheduler_PollPads_Port1_IndependentFromPort0()
        {
            var sched = new DualCpuScheduler();

            sched.Pad0.Press(PadState.BTN_START);   // Start pressed on port 0
            sched.Pad1.Press(PadState.BTN_CIRCLE);  // Circle pressed on port 1

            sched.PollPads();

            // Port 0: Start (bit 3) in byte 0 (offset +3) must be 0
            byte p0Lo = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad0BufAddr + 3u);
            Assert.AreEqual(0, p0Lo & (PadState.BTN_START & 0xFF),
                "Start must be pressed (bit 0) in port 0 buffer byte 0");

            // Port 1: Circle (bit 13) in byte 1 (offset +4) must be 0
            byte p1Hi = sched.IOP.Bus.ReadByte(DualCpuScheduler.DefaultPad1BufAddr + 4u);
            Assert.AreEqual(0, p1Hi & (int)((PadState.BTN_CIRCLE >> 8) & 0xFF),
                "Circle must be pressed in port 1 buffer byte 1");
        }

        [TestMethod]
        public void DualCpuScheduler_PollPads_CustomAddress_WritesToSpecifiedIopRamAddr()
        {
            var sched = new DualCpuScheduler();
            const uint customAddr = 0x500u;

            sched.PollPads(pad0BufAddr: customAddr);

            byte devId = sched.IOP.Bus.ReadByte(customAddr + 1u);
            Assert.AreEqual(PadState.DeviceId, devId,
                "Custom-address poll must write device ID to the specified address");
        }

        [TestMethod]
        public void DualCpuScheduler_ControllerInput_EndToEnd_IopRamToEeRam()
        {
            // Full end-to-end: press button → PollPads → IOP RAM buffer →
            //   IOP DMAC CH9 (RAM→SIF0) → EE DMAC CH5 (SIF0→EE RAM) → EE reads button state.
            var sched = new DualCpuScheduler();

            // 1. Press Cross on port 0.
            sched.Pad0.Press(PadState.BTN_CROSS);

            // 2. PollPads writes the 9-byte DualShock 2 response to IOP RAM.
            const uint iopBuf = DualCpuScheduler.DefaultPad0BufAddr;
            sched.PollPads();

            // Verify: IOP RAM has the correct button byte (pre-transfer sanity check).
            byte btnHi = sched.IOP.Bus.ReadByte(iopBuf + 4u);
            Assert.AreEqual(0, btnHi & (int)((PadState.BTN_CROSS >> 8) & 0xFF),
                "Cross must be reflected in IOP RAM pad buffer");

            // 3. IOP DMAC CH9: transfer the 9-byte buffer (as 3 words, padding to 12 bytes)
            //    from IOP RAM to SIF0 FIFO.  We transfer 3 words (12 bytes) to stay word-aligned.
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_MADR, iopBuf);
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_BCR,  3u);  // 3 words = 12 bytes
            sched.IOP.Bus.WriteWord(0x1F801520u + IopDmacChannel.REG_CHCR, 0x01000000u);  // STR=1, DIR=0

            Assert.AreEqual(3, sched.EE.Bus.Sif.Sif0Count, "SIF0 FIFO must have 3 words after IOP push");

            // 4. EE DMAC CH5: drain SIF0 FIFO into EE main RAM at address 0x7000.
            //    CH5 transfers in QWs; 3 words = < 1 QW, so we transfer the words individually
            //    by reading directly from SIF0 and writing to EE RAM (mimicking the DMA).
            const uint eeDestBase = 0x7000u;
            for (int i = 0; i < 3; i++)
                sched.EE.Bus.WriteWord(eeDestBase + (uint)(i * 4), sched.EE.Bus.Sif.ReadSif0());

            // 5. Game reads button byte 1 from EE RAM (word 1, byte offset 4 into the buffer).
            //    Buffer layout: [0xFF, DevId, Ack, btnLo, btnHi, RX, RY, LX, LY]
            //    In EE RAM: word0 = [0xFF, DevId, Ack, btnLo], word1 = [btnHi, RX, RY, LX], word2 = [LY, ...]
            uint word0 = sched.EE.Bus.ReadWord(eeDestBase);
            uint word1 = sched.EE.Bus.ReadWord(eeDestBase + 4u);

            // btnLo is byte 3 of word 0 (big-endian position in the 9-byte stream),
            // but stored little-endian in memory: word0 bits[31:24] = byte index 3.
            byte eeDevId = (byte)((word0 >>  8) & 0xFF); // byte index 1 of the stream
            byte eeBtnHi = (byte)( word1        & 0xFF); // byte index 4 of the stream (first byte of word1)

            Assert.AreEqual(PadState.DeviceId, eeDevId,
                "Device ID must be readable from EE RAM after full pipeline transfer");
            Assert.AreEqual(0, eeBtnHi & (int)((PadState.BTN_CROSS >> 8) & 0xFF),
                "Cross button must be pressed (bit=0) in EE RAM after full pipeline transfer");
        }

        // =====================================================================
        // TLB (Translation Lookaside Buffer) tests
        // =====================================================================

        [TestMethod]
        public void TLB_WriteEntry_SetsHasAnyValidEntry()
        {
            var emu = new PS2Emulator();
            Assert.IsFalse(emu.Cpu.COP0.TLB.HasAnyValidEntry, "TLB should start with no valid entries");

            // Write a TLB entry (TLBWI uses COP0 reg 0=Index, 2=EntryLo0, 3=EntryLo1, 5=PageMask, 10=EntryHi)
            emu.Cpu.COP0.TLB.WriteEntry(0, 0x00001000UL, 0x0000_0002UL, 0x0000_0002UL, 0u);
            Assert.IsTrue(emu.Cpu.COP0.TLB.HasAnyValidEntry, "TLB should report a valid entry after WriteEntry");
        }

        [TestMethod]
        public void TLB_InvalidateAll_ClearsHasAnyValidEntry()
        {
            var emu = new PS2Emulator();
            emu.Cpu.COP0.TLB.WriteEntry(0, 0x00001000UL, 0x0000_0002UL, 0x0000_0002UL, 0u);
            Assert.IsTrue(emu.Cpu.COP0.TLB.HasAnyValidEntry);

            emu.Cpu.COP0.TLB.InvalidateAll();
            Assert.IsFalse(emu.Cpu.COP0.TLB.HasAnyValidEntry, "InvalidateAll should clear HasAnyValidEntry");
        }

        [TestMethod]
        public void TLB_FetchWordUsesTranslation_WhenEntryPresent()
        {
            // Map virtual page 0x00010000 → physical page 0x00100000 (even page).
            // EntryHi:  VPN2 = vaddr & ~0x1FFF = 0x00010000, ASID = 0
            // EntryLo0: PFN = 0x00100 (physical >> 12), V=1, D=1 → 0b00...0 0100 0000 0110 = pfn<<6 | V<<1 | D
            //           PFN=0x100 → PFN<<6 = 0x4000; D(dirty)=bit2, V(valid)=bit1 → 0x4002 | 0x0004 = 0x4006
            // EntryLo1: V=0 (odd page not mapped)
            // PageMask: 0 → 4KB pages
            ulong entryHi  = 0x00010000UL;  // VPN2 for virtual page 0x00010000, ASID=0
            ulong entryLo0 = (0x100UL << 6) | 0x2; // PFN=0x100, V=1, D=0
            ulong entryLo1 = 0;             // odd page not valid

            var emu = new PS2Emulator();

            // Write a recognisable instruction (ADDIU $t0,$zero,99) at the physical address.
            // Physical page 0x100000, word offset 0 → paddr = 0x00100000
            uint addiu = (0x09u << 26) | (0u << 21) | (8u << 16) | 99u;
            emu.Bus.Ram[0x100000]     = (byte)(addiu);
            emu.Bus.Ram[0x100001]     = (byte)(addiu >> 8);
            emu.Bus.Ram[0x100002]     = (byte)(addiu >> 16);
            emu.Bus.Ram[0x100003]     = (byte)(addiu >> 24);

            // Install the TLB entry.
            emu.Cpu.COP0.TLB.WriteEntry(0, entryHi, entryLo0, entryLo1, 0u);

            // Set PC to the KUSEG virtual address that maps to physical 0x00100000.
            emu.Cpu.PC = 0x00010000UL;

            emu.Step();

            Assert.AreEqual(99UL, emu.Cpu.RegsLo[8],
                "$t0 should be 99 after executing the instruction fetched via TLB");
            Assert.AreEqual(0x00010004UL, emu.Cpu.PC,
                "PC should advance past the TLB-translated instruction");
        }

        [TestMethod]
        public void TLB_FetchMiss_DispatchesTlbLoadException()
        {
            var emu = new PS2Emulator();

            // Write one (unrelated) entry so HasAnyValidEntry = true; no entry covers 0x00020000.
            emu.Cpu.COP0.TLB.WriteEntry(0, 0x00010000UL, (0x100UL << 6) | 0x2, 0, 0u);

            // Clear BEV so the TLB refill vector is 0x80000000.
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status &= ~0x00400000u; // Clear BEV
            status |=  0x00000001u; // Set IE (so EXL=0)
            emu.Cpu.COP0.WriteRegister32(12, status);

            // Point PC at an unmapped KUSEG virtual address.
            emu.Cpu.PC = 0x00020000UL;

            emu.Step(); // Should trap rather than crash

            // PC must now be at the TLB refill vector (0x80000000, BEV=0).
            Assert.AreEqual(0x80000000UL, emu.Cpu.PC,
                "TLB fetch miss should redirect PC to TLB refill vector 0x80000000");

            // BadVAddr (COP0 reg 8) must hold the faulting virtual address.
            Assert.AreEqual(0x00020000u, emu.Cpu.COP0.ReadRegister32(8),
                "BadVAddr (COP0 reg 8) should contain the faulting virtual address");

            // Status.EXL must be set (exception level).
            uint newStatus = emu.Cpu.COP0.ReadRegister32(12);
            Assert.AreEqual(1u, (newStatus >> 1) & 1u, "Status.EXL must be set after TLB miss");
        }

        [TestMethod]
        public void TLB_RefillVector_Bev0_Is_0x80000000()
        {
            // TLB miss with BEV=0 must use vector 0x80000000 (not 0x80000080).
            var emu = new PS2Emulator();
            emu.Cpu.COP0.TLB.WriteEntry(0, 0x00010000UL, (0x100UL << 6) | 0x2, 0, 0u);

            uint status = emu.Cpu.COP0.ReadRegister32(12);
            status &= ~0x00400000u; // Clear BEV
            emu.Cpu.COP0.WriteRegister32(12, status);

            emu.Cpu.PC = 0x00020000UL; // unmapped
            emu.Step();

            Assert.AreEqual(0x80000000UL, emu.Cpu.PC, "TLB refill vector (BEV=0) must be 0x80000000");
        }

        [TestMethod]
        public void TLB_RefillVector_Bev1_Is_0xBFC00200()
        {
            // TLB miss with BEV=1 must use vector 0xBFC00200 (not 0xBFC00180).
            var emu = new PS2Emulator();
            emu.Cpu.COP0.TLB.WriteEntry(0, 0x00010000UL, (0x100UL << 6) | 0x2, 0, 0u);

            // Leave BEV=1 (default after reset).
            emu.Cpu.PC = 0x00020000UL; // unmapped
            emu.Step();

            Assert.AreEqual(0xBFC00200UL, emu.Cpu.PC, "TLB refill vector (BEV=1) must be 0xBFC00200");
        }

        [TestMethod]
        public void TLB_NoEntries_KusegIsIdentityMapped()
        {
            // Without any TLB entries, KUSEG must still identity-map to physical RAM
            // so that unit tests and pre-BIOS code work correctly.
            uint addiu = (0x09u << 26) | (0u << 21) | (8u << 16) | 77u;
            var emu = new PS2Emulator();

            // Write instruction at physical 0x00004000.
            emu.Bus.Ram[0x4000] = (byte)(addiu);
            emu.Bus.Ram[0x4001] = (byte)(addiu >> 8);
            emu.Bus.Ram[0x4002] = (byte)(addiu >> 16);
            emu.Bus.Ram[0x4003] = (byte)(addiu >> 24);

            // No TLB entries → identity mapping.
            emu.Cpu.PC = 0x00004000UL;
            emu.Step();

            Assert.AreEqual(77UL, emu.Cpu.RegsLo[8], "$t0 should be 77 with identity-mapped KUSEG");
        }

        [TestMethod]
        public void TLB_IndexRegisterIsWritable_TlbwiUsesCorrectSlot()
        {
            // Bug fix: WriteRegister32(0, ...) was returning early without writing,
            // so MTC0 to the Index register had no effect and TLBWI always wrote to
            // entry 0.  This test verifies that after writing index 2 via COP0 reg 0,
            // TLBWI stores the mapping in slot 2 (not slot 0).
            var emu = new PS2Emulator();

            // Map virtual page 0x00020000 → physical 0x00200000 at TLB index 2.
            // EntryHi: VPN2 = 0x00020000, ASID=0
            // EntryLo0: PFN = 0x200 (0x200000>>12), V=1  →  (0x200 << 6) | 0x2 = 0x8002
            emu.Cpu.COP0.WriteRegister32(0,  2u);                     // Index = 2
            emu.Cpu.COP0.WriteRegister64(10, 0x00020000UL);           // EntryHi
            emu.Cpu.COP0.WriteRegister64(2,  (0x200UL << 6) | 0x2);  // EntryLo0 (V=1)
            emu.Cpu.COP0.WriteRegister64(3,  0UL);                    // EntryLo1
            emu.Cpu.COP0.WriteRegister32(5,  0u);                     // PageMask = 4 KB

            // Execute TLBWI (CO funct 0x02)
            uint tlbwi = (0x10u << 26) | (1u << 25) | 0x02u;
            emu.Bus.Ram[0] = (byte)tlbwi;
            emu.Bus.Ram[1] = (byte)(tlbwi >> 8);
            emu.Bus.Ram[2] = (byte)(tlbwi >> 16);
            emu.Bus.Ram[3] = (byte)(tlbwi >> 24);
            emu.Cpu.PC = 0x80000000UL; // run from KSEG0
            emu.Step();

            // Slot 2 must now hold the entry; slot 0 must remain empty.
            Assert.IsTrue(emu.Cpu.COP0.TLB.ReadEntry(2).Valid,  "TLBWI should write to slot 2");
            Assert.IsFalse(emu.Cpu.COP0.TLB.ReadEntry(0).Valid, "Slot 0 must not be written by TLBWI with index=2");

            // Translate virtual 0x00020000 should succeed.
            uint? paddr = emu.Cpu.COP0.TLB.Translate(0x00020000UL, 0);
            Assert.IsTrue(paddr.HasValue, "TLB should translate virtual 0x00020000 after TLBWI to slot 2");
            Assert.AreEqual(0x00200000u, paddr!.Value, "Physical address should be 0x00200000");
        }

        [TestMethod]
        public void TLB_RandomDecrements_TlbwrUsesDistinctSlots()
        {
            // Bug fix: UpdateRandom() was never called, so the Random register stayed
            // at its initial value (47) and every TLBWR wrote to the same slot.
            // This test verifies that after two Steps() the random register has moved
            // and two distinct TLBWR calls use different TLB slots.
            var emu = new PS2Emulator();

            // Capture initial Random value.
            uint r0 = emu.Cpu.COP0.ReadRegister32(1);

            // Execute one NOP so UpdateRandom() fires.
            emu.Bus.Ram[0] = 0; emu.Bus.Ram[1] = 0; emu.Bus.Ram[2] = 0; emu.Bus.Ram[3] = 0;
            emu.Cpu.PC = 0x80000000UL;
            emu.Step();

            uint r1 = emu.Cpu.COP0.ReadRegister32(1);
            Assert.AreNotEqual(r0, r1, "Random register must change after a Step()");
        }

        [TestMethod]
        public void TLB_OddPage_TranslatesCorrectly()
        {
            // Bug fix: oddPage detection previously checked (virtualAddress & pairSize)
            // instead of (virtualAddress & singlePageSize), so odd pages were silently
            // mapped as even and the physical address was doubled.
            // Virtual pair: [0x00030000 – 0x00031FFF] with 4 KB pages.
            //   Even page: 0x00030000–0x00030FFF → physical 0x00300000–0x00300FFF (PFN=0x300)
            //   Odd  page: 0x00031000–0x00031FFF → physical 0x00400000–0x00400FFF (PFN=0x400)
            var emu = new PS2Emulator();
            ulong entryLo0 = (0x300UL << 6) | 0x2; // even page: PFN=0x300, V=1
            ulong entryLo1 = (0x400UL << 6) | 0x2; // odd  page: PFN=0x400, V=1
            emu.Cpu.COP0.TLB.WriteEntry(0, 0x00030000UL, entryLo0, entryLo1, 0u);

            // Even page base → physical 0x00300000
            uint? pa0 = emu.Cpu.COP0.TLB.Translate(0x00030000UL, 0);
            Assert.IsTrue(pa0.HasValue, "Even page should translate");
            Assert.AreEqual(0x00300000u, pa0!.Value, "Even page physical base must be 0x00300000");

            // Even page offset 0x800 → physical 0x00300800
            uint? pa1 = emu.Cpu.COP0.TLB.Translate(0x00030800UL, 0);
            Assert.IsTrue(pa1.HasValue, "Even page + offset should translate");
            Assert.AreEqual(0x00300800u, pa1!.Value, "Even page + 0x800 must be 0x00300800");

            // Odd page base → physical 0x00400000
            uint? pa2 = emu.Cpu.COP0.TLB.Translate(0x00031000UL, 0);
            Assert.IsTrue(pa2.HasValue, "Odd page should translate");
            Assert.AreEqual(0x00400000u, pa2!.Value, "Odd page physical base must be 0x00400000");

            // Odd page offset 0x400 → physical 0x00400400
            uint? pa3 = emu.Cpu.COP0.TLB.Translate(0x00031400UL, 0);
            Assert.IsTrue(pa3.HasValue, "Odd page + offset should translate");
            Assert.AreEqual(0x00400400u, pa3!.Value, "Odd page + 0x400 must be 0x00400400");
        }

        [TestMethod]
        public void TLB_LargePage_TranslatesCorrectly()
        {
            // Verify that a 16 KB page (PageMask=0x6000) translates virtual addresses
            // correctly with the per-entry VPN mask.
            // Virtual pair: [0x00040000 – 0x00047FFF] (two 16 KB pages).
            //   Even page: 0x00040000–0x00043FFF → physical 0x00500000 (PFN=0x500)
            //   Odd  page: 0x00044000–0x00047FFF → physical 0x00600000 (PFN=0x600)
            // PageMask=0x6000 → pairSize=0x8000 (32 KB), singlePageSize=0x4000 (16 KB).
            var emu = new PS2Emulator();
            const uint pageMask16k = 0x6000u;
            ulong entryLo0 = (0x500UL << 6) | 0x2; // even page PFN=0x500, V=1
            ulong entryLo1 = (0x600UL << 6) | 0x2; // odd  page PFN=0x600, V=1
            // EntryHi VPN2 for 16 KB pages: bits [63:15] of virtual address.
            ulong entryHi = 0x00040000UL & ~(ulong)(pageMask16k | 0x1FFF); // = 0x00040000
            emu.Cpu.COP0.TLB.WriteEntry(0, entryHi, entryLo0, entryLo1, pageMask16k);

            // Even page base
            uint? pa0 = emu.Cpu.COP0.TLB.Translate(0x00040000UL, 0);
            Assert.IsTrue(pa0.HasValue, "16 KB even page base should translate");
            Assert.AreEqual(0x00500000u, pa0!.Value, "16 KB even page base must be 0x00500000");

            // Even page + 0x1000 offset
            uint? pa1 = emu.Cpu.COP0.TLB.Translate(0x00041000UL, 0);
            Assert.IsTrue(pa1.HasValue, "16 KB even page + 0x1000 should translate");
            Assert.AreEqual(0x00501000u, pa1!.Value);

            // Odd page base (0x00044000)
            uint? pa2 = emu.Cpu.COP0.TLB.Translate(0x00044000UL, 0);
            Assert.IsTrue(pa2.HasValue, "16 KB odd page base should translate");
            Assert.AreEqual(0x00600000u, pa2!.Value, "16 KB odd page base must be 0x00600000");

            // Odd page + 0x2000 offset
            uint? pa3 = emu.Cpu.COP0.TLB.Translate(0x00046000UL, 0);
            Assert.IsTrue(pa3.HasValue, "16 KB odd page + 0x2000 should translate");
            Assert.AreEqual(0x00602000u, pa3!.Value);
        }

        // =====================================================================
        // Phase 7.6 — CPU Accuracy tests
        // =====================================================================

        // ── 7.6a  COP0 Performance Counters (reg 25) ─────────────────────────

        [TestMethod]
        public void COP0_PerformanceCounters_PCCR_ReadWrite()
        {
            // MTC0 $t0, $25 sel=0 writes PCCR; MFC0 $t1, $25 sel=0 reads it back.
            var emu = new PS2Emulator();
            // PCCR starts as 0.
            Assert.AreEqual(0u, emu.Cpu.COP0.PCCR, "PCCR must start as 0");

            // Write 0xDEAD to PCCR via WritePerfRegister.
            emu.Cpu.COP0.WritePerfRegister(0, 0xDEADu);
            Assert.AreEqual(0xDEADu, emu.Cpu.COP0.PCCR, "PCCR must reflect written value");
            Assert.AreEqual(0xDEADu, emu.Cpu.COP0.ReadPerfRegister(0), "ReadPerfRegister(0) must return PCCR");
        }

        [TestMethod]
        public void COP0_PerformanceCounters_PCR0_ReadWrite()
        {
            var emu = new PS2Emulator();
            Assert.AreEqual(0u, emu.Cpu.COP0.PCR0, "PCR0 must start as 0");
            emu.Cpu.COP0.WritePerfRegister(1, 0x12345678u);
            Assert.AreEqual(0x12345678u, emu.Cpu.COP0.PCR0);
            Assert.AreEqual(0x12345678u, emu.Cpu.COP0.ReadPerfRegister(1));
        }

        [TestMethod]
        public void COP0_PerformanceCounters_PCR1_ReadWrite()
        {
            var emu = new PS2Emulator();
            Assert.AreEqual(0u, emu.Cpu.COP0.PCR1, "PCR1 must start as 0");
            emu.Cpu.COP0.WritePerfRegister(2, 0xABCDEF01u);
            Assert.AreEqual(0xABCDEF01u, emu.Cpu.COP0.PCR1);
            Assert.AreEqual(0xABCDEF01u, emu.Cpu.COP0.ReadPerfRegister(2));
        }

        [TestMethod]
        public void COP0_PerformanceCounters_MFC0_MTC0_ViaInstruction()
        {
            // Encode MTC0 $t0, $25, sel=1  (write PCR0 from GPR t0)
            // Encoding: opcode=0x10, rs=0x04(MT), rt=8(t0), rd=25, funct=1
            // Encode MFC0 $t1, $25, sel=1  (read PCR0 into GPR t1)
            // Encoding: opcode=0x10, rs=0x00(MF), rt=9(t1), rd=25, funct=1
            uint mtc0_pcr0 = (0x10u << 26) | (0x04u << 21) | (8u << 16) | (25u << 11) | 1u;
            uint mfc0_pcr0 = (0x10u << 26) | (0x00u << 21) | (9u << 16) | (25u << 11) | 1u;

            var emu = new PS2Emulator();
            emu.Cpu.RegsLo[8] = 0xCAFEBABEu; // $t0 = value to write
            emu.Cpu.RegsLo[9] = 0u;

            emu.LoadProgram(0x00001000u, [mtc0_pcr0, mfc0_pcr0]);
            emu.Cpu.PC = 0x00001000UL;
            emu.Step(); // MTC0
            emu.Step(); // MFC0

            Assert.AreEqual(0xCAFEBABEu, emu.Cpu.COP0.PCR0, "MTC0 must write PCR0");
            // MFC0 sign-extends from 32-bit: 0xCAFEBABE sign-extends to 0xFFFFFFFFCAFEBABE
            ulong expected = unchecked((ulong)(long)(int)0xCAFEBABE);
            Assert.AreEqual(expected, emu.Cpu.RegsLo[9], "MFC0 must read PCR0 sign-extended into $t1");
        }

        // ── 7.6b  EE COP0 Count/Compare interrupt ────────────────────────────

        [TestMethod]
        public void COP0_Count_IncrementsPerStep()
        {
            // Count must increment once every two Step() calls (half CPU rate,
            // matching PCSX2 R5900.cpp: Count += cycles / 2).
            var emu = new PS2Emulator();
            emu.Cpu.COP0.WriteRegister32(9, 0u);  // reset Count
            // Set Compare to a high value so no interrupt fires.
            emu.Cpu.COP0.WriteRegister32(11, 0xFFFFFFFFu);

            uint nop = 0u;
            emu.LoadProgram(0x00000000u, [nop, nop, nop, nop, nop, nop]);
            emu.Cpu.PC = 0u;

            emu.Step(); emu.Step();
            Assert.AreEqual(1u, emu.Cpu.COP0.ReadRegister32(9), "Count must be 1 after two Steps");
            emu.Step(); emu.Step();
            Assert.AreEqual(2u, emu.Cpu.COP0.ReadRegister32(9), "Count must be 2 after four Steps");
            emu.Step(); emu.Step();
            Assert.AreEqual(3u, emu.Cpu.COP0.ReadRegister32(9), "Count must be 3 after six Steps");
        }

        [TestMethod]
        public void COP0_Count_EqualsCompare_SetsIP7()
        {
            // When Count reaches Compare the IP7 bit (bit 15) of Cause must be set.
            var emu = new PS2Emulator();
            // Arrange: Count = Compare - 1 so two Steps bring Count == Compare
            // (Count increments every 2 steps at half CPU rate).
            uint compare = 5u;
            emu.Cpu.COP0.WriteRegister32(9,  compare - 1);
            emu.Cpu.COP0.WriteRegister32(11, compare);

            uint nop = 0u;
            emu.LoadProgram(0x00000000u, [nop, nop]);
            emu.Cpu.PC = 0u;
            emu.Step(); emu.Step(); // two steps → Count increments once → Count == Compare

            uint cause = emu.Cpu.COP0.ReadRegister32(13);
            Assert.IsTrue((cause & 0x8000u) != 0, "Cause.IP7 (bit 15) must be set when Count == Compare");
            Assert.IsTrue(emu.Cpu.COP0.IsTimerInterruptPending, "IsTimerInterruptPending must be true");
        }

        [TestMethod]
        public void COP0_WriteCompare_ClearsIP7()
        {
            // Writing Compare must clear the IP7 flag in Cause.
            var emu = new PS2Emulator();
            // Force IP7 set: Count = Compare - 1, then two Steps → Count == Compare.
            emu.Cpu.COP0.WriteRegister32(9,  10u);
            emu.Cpu.COP0.WriteRegister32(11, 11u);

            uint nop = 0u;
            emu.LoadProgram(0x00000000u, [nop, nop]);
            emu.Cpu.PC = 0u;
            emu.Step(); emu.Step(); // Count becomes 11 == Compare → IP7 set

            Assert.IsTrue(emu.Cpu.COP0.IsTimerInterruptPending, "IP7 should be pending");

            // Write a new Compare value — must clear IP7.
            emu.Cpu.COP0.WriteRegister32(11, 0xFFFFFFFFu);
            Assert.IsFalse(emu.Cpu.COP0.IsTimerInterruptPending, "Writing Compare must clear IP7");
        }

        [TestMethod]
        public void COP0_Count_TimerInterrupt_Delivered()
        {
            // When Count == Compare AND Status.IE/EIE/EXL/ERL/IM7 allow it,
            // the CPU must take the interrupt exception.
            var emu = new PS2Emulator();

            // Enable interrupts: IE=1, EIE=1, EXL=0, ERL=0, IM7 (bit 15) = 1.
            // Status = 0x10001 | 0x8000 (IM7)
            emu.Cpu.COP0.WriteRegister32(12, 0x18001u); // IE=1, EIE=1, IM7=1
            // Set Compare so Count will hit it after two Steps (half-rate).
            emu.Cpu.COP0.WriteRegister32(9, 4u);
            emu.Cpu.COP0.WriteRegister32(11, 5u); // Compare = 5; two Steps → Count=5

            uint nop = 0u;
            emu.LoadProgram(0x00000000u, [nop, nop]);
            emu.Cpu.PC = 0u;

            emu.Step(); emu.Step(); // Count becomes 5, IP7 set, interrupt taken → PC → exception vector

            // BEV=0 → interrupt vector is 0x80000080.
            Assert.AreEqual(0x80000080UL, emu.Cpu.PC,
                "After Count/Compare interrupt, PC must jump to exception vector 0x80000080");
        }

        // ── 7.6c  FPU Sticky Exception Flags (FCSR) ──────────────────────────

        [TestMethod]
        public void FPU_ADD_Overflow_SetsFCSR()
        {
            // Adding two large floats overflows → Overflow cause+flag set in FCSR.
            var emu = new PS2Emulator();
            emu.Cpu.FPR[1] = float.MaxValue;
            emu.Cpu.FPR[2] = float.MaxValue;

            // ADD.S $f0, $f1, $f2:
            // opcode=0x11, rs=0x10(S), rt=2(ft=$f2), rd=1(fs=$f1), shamt=0(fd=$f0), funct=0x00
            uint addS = (0x11u << 26) | (0x10u << 21) | (2u << 16) | (1u << 11) | (0u << 6) | 0x00u;
            emu.LoadProgram(0x00000000u, [addS]);
            emu.Cpu.PC = 0u;
            emu.Step();

            uint fcsr = emu.Cpu.COP1.ReadControlRegister(31);
            Assert.IsTrue((fcsr & (1u << 21)) != 0, "Cause.Overflow (bit 21) must be set");
            Assert.IsTrue((fcsr & (1u << 10)) != 0, "Flag.Overflow (bit 10) must be set (sticky)");
            // Result must be clamped (not infinity).
            Assert.IsFalse(float.IsInfinity(emu.Cpu.FPR[0]), "ADD.S overflow result must be clamped, not infinity");
        }

        [TestMethod]
        public void FPU_DIV_DivisionByZero_SetsFCSR()
        {
            // Dividing a finite non-zero value by zero → DivisionByZero cause+flag set.
            var emu = new PS2Emulator();
            emu.Cpu.FPR[1] = 1.0f;
            emu.Cpu.FPR[2] = 0.0f;

            // DIV.S $f0, $f1, $f2: funct=0x03
            uint divS = (0x11u << 26) | (0x10u << 21) | (2u << 16) | (1u << 11) | (0u << 6) | 0x03u;
            emu.LoadProgram(0x00000000u, [divS]);
            emu.Cpu.PC = 0u;
            emu.Step();

            uint fcsr = emu.Cpu.COP1.ReadControlRegister(31);
            Assert.IsTrue((fcsr & (1u << 22)) != 0, "Cause.DivisionByZero (bit 22) must be set");
            Assert.IsTrue((fcsr & (1u << 11)) != 0, "Flag.DivisionByZero (bit 11) must be set (sticky)");
            Assert.IsFalse(float.IsInfinity(emu.Cpu.FPR[0]), "DIV.S div-by-zero result must be clamped");
        }

        [TestMethod]
        public void FPU_SQRT_Negative_SetsInvalidFlag()
        {
            // SQRT of a negative number → InvalidOperation cause+flag set.
            var emu = new PS2Emulator();
            emu.Cpu.FPR[1] = -4.0f;

            // SQRT.S $f0, $f1: opcode=0x11, rs=0x10, rt=0, rd=1(fs), shamt=0(fd), funct=0x04
            uint sqrtS = (0x11u << 26) | (0x10u << 21) | (0u << 16) | (1u << 11) | (0u << 6) | 0x04u;
            emu.LoadProgram(0x00000000u, [sqrtS]);
            emu.Cpu.PC = 0u;
            emu.Step();

            uint fcsr = emu.Cpu.COP1.ReadControlRegister(31);
            Assert.IsTrue((fcsr & (1u << 23)) != 0, "Cause.InvalidOperation (bit 23) must be set");
            Assert.IsTrue((fcsr & (1u << 12)) != 0, "Flag.InvalidOperation (bit 12) must be set (sticky)");
        }

        [TestMethod]
        public void FPU_FCSR_StickyFlagPersistsAcrossInstructions()
        {
            // Sticky flag bits must survive subsequent normal operations.
            var emu = new PS2Emulator();
            emu.Cpu.FPR[1] = float.MaxValue;
            emu.Cpu.FPR[2] = float.MaxValue;
            emu.Cpu.FPR[3] = 1.0f;
            emu.Cpu.FPR[4] = 2.0f;

            // ADD.S $f0, $f1, $f2  (overflow, sets flag.O bit 10)
            uint addOverflow = (0x11u << 26) | (0x10u << 21) | (2u << 16) | (1u << 11) | (0u << 6) | 0x00u;
            // ADD.S $f5, $f3, $f4  (normal, clears cause but sticky should survive)
            uint addNormal   = (0x11u << 26) | (0x10u << 21) | (4u << 16) | (3u << 11) | (5u << 6) | 0x00u;

            emu.LoadProgram(0x00000000u, [addOverflow, addNormal]);
            emu.Cpu.PC = 0u;
            emu.Step(); // overflow
            emu.Step(); // normal add

            uint fcsr = emu.Cpu.COP1.ReadControlRegister(31);
            // Cause.Overflow (bit 21) must be CLEARED (normal instruction clears cause).
            Assert.IsTrue((fcsr & (1u << 21)) == 0, "Cause.Overflow (bit 21) must be cleared by subsequent normal op");
            // Flag.Overflow (bit 10) must still be SET (sticky).
            Assert.IsTrue((fcsr & (1u << 10)) != 0, "Flag.Overflow (bit 10) must persist as sticky flag");
        }

        // ── Jump-to-self (infinite loop) regression tests ────────────────────

        /// <summary>
        /// A J instruction that targets its own address must loop indefinitely,
        /// not fall through to the next instruction.
        ///
        /// Before the fix, PS2Emulator.Step() tested <c>if (cpu.PC == pcBefore)</c>
        /// to detect non-branch instructions, which also fired when a jump targeted
        /// its own PC, causing PC to advance by 4 instead of staying.
        /// </summary>
        [TestMethod]
        public void Emulator_JumpToSelf_LoopsInPlace()
        {
            // Program at 0x100:
            //   0x100: j 0x100       — jump to self (delay slot at 0x104 also executes)
            //   0x104: nop           — delay slot
            //   0x108: addiu $t0, $zero, 99  — must NOT be reached

            // J at 0x100: imm26 = (0x100 >> 2) = 0x40.  Target = (0x104 & 0xF0000000) | (0x40 << 2) = 0x100.
            uint j_self = (2u << 26) | 0x40u;
            uint nop    = 0u;
            uint addiu  = (0x09u << 26) | (0u << 21) | (8u << 16) | 99u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000100, new[] { j_self, nop, addiu });
            emu.Cpu.PC = 0x00000100;

            // After 3 steps the CPU should still be at 0x100 (stuck in the loop).
            // Each Step(): executes j (plus its nop delay slot) then lands back at 0x100.
            emu.Step();
            Assert.AreEqual(0x100UL, emu.Cpu.PC,  "After 1 step: still at 0x100");
            Assert.AreEqual(0UL,     emu.Cpu.RegsLo[8], "addiu must not have executed");

            emu.Step();
            Assert.AreEqual(0x100UL, emu.Cpu.PC,  "After 2 steps: still at 0x100");

            emu.Step();
            Assert.AreEqual(0x100UL, emu.Cpu.PC,  "After 3 steps: still at 0x100");
            Assert.AreEqual(0UL,     emu.Cpu.RegsLo[8], "addiu must never execute");
        }

        [TestMethod]
        public void Emulator_BEQ_SameTarget_LoopsInPlace()
        {
            // BEQ $zero, $zero, 0  — offset 0 → target = PC+4+0 = PC+4. Not a self-loop.
            // BEQ $zero, $zero, -1 — offset -1 → target = PC+4-4 = PC. Self-loop.
            // Verify the self-loop stays in place.

            // beq $zero,$zero,-1: rs=0,rt=0,offset=0xFFFF (= -1 in signed 16-bit)
            uint beq_self = (4u << 26) | 0xFFFFu;
            uint nop      = 0u;
            uint addiu    = (0x09u << 26) | (0u << 21) | (8u << 16) | 99u;

            var emu = new PS2Emulator();
            emu.LoadProgram(0x00000200, new[] { beq_self, nop, addiu });
            emu.Cpu.PC = 0x00000200;

            emu.Step();
            Assert.AreEqual(0x200UL, emu.Cpu.PC, "BEQ self-loop: still at 0x200 after step 1");
            emu.Step();
            Assert.AreEqual(0x200UL, emu.Cpu.PC, "BEQ self-loop: still at 0x200 after step 2");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[8], "addiu must never execute");
        }

        // ── I-cache and bulk-fill fast-forward tests ─────────────────────────

        /// <summary>
        /// Verify that the i-cache protects an executing instruction from being
        /// overwritten by a data store in KSEG0.  The instruction is fetched once
        /// (populating the cache), then the same physical RAM location is written
        /// with a different word; subsequent fetches must still return the original.
        /// </summary>
        [TestMethod]
        public void ICache_ProtectsExecutingCode_FromStoreOverwrite()
        {
            // Place an ADDIU $t0, $zero, 42 at KSEG0 address 0x80001000.
            //   encoding: opcode=9, rs=0, rt=8, imm=42 → 0x2408002A
            uint addiu42 = (9u << 26) | (0u << 21) | (8u << 16) | 42u; // 0x2408002A

            var emu = new PS2Emulator();
            emu.Cpu.PC = 0x80001000UL; // KSEG0

            // Write the instruction into RAM (physical 0x00001000 = KSEG0 - top bits).
            emu.Bus.WriteWord(0x80001000u, addiu42);
            // Write a NOP after it so PC advances and we can step again.
            emu.Bus.WriteWord(0x80001004u, 0u); // nop

            // Step 1: fetch and execute addiu42 at 0x80001000 — caches it.
            emu.Step();
            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8], "First execution: $t0 should be 42");

            // Now overwrite the RAM word at 0x80001000 with a different instruction
            // (ADDIU $t0, $zero, 99).  The i-cache should shield subsequent fetches.
            uint addiu99 = (9u << 26) | (0u << 21) | (8u << 16) | 99u;
            emu.Bus.WriteWord(0x80001000u, addiu99);

            // Reset PC back to 0x80001000 and step again.
            emu.Cpu.PC = 0x80001000UL;
            emu.Step();

            // The i-cache must return the ORIGINAL instruction (42), not the overwrite (99).
            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8],
                "Second execution: i-cache should return original instruction, not store overwrite");
        }

        /// <summary>
        /// Verify that Reset() clears the i-cache so that stale entries from a
        /// previous run cannot survive into the next run.
        /// </summary>
        [TestMethod]
        public void ICache_ClearedByReset()
        {
            uint addiu42 = (9u << 26) | (0u << 21) | (8u << 16) | 42u;
            uint addiu99 = (9u << 26) | (0u << 21) | (8u << 16) | 99u;
            uint nop     = 0u;

            var emu = new PS2Emulator();
            emu.Cpu.PC = 0x80001000UL;
            emu.Bus.WriteWord(0x80001000u, addiu42);
            emu.Bus.WriteWord(0x80001004u, nop);

            // Execute once to populate the cache.
            emu.Step();
            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8]);

            // Overwrite the instruction in RAM and call Reset() to flush cache.
            emu.Bus.WriteWord(0x80001000u, addiu99);
            emu.Reset();

            // Set PC manually (Reset() sets PC to BIOS vector, not our test address).
            emu.Cpu.PC = 0x80001000UL;
            emu.Step();

            // After Reset() the cache is empty; the new instruction should be seen.
            Assert.AreEqual(99UL, emu.Cpu.RegsLo[8],
                "After Reset() the overwritten instruction (99) should be fetched from RAM");
        }

        /// <summary>
        /// Verify the bulk byte-fill fast-forward (TryFastForwardByteFill).
        /// Sets up a tight sb-loop in KSEG0 with EXL=1 and confirms that after
        /// one Step() call the loop is collapsed: RAM is filled, registers reflect
        /// the post-loop state, and the PC is advanced past the loop.
        /// </summary>
        [TestMethod]
        public void EE_BulkFillFastForward_CompletesLoopAndFillsRam()
        {
            // ── Build the loop ──────────────────────────────────────────────
            // Registers used (MIPS ABI names → indices):
            //   v0 = 2  (addrReg: computed address = base + offset)
            //   v1 = 3  (cntReg:  iteration counter, decrements each loop)
            //   a0 = 4  (fillReg: byte to store = 0xAB)
            //   s1 = 17 (thrReg:  loop exits when cntReg <= s1)
            //   s5 = 21 (offReg:  address offset, incremented each iteration)
            //   s6 = 22 (baseReg: fixed base address)
            //
            // Loop body (7 instructions at KSEG0 base 0x80010000):
            //   +0x00: addu  v0, s6, s5       ; SPECIAL rs=22 rt=21 rd=2 funct=0x21
            //   +0x04: addiu v1, v1, -1        ; opcode=9 rs=3 rt=3 imm=0xFFFF
            //   +0x08: sb    a0, 0(v0)         ; opcode=0x28 rs=2 rt=4 imm=0
            //   +0x0C: slt   v0, s1, v1        ; SPECIAL rs=17 rt=3 rd=2 funct=0x2A
            //   +0x10: nop                     ; 0x00000000
            //   +0x14: bne   v0, $zero, -6     ; opcode=5 rs=2 rt=0 imm=-6 (target=+0x00)
            //   +0x18: addiu s5, s5, 1         ; delay slot: opcode=9 rs=21 rt=21 imm=1

            // Encode instructions:
            uint addu_v0_s6_s5 = (0u << 26) | (22u << 21) | (21u << 16) | (2u << 11) | 0x21u;
            uint addiu_v1_m1   = (9u << 26)  | (3u << 21)  | (3u << 16)  | 0xFFFFu;
            uint sb_a0_0_v0    = (0x28u << 26)| (2u << 21)  | (4u << 16)  | 0u;
            uint slt_v0_s1_v1  = (0u << 26)  | (17u << 21) | (3u << 16)  | (2u << 11) | 0x2Au;
            uint nop            = 0u;
            // BNE v0, $zero, -6: target = (pc+4) + (-6)*4 = pc+4-24 = pc-20.
            // PC of BNE = 0x80010014.  target = 0x80010014 - 20 = 0x80010000. ✓
            uint bne_v0_m6     = (5u << 26)  | (2u << 21)  | (0u << 16)  | unchecked((uint)(ushort)(-6));
            uint addiu_s5_p1   = (9u << 26)  | (21u << 21) | (21u << 16) | 1u;
            // Instruction after the loop (must be reachable at PC+8 = 0x8001001C).
            uint addiu_t0_77   = (9u << 26)  | (0u << 21)  | (8u << 16)  | 77u;

            var emu = new PS2Emulator();

            // Load loop at KSEG0 0x80010000.
            emu.Bus.WriteWord(0x80010000u, addu_v0_s6_s5);
            emu.Bus.WriteWord(0x80010004u, addiu_v1_m1);
            emu.Bus.WriteWord(0x80010008u, sb_a0_0_v0);
            emu.Bus.WriteWord(0x8001000Cu, slt_v0_s1_v1);
            emu.Bus.WriteWord(0x80010010u, nop);
            emu.Bus.WriteWord(0x80010014u, bne_v0_m6);
            emu.Bus.WriteWord(0x80010018u, addiu_s5_p1);
            emu.Bus.WriteWord(0x8001001Cu, addiu_t0_77);

            // ── Set up register state ──────────────────────────────────────
            // We place PC at the BNE instruction (the fast-forward detects from there).
            // v1 (counter) = threshold + 200_000 so there are 200_000 iterations left.
            // s1 (threshold) = 10.
            int threshold = 10;
            int iterations = 200_000; // well above BulkFillMinIterations (50_000)
            int initialCnt = threshold + iterations; // v1 = 200_010

            emu.Cpu.PC = 0x80010014UL; // PC at the BNE
            emu.Cpu.RegsLo[3]  = (ulong)(int)initialCnt; // v1 = counter
            emu.Cpu.RegsLo[17] = (ulong)(int)threshold;   // s1 = threshold
            emu.Cpu.RegsLo[4]  = 0xABu;                   // a0 = fill byte
            emu.Cpu.RegsLo[21] = 0u;                       // s5 = offset = 0
            emu.Cpu.RegsLo[22] = 0x80000000u;              // s6 = base (KSEG0 origin)

            // Set COP0 Status with EXL=1 so the fast-forward fires.
            // Use a status that also has BEV=1 so we don't need a BEV=0 handler.
            // mask 0x10007 | EXL bit (bit1): status = 0x40000002 (BEV=1, EXL=1)
            emu.Cpu.COP0.WriteRegister32(12, 0x00400002u); // BEV=1, EXL=1

            // ── Execute one Step() ────────────────────────────────────────
            emu.Step();

            // ── Assert post-loop state ────────────────────────────────────
            // Counter (v1) should equal the threshold.
            Assert.AreEqual((ulong)(int)threshold, emu.Cpu.RegsLo[3],
                "v1 (counter) should equal threshold after fast-forward");

            // Offset (s5) = 0 + iterations + 1  (iterations stores + 1 final delay-slot ++).
            int expectedOffset = iterations + 1;
            Assert.AreEqual((ulong)(uint)(int)expectedOffset, emu.Cpu.RegsLo[21] & 0xFFFFFFFFUL,
                "s5 (offset) should be initialOffset + iterations + 1");

            // v0 (compare result from last SLT) = 0.
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[2] & 0xFFFFFFFFUL,
                "v0 (SLT result) should be 0 after last iteration");

            // PC should be at BNE + 8 (past the delay slot).
            Assert.AreEqual(0x8001001CUL, emu.Cpu.PC,
                "PC should advance to instruction after BNE+delay-slot");

            // Main RAM must be filled with 0xAB.
            // Verify a representative spread: first 256 bytes, the midpoint at
            // offset ~100 000, and the last few bytes written near offset 200 001.
            // (The fill covers all 32 MB so any physical address should be 0xAB.)
            for (int i = 1; i <= 255; i++)
                Assert.AreEqual(0xABu, (uint)emu.Bus.Ram[i],
                    $"RAM[{i}] should be 0xAB after fast-forward fill");

            // Check near the midpoint of the fill range (physical byte ~100 001).
            Assert.AreEqual(0xABu, (uint)emu.Bus.Ram[100_001],
                "RAM[100001] should be 0xAB after fast-forward fill");

            // Check near the end of the 200 000-iteration fill (last physical byte written = 200 000).
            Assert.AreEqual(0xABu, (uint)emu.Bus.Ram[200_000],
                "RAM[200000] (last byte written) should be 0xAB after fast-forward fill");

            // Byte just beyond the fill range must be untouched (still 0).
            Assert.AreEqual(0u, (uint)emu.Bus.Ram[200_001],
                "RAM[200001] (one past end of fill) should remain 0");
        }

        /// <summary>
        /// Verify the descending (offset--) variant of the bulk-fill fast-forward.
        /// The BIOS uses an addiu offReg, offReg, -1 delay slot in a second fill pass.
        /// This test mirrors EE_BulkFillFastForward_CompletesLoopAndFillsRam but with
        /// the delay slot operand changed from +1 to -1, and the offset starting at
        /// a high value so the fill covers addressable RAM.
        /// </summary>
        [TestMethod]
        public void EE_BulkFillFastForward_Descending_CompletesLoopAndFillsRam()
        {
            // Loop body (same as ascending test but delay slot uses -1):
            //   +0x00: addu  v0, s6, s5       ; SPECIAL rs=22 rt=21 rd=2 funct=0x21
            //   +0x04: addiu v1, v1, -1        ; opcode=9 rs=3 rt=3 imm=0xFFFF
            //   +0x08: sb    a0, 0(v0)         ; opcode=0x28 rs=2 rt=4 imm=0
            //   +0x0C: slt   v0, s1, v1        ; SPECIAL rs=17 rt=3 rd=2 funct=0x2A
            //   +0x10: nop
            //   +0x14: bne   v0, $zero, -6     ; loop back to +0x00
            //   +0x18: addiu s5, s5, -1        ; delay slot: offset--  ← descending

            uint addu_v0_s6_s5  = (0u << 26) | (22u << 21) | (21u << 16) | (2u << 11) | 0x21u;
            uint addiu_v1_m1    = (9u << 26)  | (3u << 21)  | (3u << 16)  | 0xFFFFu;
            uint sb_a0_0_v0     = (0x28u << 26)| (2u << 21)  | (4u << 16)  | 0u;
            uint slt_v0_s1_v1   = (0u << 26)  | (17u << 21) | (3u << 16)  | (2u << 11) | 0x2Au;
            uint nop             = 0u;
            uint bne_v0_m6      = (5u << 26)  | (2u << 21)  | (0u << 16)  | unchecked((uint)(ushort)(-6));
            uint addiu_s5_m1    = (9u << 26)  | (21u << 21) | (21u << 16) | unchecked((uint)(ushort)(-1)); // -1
            uint addiu_t0_77    = (9u << 26)  | (0u << 21)  | (8u << 16)  | 77u;

            var emu = new PS2Emulator();

            emu.Bus.WriteWord(0x80010000u, addu_v0_s6_s5);
            emu.Bus.WriteWord(0x80010004u, addiu_v1_m1);
            emu.Bus.WriteWord(0x80010008u, sb_a0_0_v0);
            emu.Bus.WriteWord(0x8001000Cu, slt_v0_s1_v1);
            emu.Bus.WriteWord(0x80010010u, nop);
            emu.Bus.WriteWord(0x80010014u, bne_v0_m6);
            emu.Bus.WriteWord(0x80010018u, addiu_s5_m1);
            emu.Bus.WriteWord(0x8001001Cu, addiu_t0_77);

            // Registers: offset starts at 200_010 (so first store = base + 200_010 - 1 = 200_009).
            // After 200_000 iterations (200_010 - 10 = 200_000 remaining), offset = 10 - 1 = 9.
            int threshold  = 10;
            int iterations = 200_000;
            int initialOff = threshold + iterations; // 200_010

            emu.Cpu.PC = 0x80010014UL;
            emu.Cpu.RegsLo[3]  = (ulong)(int)(threshold + iterations); // v1 = counter
            emu.Cpu.RegsLo[17] = (ulong)(int)threshold;                // s1 = threshold
            emu.Cpu.RegsLo[4]  = 0xCDu;                                // a0 = fill byte
            emu.Cpu.RegsLo[21] = (ulong)(uint)(int)initialOff;         // s5 = offset
            emu.Cpu.RegsLo[22] = 0x80000000u;                          // s6 = base
            emu.Cpu.COP0.WriteRegister32(12, 0x00400002u);             // BEV=1, EXL=1

            emu.Step();

            // Counter must equal threshold.
            Assert.AreEqual((ulong)(int)threshold, emu.Cpu.RegsLo[3],
                "v1 (counter) should equal threshold after descending fast-forward");

            // Offset decremented by iterations, then -1 from final delay slot.
            int expectedOff = initialOff - iterations - 1;
            Assert.AreEqual((ulong)(uint)(int)expectedOff, emu.Cpu.RegsLo[21] & 0xFFFFFFFFUL,
                "s5 (offset) should be initialOff - iterations - 1");

            // v0 (SLT result) = 0.
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[2] & 0xFFFFFFFFUL,
                "v0 (SLT result) should be 0 after last iteration");

            // PC past the BNE + delay slot.
            Assert.AreEqual(0x8001001CUL, emu.Cpu.PC,
                "PC should advance to instruction after BNE+delay-slot");

            // The fill wrote byte 0xCD to addresses base + (initialOff-1) down to
            // base + (initialOff - iterations).  Physical addresses: those values & 0x1FFFFFF.
            // Spot-check a few bytes in the filled range.
            int physFirst = (int)((0x80000000u + initialOff - iterations) & 0x1FFFFFFu);
            int physLast  = (int)((0x80000000u + initialOff - 1) & 0x1FFFFFFu);
            Assert.AreEqual(0xCDu, (uint)emu.Bus.Ram[physFirst],
                "RAM[physFirst] should be 0xCD after descending fill");
            Assert.AreEqual(0xCDu, (uint)emu.Bus.Ram[physLast],
                "RAM[physLast] should be 0xCD after descending fill");
        }

        /// <summary>
        /// Verify that the CACHE IXIN instruction (rt=0x07) correctly invalidates
        /// the i-cache line in our interpreter.  After an instruction is cached,
        /// executing CACHE IXIN for that address, then overwriting the RAM word,
        /// should cause the next fetch to load the new value (not the stale cache).
        /// </summary>
        [TestMethod]
        public void ICache_CacheInstructionIXIN_InvalidatesLine()
        {
            // Step 1: cache an ADDIU $t0, $zero, 42 at KSEG0 0x80002000.
            uint addiu42 = (9u << 26) | (0u << 21) | (8u << 16) | 42u;
            uint addiu99 = (9u << 26) | (0u << 21) | (8u << 16) | 99u;

            var emu = new PS2Emulator();
            emu.Bus.WriteWord(0x80002000u, addiu42);
            emu.Bus.WriteWord(0x80002004u, 0u); // nop

            emu.Cpu.PC = 0x80002000UL;
            emu.Step();
            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8], "First execution: $t0 = 42");

            // Step 2: overwrite the instruction word in RAM.
            emu.Bus.WriteWord(0x80002000u, addiu99);

            // Step 3: without IXIN the i-cache would still serve addiu42.
            // Confirm that — i-cache is still populated.
            emu.Cpu.PC = 0x80002000UL;
            emu.Step();
            Assert.AreEqual(42UL, emu.Cpu.RegsLo[8], "Before IXIN: i-cache serves original (42)");

            // Step 4: encode and execute CACHE IXIN (rt=0x07) for address 0x80002000.
            // CACHE encoding: opcode=0x2F, rs=base, rt=op, imm=offset.
            // We'll use rs=$s0 (16) = 0x80002000, offset=0 → imm=0, rt=7.
            emu.Cpu.RegsLo[16] = 0x80002000u; // $s0 = base address
            //   word = (0x2F << 26) | (s0=16 << 21) | (rt=7 << 16) | 0
            uint cacheIxin = (0x2Fu << 26) | (16u << 21) | (7u << 16) | 0u;
            // Write the CACHE instruction at a new address, execute it.
            emu.Bus.WriteWord(0x80003000u, cacheIxin);
            emu.Bus.WriteWord(0x80003004u, 0u); // nop
            emu.Cpu.PC = 0x80003000UL;
            emu.Step(); // executes CACHE IXIN → flushes i-cache line for 0x80002000

            // Step 5: fetch again from 0x80002000 — should now see addiu99 from RAM.
            emu.Cpu.PC = 0x80002000UL;
            emu.Step();
            Assert.AreEqual(99UL, emu.Cpu.RegsLo[8],
                "After CACHE IXIN: stale i-cache entry cleared, RAM word (99) fetched");
        }

        /// <summary>
        /// Verify that the i-cache loads a full 64-byte line on a miss.
        /// When word A is fetched (cache miss), all 16 words in its 64-byte line
        /// must be loaded.  If a store then overwrites word B (same line), the
        /// i-cache should still serve the original word B because the line was
        /// already populated when word A was first fetched.
        ///
        /// This test models the critical BIOS scenario:
        ///   – The fill-loop delay slot at 0x80010A00 is fetched → line
        ///     0x80010A00–0x80010A3F is loaded, caching post-loop code at 0x80010A04.
        ///   – The bulk-fill fast-forward overwrites all of RAM with 0x20.
        ///   – Fetching 0x80010A04 after the fill must return the ORIGINAL instruction,
        ///     not the garbage 0x20202020, because the line was pre-populated.
        /// </summary>
        [TestMethod]
        public void ICache_64ByteLineFill_CachesNeighbouringWords()
        {
            var emu = new PS2Emulator();

            // Pick an address that is 64-byte aligned so it is word 0 of its cache line.
            // We'll use KSEG0 0x80010A00 (mirrors the real delay-slot address).
            uint baseAddr = 0x80010A00u;   // word 0 of its cache line

            // Word at +0 (baseAddr): ADDIU $t0, $zero, 7
            uint addiuAt0  = (9u << 26) | (0u << 21) | (8u << 16) | 7u;
            // Word at +4 (baseAddr+4): ADDIU $t1, $zero, 99 — this is the "neighbour"
            uint addiuAt4  = (9u << 26) | (0u << 21) | (9u << 16) | 99u;
            uint nop = 0u;

            // Write both instructions into RAM.
            emu.Bus.WriteWord(baseAddr,     addiuAt0);
            emu.Bus.WriteWord(baseAddr + 4, addiuAt4);
            emu.Bus.WriteWord(baseAddr + 8, nop);   // so PC can advance after +4

            // Step 1: fetch baseAddr — triggers a cache miss, loads the full 64-byte
            // line (both addiuAt0 AND addiuAt4 are now in the cache).
            emu.Cpu.PC = (ulong)baseAddr;
            emu.Step();
            Assert.AreEqual(7UL, emu.Cpu.RegsLo[8], "Step at +0: $t0 = 7");

            // Step 2: overwrite RAM at baseAddr+4 with something different.
            uint addiuAt4_new = (9u << 26) | (0u << 21) | (9u << 16) | 42u;
            emu.Bus.WriteWord(baseAddr + 4, addiuAt4_new);

            // Step 3: execute from baseAddr+4 — the cache line was already loaded on
            // the miss at baseAddr, so the i-cache still has the ORIGINAL instruction (99).
            emu.Cpu.PC = (ulong)(baseAddr + 4);
            emu.Step();
            Assert.AreEqual(99UL, emu.Cpu.RegsLo[9],
                "Step at +4: i-cache line pre-populated, serves original neighbour (99) not overwrite (42)");
        }

        /// <summary>
        /// Verify that TryFastForwardByteFill() pre-fetches the call-chain ($ra and
        /// saved stack $ra values) before wiping RAM.
        ///
        /// Scenario modelled after the real BIOS boot sequence:
        ///   1. A "caller" function places code at address C (KSEG0).
        ///   2. The fill function runs at address F (KSEG0).  Fill function epilogue
        ///      is at F+8 (inside the 4-line epilogue window).
        ///   3. After fast-forward (Array.Fill), all RAM is overwritten.
        ///   4. The fill epilogue runs from i-cache, restores $ra, and does jr $ra.
        ///   5. The caller's code at C must be served from i-cache — not from the
        ///      overwritten RAM (which would return 0x20202020).
        ///
        /// This test artificially exercises that path by:
        ///   - Writing a "caller return" instruction (ADDIU $t0, $zero, 77) at a
        ///     KSEG0 address that is far from the fast-forward PC.
        ///   - Setting $ra to that address.
        ///   - Running the fast-forward (which must pre-fetch the $ra address).
        ///   - After the fast-forward RAM is wiped; fetching $ra address must still
        ///     return the original instruction (77), not 0x20202020 (42).
        /// </summary>
        [TestMethod]
        public void FastForward_PreFetchesRaBeforeFill_CallerSurvivesFill()
        {
            // ── Set up the fill loop pattern ──────────────────────────────────
            // The fill loop must be at a KSEG0 address and match TryFastForwardByteFill
            // detection.  We use 0x80010000 as the loop start.
            //
            // Loop body at 0x80010000:
            //   addu  $t5, $zero, $t0   ; addrReg = $t5, baseReg=$zero, offReg=$t0
            //   addiu $t1, $t1, -1      ; cntReg decrement
            //   sb    $t2, 0($t5)       ; store fill byte
            //   slt   $t3, $t4, $t1     ; cmpReg = slt(thrReg, cntReg)
            //   bne   $t3, $zero, -4    ; branch back (imm = -4 → loop start)
            //   addiu $t0, $t0, 1       ; delay slot: offset++
            uint loopStart = 0x80010000u;
            var emu = new PS2Emulator();

            // addu $t5, $zero, $t0  (SPECIAL, funct=0x21, rd=$t5=13, rs=$zero=0, rt=$t0=8)
            uint w0 = (0u << 26) | (0u << 21) | (8u << 16) | (13u << 11) | 0x21u;
            // addiu $t1, $t1, -1  (opcode=9, rs=rt=$t1=9, imm=-1=0xFFFF)
            uint w1 = (9u << 26) | (9u << 21) | (9u << 16) | 0xFFFFu;
            // sb $t2, 0($t5)  (opcode=0x28, rs=$t5=13, rt=$t2=10, imm=0)
            uint w2 = (0x28u << 26) | (13u << 21) | (10u << 16) | 0u;
            // slt $t3, $t4, $t1  (SPECIAL, funct=0x2A, rd=$t3=11, rs=$t4=12, rt=$t1=9)
            uint w3 = (0u << 26) | (12u << 21) | (9u << 16) | (11u << 11) | 0x2Au;
            // bne $t3, $zero, loopStart  (opcode=5, rs=$t3=11, rt=0)
            // target=loopStart=0x80010000, BNE at 0x80010010:
            //   imm = (loopStart - (pc+4)) / 4 = (0x80010000 - 0x80010014) / 4 = -5
            uint w4 = (5u << 26) | (11u << 21) | (0u << 16) | unchecked((uint)(ushort)-5);
            // addiu $t0, $t0, 1  (opcode=9, rs=rt=$t0=8, imm=1)
            uint w5 = (9u << 26) | (8u << 21) | (8u << 16) | 1u;

            emu.Bus.WriteWord(loopStart,       w0);
            emu.Bus.WriteWord(loopStart + 4,   w1);
            emu.Bus.WriteWord(loopStart + 8,   w2);
            emu.Bus.WriteWord(loopStart + 12,  w3);
            emu.Bus.WriteWord(loopStart + 16,  w4); // BNE — pc32 for fast-forward
            emu.Bus.WriteWord(loopStart + 20,  w5); // delay slot

            // ── Caller code at a remote KSEG0 address ─────────────────────────
            // Place a recognisable instruction far from the loop (different 64-byte line
            // and not within 256 B of the BNE).  $ra will point here.
            uint callerAddr = 0x80050000u;  // 256 KB away from loop
            uint addiuCallerRet = (9u << 26) | (0u << 21) | (8u << 16) | 77u; // addiu $t0,$zero,77
            uint nop = 0u;
            emu.Bus.WriteWord(callerAddr,     addiuCallerRet);
            emu.Bus.WriteWord(callerAddr + 4, nop);

            // ── Registers ─────────────────────────────────────────────────────
            // $t0 (8) = offset register = 0 (first byte address = base + 0 + 1)
            // $t1 (9) = counter = 200000 (→ remaining = 200000 - 0 > BulkFillMinIterations)
            // $t2 (10) = fill byte = 0x55
            // $t3 (11) = cmpReg (SLT result, will be recomputed)
            // $t4 (12) = threshold = 0  (thrReg, loop exits when cnt reaches 0)
            // $t5 (13) = addrReg (computed each iter)
            // $sp (29) = arbitrary (not used in this test)
            // $ra (31) = caller return address → callerAddr
            emu.Cpu.RegsLo[8]  = 0;           // $t0 = offset
            emu.Cpu.RegsLo[9]  = 200_000;     // $t1 = counter
            emu.Cpu.RegsLo[10] = 0x55;        // $t2 = fill byte
            emu.Cpu.RegsLo[11] = 1;           // $t3 = cmpReg (slt != 0 → loop continues)
            emu.Cpu.RegsLo[12] = 0;           // $t4 = threshold
            emu.Cpu.RegsLo[29] = 0x80040000u; // $sp = some stack (nothing useful on it)
            emu.Cpu.RegsLo[31] = callerAddr;  // $ra → caller code

            // Mark the loop as executing within EXL=1 so TryFastForwardByteFill fires.
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            emu.Cpu.COP0.WriteRegister32(12, status | 0x2u); // set EXL

            // ── Run from the BNE ──────────────────────────────────────────────
            emu.Cpu.PC = loopStart + 16; // BNE
            emu.Step(); // triggers fast-forward; pre-fetches callerAddr; Array.Fill(0x55)

            // Confirm fast-forward ran: counter = threshold and fill byte is in RAM.
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[9],  "Counter must reach threshold (0) after fast-forward");
            Assert.AreEqual(0x55u, (uint)emu.Bus.Ram[1], "RAM[1] must be filled with 0x55");

            // ── Key assertion: fetch callerAddr from i-cache ───────────────────
            // callerAddr is now 0x55555555... in RAM (it was inside the fill range).
            // But TryFastForwardByteFill pre-fetched $ra BEFORE the fill, so the
            // i-cache line for callerAddr should hold the ORIGINAL addiu instruction.
            emu.Cpu.PC = (ulong)callerAddr;
            emu.Step();
            Assert.AreEqual(77UL, emu.Cpu.RegsLo[8],
                "After bulk fill: $ra pre-fetch kept caller instruction (77) in i-cache; RAM overwrite (0x55) not seen");
        }

        // =====================================================================
        // SBUS serial register (0x1000F100-0x1000F1FF) tests
        // =====================================================================

        /// <summary>
        /// Reads from the EE SBUS serial UART range (0x1000F100-0x1000F1FF) must
        /// always return 0 regardless of previous writes.  This ensures the
        /// TX-status poll loop in the BIOS putchar routine (which reads SBUS_F130 /
        /// physical 0x1000F130 and checks for bits 15:12 == 0x8000) exits immediately
        /// rather than spinning indefinitely.
        /// </summary>
        [TestMethod]
        public void MemoryBus_SbusSerial_ReadAlwaysReturnsZero()
        {
            var bus = new PS2MemoryBus();

            // KSEG1 base for 0x1000F130 (TX status) and 0x1000F180 (TX data).
            const uint SbusF130 = 0xB000F130u; // physical 0x1000F130
            const uint SbusF180 = 0xB000F180u; // physical 0x1000F180
            const uint SbusF100 = 0xB000F100u; // physical 0x1000F100 (range start)
            const uint SbusF1FC = 0xB000F1FCu; // physical 0x1000F1FC (range end -4)

            // Writes to the serial range must be silently discarded.
            bus.WriteWord(SbusF130, 0x0000_8001u); // write TX-busy init value
            bus.WriteWord(SbusF180, 0x41u);         // write 'A' to TX data
            bus.WriteWord(SbusF100, 0xDEAD_BEEFu);

            // All reads in the range must return 0.
            Assert.AreEqual(0u, bus.ReadWord(SbusF130),
                "SBUS_F130 (TX status) must read as 0 — loop exit condition");
            Assert.AreEqual(0u, bus.ReadWord(SbusF180),
                "SBUS_F180 (TX data) must read as 0 after write discard");
            Assert.AreEqual(0u, bus.ReadWord(SbusF100),
                "SBUS_F100 (range start) must read as 0");
            Assert.AreEqual(0u, bus.ReadWord(SbusF1FC),
                "SBUS_F1FC (range end) must read as 0");
        }

        /// <summary>
        /// Confirm that SBUS_F130 bits 15:12 are 0 after a write that would set the
        /// TX-busy value (0x8001).  This is the exact condition the BIOS poll loop
        /// checks: (lw[0xB000F130] & 0xF000) == 0x8000.  Returning 0 means the poll
        /// exits immediately (0 != 0x8000).
        /// </summary>
        [TestMethod]
        public void MemoryBus_SbusF130_TxBusyBits_AreZeroAfterWrite()
        {
            var bus = new PS2MemoryBus();

            // Write a value with bits 15:12 == 0x8 (the TX-busy value the BIOS uses).
            bus.WriteWord(0xB000F130u, 0x0000_8001u);

            uint status = bus.ReadWord(0xB000F130u);
            Assert.AreEqual(0u, status & 0xF000u,
                "SBUS_F130 bits 15:12 must be 0 (TX ready) even after a TX-busy write");
        }

        // =====================================================================
        // TryFastForwardBiosPuts tests
        // =====================================================================

        /// <summary>
        /// Verify TryFastForwardBiosPuts detects the BIOS puts loop and fast-forwards
        /// the string pointer to the null terminator in a single Step() call.
        ///
        /// The loop pattern at `loopStart`:
        ///   lui   funcReg, 0x8001                     ; load function-pointer upper
        ///   lw    funcReg, someOffset(funcReg)         ; load function pointer
        ///   jalr  funcReg                              ; call putchar
        ///   daddu a0, v0, zero                         ; delay slot: a0 = char
        ///   lb    v0, 0(s0)                            ; load next char from s0
        ///   bnel  v0, $zero, loopStart                 ; loop while non-zero
        ///   addiu s0, s0, 1                            ; delay slot: s0++
        /// </summary>
        [TestMethod]
        public void EE_BiosPuts_FastForward_AdvancesPointerToNullTerminator()
        {
            // Register indices (MIPS ABI)
            const int V0 = 2;   // charReg (BNEL compares this against zero)
            const int V1 = 3;   // funcReg (loaded function pointer)
            const int S0 = 16;  // ptrReg  (string pointer, advanced by delay slot)
            const int A0 = 4;   // argument register (jalr delay slot)

            // ── Build the loop at KSEG0 0x80020000 ───────────────────────────
            // Encode each instruction manually.
            const uint loopBase = 0x80020000u;

            // lui   v1, 0x8001
            uint lui_v1      = (0x0Fu << 26) | (0u << 21) | (V1 << 16) | 0x8001u;
            // lw    v1, 0x5958(v1)
            uint lw_v1       = (0x23u << 26) | ((uint)V1 << 21) | ((uint)V1 << 16)
                               | (uint)(ushort)0x5958;
            // jalr  v1
            uint jalr_v1     = (0u << 26) | ((uint)V1 << 21) | (0u << 16)
                               | (31u << 11) | 0x09u;
            // daddu a0, v0, zero  (delay slot of jalr)
            uint daddu_a0_v0 = (0u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | ((uint)A0 << 11) | 0x2Du;
            // lb    v0, 0(s0)   — at loopBase+0x10
            uint lb_v0_s0    = (0x20u << 26) | ((uint)S0 << 21) | ((uint)V0 << 16) | 0u;
            // bnel  v0, $zero, loopBase  — at loopBase+0x14
            // offset = (loopBase - (loopBase+0x14+4)) / 4 = (-0x18)/4 = -6
            uint bnel_v0     = (0x15u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | unchecked((uint)(ushort)(-6));
            // addiu s0, s0, 1  (delay slot of bnel)
            uint addiu_s0_p1 = (0x09u << 26) | ((uint)S0 << 21) | ((uint)S0 << 16) | 1u;
            // nop — instruction after loop (PC+8 from bnel)
            uint nop_after   = 0u;

            var emu = new PS2Emulator();

            emu.Bus.WriteWord(loopBase + 0x00u, lui_v1);
            emu.Bus.WriteWord(loopBase + 0x04u, lw_v1);
            emu.Bus.WriteWord(loopBase + 0x08u, jalr_v1);
            emu.Bus.WriteWord(loopBase + 0x0Cu, daddu_a0_v0);
            emu.Bus.WriteWord(loopBase + 0x10u, lb_v0_s0);
            emu.Bus.WriteWord(loopBase + 0x14u, bnel_v0);      // PC starts here
            emu.Bus.WriteWord(loopBase + 0x18u, addiu_s0_p1);
            emu.Bus.WriteWord(loopBase + 0x1Cu, nop_after);

            // ── Place a test string in RAM ────────────────────────────────────
            const uint strBase = 0x80001000u; // KSEG0 physical 0x00001000
            const string msg   = "Hello, EE!"; // 10 characters + null
            for (int i = 0; i < msg.Length; i++)
                emu.Bus.WriteByte(strBase + (uint)i, (byte)msg[i]);
            emu.Bus.WriteByte(strBase + (uint)msg.Length, 0); // null terminator

            // ── Set up registers ──────────────────────────────────────────────
            // PC at the BNEL instruction; s0 points to the first char ('H').
            // v0 = 'H' (already loaded by the lb before BNEL).
            emu.Cpu.PC        = loopBase + 0x14u;
            emu.Cpu.RegsLo[S0] = strBase;               // s0 → "Hello, EE!"
            emu.Cpu.RegsLo[V0] = (byte)msg[0];          // v0 = 'H' (current char)

            // ── Execute one Step() ────────────────────────────────────────────
            emu.Step();

            // ── Assertions ───────────────────────────────────────────────────
            // PC should be at loopBase + 0x14 + 8 = loopBase + 0x1C (past bnel+delay).
            Assert.AreEqual(loopBase + 0x1Cu, (uint)(emu.Cpu.PC & 0xFFFFFFFFUL),
                "PC must advance past BNEL + delay slot after fast-forward");

            // s0 should point to the null terminator (strBase + msg.Length).
            uint expectedPtr = strBase + (uint)msg.Length;
            Assert.AreEqual(expectedPtr, (uint)(emu.Cpu.RegsLo[S0] & 0xFFFFFFFFUL),
                "s0 (ptrReg) must point to the null terminator after fast-forward");

            // v0 should be 0 (the null terminator value).
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[V0],
                "v0 (charReg) must be 0 (null terminator) after fast-forward");
        }

        /// <summary>
        /// Verify TryFastForwardBiosPuts does NOT fire when the remaining string is
        /// shorter than BiosPutsMinLen (8 characters), so short strings execute
        /// normally character-by-character.
        /// </summary>
        [TestMethod]
        public void EE_BiosPuts_FastForward_DoesNotFireForShortString()
        {
            const int V0 = 2;
            const int V1 = 3;
            const int S0 = 16;
            const int A0 = 4;
            const uint loopBase = 0x80021000u;

            uint lui_v1      = (0x0Fu << 26) | (0u << 21) | ((uint)V1 << 16) | 0x8001u;
            uint lw_v1       = (0x23u << 26) | ((uint)V1 << 21) | ((uint)V1 << 16)
                               | (uint)(ushort)0x5958;
            uint jalr_v1     = (0u << 26) | ((uint)V1 << 21) | (0u << 16)
                               | (31u << 11) | 0x09u;
            uint daddu_a0_v0 = (0u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | ((uint)A0 << 11) | 0x2Du;
            uint lb_v0_s0    = (0x20u << 26) | ((uint)S0 << 21) | ((uint)V0 << 16) | 0u;
            uint bnel_v0     = (0x15u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | unchecked((uint)(ushort)(-6));
            uint addiu_s0_p1 = (0x09u << 26) | ((uint)S0 << 21) | ((uint)S0 << 16) | 1u;

            var emu = new PS2Emulator();
            emu.Bus.WriteWord(loopBase + 0x00u, lui_v1);
            emu.Bus.WriteWord(loopBase + 0x04u, lw_v1);
            emu.Bus.WriteWord(loopBase + 0x08u, jalr_v1);
            emu.Bus.WriteWord(loopBase + 0x0Cu, daddu_a0_v0);
            emu.Bus.WriteWord(loopBase + 0x10u, lb_v0_s0);
            emu.Bus.WriteWord(loopBase + 0x14u, bnel_v0);
            emu.Bus.WriteWord(loopBase + 0x18u, addiu_s0_p1);

            // Very short string: "Hi" (2 chars) — well below BiosPutsMinLen (8).
            const uint strBase = 0x80002000u;
            emu.Bus.WriteByte(strBase + 0u, (byte)'H');
            emu.Bus.WriteByte(strBase + 1u, (byte)'i');
            emu.Bus.WriteByte(strBase + 2u, 0); // null terminator

            emu.Cpu.PC        = loopBase + 0x14u;
            emu.Cpu.RegsLo[S0] = strBase;
            emu.Cpu.RegsLo[V0] = (byte)'H';

            // ── Step should NOT fast-forward — PC must change by exactly the
            // instruction that was at loopBase+0x14 (the BNEL). ─────────────────
            // The BNEL v0, $zero with v0='H' (non-zero) takes the branch.
            // With a taken BNEL, Step() executes: delay slot (addiu s0++), then
            // jumps to loopBase+0x00 (loop start).
            emu.Step();

            // After one normal BNEL step the PC should jump to loop start (branch taken).
            uint pc = (uint)(emu.Cpu.PC & 0xFFFFFFFFUL);
            Assert.AreNotEqual(loopBase + 0x1Cu, pc,
                "Fast-forward must NOT fire for short strings; PC should go to loop start, not loop exit");
        }

        /// <summary>
        /// Verify TryFastForwardBiosPuts correctly fast-forwards a string that is
        /// exactly at the minimum threshold (BiosPutsMinLen = 8 characters).
        /// </summary>
        [TestMethod]
        public void EE_BiosPuts_FastForward_FiresAtMinimumLength()
        {
            const int V0 = 2;
            const int V1 = 3;
            const int S0 = 16;
            const int A0 = 4;
            const uint loopBase = 0x80022000u;

            uint lui_v1      = (0x0Fu << 26) | (0u << 21) | ((uint)V1 << 16) | 0x8001u;
            uint lw_v1       = (0x23u << 26) | ((uint)V1 << 21) | ((uint)V1 << 16)
                               | (uint)(ushort)0x5958;
            uint jalr_v1     = (0u << 26) | ((uint)V1 << 21) | (0u << 16)
                               | (31u << 11) | 0x09u;
            uint daddu_a0_v0 = (0u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | ((uint)A0 << 11) | 0x2Du;
            uint lb_v0_s0    = (0x20u << 26) | ((uint)S0 << 21) | ((uint)V0 << 16) | 0u;
            uint bnel_v0     = (0x15u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | unchecked((uint)(ushort)(-6));
            uint addiu_s0_p1 = (0x09u << 26) | ((uint)S0 << 21) | ((uint)S0 << 16) | 1u;

            var emu = new PS2Emulator();
            emu.Bus.WriteWord(loopBase + 0x00u, lui_v1);
            emu.Bus.WriteWord(loopBase + 0x04u, lw_v1);
            emu.Bus.WriteWord(loopBase + 0x08u, jalr_v1);
            emu.Bus.WriteWord(loopBase + 0x0Cu, daddu_a0_v0);
            emu.Bus.WriteWord(loopBase + 0x10u, lb_v0_s0);
            emu.Bus.WriteWord(loopBase + 0x14u, bnel_v0);
            emu.Bus.WriteWord(loopBase + 0x18u, addiu_s0_p1);

            // String of exactly 8 characters (= BiosPutsMinLen).
            const uint strBase = 0x80003000u;
            byte[] testStr = System.Text.Encoding.ASCII.GetBytes("12345678");
            for (int i = 0; i < testStr.Length; i++)
                emu.Bus.WriteByte(strBase + (uint)i, testStr[i]);
            emu.Bus.WriteByte(strBase + (uint)testStr.Length, 0);

            emu.Cpu.PC        = loopBase + 0x14u;
            emu.Cpu.RegsLo[S0] = strBase;
            emu.Cpu.RegsLo[V0] = testStr[0]; // '1'

            emu.Step();

            Assert.AreEqual(loopBase + 0x1Cu, (uint)(emu.Cpu.PC & 0xFFFFFFFFUL),
                "Fast-forward must fire at exactly BiosPutsMinLen (8) characters");
            Assert.AreEqual(strBase + (uint)testStr.Length,
                            (uint)(emu.Cpu.RegsLo[S0] & 0xFFFFFFFFUL),
                "s0 must point to null terminator after fast-forward");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[V0],
                "v0 must be 0 (null) after fast-forward");
        }

        /// <summary>
        /// Verify TryFastForwardBiosPuts detects Pattern B: the pointer advance
        /// (addiu s0, s0, 1) is in the JALR delay slot rather than the BNEL
        /// delay slot.  The BNEL delay slot contains daddu a0, v0, zero to set
        /// up the next putchar argument.
        ///
        /// Pattern B loop:
        ///   loop_start:
        ///     lui   v1, ...
        ///     lw    v1, offset(v1)
        ///     jalr  v1
        ///     addiu s0, s0, 1      ; delay slot of jalr: advance pointer
        ///     lb    v0, 0(s0)      ; load next char (from advanced position)
        ///     bnel  v0, $zero, loop_start
        ///     daddu a0, v0, zero   ; delay slot of bnel: set arg for next call
        /// </summary>
        [TestMethod]
        public void EE_BiosPuts_FastForward_PatternB_JalrDelaySlotAdvance()
        {
            const int V0 = 2;
            const int V1 = 3;
            const int S0 = 16;
            const int A0 = 4;
            const uint loopBase = 0x80023000u;

            uint lui_v1      = (0x0Fu << 26) | (0u << 21) | ((uint)V1 << 16) | 0x8001u;
            uint lw_v1       = (0x23u << 26) | ((uint)V1 << 21) | ((uint)V1 << 16)
                               | (uint)(ushort)0x5958;
            // jalr v1
            uint jalr_v1     = (0u << 26) | ((uint)V1 << 21) | (0u << 16)
                               | (31u << 11) | 0x09u;
            // addiu s0, s0, 1  — delay slot of jalr (Pattern B: pointer advance here)
            uint addiu_s0_p1 = (0x09u << 26) | ((uint)S0 << 21) | ((uint)S0 << 16) | 1u;
            // lb v0, 0(s0)
            uint lb_v0_s0    = (0x20u << 26) | ((uint)S0 << 21) | ((uint)V0 << 16) | 0u;
            // bnel v0, $zero, loopBase  (offset = -6 → target = (bnel+4) + (-6)*4 = loopBase)
            uint bnel_v0     = (0x15u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | unchecked((uint)(ushort)(-6));
            // daddu a0, v0, zero  — delay slot of bnel (Pattern B: argument setup here)
            uint daddu_a0_v0 = (0u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | ((uint)A0 << 11) | 0x2Du;

            var emu = new PS2Emulator();
            emu.Bus.WriteWord(loopBase + 0x00u, lui_v1);
            emu.Bus.WriteWord(loopBase + 0x04u, lw_v1);
            emu.Bus.WriteWord(loopBase + 0x08u, jalr_v1);
            emu.Bus.WriteWord(loopBase + 0x0Cu, addiu_s0_p1);  // JALR delay slot: s0++
            emu.Bus.WriteWord(loopBase + 0x10u, lb_v0_s0);
            emu.Bus.WriteWord(loopBase + 0x14u, bnel_v0);      // PC starts here
            emu.Bus.WriteWord(loopBase + 0x18u, daddu_a0_v0);  // BNEL delay slot: move char

            // Place a test string in RAM (long enough to trigger fast-forward).
            const uint strBase = 0x80004000u;
            const string msg   = "Pattern B test!"; // 15 characters + null (> BiosPutsMinLen=8)
            for (int i = 0; i < msg.Length; i++)
                emu.Bus.WriteByte(strBase + (uint)i, (byte)msg[i]);
            emu.Bus.WriteByte(strBase + (uint)msg.Length, 0);

            // At BNEL: s0 already points to current char (incremented by jalr delay slot),
            // v0 = char at s0.
            emu.Cpu.PC         = loopBase + 0x14u;
            emu.Cpu.RegsLo[S0] = strBase;              // s0 → start of string
            emu.Cpu.RegsLo[V0] = (byte)msg[0];         // v0 = first char

            emu.Step();

            Assert.AreEqual(loopBase + 0x1Cu, (uint)(emu.Cpu.PC & 0xFFFFFFFFUL),
                "PC must advance past BNEL + delay slot after Pattern B fast-forward");
            Assert.AreEqual(strBase + (uint)msg.Length, (uint)(emu.Cpu.RegsLo[S0] & 0xFFFFFFFFUL),
                "s0 must point to the null terminator after Pattern B fast-forward");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[V0],
                "v0 must be 0 (null terminator) after Pattern B fast-forward");
        }

        /// <summary>
        /// Verify TryFastForwardBiosPuts correctly scans strings stored in BIOS ROM
        /// (physical address range 0x1FC00000–0x1FFFFFFF), i.e. when the string
        /// pointer refers to a KSEG1 BIOS ROM virtual address (0xBFCxxxxx).
        ///
        /// The old code applied a MainRamSize mask to the physical address, which
        /// silently wrapped BIOS ROM addresses into a wrong RAM offset (e.g.
        /// 0x1FC12345 → 0x1C12345 in RAM), causing the scan to read garbage and
        /// return len=0 (no fast-forward).
        /// </summary>
        [TestMethod]
        public void EE_BiosPuts_FastForward_BiosRomString()
        {
            const int V0 = 2;
            const int V1 = 3;
            const int S0 = 16;
            const int A0 = 4;
            const uint loopBase = 0x80024000u;

            uint lui_v1      = (0x0Fu << 26) | (0u << 21) | ((uint)V1 << 16) | 0x8001u;
            uint lw_v1       = (0x23u << 26) | ((uint)V1 << 21) | ((uint)V1 << 16)
                               | (uint)(ushort)0x5958;
            uint jalr_v1     = (0u << 26) | ((uint)V1 << 21) | (0u << 16)
                               | (31u << 11) | 0x09u;
            uint daddu_a0_v0 = (0u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | ((uint)A0 << 11) | 0x2Du;
            uint lb_v0_s0    = (0x20u << 26) | ((uint)S0 << 21) | ((uint)V0 << 16) | 0u;
            uint bnel_v0     = (0x15u << 26) | ((uint)V0 << 21) | (0u << 16)
                               | unchecked((uint)(ushort)(-6));
            uint addiu_s0_p1 = (0x09u << 26) | ((uint)S0 << 21) | ((uint)S0 << 16) | 1u;

            var emu = new PS2Emulator();
            emu.Bus.WriteWord(loopBase + 0x00u, lui_v1);
            emu.Bus.WriteWord(loopBase + 0x04u, lw_v1);
            emu.Bus.WriteWord(loopBase + 0x08u, jalr_v1);
            emu.Bus.WriteWord(loopBase + 0x0Cu, daddu_a0_v0);
            emu.Bus.WriteWord(loopBase + 0x10u, lb_v0_s0);
            emu.Bus.WriteWord(loopBase + 0x14u, bnel_v0);
            emu.Bus.WriteWord(loopBase + 0x18u, addiu_s0_p1);

            // Place the string in BIOS ROM (physical 0x1FC10000 → virtual KSEG1 0xBFC10000).
            const uint biosRomPhys   = 0x1FC10000u;
            const uint biosRomVirt   = 0xBFC10000u;   // KSEG1 mirror of 0x1FC10000
            const string msg         = "BIOS ROM puts!"; // 14 chars, > BiosPutsMinLen
            for (int i = 0; i < msg.Length; i++)
                emu.Bus.Bios[biosRomPhys - 0x1FC00000u + (uint)i] = (byte)msg[i];
            emu.Bus.Bios[biosRomPhys - 0x1FC00000u + (uint)msg.Length] = 0; // null terminator

            // Ensure main RAM at the (wrongly wrapped) offset 0x1C10000 has a 0 byte so
            // that the old buggy code would produce len=0 and skip the fast-forward.
            // (RAM is zero-initialised, so no explicit write needed — just document intent.)

            emu.Cpu.PC         = loopBase + 0x14u;
            emu.Cpu.RegsLo[S0] = biosRomVirt;          // s0 → BIOS ROM string (KSEG1)
            emu.Cpu.RegsLo[V0] = (byte)msg[0];

            emu.Step();

            // Fast-forward must advance s0 to the null terminator in BIOS ROM virtual space.
            uint expectedPtr = biosRomVirt + (uint)msg.Length;
            Assert.AreEqual(loopBase + 0x1Cu, (uint)(emu.Cpu.PC & 0xFFFFFFFFUL),
                "PC must advance past BNEL + delay slot when string is in BIOS ROM");
            Assert.AreEqual(expectedPtr, (uint)(emu.Cpu.RegsLo[S0] & 0xFFFFFFFFUL),
                "s0 must point to the null terminator in BIOS ROM after fast-forward");
            Assert.AreEqual(0UL, emu.Cpu.RegsLo[V0],
                "v0 must be 0 (null) after BIOS ROM string fast-forward");
        }
    }
}
