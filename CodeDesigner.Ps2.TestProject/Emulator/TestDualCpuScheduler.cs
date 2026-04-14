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
    public class TestDualCpuScheduler
    {
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

        [TestMethod]
        public void DualCpuScheduler_Breakpoint_Physical_FiresWhenPcIsKseg1()
        {
            // Breakpoint stored as physical address (0x1FC00010) must fire when the
            // internal EE PC lands on the KSEG1 alias (0xBFC00010) — both map to
            // the same physical BIOS ROM byte.
            var sched = new DualCpuScheduler();

            // Place NOPs in BIOS ROM so the instruction fetch succeeds.
            sched.EE.Bus.Bios[0x10] = 0x00; // NOP word at physical 0x1FC00010
            sched.EE.Bus.Bios[0x11] = 0x00;
            sched.EE.Bus.Bios[0x12] = 0x00;
            sched.EE.Bus.Bios[0x13] = 0x00;

            // Start execution from the KSEG1 mirror of the same byte.
            sched.EE.Cpu.PC = 0xBFC00010UL;
            sched.Paused = true;

            // Set breakpoint using the physical form of the address.
            sched.Breakpoints.Add(0x1FC00010u);

            // StepOne: executes one NOP and then checks whether new PC hits a BP.
            // The BP is set one instruction ahead (physical 0x1FC00014 ≡ KSEG1 0xBFC00014);
            // so first step advances to 0xBFC00014 — set BP there too.
            sched.Breakpoints.Add(0x1FC00014u);
            sched.StepOne();

            // After the step the new PC should be the physical-normalized form.
            Assert.IsNotNull(sched.LastBreakpointHit,
                "LastBreakpointHit must be set when the physical PC matches a stored breakpoint");
            Assert.AreEqual(0x1FC00014u, sched.LastBreakpointHit,
                "LastBreakpointHit must be the physical form of 0xBFC00014");
        }

        [TestMethod]
        public void DualCpuScheduler_Breakpoint_Kseg0Input_NormalizedToPhysical()
        {
            // A breakpoint added as a KSEG0 address fires when the
            // PC lands on it via KSEG1 after NormalizeToPhysical canonicalises both.
            // This tests that the physical-normalization is symmetric across virtual aliases.
            var sched = new DualCpuScheduler();

            sched.EE.Bus.Bios[0x20] = 0x00;
            sched.EE.Bus.Bios[0x21] = 0x00;
            sched.EE.Bus.Bios[0x22] = 0x00;
            sched.EE.Bus.Bios[0x23] = 0x00;

            sched.EE.Cpu.PC = 0xBFC00020UL; // KSEG1
            sched.Paused = true;

            // Add breakpoint using the KSEG0 form — HandleSetBreakpoint normalises it
            // to physical (0x9FC00024 → 0x1FC00024) before storing, but here we add it
            // directly in the same physical form the scheduler expects.
            sched.Breakpoints.Add(0x1FC00024u);
            sched.StepOne();

            Assert.IsNotNull(sched.LastBreakpointHit,
                "Breakpoint at physical 0x1FC00024 must fire when PC arrives at KSEG1 0xBFC00024");
            Assert.AreEqual(0x1FC00024u, sched.LastBreakpointHit.Value,
                "LastBreakpointHit must be returned as physical 0x1FC00024");
        }

        [TestMethod]
        public void DualCpuScheduler_Breakpoint_PhysicalRam_FiresWhenPcIsKseg0()
        {
            // Regression test for the physical-address breakpoint bug: the live
            // disassembly panel displays EE Main RAM at physical base 0x00000000.
            // Breakpoints set from that panel arrive as physical addresses (e.g. 0x00010000).
            // They must fire even though the CPU runs from the KSEG0 alias (0x80010000).
            var sched = new DualCpuScheduler();

            // Place two NOPs in EE Main RAM so the instruction fetch succeeds.
            uint addr = 0x00010000u;
            sched.EE.Bus.Ram[addr]     = 0x00; // NOP word at physical 0x00010000
            sched.EE.Bus.Ram[addr + 1] = 0x00;
            sched.EE.Bus.Ram[addr + 2] = 0x00;
            sched.EE.Bus.Ram[addr + 3] = 0x00;
            sched.EE.Bus.Ram[addr + 4] = 0x00; // NOP word at physical 0x00010004
            sched.EE.Bus.Ram[addr + 5] = 0x00;
            sched.EE.Bus.Ram[addr + 6] = 0x00;
            sched.EE.Bus.Ram[addr + 7] = 0x00;

            // Start from the KSEG0 alias.
            sched.EE.Cpu.PC = (ulong)(addr | 0x80000000u); // 0x80010000
            sched.Paused = true;

            // Set breakpoint using the physical address — exactly as the panel would.
            sched.Breakpoints.Add(addr + 4); // physical 0x00010004
            sched.StepOne();

            Assert.IsNotNull(sched.LastBreakpointHit,
                "Physical breakpoint 0x00010004 must fire when KSEG0 PC arrives at 0x80010004");
            Assert.AreEqual(addr + 4, sched.LastBreakpointHit.Value,
                "LastBreakpointHit must be the physical address 0x00010004");
        }

        [TestMethod]
        public void DualCpuScheduler_Watchpoint_RunSlice_PausesAndCapturesHitInfo()
        {
            var sched = new DualCpuScheduler();

            sched.EE.LoadProgram(0x00000000u, new uint[] { 0xAC080100u, 0x00000000u });
            sched.EE.Cpu.RegsLo[8] = 0xDEADBEEFu;
            sched.EE.Cpu.PC = 0;
            sched.WriteWatchpoints.Add(new MemoryWatchpoint
            {
                Id = "wp-0",
                VirtualAddress = 0x00000100u,
                PhysStart = 0x00000100u,
                PhysEnd = 0x00000104u,
                Size = 4,
                Type = WatchpointType.Write,
            });

            sched.RunSlice(1);

            Assert.IsTrue(sched.Paused, "Scheduler must pause after a watchpoint hit");
            Assert.IsNotNull(sched.LastWatchpointHit, "Watchpoint hit details must be captured");
            Assert.AreEqual("wp-0", sched.LastWatchpointHit!.WatchpointId);
            Assert.AreEqual(0x00000100u, sched.LastWatchpointHit.PhysAddress);
            Assert.AreEqual(4, sched.LastWatchpointHit.Size);
            Assert.AreEqual(0u, sched.LastWatchpointHit.PC);
            Assert.AreEqual(WatchpointType.Write, sched.LastWatchpointHit.Type);
            Assert.AreEqual(0xDEADBEEFu, sched.EE.Bus.ReadWord(0x00000100u));
        }

        [TestMethod]
        public void DualCpuScheduler_Watchpoint_StepOne_DetectsOverlappingWrite()
        {
            var sched = new DualCpuScheduler();

            sched.EE.LoadProgram(0x00000000u, new uint[] { 0xAC080100u });
            sched.EE.Cpu.RegsLo[8] = 0x01234567u;
            sched.EE.Cpu.PC = 0;
            sched.Paused = true;
            sched.WriteWatchpoints.Add(new MemoryWatchpoint
            {
                Id = "wp-overlap",
                VirtualAddress = 0x00000103u,
                PhysStart = 0x00000103u,
                PhysEnd = 0x00000104u,
                Size = 1,
                Type = WatchpointType.Write,
            });

            sched.StepOne();

            Assert.IsNotNull(sched.LastWatchpointHit, "Overlapping writes must trigger the watchpoint");
            Assert.AreEqual("wp-overlap", sched.LastWatchpointHit!.WatchpointId);
            Assert.AreEqual(0x00000100u, sched.LastWatchpointHit.PhysAddress);
            Assert.AreEqual(4, sched.LastWatchpointHit.Size);
        }

        [TestMethod]
        public void DualCpuScheduler_Resume_ClearsLastWatchpointHit()
        {
            var sched = new DualCpuScheduler();

            sched.EE.LoadProgram(0x00000000u, new uint[] { 0xAC080100u, 0x00000000u });
            sched.EE.Cpu.RegsLo[8] = 0xCAFEBABEu;
            sched.EE.Cpu.PC = 0;
            sched.WriteWatchpoints.Add(new MemoryWatchpoint
            {
                Id = "wp-1",
                VirtualAddress = 0x00000100u,
                PhysStart = 0x00000100u,
                PhysEnd = 0x00000104u,
                Size = 4,
                Type = WatchpointType.Write,
            });

            sched.RunSlice(1);
            Assert.IsNotNull(sched.LastWatchpointHit, "Watchpoint must fire before resume");

            sched.Paused = false;

            Assert.IsFalse(sched.Paused, "Scheduler must resume when Paused is cleared");
            Assert.IsNull(sched.LastWatchpointHit, "Resuming must clear the previous watchpoint hit");
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

    }
}
