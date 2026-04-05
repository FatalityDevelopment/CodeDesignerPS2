using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// DME (Demonware Multiplayer Engine) TCP server.
///
/// After the MLS lobby server creates or joins a game world, game clients
/// connect here to exchange real-time in-game packets with each other.
///
/// Protocol:
///   1. Standard SCERT handshake (same as MAS / MLS).
///   2. Client sends an authentication request containing its session key.
///   3. Server looks up the <see cref="DmeWorld"/> for that session key,
///      assigns a DME client index, and returns connect-accept.
///   4. From that point on every RT_MSG_CLIENT_APP_* packet is relayed to
///      the other clients in the same world according to their RECV flags.
///
/// Adapted from horizon-server Server.Dme/TcpServer.cs, reworked to use raw
/// .NET sockets instead of DotNetty.
/// </summary>
public class DmeServer : BaseMediusTcpServer
{
    private readonly MultiplayerConfig _config;
    private readonly ILogger<DmeServer> _log;

    /// <summary>
    /// Active DME game worlds, keyed by world ID.
    /// Worlds are created by <see cref="MlsServer"/> when a game is created and
    /// removed here when all clients have left.
    /// </summary>
    internal static readonly ConcurrentDictionary<int, DmeWorld> Worlds = new();

    /// <summary>
    /// Create a new DME world and return it.  Called by <see cref="MlsServer"/>.
    /// </summary>
    public static DmeWorld CreateWorld(int appId, int maxPlayers)
    {
        var world = new DmeWorld(appId, maxPlayers);
        Worlds[world.WorldId] = world;
        return world;
    }

    /// <summary>
    /// Look up a world by its ID.
    /// </summary>
    public static DmeWorld? GetWorld(int worldId)
        => Worlds.TryGetValue(worldId, out var w) ? w : null;

    public DmeServer(MultiplayerConfig config, ILogger<DmeServer> log)
        : base(log, config)
    {
        _config    = config;
        _log       = log;
        ServerName = "DME";
    }

    public Task StartAsync(CancellationToken ct) =>
        StartAsync(_config.BindAddress, _config.DmePort, ct);

    // ── Disconnect cleanup ────────────────────────────────────────────────────

    protected override Task OnClientDisconnectedAsync(MediusClientState state)
    {
        if (state.DmeWorldId.HasValue && state.DmeClientIndex.HasValue)
        {
            if (Worlds.TryGetValue(state.DmeWorldId.Value, out var world))
            {
                var client = world.Clients.Values
                    .FirstOrDefault(c => c.DmeId == state.DmeClientIndex.Value);
                if (client != null)
                {
                    world.NotifyPlayerLeft(client);
                    world.RemoveClient(client);
                    _log.LogInformation("[DME] Client {Key} left world {World}", state.SessionKey, state.DmeWorldId);

                    if (world.Clients.IsEmpty && world.SelfDestructFlag)
                    {
                        Worlds.TryRemove(world.WorldId, out _);
                        _log.LogInformation("[DME] World {World} destroyed (empty + self-destruct)", world.WorldId);
                    }
                }
            }
        }
        return Task.CompletedTask;
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    protected override async Task HandleMediusMessageAsync(
        MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // DME uses raw RT messages — there is no Medius application layer in normal
        // game traffic.  This handler is invoked for RT_MSG_CLIENT_APP_TOSERVER
        // frames that wrap a Medius payload; in practice these are used during the
        // initial authentication exchange before game packets begin flowing.
        _log.LogDebug("[DME] Medius msg class=0x{C:X2} type=0x{T:X2}", (byte)packet.MessageClass, packet.MessageType);
    }

    /// <summary>
    /// Handle RT transport-layer packets that are NOT wrapped Medius messages.
    /// This is where the DME-specific game relay logic lives.
    /// </summary>
    protected override async Task HandleRtMessageAsync(
        RT_MSG_TYPE msgType, byte[] payload, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        switch (msgType)
        {
            // ── Initial DME authentication ────────────────────────────────────
            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER when !state.IsDmeAuthenticated:
                await HandleDmeAuthAsync(payload, state, tcp, ct);
                break;

            // ── Broadcast to all clients in the world ─────────────────────────
            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_BROADCAST:
                if (state.IsDmeAuthenticated && state.DmeWorldId.HasValue)
                    RelayBroadcast(state, payload);
                break;

            // ── Unicast to a single target ────────────────────────────────────
            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_SINGLE:
                if (state.IsDmeAuthenticated && state.DmeWorldId.HasValue && payload.Length >= 2)
                    RelaySingle(state, payload);
                break;

            // ── Multicast to a list of targets ────────────────────────────────
            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_LIST:
                if (state.IsDmeAuthenticated && state.DmeWorldId.HasValue && payload.Length >= 3)
                    RelayList(state, payload);
                break;

            // ── Recv-flag update ──────────────────────────────────────────────
            case RT_MSG_TYPE.RT_MSG_CLIENT_SET_RECV_FLAG:
                if (payload.Length >= 1)
                    SetRecvFlag(state, payload[0]);
                break;

            default:
                _log.LogDebug("[DME] Unhandled RT type 0x{T:X2}", (byte)msgType);
                break;
        }

        await Task.CompletedTask;
    }

    // ── DME authentication ────────────────────────────────────────────────────

    private async Task HandleDmeAuthAsync(
        byte[] payload, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // The payload begins with the session key (null-terminated, up to 64 bytes),
        // followed by an optional world ID (int32 LE).
        if (payload.Length < 2) return;

        var r = new MediusReader(payload);
        string sessionKey = r.ReadString(MediusConstants.SESSIONKEY_MAXLEN);

        // Find a world that has a pending slot for this session key.
        DmeWorld? world = null;
        DmeClientState? dmeClient = null;

        foreach (var (_, w) in Worlds)
        {
            var existing = w.FindBySessionKey(sessionKey);
            if (existing != null)
            {
                world     = w;
                dmeClient = existing;
                break;
            }
        }

        if (world == null)
        {
            // No pre-registered slot — try to find a world with a free slot
            // by matching the AppId from the SCERT connect.
            foreach (var (_, w) in Worlds)
            {
                if (w.AppId == state.AppId)
                {
                    dmeClient = w.AddClient(sessionKey, tcp);
                    if (dmeClient != null) { world = w; break; }
                }
            }
        }

        if (world == null || dmeClient == null)
        {
            _log.LogWarning("[DME] No world found for session key {Key}", sessionKey);
            // Send a forced disconnect.
            var disc = ScertPacket.CreateServerApp(new byte[] { (byte)RT_MSG_TYPE.RT_MSG_SERVER_FORCED_DISCONNECT, 0, 0 });
            await SendPacketAsync(tcp, disc, ct);
            return;
        }

        // Link MediusClientState to the DME world.
        dmeClient.IsConnected  = true;
        dmeClient.MediusVersion = state.MediusVersion;
        dmeClient.ScertId       = state.ScertId;

        state.DmeWorldId      = world.WorldId;
        state.DmeClientIndex  = dmeClient.DmeId;
        state.IsDmeAuthenticated = true;
        state.SessionKey      = sessionKey;

        _log.LogInformation("[DME] Client {Key} joined world {World} as player {Id}",
            sessionKey, world.WorldId, dmeClient.DmeId);

        // Send SERVER_CONNECT_ACCEPT_TCP with the assigned DME id.
        var accept = ScertPacket.CreateConnectAcceptTcp(state.ScertId, (uint)dmeClient.DmeId, (ushort)world.Clients.Count, null);
        await SendPacketAsync(tcp, accept, ct);

        // Notify other world members that this player has joined.
        dmeClient.HasJoined = true;
        world.NotifyPlayerJoined(dmeClient);
    }

    // ── Relay helpers ─────────────────────────────────────────────────────────

    private void RelayBroadcast(MediusClientState state, byte[] payload)
    {
        if (!Worlds.TryGetValue(state.DmeWorldId!.Value, out var world)) return;
        var src = world.Clients.Values.FirstOrDefault(c => c.DmeId == state.DmeClientIndex);
        if (src == null) return;
        world.BroadcastApp(src, payload);
        FlushWorld(world);
    }

    private void RelaySingle(MediusClientState state, byte[] payload)
    {
        if (!Worlds.TryGetValue(state.DmeWorldId!.Value, out var world)) return;
        var src = world.Clients.Values.FirstOrDefault(c => c.DmeId == state.DmeClientIndex);
        if (src == null) return;

        int targetId = payload[0] | (payload[1] << 8);
        byte[] data  = payload.Length > 2 ? payload[2..] : Array.Empty<byte>();
        world.SendAppSingle(src, targetId, data);
        FlushWorld(world);
    }

    private void RelayList(MediusClientState state, byte[] payload)
    {
        if (!Worlds.TryGetValue(state.DmeWorldId!.Value, out var world)) return;
        var src = world.Clients.Values.FirstOrDefault(c => c.DmeId == state.DmeClientIndex);
        if (src == null) return;

        // [count:1][targetId:2 LE] * count + [data]
        int count  = payload[0];
        int dataOffset = 1 + count * 2;
        if (payload.Length < dataOffset) return;

        var targets = new List<int>(count);
        for (int i = 0; i < count; i++)
            targets.Add(payload[1 + i * 2] | (payload[2 + i * 2] << 8));

        byte[] data = dataOffset < payload.Length ? payload[dataOffset..] : Array.Empty<byte>();
        world.SendAppList(src, targets, data);
        FlushWorld(world);
    }

    private void SetRecvFlag(MediusClientState state, byte flagByte)
    {
        if (!state.DmeWorldId.HasValue || !state.DmeClientIndex.HasValue) return;
        if (!Worlds.TryGetValue(state.DmeWorldId.Value, out var world)) return;
        if (!world.Clients.TryGetValue(state.DmeClientIndex.Value, out var client)) return;
        client.RecvFlag = (RT_RECV_FLAG)flagByte;
    }

    /// <summary>
    /// Drain queued outbound frames for every client in <paramref name="world"/>.
    /// </summary>
    private static void FlushWorld(DmeWorld world)
    {
        foreach (var (_, client) in world.Clients)
        {
            if (client.Disconnected) continue;
            try
            {
                var stream = client.Tcp.GetStream();
                while (client.SendQueue.TryDequeue(out var frame))
                    stream.Write(frame, 0, frame.Length);
            }
            catch
            {
                // If the write fails the receive loop will handle the disconnect.
            }
        }
    }
}
