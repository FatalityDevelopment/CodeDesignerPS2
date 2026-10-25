[← Back to Documentation Index](README.md)

# VS Code Extension

The extension (`vscode/codedesigner`) is CodeDesigner's user interface. It adds a **CodeDesigner** sidebar to VS Code and opens each tool as an editor tab. Everything it does goes through the Bridge (`CodeDesigner.Ps2.Bridge`, TCP `localhost:8765`), which it starts and connects to for you.

This page is a tour of what's there. The deeper guides are linked from each section.

---

## Getting started

1. Build the backend once (`dotnet build CodeDesigner.sln`; see [Building](building.md)). This deploys the Bridge, the built-in emulator and the MCP server to `%LOCALAPPDATA%\CodeDesigner\`.
2. Open the extension: press **F5** in `vscode/codedesigner` for a development host, or install a packaged `.vsix`.
3. Open the **CodeDesigner** sidebar (activity bar) and the **Emulator** tab.
4. Under **Connection**, pick PCSX2 (or the built-in emulator), a BIOS and a game, then click **Run**. The extension launches the emulator, starts the Bridge in its *CodeDesigner* terminal and attaches to it.

The **Welcome** page (`CodeDesigner: Open Welcome`) has quick links to every tool, keyboard tips and the What's New notes.

Every sidebar tab has a **?** button that opens its help guide.

---

## The sidebar

The sidebar has four tabs, plus an MCP button that starts the AI server. Pause, resume and the step buttons are in its status bar.

### Emulator tab

| Section | What's in it |
|---|---|
| **Connection** | Emulator, BIOS and disc paths; **Run** / **Kill**. |
| **Actions** | **Restart**, **Import** (load a binary patch), **Snapshot**, **Preview**, **Docs**, **Archive** (cheat archive). |
| **Network** | **Start Servers** (the SOCOM II online servers), **Packets**, **Config** (server `config.json`), **Debug** (script commands). |
| **Tools** | AI, Labels, Mem Card, HDD, Snapshots, Diff, Porter, Games. |
| **Assets** | Viewer, Models, Maps, Animations. |
| **Agents / Orchestration** | The AI agent personas and plans, with the lead agent and the active plan. |
| **Memories** | Progress reports saved by AI sessions. |
| **Memory Region** | Opens a disassembler panel for EE RAM, IOP RAM, BIOS ROM, Scratchpad, the hardware register windows, VU0/VU1 memory or ROM1/ROM2. |

Below these are the navigation history and the label list.

### Cheats tab

**Cheat Tools** opens the Cheat List page, the cheat archive editor, the Cheat Scanner, the Converter and the [Cheat Porter](#cheat-porter). The running game's cheats from the chosen cheat archive list (**List** dropdown) each have a toggle; **Apply All Enabled** writes them, and **Import from repository** imports the game's cheats from the cheat repository when it has no lists.

The **cheat source** box takes raw codes and [CDS](cds-language.md) together. **Compile** shows the resulting codes, **Send** writes them once or through the cheat engine, **Save .bin** writes a raw code-pair or cheat-engine binary, and **Add to list** copies them into the Cheat List. **Use .cds file** compiles the open `.cds` file (with **Add Patch** for its sidecars), replacing the old CDS tab. `.cds` files also get syntax highlighting, snippets and a compile button in the editor title bar, with output in the *CodeDesigner - CDS Compiler* channel. See [Cheat Engine](cheat-engine.md).

### Recompile tab

Lists the ELFs on the game disc and statically recompiles one to C++. See [Recompiler](recompiler.md).

---

## Debugging

### Disassembler / memory editor

One panel per memory region: disassembly, hex, labels, comment hints, function and table detection, cross-references (`X`), breakpoints (`F9`), watches and the register panel. See the **[Disassembler guide](disassembler.md)**.

Opening an `.elf`, `.bin` or `.raw` file loads it into the built-in emulator in file mode and opens a static view.

### Preview

**Preview** shows the live PCSX2 picture inside VS Code, streamed from the PCSX2 fork's GS stream server through the Bridge (port 8766). The toolbar has **Sound** (mute/unmute PCSX2), **Smoothing**, **Fullscreen**, **Screenshot**, **Controller** and **Reconnect**. It needs the CodeDesigner PCSX2 build; Sound needs a build with PINE opcodes 0x45/0x46.

### Controller

The **Controller** page is a virtual PS2 controller and a joker (button activator) code builder.

- **Build code** mode: click buttons on the controller, or press keys, to toggle them into a combo. **Capture from pad** records a combo you press on the real controller. Nothing is sent to the game.
- **Play** mode: hold buttons, drag the sticks (right-click or Shift+click a stick for L3/R3) and the input goes to PCSX2. Everything is released when you switch mode or close the page.
- **Live pad** mirrors the real controller in orange, sticks included.
- The **joker address** is found from the running game's cheats. The page ranks the addresses their D/E compare lines use, detects whether the game stores presses as 0 or 1, and lists the combos already in use. You can also type an address, and swap bytes for games that store the pad word big-endian.
- Paste code lines to get the finished activator: a `D` line for one line, an `E0nn` block for several, or a `D` line before each line.

Keys: arrows, `W A S D` (face buttons), `Q E 1 3` (shoulders), `Z C` (L3/R3), `Enter` (Start), `Right Shift` (Select).

### Snapshots

Lists the current game's PCSX2 save states with their screenshots. Load or delete a state, compare two, or extract their memory into the hex viewer.

### Memory Diff

Compares two memory dumps (snapshots, `.bin`/`.raw`/`.p2s`) or live EE RAM against a dump, shown as a GitHub-style diff: unified or split, Bytes / Words / Instructions views, adjustable context, expanders and a hunk navigator. Each hunk can be copied as write codes or opened in the disassembler.

### Debug

Runs your own script commands from `Mods/debug-commands.json` (a local, untracked file) in a terminal, with Copy / Run buttons.

---

## Cheats and patching

### Cheat List

A cheat-device style view of one of the game's cheat archive lists (picked from a dropdown; the same lists the cheat archive editor edits): tick cheats — raw codes or CDS — then compile and send them once or through the cheat engine (hook, base and status in the bottom bar), or save a raw code-pair or engine-image `.bin`. **Save to list** writes edits back to the list. See [Cheat Engine](cheat-engine.md#the-cheat-list-page).

### Cheats editor

A three-pane editor: pick a game, one of its cheat lists, and edit the cheat file with syntax highlighting. Address links jump to the debugger. **Import** runs the bulk importer against a CheatCodeArchive folder.

### Cheat Porter

Moves cheat codes from one game version's EE RAM to another's. Pick the **From** and **To** images (the game's save states, live EE RAM, or browsed dumps), paste codes or **Load game cheats**, and review the results per cheat (Ported / Review / Not found, with confidence and a picker for ambiguous matches). See [Cheat Porter](cheat-porter.md).

---

## Labels and structure

- **Labels** — the Label Manager: label sets, typed labels, pointer structs and imports. See [Labels](labels.md).
- **Structure** — Mermaid diagrams of a game's classes, vtables and call chains, built from typed labels and label groups, editable in place.
- **Struct offset calculator** — `Open Struct Offset Calculator` in the command palette.

---

## Assets

The **Assets** section opens the Zipper asset tools for SOCOM II (full) and SOCOM: Combined Assault (read-only):

- **Viewer** — browse `.ZDB` / `.ZAR` / `.ZED` packages and preview their files.
- **Models** — models, textures and characters of a package.
- **Maps** — a map composed from its GEO scene, with props, clutter, lighting and spawn points.
- **Animations** — character animations and weapon firing effects.

Every edit is a Bridge command with a matching MCP tool. See [Asset editing](asset-editing.md) and the [Zipper model format](zipper-model-format.md).

---

## Media

- **Mem Card** — browse PS2 memory cards (`.ps2`, `.mcd`, `.mc2`) with animated save icons; create a new card, and import, export or delete saves. See [Memory Card Manager](memory-card-manager.md).
- **HDD** — browse PS2 HDD images (APA partitions, PFS files). See [HDD Manager](hdd-manager.md).
- **Games** — the PCSX2 compatibility list, mirrored into the database, with covers and details.

---

## Network

- **Start Servers** runs the SOCOM II online servers (`CodeDesigner.Ps2.Multiplayer`). See [Multiplayer Servers](multiplayer-servers.md).
- **Packets** opens the Packet Viewer: capture DEV9 traffic, see every Medius, DME and SOCOM II message by name (with a layer tree and raw bytes), filter and follow flows, and use the composer to build a server message, preview it and inject it into the PS2 or send it from it.

---

## AI

- **MCP button** (top of the sidebar) starts the `CodeDesigner.AI` MCP server. See [MCP Server](mcp-server.md).
- **AI** opens the agent control panel: Overview, **Chat** (Claude, Codex, Copilot or a local model, with ASM highlighting that matches the disassembler), Orchestration, Agents, Skills, Memories, Tools, Activity and History.
- **AI** mode in the sidebar status bar (next to **Live**) hands the emulator to an MCP agent; the disassembler then follows the agent's navigation.

---

## Commands

All commands are under **CodeDesigner:** in the command palette. The main ones:

| Command | Opens |
|---|---|
| `Open Welcome` | Welcome page |
| `Connect / Disconnect Emulator` | Toggles the emulator connection |
| `Compile CDS File` | Compiles the active `.cds` file |
| `Recompile Game ELF from Disc` | Recompiler |
| `Open Preview (Live PCSX2 Display)` | Preview |
| `Open Controller` | Controller |
| `Open PS2 Cheats Editor` | Cheats editor |
| `Open Asset Viewer (Zipper)`, `Open Model Editor`, `Open Map Editor`, `Open Animation Editor` | Asset tools |
| `Open PS2 Memory Card Browser`, `Open PS2 HDD Browser` | Media browsers |
| `Open PS2 Game Compatibility` | Games |
| `Open Structure Visualizer` | Structure |
| `Open AI Activity Feed` | AI panel, Activity view |

Pages without a command (Labels, Snapshots, Diff, Porter, Packets, Debug) open from the sidebar.

---

## Source layout

```
vscode/codedesigner/
├── src/extension.ts        activation, command registration, provider wiring
├── src/socketClient.ts     every Bridge command (JSON over TCP :8765)
├── src/providers/<page>/   one provider per page (extension-host side)
├── src/webview/<page>/     the page's Angular standalone components
├── src/webview/shared/     page header, icons, shared.css, VS Code API wrapper
├── syntaxes/, snippets/    CDS grammar and snippets
└── webpack.config.js       bundles the host and one entry per webview
```

New pages follow the disassembler panel's look: VS Code theme variables, bordered cards and the shared page header.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Sidebar shows *offline* | Click **Run**, or check the *CodeDesigner* terminal for Bridge errors. |
| Bridge exits on startup | LocalDB didn't start. `sqllocaldb start MSSQLLocalDB`, then reload the window. |
| Preview stays on "Waiting for frames" | Use the CodeDesigner PCSX2 build, launched from **Run**, with a game rendering. |
| Step Over / Into do nothing | They need PCSX2 (PINE). The built-in emulator only supports Step. |
| A page looks stale after rebuilding | `Developer: Reload Window`. |
| Logs | The **CodeDesigner** output channel; webview errors also go there. |
