using System.Collections.Concurrent;
using System.Net.Sockets;
using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Multiplayer.Models;

/// <summary>
/// A DME game world: an active session that relays RT packets between connected game clients.
///
/// Adapted from horizon-server Server.Dme/Models/World.cs, reworked to use raw
/// .NET sockets instead of DotNetty.
/// </summary>
public class DmeWorld : IDisposable
{
    public const int MaxClientsPerWorld = 10;
    public const int MaxWorlds          = 256;

    // ── Static world registry ─────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<int, DmeWorld> _idToWorld = new();
    private static int _idCounter;
    private static readonly object _idLock = new();

    public static DmeWorld? GetById(int id)
        => _idToWorld.TryGetValue(id, out var w) ? w : null;

    public static IEnumerable<DmeWorld> All => _idToWorld.Values;

    // ── Instance ──────────────────────────────────────────────────────────────

    /// <summary>World ID assigned on creation (0–255).</summary>
    public int WorldId { get; private set; } = -1;

    /// <summary>Application / game ID (AppId) this world belongs to.</summary>
    public int AppId { get; }

    /// <summary>Maximum number of players permitted in this world.</summary>
    public int MaxPlayers { get; }

    /// <summary>UTC timestamp the world was created.</summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>Whether the world is flagged for destruction once all clients leave.</summary>
    public bool SelfDestructFlag { get; set; }

    /// <summary>
    /// Live clients indexed by their DME client index (0 … MaxClientsPerWorld-1).
    /// </summary>
    public ConcurrentDictionary<int, DmeClientState> Clients { get; } = new();

    private readonly bool[] _slotUsed = new bool[MaxClientsPerWorld];
    private readonly object _slotLock = new();

    public DmeWorld(int appId, int maxPlayers)
    {
        AppId      = appId;
        MaxPlayers = Math.Clamp(maxPlayers, 1, MaxClientsPerWorld);
        RegisterWorld();
    }

    // ── Slot management ───────────────────────────────────────────────────────

    /// <summary>Tries to allocate the next free client slot index.</summary>
    public bool TryAllocateSlot(out int index)
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

    /// <summary>Releases a previously allocated client slot.</summary>
    public void ReleaseSlot(int index)
    {
        if ((uint)index < (uint)MaxClientsPerWorld)
        {
            lock (_slotLock)
                _slotUsed[index] = false;
        }
    }

    // ── Client management ─────────────────────────────────────────────────────

    /// <summary>
    /// Add a new DME client to this world, returning its DME slot index.
    /// Returns -1 if the world is full.
    /// </summary>
    public DmeClientState? AddClient(string sessionKey, TcpClient tcp)
    {
        if (!TryAllocateSlot(out int index))
            return null;

        var client = new DmeClientState(sessionKey, this, index, tcp);
        if (!Clients.TryAdd(index, client))
        {
            ReleaseSlot(index);
            return null;
        }
        return client;
    }

    /// <summary>Remove a client by slot index.</summary>
    public void RemoveClient(DmeClientState client)
    {
        if (Clients.TryRemove(client.DmeId, out _))
            ReleaseSlot(client.DmeId);
    }

    /// <summary>Find an existing client in this world by session key.</summary>
    public DmeClientState? FindBySessionKey(string sessionKey)
        => Clients.Values.FirstOrDefault(c => c.SessionKey == sessionKey);

    // ── Packet relay ──────────────────────────────────────────────────────────

    /// <summary>
    /// Relay an application broadcast from <paramref name="source"/> to every other
    /// authenticated client that has <see cref="RT_RECV_FLAG.RECV_BROADCAST"/> set.
    /// </summary>
    public void BroadcastApp(DmeClientState source, byte[] payload)
    {
        foreach (var (_, client) in Clients)
        {
            if (client == source)             continue;
            if (!client.HasJoined)            continue;
            if (!client.HasRecvFlag(RT_RECV_FLAG.RECV_BROADCAST)) continue;
            EnqueueToClient(client, source.DmeId, payload);
        }
    }

    /// <summary>
    /// Send an application packet from <paramref name="source"/> to a single target by DME id.
    /// </summary>
    public void SendAppSingle(DmeClientState source, int targetDmeId, byte[] payload)
    {
        if (Clients.TryGetValue(targetDmeId, out var target)
            && target.HasJoined
            && target.HasRecvFlag(RT_RECV_FLAG.RECV_SINGLE))
        {
            EnqueueToClient(target, source.DmeId, payload);
        }
    }

    /// <summary>
    /// Send an application packet from <paramref name="source"/> to a list of target DME ids.
    /// </summary>
    public void SendAppList(DmeClientState source, IEnumerable<int> targetIds, byte[] payload)
    {
        foreach (var tid in targetIds)
        {
            if (Clients.TryGetValue(tid, out var target)
                && target.HasJoined
                && target.HasRecvFlag(RT_RECV_FLAG.RECV_LIST))
            {
                EnqueueToClient(target, source.DmeId, payload);
            }
        }
    }

    /// <summary>
    /// Notify all clients with <see cref="RT_RECV_FLAG.RECV_NOTIFICATION"/> that
    /// a player has joined the world.
    /// </summary>
    public void NotifyPlayerJoined(DmeClientState joined)
    {
        foreach (var (_, client) in Clients)
        {
            if (client == joined || !client.HasJoined) continue;
            if (!client.HasRecvFlag(RT_RECV_FLAG.RECV_NOTIFICATION)) continue;
            client.SendConnectNotify(joined.DmeId, joined.ScertId);
        }
    }

    /// <summary>
    /// Notify all clients with <see cref="RT_RECV_FLAG.RECV_NOTIFICATION"/> that
    /// a player has left the world.
    /// </summary>
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

    /// <summary>Stop all clients and destroy the world.</summary>
    public async Task StopAsync()
    {
        foreach (var (_, client) in Clients)
            await client.StopAsync();
        Clients.Clear();
        Dispose();
    }

    public void Dispose()
    {
        _idToWorld.TryRemove(WorldId, out _);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void EnqueueToClient(DmeClientState client, int sourceDmeId, byte[] payload)
    {
        // Build a minimal RT_MSG_CLIENT_APP_SINGLE wire frame:
        //   [RT_MSG_CLIENT_APP_SINGLE:1][payloadLen+2:2 LE][sourceDmeId:2 LE][payload]
        var frame = new byte[3 + 2 + payload.Length];
        frame[0] = (byte)RT_MSG_TYPE.RT_MSG_CLIENT_APP_SINGLE;
        int totalPayload = 2 + payload.Length;
        frame[1] = (byte)(totalPayload & 0xFF);
        frame[2] = (byte)((totalPayload >> 8) & 0xFF);
        frame[3] = (byte)(sourceDmeId & 0xFF);
        frame[4] = (byte)((sourceDmeId >> 8) & 0xFF);
        Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);

        client.EnqueueRaw(frame);
    }

    private void RegisterWorld()
    {
        lock (_idLock)
        {
            int tried = 0;
            while (tried < MaxWorlds && _idToWorld.ContainsKey(_idCounter))
            {
                _idCounter = (_idCounter + 1) % MaxWorlds;
                tried++;
            }
            if (tried == MaxWorlds)
                throw new InvalidOperationException("Maximum DME worlds reached.");

            WorldId = _idCounter;
            _idCounter = (_idCounter + 1) % MaxWorlds;
            _idToWorld[WorldId] = this;
        }
    }

    public override string ToString()
        => $"DmeWorld(id={WorldId}, clients={Clients.Count}/{MaxPlayers})";
}
