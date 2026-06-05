# AI Chat — Response Templates

Design spec for the multi-agent chat surface (Copilot-Chat-style) shown in the mockups.
This document defines the **wire format** the AI must emit and the **render blocks** the
webview is responsible for drawing. The renderer is dumb: it only knows the block types
listed below. Every visible piece of an agent reply must fit into one of these blocks.

> Source of truth: any new visual element requires (a) a new block `kind` here and
> (b) a renderer for it. The model never emits raw HTML.

---

## 1. Chat shell layout

The chat panel is a vertical stack of fixed regions:

```
┌─────────────────────────────────────────────────────────────┐
│  Title bar    "CodeDesigner AI" · team badge · + ↻ ⚙        │  ← header
├─────────────────────────────────────────────────────────────┤
│  TEAM   [Fatality]  ● ● ● ● ● ● ○ ○                6/8 dep. │  ← team strip
├─────────────────────────────────────────────────────────────┤
│  CONTEXT  ⊙ SOCOM·D7CFDCCF  ⊙ PC 0x00204A2C  …  + Add       │  ← context chips
│                                                  Squad: ⌄   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ───────── Today · 11:42 ─────────                          │  ← date divider
│                                                             │
│  [ messages — see §3 ]                                      │  ← scroll region
│                                                             │
├─────────────────────────────────────────────────────────────┤
│  [ suggested follow-up chips ]                              │  ← §6
├─────────────────────────────────────────────────────────────┤
│  [ composer: textarea · attach · squad · agent · send ]     │  ← composer
├─────────────────────────────────────────────────────────────┤
│  footer hint (capabilities / safety reminder)               │
└─────────────────────────────────────────────────────────────┘
```

All regions read from the same session state. The renderer never reaches into model
internals; it consumes only the JSON envelopes defined below.

---

## 2. Message envelope

Every chat entry — user or agent — is one `Message`:

```ts
interface Message {
  id: string;
  kind: 'user' | 'agent' | 'system';
  agent?: AgentRef;          // required when kind === 'agent'
  role?: string;             // role badge text, e.g. "ORCHESTRATOR", "MEMORY SCANNER"
  createdAt: string;         // ISO timestamp
  blocks: Block[];           // ordered, renders top-to-bottom
  meta?: {
    tokens?: number;         // shown top-right ("320 tok")
    latencyMs?: number;      // shown top-right ("0.8s")
    status?: 'streaming' | 'done' | 'error';
  };
  actions?: MessageAction[]; // bottom row: Copy / Redo / 👍 / 👎
}

interface AgentRef {
  id: string;                // "raiden", "sub-zero", ...
  display: string;           // "[Fatality] Raiden"
  avatarUri?: string;
  accent?: string;           // CSS var or theme key for the colored frame
}

type MessageAction = 'copy' | 'redo' | 'thumbs-up' | 'thumbs-down' | 'insert';
```

**Header row (agent messages):** `avatar · agent.display · role badge · ─── · tokens · latency`.
**Footer row:** `actions[]` rendered as ghost buttons.

User messages skip the header/footer chrome and render as a right-aligned bubble holding
a single `text` block.

---

## 3. Block types

The model emits a `blocks` array. Each block has a discriminating `kind`. **Add no fields
the renderer doesn't understand** — unknown blocks are dropped with a console warning.

### 3.1 `text`

Plain prose or short markdown. Inline `code`, **bold**, and `[[label]]` chips allowed.
No headings, no tables, no fenced code — those have dedicated blocks.

```json
{ "kind": "text", "md": "Got it. Decomposing into 5 subtasks and dispatching to the team." }
```

Inline label chip: write `[[g_player_health @ 0x0035C100]]` and the renderer turns it
into a pill (used in "Confirmed: …" lines).

### 3.2 `dispatch` — orchestrator subtask list

The colored card the orchestrator emits to show fan-out work. One row per dispatched
sub-agent. Status drives the right-side pill.

```json
{
  "kind": "dispatch",
  "title": "Dispatch · 5 subagent calls",
  "rows": [
    { "n": 1, "agent": { "id": "sub-zero",  "display": "Sub-Zero" }, "task": "Scan EE RAM for HP candidates",    "status": "done" },
    { "n": 2, "agent": { "id": "scorpion",  "display": "Scorpion" }, "task": "Trace writers to confirmed address", "status": "done" },
    { "n": 3, "agent": { "id": "kitana",    "display": "Kitana"   }, "task": "Map CZPlayer struct around HP offset", "status": "done" },
    { "n": 4, "agent": { "id": "kano",      "display": "Kano"     }, "task": "Author CDS patch with HP < 50 trap", "status": "done" },
    { "n": 5, "agent": { "id": "sonya",     "display": "Sonya"    }, "task": "Verify trap fires under controlled step", "status": "running" }
  ]
}
```

`status` ∈ `queued | running | done | error | skipped`.

### 3.3 `tool-call` — generic tool invocation card

The universal envelope for every tool the squad uses (memory scan, call-graph,
disassembly range, struct mapping, compiler, verifier, …). Collapsed by default;
expands to show `body`.

```json
{
  "kind": "tool-call",
  "tool": "mem.scan",
  "args": { "type": "i32", "range": "0x00350000-0x00380000", "value": "changed" },
  "status": "done",
  "summary": "1 result · 3 passes",
  "durationMs": 1400,
  "body": { "kind": "table", "...": "see §3.4" }
}
```

Header line, left → right:

| segment    | source                                                    |
|------------|-----------------------------------------------------------|
| status dot | `status` → ✓ done, ⟳ running, ✕ error                     |
| tool icon  | mapped from `tool` namespace prefix (`mem.`, `disasm.`, …) |
| tool sig   | `${tool} (${args formatted as k=v list})`                  |
| summary    | `summary`                                                  |
| duration   | `durationMs` formatted (`1.4s`)                            |
| chevron    | toggles `body`                                             |

`body` is itself a block — typically `table`, `disasm`, `struct-map`, `kv`, or `text`.

**Tools that have appeared so far** (extend by adding a new `tool` namespace, not a new block kind):

- `mem.scan` — body: `table` of address/value rows + a confirmation pill
- `callgraph.writers` — body: `disasm` with `WRITE` / `FN` / `CALLER` annotations per row
- `disasm.range` — body: `disasm`
- `struct.map` — body: `struct-map`
- `cds.compile` — body: `kv` (bytes, instructions) ± `text`
- `runtime.verify` — body: `timeline`, streamed row-by-row (see §3.9)

### 3.4 `table`

Generic tabular result. Used inside `tool-call.body`.

```json
{
  "kind": "table",
  "columns": [
    { "key": "n",    "label": "" },
    { "key": "addr", "label": "" },
    { "key": "trail","label": "" },
    { "key": "type", "label": "", "align": "right" },
    { "key": "tag",  "label": "", "align": "right" }
  ],
  "rows": [
    { "n": 1, "addr": "0x0035C100", "trail": "= 100 → 78 → 56", "type": "i32", "tag": { "kind": "pill", "text": "CONFIRMED", "tone": "ok" } }
  ]
}
```

Cell values: string | number | `{ kind: 'pill', text, tone }` where
`tone` ∈ `ok | warn | danger | info | muted`.

### 3.5 `disasm` — MIPS / EE disassembly listing

```json
{
  "kind": "disasm",
  "header": "Player::UpdateHealth(int newHealth)",
  "lines": [
    { "addr": "00204A30", "op": "addiu", "ops": "$sp, $sp, -0x20" },
    { "addr": "00204A44", "op": "sw",    "ops": "$a0, 0x00($s0)", "note": "← hook point", "highlight": true }
  ]
}
```

Optional per-row `tag` (`WRITE` / `FN` / `CALLER` / `READ`) renders on the right edge
as a small chip; used by `callgraph.writers`.

### 3.6 `struct-map`

The offset/type/name/value grid emitted by `struct.map`.

```json
{
  "kind": "struct-map",
  "base": "CZPlayer",
  "addr": "0x0035C100",
  "fields": [
    { "offset": "+0x00", "type": "i32", "name": "health",      "value": "78"   },
    { "offset": "+0x04", "type": "i32", "name": "max_health",  "value": "100"  },
    { "offset": "+0x08", "type": "f32", "name": "stamina",     "value": "0.82" }
  ],
  "summary": "9 fields · stable across 3 frames"
}
```

### 3.7 `code` — patch / script payload

CDS or other source the user is meant to run or insert. Includes the action row
(Copy / Run / Insert) and the post-compile result inline.

```json
{
  "kind": "code",
  "language": "cds",
  "filename": "player_hp_watch.cds",
  "source": "@hook 0x00204A48 // post-store, pre-jal\n@joker_safe ...",
  "actions": ["copy", "run", "insert"]
}
```

Hook lines (`@hook`, `@joker_safe`, `@break`, `@return`) and registers (`$s0`, `$t0`)
get syntax-highlighted by the CDS grammar already in `vscode/codedesigner/syntaxes`.

A `code` block is normally followed by a `tool-call` of `cds.compile` so the user
sees the byte count and instruction count without leaving the message.

### 3.8 `kv` — compact key/value strip

For short scalar results (compile byte count, verifier step count, etc.).

```json
{ "kind": "kv", "items": [ { "k": "bytes", "v": "28" }, { "k": "instructions", "v": "7" } ] }
```

### 3.9 `timeline` — streaming step log

The vertical step log emitted by long-running tools (e.g. `runtime.verify`). Each row
is one event with a relative timestamp, a status dot, and a short label. New rows
append as the tool runs; the trailing row is shown with a "running" dot until the
tool patches it to `done`.

Used as `tool-call.body` for any tool that wants to surface progress instead of
waiting for a single final result.

```json
{
  "kind": "timeline",
  "rows": [
    { "t": "+0.0s", "status": "done",    "label": "<placeholder>" },
    { "t": "+0.4s", "status": "done",    "label": "<placeholder>" },
    { "t": "+1.1s", "status": "done",    "label": "<placeholder>" },
    { "t": "+1.9s", "status": "done",    "label": "<placeholder>", "tag": { "kind": "pill", "text": "<placeholder>", "tone": "ok" } },
    { "t": "+2.4s", "status": "running", "label": "<placeholder>" }
  ]
}
```

Row fields:

| field    | meaning                                                                  |
|----------|--------------------------------------------------------------------------|
| `t`      | relative timestamp string, formatted by the model (`+0.0s`, `+1.2s`, …)  |
| `status` | `queued` \| `running` \| `done` \| `error` — drives the dot color        |
| `label`  | short prose; inline `code` and `[[label]]` chips allowed                 |
| `tag`    | optional trailing pill (same shape as `table` cell pills)                |

The model patches the timeline by re-emitting the full `rows` array on each update
(see §5). Don't try to diff rows in-band.

### 3.10 `actions` — per-message action chips

The row of pill buttons that appears at the bottom of certain messages (e.g. under
Sonya's verifier output: "Inject for real", "Also break on max_health change",
"Hand back to Raiden"). Distinct from the **session-level** follow-up chips in §4 —
these are scoped to one message and disappear when the user picks one or the message
is superseded.

```json
{
  "kind": "actions",
  "chips": [
    { "id": "<placeholder>", "label": "<placeholder>", "command": "<placeholder>" },
    { "id": "<placeholder>", "label": "<placeholder>", "command": "<placeholder>" }
  ]
}
```

Clicking a chip sends `{ type: 'user:messageAction', messageId, chipId }` on the
command channel. The host decides whether that fires a tool, drops text into the
composer, or routes to another agent — the chip itself just carries an opaque `id`.

### 3.11 `handoff` — inline confirmation / pass-the-baton line

The "Confirmed: `g_player_health @ 0x0035C100`. Handing off to Scorpion." pattern.
Models could emit this as `text` with a `[[label]]` chip, but `handoff` makes the
hand-off explicit so the renderer can render an arrow and the receiving agent's
avatar inline.

```json
{
  "kind": "handoff",
  "label": { "text": "g_player_health @ 0x0035C100" },
  "to":    { "id": "scorpion", "display": "Scorpion" },
  "note":  "Confirmed."
}
```

---

## 4. Suggested follow-up chips

Emitted as a session-level block, separate from any message — pinned just above the
composer until the user picks one or types instead. Lives on the session state, not
inside a `Message`.

```ts
interface FollowUps {
  primary?: string;        // the slightly elevated chip in the mockup
  suggestions: string[];   // e.g. "@Scorpion trace callers of OnTakeDamage"
}
```

Selecting a chip drops the text into the composer with the caret at the end — it
does not auto-send.

---

## 5. Streaming + status

The model streams a message in two phases:

1. **Open the envelope** with `status: "streaming"`, `blocks: []`, and any known meta.
2. **Append blocks** one at a time. The renderer animates each block in. A `tool-call`
   block may itself stream: emit it first with `status: "running"` and no `body`, then
   patch it with `status: "done"`, `summary`, `durationMs`, and `body` when finished.

Patching uses a simple `{ messageId, blockIndex, patch }` envelope on the same channel
as new blocks. No diff algorithm — the model is responsible for emitting full
replacement blocks.

Error states: set `status: "error"` on the message or the offending tool-call, and
include a `text` block (or `tool-call.body = { kind: 'text', md: '…' }`) with the error
message. Never throw silently.

---

## 6. JSON wire envelope

Everything above flows over one bidirectional channel between the extension host and
the webview. Three event kinds:

```ts
type ChatEvent =
  | { type: 'message:start';   message: Omit<Message, 'blocks'> & { blocks: [] } }
  | { type: 'message:block';   messageId: string; block: Block }
  | { type: 'message:patch';   messageId: string; blockIndex: number; patch: Partial<Block> }
  | { type: 'message:done';    messageId: string; meta?: Message['meta'] }
  | { type: 'session:context'; chips: ContextChip[] }
  | { type: 'session:team';    team: TeamState }
  | { type: 'session:followups'; followups: FollowUps };
```

User → host:

```ts
type ChatCommand =
  | { type: 'user:send';          text: string; agent?: string; squad?: string; attachments?: Attachment[] }
  | { type: 'user:action';        messageId: string; action: MessageAction }
  | { type: 'user:codeAction';    messageId: string; blockIndex: number; action: 'copy' | 'run' | 'insert' }
  | { type: 'user:messageAction'; messageId: string; chipId: string }    // §3.10 chip click
  | { type: 'user:followup';      text: string }                         // §4 chip click
  | { type: 'user:context';    op: 'add' | 'remove'; chip: ContextChip }
  | { type: 'user:cancel';     messageId: string };
```

---

## 7. Authoring rules for the model

These are the rules the system prompt enforces so generated responses always render.

1. **Always return a `Message` JSON envelope**, never raw markdown or HTML.
2. **One thought per block.** Don't pack a tool result + commentary + handoff into a
   single `text` block — split them.
3. **Tool calls go in `tool-call` blocks only.** Never paste tool output into a `text`
   block. If you have nothing structured to put in `body`, omit it.
4. **Keep prose short.** Two sentences max per `text` block; the chat is dense.
5. **Use `handoff` to pass work between agents.** Inline confirmation in prose is fine
   if there's no receiving agent.
6. **Code payloads must use the `code` block** with `language` set. Follow every `code`
   block emitted for execution with a `cds.compile` (or analogous compiler) `tool-call`.
7. **Status is honest.** A `tool-call` with `status: "done"` must have a real result.
   If a tool failed, set `status: "error"` and put the error in `body`.
8. **No unknown `kind` values.** If a new visual is needed, request a schema change
   instead of inventing one in-band.
9. **Token / latency meta belong on the message,** not in prose. Don't write
   "this took 1.4 seconds" — set `meta.latencyMs`.
10. **User-addressed text only.** Don't narrate internal deliberation; the orchestrator
    surfaces planning via the `dispatch` block, not via a paragraph.

---

## 8. Worked example — the mockup, end-to-end

The screenshots correspond to the following stream of envelopes (abbreviated):

```jsonc
// 1. Orchestrator opens
{ "type": "message:start",
  "message": { "id": "m1", "kind": "agent",
    "agent": { "id": "raiden", "display": "[Fatality] Raiden" },
    "role": "ORCHESTRATOR",
    "meta": { "tokens": 320, "latencyMs": 800, "status": "streaming" },
    "actions": ["copy","redo","thumbs-up","thumbs-down"],
    "blocks": [] } }

// 2. Prose
{ "type": "message:block", "messageId": "m1",
  "block": { "kind": "text",
    "md": "Got it. Decomposing into 5 subtasks and dispatching to the team." } }

// 3. Dispatch card
{ "type": "message:block", "messageId": "m1",
  "block": { "kind": "dispatch", "title": "Dispatch · 5 subagent calls",
    "rows": [ /* see §3.2 */ ] } }

{ "type": "message:done", "messageId": "m1" }

// 4. Sub-Zero's reply — message with a tool-call whose body is a table
{ "type": "message:start", "message": {
    "id": "m2", "kind": "agent",
    "agent": { "id": "sub-zero", "display": "[Fatality] Sub-Zero" },
    "role": "MEMORY SCANNER",
    "meta": { "tokens": 180, "latencyMs": 2100, "status": "streaming" },
    "blocks": [] } }

{ "type": "message:block", "messageId": "m2",
  "block": { "kind": "text",
    "md": "Started with an i32 scan in the player heap region. After taking damage in-game and rescanning, 4 candidates narrowed to **1**." } }

{ "type": "message:block", "messageId": "m2",
  "block": { "kind": "tool-call",
    "tool": "mem.scan",
    "args": { "type": "i32", "range": "0x00350000-0x00380000", "value": "changed" },
    "status": "done", "summary": "1 result · 3 passes", "durationMs": 1400,
    "body": { "kind": "table",
      "columns": [{"key":"n"},{"key":"addr"},{"key":"trail"},{"key":"type","align":"right"},{"key":"tag","align":"right"}],
      "rows": [ { "n": 1, "addr": "0x0035C100", "trail": "= 100 → 78 → 56", "type": "i32",
                  "tag": { "kind": "pill", "text": "CONFIRMED", "tone": "ok" } } ] } } }

{ "type": "message:block", "messageId": "m2",
  "block": { "kind": "handoff",
    "label": { "text": "g_player_health @ 0x0035C100" },
    "to":    { "id": "scorpion", "display": "Scorpion" },
    "note":  "Confirmed." } }
```

The remaining messages in the mockup (Scorpion → Kitana → Kano → Sonya) follow the
same shape; the only differences are `tool` namespace and `body` block kind, which is
exactly the point — one envelope, many tools.

---

## 9. Out of scope (for this spec)

- Persistence schema for sessions and messages.
- Backend orchestration (how Raiden actually fans out to sub-agents).
- The CDS injection / runtime-verify pipeline behind `cds.compile` and `runtime.verify`.
- Multi-session UI (tabs / history navigation).

These are tracked separately and consume — but do not modify — the block contracts above.
