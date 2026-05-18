using DiscordRPC;
using DiscordRPC.Logging;

namespace CodeDesigner.Ps2.DiscordPresence;

public sealed class DiscordPresenceClient : IDisposable
{
    private readonly DiscordRpcClient _client;

    public DiscordPresenceClient(string applicationId)
    {
        _client = new DiscordRpcClient(applicationId)
        {
            Logger = new ConsoleLogger { Level = LogLevel.Warning }
        };
    }

    public bool Initialize() => _client.Initialize();

    public void Update(GameResearchContext context)
    {
        var details = $"Researching: {context.GameName}";
        var state = $"Current: {context.PcAddress}";

        if (!string.IsNullOrWhiteSpace(context.DebugStatus))
        {
            state = $"{state} • {context.DebugStatus}";
        }

        var richPresence = new RichPresence
        {
            Details = details,
            State = state,
            Timestamps = new Timestamps(context.StartedAtUtc.UtcDateTime),
            Assets = new Assets
            {
                LargeImageKey = context.PictureKey ?? "default_game",
                LargeImageText = context.GameName,
                SmallImageKey = "code_designer",
                SmallImageText = context.GameCrc is { Length: > 0 }
                    ? $"CRC: {context.GameCrc}"
                    : "Game research session"
            }
        };

        _client.SetPresence(richPresence);
    }

    /// <summary>
    /// Publishes a generic "idle" presence, shown whenever no game-research
    /// session is active so the user's status still reflects CodeDesigner is open.
    /// </summary>
    public void UpdateIdle()
    {
        _client.SetPresence(new RichPresence
        {
            Details = "Idle",
            State = "No game loaded",
            Assets = new Assets
            {
                LargeImageKey = "code_designer",
                LargeImageText = "CodeDesigner",
            }
        });
    }

    public void Clear() => _client.ClearPresence();

    public void Dispose()
    {
        _client.ClearPresence();
        _client.Dispose();
    }
}
