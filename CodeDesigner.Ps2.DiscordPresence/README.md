# CodeDesigner.Ps2.DiscordPresence

Discord rich presence support for CodeDesigner game research/debug sessions.

## How data is sent from the database

`DatabaseDiscordPresenceDataProvider` reads the current session context from the `Settings` table, then resolves game metadata from `GameDiscs` + `Games`.

Required `Settings` keys:

- `DiscordPresence.ActiveGameDiscId` (int, FK to `GameDiscs.Id`)
- `DiscordPresence.CurrentPcAddress` (string; shown in presence state)

Optional `Settings` keys:

- `DiscordPresence.DebugStatus` (string)
- `DiscordPresence.PictureKey` (string; Discord asset key)

Flow:

1. Your emulator/debug runtime updates these `Settings` values whenever game/debug state changes.
2. `DiscordPresenceWorker` polls `IDiscordPresenceDataProvider`.
3. `DatabaseDiscordPresenceDataProvider` fetches settings + game title/CRC from DB.
4. `DiscordPresenceClient` publishes the rich presence to Discord.

## What it provides

- A `GameResearchContext` model for game metadata (name, CRC, image key, debug status, and current PC address).
- A provider abstraction (`IDiscordPresenceDataProvider`) to fetch data from the database.
- A `DatabaseDiscordPresenceDataProvider` implementation using `CodeDesignerDbContext`.
- A `DiscordPresenceClient` wrapper around `DiscordRPC`.
- A polling worker (`DiscordPresenceWorker`) that pushes status updates to Discord.

## Integration notes

1. Register `IDbContextFactory<CodeDesignerDbContext>` and `DatabaseDiscordPresenceDataProvider` in DI.
2. Ensure your runtime writes the keys above into `Settings`.
3. Construct `DiscordPresenceClient` with your Discord application ID.
4. Run `DiscordPresenceWorker.RunAsync(...)` with your chosen poll interval.
