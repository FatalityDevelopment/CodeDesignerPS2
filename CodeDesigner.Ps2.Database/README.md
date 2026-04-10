# CodeDesigner.Ps2.Database

SQL Server Express LocalDB-backed persistence layer for CodeDesigner, built on **Entity Framework Core**.  
All data lives in a `CodeDesigner` database on the local SQL Server Express LocalDB instance.

---

## Contents

- [Connection string](#connection-string)
- [Schema overview](#schema-overview)
- [Entity reference](#entity-reference)
- [Migrations](#migrations)
- [DatabaseSeeder](#databaseseeder)
- [Repositories (Bridge layer)](#repositories-bridge-layer)
- [Using the DbContext directly](#using-the-dbcontext-directly)
- [Adding a new entity](#adding-a-new-entity)

---

## Connection string

| Caller | Default connection | Override |
|--------|-------------------|---------|
| `CodeDesigner.Ps2.Database` (CLI) | `Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;` | `--connection-string <cs>` or `CODEDESIGNER_CONNECTION_STRING` env var |
| `CodeDesigner.Ps2.McpServer` | same | `--connection-string <cs>` or `CODEDESIGNER_CONNECTION_STRING` env var |
| `CodeDesigner.Ps2.Bridge` | same | `CODEDESIGNER_CONNECTION_STRING` env var |

SQL Server Express LocalDB is included with Visual Studio and the .NET SDK on Windows.  All processes call `MigrateAsync()` on startup, so the schema is always up to date before any code touches the database.

---

## Schema overview

```
Settings          — key/value application configuration (BiosPath, DiscPath, …)
├─ LabelSets      — named groups of symbol labels per game
│  └─ Labels      — individual address/function/struct labels
│     └─ LabelMembers — struct field definitions
├─ Games          — game metadata (title, description, cover art)
│  ├─ GameDiscs   — disc releases (CRC, serial, IsoPath)
│  │  └─ CheatLists ← also linked to Game
│  │     └─ Cheats
│  │        └─ CheatLines
│  └─ Snapshots   — save-state snapshot metadata
├─ McpConfigs     — MCP tool description key/value pairs (populated by DatabaseSeeder)
├─ McpMemories    — persistent agent memories / progress reports (replaces flat .md files)
└─ McpAgents      — agent identity, rules, guidelines, workflow
   └─ McpAgentTasks — session-lifecycle task definitions per agent
```

---

## Entity reference

### `Setting`
Key/value store for application-level configuration.

| Field | Type | Notes |
|-------|------|-------|
| `Key` | `string` (PK, ≤256) | Human-readable name, e.g. `"BiosPath"`, `"ui.selectedGame"` |
| `Value` | `string?` | The stored value |
| `Description` | `string?` | Optional description of the setting's purpose |
| `UpdatedAt` | `DateTime` | UTC timestamp of last update |

Well-known setting keys used by the VS Code extension and emulator:

| Key | Purpose |
|-----|---------|
| `BiosPath` | Absolute path to the PS2 BIOS ROM |
| `DiscPath` | Absolute path to the currently loaded disc image |
| `ui.selectedGame` | Game title selected in the VS Code sidebar |

---

### `LabelSet` / `Label` / `LabelMember`
Symbol database for a game session.

- **`LabelSet`** — a named collection of labels for a specific game (`GameId` is the PCSX2 CRC or title slug).  
  Unique index on `(GameId, SetName)`.
- **`Label`** — a single annotated address within a set.  Contains address, name, type (`LabelType` enum), comments, and optional call-convention annotations.  
  Cascades delete with its `LabelSet`.
- **`LabelMember`** — struct field definition attached to a `Label` of type `Struct`.  
  Cascades delete with its `Label`.

---

### `McpConfig`
Key/value configuration for the MCP server.  Primarily used to store MCP tool descriptions that are resolved via `[DynamicDescription("key")]` at startup.

| Field | Notes |
|-------|-------|
| `Key` | Unique config key, e.g. `"ping_desc"` |
| `Value` | The stored value (description text, JSON, etc.) |
| `Description` | Optional human-readable note about this config entry |
| `UpdatedAt` | UTC timestamp of last update |

The `DatabaseSeeder` populates all `*_desc` entries from `ToolDescriptions`.  Do **not** delete these rows — the MCP SDK reads them at startup before `builder.Build()` is called.

---

### `McpMemory`
Persistent agent memories — replaces the flat Markdown files that were previously stored in a `memories/` folder.

| Field | Notes |
|-------|-------|
| `Name` | Unique short identifier (slug), e.g. `"god-of-war-health"` |
| `Content` | Full Markdown content of the memory / progress report |
| `CreatedAt` | UTC creation timestamp |
| `UpdatedAt` | UTC last-updated timestamp |

The MCP tools `list_progress_reports`, `get_progress_report`, `save_progress_report`, and `delete_progress_report` all read/write this table.

---

### `McpAgent` / `McpAgentTask`
Agent identity and session-lifecycle configuration.

**`McpAgent`** — defines an agent's persona and operational rules:

| Field | Notes |
|-------|-------|
| `Name` | Display name, e.g. `"[Fatality] Raiden"` |
| `IsAgentLead` | Whether this is the primary/lead agent |
| `Description` | High-level purpose |
| `Rules` | Hard constraints (Markdown) |
| `Guidelines` | Best-practice recommendations (Markdown) |
| `RecommendedWorkflow` | Step-by-step workflow the agent should follow (Markdown) |

**`McpAgentTask`** — a single task within an agent's session lifecycle:

| Field | Notes |
|-------|-------|
| `Phase` | `SessionBegin`, `Continuous`, or `SessionEnd` |
| `SortOrder` | Display order within the phase |
| `Description` | What the task involves |

The `session_start` MCP tool loads the lead agent's rules, guidelines, and tasks from this table when available, falling back to the compiled-in `CheatMasterSystemPrompt`.

---

### `Game` / `GameDisc` / `GameRegion`
Game catalogue.

- **`GameRegion`** — geographic/market region (`"NTSC-U"`, `"PAL"`, `"NTSC-J"`, …).
- **`Game`** — title, description, and optional cover-art bytes (PNG/JPEG).
- **`GameDisc`** — a specific release of a game: CRC, serial, ISO path, and region.

Snapshots and cheat lists point back to `Game`.  Cheat lists can also point to a specific `GameDisc`.

---

### `Snapshot`
Metadata for a save-state snapshot.  The binary snapshot files live on disk; only the metadata is stored in the database.

| Field | Notes |
|-------|-------|
| `Title` | Short display title (≤512 chars) |
| `Description` | Optional notes |
| `SnapshotDateTime` | UTC timestamp when the snapshot was taken |
| `SnapshotPath` | Absolute path to the snapshot directory or file |
| `GameId` | Optional FK to `Game` (null for orphaned snapshots) |

---

### `CheatList` / `Cheat` / `CheatLine`
Cheat code storage.

- **`CheatList`** — named collection of cheats, optionally linked to a `Game` and/or a specific `GameDisc`.
- **`Cheat`** — a named cheat block (label, author, sort order).
- **`CheatLine`** — a single raw code line within a cheat (e.g. `"D0345678 ABCD0001"`).

---

## Migrations

Migrations live in `CodeDesigner.Ps2.Database/Migrations/`.  EF Core tooling manages them.

### Apply pending migrations (manual)

```bash
cd CodeDesigner.Ps2.Database
dotnet ef database update
```

### Create a new migration after changing an entity

```bash
# From the CodeDesigner.Ps2.Database project directory:
dotnet ef migrations add <MigrationName>
```

> The `CodeDesignerDbContextFactory` provides the design-time DbContext pointing at the LocalDB instance, so no separate startup project is required.

### Undo the last migration (before pushing)

```bash
dotnet ef migrations remove
```

### Automatic migration on startup

Both `CodeDesigner.Ps2.Bridge` and `CodeDesigner.Ps2.McpServer` call `db.Database.Migrate()` / `db.Database.MigrateAsync()` at startup, so the schema is always up to date in production without any manual steps.

---

## DatabaseSeeder

`DatabaseSeeder` runs after `MigrateAsync()` and populates rows that must exist for the application to work correctly.  It is **idempotent** — it only inserts rows that don't already exist.

### What it seeds

| Target | Data |
|--------|------|
| `McpConfig` | All MCP tool description entries keyed by `*_desc` (e.g. `"ping_desc"`, `"read_memory_desc"`) |

### Adding new seed data

1. Add a `(key, description)` tuple to `DatabaseSeeder.ToolDescriptions`.
2. Optionally create a new private `Seed*Async()` method and call it from `SeedAsync()`.

```csharp
// DatabaseSeeder.ToolDescriptions (partial):
("my_new_tool_desc",
    "Description text shown in Copilot for the my_new_tool MCP tool."),
```

The description is loaded into `ToolDescriptionStore` by `Program.cs` before the MCP SDK reflects over tool types, so `[DynamicDescription("my_new_tool_desc")]` resolves correctly at registration time.

---

## Repositories (Bridge layer)

The `CodeDesigner.Ps2.Bridge` project wraps raw `DbContext` access with thin async repository classes:

| Repository | Entity | Key operations |
|------------|--------|----------------|
| `DbLabelRepository` | `LabelSet` / `Label` / `LabelMember` | Load/save label sets per game |
| `DbSettingsRepository` | `Setting` | `GetAsync`, `SetAsync`, `GetAllAsync`, `DeleteAsync` |
| `DbSnapshotRepository` | `Snapshot` / `GameDisc` / `Game` | `GetSnapshotsForGameAsync`, `AddSnapshotAsync` |
| `DbMapper` | — | Maps between in-memory `LabelEntry` models and EF entities |

These repositories are **instantiated per socket command handler** — each handler creates a short-lived `CodeDesignerDbContext` and wraps it in the appropriate repository.  There is no singleton DbContext instance in the Bridge; this avoids change-tracker conflicts across concurrent socket connections.

---

## Using the DbContext directly

### In the Bridge (socket server)

```csharp
// SocketServer exposes 'Db' — the current request's DbContext instance:
var repo = new DbSettingsRepository(Db);
string? value = await repo.GetAsync("BiosPath");
```

### In the MCP Server

`Program.cs` registers `DbContextOptions<CodeDesignerDbContext>` as a singleton.  Services that need database access receive the options and create short-lived contexts per operation:

```csharp
// Injected via DI:
private readonly DbContextOptions<CodeDesignerDbContext> _dbOptions;

public async Task DoSomethingAsync()
{
    await using var db = new CodeDesignerDbContext(_dbOptions);
    var memory = await db.McpMemories.FirstOrDefaultAsync(m => m.Name == "my-report");
    // ...
}
```

---

## Adding a new entity

1. **Create the entity class** in `CodeDesigner.Ps2.Database/Entities/MyEntity.cs`.
2. **Add a `DbSet<MyEntity>`** to `CodeDesignerDbContext`.
3. **Configure the model** in `OnModelCreating` (keys, indexes, relationships).
4. **Create a migration**:
   ```bash
   dotnet ef migrations add AddMyEntity
   ```
5. **Add a repository** in `CodeDesigner.Ps2.Bridge` if the socket server needs to access it.
6. **Add MCP tools** in `CodeDesigner.Ps2.McpServer/CodeDesignerPs2Tools.cs` if Copilot needs access.
7. **Add seed data** in `DatabaseSeeder` if initial rows are required.
