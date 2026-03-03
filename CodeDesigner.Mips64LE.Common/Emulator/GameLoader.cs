using System;
using System.Text;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE.Common.Emulator
{
    /// <summary>
    /// Boots a PS2 game from an ISO-9660 disc image.
    ///
    /// The PS2 boot sequence reads <c>SYSTEM.CNF</c> from the root of the disc,
    /// locates the <c>BOOT2</c> key whose value is the executable path in the
    /// form <c>cdrom0:\SLUS_123.45;1</c>, then loads and executes that ELF.
    ///
    /// <see cref="BootFromDisc"/> performs the complete sequence:
    ///   1. Parses <c>SYSTEM.CNF</c> via <see cref="ParseSystemCnf"/>.
    ///   2. Reads the executable from the disc using <see cref="Cdvd"/>.
    ///   3. Loads the ELF into the emulator via <see cref="ElfLoader.LoadIntoEmulator"/>,
    ///      which also sets <c>cpu.PC</c> to the entry point.
    ///
    /// <see cref="InsertDisc"/> is the correct method to use when a real PS2 BIOS
    /// is present. It mounts the disc in the IOP's CDVD drive without loading the
    /// ELF or changing the PC, so the BIOS can run from its reset vector
    /// (0xBFC00000), initialise the kernel, and boot the game naturally — exactly
    /// as a real PlayStation 2 does.
    ///
    /// Reference: PCSX2 <c>pcsx2/CDVD/IsoFS/IsoFS.cpp</c> and
    /// <c>pcsx2/Elfheader.cpp</c>.
    /// </summary>
    public static class GameLoader
    {
        // Key name expected in SYSTEM.CNF (case-insensitive).
        private const string Boot2Key = "BOOT2";

        // Device prefix used by the PS2 OS (e.g. "cdrom0:\").
        private const string CdromPrefix = "cdrom0:";

        /// <summary>
        /// Parse a <c>SYSTEM.CNF</c> text and return the disc-relative path of
        /// the boot executable.
        ///
        /// The value of the <c>BOOT2</c> key looks like
        /// <c>cdrom0:\SLUS_210.40;1</c> or <c>cdrom0:/SUBDIR/GAME.ELF;1</c>.
        /// The device prefix (<c>cdrom0:\</c> or <c>cdrom0:/</c>) is stripped so
        /// the returned string can be passed directly to <see cref="Cdvd.FindFile"/>.
        /// </summary>
        /// <param name="content">Raw text content of SYSTEM.CNF.</param>
        /// <returns>
        /// The disc-relative path (e.g. <c>SLUS_210.40;1</c>), or <c>null</c>
        /// when no <c>BOOT2</c> key is found.
        /// </returns>
        public static string? ParseSystemCnf(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                int eq = line.IndexOf('=');
                if (eq < 0)
                    continue;

                string key = line[..eq].Trim();
                if (!key.Equals(Boot2Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[(eq + 1)..].Trim();

                // Strip the "cdrom0:" device prefix (if present) and any leading
                // backslash or forward slash that follows it.
                if (value.StartsWith(CdromPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value[CdromPrefix.Length..].TrimStart('\\', '/');
                }

                return value.Length > 0 ? value : null;
            }

            return null;
        }

        /// <summary>
        /// Boot a PS2 game from a raw ISO-9660 disc image byte array.
        ///
        /// The method reads <c>SYSTEM.CNF</c> from the disc, extracts the
        /// <c>BOOT2</c> executable path, reads the ELF binary, loads its
        /// PT_LOAD segments into the emulator's main RAM, and sets
        /// <c>emulator.Cpu.PC</c> to the ELF entry point.
        /// </summary>
        /// <param name="isoData">
        /// Raw disc image bytes (2048-byte sectors, no sub-channel data).
        /// </param>
        /// <param name="emulator">
        /// The <see cref="PS2Emulator"/> whose RAM and CPU will receive the
        /// loaded executable.
        /// </param>
        /// <returns>The ELF entry-point virtual address.</returns>
        /// <exception cref="GameLoadException">
        /// Thrown when <c>SYSTEM.CNF</c> is missing, the <c>BOOT2</c> key is
        /// absent, the executable cannot be found, or the ELF is malformed.
        /// </exception>
        public static uint BootFromDisc(byte[] isoData, PS2Emulator emulator)
        {
            if (isoData  == null) throw new ArgumentNullException(nameof(isoData));
            if (emulator == null) throw new ArgumentNullException(nameof(emulator));

            var cdvd = new Cdvd();
            cdvd.LoadImage(isoData);
            return BootFromCdvd(cdvd, emulator);
        }

        /// <summary>
        /// Boot a PS2 game from a seekable disc image stream.
        ///
        /// The stream must support seeking and remain open for the duration of
        /// this call.  Use this overload when loading large (>2 GB) disc images
        /// to avoid loading the entire image into memory.
        /// </summary>
        /// <param name="isoStream">
        /// Seekable stream over the disc image (2048-byte cooked ISO or 2352-byte raw BIN/CUE).
        /// </param>
        /// <param name="emulator">
        /// The <see cref="PS2Emulator"/> whose RAM and CPU will receive the
        /// loaded executable.
        /// </param>
        /// <returns>The ELF entry-point virtual address.</returns>
        /// <exception cref="GameLoadException">
        /// Thrown when <c>SYSTEM.CNF</c> is missing, the <c>BOOT2</c> key is
        /// absent, the executable cannot be found, or the ELF is malformed.
        /// </exception>
        public static uint BootFromDisc(Stream isoStream, PS2Emulator emulator)
        {
            if (isoStream == null) throw new ArgumentNullException(nameof(isoStream));
            if (emulator  == null) throw new ArgumentNullException(nameof(emulator));

            var cdvd = new Cdvd();
            cdvd.LoadFromStream(isoStream);
            return BootFromCdvd(cdvd, emulator);
        }

        /// <summary>
        /// Shared boot sequence: reads SYSTEM.CNF, parses BOOT2, loads the ELF.
        /// </summary>
        private static uint BootFromCdvd(Cdvd cdvd, PS2Emulator emulator)
        {
            // ── Step 1: find and read SYSTEM.CNF ─────────────────────────────

            Cdvd.IsoEntry? cnfEntry = cdvd.FindFile("SYSTEM.CNF");
            if (cnfEntry == null)
                throw new GameLoadException("SYSTEM.CNF not found on disc.");

            byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
            string cnfText  = Encoding.Latin1.GetString(cnfBytes);

            // ── Step 2: parse BOOT2 ───────────────────────────────────────────

            string? bootPath = ParseSystemCnf(cnfText);
            if (bootPath == null)
                throw new GameLoadException("BOOT2 entry not found in SYSTEM.CNF.");

            // ── Step 3: find the executable on the disc ───────────────────────

            Cdvd.IsoEntry? elfEntry = cdvd.FindFile(bootPath);
            if (elfEntry == null)
                throw new GameLoadException(
                    $"Boot executable '{bootPath}' not found on disc.");

            byte[] elfData = cdvd.ReadFile(elfEntry.Value);
            if (elfData.Length == 0)
                throw new GameLoadException(
                    $"Boot executable '{bootPath}' is empty.");

            // ── Step 4: load the ELF into the emulator ────────────────────────

            try
            {
                return ElfLoader.LoadIntoEmulator(elfData, emulator);
            }
            catch (ElfLoadException ex)
            {
                throw new GameLoadException(
                    $"Failed to load ELF '{bootPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Mount a disc image in the IOP's CDVD drive so that the PS2 BIOS can
        /// boot the game naturally from its reset vector (0xBFC00000).
        ///
        /// Unlike <see cref="BootFromDisc"/>, this method does <b>not</b> load
        /// the ELF executable or change the CPU program counter.  The correct
        /// call sequence when a real BIOS is available is:
        /// <code>
        ///   BiosLoader.Load(biosData, scheduler);   // resets both CPUs to 0xBFC00000
        ///   GameLoader.InsertDisc(isoStream, scheduler);  // mounts disc in CDVD
        ///   // … scheduler.RunSlice() loop …            // BIOS boots the game
        /// </code>
        ///
        /// The disc is mounted by assigning it to
        /// <see cref="IopMemoryBus.Cdvd"/>, which automatically wires the
        /// CDVD data-ready interrupt and the IOP DMAC channel-3 data provider.
        ///
        /// Reference: PCSX2 native BIOS boot — the IOP BIOS reads SYSTEM.CNF
        /// and the game ELF via the CDVD controller without any ELF pre-loading.
        /// </summary>
        /// <param name="isoStream">
        /// Seekable stream over the disc image.  The stream is kept open for the
        /// lifetime of the <see cref="Cdvd"/> instance; do not close it while
        /// the emulator is running.
        /// </param>
        /// <param name="scheduler">
        /// The <see cref="DualCpuScheduler"/> whose IOP will receive the disc.
        /// </param>
        /// <returns>
        /// The BOOT2 executable path from <c>SYSTEM.CNF</c> (e.g.
        /// <c>SLUS_210.40;1</c>), or an empty string if SYSTEM.CNF cannot be
        /// read.  This value is for informational/logging purposes only.
        /// </returns>
        public static string InsertDisc(Stream isoStream, DualCpuScheduler scheduler)
        {
            if (isoStream == null) throw new ArgumentNullException(nameof(isoStream));
            if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));

            var cdvd = new Cdvd();
            cdvd.LoadFromStream(isoStream);

            // Mount the disc in the IOP CDVD drive.  This wires the data-ready
            // interrupt and the DMAC channel-3 data provider automatically.
            scheduler.IOP.Bus.Cdvd = cdvd;
            // Also expose the disc type to the EE bus so the EE BIOS
            // disc-detection code (which reads Mechacon at 0x1F803204 via the
            // EE physical address space) can see the inserted disc.
            scheduler.EE.Bus.Cdvd = cdvd;

            // Read SYSTEM.CNF for the boot path (informational — no ELF loading).
            try
            {
                Cdvd.IsoEntry? cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                if (cnfEntry != null)
                {
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText = Encoding.Latin1.GetString(cnfBytes);
                    return ParseSystemCnf(cnfText) ?? string.Empty;
                }
            }
            catch
            {
                // Informational only — if SYSTEM.CNF cannot be read here, the
                // BIOS will deal with the error when it boots.
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Exception thrown by <see cref="GameLoader"/> when the disc or its
    /// boot executable cannot be loaded.
    /// </summary>
    public sealed class GameLoadException : Exception
    {
        public GameLoadException(string message) : base(message) { }

        public GameLoadException(string message, Exception inner)
            : base(message, inner) { }
    }
}
