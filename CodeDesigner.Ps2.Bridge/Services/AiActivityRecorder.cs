using System;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Persists AI activity events (the ones the Bridge relays to the VS Code
    /// "AI Activity" panel) into the <c>AiActivityLogs</c> table so other
    /// consumers — e.g. the Discord bot — can poll them.
    ///
    /// Writes go through a single-reader queue on a background task: inserts
    /// stay in arrival order, never block command handling, and use their own
    /// short-lived contexts rather than the Bridge's shared, non-thread-safe one.
    /// Persistence is best-effort — a failed insert is logged and dropped.
    /// </summary>
    internal sealed class AiActivityRecorder
    {
        /// <summary>Rows older than this are pruned.</summary>
        private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        /// <summary>Prune once every this many inserts.</summary>
        private const int PruneEvery = 500;

        private readonly IDbContextFactory<CodeDesignerDbContext> _dbContextFactory;
        private readonly Channel<AiActivityLog> _queue =
            Channel.CreateUnbounded<AiActivityLog>(new UnboundedChannelOptions { SingleReader = true });
        private int _insertsSincePrune = PruneEvery; // prune on the first write

        public AiActivityRecorder(IDbContextFactory<CodeDesignerDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
            _ = Task.Run(DrainAsync);
        }

        public void Record(AiActivityLog entry) => _queue.Writer.TryWrite(entry);

        private async Task DrainAsync()
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    await using var db = await _dbContextFactory.CreateDbContextAsync();
                    db.AiActivityLogs.Add(entry);

                    // Opportunistically batch anything else already queued.
                    while (_queue.Reader.TryRead(out var more)) { db.AiActivityLogs.Add(more); }

                    int added = await db.SaveChangesAsync();

                    _insertsSincePrune += added;
                    if (_insertsSincePrune >= PruneEvery)
                    {
                        _insertsSincePrune = 0;
                        var cutoff = DateTime.UtcNow - Retention;
                        await db.AiActivityLogs.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AiActivity] Failed to persist activity: {ex.Message}");
                }
            }
        }
    }
}
