[← Back to Documentation Index](README.md)

# The emulator: Ps2Emulator

Code Designer debugs games running in **Ps2Emulator**, a PlayStation 2
emulator written in Rust (its own repository). Code Designer has no emulator
of its own any more: the built-in C# emulator, the PCSX2/PINE client and the
Bridge's emulator commands were retired in favour of Ps2Emulator's debug
server.

## How they talk

```
VS Code extension ──gRPC (ps2debug protocol)──▶ Ps2Emulator  (ps2 --debug-server 127.0.0.1:28100)
        │
        └──JSON over TCP :8765──▶ CodeDesigner.Ps2.Bridge  (the workspace service)
```

- **Ps2Emulator** owns everything live: run control, breakpoints and
  watchpoints, memory and registers, disassembly with state hints, the code
  index and cross-references, value and pointer scans, traces and coverage,
  threads and modules, stack walks, patches (cheats applied every field),
  pads, the network adapter, frames, screenshots and console output. Analysis
  runs inside the emulator; only results cross the wire.
- **The Bridge** is the workspace service: labels and data formats, notes,
  cheat lists and the cheat archive, CDS compile, converters and the porter,
  discs, memory cards, HDD images, assets, the packet dissector, agents and
  memories. It pushes label sets to the emulator, so disassembly shows them.

The extension's live calls (`BridgeClient` methods for memory, registers,
breakpoints, stepping, pads, network, …) go to Ps2Emulator only; without it
they fail with "Ps2Emulator is not connected".

## Running a game

1. In the panel's **Connection** section, select `ps2.exe` as the emulator
   (browse to your Ps2Emulator build), a BIOS and a game.
2. **Run** starts the Bridge if needed, launches Ps2Emulator in a terminal
   (`ps2 bios <bios> --disc <game> --debug-server <address> --window`),
   waits for its debug server and connects. The live memory views, the
   Preview (game view) and the archive cheats for the disc follow.
3. **Kill** closes it; **Restart** closes it and runs again.

Settings:

| Setting | Meaning |
|---|---|
| `codedesigner.ps2Emulator.debugServer` | The debug server's address (default `127.0.0.1:28100`) |
| `codedesigner.ps2Emulator.network` | Start with the network adapter (`--network`): DHCP, DNS and the internet through a user-mode NAT |

The NVRAM (BIOS settings) and the memory card live in the extension's
storage. The NVRAM starts as a copy of the `.NVM` beside the BIOS, so the
BIOS does not stop at its language screen.

**CodeDesigner: Connect to Ps2Emulator** attaches to an emulator started by
hand (`ps2 … --debug-server <port>`). **Open in Ps2Emulator** on an `.elf`
runs it as a program (`ps2 elf <file>`).

## What maps where

| Feature | Ps2Emulator service |
|---|---|
| Disassembler, memory views, registers, stepping, breakpoints | `Code`, `Memory`, `Registers`, `Execution`, `Breakpoints` |
| Watchpoints (disassembler Inspect tab, CDS `watchpoint`) | `Breakpoints.SetWatchpoint` |
| Cross-references, functions | `Index` |
| Cheat Scanner, pointer chains | `Search` |
| Snapshots (memory dumps in `snapshots/<game>/<date>/<name>/`) | `Memory` reads, plus `Media.Screenshot` for the preview |
| Traces, coverage, call stacks, threads, modules | `Trace`, `Os` |
| Cheat engine, cheat lists, CDS patches | `Patches` |
| Controller | `Pads` |
| Packets panel (capture, filters, inject) | `Network` (frames dissected by the Bridge) |
| Preview | `Media.Frames` |
| Ps2Emulator Console output channel | `Media.Console` |

Not available yet: save states (a project of their own in Ps2Emulator),
audio (its SPU2 does not synthesise), reset (Restart relaunches instead),
speed limits, IOP module export labels, and PCSX2 `.p2s` files.

## AI

Agents use two MCP servers: Ps2Emulator's (`ps2 mcp`, or `/mcp` on the debug
server with `--mcp`) for everything live, and `CodeDesigner.AI` for the
workspace. See [MCP Server](mcp-server.md) and the skills in
`.github/skills/`.
