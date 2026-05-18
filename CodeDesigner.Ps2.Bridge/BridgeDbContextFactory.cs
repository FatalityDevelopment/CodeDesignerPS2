using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Minimal <see cref="IDbContextFactory{TContext}"/> that hands out fresh
    /// <see cref="CodeDesignerDbContext"/> instances from a fixed set of options.
    /// Lets background workers (e.g. Discord rich presence) own short-lived
    /// contexts instead of sharing the Bridge's single, non-thread-safe context.
    /// </summary>
    internal sealed class BridgeDbContextFactory : IDbContextFactory<CodeDesignerDbContext>
    {
        private readonly DbContextOptions<CodeDesignerDbContext> _options;

        public BridgeDbContextFactory(DbContextOptions<CodeDesignerDbContext> options)
            => _options = options;

        public CodeDesignerDbContext CreateDbContext() => new(_options);
    }
}
