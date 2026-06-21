using System.Net.Sockets;
using System.Text.Json;
using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Models;
using CodeDesigner.Ps2.Multiplayer.Protocol;
using CodeDesigner.Ps2.Multiplayer.Storage;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// MLS — Medius Lobby Server.
/// Clients connect here after authenticating on MAS.
/// Provides channel listing, game creation/joining, chat, buddy lists, etc.
///
/// Default port: 10073 (TCP)
/// </summary>
public class MlsServer : BaseMediusTcpServer
{
    private readonly MultiplayerConfig       _config;
    private readonly InMemoryStorage    _store;
    private readonly ILogger<MlsServer> _log;

    public MlsServer(MultiplayerConfig config, InMemoryStorage store, ILogger<MlsServer> log)
        : base(log, config)
    {
        _config    = config;
        _store     = store;
        _log       = log;
        ServerName = "MLS";
    }

    private FileSystemWatcher? _injectWatcher;

    public Task StartAsync(CancellationToken ct)
    {
        StartInjectDropWatcher(ct);
        return StartAsync(_config.BindAddress, _config.MlsPort, ct);
    }

    // Fire the SCERT session-established callback right after the old-proto handshake so
    // configured binary injections go out as soon as the player reaches the lobby.
    protected override bool PushDataAfterConnectComplete => true;

    // CONFIRMED on hardware: streaming our 13-byte CreateCheatQuery in the MLS lobby = INSTANT disconnect
    // (anti-cheat violation) the moment the community screen loads. So the lobby cheat heartbeat stays OFF
    // until we have the official 29-byte real-region challenge format. (LobbyCheatHeartbeat defaults false.)

    /// <summary>A player has reached the lobby — push any configured binary injections.</summary>
    protected override async Task OnSessionEstablishedAsync(MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        await SendConfiguredInjectionsAsync(tcp, ct);
    }

    // ── On-demand injection: watch a drop folder for *.json manifests ─────────
    private void StartInjectDropWatcher(CancellationToken ct)
    {
        var folder = _config.InjectDropFolder;
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            _injectWatcher = new FileSystemWatcher(folder, "*.json") { EnableRaisingEvents = true };
            _injectWatcher.Created += (_, e) => _ = HandleInjectManifestAsync(e.FullPath, ct);
            _log.LogInformation("[MLS] Watching inject drop folder: {Folder}", folder);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[MLS] Could not start inject drop watcher on '{Folder}': {Msg}", folder, ex.Message);
        }
    }

    private async Task HandleInjectManifestAsync(string path, CancellationToken ct)
    {
        try
        {
            // The file may still be being written when the event fires — retry briefly.
            byte[] bytes = Array.Empty<byte>();
            for (int i = 0; i < 10; i++)
            {
                try { bytes = await File.ReadAllBytesAsync(path, ct); if (bytes.Length > 0) break; }
                catch (IOException) { await Task.Delay(100, ct); }
            }
            var inj = JsonSerializer.Deserialize<BinaryInjection>(bytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (inj == null || !TryParseAddr(inj.LoadAddress, out uint loadAddr))
            {
                _log.LogWarning("[MLS] Inject manifest '{P}': missing/invalid LoadAddress", Path.GetFileName(path));
                return;
            }

            // Resolve the .bin relative to the manifest first, then the usual locations.
            string binPath = inj.File;
            if (!File.Exists(binPath))
                binPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", inj.File);
            if (!File.Exists(binPath))
            {
                _log.LogWarning("[MLS] Inject manifest '{P}': bin not found '{F}'", Path.GetFileName(path), inj.File);
                return;
            }

            uint? hookAddr  = TryParseAddr(inj.HookAddress, out uint h) ? h : null;
            uint? entryAddr = TryParseAddr(inj.EntryPoint, out uint e) ? e : null;
            var blob = await File.ReadAllBytesAsync(binPath, ct);

            int n = await InjectIntoAllAsync(loadAddr, blob, hookAddr, entryAddr, inj.ChunkSize, ct);
            _log.LogInformation("[MLS] On-demand inject '{N}' -> {Count} client(s)",
                string.IsNullOrEmpty(inj.Name) ? Path.GetFileName(binPath) : inj.Name, n);

            // Consume the manifest so it doesn't re-fire.
            try { File.Delete(path); } catch { /* ignore */ }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("[MLS] Inject manifest '{P}' failed: {Msg}", Path.GetFileName(path), ex.Message);
        }
    }

    protected override Task OnClientDisconnectedAsync(MediusClientState state)
    {
        // Remove player from whatever channel/game they were in
        if (state.CurrentChannelId.HasValue)
        {
            var ch = _store.GetChannelById(state.CurrentChannelId.Value);
            if (state.AccountId.HasValue) ch?.RemovePlayer(state.AccountId.Value);
        }
        if (state.CurrentGameId.HasValue)
        {
            var game = _store.GetGameById(state.CurrentGameId.Value);
            if (state.AccountId.HasValue) game?.RemovePlayer(state.AccountId.Value);
        }
        return Task.CompletedTask;
    }

    // =========================================================================
    // Medius message dispatch
    // =========================================================================

    protected override async Task HandleMediusMessageAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        if (packet.MessageClass == NetMessageClass.MessageClassLobby)
        {
            var typeId = (MediusLobbyMessageId)packet.MessageType;
            _log.LogDebug("[MLS] Lobby msg: {Type}", typeId);

            switch (typeId)
            {
                case MediusLobbyMessageId.SessionBegin:
                    await HandleSessionBeginAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.AccountLogout:
                    await HandleAccountLogoutAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.GetAnnouncementsResponse:
                    break; // client-side only

                case MediusLobbyMessageId.GetAnnouncements:
                case MediusLobbyMessageId.GetAllAnnouncements:
                    await HandleGetAnnouncementsAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.ChannelList:
                case MediusLobbyMessageId.ChannelList_ExtraInfo0:
                    await HandleChannelListAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.JoinChannel:
                    await HandleJoinChannelAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.GameList:
                case MediusLobbyMessageId.GameList_ExtraInfo0:
                    await HandleGameListAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.CreateGameRequest0:
                case MediusLobbyMessageId.CreateGame1:
                    await HandleCreateGameAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.JoinGameRequest0:
                case MediusLobbyMessageId.JoinGame:
                    await HandleJoinGameAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.PlayerInfo:
                    await HandlePlayerInfoAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.GameInfo0:
                    await HandleGameInfoAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.ChannelInfo:
                    await HandleChannelInfoAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.FindPlayer:
                    await HandleFindPlayerAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.ChatMessage:
                    await HandleChatMessageAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.UpdateUserState:
                    // Keepalive / status update — no response needed
                    break;

                case MediusLobbyMessageId.GetLocations:
                    await HandleGetLocationsAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.PickLocation:
                    await HandlePickLocationAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.VersionServer:
                    await HandleVersionServerAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.SetGameListFilter0:
                    await HandleSetGameListFilterAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.AccountUpdateStats:
                    await HandleAccountUpdateStatsAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.UpdateLadderStats:
                    await HandleUpdateLadderStatsAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.FileListFiles:
                    await HandleFileListFilesAsync(packet, state, tcp, ct);
                    break;

                // ── Community-screen init batch ─────────────────────────────
                // After the EULA/community screen the client fires its lobby-init
                // queries (buddy list, ignore list, clans, ladders, Policy). It waits
                // for replies to ALL of them; if any go unanswered it times out and
                // tears down every socket ("Disconnected from network"). We answer
                // each with the minimal valid Medius response (msgId echo + status +
                // empty list), framed exactly like the working MAS handlers.
                case MediusLobbyMessageId.Policy:
                    await HandlePolicyAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyMessageId.GetMyClans:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetMyClansResponse, ct);
                    break;

                case MediusLobbyMessageId.CheckMyClanInvitations:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.CheckMyClanInvitationsResponse, ct);
                    break;

                case MediusLobbyMessageId.ClanLadderList:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.ClanLadderListResponse, ct);
                    break;

                case MediusLobbyMessageId.LadderList_ExtraInfo0:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.LadderList_ExtraInfoResponse, ct);
                    break;

                case MediusLobbyMessageId.LadderPosition_ExtraInfo:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.LadderPosition_ExtraInfoResponse, ct);
                    break;

                case MediusLobbyMessageId.GetBuddyList_ExtraInfo:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetBuddyList_ExtraInfoResponse, ct);
                    break;

                case MediusLobbyMessageId.GetIgnoreList:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetIgnoreListResponse, ct);
                    break;

                // ── Clan info sweep (follows the init batch on the community screen) ──
                case MediusLobbyMessageId.GetClanMemberList_ExtraInfo:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetClanMemberList_ExtraInfoResponse, ct);
                    break;

                case MediusLobbyMessageId.GetClanMemberList:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetClanMemberListResponse, ct);
                    break;

                case MediusLobbyMessageId.GetMyClanMessages:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetMyClanMessagesResponse, ct);
                    break;

                case MediusLobbyMessageId.GetAllClanMessages:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetAllClanMessagesResponse, ct);
                    break;

                case MediusLobbyMessageId.GetClanInvitationsSent:
                    await SendEmptyLobbyListAsync(packet, tcp, MediusLobbyMessageId.GetClanInvitationsSentResponse, ct);
                    break;

                default:
                    _log.LogDebug("[MLS] Unhandled lobby msg 0x{Type:X2}", packet.MessageType);
                    break;
            }
        }
        else if (packet.MessageClass == NetMessageClass.MessageClassLobbyExt)
        {
            var typeId = (MediusLobbyExtMessageId)packet.MessageType;
            _log.LogDebug("[MLS] LobbyExt msg: {Type}", typeId);

            switch (typeId)
            {
                case MediusLobbyExtMessageId.GetUniverse_ExtraInfo:
                    await HandleGetUniverseAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.ChannelList_ExtraInfo:
                    await HandleChannelListExtAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.GameList_ExtraInfo:
                case MediusLobbyExtMessageId.GameList_ExtraInfoResponse:
                    await HandleGameListExtAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.CreateGame:
                    await HandleCreateGameAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.GenericChatMessage:
                    await HandleGenericChatAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.UtilGetServerVersion:
                    await HandleUtilGetServerVersionAsync(packet, state, tcp, ct);
                    break;

                case MediusLobbyExtMessageId.DnasSignaturePost:
                    break; // ignore DNAS

                case MediusLobbyExtMessageId.WorldReport:
                    break; // server-to-server, ignore from client

                case MediusLobbyExtMessageId.GetBuddyInvitations:
                    // Part of the community-screen init batch — answer with an empty list
                    // so the client doesn't block waiting for it.
                    await SendEmptyLobbyExtListAsync(packet, tcp, MediusLobbyExtMessageId.GetBuddyInvitationsResponse, ct);
                    break;

                default:
                    _log.LogDebug("[MLS] Unhandled lobbyExt msg 0x{Type:X2}", packet.MessageType);
                    break;
            }
        }
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    private async Task HandleSessionBeginAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.SessionKey = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        state.Status     = PlayerStatus.InChatWorld;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString(state.SessionKey, 64);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.SessionBeginResponse, w.ToArray()), ct);
    }

    private async Task HandleAccountLogoutAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        state.Status = PlayerStatus.Disconnected;
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.AccountLogoutResponse, w.ToArray()), ct);
    }

    private async Task HandleGetAnnouncementsAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        string msg = _config.ServerNews;

        // Layout (old-proto GetAnnouncementsResponse): header + AnnouncementID(4) +
        // Announcement text + AnnouncementType(4) + EndOfList. The AnnouncementID is a
        // 4-byte field BETWEEN status and the text — on hardware, replacing it with a
        // 1-byte EndOfList shifted the text 3 bytes and chopped "Wel" off "Welcome".
        var w = new MediusWriter();
        WriteMlsHeader(w, packet, MediusCallbackStatus.MediusSuccess);
        w.Write(0);               // AnnouncementID  (4) — text must start right after this
        w.WriteString(msg, 256);  // Announcement text
        w.Write(0);               // AnnouncementType (4)
        w.Write((byte)1);         // EndOfList
        w.WritePadding(3);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.GetAnnouncementsResponse, w.ToArray()), ct);
    }

    private async Task HandleGetUniverseAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // Universe variable info — send a minimal response
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)1);  // End of list
        w.WriteString("CodeDesigner Universe", 64);
        w.WriteString("http://localhost/", 128);

        await SendMediusAsync(tcp, MediusPacket.BuildLobbyExt(MediusLobbyExtMessageId.UniverseStatusList_ExtraInfoResponse, w.ToArray()), ct);
    }

    private async Task HandleChannelListAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // ChannelList is the first community-screen init message — mark this connection as the community
        // connection so PlayerInfo polls on it get answered (the watchdog fix), while the login-batch
        // connection's PlayerInfo stays silent (answering it breaks the community transition).
        state.CommunityReached = true;

        // Reply with the response type that MATCHES the request: ChannelList_ExtraInfo0 (0xEC)
        // must be answered with ChannelList_ExtraInfoResponse (0xED), not the basic
        // ChannelListResponse (0x2C). Old-proto correlates a reply to its request by the echoed
        // messageID — the previous handler omitted the echo AND used the wrong type, so the
        // client's ChannelList_ExtraInfo0 transaction never completed and the community-screen
        // watchdog eventually timed out ("Disconnected from Network").
        var responseType = packet.MessageType == (byte)MediusLobbyMessageId.ChannelList_ExtraInfo0
            ? MediusLobbyMessageId.ChannelList_ExtraInfoResponse
            : MediusLobbyMessageId.ChannelListResponse;

        var channels = _store.GetChannels(state.AppId).ToList();
        if (channels.Count == 0)
            channels.Add(_store.GetOrCreateDefaultChannel(state.AppId));

        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            bool isLast = i == channels.Count - 1;

            var w = new MediusWriter();
            WriteMlsHeader(w, packet, MediusCallbackStatus.MediusSuccess);  // msgId echo + pad + status
            w.Write((byte)(isLast ? 1 : 0));  // EndOfList
            w.Write(ch.ChannelId);
            w.WriteString(ch.ChannelName, 64);
            w.Write(ch.PlayerCount);
            w.Write(ch.MaxPlayers);

            await SendMediusAsync(tcp, MediusPacket.BuildLobby(responseType, w.ToArray()), ct);
        }
    }

    private async Task HandleChannelListExtAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var channels = _store.GetChannels(state.AppId).ToList();
        if (channels.Count == 0)
            channels.Add(_store.GetOrCreateDefaultChannel(state.AppId));

        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            bool isLast = i == channels.Count - 1;

            var w = new MediusWriter();
            w.Write((int)MediusCallbackStatus.MediusSuccess);
            w.Write((byte)(isLast ? 1 : 0));
            w.Write(ch.ChannelId);
            w.WriteString(ch.ChannelName, 64);
            w.Write(ch.PlayerCount);
            w.Write(ch.MaxPlayers);
            w.Write(ch.GenericField1);
            w.Write(ch.GenericField2);
            w.Write(ch.GenericField3);

            await SendMediusAsync(tcp, MediusPacket.BuildLobbyExt(MediusLobbyExtMessageId.ChannelList_ExtraInfo, w.ToArray()), ct);
        }
    }

    private async Task HandleJoinChannelAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        int    channelId  = r.ReadInt32();

        var channel = _store.GetChannelById(channelId)
                   ?? _store.GetOrCreateDefaultChannel(state.AppId);

        if (channel.IsFull)
        {
            var wFail = new MediusWriter();
            wFail.Write((int)MediusCallbackStatus.MediusFail);
            wFail.Write(0);
            await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.JoinChannelResponse, wFail.ToArray()), ct);
            return;
        }

        if (state.AccountId.HasValue)
            channel.AddPlayer(state.AccountId.Value);

        state.CurrentChannelId = channel.ChannelId;
        state.Status           = PlayerStatus.InChatWorld;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(channel.ChannelId);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.JoinChannelResponse, w.ToArray()), ct);
    }

    private async Task HandleGameListAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var games = _store.GetGames(state.AppId).ToList();

        if (games.Count == 0)
        {
            var wEmpty = new MediusWriter();
            wEmpty.Write((int)MediusCallbackStatus.MediusNoResult);
            wEmpty.Write((byte)1);  // EndOfList
            await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.GameListResponse, wEmpty.ToArray()), ct);
            return;
        }

        for (int i = 0; i < games.Count; i++)
        {
            var g = games[i];
            bool isLast = i == games.Count - 1;
            await SendGameListEntry(tcp, g, isLast, MediusLobbyMessageId.GameListResponse, ct);
        }
    }

    private async Task HandleGameListExtAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var games = _store.GetGames(state.AppId).ToList();

        if (games.Count == 0)
        {
            var wEmpty = new MediusWriter();
            wEmpty.Write((int)MediusCallbackStatus.MediusNoResult);
            wEmpty.Write((byte)1);
            await SendMediusAsync(tcp, MediusPacket.BuildLobbyExt(MediusLobbyExtMessageId.GameList_ExtraInfoResponse, wEmpty.ToArray()), ct);
            return;
        }

        for (int i = 0; i < games.Count; i++)
        {
            var g = games[i];
            bool isLast = i == games.Count - 1;
            await SendGameListEntryExt(tcp, g, isLast, ct);
        }
    }

    private async Task HandleCreateGameAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        string gameName   = r.ReadString(64);
        r.Skip(4);  // reserved / RulesSet
        int maxPlayers = r.Remaining >= 4 ? r.ReadInt32() : 8;

        _log.LogInformation("[MLS] CreateGame: '{Name}' host={Host}", gameName, state.AccountName);

        var game = _store.CreateGame(
            gameName,
            state.AppId,
            state.AccountId ?? 0,
            state.AccountName,
            maxPlayers);

        // Create a matching DME world for this game
        var dmeWorld = DmeServer.CreateWorld(state.AppId, maxPlayers);
        game.DmeWorldId = dmeWorld.WorldId;

        state.CurrentGameId = game.GameId;
        state.Status        = PlayerStatus.InGameWorld;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(game.GameId);
        // DME connection info
        w.WriteString(_config.ServerIp, 16);
        w.Write((ushort)_config.DmePort);
        w.WritePadding(2);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.CreateGameResponse, w.ToArray()), ct);
    }

    private async Task HandleJoinGameAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        int    gameId     = r.ReadInt32();

        var game = _store.GetGameById(gameId);

        if (game == null || game.IsFull || game.Status == GameWorldStatus.WorldClosed)
        {
            var wFail = new MediusWriter();
            wFail.Write((int)MediusCallbackStatus.MediusFail);
            wFail.Write(0);
            await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.JoinGameResponse, wFail.ToArray()), ct);
            return;
        }

        if (state.AccountId.HasValue)
            game.AddPlayer(state.AccountId.Value);

        state.CurrentGameId = game.GameId;
        state.Status        = PlayerStatus.InGameWorld;

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write(game.GameId);
        w.WriteString(_config.ServerIp, 16);
        w.Write((ushort)_config.DmePort);
        w.WritePadding(2);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.JoinGameResponse, w.ToArray()), ct);
    }

    // Old-proto (SOCOM II) Medius responses begin with the request's 21-byte messageID echo
    // + 3 pad bytes + the 4-byte status, exactly like the working MAS handlers. The MLS
    // handlers were written without this header, so SOCOM II read every reply as garbage.
    private static byte[] MlsMsgId(MediusPacket packet) =>
        packet.Payload.Length >= MediusConstants.MESSAGEID_MAXLEN
            ? packet.Payload[..MediusConstants.MESSAGEID_MAXLEN]
            : new byte[MediusConstants.MESSAGEID_MAXLEN];

    /// <summary>
    /// Old-proto MLS response header, matching the working MAS handlers:
    /// [msgId echo:21][pad:3][status:4]. The client correlates the reply to its
    /// pending request by the echoed messageID, so this prefix is mandatory.
    /// </summary>
    private static void WriteMlsHeader(MediusWriter w, MediusPacket packet, MediusCallbackStatus status)
    {
        w.Write(MlsMsgId(packet));            // 21 — messageID echo
        w.WritePadding(3);                    // 3  — alignment
        w.Write((int)status);                 // 4  — StatusCode
    }

    /// <summary>
    /// Minimal "empty list" reply for a community-screen init query: header + an
    /// end-of-list marker. Success (not an error) with no entries so the client
    /// stops waiting and the lobby doesn't time out. Used for buddy/ignore/clan/
    /// ladder lists, which are legitimately empty on a fresh private server.
    /// </summary>
    private async Task SendEmptyLobbyListAsync(MediusPacket packet, TcpClient tcp, MediusLobbyMessageId responseType, CancellationToken ct)
    {
        var w = new MediusWriter();
        WriteEmptyListBody(w, packet);
        _log.LogDebug("[MLS] -> {Resp} (empty list)", responseType);
        await SendMediusAsync(tcp, MediusPacket.BuildLobby(responseType, w.ToArray()), ct);
    }

    /// <summary>LobbyExt-class variant of <see cref="SendEmptyLobbyListAsync"/>.</summary>
    private async Task SendEmptyLobbyExtListAsync(MediusPacket packet, TcpClient tcp, MediusLobbyExtMessageId responseType, CancellationToken ct)
    {
        var w = new MediusWriter();
        WriteEmptyListBody(w, packet);
        _log.LogDebug("[MLS] -> {Resp} (empty list)", responseType);
        await SendMediusAsync(tcp, MediusPacket.BuildLobbyExt(responseType, w.ToArray()), ct);
    }

    /// <summary>
    /// Body for an empty list reply. Status = <see cref="MediusCallbackStatus.MediusNoResult"/>
    /// (the game's own "no entries" signal — see HandleGameListAsync) so the parser does NOT
    /// read any entry fields. CRITICAL: the minimal 32-byte form let the game read a full
    /// list-entry struct off the end of our short reply, pulling STALE bytes from the previous
    /// packet (the EULA/news text) into clan/buddy NAME fields → visible "bleed" in the UI and
    /// likely garbage IDs behind the disconnect. We zero-pad past the largest entry struct so any
    /// field the parser still touches reads clean zeros instead of leftover buffer.
    /// </summary>
    private static void WriteEmptyListBody(MediusWriter w, MediusPacket packet)
    {
        WriteMlsHeader(w, packet, MediusCallbackStatus.MediusNoResult);  // msgId + pad + status
        w.Write((byte)1);          // EndOfList
        w.WritePadding(3);         // align to 4
        w.WritePadding(256);       // zero filler — keeps stale-buffer reads clean (empty names/ids)
    }

    /// <summary>
    /// PolicyResponse (0x48) — the client requests the usage/EULA policy at the
    /// community screen. Layout mirrors the working MAS HandlePolicyAsync and the
    /// 0x4801 struct schema: header + Policy Message[POLICY_MAXLEN] + EndOfText.
    /// </summary>
    private async Task HandlePolicyAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        string policyText = _config.EulaText;

        var w = new MediusWriter();
        WriteMlsHeader(w, packet, MediusCallbackStatus.MediusSuccess);
        w.WriteString(policyText, MediusConstants.POLICY_MAXLEN);   // 256
        w.Write((byte)1);                                          // EndOfText
        w.WritePadding(3);
        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.PolicyResponse, w.ToArray()), ct);
    }

    // Watchdog RE (SCUS-97275): the ~2-min community-screen "Disconnected from Network" is a game-side
    // watchdog — the lobby PlayerInfo (0x31) poll's completion callback is cbIsGameAlive (fn 0x0030C620),
    // which shows "_1067_MEDIUS_DISCONNECTED_GAME_MSG" unless the response StatusCode (offset 0x18) == 0
    // (checked via 0x00311560 = `return status == 0 ? 1 : 0`). HOWEVER, replying to THIS login-batch
    // PlayerInfo regressed the flow (see handler note below), so the watchdog must be addressed via the
    // community-screen poll specifically, not here.
    //
    // PlayerInfo (0x31) is left UNANSWERED. Although live RE showed its completion callback is
    // cbIsGameAlive (the disconnect-modal, game fn 0x0030C620) and that StatusCode==0 satisfies it,
    // sending ANY PlayerInfoResponse during this initial login-batch — even with the correct queried
    // accountId+name (acct=2/zzzzz) and StatusCode=Success — stops the game from advancing to the
    // community-screen reconnect (the ChannelList/announcements batch never follows; it just idles on
    // CLIENT_ECHO). The known-good baseline reaches the fully-populated community screen with PlayerInfo
    // silent here, then soft-disconnects after ~2 min. Do NOT answer this one without a different,
    // RE-verified approach (the community-screen PlayerInfo poll is what the watchdog rides on, and must
    // be handled separately from this login-batch poll).
    private async Task HandlePlayerInfoAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // LOGIN-batch connection (no ChannelList seen yet): stay SILENT — answering here stops the game
        // from advancing to the community reconnect.
        if (!state.CommunityReached)
        {
            _log.LogDebug("[MLS] PlayerInfo received (login batch — no reply)");
            return;
        }

        // COMMUNITY connection: answer the poll so its cbIsGameAlive completion callback sees
        // StatusCode==0 (MediusSuccess) and does NOT fire the ~2-min "Disconnected from Network" watchdog.
        // Request layout: [msgId:21][sessionKey:17][pad:2][accountId:4]; echo the queried accountId + name.
        var r = new MediusReader(packet.Payload);
        r.Skip(MediusConstants.MESSAGEID_MAXLEN + MediusConstants.SESSIONKEY_MAXLEN + 2);
        int accountId = r.ReadInt32();
        if (accountId <= 0) accountId = state.AccountId ?? 0;
        var account = _store.GetAccountById(accountId);
        string name = account?.AccountName ?? state.AccountName ?? string.Empty;

        var w = new MediusWriter();
        WriteMlsHeader(w, packet, MediusCallbackStatus.MediusSuccess);          // msgId echo + pad + StatusCode=0 @0x18
        w.Write(accountId);                                                     // AccountID (echo queried id)
        w.WriteString(name, MediusConstants.ACCOUNTNAME_MAXLEN);                // 32
        w.WritePadding(MediusConstants.ACCOUNTSTATS_MAXLEN);                    // 256 — Stats
        w.Write((int)MediusPlayerStatus.MediusPlayerInChatWorld);              // PlayerStatus
        w.Write((int)MediusConnectionType.Ethernet);                           // ConnectionClass
        w.WritePadding(32);                                                     // zero filler

        _log.LogDebug("[MLS] -> PlayerInfoResponse (community poll) acct={Id}/{Name}", accountId, name);
        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.PlayerInfoResponse, w.ToArray()), ct);
    }

    private Task HandleSetGameListFilterAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        _log.LogDebug("[MLS] SetGameListFilter0 received (no reply — minimal lobby)");
        return Task.CompletedTask;
    }

    private Task HandleAccountUpdateStatsAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        _log.LogDebug("[MLS] AccountUpdateStats received (no reply — minimal lobby)");
        return Task.CompletedTask;
    }

    private Task HandleUpdateLadderStatsAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        _log.LogDebug("[MLS] UpdateLadderStats received (no reply — minimal lobby)");
        return Task.CompletedTask;
    }

    private Task HandleFileListFilesAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // UNANSWERED — exactly as in the L654 state that reached the EULA + community screen. The game
        // sends FileListFiles for its weapon profile but does NOT block the lobby on the reply.
        _log.LogDebug("[MLS] FileListFiles received (no reply — minimal lobby)");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Write a zeroed 172-byte Medius <c>MediusFile</c> struct (Filename[128] + ServerChecksum[16]
    /// + FileID/FileSize/CreationTimeStamp/OwnerID/GroupID (5×u32) + 4×u16 permission/op fields),
    /// little-endian, matching Horizon's RT.Models/Misc/MediusFile.cs serializer.
    /// </summary>
    private static void WriteEmptyMediusFile(MediusWriter w)
    {
        w.WriteString("", 128);   // Filename[MEDIUS_FILE_MAX_FILENAME_LENGTH]
        w.WritePadding(16);       // ServerChecksum[MEDIUS_FILE_CHECKSUM_NUMBYTES]
        w.Write(0);               // FileID
        w.Write(0);               // FileSize
        w.Write(0);               // CreationTimeStamp
        w.Write(0);               // OwnerID
        w.Write(0);               // GroupID
        w.Write((ushort)0);       // OwnerPermissionRWX
        w.Write((ushort)0);       // GroupPermissionRWX
        w.Write((ushort)0);       // GlobalPermissionRWX
        w.Write((ushort)0);       // ServerOperationID
    }

    /// <summary>Read the leading NUL-terminated ASCII string of a payload (the FileListFiles filename).</summary>
    private static string ReadLeadingString(byte[] payload)
    {
        int n = 0;
        while (n < payload.Length && payload[n] != 0) n++;
        return System.Text.Encoding.ASCII.GetString(payload, 0, n);
    }

    /// <summary>
    /// Extract the last NUL-terminated ASCII string in a payload as a 21-byte (MESSAGEID_MAXLEN)
    /// field. FileListFiles puts its messageID ("&lt;sessionKey&gt;.0") at the END of the request,
    /// after the filename and a block of (largely uninitialized) fields, so the normal front-of-
    /// payload MlsMsgId() does not apply.
    /// </summary>
    private static byte[] ExtractTrailingMessageId(byte[] payload)
    {
        var id = new byte[MediusConstants.MESSAGEID_MAXLEN];
        int end = payload.Length;
        while (end > 0 && payload[end - 1] == 0) end--;        // skip trailing NULs
        if (end == 0) return id;
        int start = end;
        while (start > 0 && payload[start - 1] != 0) start--;  // back to start of last string
        int len = Math.Min(end - start, MediusConstants.MESSAGEID_MAXLEN);
        Buffer.BlockCopy(payload, start, id, 0, len);
        return id;
    }

    private async Task HandleGameInfoAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        int    gameId     = r.ReadInt32();

        var game = _store.GetGameById(gameId);

        var w = new MediusWriter();
        if (game == null)
        {
            w.Write((int)MediusCallbackStatus.MediusFail);
        }
        else
        {
            w.Write((int)MediusCallbackStatus.MediusSuccess);
            w.Write(game.GameId);
            w.WriteString(game.GameName, 64);
            w.Write(game.PlayerCount);
            w.Write(game.MaxPlayers);
            w.Write((int)game.Status);
        }

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.GameInfoResponse0, w.ToArray()), ct);
    }

    private async Task HandleChannelInfoAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        int    channelId  = r.ReadInt32();

        var channel = _store.GetChannelById(channelId);

        var w = new MediusWriter();
        if (channel == null)
        {
            w.Write((int)MediusCallbackStatus.MediusFail);
        }
        else
        {
            w.Write((int)MediusCallbackStatus.MediusSuccess);
            w.Write(channel.ChannelId);
            w.WriteString(channel.ChannelName, 64);
            w.Write(channel.PlayerCount);
            w.Write(channel.MaxPlayers);
        }

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.ChannelInfoResponse, w.ToArray()), ct);
    }

    private async Task HandleFindPlayerAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r    = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        string targetName = r.ReadString(64);

        var account = _store.GetAccountByName(targetName);

        var w = new MediusWriter();
        if (account == null)
        {
            w.Write((int)MediusCallbackStatus.MediusAccountNotFound);
            w.Write((byte)1);  // EndOfList
        }
        else
        {
            w.Write((int)MediusCallbackStatus.MediusSuccess);
            w.Write((byte)1);
            w.Write(account.AccountId);
            w.WriteString(account.AccountName, 64);
        }

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.FindPlayerResponse, w.ToArray()), ct);
    }

    private async Task HandleChatMessageAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        int    msgType    = r.ReadInt32();
        string message    = r.ReadString(512);

        _log.LogInformation("[MLS] Chat from {User}: {Msg}", state.AccountName, message);

        // Broadcast to other clients in same channel
        if (state.CurrentChannelId.HasValue)
        {
            var channel = _store.GetChannelById(state.CurrentChannelId.Value);
            if (channel != null)
            {
                foreach (var (connId, (clientState, clientTcp)) in Clients)
                {
                    if (connId  == state.ConnectionId) continue;
                    if (clientState.CurrentChannelId != state.CurrentChannelId) continue;

                    var fwd = new MediusWriter();
                    fwd.Write((int)MediusCallbackStatus.MediusSuccess);
                    fwd.Write(state.AccountId ?? 0);
                    fwd.WriteString(state.AccountName, 64);
                    fwd.Write(msgType);
                    fwd.WriteString(message, 512);

                    await SendMediusAsync(clientTcp, MediusPacket.BuildLobby(MediusLobbyMessageId.ChatFwdMessage, fwd.ToArray()), ct);
                }
            }
        }
    }

    private async Task HandleGenericChatAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // Similar to chat but using the ext message class
        var r       = new MediusReader(packet.Payload);
        string key  = r.ReadString(64);
        string msg  = r.ReadString(512);
        _log.LogInformation("[MLS] GenericChat from {User}: {Msg}", state.AccountName, msg);
        // Forward to channel members
    }

    private async Task HandleGetLocationsAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)1);   // EndOfList
        w.Write(0);         // LocationId
        w.WriteString("Default", 64);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.GetLocationsResponse, w.ToArray()), ct);
    }

    private async Task HandlePickLocationAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.PickLocationResponse, w.ToArray()), ct);
    }

    private Task HandleVersionServerAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        // VersionServer (0x86) is a one-way notification from the PS2 client —
        // the client advances its own state machine immediately without expecting a reply.
        // The real server does NOT send VersionServerResponse here on MLS. Replying to it
        // desyncs the lobby exchange and was the regression that broke the EULA/online screen.
        _log.LogInformation("[MLS] VersionServer received (one-way notification, no reply sent)");
        return Task.CompletedTask;
    }

    private async Task HandleUtilGetServerVersionAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString("CodeDesigner MLS 1.0", 64);

        await SendMediusAsync(tcp, MediusPacket.BuildLobbyExt(MediusLobbyExtMessageId.UtilGetServerVersionResponse, w.ToArray()), ct);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task SendGameListEntry(TcpClient tcp, MediusGame g, bool isLast, MediusLobbyMessageId responseId, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)(isLast ? 1 : 0));
        w.Write(g.GameId);
        w.WriteString(g.GameName, 64);
        w.Write(g.PlayerCount);
        w.Write(g.MaxPlayers);
        w.Write((int)g.Status);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(responseId, w.ToArray()), ct);
    }

    private async Task SendGameListEntryExt(TcpClient tcp, MediusGame g, bool isLast, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)(isLast ? 1 : 0));
        w.Write(g.GameId);
        w.WriteString(g.GameName, 64);
        w.Write(g.PlayerCount);
        w.Write(g.MaxPlayers);
        w.Write((int)g.Status);
        w.Write(g.GenericField1);
        w.Write(g.GenericField2);
        w.Write(g.GenericField3);
        w.Write(g.GenericField4);

        await SendMediusAsync(tcp, MediusPacket.BuildLobbyExt(MediusLobbyExtMessageId.GameList_ExtraInfoResponse, w.ToArray()), ct);
    }
}
