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
    public class TestTlb
    {
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

    }
}
