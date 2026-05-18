namespace CodeDesigner.Ps2.DiscordPresence;

public sealed record GameResearchContext(
    string GameName,
    string? GameCrc,
    string? PictureKey,
    string PcAddress,
    string? DebugStatus,
    DateTimeOffset StartedAtUtc)
{
    public static GameResearchContext Create(
        string gameName,
        string pcAddress,
        string? gameCrc = null,
        string? pictureKey = null,
        string? debugStatus = null)
    {
        return new GameResearchContext(
            gameName,
            gameCrc,
            pictureKey,
            pcAddress,
            debugStatus,
            DateTimeOffset.UtcNow);
    }
}
