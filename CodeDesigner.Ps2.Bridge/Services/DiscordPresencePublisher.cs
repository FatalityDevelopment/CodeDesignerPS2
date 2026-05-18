using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Bridge.Repositories;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.DiscordPresence;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Bridge-side background loop that detects the game running under an attached
    /// PCSX2 instance and writes the Discord-presence session keys into the
    /// database. <see cref="DiscordPresenceWorker"/> reads those keys and publishes
    /// the rich presence, so the user never has to set anything by hand.
    /// <para>
    /// Game metadata (title, serial) is only available over PCSX2 PINE. When no
    /// PCSX2 client is attached, or no game is loaded, the session keys are cleared
    /// and the presence falls back to its idle state.
    /// </para>
    /// </summary>
    internal sealed class DiscordPresencePublisher
    {
        private readonly SocketServer _server;
        private readonly IDbContextFactory<CodeDesignerDbContext> _dbContextFactory;

        // Cache so a steady-state poll does not re-resolve the GameDisc every cycle.
        private string? _resolvedSerial;
        private int _resolvedGameDiscId;

        // Starts true so the first idle poll clears any session keys left behind by
        // a previous Bridge run; thereafter only toggled on running/idle changes.
        private bool _sessionWritten = true;

        public DiscordPresencePublisher(
            SocketServer server,
            IDbContextFactory<CodeDesignerDbContext> dbContextFactory)
        {
            _server = server;
            _dbContextFactory = dbContextFactory;
        }

        public async Task RunAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await PublishOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Discord] Session publish failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(pollInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PublishOnceAsync(CancellationToken cancellationToken)
        {
            string serial = string.Empty;
            string title = string.Empty;
            uint pc = 0;
            bool paused = false;
            bool gameRunning = false;

            var pine = _server.PineClient;
            if (pine is not null)
            {
                try
                {
                    if (pine.GetStatus() != PineClient.EmuStatus.Shutdown)
                    {
                        serial = (pine.GetGameID() ?? string.Empty).Trim();
                        title  = (pine.GetGameTitle() ?? string.Empty).Trim();
                        pc     = pine.GetProgramCounter(out paused);
                        gameRunning = serial.Length > 0;
                    }
                }
                catch
                {
                    // PCSX2 went away mid-poll — treat as no active game.
                    gameRunning = false;
                }
            }

            if (!gameRunning)
            {
                await ClearSessionAsync(cancellationToken);
                return;
            }

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            if (!string.Equals(serial, _resolvedSerial, StringComparison.OrdinalIgnoreCase))
            {
                _resolvedGameDiscId = GameDiscService.UpsertGameDisc(db, serial, title).Id;
                _resolvedSerial = serial;
            }

            var settings = new DbSettingsRepository(db);
            await settings.SetAsync(
                DatabaseDiscordPresenceDataProvider.ActiveGameDiscIdKey,
                _resolvedGameDiscId.ToString(CultureInfo.InvariantCulture));
            await settings.SetAsync(
                DatabaseDiscordPresenceDataProvider.CurrentPcAddressKey,
                $"0x{pc:X8}");
            await settings.SetAsync(
                DatabaseDiscordPresenceDataProvider.DebugStatusKey,
                paused ? "Paused" : "Running");

            _sessionWritten = true;
        }

        private async Task ClearSessionAsync(CancellationToken cancellationToken)
        {
            if (!_sessionWritten)
                return;

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var settings = new DbSettingsRepository(db);
            await settings.DeleteAsync(DatabaseDiscordPresenceDataProvider.ActiveGameDiscIdKey);
            await settings.DeleteAsync(DatabaseDiscordPresenceDataProvider.CurrentPcAddressKey);
            await settings.DeleteAsync(DatabaseDiscordPresenceDataProvider.DebugStatusKey);

            _resolvedSerial = null;
            _resolvedGameDiscId = 0;
            _sessionWritten = false;
        }
    }
}
