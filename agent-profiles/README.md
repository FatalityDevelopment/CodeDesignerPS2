# CodeDesigner.AI — Agent Profiles & Orchestration Plans

These Markdown files describe the multi-agent team **as the system works today**
(the profile-driven `CodeDesigner.AI` MCP server: session lifecycle, tool profiles,
the DB-backed coordination bus, and the orchestration runtime). They are the source
of truth for what you load into the database.

- `agents/` — one file per `McpAgents` row (the [Fatality] team, Mortal-Kombat themed).
- `orchestrations/` — one file per `McpOrchestrations` row (the team plan + a solo default).

## How to apply

The bridge's `MCPAGENT_IMPORT` / `MCPORCHESTRATION_IMPORT` commands (and the `import_orchestration_plan` MCP tool) are **lossy**: they
read only the YAML front-matter and put the whole body into `Description`. So to
get full fidelity, set each column **manually** (SQL, the AI Config webview, or the
`create/update_*` MCP tools). Each `## Section` below maps to exactly one column.

### `McpAgents` column mapping
| Section / front-matter | Column |
|---|---|
| front-matter `name` | `Name` |
| front-matter `status` | `Status` |
| front-matter `isLead` | `IsAgentLead` |
| `## Description` | `Description` |
| `## Objective` | `Objective` |
| `## Rules` | `Rules` |
| `## Guidelines` | `Guidelines` |
| `## Session Begin` | `SessionBegin` |
| `## Concurrent Tasks` | `ConcurrentTasks` |
| `## Session End` | `SessionEnd` |
| `## Recommended Workflow` | `RecommendedWorkflow` |
| `## Research` | `Research` |

### `McpOrchestrations` column mapping
| Section / front-matter | Column |
|---|---|
| front-matter `name` | `Name` |
| front-matter `isActive` | `IsActive` |
| `## Description` | `Description` |
| `## Strategy` | `Strategy` |
| `## Roles` | `Roles` |
| `## Workflow` | `Workflow` |
| `## Rules` | `Rules` |
| `## Notes` | `Notes` |

> **Why the Workflow is a numbered list:** the orchestration runtime
> (`cd_orch_current_step` / `cd_orch_advance`) parses `Workflow` into ordered steps.
> Keep it as a numbered list so stage tracking resolves cleanly (`cd_orch_advance "4"`
> jumps to step 4; a label like `"Verify"` fuzzy-matches the step text).

## Other tables referenced

- **`McpAgentSessions`** — created at runtime by `cd_session_begin` (one per launched
  sub-agent). Not seeded; nothing to enter by hand.
- **`McpAgentMessages`** — the inter-agent inbox, written by `cd_send_message`. Runtime only.
- **`McpOrchestrations.CurrentStage` / `CurrentStageSummary` / `StageUpdatedAt`** —
  set live by `cd_orch_advance`; leave NULL when seeding (= "not started").

## How an agent runs (the model these profiles assume)

1. The lead (Raiden) spins up a teammate via the IDE's chat-side sub-agent mechanism,
   launching `CodeDesigner.AI --agent-id <n> --profile <name>` (it is a *coordination*
   layer — it never forks child processes itself).
2. On launch, `cd_session_init` auto-surfaces that agent's **Session Begin** and
   **Concurrent Tasks** text (because `--agent-id` is set).
3. Agents coordinate through the shared LocalDB: pull the plan with
   `cd_orch_current_step`, message via `cd_send_message` / `cd_inbox_pop`, and the lead
   moves the team forward with `cd_orch_advance`.
4. Each agent gets only the tools its **profile** allows (`re`, `patching`, `cheats`,
   `pcsx2`, `multi-agent`, …) and can widen at runtime with `cd_enable_domain`.

See `docs/ARCHITECTURE.md` (§4 Multi-agent coordination) for the full picture.
