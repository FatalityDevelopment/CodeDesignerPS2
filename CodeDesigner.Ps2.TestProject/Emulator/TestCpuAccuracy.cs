using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU.COP0;
using CodeDesigner.Ps2.Common.CPU.COP1;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestCpuAccuracy
    {
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
