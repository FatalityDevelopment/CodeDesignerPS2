using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Entity Framework Core database context for CodeDesigner.
/// Uses SQLite as the underlying storage engine.
/// </summary>
public class CodeDesignerDbContext : DbContext
{
    // ── DbSets ────────────────────────────────────────────────────────────────

    /// <summary>Application settings, including emulator file paths.</summary>
    public DbSet<Setting> Settings { get; set; } = null!;

    /// <summary>Label sets grouping labels by game and set name.</summary>
    public DbSet<LabelSet> LabelSets { get; set; } = null!;

    /// <summary>Individual label entries within a label set.</summary>
    public DbSet<Label> Labels { get; set; } = null!;

    /// <summary>Struct member definitions belonging to a label.</summary>
    public DbSet<LabelMember> LabelMembers { get; set; } = null!;

    /// <summary>MCP configuration key/value pairs.</summary>
    public DbSet<McpConfig> McpConfigs { get; set; } = null!;

    /// <summary>MCP persistent memories / progress reports.</summary>
    public DbSet<McpMemory> McpMemories { get; set; } = null!;

    /// <summary>MCP agent definitions.</summary>
    public DbSet<McpAgent> McpAgents { get; set; } = null!;

    /// <summary>Session-lifecycle tasks belonging to an MCP agent.</summary>
    public DbSet<McpAgentTask> McpAgentTasks { get; set; } = null!;

    // ── Construction ──────────────────────────────────────────────────────────

    public CodeDesignerDbContext(DbContextOptions<CodeDesignerDbContext> options)
        : base(options)
    {
    }

    // ── Model configuration ───────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Setting ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Setting>(entity =>
        {
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(256);
        });

        // ── LabelSet ───────────────────────────────────────────────────────────
        modelBuilder.Entity<LabelSet>(entity =>
        {
            entity.HasKey(ls => ls.Id);
            entity.Property(ls => ls.SetName).HasMaxLength(256).IsRequired();
            entity.Property(ls => ls.GameId).HasMaxLength(64);
            entity.HasIndex(ls => new { ls.GameId, ls.SetName }).IsUnique();
        });

        // ── Label ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<Label>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Address).HasMaxLength(32).IsRequired();
            entity.Property(l => l.Name).HasMaxLength(512).IsRequired();

            entity.HasOne(l => l.LabelSet)
                  .WithMany(ls => ls.Labels)
                  .HasForeignKey(l => l.LabelSetId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => new { l.LabelSetId, l.Address });
        });

        // ── LabelMember ────────────────────────────────────────────────────────
        modelBuilder.Entity<LabelMember>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Name).HasMaxLength(256).IsRequired();

            entity.HasOne(m => m.Label)
                  .WithMany(l => l.Members)
                  .HasForeignKey(m => m.LabelId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── McpConfig ──────────────────────────────────────────────────────────
        modelBuilder.Entity<McpConfig>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.Key).IsUnique();
            entity.Property(c => c.Key).HasMaxLength(256).IsRequired();
        });

        // ── McpMemory ──────────────────────────────────────────────────────────
        modelBuilder.Entity<McpMemory>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.Name).IsUnique();
            entity.Property(m => m.Name).HasMaxLength(512).IsRequired();
        });

        // ── McpAgent ───────────────────────────────────────────────────────────
        modelBuilder.Entity<McpAgent>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).HasMaxLength(256).IsRequired();
        });

        // ── McpAgentTask ───────────────────────────────────────────────────────
        modelBuilder.Entity<McpAgentTask>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.HasOne(t => t.McpAgent)
                  .WithMany(a => a.Tasks)
                  .HasForeignKey(t => t.McpAgentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => new { t.McpAgentId, t.Phase, t.SortOrder });
        });
    }
}
