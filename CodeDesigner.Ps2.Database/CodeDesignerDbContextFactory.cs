using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c> and
/// <c>dotnet ef database update</c> when no application host is present.
/// The database file is placed next to the running binary at runtime;
/// for design-time tooling it defaults to a temp path.
/// </summary>
public class CodeDesignerDbContextFactory : IDesignTimeDbContextFactory<CodeDesignerDbContext>
{
    public CodeDesignerDbContext CreateDbContext(string[] args)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "codedesigner_design.db");

        var options = new DbContextOptionsBuilder<CodeDesignerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new CodeDesignerDbContext(options);
    }
}
