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

    /// <summary>Individual label entries within a label set (recursive: children have ParentId set).</summary>
    public DbSet<Label> Labels { get; set; } = null!;

    /// <summary>MCP configuration key/value pairs.</summary>
    public DbSet<McpConfig> McpConfigs { get; set; } = null!;

    /// <summary>MCP persistent memories / progress reports.</summary>
    public DbSet<McpMemory> McpMemories { get; set; } = null!;

    /// <summary>MCP agent definitions.</summary>
    public DbSet<McpAgent> McpAgents { get; set; } = null!;

    /// <summary>Session-lifecycle tasks belonging to an MCP agent.</summary>
    public DbSet<McpAgentTask> McpAgentTasks { get; set; } = null!;

    /// <summary>Game metadata (title, description, cover art).</summary>
    public DbSet<Game> Games { get; set; } = null!;

    /// <summary>Geographic/market regions, e.g. "NTSC-U", "PAL".</summary>
    public DbSet<GameRegion> GameRegions { get; set; } = null!;

    /// <summary>Specific disc releases of a game (one per region/version).</summary>
    public DbSet<GameDisc> GameDiscs { get; set; } = null!;

    /// <summary>Save-state snapshot metadata for a game session.</summary>
    public DbSet<Snapshot> Snapshots { get; set; } = null!;

    /// <summary>Named collections of cheats associated with a game or disc.</summary>
    public DbSet<CheatList> CheatLists { get; set; } = null!;

    /// <summary>Individual cheat entries (CheatBlock) within a cheat list.</summary>
    public DbSet<Cheat> Cheats { get; set; } = null!;

    /// <summary>Individual raw code lines within a cheat.</summary>
    public DbSet<CheatLine> CheatLines { get; set; } = null!;

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
            entity.Property(l => l.Address).HasMaxLength(64).IsRequired();
            entity.Property(l => l.Name).HasMaxLength(512).IsRequired();

            // Owning label set.
            entity.HasOne(l => l.LabelSet)
                  .WithMany(ls => ls.Labels)
                  .HasForeignKey(l => l.LabelSetId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Self-referencing parent / child relationship.
            entity.HasOne(l => l.Parent)
                  .WithMany(l => l.Children)
                  .HasForeignKey(l => l.ParentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => new { l.LabelSetId, l.Address });
        });

        // ── LabelMember (legacy table kept only for migration; DO NOT USE) ─────
        // The LabelMember table was replaced by the self-referencing Label.ParentId
        // relationship in migration AddRecursiveLabels.

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

        // ── GameRegion ─────────────────────────────────────────────────────────
        modelBuilder.Entity<GameRegion>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).HasMaxLength(64).IsRequired();
            entity.HasIndex(r => r.Name).IsUnique();
        });

        // ── Game ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Title).HasMaxLength(512).IsRequired();
            entity.HasIndex(g => g.Title);
        });

        // ── GameDisc ───────────────────────────────────────────────────────────
        modelBuilder.Entity<GameDisc>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Serial).HasMaxLength(32);
            entity.Property(d => d.CRC).HasMaxLength(16);

            entity.HasOne(d => d.Game)
                  .WithMany(g => g.Discs)
                  .HasForeignKey(d => d.GameId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.GameRegion)
                  .WithMany(r => r.Discs)
                  .HasForeignKey(d => d.GameRegionId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(d => d.CRC);
            entity.HasIndex(d => d.Serial);
            entity.HasIndex(d => new { d.GameId, d.GameRegionId });
        });

        // ── Snapshot ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Snapshot>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Title).HasMaxLength(512).IsRequired();
            entity.Property(s => s.SnapshotPath).HasMaxLength(1024).IsRequired();

            entity.HasOne(s => s.Game)
                  .WithMany(g => g.Snapshots)
                  .HasForeignKey(s => s.GameId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ── CheatList ──────────────────────────────────────────────────────────
        modelBuilder.Entity<CheatList>(entity =>
        {
            entity.HasKey(cl => cl.Id);
            entity.Property(cl => cl.Title).HasMaxLength(512).IsRequired();

            entity.HasOne(cl => cl.Game)
                  .WithMany(g => g.CheatLists)
                  .HasForeignKey(cl => cl.GameId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(cl => cl.GameDisc)
                  .WithMany(d => d.CheatLists)
                  .HasForeignKey(cl => cl.GameDiscId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(cl => cl.GameDiscId);
        });

        // ── Cheat ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<Cheat>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Label).HasMaxLength(512).IsRequired();
            entity.Property(c => c.Author).HasMaxLength(256);

            entity.HasOne(c => c.CheatList)
                  .WithMany(cl => cl.Cheats)
                  .HasForeignKey(c => c.CheatListId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => new { c.CheatListId, c.SortOrder });
        });

        // ── CheatLine ──────────────────────────────────────────────────────────
        modelBuilder.Entity<CheatLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Code).HasMaxLength(32).IsRequired();

            entity.HasOne(l => l.Cheat)
                  .WithMany(c => c.CheatLines)
                  .HasForeignKey(l => l.CheatId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => new { l.CheatId, l.SortOrder });
        });
    }
}
