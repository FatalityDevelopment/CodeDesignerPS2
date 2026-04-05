using System.Collections.Concurrent;
using System.Net.Sockets;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Multiplayer.Models;

/// <summary>
/// Per-connection state for a DME game client.
///
/// A DME client connects to the DME server after the MLS lobby creates or
/// joins a game world.  The client authenticates using the session key received
/// from MLS, is assigned a DME slot index in a <see cref="DmeWorld"/>, and
/// then exchanges RT application packets with other clients in that world.
///
/// Adapted from horizon-server Server.Dme/Models/ClientObject.cs, reworked to
/// use raw .NET sockets instead of DotNetty.
/// </summary>
public class DmeClientState
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Session key received from MLS, used to match this client to a game.</summary>
    public string SessionKey { get; }

    /// <summary>Slot index within the <see cref="DmeWorld"/> (0 … MaxClients-1).</summary>
    public int DmeId { get; }

    /// <summary>SCERT client ID assigned during the SCERT handshake.</summary>
    public uint ScertId { get; set; }

    /// <summary>Medius protocol version (108 = old PS2, >108 = PS3).</summary>
    public int MediusVersion { get; set; } = 108;

    /// <summary>The world this client belongs to.</summary>
    public DmeWorld World { get; }

    // ── Transport ─────────────────────────────────────────────────────────────

    /// <summary>Underlying TCP socket for this client.</summary>
    public TcpClient Tcp { get; }

    /// <summary>Outbound packet queue (raw wire-format frames).</summary>
    public ConcurrentQueue<byte[]> SendQueue { get; } = new();

    // ── State flags ───────────────────────────────────────────────────────────

    /// <summary>Whether the SCERT handshake is complete.</summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// True once the client has sent its first application packet and is
    /// fully participating in the world.
    /// </summary>
    public bool HasJoined { get; set; }

    /// <summary>Whether the client has been gracefully disconnected.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>Receive-filter flags controlling which packet types are forwarded.</summary>
    public RT_RECV_FLAG RecvFlag { get; set; } =
        RT_RECV_FLAG.RECV_SINGLE | RT_RECV_FLAG.RECV_LIST;

    // ── Timing ────────────────────────────────────────────────────────────────

    public DateTime ConnectedAt    { get; } = DateTime.UtcNow;
    public DateTime LastMessageAt  { get; set; } = DateTime.UtcNow;
    public TimeSpan Timeout        { get; set; } = TimeSpan.FromSeconds(60);

    public bool IsTimedOut => DateTime.UtcNow - LastMessageAt > Timeout;

    // ── Construction ──────────────────────────────────────────────────────────

    public DmeClientState(string sessionKey, DmeWorld world, int dmeId, TcpClient tcp)
    {
        SessionKey = sessionKey;
        World      = world;
        DmeId      = dmeId;
        Tcp        = tcp;
    }

    // ── Receive-flag helper ───────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the client should receive packets matching
    /// <paramref name="flag"/>.  For old Medius (v ≤ 108) all flags are implicitly set.
    /// </summary>
    public bool HasRecvFlag(RT_RECV_FLAG flag)
        => MediusVersion <= 108 || RecvFlag.HasFlag(flag);

    // ── Outbound helpers ──────────────────────────────────────────────────────

    /// <summary>Enqueue a raw pre-serialised frame for sending.</summary>
    public void EnqueueRaw(byte[] frame)
        => SendQueue.Enqueue(frame);

    /// <summary>
    /// Enqueue a SERVER_CONNECT_NOTIFY (player joined) notification.
    /// Wire format: [RT_MSG_SERVER_CONNECT_NOTIFY:1][len:2 LE][playerIndex:2 LE][scertId:2 LE]
    /// </summary>
    public void SendConnectNotify(int playerIndex, uint scertId)
    {
        var frame = new byte[3 + 4];
        frame[0] = (byte)RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_NOTIFY;
        frame[1] = 4; frame[2] = 0; // length = 4
        frame[3] = (byte)(playerIndex & 0xFF);
        frame[4] = (byte)((playerIndex >> 8) & 0xFF);
        frame[5] = (byte)(scertId & 0xFF);
        frame[6] = (byte)((scertId >> 8) & 0xFF);
        SendQueue.Enqueue(frame);
    }

    /// <summary>
    /// Enqueue a SERVER_DISCONNECT_NOTIFY (player left) notification.
    /// Wire format: [RT_MSG_SERVER_DISCONNECT_NOTIFY:1][len:2 LE][playerIndex:2 LE][scertId:2 LE]
    /// </summary>
    public void SendDisconnectNotify(int playerIndex, uint scertId)
    {
        var frame = new byte[3 + 4];
        frame[0] = (byte)RT_MSG_TYPE.RT_MSG_SERVER_DISCONNECT_NOTIFY;
        frame[1] = 4; frame[2] = 0;
        frame[3] = (byte)(playerIndex & 0xFF);
        frame[4] = (byte)((playerIndex >> 8) & 0xFF);
        frame[5] = (byte)(scertId & 0xFF);
        frame[6] = (byte)((scertId >> 8) & 0xFF);
        SendQueue.Enqueue(frame);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Mark the client as disconnected and close the TCP socket.</summary>
    public Task StopAsync()
    {
        if (Disconnected) return Task.CompletedTask;
        Disconnected = true;
        try { Tcp.Close(); } catch { /* ignore */ }
        return Task.CompletedTask;
    }

    public override string ToString()
        => $"DmeClient(world={World.WorldId}, dmeId={DmeId}, key={SessionKey})";
}
