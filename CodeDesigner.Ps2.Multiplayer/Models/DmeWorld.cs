using System.Collections.Concurrent;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Multiplayer.Models;

/// <summary>
/// A DME game world: an active session that relays RT packets between connected game clients.
///
/// Adapted from horizon-server Server.Dme/Models/World.cs, reworked to use raw
/// .NET sockets instead of DotNetty.
/// </summary>
public class DmeWorld
{
    public const int MaxClientsPerWorld = 32;

    /// <summary>
    /// World ids run 1..255: SOCOM II (v108) sends the target world in ONE byte of CLIENT_CONNECT_TCP
    /// (the high byte of the TargetWorldId u32), so a DME world id must fit in a byte. 0 is left unused.
    /// </summary>
    public const int MaxWorldId = 255;

    // ── Static world registry ─────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<int, DmeWorld> _idToWorld = new();
    private static int _idCounter = 1;
    private static readonly object _idLock = new();

    public static DmeWorld? GetById(int id)
        => _idToWorld.TryGetValue(id, out var w) ? w : null;

    public static IEnumerable<DmeWorld> All => _idToWorld.Values;

    // ── Instance ──────────────────────────────────────────────────────────────

    /// <summary>DME world id (1–255) — the WorldID put in JoinGameResponse's NetConnectionInfo.</summary>
    public int WorldId { get; private set; } = -1;

    /// <summary>The Medius game (MediusWorldID as listed in the game list) this world hosts.</summary>
    public int MediusWorldId { get; }

    /// <summary>Application / game ID (AppId) this world belongs to.</summary>
    public int AppId { get; }

    /// <summary>Maximum number of players permitted in this world.</summary>
    public int MaxPlayers { get; }

    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>Destroy the world once its last client leaves (player-created games).</summary>
    public bool SelfDestructFlag { get; set; }

    /// <summary>Slots (reserved or connected) indexed by DME client index.</summary>
    public ConcurrentDictionary<int, DmeClientState> Clients { get; } = new();

    private readonly bool[] _slotUsed = new bool[MaxClientsPerWorld];
    private readonly object _slotLock = new();

    public DmeWorld(int mediusWorldId, int appId, int maxPlayers)
    {
        MediusWorldId = mediusWorldId;
        AppId         = appId;
        MaxPlayers    = Math.Clamp(maxPlayers, 1, MaxClientsPerWorld);
        RegisterWorld();
    }

    // ── Slot management ───────────────────────────────────────────────────────

    private bool TryAllocateSlot(out int index)
    {
        lock (_slotLock)
        {
            for (index = 0; index < MaxPlayers; index++)
            {
                if (!_slotUsed[index])
                {
                    _slotUsed[index] = true;
                    return true;
                }
            }
        }
        index = -1;
        return false;
    }

    private void ReleaseSlot(int index)
    {
        if ((uint)index < (uint)MaxClientsPerWorld)
            lock (_slotLock)
                _slotUsed[index] = false;
    }

    // ── Client management ─────────────────────────────────────────────────────

    /// <summary>
    /// Reserve a slot for a player joining through MLS. Re-joining with the same session key reuses the
    /// existing slot (horizon behaviour). Returns null when the world is full.
    /// </summary>
    public DmeClientState? Reserve(string sessionKey, int accountId)
    {
        var existing = Clients.Values.FirstOrDefault(c => c.SessionKey == sessionKey && !c.Disconnected);
        if (existing != null) return existing;

        if (!TryAllocateSlot(out int index))
            return null;

        string token = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        var client = new DmeClientState(sessionKey, token, accountId, this, index);
        if (!Clients.TryAdd(index, client))
        {
            ReleaseSlot(index);
            return null;
        }
        return client;
    }

    /// <summary>Find a reserved slot by the access token presented in the DME connect.</summary>
    public DmeClientState? FindByAccessKey(string accessKey)
        => Clients.Values.FirstOrDefault(c => c.AccessKey == accessKey && !c.Disconnected);

    /// <summary>Remove a client and free its slot.</summary>
    public void RemoveClient(DmeClientState client)
    {
        client.MarkDisconnected();
        if (Clients.TryRemove(client.DmeId, out _))
            ReleaseSlot(client.DmeId);
    }

    /// <summary>Number of clients with a live DME connection.</summary>
    public int ConnectedCount => Clients.Values.Count(c => c.IsConnected && !c.Disconnected);

    // ── Packet relay ──────────────────────────────────────────────────────────

    /// <summary>Relay an application broadcast from <paramref name="source"/> to every other joined client.</summary>
    public void BroadcastApp(DmeClientState source, byte[] payload)
    {
        foreach (var (_, client) in Clients)
        {
            if (client == source || !client.HasJoined) continue;
            if (!client.HasRecvFlag(RT_RECV_FLAG.RECV_BROADCAST)) continue;
            Forward(client, source.DmeId, payload);
        }
    }

    /// <summary>Send an application packet from <paramref name="source"/> to a single target by DME id.</summary>
    public void SendAppSingle(DmeClientState source, int targetDmeId, byte[] payload)
    {
        if (Clients.TryGetValue(targetDmeId, out var target)
            && target.HasJoined
            && target.HasRecvFlag(RT_RECV_FLAG.RECV_SINGLE))
        {
            Forward(target, source.DmeId, payload);
        }
    }

    /// <summary>Send an application packet from <paramref name="source"/> to a list of target DME ids.</summary>
    public void SendAppList(DmeClientState source, IEnumerable<int> targetIds, byte[] payload)
    {
        foreach (var tid in targetIds)
        {
            if (Clients.TryGetValue(tid, out var target)
                && target.HasJoined
                && target.HasRecvFlag(RT_RECV_FLAG.RECV_LIST))
            {
                Forward(target, source.DmeId, payload);
            }
        }
    }

    public void NotifyPlayerJoined(DmeClientState joined)
    {
        foreach (var (_, client) in Clients)
        {
            if (client == joined || !client.HasJoined) continue;
            if (!client.HasRecvFlag(RT_RECV_FLAG.RECV_NOTIFICATION)) continue;
            client.SendConnectNotify(joined.DmeId, joined.ScertId);
        }
    }

    public void NotifyPlayerLeft(DmeClientState left)
    {
        foreach (var (_, client) in Clients)
        {
            if (client == left || !client.HasJoined) continue;
            if (!client.HasRecvFlag(RT_RECV_FLAG.RECV_NOTIFICATION)) continue;
            client.SendDisconnectNotify(left.DmeId, left.ScertId);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Destroy() => _idToWorld.TryRemove(WorldId, out _);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Server→client relay frame: RT_MSG_CLIENT_APP_SINGLE [sourceDmeId:2 LE][payload] (horizon
    /// TargetOrSource convention; retail DME traffic to the client is CLIENT_APP_SINGLE frames).
    /// </summary>
    private static void Forward(DmeClientState target, int sourceDmeId, byte[] payload)
    {
        var body = new byte[2 + payload.Length];
        body[0] = (byte)sourceDmeId;
        body[1] = (byte)(sourceDmeId >> 8);
        Buffer.BlockCopy(payload, 0, body, 2, payload.Length);
        target.Send(RT_MSG_TYPE.RT_MSG_CLIENT_APP_SINGLE, body);
    }

    private void RegisterWorld()
    {
        lock (_idLock)
        {
            for (int tried = 0; tried < MaxWorldId; tried++)
            {
                int id = _idCounter;
                _idCounter = _idCounter >= MaxWorldId ? 1 : _idCounter + 1;
                if (_idToWorld.TryAdd(id, this))
                {
                    WorldId = id;
                    return;
                }
            }
            throw new InvalidOperationException("Maximum DME worlds reached.");
        }
    }

    public override string ToString()
        => $"DmeWorld(id={WorldId}, game=0x{MediusWorldId:X}, clients={ConnectedCount}/{MaxPlayers})";
}
