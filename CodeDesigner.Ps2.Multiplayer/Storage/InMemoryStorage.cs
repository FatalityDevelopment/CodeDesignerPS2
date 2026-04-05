using System.Collections.Concurrent;
using CodeDesigner.Ps2.Multiplayer.Models;

namespace CodeDesigner.Ps2.Multiplayer.Storage;

/// <summary>
/// Simple thread-safe in-memory store for accounts, channels, and games.
/// No persistence — data is lost when the server restarts.
/// For development and LAN play. Replace with a database backend for production.
/// </summary>
public class InMemoryStorage
{
    // -------------------------------------------------------------------------
    // Accounts
    // -------------------------------------------------------------------------
    private readonly ConcurrentDictionary<int,    MediusAccount> _accountsById   = new();
    private readonly ConcurrentDictionary<string, MediusAccount> _accountsByName = new(StringComparer.OrdinalIgnoreCase);
    private int _nextAccountId = 1;

    public MediusAccount? GetAccountById(int id) =>
        _accountsById.TryGetValue(id, out var a) ? a : null;

    public MediusAccount? GetAccountByName(string name) =>
        _accountsByName.TryGetValue(name, out var a) ? a : null;

    public MediusAccount CreateAccount(string name, string passwordHash)
    {
        var account = new MediusAccount
        {
            AccountId    = Interlocked.Increment(ref _nextAccountId),
            AccountName  = name,
            PasswordHash = passwordHash,
        };
        _accountsById[account.AccountId]    = account;
        _accountsByName[account.AccountName] = account;
        return account;
    }

    // -------------------------------------------------------------------------
    // Channels
    // -------------------------------------------------------------------------
    private readonly ConcurrentDictionary<int, MediusChannel> _channelsById = new();
    private int _nextChannelId = 1;

    public IEnumerable<MediusChannel> GetChannels(int appId) =>
        _channelsById.Values.Where(c => c.AppId == appId);

    public MediusChannel? GetChannelById(int id) =>
        _channelsById.TryGetValue(id, out var c) ? c : null;

    public MediusChannel GetOrCreateDefaultChannel(int appId)
    {
        var existing = _channelsById.Values.FirstOrDefault(c => c.AppId == appId);
        if (existing != null) return existing;

        var channel = new MediusChannel
        {
            ChannelId   = Interlocked.Increment(ref _nextChannelId),
            ChannelName = "Lobby",
            MaxPlayers  = 256,
            AppId       = appId,
        };
        _channelsById[channel.ChannelId] = channel;
        return channel;
    }

    public MediusChannel CreateChannel(string name, int appId, int maxPlayers = 256)
    {
        var channel = new MediusChannel
        {
            ChannelId   = Interlocked.Increment(ref _nextChannelId),
            ChannelName = name,
            MaxPlayers  = maxPlayers,
            AppId       = appId,
        };
        _channelsById[channel.ChannelId] = channel;
        return channel;
    }

    // -------------------------------------------------------------------------
    // Games
    // -------------------------------------------------------------------------
    private readonly ConcurrentDictionary<int, MediusGame> _gamesById = new();
    private int _nextGameId = 1;

    public IEnumerable<MediusGame> GetGames(int appId) =>
        _gamesById.Values.Where(g => g.AppId == appId && g.Status != GameWorldStatus.WorldInactive);

    public MediusGame? GetGameById(int id) =>
        _gamesById.TryGetValue(id, out var g) ? g : null;

    public MediusGame CreateGame(string name, int appId, int hostAccountId, string hostName, int maxPlayers)
    {
        var game = new MediusGame
        {
            GameId        = Interlocked.Increment(ref _nextGameId),
            GameName      = name,
            AppId         = appId,
            HostAccountId = hostAccountId,
            HostName      = hostName,
            MaxPlayers    = maxPlayers,
            Status        = GameWorldStatus.WorldStaging,
        };
        _gamesById[game.GameId] = game;
        return game;
    }

    public bool RemoveGame(int gameId) => _gamesById.TryRemove(gameId, out _);
}
