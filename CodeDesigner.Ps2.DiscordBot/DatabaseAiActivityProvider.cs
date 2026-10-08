using System.Globalization;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Database-backed <see cref="IAiActivityProvider"/>. Activity comes from the
/// <c>AiActivityLogs</c> table (written by the Bridge); configuration and the
/// posting cursor live in the <c>Settings</c> table.
/// </summary>
/// <remarks>
/// Settings keys (each can be overridden by the matching environment variable):
/// - DiscordBot.Token      / CODEDESIGNER_DISCORD_BOT_TOKEN
/// - DiscordBot.ChannelId  / CODEDESIGNER_DISCORD_CHANNEL_ID
/// - DiscordBot.WebhookUrl / CODEDESIGNER_DISCORD_WEBHOOK_URL
/// - DiscordBot.Kinds      (optional, comma-separated, e.g. "tool,session")
/// - DiscordBot.LastActivityId (written by the bot)
/// </remarks>
public sealed class DatabaseAiActivityProvider : IAiActivityProvider
{
    public const string TokenKey = "DiscordBot.Token";
    public const string ChannelIdKey = "DiscordBot.ChannelId";
    public const string WebhookUrlKey = "DiscordBot.WebhookUrl";
    public const string KindsKey = "DiscordBot.Kinds";
    public const string LastActivityIdKey = "DiscordBot.LastActivityId";

    private readonly IDbContextFactory<CodeDesignerDbContext> _dbContextFactory;

    public DatabaseAiActivityProvider(IDbContextFactory<CodeDesignerDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<DiscordBotOptions> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var settings = await db.Settings
            .AsNoTracking()
            .Where(s => s.Key == TokenKey
                     || s.Key == ChannelIdKey
                     || s.Key == WebhookUrlKey
                     || s.Key == KindsKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        string? Resolve(string key, string envVar)
        {
            var fromEnv = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(fromEnv)) { return fromEnv.Trim(); }
            return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        settings.TryGetValue(KindsKey, out var kinds);

        return new DiscordBotOptions
        {
            Token = Resolve(TokenKey, "CODEDESIGNER_DISCORD_BOT_TOKEN"),
            ChannelId = Resolve(ChannelIdKey, "CODEDESIGNER_DISCORD_CHANNEL_ID"),
            WebhookUrl = Resolve(WebhookUrlKey, "CODEDESIGNER_DISCORD_WEBHOOK_URL"),
            Kinds = (kinds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => k.ToLowerInvariant())
                .ToHashSet(),
        };
    }

    public async Task<long> GetLatestIdAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AiActivityLogs.MaxAsync(a => (long?)a.Id, cancellationToken) ?? 0;
    }

    public async Task<IReadOnlyList<AiActivityLog>> GetActivityAfterAsync(long afterId, int take, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AiActivityLogs
            .AsNoTracking()
            .Where(a => a.Id > afterId)
            .OrderBy(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<long?> GetLastPostedIdAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var value = await db.Settings
            .AsNoTracking()
            .Where(s => s.Key == LastActivityIdKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    public async Task SetLastPostedIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == LastActivityIdKey, cancellationToken);
        if (setting is null)
        {
            setting = new Setting
            {
                Key = LastActivityIdKey,
                Description = "Id of the last AI activity row the Discord bot posted.",
            };
            db.Settings.Add(setting);
        }

        setting.Value = id.ToString(CultureInfo.InvariantCulture);
        setting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
