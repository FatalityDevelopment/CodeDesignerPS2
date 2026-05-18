using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.DiscordPresence;

/// <summary>
/// Default database-backed provider for Discord presence context.
/// </summary>
/// <remarks>
/// This provider expects the active runtime session to write the following keys into the Settings table:
/// - DiscordPresence.ActiveGameDiscId (int)
/// - DiscordPresence.CurrentPcAddress (string)
/// - DiscordPresence.DebugStatus (string, optional)
/// - DiscordPresence.PictureKey (string, optional)
/// </remarks>
public sealed class DatabaseDiscordPresenceDataProvider : IDiscordPresenceDataProvider
{
    public const string ActiveGameDiscIdKey = "DiscordPresence.ActiveGameDiscId";
    public const string CurrentPcAddressKey = "DiscordPresence.CurrentPcAddress";
    public const string DebugStatusKey = "DiscordPresence.DebugStatus";
    public const string PictureKeyKey = "DiscordPresence.PictureKey";

    private readonly IDbContextFactory<CodeDesignerDbContext> _dbContextFactory;

    public DatabaseDiscordPresenceDataProvider(IDbContextFactory<CodeDesignerDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<GameResearchContext?> GetCurrentContextAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var settings = await db.Settings
            .Where(s => s.Key == ActiveGameDiscIdKey
                     || s.Key == CurrentPcAddressKey
                     || s.Key == DebugStatusKey
                     || s.Key == PictureKeyKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        if (!settings.TryGetValue(ActiveGameDiscIdKey, out var gameDiscIdText)
            || !int.TryParse(gameDiscIdText, out var gameDiscId))
        {
            return null;
        }

        if (!settings.TryGetValue(CurrentPcAddressKey, out var pcAddress)
            || string.IsNullOrWhiteSpace(pcAddress))
        {
            return null;
        }

        var disc = await db.GameDiscs
            .AsNoTracking()
            .Include(d => d.Game)
            .Where(d => d.Id == gameDiscId)
            .Select(d => new
            {
                d.CRC,
                GameTitle = d.Game.Title
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (disc is null)
        {
            return null;
        }

        settings.TryGetValue(DebugStatusKey, out var debugStatus);
        settings.TryGetValue(PictureKeyKey, out var pictureKey);

        return GameResearchContext.Create(
            gameName: disc.GameTitle,
            pcAddress: pcAddress,
            gameCrc: disc.CRC,
            pictureKey: string.IsNullOrWhiteSpace(pictureKey) ? null : pictureKey,
            debugStatus: string.IsNullOrWhiteSpace(debugStatus) ? null : debugStatus);
    }
}
