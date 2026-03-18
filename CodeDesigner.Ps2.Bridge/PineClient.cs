using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// PCSX2 PINE (Protocol for Instrumentation of Emulators) IPC client.
    /// <para>
    /// Implements the binary PINE protocol to read/write PCSX2 guest memory,
    /// query emulator status, and — once the PINE debugger extension PR is
    /// merged — control EE execution (pause, resume, step, breakpoints, registers).
    /// </para>
    /// <para>
    /// <b>Existing opcodes (upstream PINE):</b> 0x00–0x0F<br/>
    /// <b>Extended opcodes (our PCSX2 PR):</b> 0x10–0x17<br/>
    /// See <c>PINE_EXTENSION_PROPOSAL.md</c> for the full protocol spec and the
    /// reference C++ implementation to submit to PCSX2.
    /// </para>
    /// <para>
    /// <b>Packet format:</b><br/>
    ///   Request:  <c>[total_size : u32 LE][opcode : u8][args…]</c><br/>
    ///   Reply:    <c>[total_size : u32 LE][status : u8][data…]</c><br/>
    ///   status = 0x00 (IPC_OK) or 0xFF (IPC_FAIL).
    /// </para>
    /// <para>
    /// Reference: <see href="https://github.com/GovanifY/pine"/> and
    /// <see href="https://github.com/PCSX2/pcsx2/blob/master/pcsx2/PINE.h"/>.
    /// </para>
    /// </summary>
    public sealed class PineClient : IDisposable
    {
        // ── Default slot / port ───────────────────────────────────────────────
        /// <summary>Default PCSX2 PINE TCP port (Windows) / Unix socket suffix.</summary>
        public const int DefaultSlot = 28011;

        // ── Existing PINE opcodes (upstream) ──────────────────────────────────
        private const byte MsgRead8       = 0x00;
        private const byte MsgRead16      = 0x01;
        private const byte MsgRead32      = 0x02;
        private const byte MsgRead64      = 0x03;
        private const byte MsgWrite8      = 0x04;
        private const byte MsgWrite16     = 0x05;
        private const byte MsgWrite32     = 0x06;
        private const byte MsgWrite64     = 0x07;
        private const byte MsgVersion     = 0x08;
        private const byte MsgSaveState   = 0x09;
        private const byte MsgLoadState   = 0x0A;
        private const byte MsgTitle       = 0x0B;
        private const byte MsgID          = 0x0C;
        private const byte MsgUUID        = 0x0D;
        private const byte MsgGameVersion = 0x0E;
        private const byte MsgStatus      = 0x0F;

        // ── Extended PINE opcodes (PINE_EXTENSION_PROPOSAL.md) ────────────────
        private const byte MsgGetProgramCounter   = 0x10;
        private const byte MsgPause               = 0x11;
        private const byte MsgResume              = 0x12;
        private const byte MsgStep                = 0x13;
        private const byte MsgSetBreakpoint       = 0x14;
        private const byte MsgClearBreakpoint     = 0x15;
        private const byte MsgClearAllBreakpoints = 0x16;
        private const byte MsgGetRegisters        = 0x17;

        // ── PINE result codes ─────────────────────────────────────────────────
        private const byte IPC_OK   = 0x00;
        private const byte IPC_FAIL = 0xFF;

        // ── Batch-mode limits ─────────────────────────────────────────────────
        // Keeps packets well within pine.h MAX_IPC_SIZE (650000 bytes).
        private const int MaxRead32PerBatch  = 4096;
        private const int MaxWrite8PerBatch  = 4096;

        // ── Connection state ──────────────────────────────────────────────────
        private readonly string _host;
        private readonly int    _slot;   // TCP port on Windows; Unix socket suffix on Linux/macOS
        private Socket?  _socket;
        private readonly object _lock = new object();

        // ── Public enums / structs ────────────────────────────────────────────

        /// <summary>Emulator status values returned by <see cref="GetStatus"/>.</summary>
        public enum EmuStatus : uint
        {
            Running  = 0,
            Paused   = 1,
            Shutdown = 2,
        }

        /// <summary>EE register snapshot returned by <see cref="GetRegisters"/>.</summary>
        public sealed class RegisterSnapshot
        {
            /// <summary>All 32 EE general-purpose registers (lower 32-bit halves).</summary>
            public uint[] GPR { get; }
            /// <summary>EE program counter (KSEG0-normalised).</summary>
            public uint PC  { get; }
            /// <summary>HI multiply/divide result register (lower 32 bits).</summary>
            public uint HI  { get; }
            /// <summary>LO multiply/divide result register (lower 32 bits).</summary>
            public uint LO  { get; }

            internal RegisterSnapshot(uint[] gpr, uint pc, uint hi, uint lo)
            {
                GPR = gpr; PC = pc; HI = hi; LO = lo;
            }
        }

        /// <summary>
        /// Creates a new <see cref="PineClient"/>.
        /// </summary>
        /// <param name="host">PCSX2 host address (default <c>"127.0.0.1"</c>).</param>
        /// <param name="slot">PINE slot / TCP port (default <see cref="DefaultSlot"/> = 28011).</param>
        public PineClient(string host = "127.0.0.1", int slot = DefaultSlot)
        {
            _host = host;
            _slot = slot;
        }

        /// <summary>Gets whether the client currently has an open socket connection.</summary>
        public bool IsConnected => _socket?.Connected == true;

        // ═════════════════════════════════════════════════════════════════════
        // Existing PINE API (upstream opcodes 0x00–0x0F)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads <paramref name="length"/> bytes from PCSX2 guest memory starting at
        /// <paramref name="address"/> using batched PINE Read32 commands.
        /// </summary>
        public byte[] ReadBytes(uint address, int length)
        {
            if (length <= 0) return Array.Empty<byte>();

            var result = new byte[length];
            int offset = 0;

            // Handle leading unaligned bytes one at a time.
            while (offset < length && ((address + offset) & 3) != 0)
            {
                result[offset] = ReadOneByte((uint)(address + offset));
                offset++;
            }

            // Batch-read the aligned body using Read32 commands.
            while (offset + 4 <= length)
            {
                int words = Math.Min((length - offset) / 4, MaxRead32PerBatch);
                ReadBatchRead32((uint)(address + offset), words, result, offset);
                offset += words * 4;
            }

            // Handle trailing unaligned bytes one at a time.
            while (offset < length)
            {
                result[offset] = ReadOneByte((uint)(address + offset));
                offset++;
            }

            return result;
        }

        /// <summary>
        /// Writes <paramref name="data"/> to PCSX2 guest memory starting at
        /// <paramref name="address"/> using batched PINE Write8 commands.
        /// </summary>
        public void WriteBytes(uint address, byte[] data)
        {
            if (data == null || data.Length == 0) return;

            int offset = 0;
            while (offset < data.Length)
            {
                int count = Math.Min(data.Length - offset, MaxWrite8PerBatch);
                WriteBatchWrite8((uint)(address + offset), data, offset, count);
                offset += count;
            }
        }

        /// <summary>Returns the current emulator status from PCSX2.</summary>
        public EmuStatus GetStatus()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStatus), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw PineFail("MsgStatus");
            return (EmuStatus)ReadU32LE(rep, 5);
        }

        /// <summary>Returns the PCSX2 emulator version string.</summary>
        public string GetVersion() => QueryString(MsgVersion);

        /// <summary>Returns the running game title.</summary>
        public string GetGameTitle() => QueryString(MsgTitle);

        /// <summary>Returns the running game serial ID (e.g. "SCUS-97113").</summary>
        public string GetGameID() => QueryString(MsgID);

        /// <summary>Returns the running game UUID.</summary>
        public string GetGameUUID() => QueryString(MsgUUID);

        /// <summary>Returns the running game version string.</summary>
        public string GetGameVersion() => QueryString(MsgGameVersion);

        /// <summary>Saves a PCSX2 save-state to <paramref name="slot"/>.</summary>
        public void SaveState(byte slot)
        {
            byte[] rep = SendReceive(BuildStateRequest(MsgSaveState, slot), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgSaveState slot {slot}");
        }

        /// <summary>Loads a PCSX2 save-state from <paramref name="slot"/>.</summary>
        public void LoadState(byte slot)
        {
            byte[] rep = SendReceive(BuildStateRequest(MsgLoadState, slot), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgLoadState slot {slot}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Extended PINE API (opcodes 0x10–0x17 — requires PCSX2 PR)
        // See PINE_EXTENSION_PROPOSAL.md for the C++ implementation.
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Extended PINE] Returns the current EE PC and whether emulation is paused.
        /// Reply: <c>[size(4)][IPC_OK(1)][PC(4)][IsPaused(1)]</c>
        /// </summary>
        /// <param name="isPaused">Set to <c>true</c> if the emulator is currently paused.</param>
        /// <returns>EE program counter (KSEG0-normalised).</returns>
        public uint GetProgramCounter(out bool isPaused)
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgGetProgramCounter), expectedReplySize: 10);
            if (rep[4] != IPC_OK) throw PineFail("MsgGetProgramCounter");
            isPaused = rep[9] != 0;
            return ReadU32LE(rep, 5);
        }

        /// <summary>
        /// [Extended PINE] Pauses EE execution.
        /// </summary>
        public void Pause()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgPause), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgPause");
        }

        /// <summary>
        /// [Extended PINE] Resumes EE execution.
        /// </summary>
        public void Resume()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgResume), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgResume");
        }

        /// <summary>
        /// [Extended PINE] Executes exactly one EE instruction while staying paused,
        /// then returns the new PC.
        /// </summary>
        /// <returns>EE PC after stepping (KSEG0-normalised).</returns>
        /// <exception cref="InvalidOperationException">Thrown if the emulator is not paused.</exception>
        public uint Step()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStep), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw PineFail("MsgStep (emulator must be paused)");
            return ReadU32LE(rep, 5);
        }

        /// <summary>
        /// [Extended PINE] Adds a PC execution breakpoint at <paramref name="address"/>.
        /// </summary>
        public void SetBreakpoint(uint address)
        {
            byte[] rep = SendReceive(BuildAddressRequest(MsgSetBreakpoint, address), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgSetBreakpoint 0x{address:X8}");
        }

        /// <summary>
        /// [Extended PINE] Removes the breakpoint at <paramref name="address"/>.
        /// Returns <c>false</c> when no breakpoint existed at that address.
        /// </summary>
        public bool ClearBreakpoint(uint address)
        {
            byte[] rep = SendReceive(BuildAddressRequest(MsgClearBreakpoint, address), expectedReplySize: 5);
            return rep[4] == IPC_OK;
        }

        /// <summary>
        /// [Extended PINE] Removes all PC breakpoints.
        /// </summary>
        public void ClearAllBreakpoints()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgClearAllBreakpoints), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgClearAllBreakpoints");
        }

        /// <summary>
        /// [Extended PINE] Returns a snapshot of all 32 EE GPRs (lower 32-bit halves),
        /// PC, HI, and LO.
        /// Reply size: 145 bytes.
        /// </summary>
        public RegisterSnapshot GetRegisters()
        {
            // Reply: [size(4)][IPC_OK(1)][r0..r31(128)][PC(4)][HI(4)][LO(4)] = 145 bytes
            byte[] rep = SendReceive(BuildSingleRequest(MsgGetRegisters), expectedReplySize: 145);
            if (rep[4] != IPC_OK) throw PineFail("MsgGetRegisters");

            var gpr = new uint[32];
            for (int i = 0; i < 32; i++)
                gpr[i] = ReadU32LE(rep, 5 + i * 4);

            uint pc = ReadU32LE(rep, 5 + 128);
            uint hi = ReadU32LE(rep, 5 + 132);
            uint lo = ReadU32LE(rep, 5 + 136);
            return new RegisterSnapshot(gpr, pc, hi, lo);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Internal helpers
        // ═════════════════════════════════════════════════════════════════════

        // ── Bulk memory helpers ───────────────────────────────────────────────

        private byte ReadOneByte(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgRead8, address), expectedReplySize: 6);
            if (rep[4] != IPC_OK) throw PineFail($"MsgRead8 0x{address:X8}");
            return rep[5];
        }

        private void ReadBatchRead32(uint baseAddress, int wordCount, byte[] dest, int destOffset)
        {
            // Batch request: [total_size(4)][MsgRead32(1)][addr(4)] × wordCount
            int batchSize = 4 + wordCount * 5;
            var req = new byte[batchSize];
            WriteU32LE(req, 0, (uint)batchSize);
            for (int i = 0; i < wordCount; i++)
            {
                int pos = 4 + i * 5;
                req[pos] = MsgRead32;
                WriteU32LE(req, pos + 1, baseAddress + (uint)(i * 4));
            }

            // Reply: [size(4)][IPC_OK(1)][val(4)] × wordCount
            int expectedReply = 5 + wordCount * 4;
            byte[] rep = SendReceive(req, expectedReply);
            if (rep[4] != IPC_OK) throw PineFail($"Batch MsgRead32 0x{baseAddress:X8}");
            Buffer.BlockCopy(rep, 5, dest, destOffset, wordCount * 4);
        }

        private void WriteBatchWrite8(uint baseAddress, byte[] src, int srcOffset, int count)
        {
            // Batch request: [total_size(4)][MsgWrite8(1)][addr(4)][val(1)] × count
            int batchSize = 4 + count * 6;
            var req = new byte[batchSize];
            WriteU32LE(req, 0, (uint)batchSize);
            for (int i = 0; i < count; i++)
            {
                int pos = 4 + i * 6;
                req[pos] = MsgWrite8;
                WriteU32LE(req, pos + 1, baseAddress + (uint)i);
                req[pos + 5] = src[srcOffset + i];
            }

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"Batch MsgWrite8 0x{baseAddress:X8}");
        }

        // ── String query ──────────────────────────────────────────────────────

        private string QueryString(byte opcode)
        {
            byte[] rep = SendReceiveVariable(BuildSingleRequest(opcode));
            if (rep[4] != IPC_OK) return string.Empty;
            if (rep.Length < 9) return string.Empty;
            int strSize = (int)ReadU32LE(rep, 5);
            if (strSize <= 0 || 9 + strSize > rep.Length) return string.Empty;
            return Encoding.UTF8.GetString(rep, 9, strSize);
        }

        // ── Request builders ──────────────────────────────────────────────────

        /// <summary>Builds a zero-arg command: [5][opcode]</summary>
        private static byte[] BuildSingleRequest(byte opcode)
        {
            var buf = new byte[5];
            WriteU32LE(buf, 0, 5u);
            buf[4] = opcode;
            return buf;
        }

        /// <summary>Builds a read-address command: [9][opcode][addr]</summary>
        private static byte[] BuildReadRequest(byte opcode, uint address)
        {
            var buf = new byte[9];
            WriteU32LE(buf, 0, 9u);
            buf[4] = opcode;
            WriteU32LE(buf, 5, address);
            return buf;
        }

        /// <summary>Builds an address-arg command: [9][opcode][addr]  (breakpoints, etc.)</summary>
        private static byte[] BuildAddressRequest(byte opcode, uint address)
            => BuildReadRequest(opcode, address); // same layout

        /// <summary>Builds a savestate/loadstate command: [6][opcode][slot]</summary>
        private static byte[] BuildStateRequest(byte opcode, byte slot)
        {
            var buf = new byte[6];
            WriteU32LE(buf, 0, 6u);
            buf[4] = opcode;
            buf[5] = slot;
            return buf;
        }

        // ── Transport ─────────────────────────────────────────────────────────

        private byte[] SendReceive(byte[] request, int expectedReplySize)
        {
            lock (_lock)
            {
                EnsureConnected();
                try { return DoSendReceive(request, expectedReplySize); }
                catch (SocketException) { Disconnect(); EnsureConnected(); return DoSendReceive(request, expectedReplySize); }
                catch (IOException)     { Disconnect(); EnsureConnected(); return DoSendReceive(request, expectedReplySize); }
            }
        }

        private byte[] SendReceiveVariable(byte[] request)
        {
            lock (_lock)
            {
                EnsureConnected();
                try { return DoSendReceiveVariable(request); }
                catch (SocketException) { Disconnect(); EnsureConnected(); return DoSendReceiveVariable(request); }
                catch (IOException)     { Disconnect(); EnsureConnected(); return DoSendReceiveVariable(request); }
            }
        }

        private byte[] DoSendReceive(byte[] request, int expectedReplySize)
        {
            var ns = new NetworkStream(_socket!, ownsSocket: false);
            ns.Write(request, 0, request.Length);
            ns.Flush();
            var buf = new byte[expectedReplySize];
            int received = 0;
            while (received < expectedReplySize)
            {
                int n = ns.Read(buf, received, expectedReplySize - received);
                if (n == 0) throw new IOException("Connection closed by PCSX2 PINE server");
                received += n;
            }
            return buf;
        }

        private byte[] DoSendReceiveVariable(byte[] request)
        {
            var ns = new NetworkStream(_socket!, ownsSocket: false);
            ns.Write(request, 0, request.Length);
            ns.Flush();

            var header = new byte[4];
            int got = 0;
            while (got < 4) { int n = ns.Read(header, got, 4 - got); if (n == 0) throw new IOException("PINE connection closed"); got += n; }
            int totalSize = (int)ReadU32LE(header, 0);
            if (totalSize < 4 || totalSize > 1_000_000)
                throw new InvalidDataException($"PINE reply has implausible size {totalSize}");

            var buf = new byte[totalSize];
            Buffer.BlockCopy(header, 0, buf, 0, 4);
            got = 4;
            while (got < totalSize) { int n = ns.Read(buf, got, totalSize - got); if (n == 0) throw new IOException("PINE connection closed"); got += n; }
            return buf;
        }

        private void EnsureConnected()
        {
            if (_socket?.Connected == true) return;
            _socket?.Dispose();
            Connect();
        }

        private void Connect()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(_host, _slot);
            }
            else
            {
                // Linux / macOS: PINE uses a Unix-domain socket named /tmp/pcsx2.<slot>
                string socketPath = $"/tmp/pcsx2.{_slot}";
                var ep = new UnixDomainSocketEndPoint(socketPath);
                _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _socket.Connect(ep);
            }
        }

        private void Disconnect()
        {
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket?.Dispose();
            _socket = null;
        }

        // ── Byte-order helpers ────────────────────────────────────────────────

        private static void WriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset]     = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >>  8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static uint ReadU32LE(byte[] buf, int offset)
            => (uint)(buf[offset]
                    | (buf[offset + 1] <<  8)
                    | (buf[offset + 2] << 16)
                    | (buf[offset + 3] << 24));

        private static InvalidOperationException PineFail(string op)
            => new InvalidOperationException($"PINE IPC_FAIL on {op}");

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose() => Disconnect();
    }
}
