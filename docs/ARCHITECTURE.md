[← Back to Documentation Index](README.md)

# CodeDesigner — Architecture

This document maps how CodeDesigner's pieces fit together at runtime, with a focus
on the **AI / agent layer** (`CodeDesigner.AI`). For a per-project description of
every assembly in the solution, see **[Projects](projects.md)**; this page is about
the *system* — processes, data flow, and the agent/skills stack layered on top.

---

## 1. The big picture

CodeDesigner is a PS2 Emotion Engine toolset whose capabilities are reachable three
ways: a **VS Code extension** (human GUI), a **socket bridge** (machine protocol),
and **MCP servers** (AI agents). They all front the same engine and database.

```
        ┌──────────────────┐     ┌──────────────────────┐     ┌───────────────────┐
        │  VS Code          │     │  AI agent host        │     │  Other MCP client │
        │  extension (GUI)  │     │  (Copilot / Claude /  │     │  (CLI, cloud)     │
        │                   │     │   Grok chat)          │     │                   │
        └────────┬─────────┘     └──────────┬───────────┘     └─────────┬─────────┘
                 │ JSON/TCP                   │ MCP (stdio)               │ MCP (stdio)
                 │                            ▼                           │
                 │            ┌───────────────────────────────┐          │
                 │            │  MCP servers                   │◄─────────┘
                 │            │  • CodeDesigner.Ps2.McpServer  │  (legacy, ~170 flat tools)
                 │            │  • CodeDesigner.AI             │  (profile-driven, this effort)
                 │            └───────┬───────────────┬───────┘
                 │                    │ socket         │ EF Core (direct)
                 ▼                    ▼                ▼
        ┌───────────────────────────────────┐   ┌───────────────────────────┐
        │  CodeDesigner.Ps2.Bridge           │   │  SQL Server LocalDB        │
        │  TCP socket server (port 8765)     │   │  Database=CodeDesigner     │
        │  + PCSX2 PINE client               │   │  (EF: CodeDesigner.Ps2.    │
        └───────┬───────────────────┬───────┘   │   Database)                │
                ▼                   ▼            └───────────────────────────┘
        ┌──────────────┐    ┌──────────────┐
        │ Built-in PS2 │    │  PCSX2        │
        │ emulator     │    │  (PINE)       │
        └──────────────┘    └──────────────┘
```

Two facts drive most of the design:

- **The bridge needs a live emulator; the database does not.** Anything that touches
  PS2 memory, registers, or run state goes through the socket to the Bridge (and on
  to the built-in emulator or PCSX2). Anything that is *coordination* state — labels,
  progress reports, agent personas, orchestration plans, sub-agent sessions, the
  inter-agent message bus — lives in SQL Server LocalDB and is reachable **directly
  via EF Core**, with no emulator running.
- **The AI server is a coordination layer, not a spawner.** `CodeDesigner.AI` never
  launches child processes. Sub-agents are spun up by the *host's* chat-side mechanism
  (e.g. Claude Code's Task tool, Copilot subagents); each child is just another
  `CodeDesigner.AI` process that is handed a profile and an `--agent-id`. They
  coordinate through the shared database (see §4).

---

## 2. The AI layer at a glance

`CodeDesigner.AI` is a stdio MCP server built on the `ModelContextProtocol` C# SDK.
It is deliberately split so the tool surface stays decoupled from the transport:

| Project | Role |
|---|---|
| **CodeDesigner.AI.Abstractions** | Dependency-free contracts + pure logic: `ToolProfile`, `ToolDomain`, `ToolRisk`, `CdText`, `ICdContextProvider`, `IAgentCoordinator`, `IOrchestrationCoordinator` + `WorkflowParser`. No MCP / EF / hosting references. |
| **CodeDesigner.AI** | The MCP host: tool classes (`[CdTool]`), profile gating, session state, the DB-backed coordinators, MCP Resources + Prompts. References Abstractions + `CodeDesigner.Ps2.Database`, and **links** `SocketBridgeClient.cs` + `McpDatabaseService.cs` from the legacy server as the single source of truth for bridge/DB plumbing. |
| **CodeDesigner.AI.Tests** | MSTest unit tests for the pure logic (`WorkflowParser`, `ToolProfile`, `CdText`, `SessionState`). |

### Tool model

Every tool is a class annotated with `[CdTool(name, domain, risk, shortDescription)]`
and a `[ToolEntryPoint]` method. At startup `ToolRegistry.DiscoverAll` reflects over
the assembly, and the active **profile** filters what is exposed:

- **`ToolDomain`** — the feature area: `Core, Memory, Debug, Labels, Network, Patching,
  Cheats, Pcsx2, Pad, Media, Admin`. `Core` is always on.
- **`ToolRisk`** (severity order) — `ReadOnly(0) < MutatesDatabase(1) < WritesDisk(2) <
  WritesMemory(3) < NetworkInjection(4) < Dangerous(5)`. A profile sets a max risk cap.
- **`ToolProfile`** — a named domain set + risk cap (or a curated allow-list). See
  [`ToolProfile.Known`](../CodeDesigner.AI.Abstractions/ToolProfile.cs):
  `core, lite, re, live-debug, packets, patching, cheats, media, pcsx2, multi-agent,
  admin, all`. Launch with `--profile <name>`, `--readonly` (caps at ReadOnly), or
  `--dangerous-tools false`.

The point is **token economy**: a client only ever sees the tools its profile allows
(`lite` is ~18 tools / ~6.5 KB of `tools/list`; `re` is ~116). Profiles can also be
**widened at runtime** with `cd_enable_domain`, which mutates the live tool collection
and fires `tools/list_changed`.

```
args ──▶ ToolProfile.FromArgs ──▶ ToolRegistry.DiscoverAll ──▶ filter by profile
                                                                   │
                          McpToolFactory.Create (DI per tool) ◀────┘
                                   │
                       wraps high-risk tools in RiskFrictionTool
                                   │
                                   ▼
                       McpServerPrimitiveCollection  ──▶  stdio transport
```

### Keep-on-track mechanics

- **`SessionState`** (per-process singleton) tracks goal, labels added, progress
  saves, milestones, and high-risk-op counts. Surfaced via `cd_check_session_health`
  (warnings) and `cd_session_summary` (neutral recap).
- **`RiskFrictionTool`** is a `DelegatingMcpServerTool` wrapper applied to tools above
  `MutatesDatabase`. It adds an optional `reason` parameter and, when a high-risk tool
  runs without one, appends a one-line nudge — friction, not a hard block.
- **Lazy context** — `cd_session_init` stays small; heavy docs (DB schema, agent
  personas, plans, workflows) are pulled on demand via `cd_context_get` and MCP
  Resources (`cd://docs/*`).

---

## 3. Session lifecycle & onboarding

```
cd_session_init(--agent-id N, gameId, resume:true)
   │
   ├─ bridge PING (connected? )
   ├─ resume: list progress reports; if scoped to a game, preview the top match
   ├─ agent persona (EF): surface SessionBegin / ConcurrentTasks onboarding
   └─ core rules + suggested next calls
```

When the process is launched with `--agent-id <n>` (or the caller passes one),
`cd_session_init` reads that persona from `McpAgents` via the `IAgentCoordinator`
and auto-surfaces its **SessionBegin** ("do these now") and **ConcurrentTasks**
("keep doing") Markdown — so a sub-agent is onboarded without having to know its own
id. `cd_session_summary` answers "where are we?" from `SessionState` at any point.

---

## 4. Multi-agent coordination

Because sub-agents are **separate processes sharing one LocalDB**, coordination is
DB-centric (pull), with an optional push over a message bus.

```
        lead session ──cd_session_begin──▶ McpAgentSessions ◀──cd_session_begin── sub-agent
              │                                  │
              ├─ cd_send_message ─────▶ McpAgentMessages ─────▶ cd_inbox_pop
              │
              └─ cd_orch_advance(stage) ─▶ McpOrchestrations.CurrentStage
                                              ▲
                       every agent ──cd_orch_current_step──┘  (shared source of truth)
```

| Concern | Tool(s) | Backing |
|---|---|---|
| Register / end a (sub-)agent session | `cd_session_begin`, `cd_session_end` | `McpAgentSessions` (`IAgentCoordinator`) |
| Inspect the team | `cd_active_sessions`, `cd_session_status` | `McpAgentSessions` |
| Inter-agent messaging | `cd_send_message`, `cd_inbox_pop` | `McpAgentMessages` |
| Orchestration runtime | `cd_orch_current_step`, `cd_orch_advance` | `McpOrchestrations` (`IOrchestrationCoordinator`) |

The **orchestration runtime** parses the active plan's `Workflow` Markdown into ordered
steps (`WorkflowParser`: numbered list → headings → bullets → lines), persists the live
stage on the shared row, and resolves a stage label back to a concrete step (by number,
then exact text, then substring). `cd_orch_advance` can additionally fan the change out
to other active sessions' inboxes. The DB row *is* the broadcast; the message bus is a
convenience on top.

All three coordinators (`DbAgentCoordinator`, `DbOrchestrationCoordinator`,
`DbContextProvider`→`McpDatabaseService`) use **short-lived `CodeDesignerDbContext`
instances per call** and run on the EF-direct path — no socket, no emulator.

---

## 5. Resources, Prompts & Skills

- **MCP Resources** (`CdResources`, `cd://…`) expose static/lookup content (rules, DB
  schema, agent rosters, plans) so it is fetched on demand instead of front-loaded.
- **MCP Prompts** (`CdPrompts`) provide reusable bootstrap/workflow prompts.
- **Agent Skills** (`SKILL.md`) live in [`.github/skills/`](../.github/skills/) and are
  portable across Copilot chat, the CLI, and the cloud agent as `/slash-commands`:
  `cd-session-bootstrap`, `cd-reverse-function`, `cd-packet-flow`, `cd-cheat-port`,
  `cd-cds-patch`, `cd-multi-agent-session`. Skills are the *recipe* layer; they
  sequence the MCP tools above.

---

## 6. Persistence

The AI layer's state is in **SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`,
`Database=CodeDesigner`), modeled by `CodeDesigner.Ps2.Database` (EF Core 8). Relevant
entities: `McpMemories` (progress reports + agent/shared memory), `McpAgents`
(personas), `McpAgentSessions`, `McpAgentMessages`, `McpOrchestrations`. Migrations
live under `CodeDesigner.Ps2.Database/Migrations/` and are applied by
`DatabaseInitialiser` (and by the legacy server at startup). Override the connection
with `--connection-string` or `CODEDESIGNER_CONNECTION_STRING`.

> The legacy Bridge also keeps its own stores for live-session artifacts; the *agent
> coordination* tables described here are the EF/LocalDB set the AI layer reads/writes.

---

## 7. Running it

| Task | Command |
|---|---|
| Build the AI server | `dotnet build CodeDesigner.AI/CodeDesigner.AI.csproj` |
| List a profile's tools | `dotnet …/CodeDesigner.AI.dll --profile lite --list-tools` |
| Run tests | `dotnet test CodeDesigner.AI.Tests/` |
| Register with VS Code | see [`.vscode/mcp.json`](../.vscode/mcp.json) variants |

`.vscode/mcp.json` ships per-task variants (`CodeDesigner-AI-core`,
`-re`, `-multi-agent`, `-readonly`) so a workspace can pick the right profile without
editing args by hand. The build deploys to `%LOCALAPPDATA%\CodeDesigner\AI\` so the AI
server runs side-by-side with the legacy server.

---

## 8. Relationship to the legacy server

`CodeDesigner.Ps2.McpServer` (the original, flat ~170-tool MCP server) and
`CodeDesigner.AI` currently coexist. `CodeDesigner.AI` reuses the legacy server's
`SocketBridgeClient` and `McpDatabaseService` via linked source so there is one
implementation of the bridge/DB plumbing. The intended end state (tracked in the
improvement plan) is to extract that shared infra into a library and retire the legacy
server once `CodeDesigner.AI` passes a documented parity check.

---

*See also: [Projects](projects.md) · [MCP Server and Bridge](mcp-server.md) ·
[Socket Communication](socket-communication.md) · [Labels](labels.md)*
