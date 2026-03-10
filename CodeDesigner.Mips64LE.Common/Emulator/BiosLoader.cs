using System;
using System.Text;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE.Common.Emulator
{
    /// <summary>
    /// Loads a PS2 BIOS ROM image into the Emotion Engine and IOP memory buses
    /// and resets both CPUs to their power-on state (PC = 0xBFC00000).
    ///
    /// A genuine PS2 BIOS ROM is exactly 4 MB.  Its first bytes contain a ROM
    /// directory (<c>ROMDIR</c>) whose first entry is named <c>RESET</c>.
    /// A <c>ROMVER</c> entry (when present) holds a 15-byte ASCII string of the
    /// form <c>VVVVRX YYYYMMDD</c> that encodes the firmware version, region
    /// code and build date.
    ///
    /// Usage with <see cref="DualCpuScheduler"/>:
    /// <code>
    ///   byte[] romBytes = File.ReadAllBytes("SCPH-70012.bin");
    ///   var scheduler = new DualCpuScheduler();
    ///   BiosInfo info = BiosLoader.Load(romBytes, scheduler);
    ///   Console.WriteLine($"BIOS {info.Version} ({info.Region}) {info.Date}");
    ///   while (running)
    ///   {
    ///       scheduler.RunSlice();
    ///       scheduler.TickHardware();
    ///   }
    /// </code>
    ///
    /// Reference: PCSX2 <c>pcsx2/BiosThread.cpp</c>, <c>pcsx2/Bios.h</c>,
    /// and the PS2 Technical Reference Manual (Sony, 2000).
    /// </summary>
    public static class BiosLoader
    {
        // ── PS2 BIOS ROM constants ────────────────────────────────────────────

        /// <summary>Size of the PS2 BIOS ROM in bytes (4 MB).</summary>
        public const int BiosRomSize = 4 * 1024 * 1024;

        // ROMDIR entry layout (16 bytes total):
        //   bytes  0–9  : entry name, null-padded ASCII
        //   bytes 10–11 : extInfoSize (uint16 LE) — size of extended-info block
        //   bytes 12–15 : fileSize   (uint32 LE) — size of the module in ROM
        private const int RomDirEntrySize     = 16;
        private const int RomDirNameLen       = 10;
        private const string RomDirResetName  = "RESET";
        private const string RomDirRomVerName = "ROMVER";

        // ROMVER string is 15 bytes: "VVVVRX YYYYMMDD"
        // VVVV = 4-digit version, R = region letter, X = console class,
        // ' ' separator, YYYY = year, MM = month, DD = day.
        private const int RomVerLength = 15;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Validate and load a BIOS ROM image into a <see cref="DualCpuScheduler"/>
        /// and reset both CPUs to the power-on state.
        /// </summary>
        /// <param name="biosData">Raw bytes of the PS2 BIOS ROM (must be ≤ 4 MB).</param>
        /// <param name="scheduler">
        /// The scheduler whose EE and IOP emulators will receive the BIOS image.
        /// </param>
        /// <returns>
        /// A <see cref="BiosInfo"/> describing the firmware version, region and
        /// build date extracted from the <c>ROMVER</c> directory entry.
        /// </returns>
        /// <exception cref="BiosLoadException">
        /// Thrown when <paramref name="biosData"/> is too large, does not start
        /// with a valid <c>ROMDIR</c>, or the first directory entry is not
        /// <c>RESET</c>.
        /// </exception>
        public static BiosInfo Load(ReadOnlySpan<byte> biosData, DualCpuScheduler scheduler)
        {
            if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
            BiosInfo info = Validate(biosData);
            CopyInto(biosData, scheduler.EE.Bus.Bios);
            CopyInto(biosData, scheduler.IOP.Bus.Bios);
            scheduler.SetBiosData(biosData);
            scheduler.Reset();
            return info;
        }

        /// <summary>
        /// Validate and load a BIOS ROM image directly into an EE emulator and an
        /// IOP emulator, then reset both CPUs.
        /// </summary>
        /// <param name="biosData">Raw bytes of the PS2 BIOS ROM (must be ≤ 4 MB).</param>
        /// <param name="eeEmulator">Emotion Engine emulator to receive the image.</param>
        /// <param name="iopEmulator">IOP emulator to receive the image.</param>
        /// <returns>
        /// A <see cref="BiosInfo"/> describing the firmware version, region and
        /// build date.
        /// </returns>
        /// <exception cref="BiosLoadException">
        /// Thrown on validation failure (see <see cref="Validate"/>).
        /// </exception>
        public static BiosInfo Load(ReadOnlySpan<byte> biosData,
                                    PS2Emulator eeEmulator,
                                    IopEmulator iopEmulator)
        {
            if (eeEmulator  == null) throw new ArgumentNullException(nameof(eeEmulator));
            if (iopEmulator == null) throw new ArgumentNullException(nameof(iopEmulator));
            BiosInfo info = Validate(biosData);
            CopyInto(biosData, eeEmulator.Bus.Bios);
            CopyInto(biosData, iopEmulator.Bus.Bios);
            eeEmulator.Reset();
            iopEmulator.Reset();
            return info;
        }

        /// <summary>
        /// Validate a BIOS ROM image and return its <see cref="BiosInfo"/> without
        /// modifying any emulator state.
        ///
        /// Validation rules (matching PCSX2 <c>BiosThread.cpp</c>):
        /// <list type="number">
        ///   <item>Image must not be empty and must not exceed 4 MB.</item>
        ///   <item>A valid ROMDIR must exist somewhere in the ROM (found by scanning
        ///     for a 16-byte-aligned entry whose name is <c>RESET</c>).  In real PS2
        ///     BIOS images the ROMDIR does not start at offset 0; it is typically
        ///     located around 0x2700–0x2740.</item>
        /// </list>
        /// </summary>
        /// <param name="biosData">Raw bytes of the PS2 BIOS ROM.</param>
        /// <returns>Version information extracted from the ROMVER entry, or a
        /// default <see cref="BiosInfo"/> when the entry is absent.</returns>
        /// <exception cref="BiosLoadException">
        /// Thrown when any validation rule is violated.
        /// </exception>
        public static BiosInfo Validate(ReadOnlySpan<byte> biosData)
        {
            if (biosData.Length == 0)
                throw new BiosLoadException("BIOS image is empty.");

            if (biosData.Length > BiosRomSize)
                throw new BiosLoadException(
                    $"BIOS image is too large: {biosData.Length} bytes " +
                    $"(maximum is {BiosRomSize} bytes / 4 MB).");

            // Need at least one complete ROMDIR entry (16 bytes).
            if (biosData.Length < RomDirEntrySize)
                throw new BiosLoadException(
                    $"BIOS image is too small ({biosData.Length} bytes) to " +
                    "contain a valid ROM directory.");

            // Scan for the ROMDIR at any 16-byte-aligned offset (PCSX2 approach).
            // In genuine PS2 BIOS ROMs the directory is NOT at offset 0; it
            // typically sits around 0x2700–0x2740 after the initial boot code.
            int romDirOffset = FindRomDirOffset(biosData);
            if (romDirOffset < 0)
                throw new BiosLoadException(
                    "Invalid BIOS ROM: no valid ROM directory (RESET entry) found.");

            // Walk the directory to find the ROMVER entry and compute module offsets.
            BiosInfo info = ReadRomDir(biosData, romDirOffset);
            return info;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Scan the BIOS image at 16-byte-aligned offsets for a ROMDIR entry
        /// whose name is <c>RESET</c>.  This matches the PCSX2 scanning strategy
        /// because genuine PS2 BIOS ROMs place the ROMDIR directory after an
        /// initial boot-code block (typically around offset 0x2700–0x2740) rather
        /// than at the very beginning of the image.
        /// </summary>
        /// <returns>
        /// The byte offset of the first ROMDIR entry if found; -1 otherwise.
        /// </returns>
        private static int FindRomDirOffset(ReadOnlySpan<byte> bios)
        {
            for (int i = 0; i + RomDirEntrySize <= bios.Length; i += RomDirEntrySize)
            {
                string name = ReadRomDirName(bios, i);
                if (name.Equals(RomDirResetName, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Walk the ROMDIR table (starting at <paramref name="romDirOffset"/>) and
        /// extract BIOS version/region/date from the ROMVER module.
        ///
        /// PS2 BIOS ROM layout:
        /// <list type="bullet">
        ///   <item>Module <em>data</em> is packed sequentially from the <em>start</em>
        ///     of the ROM image (offset 0), each rounded up to a 16-byte boundary.
        ///     The first module (RESET) IS the ROMDIR table itself.</item>
        ///   <item>The ROMDIR <em>entries</em> start at <paramref name="romDirOffset"/>
        ///     and are read sequentially until an empty-name terminator is found.</item>
        /// </list>
        /// </summary>
        private static BiosInfo ReadRomDir(ReadOnlySpan<byte> bios, int romDirOffset)
        {
            // Module data starts at offset 0 in the ROM image; we accumulate
            // the data offset as we walk the directory entries.
            uint moduleOffset = 0;

            for (int i = romDirOffset; i + RomDirEntrySize <= bios.Length; i += RomDirEntrySize)
            {
                string name     = ReadRomDirName(bios, i);
                uint   fileSize = ReadUInt32LE(bios, i + 12);

                // Empty name signals the end of the directory.
                if (name.Length == 0)
                    break;

                if (name.Equals(RomDirRomVerName, StringComparison.Ordinal))
                {
                    if (moduleOffset + RomVerLength <= (uint)bios.Length && fileSize >= RomVerLength)
                    {
                        string ver = Encoding.ASCII.GetString(
                            bios.Slice((int)moduleOffset, RomVerLength));
                        return ParseRomVer(ver);
                    }
                    break;
                }

                // Advance module data pointer by fileSize rounded to a 16-byte boundary.
                moduleOffset += (fileSize + 15u) & ~15u;
            }

            // ROMVER not found or too small — return an unknown placeholder.
            return new BiosInfo("Unknown", "Unknown", "Unknown");
        }

        /// <summary>
        /// Parse a 15-byte ROMVER string into a <see cref="BiosInfo"/>.
        ///
        /// Format: <c>VVVVRX YYYYMMDD</c>
        /// <list type="bullet">
        ///   <item><c>VVVV</c> — 4-digit version (e.g. "0160" → v1.60)</item>
        ///   <item><c>R</c>    — region letter: J=Japan, A=USA, E=Europe, C=China, H=HK/Asia</item>
        ///   <item><c>X</c>    — console class (C=consumer, D=dev kit, T=tool)</item>
        ///   <item><c>YYYYMMDD</c> — build date (after the space at position 6)</item>
        /// </list>
        /// </summary>
        private static BiosInfo ParseRomVer(string ver)
        {
            // ver is exactly 15 characters: positions 0–3 = version digits,
            // 4 = region, 5 = class, 6 = space, 7–14 = date (8 chars).
            if (ver.Length < RomVerLength)
                return new BiosInfo(ver.Trim(), "Unknown", "Unknown");

            // Version: insert decimal point between digit 2 and 3, e.g. "0160" → "1.60"
            string rawVer  = ver[..4];
            string version = $"{int.Parse(rawVer[..2])}.{rawVer[2..]}";

            string regionCode = ver[4..5];
            string region = regionCode switch
            {
                "J" => "Japan",
                "A" => "USA",
                "E" => "Europe",
                "C" => "China",
                "H" => "HK/Asia",
                _   => regionCode
            };

            // Date at positions 7–14: YYYYMMDD
            string rawDate = ver[6..].Trim();
            string date    = rawDate.Length == 8
                ? $"{rawDate[..4]}-{rawDate[4..6]}-{rawDate[6..8]}"
                : rawDate;

            return new BiosInfo(version, region, date);
        }

        /// <summary>Read a null-padded 10-byte ASCII name from a ROMDIR entry.</summary>
        private static string ReadRomDirName(ReadOnlySpan<byte> bios, int entryOffset)
        {
            int nameEnd = 0;
            while (nameEnd < RomDirNameLen && entryOffset + nameEnd < bios.Length
                   && bios[entryOffset + nameEnd] != 0)
                nameEnd++;
            return Encoding.ASCII.GetString(bios.Slice(entryOffset, nameEnd));
        }

        /// <summary>Copy BIOS data into a destination buffer, zero-filling any remainder.</summary>
        private static void CopyInto(ReadOnlySpan<byte> src, byte[] dst)
        {
            int copyLen = Math.Min(src.Length, dst.Length);
            src[..copyLen].CopyTo(dst.AsSpan(0, copyLen));
            // Zero-fill any remaining space in the 4 MB buffer.
            if (copyLen < dst.Length)
                Array.Clear(dst, copyLen, dst.Length - copyLen);
        }

        private static uint ReadUInt32LE(ReadOnlySpan<byte> data, int offset) =>
            (uint)(data[offset]
                 | (data[offset + 1] << 8)
                 | (data[offset + 2] << 16)
                 | (data[offset + 3] << 24));
    }

    // ── Supporting types ──────────────────────────────────────────────────────

    /// <summary>
    /// Firmware metadata extracted from the <c>ROMVER</c> ROMDIR entry.
    /// </summary>
    public sealed class BiosInfo
    {
        /// <summary>
        /// Human-readable version string, e.g. <c>"1.60"</c>.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Console region, e.g. <c>"Japan"</c>, <c>"USA"</c>, <c>"Europe"</c>.
        /// </summary>
        public string Region { get; }

        /// <summary>
        /// Build date in ISO-8601 format (<c>YYYY-MM-DD</c>), e.g. <c>"2002-04-26"</c>.
        /// </summary>
        public string Date { get; }

        public BiosInfo(string version, string region, string date)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Region  = region  ?? throw new ArgumentNullException(nameof(region));
            Date    = date    ?? throw new ArgumentNullException(nameof(date));
        }

        public override string ToString() => $"PS2 BIOS {Version} ({Region}) {Date}";
    }

    /// <summary>
    /// Exception thrown by <see cref="BiosLoader"/> when the BIOS image is
    /// invalid or cannot be loaded.
    /// </summary>
    public sealed class BiosLoadException : Exception
    {
        public BiosLoadException(string message) : base(message) { }
    }
}
