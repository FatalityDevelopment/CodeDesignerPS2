using System;
using System.Collections.Generic;
using System.Text;

namespace CodeDesigner.Mips64LE.Common.Emulator.IOP
{
    /// <summary>
    /// Emulates the PS2 CDVD controller and provides an ISO-9660 disc image reader.
    ///
    /// The CDVD controller is an IOP peripheral memory-mapped at
    /// 0x1F402000–0x1F40201F (32 byte window, byte-granular registers).
    ///
    /// N-commands (normal disc operations, e.g. ReadCd) are issued by writing
    /// parameter bytes one at a time to offset 0x00, then writing the command
    /// code to offset 0x04.  When complete, <see cref="DataReady"/> is raised
    /// and INTR_STAT (offset 0x08) bit 2 is set.  Sector data accumulated in the
    /// internal buffer is then supplied word-by-word to IOP DMAC channel 3 via
    /// <see cref="ReadNextDmaWord"/>.
    ///
    /// ISO-9660 support:
    ///   Call <see cref="LoadImage"/> to attach a raw sector image (2048 bytes/sector,
    ///   no sub-channel data).  <see cref="FindFile"/> walks the Primary Volume
    ///   Descriptor tree to locate a file by path; <see cref="ReadFile(IsoEntry)"/>
    ///   returns the file's raw bytes.
    ///
    /// N-command 0x06 (ReadCd) parameter layout
    /// (written one byte at a time to offset 0x00 before issuing the command):
    ///   Byte 0–3: LBA (little-endian uint32)
    ///   Byte 4:   Sector count
    ///   Byte 5:   Reserved / media type
    ///   Byte 6:   Transfer type
    ///
    /// Reference: PCSX2 pcsx2/CDVD/CDVD.cpp, pcsx2/IopMem.cpp.
    /// </summary>
    public sealed class Cdvd
    {
        // ── ISO-9660 constants ────────────────────────────────────────────────

        /// <summary>Logical sector size for ISO-9660 (2048 bytes).</summary>
        public const int SectorSize = 2048;

        /// <summary>Logical Block Address of the Primary Volume Descriptor.</summary>
        private const int PvdLba = 16;

        // ── CDVD register window ──────────────────────────────────────────────

        /// <summary>Base address of the CDVD register window in IOP address space.</summary>
        public const uint RegBase = 0x1F402000u;

        /// <summary>Size of the CDVD register window (32 bytes).</summary>
        public const uint RegSize = 0x20u;

        // Byte offsets within the register window
        private const uint REG_DATA    = 0x00u; // N-command parameter write port
        private const uint REG_NCMD    = 0x04u; // N command register
        private const uint REG_NREADY  = 0x05u; // N ready/busy (0 = ready)
        private const uint REG_NDOUT   = 0x06u; // N data out (status responses)
        private const uint REG_BREAK   = 0x07u; // Break / cancel
        private const uint REG_ISTAT   = 0x08u; // Interrupt status (W1C)
        private const uint REG_STATUS  = 0x09u; // Drive status
        private const uint REG_TRAY    = 0x0Au; // Tray state
        private const uint REG_CRT     = 0x0Bu; // Current disc type
        private const uint REG_SDATA   = 0x0Fu; // S-command data output
        private const uint REG_SCMD    = 0x17u; // S command register
        private const uint REG_SSTAT   = 0x18u; // S-command status
        private const uint REG_SPARAM  = 0x1Fu; // S-command parameter

        // ── N-command codes ───────────────────────────────────────────────────

        /// <summary>ReadCd N-command code (read one or more sectors by LBA).</summary>
        public const byte NCMD_READ_CD = 0x06;

        // ── Disc-type constants (CRT register) ────────────────────────────────

        /// <summary>No disc inserted.</summary>
        public const byte DISC_NO_DISC = 0x00;

        /// <summary>PS2 CD-ROM disc.</summary>
        public const byte DISC_PS2_CD  = 0x13;

        /// <summary>PS2 DVD-ROM disc.</summary>
        public const byte DISC_PS2_DVD = 0x14;

        // ── INTR_STAT bits ────────────────────────────────────────────────────
        private const byte INTR_DATA_READY = 0x04; // bit 2: N-command data ready

        // ── State ─────────────────────────────────────────────────────────────

        private byte[] _iso = Array.Empty<byte>();

        // N-command parameter buffer (written byte-by-byte to REG_DATA before NCMD)
        private readonly byte[] _nParamBuf = new byte[16];
        private int  _nParamPos;
        private byte _nIntrStat;

        // Sector data buffer: filled by ExecuteNCommand, consumed by ReadNextDmaWord
        private byte[] _sectorData       = Array.Empty<byte>();
        private int    _sectorDataOffset;

        // S-command output buffer
        private readonly byte[] _sCmdOutput = new byte[32];
        private int _sCmdOutputPos;
        private int _sCmdOutputLen;

        // ── Public properties ─────────────────────────────────────────────────

        /// <summary>True when a disc image is loaded.</summary>
        public bool HasDisc => _iso.Length > 0;

        /// <summary>Current disc type byte (value of the CRT register).</summary>
        public byte DiscType { get; private set; } = DISC_NO_DISC;

        /// <summary>
        /// Raised when an N-command completes (data is ready in the sector buffer).
        /// <see cref="IopMemoryBus"/> wires this to assert IOP INTC bit 2.
        /// </summary>
        public event EventHandler? DataReady;

        // ── ISO image loading ─────────────────────────────────────────────────

        /// <summary>
        /// Load a raw ISO-9660 disc image (2048-byte sectors, no sub-channel data).
        ///
        /// After loading, <see cref="HasDisc"/> is <c>true</c> and
        /// <see cref="DiscType"/> returns <see cref="DISC_PS2_DVD"/>.
        /// Pass <c>null</c> or an empty array to eject the disc.
        /// </summary>
        /// <param name="iso">Raw disc image byte array, or <c>null</c> to eject.</param>
        public void LoadImage(byte[]? iso)
        {
            _iso              = iso ?? Array.Empty<byte>();
            DiscType          = _iso.Length > 0 ? DISC_PS2_DVD : DISC_NO_DISC;
            _nParamPos        = 0;
            _nIntrStat        = 0;
            _sectorData       = Array.Empty<byte>();
            _sectorDataOffset = 0;
        }

        // ── CDVD register I/O ─────────────────────────────────────────────────

        /// <summary>
        /// Read one byte from a CDVD register at <paramref name="offset"/>
        /// (relative to <see cref="RegBase"/>).
        /// </summary>
        public byte ReadRegister(uint offset)
        {
            return offset switch
            {
                REG_NREADY => 0,                          // 0 = ready (no pending command)
                REG_ISTAT  => _nIntrStat,
                REG_STATUS => HasDisc ? (byte)0x06 : (byte)0x00, // 0x06 = paused/disc present
                REG_TRAY   => HasDisc ? (byte)0x00 : (byte)0x01, // 0=closed, 1=open
                REG_CRT    => DiscType,
                REG_SDATA  => _sCmdOutputPos < _sCmdOutputLen
                                  ? _sCmdOutput[_sCmdOutputPos++]
                                  : (byte)0,
                REG_SSTAT  => (byte)0x40,                 // S-command ready
                _          => 0
            };
        }

        /// <summary>
        /// Write one byte to a CDVD register at <paramref name="offset"/>
        /// (relative to <see cref="RegBase"/>).
        /// </summary>
        public void WriteRegister(uint offset, byte value)
        {
            switch (offset)
            {
                case REG_DATA:
                    // Accumulate N-command parameter bytes
                    if (_nParamPos < _nParamBuf.Length)
                        _nParamBuf[_nParamPos++] = value;
                    break;

                case REG_NCMD:
                    // Execute N-command, then clear the parameter accumulator
                    ExecuteNCommand(value);
                    _nParamPos = 0;
                    break;

                case REG_BREAK:
                    // Cancel current operation
                    _nParamPos = 0;
                    _nIntrStat = 0;
                    break;

                case REG_ISTAT:
                    // W1C: writing 1s clears the corresponding interrupt-status bits
                    _nIntrStat &= (byte)~value;
                    break;

                case REG_SCMD:
                    ExecuteSCommand(value);
                    break;
            }
        }

        // ── DMA data output ───────────────────────────────────────────────────

        /// <summary>
        /// Read the next 32-bit little-endian word from the sector data buffer.
        ///
        /// Called once per word by IOP DMAC channel 3 (device → RAM direction)
        /// via the <c>DeviceDataProvider</c> callback set in
        /// <see cref="IopMemoryBus"/>.  Returns 0 when the buffer is exhausted.
        /// </summary>
        public uint ReadNextDmaWord()
        {
            if (_sectorDataOffset + 4 > _sectorData.Length)
                return 0u;

            uint value = (uint)(_sectorData[_sectorDataOffset]
                              | (_sectorData[_sectorDataOffset + 1] << 8)
                              | (_sectorData[_sectorDataOffset + 2] << 16)
                              | (_sectorData[_sectorDataOffset + 3] << 24));
            _sectorDataOffset += 4;
            return value;
        }

        // ── N-command execution ───────────────────────────────────────────────

        private void ExecuteNCommand(byte cmd)
        {
            if (cmd == NCMD_READ_CD && _nParamPos >= 5)
            {
                uint lba   = (uint)( _nParamBuf[0]
                                   | (_nParamBuf[1] << 8)
                                   | (_nParamBuf[2] << 16)
                                   | (_nParamBuf[3] << 24));
                uint count = _nParamBuf[4];
                if (count == 0) count = 1;
                LoadSectorBuffer(lba, count);
            }

            _nIntrStat |= INTR_DATA_READY;
            DataReady?.Invoke(this, EventArgs.Empty);
        }

        private void LoadSectorBuffer(uint lba, uint count)
        {
            int totalBytes    = (int)(count * SectorSize);
            _sectorData       = new byte[totalBytes];
            _sectorDataOffset = 0;

            for (uint i = 0; i < count; i++)
            {
                long srcOffset = (long)(lba + i) * SectorSize;
                int  dstOffset = (int)(i * SectorSize);
                if (srcOffset >= 0 && srcOffset + SectorSize <= _iso.Length)
                    Array.Copy(_iso, srcOffset, _sectorData, dstOffset, SectorSize);
                // else: destination bytes remain zero (buffer was initialised to 0)
            }
        }

        // ── S-command execution ───────────────────────────────────────────────

        private void ExecuteSCommand(byte cmd)
        {
            // Stub: S-commands (disc info, mechanical) return empty results for now.
            _sCmdOutputPos = 0;
            _sCmdOutputLen = 0;
        }

        // ── ISO-9660 file-system reader ───────────────────────────────────────

        /// <summary>
        /// Represents a file or directory entry from the ISO-9660 file system.
        /// </summary>
        public readonly struct IsoEntry
        {
            /// <summary>File or directory name (the version suffix ";1" is retained).</summary>
            public string Name { get; init; }

            /// <summary>Logical Block Address (sector number) of the data extent.</summary>
            public uint Lba { get; init; }

            /// <summary>Size of the data extent in bytes.</summary>
            public uint Size { get; init; }

            /// <summary>True when this entry is a directory (File Flags bit 1 set).</summary>
            public bool IsDirectory { get; init; }
        }

        /// <summary>
        /// Locate a file or directory in the ISO-9660 file system by path.
        ///
        /// Path components are separated by <c>'/'</c> or <c>'\'</c>; a leading
        /// separator is optional.  Component names are matched case-insensitively
        /// and the ISO-9660 version suffix <c>";1"</c> is stripped before comparison.
        /// </summary>
        /// <param name="path">
        /// Path such as <c>"SYSTEM.CNF"</c>, <c>"/SLPM_550.01;1"</c>, or
        /// <c>"/PSP_GAME/PARAM.SFO"</c>.
        /// </param>
        /// <returns>
        /// The matching <see cref="IsoEntry"/>, or <c>null</c> when the entry is
        /// not found or no disc image is loaded.
        /// </returns>
        public IsoEntry? FindFile(string path)
        {
            if (!HasDisc || string.IsNullOrEmpty(path))
                return null;

            if (!TryReadPvd(out uint rootLba, out uint rootSize))
                return null;

            string[] parts = path.Split(new[] { '/', '\\' },
                                        StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            uint dirLba  = rootLba;
            uint dirSize = rootSize;

            for (int p = 0; p < parts.Length; p++)
            {
                string component = StripVersionSuffix(parts[p]).ToUpperInvariant();
                bool   isLast    = p == parts.Length - 1;
                bool   found     = false;

                foreach (IsoEntry entry in ListDirectory(dirLba, dirSize))
                {
                    string entryName = StripVersionSuffix(entry.Name).ToUpperInvariant();
                    if (entryName != component)
                        continue;

                    if (isLast)
                        return entry;

                    if (!entry.IsDirectory)
                        return null; // path component is not a directory

                    dirLba  = entry.Lba;
                    dirSize = entry.Size;
                    found   = true;
                    break;
                }

                if (!found)
                    return null;
            }

            return null;
        }

        /// <summary>
        /// Read the raw bytes of the file described by <paramref name="entry"/>
        /// into a new array.  Returns an empty array when the extent is out of
        /// range or no disc image is loaded.
        /// </summary>
        public byte[] ReadFile(IsoEntry entry)
        {
            if (!HasDisc || entry.Size == 0)
                return Array.Empty<byte>();

            long srcOffset = (long)entry.Lba * SectorSize;
            if (srcOffset < 0 || srcOffset + (long)entry.Size > _iso.Length)
                return Array.Empty<byte>();

            byte[] data = new byte[entry.Size];
            Array.Copy(_iso, srcOffset, data, 0, entry.Size);
            return data;
        }

        /// <summary>
        /// Copy raw sector data from the disc image into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="lba">Starting logical block address.</param>
        /// <param name="count">Number of sectors to read.</param>
        /// <param name="buffer">Destination buffer (must be large enough).</param>
        /// <param name="bufferOffset">Byte offset within <paramref name="buffer"/>.</param>
        public void ReadSectors(uint lba, uint count, byte[] buffer, int bufferOffset = 0)
        {
            for (uint i = 0; i < count; i++)
            {
                long srcOffset = (long)(lba + i) * SectorSize;
                int  dstOffset = bufferOffset + (int)(i * SectorSize);
                if (srcOffset >= 0 && srcOffset + SectorSize <= _iso.Length
                    && dstOffset + SectorSize <= buffer.Length)
                {
                    Array.Copy(_iso, srcOffset, buffer, dstOffset, SectorSize);
                }
            }
        }

        // ── ISO-9660 internal helpers ─────────────────────────────────────────

        private bool TryReadPvd(out uint rootLba, out uint rootSize)
        {
            rootLba  = 0;
            rootSize = 0;

            long pvdOffset = (long)PvdLba * SectorSize;
            if (pvdOffset + SectorSize > _iso.Length)
                return false;

            // Verify PVD signature: type=1, identifier="CD001", version=1
            if (_iso[pvdOffset + 0] != 1)
                return false;
            if (_iso[pvdOffset + 1] != 'C' || _iso[pvdOffset + 2] != 'D'
             || _iso[pvdOffset + 3] != '0' || _iso[pvdOffset + 4] != '0'
             || _iso[pvdOffset + 5] != '1')
                return false;

            // Root Directory Record at PVD byte offset 156
            int rrBase = (int)pvdOffset + 156;
            rootLba  = ReadU32LE(_iso, rrBase + 2);
            rootSize = ReadU32LE(_iso, rrBase + 10);
            return true;
        }

        private IEnumerable<IsoEntry> ListDirectory(uint dirLba, uint dirSize)
        {
            if (dirSize == 0) yield break;

            uint sectorCount = (dirSize + (uint)SectorSize - 1) / (uint)SectorSize;
            byte[] dirData   = new byte[sectorCount * SectorSize];
            ReadSectors(dirLba, sectorCount, dirData);

            int pos = 0;
            int end = (int)Math.Min(dirSize, (uint)dirData.Length);

            while (pos < end)
            {
                int recordLen = dirData[pos];
                if (recordLen == 0)
                {
                    // Zero padding at sector boundary — skip to next sector
                    int nextSector = ((pos / SectorSize) + 1) * SectorSize;
                    if (nextSector >= end) break;
                    pos = nextSector;
                    continue;
                }

                if (recordLen < 34 || pos + recordLen > end)
                    break;

                int nameLen = dirData[pos + 32];

                // Skip the special "." (id=0x00) and ".." (id=0x01) entries
                if (nameLen == 1 && dirData[pos + 33] <= 0x01)
                {
                    pos += recordLen;
                    continue;
                }

                if (nameLen > 0)
                {
                    uint   entryLba  = ReadU32LE(dirData, pos + 2);
                    uint   entrySize = ReadU32LE(dirData, pos + 10);
                    byte   flags     = dirData[pos + 25];
                    string name      = Encoding.ASCII.GetString(dirData, pos + 33, nameLen);

                    yield return new IsoEntry
                    {
                        Name        = name,
                        Lba         = entryLba,
                        Size        = entrySize,
                        IsDirectory = (flags & 0x02) != 0
                    };
                }

                pos += recordLen;
            }
        }

        private static string StripVersionSuffix(string name)
        {
            int semi = name.IndexOf(';');
            return semi >= 0 ? name[..semi] : name;
        }

        private static uint ReadU32LE(byte[] buf, int offset)
        {
            if (offset + 4 > buf.Length) return 0u;
            return (uint)(buf[offset]
                        | (buf[offset + 1] << 8)
                        | (buf[offset + 2] << 16)
                        | (buf[offset + 3] << 24));
        }
    }
}
