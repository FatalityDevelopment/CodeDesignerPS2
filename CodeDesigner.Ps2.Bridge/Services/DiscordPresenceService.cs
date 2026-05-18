using System.Globalization;

namespace CodeDesigner.Ps2.Bridge.Services;

internal sealed class DiscordPresenceService : ICommandService
{
    private readonly IBridgeContext _ctx;

    private const string ActiveGameDiscIdKey = "DiscordPresence.ActiveGameDiscId";
    private const string CurrentPcAddressKey = "DiscordPresence.CurrentPcAddress";
    private const string DebugStatusKey = "DiscordPresence.DebugStatus";
    private const string PictureKeyKey = "DiscordPresence.PictureKey";

    public DiscordPresenceService(IBridgeContext ctx) => _ctx = ctx;

    public void Register(CommandRegistry reg)
    {
        reg.Add(c => HandleSetDiscordPresence(c.DiscordGameDiscId, c.DiscordPcAddress, c.DiscordDebugStatus, c.DiscordPictureKey), "SETDISCORDPRESENCE");
        reg.Add(_ => HandleClearDiscordPresence(), "CLEARDISCORDPRESENCE");
    }

    private string HandleSetDiscordPresence(int? gameDiscId, string? pcAddress, string? debugStatus, string? pictureKey)
    {
        if (gameDiscId is null || gameDiscId <= 0)
            return Responses.Error("DiscordGameDiscId is required and must be > 0.");

        if (string.IsNullOrWhiteSpace(pcAddress))
            return Responses.Error("DiscordPcAddress is required.");

        try
        {
            _ctx.SettingsRepo.SetAsync(ActiveGameDiscIdKey, gameDiscId.Value.ToString(CultureInfo.InvariantCulture)).GetAwaiter().GetResult();
            _ctx.SettingsRepo.SetAsync(CurrentPcAddressKey, pcAddress.Trim()).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(debugStatus))
                _ctx.SettingsRepo.DeleteAsync(DebugStatusKey).GetAwaiter().GetResult();
            else
                _ctx.SettingsRepo.SetAsync(DebugStatusKey, debugStatus.Trim()).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(pictureKey))
                _ctx.SettingsRepo.DeleteAsync(PictureKeyKey).GetAwaiter().GetResult();
            else
                _ctx.SettingsRepo.SetAsync(PictureKeyKey, pictureKey.Trim()).GetAwaiter().GetResult();

            return Responses.Success("Discord presence settings updated.");
        }
        catch (Exception ex)
        {
            return Responses.Error($"Failed to update Discord presence settings: {ex.Message}");
        }
    }

    private string HandleClearDiscordPresence()
    {
        try
        {
            _ctx.SettingsRepo.DeleteAsync(ActiveGameDiscIdKey).GetAwaiter().GetResult();
            _ctx.SettingsRepo.DeleteAsync(CurrentPcAddressKey).GetAwaiter().GetResult();
            _ctx.SettingsRepo.DeleteAsync(DebugStatusKey).GetAwaiter().GetResult();
            _ctx.SettingsRepo.DeleteAsync(PictureKeyKey).GetAwaiter().GetResult();
            return Responses.Success("Discord presence settings cleared.");
        }
        catch (Exception ex)
        {
            return Responses.Error($"Failed to clear Discord presence settings: {ex.Message}");
        }
    }
}
