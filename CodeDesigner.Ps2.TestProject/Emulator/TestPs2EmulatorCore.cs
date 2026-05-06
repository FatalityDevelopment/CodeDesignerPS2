using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU.COP0;
using CodeDesigner.Ps2.Common.CPU.COP1;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.Hardware;
using CodeDesigner.Ps2.EmulatorCore.IOP;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestPs2EmulatorCore
    {
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
        public void Emulator_UnknownInstruction_RaisesEventAndDispatchesRI()
        {
            uint unknownWord = 0;
            var emu = new PS2Emulator();

            // Write a clearly invalid opcode at a known address.
            // Opcode 0x1D is reserved and has no defined PS2 EE instruction.
            uint badWord = 0x74000000u; // opcode = 0x1D (= 29)
            emu.LoadProgram(0x00000200, new[] { badWord });
            emu.Cpu.PC = 0x00000200;

            // Ensure BEV=0 so we can predict the exception vector (0x80000080).
            emu.Cpu.COP0.WriteRegister32(12, 0u); // Status = 0 → BEV=0

            bool eventFired = false;
            emu.UnknownInstruction += (_, w) => { unknownWord = w; eventFired = true; };

            emu.Step();

            // The UnknownInstruction event must still fire to allow external notification.
            Assert.IsTrue(eventFired, "UnknownInstruction event should fire");
            Assert.AreEqual(badWord, unknownWord, "Event payload should be the offending word");

            // Per MIPS architecture, executing an undefined opcode raises a
            // Reserved Instruction exception (ExcCode = 10).
            uint cause = emu.Cpu.COP0.ReadRegister32(13);
            uint excCode = (cause >> 2) & 0x1Fu;
            Assert.AreEqual(10u, excCode, "Cause.ExcCode must be 10 (ReservedInstr)");

            // EPC should point to the faulting instruction.
            ulong epc = emu.Cpu.COP0.ReadRegister64(14);
            Assert.AreEqual(0x200UL, epc, "EPC must point to the faulting instruction");

            // Status.EXL must be set (we are now in exception mode).
            uint status = emu.Cpu.COP0.ReadRegister32(12);
            Assert.IsTrue((status & (1u << 1)) != 0, "Status.EXL must be set after RI exception");

            // With BEV=0, the general exception vector is 0x80000080.
            Assert.AreEqual(0x80000080UL, emu.Cpu.PC,
                "PC must be redirected to the general exception vector (BEV=0 → 0x80000080)");
        }

        /// <summary>
        /// Verifies that each encoding from the problem statement raises a proper
        /// Reserved Instruction exception (ExcCode=10) per MIPS architecture.
        /// </summary>
        [TestMethod]
        public void Emulator_UndefinedOpcodes_DispatchReservedInstructionException()
        {
            // All four problem-statement encodings are undefined in the PS2 EE:
            //   0x74726574 — opcode=0x1D  (reserved in PS2 EE standard table)
            //   0x4E49203A — COP1X funct=0x3A  (no such COP1X on R5900)
            //   0x49202300 — COP2  rs=0x09      (COP2_Unknown in PCSX2)
            //   0x43414D44 — COP0  rs=0x1A      (COP0_Unknown in PCSX2)
            uint[] undefinedWords = { 0x74726574u, 0x4E49203Au, 0x49202300u, 0x43414D44u };

            foreach (uint word in undefinedWords)
            {
                var emu = new PS2Emulator();
                emu.LoadProgram(0x00000100, new[] { word });
                emu.Cpu.PC = 0x00000100;
                emu.Cpu.COP0.WriteRegister32(12, 0u); // Status = 0 → BEV=0, EXL=0

                bool fired = false;
                emu.UnknownInstruction += (_, _) => fired = true;

                emu.Step();

                Assert.IsTrue(fired,
                    $"UnknownInstruction event should fire for undefined encoding 0x{word:X8}");

                uint cause = emu.Cpu.COP0.ReadRegister32(13);
                uint excCode = (cause >> 2) & 0x1Fu;
                Assert.AreEqual(10u, excCode,
                    $"Cause.ExcCode must be 10 (ReservedInstr) for 0x{word:X8}, got ExcCode={excCode}");

                ulong epc = emu.Cpu.COP0.ReadRegister64(14);
                Assert.AreEqual(0x100UL, epc,
                    $"EPC must point to faulting instruction (0x100) for 0x{word:X8}");

                Assert.AreEqual(0x80000080UL, emu.Cpu.PC,
                    $"PC must redirect to exception vector for 0x{word:X8}");
            }
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
            var cpu = new CodeDesigner.Ps2.Common.CPU.CpuState();

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
            var cpu = new CodeDesigner.Ps2.Common.CPU.CpuState();
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
            var cpu = new CodeDesigner.Ps2.Common.CPU.CpuState();

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
            var cpu = new CodeDesigner.Ps2.Common.CPU.CpuState();
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

    }
}
