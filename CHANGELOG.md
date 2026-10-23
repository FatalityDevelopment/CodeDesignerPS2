# Changelog

## 1.4.0 — 2026-10-23

### Assets editors
- New **Assets** menu category. The Zipper workbench moves into it and is renamed **Viewer**.
- New **Model**, **Animation** and **Map** editor pages, fully working for SOCOM II and with minor issues for SOCOM CA. Every edit and query is a bridge command with a matching MCP tool, so AI and human editing behave the same (see `docs/asset-editing.md`).
- Map editor composes the whole map from its GEO scene: each visual is placed at its GEO node, chunk origins and flagged chain tags are honored, props and lighting appear in composed scenes, and textures are mipmapped.
- Map editor extras: spawn camera, sky toggle, fly-camera keys that work without focus, and a package-state pane that explains where textures came from.
- Map loading is more forgiving. It falls back to the world model alone when composition fails, finds a ZDB's top-level archives by path and size, fetches root map archives a package didn't deliver, and loads `AIMAPS.MPS` and the map config with a loose `WORL_MDL`.
- World draws cut out clear texels.
- Zipper decoders now follow the game: GS texture registers, DMA chain binding and vertex colours. Fixes for APACHE/ZDB formats.

### SOCOM II online server
- Briefing rooms, room join and default games reach the in-game lobby on hardware.
- Room transitions, round end and stat submission no longer disconnect.
- Community-screen disconnect fixed with retail login-batch replies.
- Friends, ignore list, player rankings and clans, confirmed on hardware.
- Chat, admin bans, cheat scanner, news fix and in-game **patch download**.
- Lobby refresh chain, room player list, join-by-name, friends-by-name and spectators.
- EULA and Medius news display; the MLS no longer sends proactive keepalives once the client self-heartbeats, and gates PlayerInfo to the community connection.
- Poke injection on the MUIS cheat exchange (early peek/poke experiment).
- **Multiplayer debug API** for scripting server tools.
- **12v12** lobby size, extra online maps and simulated bots. `socom_bots.py` joins named fake players (`BOT1`..`BOT23`) into the host's lobby through the game's own DME join path.
- Server container support (Docker); removed leftover `ps2-electron` files.

### SOCOM CA in SOCOM II
- Runtime character and prop packs; CA model ports.
- CA weapons ripped and added to the armory, loaded at map load.
- CA vehicles: batch converter, real bounds, ground placement.
- CA static props: LOD copies and enclosing hulls are detected and dropped automatically.
- CA animations retargeted and played in game.

### SOCOM II player and cheat tools
- Commands: `giveweapon` (CA and custom weapons in hand), `revive`, `kill`, `respawn`, `prop`, `despawn`.
- Custom weapon HUD icons drawn over a stand-in icon's buffers, with a splash tool.
- A weapon authoring kit.
- Mapped body-carry state, weapon record layout, nearby-soldier list, animation set and UI character type fields.
- `CHEATS.md` help for the cheat and player-tool commands; Debug page help button; ESP script.

### Debugger and disassembler
- Cheat list annotations in the debugger and in the label column.
- Disassembler: persisted data formats restored, ASCII hints on word data, search highlighting fixed, cursor jumps at page boundaries, goto and format fixed, hex view refreshes on page change in live mode.
- Memory panels no longer carry their own EE RAM / IOP RAM / BIOS ROM / Scratchpad buttons; regions open from the sidebar.
- Go to address opens the matching memory panel when it isn't open yet (e.g. a BIOS address opens BIOS ROM) and lands on the address once the panel finishes loading.
- **Comment-column hints** are reliable: `lui`+`addiu` shows the right address, loads show the loaded value, branch targets stay correct after string rows, and registers the analyzer never saw set are left out instead of shown as 0. `jal` lists arguments set in its delay slot, registers are forgotten across calls and jumps, hints carry across page boundaries, and paused state applies from the PC row only. Your own comments show before generated hints.
- **R5900 decoding:** VU0 macro mode rebuilt from PCSX2's tables (dest masks, every op, assembler support); `qmfc2`/`qmtc2`/`cfc2`/`ctc2` fixed with `.i` and named control registers; COP1 accumulator/max/min field fix; `mult rd`, `pmfhl.*`/`pmthl.lw`, `pabsw`/`pabsh` fixed; BC0/BC2 branches, PREF and debug/perf-counter moves added; syscall codes decode correctly. Opcodes the EE lacks decode as `.word`.
- **Cross-references:** the bridge indexes each region once, so rows show an incoming-reference badge (`◂3`, `◂3+2` with data pointers). Click it or press **X** to list callers, branches, loads, stores and pointers instantly; right-click → *Rebuild Cross-reference Index* after the game loads new code. MCP tools `get_xrefs` / `rebuild_xrefs`.
- **Function boundaries:** functions are detected from call targets and stack-frame prologues. Each one gets a divider and an `FNC_` placeholder name when unnamed, comment hints simulate from the function's entry (so values set far above a page still resolve), and register state resets at every function start. MCP tool `get_function`.
- **Data tables:** switch jump tables (from the `lui`/`addu`/`lw`/`jr` pattern, bounded by `sltiu`), pointer tables and float tables (data loaded by `lwc1`) are recognised and shown as `.word`/`.float` rows with `case N -> target`, pointer and value comments. Switch cases also appear as cross-references. Your own data formats still take priority.
- **Hint level:** a *Hints* dropdown in the memory editor toolbar picks Full, Addresses (only calls, branches, loads/stores, built addresses, syscalls) or Off. The choice is stored by the bridge and shared by every panel.
- Signed immediates (`addiu sp, sp, -$0030`) and pseudo-ops (`move`, `li`, `b`, `bal`, `beqz`/`bnez`, `not`, `negu`, `sync.p`); every printed form re-assembles.
- Memory editor fixes: pages skipped during another read are replayed, patches write to the panel's own region (IOP RAM), the edit overlay no longer saves hints as comments, labels reach every open panel, one pause no longer spams navigation, and the host opens the region panel that holds the PC.
- Breakpoints: PCSX2 resumes correctly at breakpoints.
- Watches no longer use the wrong address in the UI and MCP tool.
- Structure visualizer chrome updated.
- Label member parent resolution fixed.

### Cheats editor
- Syntax highlighting.
- Placeholder code lines allowed.
- Cheat archive refresh importer, resilient to failures.
- Address links navigate correctly and perform better.
- Actions stay visible.

### CDS language
- Editor **Run** action and a compiler output channel.
- Comment and instruction colors match the editor; register handling synced with the grammar.
- Compiler fixes: 9-digit immediates, FPU/COP instructions, label operands.

### AI and MCP
- **AI chat** panel: composer, model selector and ASM highlighting that matches the disassembler.
- MCP label search tools.
- **Discord bot** that posts AI activity to a channel.

### Other
- Patch editor updates.
- Removed the `Ps2Dis` project and the Electron scaffold.

## 1.3.0 — 2026-06-04

- **VU0 / VU1 tabs** — Full vector + integer register inspection.
- **Cheat archive sync** — Pulls known codes from `CheatCodeArchive` per CRC.
- **Zipper** — Read `.zdb`, `.zar`, `.savegame` blobs.
- **MCP tools** — Copilot can now write live memory.
- **PCSX2 paths sidecar** — Snapshot viewer follows the running emulator's savestate directory.
- **Label Manager** — Always uses SCUS/SLUS serial as the `gameId`.
- New welcome page; 2D viewing fixes in the ZDB workbench.
