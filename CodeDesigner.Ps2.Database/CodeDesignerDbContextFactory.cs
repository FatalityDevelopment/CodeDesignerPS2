using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c> and
/// <c>dotnet ef database update</c> when no application host is present.
/// Uses a SQL Server Express LocalDB instance for design-time tooling.
/// </summary>
public class CodeDesignerDbContextFactory : IDesignTimeDbContextFactory<CodeDesignerDbContext>
{
    public CodeDesignerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CodeDesignerDbContext>()
            .UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;")
            .Options;

        return new CodeDesignerDbContext(options);
    }
}
