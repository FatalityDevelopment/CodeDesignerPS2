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
    ///   Call <see cref="LoadImage"/> to attach a disc image.  Images may be either
    ///   "cooked" ISO-9660 (2048 bytes/sector) or raw BIN/CUE images
    ///   (2352 bytes/sector with a 24-byte sector header).  The format is
    ///   auto-detected from the sync pattern in the first sector.
    ///   <see cref="FindFile"/> walks the Primary Volume Descriptor tree to locate
    ///   a file by path; <see cref="ReadFile(IsoEntry)"/> returns the file's raw bytes.
    ///
    /// N-command 0x06 (ReadCd) parameter layout:
    ///   Byte 0–3: LBA (little-endian uint32)
    ///   Byte 4:   Sector count
    ///   Byte 5:   Reserved / media type
    ///   Byte 6:   Transfer type
    ///
    /// N-command 0x07 (ReadCDDA) parameter layout (same as ReadCd):
    ///   Byte 0–3: LBA (little-endian uint32)
    ///   Byte 4:   Sector count
    ///   In raw BIN images the 2352-byte raw sector is returned as-is (16-bit
    ///   stereo PCM at 44100 Hz); in cooked ISO images silence is returned.
    ///
    /// N-command 0x08 (ReadDvd) parameter layout (same header as ReadCd):
    ///   Byte 0–3: LBA (little-endian uint32)
    ///   Byte 4:   Sector count
    ///   DVD sectors are always 2048-byte user data (no sub-channel, no raw mode).
    ///   Reference: PCSX2 pcsx2/CDVD/CDVD.cpp cdvdDvdRead().
    ///
    /// N-command 0x09 (GetTOC):
    ///   No parameters.  Returns a 1024-byte Table of Contents buffer.
    ///   For DVD images the first 16 bytes describe layer 0 (book type, disc
    ///   dimensions, start/end PSN).  Reference: PCSX2 cdvdGetTOC().
    ///
    /// Sub-channel Q data (N-command 0x06 sector type 4):
    ///   When params[5] = 4 the response for each sector is extended by 16 bytes
    ///   of sub-channel data: 12 bytes of synthesised sub-Q (ctrl/adr, track,
    ///   index, relative and absolute MSF in BCD) followed by 4 reserved bytes.
    ///   Reference: PCSX2 pcsx2/CDVD/CDVD.cpp cdvdGetSector() CdRawSectorWithOC.
    ///
    /// S-command support:
    ///   0x00 GetMedia  – returns single disc-type byte.
    ///   0x05 GetDiscType – returns disc-type byte (same as CRT register).
    ///   0x12 GetLayerInfo – returns 4-byte layer-0 end-PSN for dual-layer DVDs
    ///   (returns zeros for single-layer discs).
    ///
    /// Reference: PCSX2 pcsx2/CDVD/CDVD.cpp, pcsx2/IopMem.cpp.
    /// </summary>
    public sealed class Cdvd
    {
        // ── ISO-9660 constants ────────────────────────────────────────────────

        /// <summary>Logical sector size for ISO-9660 (2048 bytes).</summary>
        public const int SectorSize = 2048;

        /// <summary>Raw sector size for BIN/CUE images (2352 bytes).</summary>
        public const int RawSectorSize = 2352;

        /// <summary>Byte offset of user data within a raw Mode-1/Mode-2 sector.</summary>
        public const int RawSectorHeaderSize = 24;

        /// <summary>Logical Block Address of the Primary Volume Descriptor.</summary>
        private const int PvdLba = 16;

        // ── CDVD register window ──────────────────────────────────────────────

        /// <summary>Base address of the CDVD register window in IOP address space.</summary>
        public const uint RegBase = 0x1F402000u;

        /// <summary>Size of the CDVD register window (32 bytes).</summary>
        public const uint RegSize = 0x20u;

        // Byte offsets within the register window
        private const uint REG_DATA       = 0x00u; // N-command parameter write port
        private const uint REG_NCMD       = 0x04u; // N command register
        private const uint REG_NREADY     = 0x05u; // N ready/busy (0 = ready)
        private const uint REG_NDOUT      = 0x06u; // N data out (status responses)
        private const uint REG_BREAK      = 0x07u; // Break / cancel
        private const uint REG_ISTAT      = 0x08u; // Interrupt status (W1C)
        private const uint REG_STATUS     = 0x09u; // Drive status
        private const uint REG_TRAY       = 0x0Au; // Tray state
        private const uint REG_CRT        = 0x0Bu; // Current disc type
        private const uint REG_NDATAOUT   = 0x0Fu; // N-command data output (PCSX2 cdvdRead0F)
        private const uint REG_SCMD_EXEC  = 0x16u; // S command execute register (WRITE = cdvdWrite16 in PCSX2)
        private const uint REG_SCMD_PARAM = 0x17u; // S command parameter byte (WRITE = cdvdWrite17; READ = SREADY/sDataIn)
        private const uint REG_SDATAOUT   = 0x18u; // S-command data output (READ = cdvdRead18, returns SCMDResultBuff[pos++])

        // ── N-command codes ───────────────────────────────────────────────────

        /// <summary>ReadCd N-command code (read one or more data sectors by LBA).</summary>
        public const byte NCMD_READ_CD   = 0x06;

        /// <summary>ReadCDDA N-command code (read one or more CD audio sectors by LBA).</summary>
        public const byte NCMD_READ_CDDA = 0x07;

        /// <summary>ReadDvd N-command code (read one or more DVD data sectors by LBA).</summary>
        public const byte NCMD_READ_DVD  = 0x08;

        /// <summary>GetTOC N-command code (returns a 1024-byte Table of Contents buffer).</summary>
        public const byte NCMD_GET_TOC   = 0x09;

        /// <summary>Size of the Table of Contents buffer returned by NCMD_GET_TOC (1024 bytes).</summary>
        public const int TocSize = 1024;

        /// <summary>
        /// Number of sub-channel bytes appended per sector when sectorType = 4
        /// (12-byte sub-Q followed by 4 reserved bytes = 16 total).
        /// Reference: PCSX2 CdRawSectorWithOC mode (2352 + 16 = 2368 bytes/sector).
        /// </summary>
        public const int SubChannelSize = 16;

        // ── Disc-type constants (CRT register) ────────────────────────────────

        /// <summary>No disc inserted.</summary>
        public const byte DISC_NO_DISC = 0x00;

        /// <summary>PS2 CD-ROM disc.</summary>
        public const byte DISC_PS2_CD  = 0x13;

        /// <summary>PS2 DVD-ROM disc.</summary>
        public const byte DISC_PS2_DVD = 0x14;

        // ── N-command status (REG_NREADY / 0x05) bit-flags ───────────────────
        // Reference: PS2Tek https://psi-rockin.github.io/ps2tek/#cdvdioports
        //   bit 0: Error        bit 2: DEV9 connected   bit 3: MECHA_INIT
        //   bit 5: Power-off    bit 6: Drive ready       bit 7: Busy
        //
        // PCSX2 cdvdUpdateReady() always ORs MECHA_INIT | DEV9CON into cdvd.Ready
        // ("We don't really use the MECHA bit but Cold Fear will kick back to the
        //  BIOS if it's not set").  The IOP BIOS polls bit 3 (MECHA_INIT) at
        // 0xB0C4/0xB0EC; if it is never set the BIOS times out and skips SIF
        // initialisation, leaving I_MASK bit 1 (SBUS) disabled and the IOP
        // permanently unable to receive EE commands.

        /// <summary>Drive ready, not busy: same as PCSX2 CDVD_DRIVE_READY.</summary>
        private const byte DRIVE_READY     = 0x40;

        /// <summary>Always-set MECHA_INIT flag: CDVD_DRIVE_MECHA_INIT in PCSX2.</summary>
        private const byte DRIVE_MECHA_INIT = 0x08;

        /// <summary>Always-set DEV9 connection flag: CDVD_DRIVE_DEV9CON in PCSX2.</summary>
        private const byte DRIVE_DEV9CON   = 0x04;

        /// <summary>Drive busy executing N-command: CDVD_DRIVE_BUSY in PCSX2.</summary>
        private const byte DRIVE_BUSY      = 0x80;

        // ── CDVD_STATUS values (REG_STATUS / 0x09) — from PCSX2 cdvd.h ──────
        // These match PCSX2's enum exactly:
        //   CDVD_STATUS_STOP  = 0x00  (no disc / stopped)
        //   CDVD_STATUS_READ  = 0x06  (executing N-command)
        //   CDVD_STATUS_PAUSE = 0x0A  (disc present, idle/paused)
        private const byte STATUS_STOP  = 0x00;
        private const byte STATUS_READ  = 0x06;
        private const byte STATUS_PAUSE = 0x0A; // PCSX2 CDVD_STATUS_PAUSE

        // ── INTR_STAT bits ────────────────────────────────────────────────────
        private const byte INTR_DISC_DETECT = 0x01; // bit 0: disc-detection / disc-change (PCSX2 cdvdDetectDisk: cdvd.IntrStat |= 1)
        private const byte INTR_DATA_READY  = 0x04; // bit 2: N-command data ready

        // ── State ─────────────────────────────────────────────────────────────

        private Stream? _isoStream;      // backing stream for the disc image
        private long    _isoLength;      // total byte length of the disc image
        private bool    _isRawImage;     // true = 2352-byte BIN/CUE format

        // N-command parameter buffer (written byte-by-byte to REG_DATA before NCMD)
        private readonly byte[] _nParamBuf = new byte[16];
        private int  _nParamPos;
        private byte _nIntrStat;

        // S-command parameter buffer (written byte-by-byte to REG_SCMD_PARAM before
        // REG_SCMD_EXEC).  Mirrors PCSX2's cdvdWrite17 / SCMDParamBuff[].
        private readonly byte[] _sCmdParamBuf = new byte[16];
        private int _sCmdParamPos;

        // Current N_READY status byte (REG_NREADY, offset 0x05).
        // Initialised to READY | MECHA_INIT | DEV9CON, matching PCSX2 cdvdReset()
        // which calls cdvdUpdateReady(CDVD_DRIVE_READY) → always includes those bits.
        private byte _nReady = DRIVE_READY | DRIVE_MECHA_INIT | DRIVE_DEV9CON;

        // S-command ready / "sDataIn" byte (REG_SCMD READ side, offset 0x17).
        // In PCSX2 v1.6.0 cdvdRead17() is named "SREADY" and returns cdvd.sDataIn:
        //   cdvdReset() initialises sDataIn = 0x40 (ready)
        //   SetResultSize() clears sDataIn &= ~0x40 while processing
        //   after execution sDataIn |= 0x40 (done)
        // Since our S-commands execute synchronously, _sReady stays 0x40 from the
        // caller's perspective: the IOP polls 0x17 after issuing the S-command and
        // always sees bit 6 = 1, so the loop at 0xAA70 exits immediately.
        private byte _sReady = 0x40;

        // Drive status: true while an N-command is executing (set before DataReady fires,
        // cleared after).  Mirrors PCSX2 cdvd.Status: READ (0x06) during command,
        // PAUSE (0x0A) when disc present and idle.
        private bool _nCmdActive;

        // N-command result buffer: small responses (GetDriveStatus, etc.) returned
        // via REG_NDATAOUT (offset 0x0F).  Separate from the DMA sector buffer.
        // Mirrors PCSX2 cdvd.Result[] / cdvd.ResultP / cdvd.ResultC.
        private readonly byte[] _nCmdResult = new byte[16];
        private int _nCmdResultLen;
        private int _nCmdResultPos;

        // Sector data buffer: filled by ExecuteNCommand, consumed by ReadNextDmaWord
        private byte[] _sectorData       = Array.Empty<byte>();
        private int    _sectorDataOffset;

        // S-command output buffer
        private readonly byte[] _sCmdOutput = new byte[32];
        private int _sCmdOutputPos;
        private int _sCmdOutputLen;

        // ── Public properties ─────────────────────────────────────────────────

        /// <summary>True when a disc image is loaded.</summary>
        public bool HasDisc => _isoLength > 0;

        /// <summary>Current disc type byte (value of the CRT register).</summary>
        public byte DiscType { get; private set; } = DISC_NO_DISC;

        /// <summary>True when the loaded image uses 2352-byte raw sectors (BIN/CUE format).</summary>
        public bool IsRawImage => _isRawImage;

        /// <summary>
        /// Raised when an N-command completes (data is ready in the sector buffer).
        /// <see cref="IopMemoryBus"/> wires this to assert IOP INTC bit 2.
        /// </summary>
        public event EventHandler? DataReady;

        /// <summary>
        /// Set ISTAT bit 0 (disc-detection event) and return it so the caller can
        /// fire IOP INTC bit 2.  Matches PCSX2 cdvdDetectDisk(): cdvd.IntrStat |= 1.
        /// </summary>
        public void RaiseDiscDetect()
        {
            _nIntrStat |= INTR_DISC_DETECT;
        }

        // ── ISO image loading ─────────────────────────────────────────────────

        /// <summary>
        /// Load a disc image from a byte array.  The image may be a cooked ISO-9660
        /// image (2048 bytes/sector) or a raw BIN/CUE image (2352 bytes/sector with a
        /// 24-byte sector header).  The format is detected automatically from the
        /// sector sync pattern.
        ///
        /// Pass <c>null</c> or an empty array to eject the disc.
        /// </summary>
        public void LoadImage(byte[]? iso)
        {
            _isoStream        = iso != null && iso.Length > 0
                                    ? new MemoryStream(iso, writable: false)
                                    : null;
            _isoLength        = iso?.Length ?? 0;
            _isRawImage       = DetectRawImage();
            DiscType          = _isoLength > 0 ? DISC_PS2_DVD : DISC_NO_DISC;
            _nParamPos        = 0;
            _nIntrStat        = 0;
            _sCmdParamPos     = 0;
            _sReady           = 0x40; // PCSX2 cdvdReset() initialises sDataIn = 0x40
            _sectorData       = Array.Empty<byte>();
            _sectorDataOffset = 0;
        }

        /// <summary>
        /// Load a disc image from a <see cref="Stream"/>.  The stream must support
        /// seeking (<see cref="Stream.CanSeek"/> must be <c>true</c>) and remain open
        /// for the lifetime of this <see cref="Cdvd"/> instance.  The format is
        /// detected automatically from the sector sync pattern.
        ///
        /// Use this overload when the disc image cannot be loaded into a byte array,
        /// for example when the image file is larger than 2 GB.
        /// </summary>
        public void LoadFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable.", nameof(stream));

            _isoStream        = stream;
            _isoLength        = stream.Length;
            _isRawImage       = DetectRawImage();
            DiscType          = _isoLength > 0 ? DISC_PS2_DVD : DISC_NO_DISC;
            _nParamPos        = 0;
            _nIntrStat        = 0;
            _sCmdParamPos     = 0;
            _sReady           = 0x40; // PCSX2 cdvdReset() initialises sDataIn = 0x40
            _sectorData       = Array.Empty<byte>();
            _sectorDataOffset = 0;
        }

        // ── Raw-image detection ───────────────────────────────────────────────

        /// <summary>
        /// Return true when the loaded disc image appears to be a raw 2352-byte-sector
        /// BIN/CUE image.  Detection uses the CD-ROM sync pattern at byte 0 of the first
        /// sector (0x00 0xFF×11 0x00).
        /// </summary>
        private bool DetectRawImage()
        {
            if (_isoStream == null || _isoLength < RawSectorSize) return false;

            byte[] header = new byte[12];
            if (ReadIsoBytes(0, header, 0, 12) < 12) return false;

            // CD-ROM sync header: byte 0 = 0x00, bytes 1-11 = 0xFF, byte 12 = 0x00
            if (header[0] != 0x00 || header[11] != 0x00) return false;
            for (int i = 1; i <= 10; i++)
                if (header[i] != 0xFF) return false;
            return true;
        }

        // ── Stream read helper ────────────────────────────────────────────────

        /// <summary>
        /// Read up to <paramref name="count"/> bytes from the disc image at
        /// <paramref name="offset"/> into <paramref name="buf"/> starting at
        /// <paramref name="bufOffset"/>.  Returns the number of bytes actually read.
        /// </summary>
        private int ReadIsoBytes(long offset, byte[] buf, int bufOffset, int count)
        {
            if (_isoStream == null || offset < 0 || offset >= _isoLength)
                return 0;

            long available = _isoLength - offset;
            int toRead = (int)Math.Min(count, available);
            if (toRead <= 0) return 0;

            _isoStream.Position = offset;
            int read = 0;
            while (read < toRead)
            {
                int n = _isoStream.Read(buf, bufOffset + read, toRead - read);
                if (n == 0) break;
                read += n;
            }
            return read;
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
                REG_NREADY     => _nReady,                    // READY | MECHA_INIT | DEV9CON (0x4C)
                REG_NDOUT      => _nCmdResultLen > 0          // 0x06: NCMDResult (PCSX2 cdvdRead06)
                                      ? _nCmdResult[0]        // non-destructive: same byte every read (like PCSX2 cdvd.NCMDResult)
                                      : (byte)0x00,           // 0x00 = no result / reset state
                REG_ISTAT      => _nIntrStat,
                REG_STATUS     => HasDisc                     // PCSX2: CDVD_STATUS_PAUSE=0x0A, CDVD_STATUS_READ=0x06, STOP=0x00
                                      ? (_nCmdActive ? STATUS_READ : STATUS_PAUSE)
                                      : STATUS_STOP,
                REG_TRAY       => HasDisc ? (byte)0x00 : (byte)0x01, // 0=closed, 1=open
                REG_CRT        => DiscType,
                REG_NDATAOUT   => _nCmdResultPos < _nCmdResultLen  // 0x0F = N-cmd result bytes (sequential, like PCSX2 cdvd.Result[ResultP++])
                                      ? _nCmdResult[_nCmdResultPos++]
                                      : (byte)0,
                REG_SCMD_PARAM => _sReady,                    // 0x17 READ = SREADY/sDataIn (bit6=idle, bit7=error)
                REG_SDATAOUT   => ReadSDataByte(),            // 0x18 = cdvdRead18: SCMDResultBuff[pos++]
                _              => 0
            };
        }

        /// <summary>
        /// Return the next S-command result byte, tracking bit 6 of <see cref="_sReady"/>
        /// (sDataIn) per PCSX2 cdvdRead18 semantics.
        ///   bit 6 = 0 → result delivery in progress (cleared by ExecuteSCommand)
        ///   bit 6 = 1 → idle / all bytes consumed (set when pos ≥ len)
        /// </summary>
        private byte ReadSDataByte()
        {
            if ((_sReady & 0x40) == 0 && _sCmdOutputPos < _sCmdOutputLen)
            {
                byte val = _sCmdOutput[_sCmdOutputPos++];
                if (_sCmdOutputPos >= _sCmdOutputLen)
                    _sReady |= 0x40; // all bytes consumed → mark idle
                return val;
            }
            // Either idle already or position exhausted: return last byte (PCSX2 behaviour)
            return _sCmdOutputPos > 0 ? _sCmdOutput[_sCmdOutputPos - 1] : (byte)0;
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

                case REG_SCMD_EXEC:
                    // Execute the S-command using accumulated parameters (cdvdWrite16 in PCSX2).
                    // After execution the parameter position resets, matching PCSX2 behaviour.
                    ExecuteSCommand(value);
                    _sCmdParamPos = 0;
                    break;

                case REG_SCMD_PARAM:
                    // Accumulate S-command parameter byte (cdvdWrite17 "SDATAIN" in PCSX2).
                    if (_sCmdParamPos < _sCmdParamBuf.Length)
                        _sCmdParamBuf[_sCmdParamPos++] = value;
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

        /// <summary>
        /// Set up the N-command result buffer (small responses via REG_NDATAOUT).
        /// Mirrors PCSX2 SetResultSize(n) which stores n bytes in cdvd.Result[].
        /// </summary>
        private void SetNResult(params byte[] bytes)
        {
            int len = Math.Min(bytes.Length, _nCmdResult.Length);
            for (int i = 0; i < len; i++) _nCmdResult[i] = bytes[i];
            _nCmdResultLen = len;
            _nCmdResultPos = 0;
        }

        private void ExecuteNCommand(byte cmd)
        {
            // Mark drive as busy (STATUS = READ) for the duration of this call.
            // Matches PCSX2: cdvd.Status = CDVD_STATUS_READ before command, then
            // reset to CDVD_STATUS_PAUSE after.
            _nCmdActive = true;
            _nCmdResultLen = 0;
            _nCmdResultPos = 0;
            try
            {
                switch (cmd)
                {
                    // ── Startup / status N-commands (small results via NDATAOUT) ──
                    case 0x00: // NoOp / NOP
                        SetNResult(0x00);
                        break;

                    case 0x01: // Standby / Stop
                        // PCSX2: result[0] = 0 (success).  No disc motion change in emulation.
                        SetNResult(0x00);
                        break;

                    case 0x02: // Seek
                        SetNResult(0x00);
                        break;

                    case 0x03: // GetErrors — returns error count byte
                        SetNResult(0x00); // 0 = no errors
                        break;

                    case 0x04: // GetTrayState — returns tray state (same as REG_TRAY)
                        SetNResult(HasDisc ? (byte)0x00 : (byte)0x01);
                        break;

                    case 0x0B: // GetDriveStatus / cdvdGetDriveStatus in PCSX2
                        // Returns 1 byte: the drive STATUS register value.
                        // CDVDman reads this and checks for CDVD_STATUS_PAUSE (0x0A)
                        // to confirm disc is idle/ready.
                        SetNResult(HasDisc ? STATUS_PAUSE : STATUS_STOP);
                        break;

                    case 0x0C: // GetError (error code)
                        SetNResult(0x00);
                        break;

                    case 0x0F: // GetDiscType — same as REG_CRT
                        SetNResult(DiscType);
                        break;

                    case 0x10: // GetDiscID / CD-ID (12 bytes)
                        // Return 12 zero bytes; CDVDman uses this for copy-protection checks.
                        SetNResult(0,0,0,0, 0,0,0,0, 0,0,0,0);
                        break;

                    case NCMD_READ_CD:
                        if (_nParamPos >= 5)
                        {
                            uint lba        = ReadParamU32(0);
                            uint count      = _nParamBuf[4];
                            if (count == 0) count = 1;
                            byte sectorType = _nParamPos >= 6 ? _nParamBuf[5] : (byte)0;
                            LoadSectorBuffer(lba, count, sectorType);
                        }
                        break;

                    case NCMD_READ_CDDA:
                        if (_nParamPos >= 5)
                        {
                            uint lba   = ReadParamU32(0);
                            uint count = _nParamBuf[4];
                            if (count == 0) count = 1;
                            LoadCddaBuffer(lba, count);
                        }
                        break;

                    case NCMD_READ_DVD:
                        // DVD sectors are always 2048-byte user data; no sector type parameter.
                        if (_nParamPos >= 5)
                        {
                            uint lba   = ReadParamU32(0);
                            uint count = _nParamBuf[4];
                            if (count == 0) count = 1;
                            LoadSectorBuffer(lba, count, sectorType: 0);
                        }
                        break;

                    case NCMD_GET_TOC:
                        LoadTocBuffer();
                        break;
                }
            }
            finally
            {
                // Command complete: return to PAUSE state (disc present, idle).
                _nCmdActive = false;
            }

            _nIntrStat |= INTR_DATA_READY;
            DataReady?.Invoke(this, EventArgs.Empty);
        }

        private uint ReadParamU32(int start)
            => (uint)(_nParamBuf[start]
                    | (_nParamBuf[start + 1] << 8)
                    | (_nParamBuf[start + 2] << 16)
                    | (_nParamBuf[start + 3] << 24));

        private void LoadSectorBuffer(uint lba, uint count, byte sectorType = 0)
        {
            // sectorType 4 = raw sector + sub-channel (2048 user bytes + SubChannelSize per sector)
            bool withSubQ       = sectorType == 4;
            int  bytesPerSector = withSubQ ? SectorSize + SubChannelSize : SectorSize;
            int  totalBytes     = (int)(count * bytesPerSector);
            _sectorData       = new byte[totalBytes];
            _sectorDataOffset = 0;

            for (uint i = 0; i < count; i++)
            {
                long srcOffset = _isRawImage
                    ? (long)(lba + i) * RawSectorSize + RawSectorHeaderSize
                    : (long)(lba + i) * SectorSize;
                int dstOffset = (int)(i * bytesPerSector);
                if (srcOffset >= 0 && srcOffset + SectorSize <= _isoLength)
                    ReadIsoBytes(srcOffset, _sectorData, dstOffset, SectorSize);

                if (withSubQ)
                {
                    byte[] subq = BuildSubQ(lba + i);
                    Array.Copy(subq, 0, _sectorData, dstOffset + SectorSize, SubChannelSize);
                }
            }
        }

        private void LoadCddaBuffer(uint lba, uint count)
        {
            if (_isRawImage)
            {
                // Raw BIN/CUE: CDDA sectors are 2352 bytes of raw 16-bit stereo PCM
                // at 44100 Hz; the entire sector is audio data (no header).
                int totalBytes    = (int)(count * RawSectorSize);
                _sectorData       = new byte[totalBytes];
                _sectorDataOffset = 0;

                for (uint i = 0; i < count; i++)
                {
                    long srcOffset = (long)(lba + i) * RawSectorSize;
                    int  dstOffset = (int)(i * RawSectorSize);
                    if (srcOffset >= 0 && srcOffset + RawSectorSize <= _isoLength)
                        ReadIsoBytes(srcOffset, _sectorData, dstOffset, RawSectorSize);
                }
            }
            else
            {
                // Cooked ISO-9660 has no audio tracks — return silence.
                _sectorData       = new byte[count * RawSectorSize]; // zeroed = silence
                _sectorDataOffset = 0;
            }
        }

        // ── S-command execution ───────────────────────────────────────────────

        private void ExecuteSCommand(byte cmd)
        {
            _sCmdOutputPos = 0;
            _sCmdOutputLen = 0;

            switch (cmd)
            {
                case 0x00: // GetMedia — returns disc type byte
                case 0x05: // GetDiscType — same as CRT register
                    _sCmdOutput[0] = DiscType;
                    _sCmdOutputLen = 1;
                    break;

                case 0x03: // GetMechaVersion — returns 4-byte mechacon version
                    // PCSX2 reads from BIOS.mec file or creates {0x03, 0x06, 0x02, 0x00}.
                    _sCmdOutput[0] = 0x03;
                    _sCmdOutput[1] = 0x06;
                    _sCmdOutput[2] = 0x02;
                    _sCmdOutput[3] = 0x00;
                    _sCmdOutputLen = 4;
                    break;

                case 0x12: // GetLayerInfo — layer-break PSN for dual-layer DVDs
                    // Single-layer disc: layer-break PSN = 0 (no second layer).
                    _sCmdOutput[0] = 0x00;
                    _sCmdOutput[1] = 0x00;
                    _sCmdOutput[2] = 0x00;
                    _sCmdOutput[3] = 0x00;
                    _sCmdOutputLen = 4;
                    break;

                case 0x40: // CdOpenConfig — returns 1 status byte (0 = OK)
                    // Parameters (3 bytes from _sCmdParamBuf): readWrite, offset, numBlocks.
                    // We accept all config open requests and return success.
                    _sCmdOutput[0] = 0x00; // 0 = complete / success
                    _sCmdOutputLen = 1;
                    break;

                case 0x41: // CdReadConfig — returns 16 bytes of NVM config data
                    // PCSX2 reads from BIOS.nvm; we return all-zero (default region settings).
                    for (int i = 0; i < 16; i++) _sCmdOutput[i] = 0x00;
                    _sCmdOutputLen = 16;
                    break;

                case 0x42: // CdCloseConfig — returns 1 status byte (0 = OK)
                    _sCmdOutput[0] = 0x00;
                    _sCmdOutputLen = 1;
                    break;

                case 0x43: // CdWriteNVM — write 16 bytes to NVM config, returns 1 status byte
                    // PCSX2 cdvdWrite43(): writes to BIOS.nvm, returns 0 = OK.
                    _sCmdOutput[0] = 0x00;
                    _sCmdOutputLen = 1;
                    break;

                case 0x47: // GetModelNumber — returns 8 bytes model string (all zero ok)
                    for (int i = 0; i < 8; i++) _sCmdOutput[i] = 0x00;
                    _sCmdOutputLen = 8;
                    break;

                // Other S-commands (disc info, mechanical ops) return empty.
            }
            // Per PCSX2 SetResultSize(): clear bit 6 of sDataIn to signal result delivery
            // in progress. ReadSDataByte() sets it back when the last byte is consumed.
            if (_sCmdOutputLen > 0)
                _sReady &= 0xBF; // clear bit 6 (not idle, data available from 0x18)
            else
                _sReady |= 0x40; // no data → immediately idle
        }

        // ── TOC buffer builder ─────────────────────────────────────────────────

        /// <summary>
        /// Build a 1024-byte TOC buffer for the current disc image.
        ///
        /// DVD TOC layout (first 16 bytes, per PS2 DVD specification):
        /// <list type="table">
        ///   <item><term>Byte 0</term><description>Book type / disc category (0x04 = DVD-ROM)</description></item>
        ///   <item><term>Byte 1</term><description>Disc size / maximum data rate (0xF2 = 120 mm, 52×)</description></item>
        ///   <item><term>Byte 2</term><description>Number of layers minus one (0 = single-layer)</description></item>
        ///   <item><term>Byte 3</term><description>Track path / layer type (0 = parallel, read-only)</description></item>
        ///   <item><term>Bytes 4–7</term><description>Layer 0 start PSN (big-endian; standard DVD = 0x00030000)</description></item>
        ///   <item><term>Bytes 8–11</term><description>Layer 0 end PSN (big-endian; start + numSectors − 1)</description></item>
        ///   <item><term>Bytes 12–15</term><description>Layer 0 end PSN in layer 0 data area (same as end for single-layer)</description></item>
        /// </list>
        ///
        /// Reference: PCSX2 <c>cdvdGetTOC()</c> in <c>pcsx2/CDVD/CDVD.cpp</c>.
        /// </summary>
        private void LoadTocBuffer()
        {
            byte[] toc = new byte[TocSize];

            if (HasDisc)
            {
                int numSectors = (int)(_isoLength / (_isRawImage ? RawSectorSize : SectorSize));

                // DVD-ROM single-layer layout.
                toc[0] = 0x04; // book type: DVD-ROM
                toc[1] = 0xF2; // disc size: 120 mm, max rate
                toc[2] = 0x00; // single layer (0 = 1 layer)
                toc[3] = 0x00; // track path: parallel / read-only

                // Layer 0 start PSN = 0x00030000 (standard DVD data area offset).
                const uint startPsn = 0x00030000u;
                toc[4]  = (byte)((startPsn >> 24) & 0xFF);
                toc[5]  = (byte)((startPsn >> 16) & 0xFF);
                toc[6]  = (byte)((startPsn >> 8)  & 0xFF);
                toc[7]  = (byte)((startPsn)        & 0xFF);

                // Layer 0 end PSN = start + numSectors − 1.
                uint endPsn = startPsn + (uint)numSectors - 1;
                toc[8]  = (byte)(endPsn >> 24);
                toc[9]  = (byte)(endPsn >> 16);
                toc[10] = (byte)(endPsn >> 8);
                toc[11] = (byte)(endPsn);

                // Layer 0 end PSN in layer 0 data area (same as end for single-layer).
                toc[12] = toc[8];
                toc[13] = toc[9];
                toc[14] = toc[10];
                toc[15] = toc[11];
            }

            _sectorData       = toc;
            _sectorDataOffset = 0;
        }

        // ── Sub-channel Q builder ──────────────────────────────────────────────

        /// <summary>
        /// Build a 16-byte sub-channel block for <paramref name="lba"/>:
        /// 12 bytes of sub-Q data followed by 4 reserved bytes.
        ///
        /// Sub-Q format (per ECMA-130 / PS2 hardware spec):
        /// <list type="table">
        ///   <item><term>[0] ctrl|adr</term><description>0x41 = data track, ADR=1 (position)</description></item>
        ///   <item><term>[1] track</term><description>BCD track number (always 01 for data discs)</description></item>
        ///   <item><term>[2] index</term><description>BCD index (always 01)</description></item>
        ///   <item><term>[3–5] relative MSF</term><description>BCD minute/second/frame from track start (0 for simplified emulation)</description></item>
        ///   <item><term>[6] zero</term><description>0x00</description></item>
        ///   <item><term>[7–9] absolute MSF</term><description>BCD absolute minute/second/frame (LBA + 150 lead-in offset)</description></item>
        ///   <item><term>[10] zero</term><description>0x00</description></item>
        ///   <item><term>[11] CRC</term><description>0x00 (ignored by emulator)</description></item>
        ///   <item><term>[12–15] reserved</term><description>0x00 padding</description></item>
        /// </list>
        ///
        /// Reference: PCSX2 <c>CdRawSectorWithOC</c> mode in <c>pcsx2/CDVD/CDVD.cpp</c>.
        /// </summary>
        private static byte[] BuildSubQ(uint lba)
        {
            // CD-ROM absolute MSF: LBA 0 corresponds to MSF 00:02:00 (150-sector lead-in).
            uint abs      = lba + 150;
            byte absFrame = ToBcd((int)(abs % 75));
            byte absSec   = ToBcd((int)(abs / 75 % 60));
            byte absMin   = ToBcd((int)(abs / 75 / 60));

            byte[] subq = new byte[SubChannelSize]; // 16 bytes, zero-initialised
            subq[0] = 0x41; // ctrl=4 (data), adr=1 (position)
            subq[1] = 0x01; // track 01 (BCD)
            subq[2] = 0x01; // index 01 (BCD)
            // relative MSF [3-5] = 0 (simplified — not used by most games)
            subq[6]  = 0x00;
            subq[7]  = absMin;
            subq[8]  = absSec;
            subq[9]  = absFrame;
            // [10] = 0x00, [11] CRC = 0x00, [12-15] reserved = 0x00
            return subq;
        }

        /// <summary>Encode a decimal value (0–99) as a BCD byte.</summary>
        private static byte ToBcd(int val) => (byte)((val / 10 << 4) | (val % 10));

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
            if (srcOffset < 0 || srcOffset + (long)entry.Size > _isoLength)
                return Array.Empty<byte>();

            byte[] data = new byte[entry.Size];
            ReadIsoBytes(srcOffset, data, 0, (int)entry.Size);
            return data;
        }

        /// <summary>
        /// Copy sector user-data from the disc image into <paramref name="buffer"/>.
        /// For raw BIN/CUE images the 24-byte sector header is stripped automatically.
        /// </summary>
        public void ReadSectors(uint lba, uint count, byte[] buffer, int bufferOffset = 0)
        {
            for (uint i = 0; i < count; i++)
            {
                long srcOffset = _isRawImage
                    ? (long)(lba + i) * RawSectorSize + RawSectorHeaderSize
                    : (long)(lba + i) * SectorSize;
                int dstOffset = bufferOffset + (int)(i * SectorSize);
                if (srcOffset >= 0 && srcOffset + SectorSize <= _isoLength
                    && dstOffset + SectorSize <= buffer.Length)
                {
                    ReadIsoBytes(srcOffset, buffer, dstOffset, SectorSize);
                }
            }
        }

        // ── ISO-9660 internal helpers ─────────────────────────────────────────

        private bool TryReadPvd(out uint rootLba, out uint rootSize)
        {
            rootLba  = 0;
            rootSize = 0;

            // For raw images the user data starts 24 bytes into each 2352-byte sector.
            long pvdOffset = _isRawImage
                ? (long)PvdLba * RawSectorSize + RawSectorHeaderSize
                : (long)PvdLba * SectorSize;
            if (pvdOffset + SectorSize > _isoLength)
                return false;

            byte[] pvd = new byte[SectorSize];
            if (ReadIsoBytes(pvdOffset, pvd, 0, SectorSize) < SectorSize)
                return false;

            // Verify PVD signature: type=1, identifier="CD001", version=1
            if (pvd[0] != 1)
                return false;
            if (pvd[1] != 'C' || pvd[2] != 'D'
             || pvd[3] != '0' || pvd[4] != '0'
             || pvd[5] != '1')
                return false;

            // Root Directory Record at PVD byte offset 156
            rootLba  = ReadU32LE(pvd, 156 + 2);
            rootSize = ReadU32LE(pvd, 156 + 10);
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

        /// <summary>
        /// Recursively enumerate all non-directory entries in the ISO-9660 file
        /// system, starting from the root directory.
        /// </summary>
        /// <returns>
        /// A sequence of <c>(string DiscPath, IsoEntry Entry)</c> tuples where
        /// <c>DiscPath</c> is the absolute disc path such as <c>/SCUS_971.98;1</c>
        /// or <c>/MODULES/CDVD.IRX;1</c>.
        /// </returns>
        public IEnumerable<(string DiscPath, IsoEntry Entry)> EnumerateFiles()
        {
            if (!HasDisc) yield break;
            if (!TryReadPvd(out uint rootLba, out uint rootSize)) yield break;

            foreach (var item in WalkDirectory("/", rootLba, rootSize))
                yield return item;
        }

        private IEnumerable<(string DiscPath, IsoEntry Entry)> WalkDirectory(
            string dirPath, uint dirLba, uint dirSize)
        {
            foreach (var entry in ListDirectory(dirLba, dirSize))
            {
                string fullPath = dirPath.TrimEnd('/') + "/" + entry.Name;
                if (entry.IsDirectory)
                {
                    foreach (var sub in WalkDirectory(fullPath, entry.Lba, entry.Size))
                        yield return sub;
                }
                else
                {
                    yield return (fullPath, entry);
                }
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
