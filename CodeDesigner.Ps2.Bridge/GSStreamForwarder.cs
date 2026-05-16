using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Fans out PCSX2's GS framebuffer stream from one inbound socket to many
    /// outbound subscribers, with a latest-frame slot for on-demand polling.
    ///
    /// <para>
    /// Topology:
    /// </para>
    /// <code>
    ///   PCSX2 GSStreamServer ──TCP (binary)──▶ GSStreamForwarder ──┬─▶ subscriber 1 (e.g. VS Code webview)
    ///                                                               ├─▶ subscriber 2
    ///                                                               └─▶ latest-frame slot ──▶ MCP GETGSFRAME
    /// </code>
    ///
    /// <para>
    /// The high-rate path (binary push to webview) and the on-demand path
    /// (MCP polling the latest frame) both go through the bridge so the MCP
    /// server has equal access — which is the reason the bridge is in the
    /// loop in the first place.
    /// </para>
    ///
    /// <para>
    /// Wire format on the inbound socket and rebroadcast unchanged to
    /// outbound subscribers (little-endian, per frame):
    /// </para>
    /// <code>
    ///   u32 size       // bytes following this field (header + payload)
    ///   u8  type       // 1 = frame
    ///   u32 frame_idx
    ///   u16 width
    ///   u16 height
    ///   u8  codec      // 1 = MJPEG
    ///   u8  flags
    ///   bytes payload
    /// </code>
    /// </summary>
    public sealed class GSStreamForwarder : IDisposable
    {
        public const int DefaultListenerPort = 8766;

        private const byte MsgTypeFrame   = 1;
        private const int  HeaderTotal    = 15;       // 4 size + 11 fields
        private const int  MaxFrameBytes  = 4 * 1024 * 1024; // hard cap to refuse junk

        private readonly int _listenPort;
        private TcpListener? _listener;
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptTask;

        // Inbound side (this process is the TCP client of PCSX2's GSStreamServer).
        private TcpClient? _inbound;
        private Task? _readerTask;
        private PineClient? _attachedPineClient;
        private readonly object _attachLock = new();

        // Subscribers (this process is the TCP server for the webview / others).
        private readonly object _subscribersLock = new();
        private readonly List<TcpClient> _subscribers = new();

        // Latest-frame slot — drop-newest, single slot. Polled by GetLatestFrame()
        // (MCP GETGSFRAME). Lock-protected because reads and writes can race.
        private readonly object _latestLock = new();
        private FrameSnapshot? _latest;

        // Stats
        private long _framesIn;
        private long _framesOut;
        private long _subscriberDrops;

        public GSStreamForwarder(int listenPort = DefaultListenerPort)
        {
            _listenPort = listenPort;
        }

        /// <summary>The port this forwarder accepts subscribers on.</summary>
        public int ListenerPort => _listenPort;

        /// <summary>True when an inbound connection to PCSX2 is established.</summary>
        public bool IsConnectedToPcsx2 => _inbound?.Connected == true;

        public int SubscriberCount
        {
            get { lock (_subscribersLock) return _subscribers.Count; }
        }

        public long FramesReceived  => Interlocked.Read(ref _framesIn);
        public long FramesForwarded => Interlocked.Read(ref _framesOut);
        public long SubscriberDrops => Interlocked.Read(ref _subscriberDrops);

        /// <summary>
        /// Bring up the subscriber-facing listener. Idempotent — safe to call
        /// multiple times; only the first call has effect.
        /// </summary>
        public void Start()
        {
            if (_listener != null)
                return;

            _listener = new TcpListener(IPAddress.Loopback, _listenPort);
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoop(_cts.Token));
            Console.WriteLine($"GSStream: forwarder listening on 127.0.0.1:{_listenPort}");
        }

        /// <summary>
        /// Stop accepting subscribers, drop the inbound link, and close all
        /// connected clients. Safe to call from <see cref="Dispose"/>.
        /// </summary>
        public void Stop()
        {
            try { _cts.Cancel(); } catch { /* swallow */ }

            try { _listener?.Stop(); } catch { /* swallow */ }
            _listener = null;

            DetachPineClient();

            lock (_subscribersLock)
            {
                foreach (var sub in _subscribers)
                {
                    try { sub.Close(); } catch { /* swallow */ }
                }
                _subscribers.Clear();
            }
        }

        /// <summary>
        /// Discover PCSX2's GSStreamServer port via the PINE control plane and
        /// open a long-lived inbound socket. Frames flow as soon as PCSX2 has
        /// any subscriber — so the inbound reader will block on the socket until
        /// PCSX2 starts pushing.
        ///
        /// <para>
        /// Failure here is non-fatal — older PCSX2 builds without GSStreamServer
        /// support will return port=0 (or fail the opcode entirely), and the
        /// bridge continues operating with the GS stream disabled.
        /// </para>
        /// </summary>
        public void AttachPineClient(PineClient pine)
        {
            lock (_attachLock)
            {
                _attachedPineClient = pine;
            }

            // Close any previous inbound link so we always reflect the current pine.
            DetachPineClient(clearAttached: false);

            ushort port;
            try
            {
                port = pine.GSStreamGetPort();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GSStream: PCSX2 build does not expose GSStreamServer ({ex.Message}). Live frames disabled.");
                return;
            }

            if (port == 0)
            {
                Console.WriteLine("GSStream: PCSX2 reported port 0 — listener not initialized. Live frames disabled.");
                return;
            }

            try
            {
                var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.NoDelay = true;
                _inbound = client;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GSStream: failed to connect to PCSX2 GS stream port {port}: {ex.Message}");
                return;
            }

            _readerTask = Task.Run(() => InboundReadLoop(_inbound, _cts.Token));

            Console.WriteLine($"GSStream: connected to PCSX2 GSStreamServer on port {port}.");
        }

        /// <summary>
        /// Drop the inbound connection without affecting subscribers. Called
        /// when the active <see cref="PineClient"/> is replaced or removed.
        /// </summary>
        public void DetachPineClient(bool clearAttached = true)
        {
            if (clearAttached)
            {
                lock (_attachLock)
                {
                    _attachedPineClient = null;
                }
            }

            try { _inbound?.Close(); } catch { /* swallow */ }
            _inbound = null;
            _readerTask = null;

            // Clear the latest-frame slot so MCP polls don't return stale frames
            // from a previous PCSX2 session.
            lock (_latestLock) _latest = null;
        }

        /// <summary>
        /// Most-recent frame received from PCSX2, or null if no frame has been
        /// delivered since the inbound connection came up. Snapshot is owned by
        /// the caller; the byte array is safe to read.
        /// </summary>
        public FrameSnapshot? GetLatestFrame()
        {
            lock (_latestLock) return _latest;
        }

        // ── Subscriber acceptance ────────────────────────────────────────────

        private async Task AcceptLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _listener != null)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException)    { break; }

                    client.NoDelay = true;
                    // Don't let one slow client wedge the broadcaster: writes
                    // that take longer than this drop the client.
                    client.SendTimeout = 250;

                    lock (_subscribersLock)
                        _subscribers.Add(client);
                    Console.WriteLine($"GSStream: subscriber connected (now {SubscriberCount} total).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GSStream: accept loop error: {ex.Message}");
            }
        }

        // ── Inbound read + fan-out ───────────────────────────────────────────

        private async Task InboundReadLoop(TcpClient client, CancellationToken ct)
        {
            try
            {
                using var stream = client.GetStream();
                var sizeBuf = new byte[4];

                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, sizeBuf, 0, 4, ct))
                        break;

                    uint payloadSize = ReadU32LE(sizeBuf, 0);
                    if (payloadSize < HeaderTotal - 4 || payloadSize > MaxFrameBytes)
                    {
                        Console.WriteLine($"GSStream: implausible inbound frame size {payloadSize}; dropping connection.");
                        break;
                    }

                    // Allocate one buffer big enough for the whole frame
                    // (size field included) so we can rebroadcast it as-is.
                    var raw = new byte[4 + (int)payloadSize];
                    Buffer.BlockCopy(sizeBuf, 0, raw, 0, 4);

                    if (!await ReadExactAsync(stream, raw, 4, (int)payloadSize, ct))
                        break;

                    var snap = ParseFrameSnapshot(raw);
                    if (snap == null)
                        continue;

                    Interlocked.Increment(ref _framesIn);
                    lock (_latestLock) _latest = snap;

                    Broadcast(raw);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                Console.WriteLine($"GSStream: inbound reader error: {ex.Message}");
            }
            finally
            {
                // If this loop was serving the currently tracked inbound link,
                // clear it so status callers don't see a stale "connected"
                // state after the socket has dropped.
                if (ReferenceEquals(_inbound, client))
                {
                    _inbound = null;

                    // Also drop the latest-frame slot; once the link is down we
                    // should not report stale pixels as if they were current.
                    lock (_latestLock) _latest = null;
                }

                Console.WriteLine("GSStream: inbound connection closed.");
                _ = Task.Run(() => TryReconnectAfterDropAsync(ct));
            }
        }

        private FrameSnapshot? ParseFrameSnapshot(byte[] raw)
        {
            // raw[0..3]  size
            // raw[4]     type
            // raw[5..8]  frame_idx
            // raw[9..10] width
            // raw[11..12] height
            // raw[13]    codec
            // raw[14]    flags
            // raw[15..]  payload
            if (raw.Length < HeaderTotal) return null;
            if (raw[4] != MsgTypeFrame)   return null;

            uint   idx     = ReadU32LE(raw, 5);
            ushort width   = ReadU16LE(raw, 9);
            ushort height  = ReadU16LE(raw, 11);
            byte   codec   = raw[13];
            // raw[14] = flags (currently unused)

            int payloadLen = raw.Length - HeaderTotal;
            var payload    = new byte[payloadLen];
            Buffer.BlockCopy(raw, HeaderTotal, payload, 0, payloadLen);

            return new FrameSnapshot(idx, width, height, codec, payload, DateTime.UtcNow);
        }

        private void Broadcast(byte[] raw)
        {
            // Snapshot the subscriber list so we can write outside the lock —
            // a slow client must not block the inbound reader.
            TcpClient[] copies;
            lock (_subscribersLock)
                copies = _subscribers.ToArray();

            List<TcpClient>? toDrop = null;

            foreach (var sub in copies)
            {
                try
                {
                    var s = sub.GetStream();
                    s.Write(raw, 0, raw.Length);
                    Interlocked.Increment(ref _framesOut);
                }
                catch
                {
                    (toDrop ??= new List<TcpClient>()).Add(sub);
                }
            }

            if (toDrop != null)
            {
                lock (_subscribersLock)
                {
                    foreach (var dead in toDrop)
                    {
                        if (_subscribers.Remove(dead))
                        {
                            try { dead.Close(); } catch { /* swallow */ }
                            Interlocked.Increment(ref _subscriberDrops);
                        }
                    }
                }
                Console.WriteLine($"GSStream: dropped {toDrop.Count} slow/dead subscriber(s); now {SubscriberCount} total.");
            }
        }

        private async Task TryReconnectAfterDropAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            PineClient? pine;
            lock (_attachLock)
            {
                pine = _attachedPineClient;
            }

            if (pine == null) return;
            if (!ReferenceEquals(_inbound, null)) return;

            try
            {
                await Task.Delay(350, ct);
            }
            catch (OperationCanceledException) { return; }

            // Only reconnect if nothing else has already attached a new inbound
            // link in the meantime.
            if (_inbound != null || ct.IsCancellationRequested) return;

            Console.WriteLine("GSStream: attempting automatic reconnect to PCSX2 GS stream...");
            AttachPineClient(pine);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static async Task<bool> ReadExactAsync(NetworkStream s, byte[] buf, int offset, int count, CancellationToken ct)
        {
            int got = 0;
            while (got < count)
            {
                int n = await s.ReadAsync(buf.AsMemory(offset + got, count - got), ct);
                if (n == 0) return false;
                got += n;
            }
            return true;
        }

        private static uint ReadU32LE(byte[] buf, int offset)
            => (uint)(buf[offset]
                    | (buf[offset + 1] <<  8)
                    | (buf[offset + 2] << 16)
                    | (buf[offset + 3] << 24));

        private static ushort ReadU16LE(byte[] buf, int offset)
            => (ushort)(buf[offset] | (buf[offset + 1] << 8));

        public void Dispose() => Stop();

        /// <summary>Immutable snapshot of one received frame.</summary>
        public sealed class FrameSnapshot
        {
            public uint     FrameIndex   { get; }
            public ushort   Width        { get; }
            public ushort   Height       { get; }
            public byte     Codec        { get; } // 1 = MJPEG
            public byte[]   Payload      { get; }
            public DateTime ReceivedUtc  { get; }

            internal FrameSnapshot(uint idx, ushort w, ushort h, byte codec, byte[] payload, DateTime received)
            {
                FrameIndex  = idx;
                Width       = w;
                Height      = h;
                Codec       = codec;
                Payload     = payload;
                ReceivedUtc = received;
            }
        }
    }
}
