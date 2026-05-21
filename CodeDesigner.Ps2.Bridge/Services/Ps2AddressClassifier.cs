namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Classifies 32-bit values as plausible PS2 EE virtual addresses.
    /// Shared by the pointer scanner and the disassembler render path.
    /// </summary>
    internal static class Ps2AddressClassifier
    {
        // Hardcoded to avoid an EmulatorCore dependency. These mirror
        // CodeDesigner.Ps2.EmulatorCore.PS2MemoryBus.{MainRamSize,ScratchpadSize}.
        public const uint EeMainRamSizeBytes    = 32 * 1024 * 1024;
        public const uint EeScratchpadSizeBytes = 16 * 1024;

        /// <summary>
        /// Minimum physical address a pointer must target to be considered valid.
        /// The first 64 KB of EE physical RAM is the exception-vector / BIOS-stub area;
        /// game data and heap allocations never live that low, so small constants such as
        /// 0x00000001 that accidentally match the address range are rejected.
        /// </summary>
        public const uint MinValidPointerPhysAddr = 0x00010000u;

        /// <summary>
        /// Tests whether <paramref name="val"/> is a plausible EE virtual address:
        /// user-mode or KSEG0/KSEG1 main RAM (physical 0x10000–32 MB), or EE scratchpad
        /// (0x70000000–0x70003FFF).
        ///
        /// <para>
        /// The leading filter — <c>(val &gt;&gt; 16) != 0</c> — rejects small constants
        /// like <c>0x00000020</c> or <c>0x000000FF</c> outright before doing the more
        /// expensive range work. For a real EE address such as <c>0x00123456</c> the
        /// upper halfword is <c>0x0012</c>, so the test passes.
        /// </para>
        /// </summary>
        public static bool IsValidEePointer(uint val)
        {
            if (val == 0) return false;

            // Cheap upper-halfword pre-check. Catches values that survive the
            // physical-range check only by accident (e.g. KSEG0 0x80000000+small
            // never appears here because phys mapping below catches it, but
            // raw user-mode values like 0x000000FF are knocked out cheaply).
            if ((val >> 16) == 0) return false;

            // Pointers are always 4-byte aligned in MIPS — reject unaligned values.
            if ((val & 3) != 0) return false;

            uint phys = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;

            // Reject the exception-vector / BIOS-stub area (physical < 0x00010000).
            if (phys < MinValidPointerPhysAddr) return false;

            return phys < EeMainRamSizeBytes ||
                   (val >= 0x70000000u && val < 0x70000000u + EeScratchpadSizeBytes);
        }

        /// <summary>
        /// Normalises <paramref name="vaddr"/> to its EE physical equivalent.
        /// KSEG0/KSEG1 addresses are stripped via <c>&amp; 0x1FFFFFFF</c>;
        /// scratchpad and user-mode are passed through.
        /// </summary>
        public static uint ToPhysical(uint vaddr)
        {
            return vaddr >= 0x80000000u && vaddr < 0xC0000000u
                ? vaddr & 0x1FFFFFFFu
                : vaddr;
        }
    }
}
