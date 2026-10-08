# CodeDesigner.Ps2.DiscordBot

Discord bot that posts the AI's CodeDesigner activity — the same events shown
in the VS Code **AI Activity** panel — to a Discord channel, one embed per entry.

## How data flows

1. The CodeDesigner.AI MCP server wraps every tool and sends an `AI_TOOL_CALL`
   to the Bridge (`ActivityLoggingTool`). Other AI commands (memory/navigation,
   label edits, packet captures, `SESSION_START`) reach the Bridge directly.
2. The Bridge relays each event to the AI Activity panel **and** persists it to
   the `AiActivityLogs` table (`Services/AiActivityRecorder`). Rows older than
   7 days are pruned.
3. `DiscordBotWorker` polls `AiActivityLogs` every 3 s through
   `DatabaseAiActivityProvider`, formats each new row with
   `AiActivityMessageFormatter`, and posts it via `DiscordRestClient`.

The Bridge hosts the worker automatically (`Program.StartDiscordBot`), next to
rich presence. It idles until it is configured.

## Configuration

Set either a bot token + channel ID **or** a channel webhook URL. Values live in
the `Settings` table (e.g. via the `set_setting` MCP tool) and can be overridden
by environment variables. They are re-read every poll — no restart needed.

| Settings key            | Environment variable               | Purpose |
|-------------------------|------------------------------------|---------|
| `DiscordBot.Token`      | `CODEDESIGNER_DISCORD_BOT_TOKEN`   | Bot token (Developer Portal → Bot → Reset Token) |
| `DiscordBot.ChannelId`  | `CODEDESIGNER_DISCORD_CHANNEL_ID`  | Channel to post in (Developer Mode → right-click channel → Copy ID) |
| `DiscordBot.WebhookUrl` | `CODEDESIGNER_DISCORD_WEBHOOK_URL` | Alternative to token + channel (Channel → Integrations → Webhooks) |
| `DiscordBot.Kinds`      | —                                  | Optional comma list of kinds to post: `tool`, `activity`, `session`, `label`, `packet` (default: all) |
| `DiscordBot.LastActivityId` | —                              | Cursor written by the bot (last posted row) |

With a bot token, invite the bot to your server with the **View Channel**,
**Send Messages** and **Embed Links** permissions. The bot only uses the REST
API, so no privileged gateway intents are needed. Prefer the environment
variable for the token — the `Settings` table stores it in plain text.

## Context: reasons and AI replies

- **Reason per tool call** — `ActivityLoggingTool` adds a required `reason`
  argument to every MCP tool's schema. The AI states in one line why it is making
  the call; it is logged (in `AiActivityLogs.Detail`), stripped before the tool
  runs, and shown as a quote on the tool's card.
- **AI replies** — the MCP server never sees the model's chat text, so a Claude
  Code `Stop` hook (`.claude/settings.json` → `tools/hooks/send-ai-message.js`)
  reads the turn's final reply from the transcript and sends it to the Bridge as
  `AI_MESSAGE`. It is stored as a `message` row and posted as a "💬 AI response"
  card. The hook only runs for Claude Code sessions opened in this repo.

## Message format

Each activity row becomes one embed (grouped per message within Discord's
limits: 10 embeds, 6000 characters, 10 attachments):

- **Tool calls** — title is a category emoji + tool name, green on success / red
  on failure, with the duration and the AI's stated reason. Short scalar
  arguments are `**Key:** value` lines (address-like keys in `0x…` hex);
  structured arguments are a pretty-printed ```` ```json ```` block, with JSON
  passed as a string expanded inline. The result goes in a code block tagged with
  its language — `json` (pretty-printed, nulls and bridge boilerplate removed),
  `mipsasm` (disassembly), `yaml` (hex dumps), `ini` (register dumps), `diff`
  (errors, in red). Output too long for the card is cut and attached in full as
  a file.
- **AI responses** are posted as Markdown (Discord renders it natively); long
  replies are attached in full as `.md`.
- **Address activity**, **session start**, **label updates** and **packet
  captures** get a one-line description in their own colour.

On first run the bot starts from the newest row (history isn't replayed). If it
falls more than 100 rows behind (e.g. it was offline), older rows are skipped.
