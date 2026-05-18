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

## Integration

The Bridge hosts this worker automatically. On start-up (default control-server
mode) it constructs the provider, client, and worker and begins polling every
15 seconds. It uses a built-in default Discord application (client) ID, so rich
presence works out of the box with no configuration.

To point it at a different Discord application, send the Bridge a `SETDISCORDAPPID`
command with the `DiscordApplicationId` field set to your application ID, then
restart the Bridge — the `DiscordPresence.ApplicationId` setting overrides the
default. The application ID is the only credential needed — Discord's *public key*
is only used for verifying inbound interaction webhooks, which this feature does
not use.
