using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Multiplayer.Models;

/// <summary>
/// One player slot in a <see cref="DmeWorld"/>.
///
/// The slot is reserved by MLS JoinGame (keyed by an access token returned in the JoinGameResponse's
/// NetConnectionInfo), then bound to a TCP connection when the client's CLIENT_CONNECT_TCP to the DME
/// presents that token — the same reserve-then-connect model as horizon-server's DME.
/// </summary>
public class DmeClientState
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>MLS session key of the player that reserved this slot.</summary>
    public string SessionKey { get; }

    /// <summary>Access token handed out in JoinGameResponse; the DME connect presents it back.</summary>
    public string AccessKey { get; }

    /// <summary>Account that reserved the slot (for logging / game player counts).</summary>
    public int AccountId { get; }

    /// <summary>Slot index within the <see cref="DmeWorld"/> (0 … MaxClients-1).</summary>
    public int DmeId { get; }

    /// <summary>SCERT client ID assigned during the SCERT handshake.</summary>
    public uint ScertId { get; set; }

    /// <summary>Medius protocol version (108 = old PS2, >108 = PS3).</summary>
    public int MediusVersion { get; set; } = 108;

    /// <summary>The world this client belongs to.</summary>
    public DmeWorld World { get; }

    // ── Transport ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends one RT frame (type + plaintext payload) to this client over its encrypted DME connection.
    /// Null until the client's TCP connection is bound to the slot.
    /// </summary>
    public Func<RT_MSG_TYPE, byte[], Task>? Sender { get; set; }

    // ── State flags ───────────────────────────────────────────────────────────

    /// <summary>Whether a TCP connection is bound to this slot.</summary>
    public bool IsConnected { get; set; }

    /// <summary>True once the DME connect handshake completed and the client takes part in relaying.</summary>
    public bool HasJoined { get; set; }

    /// <summary>Whether the client has been disconnected.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>Receive-filter flags controlling which packet types are forwarded.</summary>
    public RT_RECV_FLAG RecvFlag { get; set; } =
        RT_RECV_FLAG.RECV_SINGLE | RT_RECV_FLAG.RECV_LIST;

    // ── Timing ────────────────────────────────────────────────────────────────

    public DateTime ReservedAt     { get; } = DateTime.UtcNow;
    public DateTime LastMessageAt  { get; set; } = DateTime.UtcNow;

    // ── Construction ──────────────────────────────────────────────────────────

    public DmeClientState(string sessionKey, string accessKey, int accountId, DmeWorld world, int dmeId)
    {
        SessionKey = sessionKey;
        AccessKey  = accessKey;
        AccountId  = accountId;
        World      = world;
        DmeId      = dmeId;
    }

    // ── Receive-flag helper ───────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the client should receive packets matching
    /// <paramref name="flag"/>.  For old Medius (v ≤ 108) all flags are implicitly set.
    /// </summary>
    public bool HasRecvFlag(RT_RECV_FLAG flag)
        => MediusVersion <= 108 || RecvFlag.HasFlag(flag);

    // ── Outbound helpers ──────────────────────────────────────────────────────

    /// <summary>Send one RT frame to this client (fire-and-forget; send errors surface in the server log).</summary>
    public void Send(RT_MSG_TYPE type, byte[] payload)
    {
        if (Disconnected || Sender == null) return;
        _ = Sender(type, payload);
    }

    /// <summary>
    /// SERVER_CONNECT_NOTIFY (player joined): [playerIndex:2 LE][scertId:2 LE].
    /// UNVERIFIED for v108 — only reachable with two or more players in a world.
    /// </summary>
    public void SendConnectNotify(int playerIndex, uint scertId)
        => Send(RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_NOTIFY,
                new[] { (byte)playerIndex, (byte)(playerIndex >> 8), (byte)scertId, (byte)(scertId >> 8) });

    /// <summary>SERVER_DISCONNECT_NOTIFY (player left): [playerIndex:2 LE][scertId:2 LE]. UNVERIFIED for v108.</summary>
    public void SendDisconnectNotify(int playerIndex, uint scertId)
        => Send(RT_MSG_TYPE.RT_MSG_SERVER_DISCONNECT_NOTIFY,
                new[] { (byte)playerIndex, (byte)(playerIndex >> 8), (byte)scertId, (byte)(scertId >> 8) });

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void MarkDisconnected()
    {
        Disconnected = true;
        Sender = null;
    }

    public override string ToString()
        => $"DmeClient(world={World.WorldId}, dmeId={DmeId}, acct={AccountId})";
}
