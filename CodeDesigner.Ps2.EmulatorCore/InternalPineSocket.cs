using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CodeDesigner.Ps2.EmulatorCore
{
    /// <summary>
    /// Server-side counterpart of <c>CodeDesigner.Ps2.Bridge.PineClient</c>.
    /// Implements the same wire protocol as PCSX2's PINE IPC so the Bridge can
    /// drive the built-in emulator with the same client code it uses for PCSX2,
    /// just pointed at a different port.
    /// <para>
    /// <b>Wire format</b> (matches PINE):
    ///   Request:  <c>[total_size : u32 LE][opcode : u8][args…]</c><br/>
    ///   Reply:    <c>[total_size : u32 LE][status : u8][data…]</c><br/>
    ///   status = 0x00 (IPC_OK) or 0xFF (IPC_FAIL).
    /// </para>
    /// <para>
    /// A request packet may pack multiple commands back-to-back inside the body;
    /// the server processes them in order and produces one concatenated reply.
    /// This matches PineClient's batched <c>ReadBatchRead32</c>/<c>WriteBatchWrite8</c>.
    /// </para>
    /// <para>
    /// This is a minimal initial implementation covering the opcodes Bridge needs
    /// for memory access, basic execution control, and status queries. Additional
    /// opcodes (registers, threads, modules, watchpoints, pad, network) can be
    /// filled in incrementally — unsupported opcodes return IPC_FAIL.
    /// </para>
    /// </summary>
    public sealed class InternalPineSocket : IDisposable
    {
        public const int DefaultPort = 28012;

        // ── Opcodes (mirror CodeDesigner.Ps2.Bridge.PineClient) ───────────────
        private const byte MsgRead8       = 0x00;
        private const byte MsgRead16      = 0x01;
        private const byte MsgRead32      = 0x02;
        private const byte MsgRead64      = 0x03;
        private const byte MsgWrite8      = 0x04;
        private const byte MsgWrite16     = 0x05;
        private const byte MsgWrite32     = 0x06;
        private const byte MsgWrite64     = 0x07;
        private const byte MsgVersion     = 0x08;
        private const byte MsgTitle       = 0x0B;
        private const byte MsgID          = 0x0C;
        private const byte MsgUUID        = 0x0D;
        private const byte MsgGameVersion = 0x0E;
        private const byte MsgStatus      = 0x0F;

        private const byte MsgGetProgramCounter   = 0x10;
        private const byte MsgPause               = 0x11;
        private const byte MsgResume              = 0x12;
        private const byte MsgStep                = 0x13;
        private const byte MsgSetBreakpoint       = 0x14;
        private const byte MsgClearBreakpoint     = 0x15;
        private const byte MsgClearAllBreakpoints = 0x16;
        // Internal-only set-PC. Used by Bridge's file-mode loader to point the
        // EE at the entry point after writing a binary into RAM.
        private const byte MsgSetProgramCounter   = 0x17;
        // Internal-only bulk write: writes N bytes at a base address in one
        // round-trip. Required for file-mode loading — byte-at-a-time would
        // take minutes for a 32 MB dump.
        private const byte MsgWriteBuffer         = 0x18;
        // Internal-only bulk read: reads N bytes at a base address in one
        // round-trip. Required for label/string/value scanners that walk
        // multi-MB regions — without this they make millions of round-trips
        // and time out.
        private const byte MsgReadBuffer          = 0x19;
        private const byte MsgReset               = 0x24;

        // Internal-only opcodes (high range, won't collide with PCSX2 PINE).
        // 0x80 = signal pending restart so the host run-loop reinitialises BIOS+disc.
        private const byte MsgInternalRequestRestart = 0x80;

        private const byte IPC_OK   = 0x00;
        private const byte IPC_FAIL = 0xFF;

        private DualCpuScheduler _scheduler;
        private readonly int _port;
        private readonly object _swapLock = new object();
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;
        private volatile bool _pendingRestart;

        public InternalPineSocket(DualCpuScheduler scheduler, int port = DefaultPort)
        {
            _scheduler = scheduler;
            _port      = port;
        }

        /// <summary>
        /// Set when a client requests a restart via <c>MsgInternalRequestRestart</c>.
        /// The host run-loop polls this and reinitialises the scheduler.
        /// </summary>
        public bool PendingRestart => _pendingRestart;

        /// <summary>Reset the pending-restart flag once the host has handled it.</summary>
        public void ClearPendingRestart() => _pendingRestart = false;

        /// <summary>Replace the active scheduler (e.g. after a restart).</summary>
        public void SwapScheduler(DualCpuScheduler newScheduler)
        {
            lock (_swapLock) _scheduler = newScheduler;
        }

        public void Start()
        {
            if (_listener != null) throw new InvalidOperationException("InternalPineSocket already started");
            _cts      = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _acceptTask = AcceptLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
        }

        public void Dispose() => Stop();

        // ── Accept / per-connection loop ──────────────────────────────────────

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException)    { return; }

                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                while (!ct.IsCancellationRequested)
                {
                    // Read header (4 bytes) — total size of this request packet.
                    var header = new byte[4];
                    int got = 0;
                    while (got < 4)
                    {
                        int n;
                        try { n = await stream.ReadAsync(header.AsMemory(got, 4 - got), ct).ConfigureAwait(false); }
                        catch { return; }
                        if (n == 0) return; // peer closed
                        got += n;
                    }
                    int totalSize = (int)ReadU32LE(header, 0);
                    if (totalSize < 5 || totalSize > 1_000_000) return;

                    var packet = new byte[totalSize];
                    Buffer.BlockCopy(header, 0, packet, 0, 4);
                    while (got < totalSize)
                    {
                        int n;
                        try { n = await stream.ReadAsync(packet.AsMemory(got, totalSize - got), ct).ConfigureAwait(false); }
                        catch { return; }
                        if (n == 0) return;
                        got += n;
                    }

                    byte[] reply = ProcessPacket(packet);
                    try { await stream.WriteAsync(reply, ct).ConfigureAwait(false); }
                    catch { return; }
                }
            }
        }

        // ── Request dispatcher ────────────────────────────────────────────────

        /// <summary>
        /// Process a request packet and produce the matching reply.
        /// The packet body (after the 4-byte size) may contain one or more
        /// commands back-to-back; reply data from each command is concatenated.
        /// </summary>
        private byte[] ProcessPacket(byte[] packet)
        {
            using var ms = new MemoryStream();
            // Reserve 4 bytes for size + 1 for status; we fill them at the end.
            ms.Position = 5;
            byte status = IPC_OK;

            int pos = 4;
            while (pos < packet.Length)
            {
                byte op = packet[pos++];
                try
                {
                    switch (op)
                    {
                        case MsgRead8:
                            pos = HandleRead8(packet, pos, ms); break;
                        case MsgRead16:
                            pos = HandleRead16(packet, pos, ms); break;
                        case MsgRead32:
                            pos = HandleRead32(packet, pos, ms); break;
                        case MsgRead64:
                            pos = HandleRead64(packet, pos, ms); break;
                        case MsgWrite8:
                            pos = HandleWrite8(packet, pos); break;
                        case MsgWrite16:
                            pos = HandleWrite16(packet, pos); break;
                        case MsgWrite32:
                            pos = HandleWrite32(packet, pos); break;
                        case MsgWrite64:
                            pos = HandleWrite64(packet, pos); break;
                        case MsgVersion:
                            WriteString(ms, "CodeDesigner.Ps2.EmulatorCore InternalPineSocket 1.0"); break;
                        case MsgTitle:
                            WriteString(ms, "CodeDesigner Built-In Emulator"); break;
                        case MsgID:
                        case MsgUUID:
                        case MsgGameVersion:
                            WriteString(ms, ""); break;
                        case MsgStatus:
                            WriteU32LE(ms, _scheduler.Paused ? 1u : 0u); break;
                        case MsgGetProgramCounter:
                            WriteU32LE(ms, (uint)(_scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
                            ms.WriteByte(_scheduler.Paused ? (byte)1 : (byte)0);
                            break;
                        case MsgPause:
                            _scheduler.Paused = true; break;
                        case MsgResume:
                            // Skip the current PC if it sits on a breakpoint —
                            // otherwise the BP would re-fire immediately. Match
                            // PCSX2 PINE's behaviour by stepping past it once.
                            ResumeWithBreakpointSkip();
                            break;
                        case MsgStep:
                            DoStep(ms); break;
                        case MsgSetBreakpoint:
                            pos = HandleBreakpointAdd(packet, pos); break;
                        case MsgClearBreakpoint:
                            pos = HandleBreakpointRemove(packet, pos); break;
                        case MsgClearAllBreakpoints:
                            _scheduler.Breakpoints.Clear(); break;
                        case MsgSetProgramCounter:
                            pos = HandleSetProgramCounter(packet, pos); break;
                        case MsgWriteBuffer:
                            pos = HandleWriteBuffer(packet, pos); break;
                        case MsgReadBuffer:
                            pos = HandleReadBuffer(packet, pos, ms); break;
                        case MsgReset:
                        case MsgInternalRequestRestart:
                            _pendingRestart = true; break;

                        default:
                            // Unknown / not-yet-implemented opcode — fail the whole packet.
                            status = IPC_FAIL;
                            goto done;
                    }
                }
                catch
                {
                    status = IPC_FAIL;
                    goto done;
                }
            }

        done:
            byte[] reply = ms.ToArray();
            uint size = (uint)reply.Length;
            // Write size header and status.
            reply[0] = (byte)(size      );
            reply[1] = (byte)(size >> 8 );
            reply[2] = (byte)(size >> 16);
            reply[3] = (byte)(size >> 24);
            reply[4] = status;
            return reply;
        }

        // ── Per-opcode handlers ──────────────────────────────────────────────

        private int HandleRead8(byte[] p, int pos, MemoryStream reply)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            byte v;
            lock (_swapLock) v = _scheduler.EE.Bus.ReadByte(addr);
            reply.WriteByte(v);
            return pos;
        }

        private int HandleRead16(byte[] p, int pos, MemoryStream reply)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            ushort v;
            lock (_swapLock) v = _scheduler.EE.Bus.ReadHalf(addr);
            reply.WriteByte((byte)v);
            reply.WriteByte((byte)(v >> 8));
            return pos;
        }

        private int HandleRead32(byte[] p, int pos, MemoryStream reply)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            uint v;
            lock (_swapLock) v = _scheduler.EE.Bus.ReadWord(addr);
            WriteU32LE(reply, v);
            return pos;
        }

        private int HandleRead64(byte[] p, int pos, MemoryStream reply)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            ulong v;
            lock (_swapLock) v = _scheduler.EE.Bus.ReadDoubleWord(addr);
            WriteU64LE(reply, v);
            return pos;
        }

        private int HandleWrite8(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            byte v = p[pos++];
            lock (_swapLock) _scheduler.EE.Bus.WriteByte(addr, v);
            return pos;
        }

        private int HandleWrite16(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            ushort v = (ushort)(p[pos] | (p[pos + 1] << 8));
            pos += 2;
            lock (_swapLock) _scheduler.EE.Bus.WriteHalf(addr, v);
            return pos;
        }

        private int HandleWrite32(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            uint v = ReadU32LE(p, pos); pos += 4;
            lock (_swapLock) _scheduler.EE.Bus.WriteWord(addr, v);
            return pos;
        }

        private int HandleWrite64(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            ulong v = ReadU64LE(p, pos); pos += 8;
            lock (_swapLock) _scheduler.EE.Bus.WriteDoubleWord(addr, v);
            return pos;
        }

        /// <summary>
        /// Set the EE program counter. Used by file-mode load to jump to the
        /// freshly written binary's entry point. Wire format: u32 PC.
        /// </summary>
        private int HandleSetProgramCounter(byte[] p, int pos)
        {
            uint pc = ReadU32LE(p, pos); pos += 4;
            lock (_swapLock) _scheduler.EE.Cpu.PC = pc;
            return pos;
        }

        /// <summary>
        /// Bulk read: <c>u32 address, u32 length</c>. Reply body is
        /// <c>byte[length]</c>. Mirrors the byte-level read path so VTLB /
        /// IOP-mirror semantics stay identical to <see cref="MsgRead8"/>.
        /// </summary>
        private int HandleReadBuffer(byte[] p, int pos, MemoryStream reply)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            uint len  = ReadU32LE(p, pos); pos += 4;
            // Cap to a few MB so a malformed length can't allocate gigabytes.
            if (len > 16 * 1024 * 1024)
                throw new InvalidOperationException("ReadBuffer length too large");

            lock (_swapLock)
            {
                var bus = _scheduler.EE.Bus;
                for (uint i = 0; i < len; i++)
                    reply.WriteByte(bus.ReadByte(addr + i));
            }
            return pos;
        }

        /// <summary>
        /// Bulk write: <c>u32 address, u32 length, byte[length] data</c>. Goes
        /// through the same <see cref="PS2MemoryBus.WriteByte"/> path as the
        /// per-byte opcode so VTLB / IOP-mirror semantics remain identical.
        /// </summary>
        private int HandleWriteBuffer(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            uint len  = ReadU32LE(p, pos); pos += 4;
            // Bound-check against the remaining packet so a malformed length
            // can't read past the buffer.
            if (len > (uint)(p.Length - pos))
                throw new InvalidOperationException("WriteBuffer length exceeds packet");

            lock (_swapLock)
            {
                var bus = _scheduler.EE.Bus;
                for (uint i = 0; i < len; i++)
                    bus.WriteByte(addr + i, p[pos + (int)i]);
            }
            pos += (int)len;
            return pos;
        }

        /// <summary>
        /// Strip KSEG0/KSEG1 segment bits from a virtual address. The scheduler
        /// stores breakpoints as physical addresses and compares them against
        /// physically-normalised PCs (see <c>DualCpuScheduler.NormalizeToPhysical</c>),
        /// so we must drop incoming KSEG aliases here or BPs from the front
        /// end (which sends KSEG0) will never match the running PC.
        /// </summary>
        private static uint NormaliseBreakpointAddress(uint vaddr)
        {
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu)
                return vaddr & 0x1FFFFFFFu;
            return vaddr;
        }

        private int HandleBreakpointAdd(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            uint phys = NormaliseBreakpointAddress(addr);
            lock (_swapLock) _scheduler.Breakpoints.Add(phys);
            return pos;
        }

        private int HandleBreakpointRemove(byte[] p, int pos)
        {
            uint addr = ReadU32LE(p, pos); pos += 4;
            uint phys = NormaliseBreakpointAddress(addr);
            lock (_swapLock) _scheduler.Breakpoints.Remove(phys);
            return pos;
        }

        /// <summary>
        /// Resume the scheduler, but if the current PC matches a registered
        /// breakpoint, temporarily remove that breakpoint, single-step past it,
        /// and re-add it. Without this dance the BP would re-fire immediately
        /// on resume.
        /// </summary>
        private void ResumeWithBreakpointSkip()
        {
            lock (_swapLock)
            {
                uint currentPc = (uint)(_scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                uint phys = (currentPc & 0x80000000u) != 0 ? currentPc & 0x1FFFFFFFu : currentPc;
                bool hadBp = _scheduler.Breakpoints.Contains(phys);
                if (hadBp)
                {
                    _scheduler.Breakpoints.Remove(phys);
                    try { _scheduler.RunSlice(1); } catch { /* ignore */ }
                    _scheduler.Breakpoints.Add(phys);
                }
                _scheduler.Paused = false;
            }
        }

        private void DoStep(MemoryStream reply)
        {
            lock (_swapLock)
            {
                bool wasPaused = _scheduler.Paused;
                _scheduler.Paused = false;
                try { _scheduler.RunSlice(1); }
                catch { /* swallow — caller still gets the new PC */ }
                _scheduler.Paused = wasPaused || true; // pause again after single step
                WriteU32LE(reply, (uint)(_scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
            }
        }

        // ── Byte-order helpers ───────────────────────────────────────────────

        private static uint ReadU32LE(byte[] buf, int offset)
            => (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

        private static ulong ReadU64LE(byte[] buf, int offset)
        {
            ulong lo = ReadU32LE(buf, offset);
            ulong hi = ReadU32LE(buf, offset + 4);
            return (hi << 32) | lo;
        }

        private static void WriteU32LE(MemoryStream ms, uint v)
        {
            ms.WriteByte((byte)(v      ));
            ms.WriteByte((byte)(v >> 8 ));
            ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 24));
        }

        private static void WriteU64LE(MemoryStream ms, ulong v)
        {
            WriteU32LE(ms, (uint)(v      ));
            WriteU32LE(ms, (uint)(v >> 32));
        }

        private static void WriteString(MemoryStream ms, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            // PINE includes the null terminator in the size field.
            uint size = (uint)bytes.Length + 1;
            WriteU32LE(ms, size);
            ms.Write(bytes, 0, bytes.Length);
            ms.WriteByte(0);
        }
    }
}
