using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Crypto;
using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// Abstract TCP server that accepts SCERT connections, manages the per-client
/// receive buffer, and dispatches decoded packets to subclasses.
///
/// SCERT Handshake (Old Medius v &lt;= 108, typical for 2002-2004 PS2 games):
///   CLIENT_HELLO               → SERVER_HELLO (128-byte RSA N or zeros)
///   CLIENT_CRYPTKEY_PUBLIC     → SERVER_CRYPTKEY_PEER (64-byte RC4 key)
///   CLIENT_CONNECT_TCP         → SERVER_CRYPTKEY_GAME + SERVER_CONNECT_ACCEPT_TCP + SERVER_CONNECT_COMPLETE
///   CLIENT_CONNECT_READY_TCP   → SERVER_CONNECT_COMPLETE
///   &lt;Medius app messages follow&gt;
///
/// SCERT Handshake (New Medius v &gt; 108, PS3 era):
///   ... same up to CONNECT_TCP ...
///   CLIENT_CONNECT_TCP         → SERVER_CONNECT_REQUIRE
///   CLIENT_CONNECT_READY_REQUIRE → SERVER_CRYPTKEY_GAME + SERVER_CONNECT_ACCEPT_TCP
///   CLIENT_CONNECT_READY_TCP   → SERVER_CONNECT_COMPLETE
/// </summary>
public abstract class BaseMediusTcpServer
{
    private readonly ILogger      _log;
    private readonly MultiplayerConfig _config;
    private TcpListener?         _listener;

    private uint _scertIdCounter = 0;

    protected readonly ConcurrentDictionary<string, (MediusClientState State, TcpClient Client)> Clients = new();

    protected BaseMediusTcpServer(ILogger log, MultiplayerConfig config)
    {
        _log    = log;
        _config = config;
    }

    public string ServerName { get; protected set; } = "SCERT";
    public int    Port       { get; protected set; }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public Task StartAsync(string bindAddress, int port, CancellationToken ct)
    {
        Port      = port;
        _listener = new TcpListener(IPAddress.Parse(bindAddress), port);
        _listener.Start();
        _log.LogInformation("[{Name}] Listening on {Addr}:{Port}", ServerName, bindAddress, port);
        _ = AcceptLoopAsync(ct);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _listener?.Stop();
        foreach (var (_, (_, client)) in Clients)
            client.Close();
        Clients.Clear();
    }

    // -------------------------------------------------------------------------
    // Accept loop
    // -------------------------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(tcp, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[{Name}] Accept error", ServerName);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Per-client read loop
    // -------------------------------------------------------------------------

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        tcp.NoDelay = true;

        var state = new MediusClientState
        {
            RemoteEndpoint = tcp.Client.RemoteEndPoint?.ToString() ?? "?",
            Timeout        = TimeSpan.FromSeconds(60),
        };

        Clients[state.ConnectionId] = (state, tcp);
        _log.LogInformation("[{Name}] Client connected: {Ep} ({Id})", ServerName, state.RemoteEndpoint, state.ConnectionId);

        await OnClientConnectedAsync(state, tcp);

        var stream = tcp.GetStream();
        var buffer = new byte[8192];
        var pending = new List<byte>(8192);

        try
        {
            while (!ct.IsCancellationRequested && tcp.Connected)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;

                state.Touch();
                _log.LogDebug("[{Name}] RAW recv {Len} bytes: {Hex}", ServerName, read,
                    BitConverter.ToString(buffer, 0, read).Replace("-", " "));
                pending.AddRange(buffer[..read]);

                // Parse as many complete packets as possible
                while (true)
                {
                    var pendingArray = pending.ToArray();
                    var packet = ScertPacket.TryRead(pendingArray, 0, pendingArray.Length, out int consumed);
                    if (packet == null || consumed == 0) break;

                    pending.RemoveRange(0, consumed);

                    // Decrypt payload in-place using per-packet stateless PS2 RC4.
                    // Only applies AFTER the key exchange completes (CLIENT_CRYPTKEY_PUBLIC
                    // itself is RSA-encrypted, not RC4 — don't decrypt it).
                    if (packet.IsEncrypted && packet.Hash != null && state.CryptKeyReceived)
                        PS2_RC4.Decrypt(state.SessionRc4Key, packet.Hash, packet.Payload);

                    await DispatchPacketAsync(packet, state, tcp, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("[{Name}] Client {Id} read error: {Msg}", ServerName, state.ConnectionId, ex.Message);
        }
        finally
        {
            if (pending.Count > 0)
                _log.LogWarning("[{Name}] Client {Id} disconnected with {N} unparsed bytes: {Hex}",
                    ServerName, state.ConnectionId, pending.Count,
                    BitConverter.ToString(pending.ToArray()).Replace("-", " "));
            Clients.TryRemove(state.ConnectionId, out _);
            await OnClientDisconnectedAsync(state);
            tcp.Close();
            _log.LogInformation("[{Name}] Client disconnected: {Id}", ServerName, state.ConnectionId);
        }
    }

    // -------------------------------------------------------------------------
    // Packet dispatch
    // -------------------------------------------------------------------------

    private async Task DispatchPacketAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        _log.LogDebug("[{Name}] <- {Id} {Type} ({Len} bytes) {Hex}",
            ServerName, state.ConnectionId, packet.MessageType, packet.Payload.Length,
            BitConverter.ToString(packet.Payload).Replace("-", " "));

        switch (packet.MessageType)
        {
            case RT_MSG_TYPE.RT_MSG_CLIENT_HELLO:
                await HandleHelloAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_CRYPTKEY_PUBLIC:
                await HandleCryptKeyPublicAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_TCP:
                await HandleConnectTcpAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_READY_REQUIRE:
                await HandleConnectReadyRequireAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_READY_TCP:
                await HandleConnectReadyTcpAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_ECHO:
                await SendPacketAsync(tcp, ScertPacket.CreateEchoResponse(packet.Payload), ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER:
                var mp = MediusPacket.Parse(packet.Payload);
                if (mp != null)
                    await HandleMediusMessageAsync(mp, state, tcp, ct);
                else
                    await HandleRtMessageAsync(RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER, packet.Payload, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_BROADCAST:
            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_SINGLE:
            case RT_MSG_TYPE.RT_MSG_CLIENT_APP_LIST:
            case RT_MSG_TYPE.RT_MSG_CLIENT_SET_RECV_FLAG:
            case RT_MSG_TYPE.RT_MSG_CLIENT_SET_AGG_TIME:
                await HandleRtMessageAsync(packet.MessageType, packet.Payload, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_DISCONNECT:
            case RT_MSG_TYPE.RT_MSG_CLIENT_DISCONNECT_WITH_REASON:
                tcp.Close();
                break;

            default:
                _log.LogInformation("[{Name}] Unhandled SCERT type {Type} payload={Hex}",
                    ServerName, packet.MessageType,
                    BitConverter.ToString(packet.Payload).Replace("-", " "));
                break;
        }
    }

    // -------------------------------------------------------------------------
    // SCERT handshake — common to all servers
    // -------------------------------------------------------------------------

    private Task HandleHelloAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // CLIENT_HELLO payload: [MediusVersion: 2 bytes LE][reserved: ...]
        if (packet.Payload.Length >= 2)
            state.MediusVersion = BitConverter.ToUInt16(packet.Payload, 0);

        state.HelloReceived = true;
        _log.LogInformation("[{Name}] CLIENT_HELLO ver={Ver} from {Ep}", ServerName, state.MediusVersion, state.RemoteEndpoint);

        // SERVER_HELLO was already sent proactively in OnClientConnectedAsync.
        return Task.CompletedTask;
    }

    private async Task HandleCryptKeyPublicAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.CryptKeyReceived = true;

        // Store the client's public key bytes (used when encryption is implemented)
        state.ClientCryptKey = packet.Payload;

        // Use all-zero RC4 key for testing. Either the client will see zeros and skip
        // RC4 entirely (IsEncrypted=0) or it will use all-zero key — both are deterministic.
        state.SessionRc4Key = new byte[64];

        _log.LogDebug("[{Name}] CLIENT_CRYPTKEY_PUBLIC ({Len} bytes key)", ServerName, packet.Payload.Length);

        // Reply with server's RC4 session key.
        await SendPacketAsync(tcp, ScertPacket.CreateCryptKeyPeer(state.SessionRc4Key), ct);

        // For new protocol (v > 108), SERVER_CONNECT_REQUIRE prompts CLIENT_CONNECT_TCP.
        // For old protocol (SOCOM II, v <= 108), the client sends CLIENT_CONNECT_TCP
        // automatically after receiving SERVER_CRYPTKEY_PEER — no prompt needed.
        // Sending SERVER_CONNECT_REQUIRE to an old-protocol client triggers a handler
        // that calls an uninitialised stub (0x0064DAE8) which returns 0, causing the
        // SCERT state machine to run the cleanup/teardown path instead of proceeding.
        if (state.MediusVersion > 108)
            await SendPacketAsync(tcp, ScertPacket.CreateConnectRequire(), ct);

        // No persistent decryptor needed — PS2_RC4.Decrypt is stateless per-packet.
    }

    private async Task HandleConnectTcpAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.ConnectReceived = true;

        // Decrypted payload layout (per Medius protocol):
        //   Old Medius (version < 109):  [TargetWorldId: 4][UNK0: 1][AppId: 4][RSA_KEY: ...]
        //   New Medius (version >= 109): [TargetWorldId: 4][AppId: 4][RSA_KEY: ...]
        // Note: MediusVersion is NOT in this packet — it was established from CLIENT_HELLO.
        if (packet.Payload.Length >= 4)
        {
            int pos = 4; // skip TargetWorldId
            if (state.MediusVersion < 109)
                pos += 1; // skip UNK0 (old protocol only)
            if (packet.Payload.Length >= pos + 4)
                state.AppId = BitConverter.ToInt32(packet.Payload, pos);
        }

        _log.LogInformation("[{Name}] CLIENT_CONNECT_TCP AppId={AppId} ver={Ver}", ServerName, state.AppId, state.MediusVersion);

        // Allocate a scert ID for this client
        state.ScertId = Interlocked.Increment(ref _scertIdCounter);

        var clientIp = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;

        if (state.MediusVersion > 108)
        {
            // New protocol: client must send CLIENT_CONNECT_READY_REQUIRE before we proceed
            await SendPacketAsync(tcp, ScertPacket.CreateConnectRequire(), ct);
        }
        else
        {
            // Old protocol (SOCOM II era): complete the handshake immediately
            await SendPacketAsync(tcp, ScertPacket.CreateCryptKeyGame(state.SessionRc4Key), ct);
            await SendPacketAsync(tcp, ScertPacket.CreateConnectAcceptTcp(state.ScertId, 0, 1, clientIp), ct);
            await SendPacketAsync(tcp, ScertPacket.CreateConnectComplete(), ct);
            state.IsSessionEstablished = true;
            _log.LogInformation("[{Name}] SCERT session established (old proto) for {Ep}", ServerName, state.RemoteEndpoint);
        }
    }

    private async Task HandleConnectReadyRequireAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // New protocol: complete the handshake now
        var clientIp = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
        await SendPacketAsync(tcp, ScertPacket.CreateCryptKeyGame(state.SessionRc4Key), ct);
        await SendPacketAsync(tcp, ScertPacket.CreateConnectAcceptTcp(state.ScertId, 0, 1, clientIp), ct);
    }

    private async Task HandleConnectReadyTcpAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.ConnectReadyReceived = true;
        await SendPacketAsync(tcp, ScertPacket.CreateConnectComplete(), ct);
        if (state.MediusVersion > 108)
            await SendPacketAsync(tcp, ScertPacket.CreateServerEcho(), ct);
        if (!state.IsSessionEstablished)
        {
            state.IsSessionEstablished = true;
            _log.LogInformation("[{Name}] SCERT session established for {Ep}", ServerName, state.RemoteEndpoint);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    protected async Task SendPacketAsync(TcpClient tcp, ScertPacket packet, CancellationToken ct)
    {
        if (!tcp.Connected) return;
        try
        {
            var data = packet.Serialize();
            var stream = tcp.GetStream();
            await stream.WriteAsync(data, ct);
            _log.LogDebug("[{Name}] -> {Type} ({Len} bytes)", ServerName, packet.MessageType, packet.Payload.Length);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("[{Name}] Send error: {Msg}", ServerName, ex.Message);
        }
    }

    protected async Task SendMediusAsync(TcpClient tcp, MediusPacket msg, CancellationToken ct)
    {
        var payload = msg.Serialize();
        await SendPacketAsync(tcp, ScertPacket.CreateServerApp(payload), ct);
    }

    // -------------------------------------------------------------------------
    // Abstract / virtual
    // -------------------------------------------------------------------------

    protected virtual async Task OnClientConnectedAsync(MediusClientState state, TcpClient tcp)
    {
        // Send SERVER_HELLO immediately so clients that skip CLIENT_HELLO still get it.
        // All-zero RSA modulus signals "no RSA encryption" to the client.
        await SendPacketAsync(tcp, ScertPacket.CreateHello(), default);
        state.HelloReceived = true;
    }

    protected virtual Task OnClientDisconnectedAsync(MediusClientState state)
        => Task.CompletedTask;

    /// <summary>
    /// Called for raw RT transport-layer messages that are NOT decoded as Medius
    /// application-layer packets (e.g. DME game relay messages).
    /// </summary>
    protected virtual Task HandleRtMessageAsync(
        RT_MSG_TYPE msgType, byte[] payload, MediusClientState state, TcpClient tcp, CancellationToken ct)
        => Task.CompletedTask;

    protected abstract Task HandleMediusMessageAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct);
}
