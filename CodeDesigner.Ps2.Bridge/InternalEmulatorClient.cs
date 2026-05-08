using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Client for the built-in emulator's <c>InternalPineSocket</c> (hosted in
    /// <c>CodeDesigner.Ps2.Emulator</c>). Mirrors the PCSX2 PINE wire protocol used by
    /// <see cref="PineClient"/> so Bridge can drive PCSX2 and the built-in emulator
    /// with the same command vocabulary, just pointed at different ports.
    /// <para>
    /// Default port: <see cref="DefaultPort"/> (28012). PCSX2 PINE lives on 28011.
    /// </para>
    /// <para>
    /// This is a deliberately small initial surface — read/write memory, basic
    /// execution control, status, breakpoints — covering what Bridge handlers
    /// need today. Additional opcodes can be added in lockstep with
    /// <c>InternalPineSocket</c> as the handler migration progresses.
    /// </para>
    /// </summary>
    public sealed class InternalEmulatorClient : IDebugClient
    {
        public const int DefaultPort = 28012;

        // ── Opcodes (mirror PineClient + InternalPineSocket) ──────────────────
        private const byte MsgRead8       = 0x00;
        private const byte MsgRead16      = 0x01;
        private const byte MsgRead32      = 0x02;
        private const byte MsgRead64      = 0x03;
        private const byte MsgWrite8      = 0x04;
        private const byte MsgWrite16     = 0x05;
        private const byte MsgWrite32     = 0x06;
        private const byte MsgWrite64     = 0x07;
        private const byte MsgStatus      = 0x0F;

        private const byte MsgGetProgramCounter   = 0x10;
        private const byte MsgPause               = 0x11;
        private const byte MsgResume              = 0x12;
        private const byte MsgStep                = 0x13;
        private const byte MsgSetBreakpoint       = 0x14;
        private const byte MsgClearBreakpoint     = 0x15;
        private const byte MsgClearAllBreakpoints = 0x16;
        private const byte MsgSetProgramCounter   = 0x17;
        private const byte MsgWriteBuffer         = 0x18;
        private const byte MsgReadBuffer          = 0x19;
        private const byte MsgReset               = 0x24;
        private const byte MsgInternalRequestRestart = 0x80;

        private const byte IPC_OK   = 0x00;
        private const byte IPC_FAIL = 0xFF;

        public enum EmuStatus : uint { Running = 0, Paused = 1, Shutdown = 2 }

        private readonly string _host;
        private readonly int    _port;
        private Socket?  _socket;
        private readonly object _lock = new object();

        public InternalEmulatorClient(string host = "127.0.0.1", int port = DefaultPort)
        {
            _host = host;
            _port = port;
        }

        public bool IsConnected => _socket?.Connected == true;

        // ── Public API ────────────────────────────────────────────────────────

        public byte[] ReadBytes(uint address, int length)
        {
            if (length <= 0) return Array.Empty<byte>();

            // Use the bulk MsgReadBuffer opcode so scanners that walk multi-MB
            // regions (label, string, value, function, pointer) finish in
            // seconds instead of timing out. Chunk to 1 MiB so each packet
            // stays manageable.
            const int ChunkSize = 1024 * 1024;
            var result = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int len = Math.Min(ChunkSize, length - offset);
                byte[] req = new byte[5 + 4 + 4];
                WriteU32LE(req, 0, (uint)req.Length);
                req[4] = MsgReadBuffer;
                WriteU32LE(req, 5, (uint)(address + offset));
                WriteU32LE(req, 9, (uint)len);
                // Reply layout: u32 size + u8 status + payload of `len` bytes.
                byte[] rep = SendReceive(req, expectedReplySize: 5 + len);
                if (rep[4] != IPC_OK) throw Fail($"MsgReadBuffer 0x{address + (uint)offset:X8} +{len}");
                Buffer.BlockCopy(rep, 5, result, offset, len);
                offset += len;
            }
            return result;
        }

        public void WriteBytes(uint address, byte[] data)
        {
            if (data == null || data.Length == 0) return;

            // Use the bulk MsgWriteBuffer opcode so loading a 32 MB dump is a
            // handful of round-trips, not 33 million. The protocol uses a u32
            // length so we chunk anything bigger than ~4 GB; 1 MiB chunks keep
            // each packet manageable.
            const int ChunkSize = 1024 * 1024;
            int offset = 0;
            while (offset < data.Length)
            {
                int len = Math.Min(ChunkSize, data.Length - offset);
                byte[] req = new byte[5 + 4 + 4 + len];
                WriteU32LE(req, 0, (uint)req.Length);
                req[4] = MsgWriteBuffer;
                WriteU32LE(req, 5, address + (uint)offset);
                WriteU32LE(req, 9, (uint)len);
                Buffer.BlockCopy(data, offset, req, 13, len);
                byte[] rep = SendReceive(req, expectedReplySize: 5);
                if (rep[4] != IPC_OK) throw Fail($"MsgWriteBuffer 0x{address + (uint)offset:X8} +{len}");
                offset += len;
            }
        }

        /// <summary>
        /// Set the EE program counter on the running emulator. Used by file-mode
        /// load to point execution at the freshly written binary's entry point.
        /// </summary>
        public void SetProgramCounter(uint pc)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgSetProgramCounter, pc), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail($"MsgSetProgramCounter 0x{pc:X8}");
        }

        public byte ReadByte(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgRead8, address), expectedReplySize: 6);
            if (rep[4] != IPC_OK) throw Fail($"MsgRead8 0x{address:X8}");
            return rep[5];
        }

        public ushort ReadHalf(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgRead16, address), expectedReplySize: 7);
            if (rep[4] != IPC_OK) throw Fail($"MsgRead16 0x{address:X8}");
            return (ushort)(rep[5] | (rep[6] << 8));
        }

        public uint ReadWord(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgRead32, address), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw Fail($"MsgRead32 0x{address:X8}");
            return ReadU32LE(rep, 5);
        }

        public ulong ReadDoubleWord(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgRead64, address), expectedReplySize: 13);
            if (rep[4] != IPC_OK) throw Fail($"MsgRead64 0x{address:X8}");
            return ReadU32LE(rep, 5) | ((ulong)ReadU32LE(rep, 9) << 32);
        }

        public void WriteByte(uint address, byte value)
        {
            var req = new byte[10];
            WriteU32LE(req, 0, 10u);
            req[4] = MsgWrite8;
            WriteU32LE(req, 5, address);
            req[9] = value;
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail($"MsgWrite8 0x{address:X8}");
        }

        public EmuStatus GetStatus()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStatus), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw Fail("MsgStatus");
            return (EmuStatus)ReadU32LE(rep, 5);
        }

        public uint GetProgramCounter()
        {
            return GetProgramCounter(out _);
        }

        public uint GetProgramCounter(out bool isPaused)
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgGetProgramCounter), expectedReplySize: 10);
            if (rep[4] != IPC_OK) throw Fail("MsgGetProgramCounter");
            isPaused = rep[9] != 0;
            return ReadU32LE(rep, 5);
        }

        public DebugClientStatus GetStatusGeneric()
            => (DebugClientStatus)(uint)GetStatus();

        public void Pause()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgPause), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail("MsgPause");
        }

        public void Resume()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgResume), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail("MsgResume");
        }

        public uint Step()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStep), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw Fail("MsgStep");
            return ReadU32LE(rep, 5);
        }

        public void SetBreakpoint(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgSetBreakpoint, address), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail("MsgSetBreakpoint");
        }

        public bool ClearBreakpoint(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgClearBreakpoint, address), expectedReplySize: 5);
            return rep[4] == IPC_OK;
        }

        public void ClearAllBreakpoints()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgClearAllBreakpoints), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail("MsgClearAllBreakpoints");
        }

        /// <summary>
        /// Internal-only opcode — asks the emulator host to reload BIOS+disc
        /// (the built-in equivalent of PCSX2's reset). Mirrors
        /// <c>InternalPineSocket.MsgInternalRequestRestart</c>.
        /// </summary>
        public void RequestRestart()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgInternalRequestRestart), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw Fail("MsgInternalRequestRestart");
        }

        public void Dispose()
        {
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket?.Dispose();
            _socket = null;
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private byte ReadOneByte(uint address) => ReadByte(address);
        private void WriteOneByte(uint address, byte value) => WriteByte(address, value);

        private static byte[] BuildSingleRequest(byte opcode)
        {
            var buf = new byte[5];
            WriteU32LE(buf, 0, 5u);
            buf[4] = opcode;
            return buf;
        }

        private static byte[] BuildReadRequest(byte opcode, uint address)
        {
            var buf = new byte[9];
            WriteU32LE(buf, 0, 9u);
            buf[4] = opcode;
            WriteU32LE(buf, 5, address);
            return buf;
        }

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

        private byte[] DoSendReceive(byte[] request, int expectedReplySize)
        {
            using var ns = new NetworkStream(_socket!, ownsSocket: false);
            ns.Write(request, 0, request.Length);
            ns.Flush();
            var buf = new byte[expectedReplySize];
            int received = 0;
            while (received < expectedReplySize)
            {
                int n = ns.Read(buf, received, expectedReplySize - received);
                if (n == 0) throw new IOException("InternalPineSocket connection closed");
                received += n;
            }
            return buf;
        }

        private void EnsureConnected()
        {
            if (_socket?.Connected == true) return;
            _socket?.Dispose();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(_host, _port);
        }

        private void Disconnect()
        {
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket?.Dispose();
            _socket = null;
        }

        private static InvalidOperationException Fail(string what)
            => new InvalidOperationException($"InternalPineSocket {what} returned IPC_FAIL");

        private static uint ReadU32LE(byte[] buf, int offset)
            => (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

        private static void WriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset    ] = (byte)(value      );
            buf[offset + 1] = (byte)(value >> 8 );
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }
    }
}
