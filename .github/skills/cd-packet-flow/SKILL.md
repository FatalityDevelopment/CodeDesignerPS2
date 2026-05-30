---
name: cd-packet-flow
description: SOCOM/Medius/DNAS-style packet reverse-engineering — start a capture, reproduce the action, query summaries, decode interesting frames, and persist findings. Use whenever the user wants to understand a network flow (login, lobby join, match handshake).
argument-hint: [filter]
---

# Reverse a network flow

DEV9 packet capture is observation-only by default. Inject only with explicit
intent (`net_inject` / `net_inject_tx` are tagged NetworkInjection risk).

## Required state

- `cd_status` shows **Bridge: connected**.
- `net_get_status` shows **Enabled: true** and a known adapter type. If not,
  tell the user to enable DEV9 in PCSX2 first.

## Steps

1. **Sanity-check the network surface**: `net_get_status`, `net_get_stats`.

2. **Start a capture**, scoped narrowly: `net_capture enable=true direction=0
   maxQueue=2000`. If a `filter` argument was given to this skill, also call
   `net_set_filter` to drop noise (e.g. `filterType=2 port=10075`).

3. **Tell the user** to reproduce the action in-game now. Wait one turn for
   their go-ahead before proceeding.

4. **Stop the capture**: `net_capture enable=false`.

5. **Drain summaries**: `net_recv_capture maxPackets=200`. Scan the resulting
   table for: TCP three-way handshakes, DNS lookups, recurring Medius opcodes,
   and direction changes. Pick the **2–5 most interesting** packet ids.

6. **Decode the interesting ones**: for each picked id call
   `net_disassemble frameBase64=<bytes>` (use the base64 from the row, not
   the table). Note opcode patterns, IDs, payload shapes.

7. **Label finds** in the active label set:
   - For known server endpoints, `label_add` with type `Address` at the IP/port.
   - For game-side handler functions, `label_add` type `Function`.

8. **Persist the flow** via `progress_save` with a name like
   `<game>-<flow>-packets` containing: server endpoints, packet sequence,
   opcode mapping, unresolved questions. `cd_milestone_reached "<flow> captured"`.

## Tips

- For repeated runs, increase `maxQueue` and call `net_capture` between runs
  to clear stale frames.
- Use `cd_context_get "packet_workflow"` for a longer reference doc on common
  Medius/DNAS opcodes if the user wants depth.
