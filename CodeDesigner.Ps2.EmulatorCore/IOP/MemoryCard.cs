using System;

namespace CodeDesigner.Ps2.EmulatorCore.IOP
{
    /// <summary>
    /// Emulates a PS2 8 MB memory card accessible through the SIO2 serial bus.
    ///
    /// The memory card stores data in 16,384 pages. Each page is 528 bytes on disk:
    /// 512 bytes of payload followed by 16 bytes of ECC (error-correcting code).
    /// The SIO2 protocol exposes individual pages as 512-byte units.
    ///
    /// <b>File format (.ps2)</b>: produced by PCSX2.  Exactly <see cref="TotalFileSize"/>
    /// bytes laid out as <c>TotalPages × PageStride</c> byte runs, where each run is
    /// <c>PageDataSize</c> data bytes followed by <c>PageEccSize</c> ECC bytes.
    ///
    /// <b>Superblock</b>: page 0 begins with the ASCII magic string
    /// <c>"Sony PS2 Memory Card Format 1.2.0.0"</c> followed by card-geometry fields.
    ///
    /// <b>SIO2 command protocol</b>:
    /// The IOP memory-card manager (mcman.irx) communicates with this device by
    /// writing command bytes into the SIO2 FIFO_IN register and reading responses
    /// from FIFO_OUT.  Transactions are split across one or more SEND3 entries:
    /// <list type="table">
    ///   <item>
    ///     <term>Phase 1 — command header</term>
    ///     <description>
    ///       sendLen ≈ 7, recvLen ≈ 8.  First byte is <c>0x81</c> (slot 0) or
    ///       <c>0x82</c> (slot 1); second byte is the command (e.g. 0x52 = read).
    ///       Bytes 3–4 carry the 16-bit page address (big-endian).
    ///       Response: <c>[0xFF, 0x00, 0x2B, 0x00, addrH, addrL, 0x5A, 0x5D]</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Phase 2 — data transfer</term>
    ///     <description>
    ///       sendLen = recvLen = 514 (512 data + XOR checksum + terminator).
    ///       For a READ the IOP sends dummy bytes; the card returns page data.
    ///       For a WRITE the IOP sends the new page data; the card acknowledges.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// Reference: PCSX2 pcsx2/SIO/Mcd/MemoryCardProtocol.cpp.
    /// </summary>
    public sealed class MemoryCard
    {
        // ── Card geometry constants ────────────────────────────────────────────

        /// <summary>Payload bytes in a single page (512).</summary>
        public const int PageDataSize    = 512;

        /// <summary>ECC bytes appended after each page's payload in the file (16).</summary>
        public const int PageEccSize     = 16;

        /// <summary>Total bytes per page on disk (data + ECC = 528).</summary>
        public const int PageStride      = PageDataSize + PageEccSize;

        /// <summary>Pages in one allocatable cluster (2).</summary>
        public const int PagesPerCluster = 2;

        /// <summary>Pages per erase block (16).</summary>
        public const int PagesPerBlock   = 16;

        /// <summary>Total allocatable clusters (8192 for an 8 MB card).</summary>
        public const int ClustersPerCard = 8192;

        /// <summary>Total number of pages (16 384).</summary>
        public const int TotalPages      = ClustersPerCard * PagesPerCluster;

        /// <summary>
        /// Total size in bytes of a standard PS2 memory-card image file (8 650 752).
        /// </summary>
        public const int TotalFileSize   = TotalPages * PageStride;

        // ── SIO2 address bytes ────────────────────────────────────────────────

        /// <summary>SIO2 address byte that selects memory-card slot 0 (0x81).</summary>
        public const byte AddrSlot0 = 0x81;

        /// <summary>SIO2 address byte that selects memory-card slot 1 (0x82).</summary>
        public const byte AddrSlot1 = 0x82;

        // ── Command bytes ─────────────────────────────────────────────────────

        /// <summary>Read page command (0x52).</summary>
        public const byte CmdRead    = 0x52;

        /// <summary>Write page command (0x57).</summary>
        public const byte CmdWrite   = 0x57;

        /// <summary>Erase cluster command (0x82).</summary>
        public const byte CmdErase   = 0x82;

        /// <summary>Get card specifications command (0x46).</summary>
        public const byte CmdGetSpec = 0x46;

        /// <summary>Get card ID / set terminator command (0x47).</summary>
        public const byte CmdGetId   = 0x47;

        /// <summary>Get operation status command (0x43).</summary>
        public const byte CmdStatus  = 0x43;

        /// <summary>Reset / probe command (0xF3).</summary>
        public const byte CmdReset   = 0xF3;

        /// <summary>Authentication command (0xF0).</summary>
        public const byte CmdAuth    = 0xF0;

        // ── Protocol framing bytes ────────────────────────────────────────────

        /// <summary>Acknowledge byte in MC response (0x2B).</summary>
        public const byte RespAck  = 0x2B;

        /// <summary>First "OK" marker used in read/write header exchanges (0x5A).</summary>
        public const byte RespOk1  = 0x5A;

        /// <summary>Second "OK" marker used in read/write header exchanges (0x5D).</summary>
        public const byte RespOk2  = 0x5D;

        /// <summary>Terminator byte that ends a data-phase response (0x47).</summary>
        public const byte RespTerm = 0x47;

        // ── Superblock ASCII magic ─────────────────────────────────────────────

        /// <summary>ASCII magic string at the start of the superblock (page 0).</summary>
        public const string SuperblockMagic = "Sony PS2 Memory Card Format 1.2.0.0";

        // ── Internal state ────────────────────────────────────────────────────

        private readonly byte[] _data;

        // Per-transaction state machine.
        private enum McState { Idle, ReadPage, WritePage }
        private McState _state       = McState.Idle;
        private int     _pendingPage = 0;

        // ── Construction / factory ────────────────────────────────────────────

        /// <summary>
        /// Load a memory card from an existing <paramref name="data"/> array.
        /// The array must be exactly <see cref="TotalFileSize"/> bytes.
        /// The data is copied; the caller's buffer is not retained.
        /// </summary>
        /// <exception cref="ArgumentNullException">When <paramref name="data"/> is null.</exception>
        /// <exception cref="ArgumentException">When the length is wrong.</exception>
        public MemoryCard(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length != TotalFileSize)
                throw new ArgumentException(
                    $"Memory card data must be exactly {TotalFileSize} bytes " +
                    $"({TotalPages} pages × {PageStride} bytes), got {data.Length}.",
                    nameof(data));

            _data = (byte[])data.Clone();
        }

        /// <summary>
        /// Create a blank, freshly formatted memory card.
        /// All pages are erased (filled with <c>0xFF</c>) and the superblock is
        /// written at page 0.
        /// </summary>
        public static MemoryCard CreateBlank()
        {
            byte[] data = new byte[TotalFileSize];
            for (int i = 0; i < data.Length; i++)
                data[i] = 0xFF;
            WriteSuperblock(data);
            return new MemoryCard(data);
        }

        // ── Public data accessor ──────────────────────────────────────────────

        /// <summary>
        /// Return a copy of the raw card image data.
        /// This can be persisted to a <c>.ps2</c> file.
        /// </summary>
        public byte[] GetData() => (byte[])_data.Clone();

        // ── SIO2 device interface ─────────────────────────────────────────────

        /// <summary>
        /// Process one SIO2 SEND3 descriptor's command bytes and generate the
        /// corresponding response bytes for the output FIFO.
        ///
        /// Transactions that require multiple SEND3 entries (e.g. read/write) use
        /// an internal state machine:
        /// <list type="number">
        ///   <item>The first descriptor starts with a MC address byte (<c>0x81</c>/<c>0x82</c>)
        ///   and a command byte; this header phase returns 8 framing bytes.</item>
        ///   <item>The next descriptor carries the data payload; this data phase
        ///   returns the page data (read) or an acknowledgement (write).</item>
        /// </list>
        /// </summary>
        /// <param name="cmd">Bytes sent by the IOP (dequeued from SIO2 FIFO_IN).</param>
        /// <param name="responseLength">Number of response bytes the IOP expects (SIO2 recvLen).</param>
        /// <returns>Response bytes to enqueue into SIO2 FIFO_OUT.</returns>
        public byte[] ProcessCommand(byte[] cmd, int responseLength)
        {
            if (cmd.Length == 0)
                return Fill(responseLength, 0xFF);

            // A new command always starts with the MC address byte.
            if (cmd[0] == AddrSlot0 || cmd[0] == AddrSlot1)
            {
                _state = McState.Idle;
                if (cmd.Length < 2)
                    return Fill(responseLength, 0xFF);
                return HandleNewCommand(cmd, responseLength);
            }

            // Continuation of a multi-entry transaction.
            return HandleContinuation(cmd, responseLength);
        }

        // ── Command dispatch ──────────────────────────────────────────────────

        private byte[] HandleNewCommand(byte[] cmd, int responseLength)
        {
            byte command = cmd[1];

            switch (command)
            {
                case CmdRead:
                {
                    // Command header: [0x81, 0x52, 0x00, pageH, pageL, 0x5A, 0x5D]
                    // Response header: [0xFF, 0x00, 0x2B, 0x00, pageH, pageL, 0x5A, 0x5D]
                    int page = DecodePageAddress(cmd);
                    _pendingPage = page;
                    _state       = McState.ReadPage;

                    byte[] resp = Fill(responseLength, 0xFF);
                    WriteHeader(resp, page);
                    return resp;
                }

                case CmdWrite:
                {
                    // Command header: [0x81, 0x57, 0x00, pageH, pageL, 0x5A, 0x5D]
                    // Response header: [0xFF, 0x00, 0x2B, 0x00, pageH, pageL, 0x5A, 0x5D]
                    int page = DecodePageAddress(cmd);
                    _pendingPage = page;
                    _state       = McState.WritePage;

                    byte[] resp = Fill(responseLength, 0xFF);
                    WriteHeader(resp, page);
                    return resp;
                }

                case CmdErase:
                {
                    // Erase the cluster (PagesPerCluster pages) that contains the given page.
                    // Command: [0x81, 0x82, 0x00, pageH, pageL, ...]
                    // Response: [0xFF, 0x00, 0x2B, 0x00, pageH, pageL, 0x5A, 0x5D, TERM]
                    int page         = DecodePageAddress(cmd);
                    int clusterFirst = (page / PagesPerCluster) * PagesPerCluster;
                    for (int p = clusterFirst; p < clusterFirst + PagesPerCluster; p++)
                        ErasePage(p);

                    byte[] resp = Fill(responseLength, 0xFF);
                    WriteHeader(resp, page);
                    if (responseLength >= 9) resp[8] = RespTerm;
                    return resp;
                }

                case CmdGetSpec:
                {
                    // Returns one of two specification sets depending on cmd[4]:
                    //   0x00 → timing parameters
                    //   0x01 → geometry parameters
                    // Response: [0xFF, 0x00, 0x2B, 0x00, 0x00, 0x5A, 0x5D, spec[5], xor, TERM]
                    byte specSet = cmd.Length >= 5 ? cmd[4] : (byte)0;
                    byte[] spec  = BuildSpec(specSet);
                    byte   xor   = ComputeXor(spec, 0, spec.Length);

                    byte[] resp = Fill(responseLength, 0xFF);
                    if (responseLength >= 1) resp[0] = 0xFF;
                    if (responseLength >= 2) resp[1] = 0x00;
                    if (responseLength >= 3) resp[2] = RespAck;
                    if (responseLength >= 4) resp[3] = 0x00;
                    if (responseLength >= 5) resp[4] = 0x00;
                    if (responseLength >= 6) resp[5] = RespOk1;
                    if (responseLength >= 7) resp[6] = RespOk2;
                    for (int i = 0; i < spec.Length; i++)
                        if (7 + i < responseLength) resp[7 + i] = spec[i];
                    int xorIdx  = 7 + spec.Length;
                    int termIdx = xorIdx + 1;
                    if (xorIdx  < responseLength) resp[xorIdx]  = xor;
                    if (termIdx < responseLength) resp[termIdx] = RespTerm;
                    return resp;
                }

                case CmdStatus:
                {
                    // Returns card status (0x00 = OK).
                    // Response: [0xFF, 0x00, 0x2B, 0x00, 0x00, TERM]
                    byte[] resp = Fill(responseLength, 0xFF);
                    if (responseLength >= 1) resp[0] = 0xFF;
                    if (responseLength >= 2) resp[1] = 0x00;
                    if (responseLength >= 3) resp[2] = RespAck;
                    if (responseLength >= 4) resp[3] = 0x00;
                    if (responseLength >= 5) resp[4] = 0x00;
                    if (responseLength >= 6) resp[5] = RespTerm;
                    return resp;
                }

                default:
                {
                    // Reset, Auth, GetId, or unknown: respond with a minimal ACK.
                    byte[] resp = Fill(responseLength, 0xFF);
                    if (responseLength >= 1) resp[0] = 0xFF;
                    if (responseLength >= 2) resp[1] = 0x00;
                    if (responseLength >= 3) resp[2] = RespAck;
                    if (responseLength >= 4) resp[3] = RespTerm;
                    return resp;
                }
            }
        }

        private byte[] HandleContinuation(byte[] cmd, int responseLength)
        {
            switch (_state)
            {
                case McState.ReadPage:
                {
                    // IOP is clocking dummy bytes; return page data + checksum + terminator.
                    _state = McState.Idle;
                    byte[] resp = Fill(responseLength, 0xFF);

                    if (_pendingPage >= 0 && _pendingPage < TotalPages)
                    {
                        int pageOff = _pendingPage * PageStride;
                        int copy    = Math.Min(PageDataSize, responseLength);
                        Array.Copy(_data, pageOff, resp, 0, copy);

                        int xorIdx  = PageDataSize;
                        int termIdx = PageDataSize + 1;
                        if (xorIdx  < responseLength)
                            resp[xorIdx]  = ComputeXor(_data, pageOff, PageDataSize);
                        if (termIdx < responseLength)
                            resp[termIdx] = RespTerm;
                    }
                    return resp;
                }

                case McState.WritePage:
                {
                    // IOP is sending page data; store first PageDataSize bytes.
                    _state = McState.Idle;

                    if (_pendingPage >= 0 && _pendingPage < TotalPages && cmd.Length >= PageDataSize)
                    {
                        int pageOff = _pendingPage * PageStride;
                        Array.Copy(cmd, 0, _data, pageOff, PageDataSize);
                    }

                    byte[] resp = Fill(responseLength, 0xFF);
                    if (responseLength >= 1) resp[responseLength - 1] = RespTerm;
                    return resp;
                }

                default:
                    return Fill(responseLength, 0xFF);
            }
        }

        // ── Low-level page I/O ─────────────────────────────────────────────────

        /// <summary>
        /// Read the data portion of <paramref name="page"/> into <paramref name="dest"/>.
        /// </summary>
        public void ReadPage(int page, byte[] dest, int destOffset = 0)
        {
            if (page < 0 || page >= TotalPages)
                throw new ArgumentOutOfRangeException(nameof(page));
            Array.Copy(_data, page * PageStride, dest, destOffset, PageDataSize);
        }

        /// <summary>
        /// Write <paramref name="src"/> data into <paramref name="page"/> (data portion only).
        /// </summary>
        public void WritePage(int page, byte[] src, int srcOffset = 0)
        {
            if (page < 0 || page >= TotalPages)
                throw new ArgumentOutOfRangeException(nameof(page));
            Array.Copy(src, srcOffset, _data, page * PageStride, PageDataSize);
        }

        /// <summary>
        /// Erase one page (fill all <see cref="PageStride"/> bytes with <c>0xFF</c>).
        /// </summary>
        public void ErasePage(int page)
        {
            if (page < 0 || page >= TotalPages)
                throw new ArgumentOutOfRangeException(nameof(page));
            int off = page * PageStride;
            for (int i = 0; i < PageStride; i++)
                _data[off + i] = 0xFF;
        }

        // ── Protocol helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Decode the 16-bit big-endian page address from command bytes [3] and [4].
        /// Returns 0 if the command is too short.
        /// </summary>
        private static int DecodePageAddress(byte[] cmd)
        {
            if (cmd.Length < 5) return 0;
            return (cmd[3] << 8) | cmd[4];
        }

        /// <summary>
        /// Write the 8-byte response header into <paramref name="resp"/>.
        /// Layout: <c>[0xFF, 0x00, ACK, 0x00, addrH, addrL, OK1, OK2]</c>.
        /// </summary>
        private static void WriteHeader(byte[] resp, int page)
        {
            if (resp.Length >= 1) resp[0] = 0xFF;
            if (resp.Length >= 2) resp[1] = 0x00;
            if (resp.Length >= 3) resp[2] = RespAck;
            if (resp.Length >= 4) resp[3] = 0x00;
            if (resp.Length >= 5) resp[4] = (byte)(page >> 8);
            if (resp.Length >= 6) resp[5] = (byte)(page & 0xFF);
            if (resp.Length >= 7) resp[6] = RespOk1;
            if (resp.Length >= 8) resp[7] = RespOk2;
        }

        /// <summary>Build the 5-byte specification data for command 0x46.</summary>
        private static byte[] BuildSpec(byte specSet)
        {
            if (specSet == 0x00)
            {
                // Timing parameters (from PCSX2 reference implementation).
                return new byte[] { 0x04, 0x00, 0x00, 0x80, 0x00 };
            }
            else
            {
                // Geometry parameters: page size / 128, pages/cluster,
                // pages/block, and high byte of clusters/card.
                return new byte[]
                {
                    (byte)(PageDataSize / 128),    // 4  (512 / 128)
                    PagesPerCluster,               // 2
                    PagesPerBlock,                 // 16
                    0x00,
                    (byte)(ClustersPerCard >> 8)   // 32 (8192 >> 8)
                };
            }
        }

        /// <summary>XOR all bytes in <paramref name="data"/>[offset..offset+length).</summary>
        private static byte ComputeXor(byte[] data, int offset, int length)
        {
            byte xor = 0;
            for (int i = 0; i < length; i++)
                xor ^= data[offset + i];
            return xor;
        }

        /// <summary>Create a byte array of <paramref name="length"/> filled with <paramref name="fill"/>.</summary>
        private static byte[] Fill(int length, byte fill)
        {
            byte[] arr = new byte[length];
            if (fill != 0)
                for (int i = 0; i < length; i++)
                    arr[i] = fill;
            return arr;
        }

        // ── Superblock writer ──────────────────────────────────────────────────

        /// <summary>
        /// Write a valid superblock into <paramref name="data"/> at the start of page 0
        /// (byte offset 0).  This matches the layout produced by PCSX2 when formatting
        /// a new memory card.
        /// </summary>
        private static void WriteSuperblock(byte[] data)
        {
            // Magic string (36 bytes of ASCII, NUL-terminated, padded to 40 bytes).
            byte[] magic = System.Text.Encoding.ASCII.GetBytes(SuperblockMagic + "\0");
            for (int i = 0; i < 40 && i < magic.Length; i++)
                data[i] = magic[i];

            // Page size: 0x0200 = 512 (little-endian uint16 at offset 0x28).
            data[0x28] = 0x00;
            data[0x29] = 0x02;

            // Pages per cluster: 2 (uint16 LE at offset 0x2A).
            data[0x2A] = 0x02;
            data[0x2B] = 0x00;

            // Pages per block: 16 (uint16 LE at offset 0x2C).
            data[0x2C] = 0x10;
            data[0x2D] = 0x00;

            // Clusters per card: 8192 = 0x2000 (uint32 LE at offset 0x30).
            data[0x30] = 0x00;
            data[0x31] = 0x20;
            data[0x32] = 0x00;
            data[0x33] = 0x00;
        }
    }
}
