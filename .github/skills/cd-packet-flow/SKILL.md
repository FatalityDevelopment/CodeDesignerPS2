---
name: cd-packet-flow
description: SOCOM/Medius/DNAS-style packet reverse-engineering — capture Ps2Emulator's network traffic while the user reproduces an action, decode interesting frames, and persist findings. Use whenever the user wants to understand a network flow (login, lobby join, match handshake).
argument-hint: [filter]
---

# Reverse a network flow

Ps2Emulator's MCP captures the PS2's network adapter (`ps2_network`,
`ps2_capture`); Code Designer's MCP decodes the game protocols in the
frames (`net_disassemble`, `net_decrypt_scert`, `medius_decode`) and keeps
the labels and notes.

## Required state

- Ps2Emulator runs the game with its network adapter (Code Designer's
  `codedesigner.ps2Emulator.network` setting, or `ps2 … --network`).
- `ps2_network` shows the adapter and an address (the game has done DHCP).

## Steps

1. **Sanity-check the network surface**: `ps2_network` (adapter, address,
   traffic counters).

2. **Tell the user** to get ready to reproduce the action in-game; agree on how
   long it takes.

3. **Capture while they do it**: `ps2_capture seconds=<n> bytes=true`,
   scoped narrowly when a `filter` argument was given to this skill
   (`protocol`, `port`, or `ip`; e.g. `port=10075`). Each frame is a line
   (direction, then its layers: Ethernet, IPv4, TCP/UDP, DNS/DHCP) followed
   by its bytes in base64.

4. **Pick the 2–5 most interesting frames**: TCP handshakes, DNS lookups,
   recurring Medius ports, direction changes.

5. **Decode them**: `net_disassemble frameBase64=<bytes>` gives the full layer
   tree, SCERT and Medius included. Encrypted SCERT records go through
   `net_decrypt_scert` with the session key; Medius payloads through
   `medius_decode`. Note opcode patterns, IDs, payload shapes.

6. **Label finds** in the active label set:
   - For known server endpoints, `label_add` with type `Address` at the IP/port.
   - For game-side handler functions, `label_add` type `Function`.

7. **Persist the flow** via `progress_save` with a name like
   `<game>-<flow>-packets` containing: server endpoints, packet sequence,
   opcode mapping, unresolved questions. `cd_milestone_reached "<flow> captured"`.

## Tips

- Capture again for each run: `ps2_capture` starts and stops its own capture,
  so runs never mix.
- To replay or craft a frame, build it with `net_build_message` /
  Code Designer's packet composer; injecting it is a Ps2Emulator action
  (the Packets panel in Code Designer), done only with the user's intent.
- Use `cd_context_get "packet_workflow"` for a longer reference doc on common
  Medius/DNAS opcodes if the user wants depth.
