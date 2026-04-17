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
/// SCERT Handshake (SOCOM II / old Medius v &lt;= 108, 2002-2004 PS2 games):
///   CLIENT_HELLO               → SERVER_HELLO (128-byte RSA N or zeros)
///   CLIENT_CRYPTKEY_PUBLIC     → SERVER_CRYPTKEY_PEER  (PS2 auto-sends CLIENT_CONNECT_TCP next — no server trigger needed)
///   CLIENT_CONNECT_TCP         → [CRYPTKEY_GAME (MAS/MLS only)] + SERVER_CONNECT_ACCEPT_TCP + SERVER_CONNECT_COMPLETE
///   CLIENT_CONNECT_READY_TCP   → SERVER_CONNECT_COMPLETE
///   &lt;Medius app messages follow&gt;
///
/// SCERT Handshake (New Medius v &gt; 108, PS3 era):
///   ... same key exchange ...
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

    /// <summary>
    /// Whether to send SERVER_CRYPTKEY_GAME during the old-protocol CLIENT_CONNECT_TCP handshake.
    /// MUIS does NOT send it (matches Horizon); MAS and MLS do.
    /// </summary>
    protected virtual bool SendCryptKeyGameOnConnect => true;

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
                    // DISABLED: RC4 XOR NOP'd in PS2 EE memory — payload is plaintext despite 0x80 flag.
                    // if (packet.IsEncrypted && packet.Hash != null && state.CryptKeyReceived)
                    //     PS2_RC4.Decrypt(state.SessionRc4Key, packet.Hash, packet.Payload);

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

            case RT_MSG_TYPE.RT_MSG_CLIENT_CRYPTKEY_PEER:
                // PS2 sends its own session key encrypted with the server's RSA public key
                // as mutual authentication. No server response is required; PS2 proceeds to
                // CLIENT_CONNECT_TCP immediately after this.
                _log.LogInformation("[{Name}] <- CLIENT_CRYPTKEY_PEER (mutual auth, {Len} bytes)", ServerName, packet.Payload.Length);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_READY_REQUIRE:
                await HandleConnectReadyRequireAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_READY_TCP:
                await HandleConnectReadyTcpAsync(packet, state, tcp, ct);
                break;

            case RT_MSG_TYPE.RT_MSG_CLIENT_ECHO:
                await SendPacketAsync(tcp, ScertPacket.CreateEchoResponse(packet.Payload), state, ct);
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

    private async Task HandleHelloAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // CLIENT_HELLO payload: array of ushorts.
        // Parameters[0] = unknown/flags, Parameters[1] = MediusVersion
        if (packet.Payload.Length >= 4)
            state.MediusVersion = BitConverter.ToUInt16(packet.Payload, 2);  // Parameters[1]
        else if (packet.Payload.Length >= 2)
            state.MediusVersion = BitConverter.ToUInt16(packet.Payload, 0);  // fallback

        state.HelloReceived = true;
        _log.LogInformation("[{Name}] CLIENT_HELLO ver={Ver} from {Ep}", ServerName, state.MediusVersion, state.RemoteEndpoint);

        // Respond with SERVER_HELLO.  Some games (e.g. Ratchet) send CLIENT_HELLO
        // and expect a reply.  SOCOM II never sends CLIENT_HELLO (it expects the
        // proactive one from OnClientConnectedAsync), but responding here is harmless.
        await SendPacketAsync(tcp, ScertPacket.CreateHello(), ct);
    }

    private async Task HandleCryptKeyPublicAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.CryptKeyReceived = true;

        _log.LogDebug("[{Name}] CLIENT_CRYPTKEY_PUBLIC ({Len} bytes) hash={HashHex} payload={Hex}",
            ServerName, packet.Payload.Length,
            packet.Hash != null ? BitConverter.ToString(packet.Hash).Replace("-", " ") : "null",
            BitConverter.ToString(packet.Payload).Replace("-", " "));

        // CLIENT_CRYPTKEY_PUBLIC payload is RSA(N_ps2, E=17, N_server) — the PS2's public modulus
        // encrypted with our server key. Decrypt with D_server to recover N_ps2.
        // The +N fallback (inside Decrypt) handles the case where N_ps2 >= N_server.
        // This mirrors Horizon: BaseScertMessage.Instantiate → cipherService.Decrypt → Key → GenerateCipher.
        if (!Ps2Rsa.ServerKey.Decrypt(packet.Payload, packet.Hash ?? Array.Empty<byte>(), out var ps2NBytes))
        {
            _log.LogWarning("[{Name}] CLIENT_CRYPTKEY_PUBLIC: RSA decrypt / hash verification failed — closing", ServerName);
            tcp.Close();
            return;
        }

        _log.LogDebug("[{Name}] N_ps2 (decrypted, LE)={Hex}", ServerName,
            BitConverter.ToString(ps2NBytes).Replace("-", " "));

        state.ClientRsa = Ps2Rsa.FromClientPublicKey(ps2NBytes);

        // Generate a random 64-byte RC4 session key.
        state.SessionRc4Key = PS2_RC4.GenerateKey();

        _log.LogDebug("[{Name}] SessionRc4Key={Hex}", ServerName,
            BitConverter.ToString(state.SessionRc4Key).Replace("-", " "));

        // RSA-encrypt the RC4 key with N_ps2, E=17 so the PS2 can decrypt with D_ps2.
        var encryptedKey = state.ClientRsa.Encrypt(state.SessionRc4Key);
        var peerHash = PS2_RC4.ComputeHash(state.SessionRc4Key, CipherContext.RsaAuth);

        _log.LogDebug("[{Name}] CRYPTKEY_PEER hash={HashHex} payload={Hex}", ServerName,
            BitConverter.ToString(peerHash).Replace("-", " "),
            BitConverter.ToString(encryptedKey).Replace("-", " "));

        await SendPacketAsync(tcp, ScertPacket.CreateCryptKeyPeer(encryptedKey, peerHash), ct);
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
            // New protocol: send SERVER_CONNECT_REQUIRE, client responds with CLIENT_CONNECT_READY_REQUIRE
            await SendPacketAsync(tcp, ScertPacket.CreateConnectRequire(), ct);
        }
        else
        {
            // Old protocol (SOCOM II era): complete the handshake immediately.
            // MUIS omits CRYPTKEY_GAME; MAS/MLS send it (matches Horizon's per-server behaviour).
            if (SendCryptKeyGameOnConnect)
                await SendPacketAsync(tcp, ScertPacket.CreateCryptKeyGame(state.SessionRc4Key), state, ct);
            await SendPacketAsync(tcp, ScertPacket.CreateConnectAcceptTcp(state.ScertId, 0, 1, clientIp, state.MediusVersion), state, ct);
            await SendPacketAsync(tcp, ScertPacket.CreateConnectComplete(), state, ct);
            state.IsSessionEstablished = true;
            _log.LogInformation("[{Name}] SCERT session established (old proto) for {Ep}", ServerName, state.RemoteEndpoint);
        }
    }

    private async Task HandleConnectReadyRequireAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // PS2 replied to SERVER_CONNECT_REQUIRE — allocate ScertId and complete the handshake.
        state.ScertId = Interlocked.Increment(ref _scertIdCounter);
        var clientIp = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
        if (SendCryptKeyGameOnConnect)
            await SendPacketAsync(tcp, ScertPacket.CreateCryptKeyGame(state.SessionRc4Key), state, ct);
        await SendPacketAsync(tcp, ScertPacket.CreateConnectAcceptTcp(state.ScertId, 0, 1, clientIp, state.MediusVersion), state, ct);
        await SendPacketAsync(tcp, ScertPacket.CreateConnectComplete(), state, ct);
        state.IsSessionEstablished = true;
        _log.LogInformation("[{Name}] SCERT session established (CONNECT_READY_REQUIRE path) for {Ep}", ServerName, state.RemoteEndpoint);
    }

    private async Task HandleConnectReadyTcpAsync(ScertPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.ConnectReadyReceived = true;
        await SendPacketAsync(tcp, ScertPacket.CreateConnectComplete(), state, ct);
        if (state.MediusVersion > 108)
            await SendPacketAsync(tcp, ScertPacket.CreateServerEcho(), state, ct);
        if (!state.IsSessionEstablished)
        {
            state.IsSessionEstablished = true;
            _log.LogInformation("[{Name}] SCERT session established for {Ep}", ServerName, state.RemoteEndpoint);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Send a raw (unencrypted) SCERT packet — used only for pre-handshake messages.</summary>
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

    /// <summary>
    /// Send a SCERT packet, RC4-encrypting the payload if the session key is established.
    /// Packets whose <see cref="ScertPacket.Hash"/> is already set (e.g. RSA-encrypted CRYPTKEY_PEER)
    /// are sent as-is without a second encryption pass.
    /// </summary>
    protected Task SendPacketAsync(TcpClient tcp, ScertPacket packet, MediusClientState state, CancellationToken ct)
    {
        // DISABLED: RC4 XOR NOP'd in PS2 EE memory — send plaintext so PS2 accepts without decrypt.
        // if (state.SessionRc4Key != null && packet.Hash == null)
        // {
        //     PS2_RC4.Encrypt(state.SessionRc4Key, CipherContext.RcClientSession,
        //         packet.Payload, out var cipher, out var hash);
        //     packet.Hash    = hash;
        //     packet.Payload = cipher;
        // }
        return SendPacketAsync(tcp, packet, ct);
    }

    protected Task SendMediusAsync(TcpClient tcp, MediusPacket msg, MediusClientState state, CancellationToken ct)
    {
        var payload = msg.Serialize();
        return SendPacketAsync(tcp, ScertPacket.CreateServerApp(payload), state, ct);
    }

    /// <summary>
    /// Legacy overload used by derived-class handlers that have not yet been updated to pass state.
    /// Sends the SERVER_APP packet unencrypted; works because the PS2 SCERT overlay accepts
    /// both encrypted and plaintext packets based on the 0x80 type-byte flag.
    /// </summary>
    protected Task SendMediusAsync(TcpClient tcp, MediusPacket msg, CancellationToken ct)
    {
        var payload = msg.Serialize();
        return SendPacketAsync(tcp, ScertPacket.CreateServerApp(payload), ct);
    }

    // -------------------------------------------------------------------------
    // Abstract / virtual
    // -------------------------------------------------------------------------

    protected virtual async Task OnClientConnectedAsync(MediusClientState state, TcpClient tcp)
    {
        // SOCOM II (and other old-protocol PS2 games) expect SERVER_HELLO proactively
        // on connect.  They never send CLIENT_HELLO — they go straight to
        // CLIENT_CRYPTKEY_PUBLIC after receiving SERVER_HELLO.
        await SendPacketAsync(tcp, ScertPacket.CreateHello(), default);
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
