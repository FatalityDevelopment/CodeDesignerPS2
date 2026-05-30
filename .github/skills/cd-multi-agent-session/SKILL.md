---
name: cd-multi-agent-session
description: Lead role for a multi-agent reverse-engineering session — register your own session, spawn sub-agents (via your IDE's subagent mechanism) with a CodeDesigner agent persona, exchange messages, monitor progress, and close out. Use whenever you decompose a reversing job across multiple workers.
argument-hint: [orchestration-plan-id]
---

# Lead a multi-agent CodeDesigner session

CodeDesigner.AI exposes a coordination layer (sessions + inbox-style messages)
on top of the `McpAgent` profiles you've already authored. The lead AI here
is **you**. Sub-agents are spawned by your IDE's chat-side subagent mechanism
(Claude Code's Task tool, Copilot's agents, etc.) — they read their persona
from the DB via `get_agent_profile`, register a session via `cd_session_begin`,
and coordinate with you through `cd_send_message` / `cd_inbox_pop`.

## Required state

- `list_agent_profiles` returns at least two profiles (lead + ≥1 sub).
  If not, ask the user to author them via `create_agent_profile` first or
  load an orchestration plan: `get_active_orchestration_plan` /
  `set_active_orchestration_plan <id>`.
- Optionally, an orchestration plan id passed as the skill argument.

## Steps

1. **Register your own lead session** so sub-agents can address you back:
   `cd_session_begin agentId=<your-lead-id> task="<one-line goal>"`. Store
   the returned session id; you'll pass it as `fromSessionId` everywhere.

2. **Decompose the work**. Read the orchestration plan if one is active:
   `get_active_orchestration_plan` — its **Workflow** and **Roles** fields
   describe how to split tasks across personas. Otherwise: improvise one task
   per sub-agent.

3. **Spawn each sub-agent** via your IDE's subagent mechanism (this is
   IDE-side, not an MCP call). Pass the sub-agent these initial instructions:
   - call `get_agent_profile <agentId>` to load their persona
   - call `cd_session_begin agentId=<agentId> parentSessionId=<your-session-id>
     task="<their-slice>" externalId="<task-handle>"`
   - then call `cd_inbox_pop sessionId=<their-session-id>` for any pending
     messages before starting work
   - report milestones back to you via `cd_send_message
     fromSessionId=<their-session-id> toSessionId=<your-session-id>
     body="<update>"`
   - call `cd_session_end sessionId=<their-session-id> summary="<result>"`
     when done

4. **Monitor**: every minute or two, call `cd_active_sessions` to see who's
   live, and `cd_inbox_pop sessionId=<your-session-id>` to drain reports.
   For richer detail on a worker: `cd_session_status <id>`.

5. **Steer**: when a sub needs redirection, `cd_send_message
   fromSessionId=<lead> toSessionId=<sub> body="<instruction>"`. For
   broadcast guidance, omit both `toSessionId` and `toAgentId`.

6. **Synthesise** results once subs finish. Combine their notes (the `notes`
   field on each closed session via `cd_session_status`) into a single
   `progress_save`.

7. **Close out**: `cd_session_end sessionId=<your-lead-session-id>
   summary="<roll-up>" status="completed"`. Surface the agent's `SessionEnd`
   Markdown to confirm any pending obligations.

## Tips

- Every tool call's calls/labels are tracked by `SessionState`. Use
  `cd_check_session_health` at the end to confirm there are no nag flags
  before closing.
- A sub-agent that crashes (its IDE-side chat dies) leaves an `active`
  session row. The lead can `cd_session_end status=failed summary="lost
  contact"` to reap it.
- For broadcast-style instructions to a whole *persona* (e.g. "every memory
  worker pause"), use `cd_send_message toAgentId=<persona-id>` — every
  active session running that agent picks the message up on its next inbox
  pop.
