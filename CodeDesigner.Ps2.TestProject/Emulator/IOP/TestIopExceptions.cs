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
    public class TestIopExceptions
    {
        // =====================================================================
        // IOP interrupt delivery and exception dispatch (R3000A) tests
        // =====================================================================

        [TestMethod]
        public void IopEmulator_COP0_Sync_MTC0_UpdatesIopStateSR()
        {
            // MTC0 on the IOP bridge must update IopState.SR via SyncIopFromBridge.
            var iop = new IopEmulator();
            // addiu $8, $0, 0x401  (load 0x401 into $8 = $t0)
            uint addiuT0 = 0x24080401u;
            // mtc0 $8, $12 (COP0: opcode=0x10, rs=4 MTC, rt=8, rd=12)
            // Encoding: 0x10 << 26 | 0x04 << 21 | 0x08 << 16 | 0x0C << 11
            uint mtc0T0  = (0x10u << 26) | (0x04u << 21) | (0x08u << 16) | (0x0Cu << 11);
            // LoadProgram writes directly to the physical RAM byte array (WriteWordToRam has
            // no address translation). KSEG0 0x80000000 translates to physical 0x00000000,
            // so pass 0 to match what the CPU fetches when PC = 0x80000000.
            iop.LoadProgram(0x00000000u, new uint[] { addiuT0, mtc0T0 });
            iop.State.PC = 0x80000000u; // KSEG0 virtual → physical 0
            iop.State.SR = 0u; // clear BEV so code runs from RAM

            iop.Step(); // execute addiu
            iop.Step(); // execute mtc0

            Assert.AreEqual(0x401u, iop.State.SR & 0xFFFu,
                "MTC0 $t0,$12 must update IopState.SR via COP0 bridge sync");
        }

        [TestMethod]
        public void IopEmulator_Interrupt_DeliveredWhenIEcAndIP2Set()
        {
            // Verify IOP takes hardware interrupt when I_CTRL=1, SR.IEc=1, SR.IM2=1, and INTC pending.
            // Matches PCSX2 iopEventTest: checks HW_ICTRL != 0 before dispatching.
            var iop = new IopEmulator();
            // SR with IEc=1 (bit 0) and IM2=1 (bit 10), no BEV → vector 0x80000080
            iop.State.SR    = 0x401u;   // IEc=1, IM2=1
            iop.State.Cause = 0u;
            iop.State.PC    = 0x80000000u;

            // The interrupt check fires at the top of Step() before any instruction fetch,
            // so the instruction at PC is never read. DispatchIopException sets PC = 0x80000080
            // directly. No code needs to exist at the initial PC or exception vector.

            // Raise IOP INTC interrupt (sets I_STAT bit 0)
            iop.Bus.RaiseInterrupt(0);
            // Enable I_MASK bit 0 so IsInterruptPending = true
            iop.Bus.WriteWord(0x1F801074u, 0x1u);
            // Enable I_CTRL (master interrupt enable) — required per PCSX2 iopEventTest
            iop.Bus.WriteWord(0x1F801078u, 0x1u);

            iop.Step(); // should take interrupt and dispatch to 0x80000080

            Assert.AreEqual(0x80000080u, iop.State.PC, "IOP must jump to 0x80000080 on interrupt (BEV=0)");
        }

        [TestMethod]
        public void IopEmulator_Interrupt_NotTakenWhenIEcClear()
        {
            // IOP must NOT take interrupt when SR.IEc=0 even if pending.
            var iop = new IopEmulator();
            iop.State.SR    = 0x400u;   // IM2=1 but IEc=0
            iop.State.Cause = 0u;
            iop.State.PC    = 0x80000000u;

            // IopMemoryBus.Ram is a freshly allocated byte[] (zero-filled by .NET), so
            // physical address 0 contains 0x00000000 = NOP. Explicitly write it to be clear.
            iop.LoadProgram(0x00000000u, new uint[] { 0u }); // nop at physical 0 (KSEG0 0x80000000)

            iop.Bus.RaiseInterrupt(0);
            iop.Bus.WriteWord(0x1F801074u, 0x1u);
            iop.Bus.WriteWord(0x1F801078u, 0x1u); // I_CTRL=1 (enabled)

            iop.Step(); // should execute nop, NOT take interrupt

            Assert.AreEqual(0x80000004u, iop.State.PC, "IOP must NOT take interrupt when IEc=0");
        }

        [TestMethod]
        public void IopEmulator_Interrupt_NotTakenWhenICtrlClear()
        {
            // IOP must NOT take interrupt when I_CTRL=0 (master disable), even if SR and INTC
            // are fully enabled.  Matches PCSX2 iopEventTest: HW_ICTRL != 0 guard.
            var iop = new IopEmulator();
            iop.State.SR    = 0x401u;   // IEc=1, IM2=1
            iop.State.Cause = 0u;
            iop.State.PC    = 0x80000000u;

            iop.LoadProgram(0x00000000u, new uint[] { 0u }); // nop at physical 0 (KSEG0 0x80000000)

            iop.Bus.RaiseInterrupt(0);
            iop.Bus.WriteWord(0x1F801074u, 0x1u);
            // I_CTRL intentionally left at 0 (reset default) — interrupt must be suppressed

            iop.Step(); // should execute nop, NOT take interrupt

            Assert.AreEqual(0x80000004u, iop.State.PC,
                "IOP must NOT take interrupt when I_CTRL=0 (master interrupt disable)");
        }

        [TestMethod]
        public void IopEmulator_DispatchException_R3000A_StackShift()
        {
            // Verify that exception entry uses R3000A stack shift, not EXL.
            // SR = IEc(0)=0, KUc(1)=1 (user mode), IEp(2)=1 (prev IE=1), KUp(3)=0
            //    = 0b0110 = 0x6
            var iop = new IopEmulator();
            iop.State.SR    = 0x6u;
            iop.State.Cause = 0u;
            iop.State.PC    = 0x80000000u;

            // LoadProgram writes to physical RAM; KSEG0 0x80000000 → phys 0x00000000.
            // Disable BEV so exception vector is 0x80000080 → phys 0x00000080.
            iop.State.SR &= ~IopState.SR_BEV;
            iop.LoadProgram(0x00000000u, new uint[] { 0x0000000Cu }); // syscall at phys 0
            iop.LoadProgram(0x00000080u, new uint[] { 0u });           // nop at phys 0x80 (exception vector)

            iop.Step(); // dispatch syscall exception

            // After R3000A stack shift on SR=0x6: sr = (0x6 & ~0x3F) | ((0x6 & 0x0F) << 2)
            //   = 0 | (6 << 2) = 0x18
            Assert.AreEqual(0x18u, iop.State.SR & 0x3Fu,
                "R3000A exception must shift IE/KU stack into bits 5-0 of SR");
        }

        [TestMethod]
        public void IopEmulator_RFE_RestoresIEKUStack()
        {
            // SR bits 5-0 = 0x28 = 0b101000: KUo=1,IEo=0,KUp=1,IEp=0,KUc=0,IEc=0
            // RFE: {IEc,KUc} ← {IEp,KUp}; {IEp,KUp} ← {IEo,KUo}
            //   Formula: sr = (sr & ~0x0F) | ((sr & 0x3C) >> 2)
            //          = (0x28 & ~0xF) | ((0x28 & 0x3C) >> 2)
            //          = 0x20           | (0x28 >> 2 & 0xF)  = 0x20 | 0x0A = 0x2A
            var iop = new IopEmulator();
            iop.State.SR = 0x28u | IopState.SR_BEV; // BEV=1, bits 5-0 = 0x28
            iop.State.PC = 0xBFC00000u;

            // Write RFE (0x42000010) directly into the BIOS ROM byte array (read-only via WriteWord)
            void WriteBiosLE(uint physOff, uint word)
            {
                byte[] b = iop.Bus.Bios;
                b[physOff + 0] = (byte)(word & 0xFF);
                b[physOff + 1] = (byte)((word >> 8) & 0xFF);
                b[physOff + 2] = (byte)((word >> 16) & 0xFF);
                b[physOff + 3] = (byte)((word >> 24) & 0xFF);
            }
            WriteBiosLE(0, 0x42000010u); // rfe at 0xBFC00000 → Bios[0..3]
            WriteBiosLE(4, 0x00000000u); // nop at 0xBFC00004

            iop.Step(); // execute rfe

            Assert.AreEqual(0x2Au, iop.State.SR & 0x3Fu,
                "RFE must restore IE/KU stack: IEc←IEp, KUc←KUp, IEp←IEo, KUp←KUo");
        }

        [TestMethod]
        public void Sif_EeSignaledIop_FiredOnNewMsFlagBits()
        {
            var sif = new SIF();
            int fired = 0;
            sif.EeSignaledIop += (_, _) => fired++;

            sif.WriteRegister(SIF.REG_MSFLAG, 0x1u); // new bits → fires
            Assert.AreEqual(1, fired, "EeSignaledIop must fire when EE sets new MSFLAG bits");

            sif.WriteRegister(SIF.REG_MSFLAG, 0x1u); // no new bits → no fire
            Assert.AreEqual(1, fired, "EeSignaledIop must NOT fire when no new bits");

            sif.WriteRegister(SIF.REG_MSFLAG, 0x2u); // new bit → fires again
            Assert.AreEqual(2, fired, "EeSignaledIop must fire again for new MSFLAG bits");
        }

        [TestMethod]
        public void IopEmulator_EeSignaledIop_RaisesIopIntcSbus()
        {
            var emu = new PS2Emulator();
            var scheduler = new DualCpuScheduler(emu);

            Assert.AreEqual(0u, scheduler.IOP.Bus.IStat & 0x2u,
                "IOP INTC SBUS bit must start clear");

            // EE writes to MSFLAG to signal the IOP
            emu.Bus.Sif.WriteRegister(SIF.REG_MSFLAG, 0x00000001u);

            Assert.AreNotEqual(0u, scheduler.IOP.Bus.IStat & 0x2u,
                "IOP INTC SBUS (bit 1) must be raised when EE sets MSFLAG");
        }

        // ── IOP bgez / bltz sign-extension regression tests ──────────────────────
        // Before the SyncBridgeFromIop sign-extension fix, IOP 32-bit registers
        // holding negative values (e.g. 0xFFFFFFFF = −1) were zero-extended to
        // 0x00000000_FFFFFFFF in the 64-bit EE bridge.  Signed branch conditions
        // then treated −1 as +4,294,967,295 (positive), causing bgez to loop
        // forever in the memmove backward-copy path during IOP BIOS boot.

        [TestMethod]
        public void IopEmulator_Bgez_NegativeRegister_NotTaken()
        {
            // bgez should NOT branch when rs holds a negative 32-bit value (−1 = 0xFFFFFFFF).
            var bus = new IopMemoryBus();
            var iop = new IopEmulator(bus);

            // Write:   addiu a2, zero, -1   (a2 = 0xFFFFFFFF = −1 as 32-bit)
            //          bgez a2, +1           (should NOT branch)
            //          addiu v0, zero, 7     (fall-through: v0 = 7)
            //          jr ra
            //          nop
            const uint kBase = 0x0000_1000u;
            // addiu a2, zero, -1  →  opcode=9, rt=6, rs=0, imm=0xFFFF
            bus.WriteWord(kBase + 0,  0x2406_FFFFu);
            // bgez a2, +1  →  REGIMM(rt=1), rs=6, offset=1  →  jumps to kBase+12 if taken
            bus.WriteWord(kBase + 4,  0x04C1_0001u);
            // addiu v0, zero, 7  (fall-through = branch NOT taken)
            bus.WriteWord(kBase + 8,  0x2402_0007u);
            // jr ra
            bus.WriteWord(kBase + 12, 0x03E0_0008u);
            // nop (delay slot)
            bus.WriteWord(kBase + 16, 0x0000_0000u);

            iop.State.PC = kBase;
            iop.State.WriteReg(31, kBase + 100u); // ra = somewhere safe
            for (int i = 0; i < 10; i++) iop.Step();

            Assert.AreEqual(7u, iop.State.ReadReg(2),
                "bgez on −1 must NOT branch; fall-through sets v0=7");
        }

        [TestMethod]
        public void IopEmulator_Bgez_ZeroRegister_Taken()
        {
            // bgez SHOULD branch when rs = 0 (≥ 0 is true for zero).
            var bus = new IopMemoryBus();
            var iop = new IopEmulator(bus);

            const uint kBase = 0x0000_1000u;
            // addiu a2, zero, 0   (a2 = 0)
            bus.WriteWord(kBase + 0,  0x2406_0000u);
            // bgez a2, +2  →  offset=2 → target = (kBase+4+4) + 2*4 = kBase+16 (jr ra)
            //   delay slot (kBase+8 = nop) always runs; kBase+12 is skipped when taken
            bus.WriteWord(kBase + 4,  0x04C1_0002u);
            // nop (delay slot — always executes)
            bus.WriteWord(kBase + 8,  0x0000_0000u);
            // addiu v0, zero, 7   (NOT reached when branch taken)
            bus.WriteWord(kBase + 12, 0x2402_0007u);
            // jr ra
            bus.WriteWord(kBase + 16, 0x03E0_0008u);
            // nop (delay slot of jr ra)
            bus.WriteWord(kBase + 20, 0x0000_0000u);

            iop.State.PC = kBase;
            iop.State.WriteReg(31, kBase + 100u);
            for (int i = 0; i < 10; i++) iop.Step();

            Assert.AreEqual(0u, iop.State.ReadReg(2),
                "bgez on 0 MUST branch; v0=7 assignment skipped so v0 stays 0");
        }

        [TestMethod]
        public void IopEmulator_Bltz_NegativeRegister_Taken()
        {
            // bltz SHOULD branch when rs = −1 (0xFFFFFFFF as 32-bit).
            var bus = new IopMemoryBus();
            var iop = new IopEmulator(bus);

            const uint kBase = 0x0000_1000u;
            // addiu a2, zero, -1   (a2 = 0xFFFFFFFF = −1)
            bus.WriteWord(kBase + 0,  0x2406_FFFFu);
            // bltz a2, +2  (rt=0 = BLTZ; offset=2 → target = kBase+16, skips kBase+12)
            bus.WriteWord(kBase + 4,  0x04C0_0002u);
            // nop (delay slot)
            bus.WriteWord(kBase + 8,  0x0000_0000u);
            // addiu v0, zero, 7    (NOT reached when branch taken)
            bus.WriteWord(kBase + 12, 0x2402_0007u);
            // jr ra
            bus.WriteWord(kBase + 16, 0x03E0_0008u);
            // nop
            bus.WriteWord(kBase + 20, 0x0000_0000u);

            iop.State.PC = kBase;
            iop.State.WriteReg(31, kBase + 100u);
            for (int i = 0; i < 10; i++) iop.Step();

            Assert.AreEqual(0u, iop.State.ReadReg(2),
                "bltz on −1 MUST branch; v0=7 assignment skipped so v0 stays 0");
        }

        [TestMethod]
        public void IopMemoryBus_ICtrl_ReadClearsRegister()
        {
            // 0x1F801078 (I_CTRL) uses read-and-clear + write-OR semantics per PCSX2 IopHw.cpp.
            var bus = new IopMemoryBus();
            const uint kICtrl = 0x1F801078u;

            // Initial value must be 0.
            Assert.AreEqual(0u, bus.ICtrl, "I_CTRL must reset to 0");

            // Write 1 → I_CTRL = 0 | 1 = 1 (OR semantics).
            bus.WriteWord(kICtrl, 1u);
            Assert.AreEqual(1u, bus.ICtrl, "I_CTRL must be 1 after WriteWord(1)");

            // Read must return 1 then clear the register.
            uint readVal = bus.ReadWord(kICtrl);
            Assert.AreEqual(1u, readVal,   "I_CTRL read must return 1");
            Assert.AreEqual(0u, bus.ICtrl, "I_CTRL must be 0 after read-and-clear");

            // Write 0 after clear has no effect (OR with 0 = 0).
            bus.WriteWord(kICtrl, 0u);
            Assert.AreEqual(0u, bus.ICtrl, "I_CTRL write-OR with 0 must not change value");

            // Write 1 again → re-enable.
            bus.WriteWord(kICtrl, 1u);
            Assert.AreEqual(1u, bus.ICtrl, "I_CTRL must be 1 after second WriteWord(1)");

            // Non-destructive ICtrl property must not clear the register.
            uint prop1 = bus.ICtrl;
            uint prop2 = bus.ICtrl;
            Assert.AreEqual(1u, prop1, "ICtrl property must return current value non-destructively");
            Assert.AreEqual(1u, prop2, "ICtrl property must not clear the register");
        }

        [TestMethod]
        public void IopTimer_Zret_ResetsCounterOnCompareMatch()
        {
            // Mode bit [3] = ZRET: counter must reset to 0 after hitting the target.
            // Without ZRET the counter wraps after 65535; with ZRET it resets immediately.
            var t = new IopTimer(0);
            // CUE=1, CMPE=1, ZRET=1 → bits 7|4|3 = 0x98
            t.WriteRegister(IopTimer.REG_MODE,   0x0098u);
            t.WriteRegister(IopTimer.REG_TARGET,    5u);

            int fires = 0;
            t.CompareInterruptPending += (_, _) => fires++;

            // First firing: count reaches 5 → EQUF set, interrupt, count reset to 0.
            t.Tick(5u);
            Assert.AreEqual(0u, t.Count,  "ZRET: counter must reset to 0 after compare match");
            Assert.AreEqual(1,  fires,    "ZRET: one interrupt must have fired at target=5");

            // Read TN_MODE clears EQUF so the second interrupt can fire.
            t.ReadRegister(IopTimer.REG_MODE);

            // Second period: count goes from 0 to 5 → fires again.
            t.Tick(5u);
            Assert.AreEqual(0u, t.Count, "ZRET: counter must reset again after second match");
            Assert.AreEqual(2, fires,    "ZRET: second interrupt must fire at target=5");
        }

        [TestMethod]
        public void IopTimer_NoZret_CountContinuesPastTarget()
        {
            // Without ZRET the counter does NOT reset on compare match — it keeps counting.
            var t = new IopTimer(0);
            // CUE=1, CMPE=1, ZRET=0 → bits 7|4 = 0x90
            t.WriteRegister(IopTimer.REG_MODE,   0x0090u);
            t.WriteRegister(IopTimer.REG_TARGET,   5u);

            int fires = 0;
            t.CompareInterruptPending += (_, _) => fires++;

            // Count 0 → 5: interrupt fires.
            t.Tick(5u);
            Assert.AreEqual(5u, t.Count, "No ZRET: counter must NOT reset on compare match");
            Assert.AreEqual(1,  fires,   "No ZRET: one interrupt at target=5");

            // Count past target: no second interrupt while EQUF is set.
            t.Tick(3u);
            Assert.AreEqual(8u, t.Count, "No ZRET: counter continues past target");
            Assert.AreEqual(1,  fires,   "No ZRET: no second interrupt while EQUF is still set");
        }
    }
}
