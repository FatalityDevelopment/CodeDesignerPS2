using System.Net.Sockets;
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

    public Task StartAsync(CancellationToken ct) =>
        StartAsync(_config.BindAddress, _config.MlsPort, ct);

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
        const string msg = "Welcome to CodeDesigner Medius Server!";

        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.Write((byte)1);         // EndOfList
        w.WriteString(msg, 256);  // Announcement text

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
        var channels = _store.GetChannels(state.AppId).ToList();
        if (channels.Count == 0)
            channels.Add(_store.GetOrCreateDefaultChannel(state.AppId));

        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            bool isLast = i == channels.Count - 1;

            var w = new MediusWriter();
            w.Write((int)MediusCallbackStatus.MediusSuccess);
            w.Write((byte)(isLast ? 1 : 0));  // EndOfList
            w.Write(ch.ChannelId);
            w.WriteString(ch.ChannelName, 64);
            w.Write(ch.PlayerCount);
            w.Write(ch.MaxPlayers);

            await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.ChannelListResponse, w.ToArray()), ct);
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

    private async Task HandlePlayerInfoAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var r = new MediusReader(packet.Payload);
        string sessionKey = r.ReadString(64);
        int    accountId  = r.ReadInt32();

        var account = _store.GetAccountById(accountId);

        var w = new MediusWriter();
        if (account == null)
        {
            w.Write((int)MediusCallbackStatus.MediusAccountNotFound);
            w.Write(0);
            w.WriteString("", 64);
        }
        else
        {
            w.Write((int)MediusCallbackStatus.MediusSuccess);
            w.Write(account.AccountId);
            w.WriteString(account.AccountName, 64);
        }

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.PlayerInfoResponse, w.ToArray()), ct);
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

    private async Task HandleVersionServerAsync(MediusPacket packet, MediusClientState state, TcpClient tcp, CancellationToken ct)
    {
        var w = new MediusWriter();
        w.Write((int)MediusCallbackStatus.MediusSuccess);
        w.WriteString("CodeDesigner MLS 1.0", 64);

        await SendMediusAsync(tcp, MediusPacket.BuildLobby(MediusLobbyMessageId.VersionServerResponse, w.ToArray()), ct);
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
