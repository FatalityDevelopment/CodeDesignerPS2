[← Back to Documentation Index](README.md)

# MCP Server — User Guide

`CodeDesigner.AI` is CodeDesigner's MCP (Model Context Protocol) server: the **workspace**. It gives AI assistants such as Claude, GitHub Copilot or any other MCP client the project data a human has in VS Code: labels and data formats, notes and progress reports, CDS compile, cheats and converters, disc/card/HDD images, the Zipper asset editors, the SOCOM II network protocols, and multi-agent coordination.

Everything **live** — run control, breakpoints and watchpoints, memory and registers, disassembly with state hints, cross-references, value and pointer scans, traces, threads, patches, controller input, network capture, screenshots — is **Ps2Emulator's MCP** (`ps2_*` tools: `ps2 mcp`, or `/mcp` on its debug server with `--mcp`). Agents attach both servers; see [Emulator](emulator.md).

It exposes **149 tools** in 8 domains. A **profile** picks which domains and how much risk a client sees, so a small session doesn't pay for the whole tool list.

> The older `CodeDesigner.Ps2.McpServer` project has been removed. `CodeDesigner.AI` is the only MCP server.

---

## Architecture

```
AI client (Claude / Copilot / ...)
   │ stdio JSON-RPC (MCP)                           │ stdio or HTTP (MCP)
   ▼                                                ▼
CodeDesigner.AI                                 Ps2Emulator's MCP (ps2_* tools)
   │                        │                       │ gRPC (ps2debug protocol)
   │ TCP localhost:8765      │ EF Core               ▼
   ▼                        ▼                   Ps2Emulator (the running game)
CodeDesigner.Ps2.Bridge   SQL Server LocalDB
(the workspace service)   (Database=CodeDesigner)
```

- **Workspace work** (compiling, converting, dissecting, disc and asset files) goes through the **Bridge** socket on `localhost:8765`. The Bridge must be running; the VS Code extension starts it for you.
- **Coordination state** (labels, progress reports, agent profiles, orchestration plans, sessions, messages) is read and written **directly in LocalDB**, so those tools work without the Bridge.

For the full system map see [Architecture](ARCHITECTURE.md).

---

## Running the server

Building `CodeDesigner.AI` deploys it to `%LOCALAPPDATA%\CodeDesigner\AI\`:

```bash
dotnet build CodeDesigner.AI/CodeDesigner.AI.csproj
```

There are three ways to run it:

- **From the extension.** The sidebar's MCP button (top of the Emulator panel) starts `CodeDesigner.AI.exe --profile all` in a *CodeDesigner.AI MCP* terminal and shows whether it is online.
- **From your AI client.** Point the client at the exe. The repo's [`.vscode/mcp.json`](../.vscode/mcp.json) ships ready-made entries:

  ```json
  {
    "servers": {
      "CodeDesigner": {
        "type": "stdio",
        "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe",
        "args": ["--profile", "all"]
      },
      "CodeDesigner-AI-re":       { "type": "stdio", "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe", "args": ["--profile", "re"] },
      "CodeDesigner-AI-readonly": { "type": "stdio", "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe", "args": ["--profile", "re", "--readonly"] }
    }
  }
  ```

  For Claude Code: `claude mcp add codedesigner -- "%LOCALAPPDATA%\CodeDesigner\AI\CodeDesigner.AI.exe" --profile re`.
- **By hand, to inspect a profile.** `CodeDesigner.AI.exe --profile lite --list-tools` prints the tools a profile exposes and exits. It needs no bridge or database.

### Command-line options

| Option | Meaning |
|---|---|
| `--profile <name>` | Tool profile (default `core`). See the table below. |
| `--readonly` | Cap the profile at read-only tools. |
| `--dangerous-tools false` | Drop tools rated `dangerous`. |
| `--agent-id <n>` | Run as agent persona *n*. `cd_session_init` then shows that agent's onboarding. |
| `--parent-session <n>` | Link this process to a lead session (sub-agents). |
| `--host <h>` / `--port <p>` | Bridge address (default `127.0.0.1:8765`, or `CODEDESIGNER_HOST` / `CODEDESIGNER_PORT`). |
| `--connection-string <cs>` | Database (default LocalDB, or `CODEDESIGNER_CONNECTION_STRING`). |
| `--list-tools` | Print the profile's tools and exit. |

### Profiles

| Profile | Domains | Max risk | Tools |
|---|---|---|---|
| `lite` | curated list (session, progress, labels, disassemble, compile) | database | 17 |
| `core` | Core | database | 25 |
| `multi-agent` | Core + Admin | database | 49 |
| `cheats` | Core + Memory, Patching, Cheats | database | 50 |
| `packets` | Core + Labels, Network | database | 52 |
| `re` | Core + Memory, Labels, Patching | database | 60 |
| `patching` | Core + Memory, Labels, Patching, Cheats | database | 73 |
| `media` | Core + Labels, Media | disk | 94 |
| `admin` / `all` | everything | dangerous | 149 |

A session can widen itself without relaunching: `cd_enable_domain` adds a domain (within the launch risk cap) and the client receives `tools/list_changed`. `cd_list_domains` shows what is on.

**Risk levels**, lowest to highest: `read-only` → `database` → `disk` → `memory` → `network` → `dangerous`. Tools above `database` take an optional `reason` argument; calling one without a reason adds a short reminder to the result.

---

## Starting a session

Start every session with **`cd_session_init`**. It returns a compact summary: the active profile, bridge status, matching progress reports to resume, the agent's onboarding (when `--agent-id` is set), core rules and suggested next calls.

```
cd_session_init                      → profile, bridge status, resume candidates
progress_search "socom2 health"      → find earlier work
progress_get "socom2-player-health"  → read it
cd_set_goal "map the player struct"  → keeps the session on task
```

Other session helpers:

- `cd_tool_index` lists the profile's tools; `cd_tool_help <tool>` gives one tool's full description and parameters.
- `cd_context_get` fetches heavier docs on demand (agent profile, orchestration plan, progress report, DB schema, workflows), so `cd_session_init` stays small.
- `cd_milestone_reached`, `cd_check_session_health` and `cd_session_summary` track progress and say when it is time to save.
- Save findings with `progress_save` at milestones and before you stop.

The server also publishes MCP **resources** (`cd://docs/rules`, `cd://docs/db-schema`, `cd://docs/packet-workflow`) and **prompts** (`session_bootstrap`, `packet_reversing`).

---

## Tool reference

Tool descriptions come from the `[CdTool]` attribute on each tool class under `CodeDesigner.AI/Tools/<Domain>/`. That source is the only definition; there are no database overrides. The tables below list every tool. `cd_tool_help` shows parameters.

### Core (25)

Always on. Session start, status, tool discovery, goals, progress reports, multi-agent messaging, and basic labels.

| Tool | Risk | Description |
|------|------|-------------|
| `cd_active_sessions` | read-only | List currently active (sub-)agent sessions. |
| `cd_check_session_health` | read-only | Report session age, last progress save, labels added, current goal, milestones, recommendations. |
| `cd_context_get` | read-only | Fetch one context doc on demand (agent_profile, orchestration_plan, progress_report, db_schema, *_workflow). Keeps session_init small. |
| `cd_enable_domain` | read-only | Expose another tool domain at runtime without relaunching (memory\|labels\|network\|patching\|cheats\|media\|admin). Honours the launch risk cap. |
| `cd_get_goal` | read-only | Return the current session goal (or hint to set one). |
| `cd_inbox_pop` | database | Pull unread messages addressed to your session (or your agent persona). Marks them read. |
| `cd_list_domains` | read-only | List tool domains and whether each is currently enabled (launch profile + any widened at runtime). |
| `cd_milestone_reached` | read-only | Mark a milestone (e.g. 'found struct', 'verified hook'). Shows up in cd_check_session_health. |
| `cd_orch_advance` | database | Advance the active orchestration to a stage (step number or label) with an optional summary; optionally notify active sessions. |
| `cd_orch_current_step` | read-only | Show the active orchestration plan's current stage, the resolved current/next workflow step, and the parsed step list. |
| `cd_send_message` | database | Send a message to a specific session, a specific agent persona, or broadcast (both null). |
| `cd_session_begin` | database | Register a new (sub-)agent session and surface that agent's Rules/Guidelines/Objective/SessionBegin Markdown. Returns the new session id. |
| `cd_session_end` | database | Mark a (sub-)agent session complete and surface the agent's SessionEnd checklist. |
| `cd_session_init` | read-only | Start a session: compact summary (profile, game, bridge, active progress, agent onboarding, core rules, suggested next calls). |
| `cd_session_status` | read-only | Read full details for one (sub-)agent session by id. |
| `cd_session_summary` | read-only | Recap the session so far: goal, age, labels/saves, and the milestone narrative. The 'where are we?' view. |
| `cd_set_goal` | read-only | Declare the current session goal (one line). Helps keep the AI on task. |
| `cd_status` | read-only | Compact backend status: profile and workspace (Bridge) connectivity. The emulator is Ps2Emulator's MCP (ps2_status). |
| `cd_tool_help` | read-only | Full description and parameter schema for one tool. Use when the one-line summary isn't enough. |
| `cd_tool_index` | read-only | List the tools in the current profile (names grouped by domain; verbose:true adds one-line descriptions). |
| `label_add` | database | Create or update a label at an address. |
| `label_list` | read-only | List labels from the active label set; optional substring filter on the label name. |
| `progress_get` | read-only | Read the full Markdown of one progress report by name. |
| `progress_save` | database | Create or overwrite a progress report. Use at milestones and before ending a session. |
| `progress_search` | read-only | Search saved progress reports by text; returns matching report names. |

### Memory (6)

Analysis of bytes you supply (a file, a dump, or memory read with Ps2Emulator's `ps2_read_memory`): strings, functions, pointers, floats, integers; and snapshot records.

| Tool | Risk | Description |
|------|------|-------------|
| `get_floats` | read-only | Scan a base64 binary blob for plausible single-precision floats. |
| `get_functions` | read-only | Scan a base64 binary blob for MIPS function entry points. |
| `get_integers` | read-only | Scan a base64 binary blob for non-zero 4-byte aligned integers. |
| `get_pointers` | read-only | Scan a base64 binary blob for confirmed EE RAM pointers. |
| `get_snapshots` | read-only | List saved memory snapshots for a game (lookup by ISO filename or title). |
| `get_strings` | read-only | Scan a base64 binary blob for printable ASCII strings. |

### Labels (23)

Label sets, pointer structs, groups, data formats, call graphs and IOP module labels.

| Tool | Risk | Description |
|------|------|-------------|
| `callgraph_query` | read-only | Return callers/callees of a focus function, up to a depth. Requires a prior callgraph_scan. |
| `data_format_clear` | database | Remove data-format overrides covering an address range (reverts to instruction decoding). |
| `data_format_list` | read-only | List data-format overrides for a game (optionally within an address range). |
| `data_format_set` | database | Tag a memory range as a data type (Byte\|Half\|Word\|DWord\|QWord\|Float\|Double) for disassembler rendering. |
| `group_add_member` | database | Add the label at an address to a group (the label must already exist in the set). |
| `group_create` | database | Create/update a label group (Class/Module/Subsystem/VTable) for the Structure Visualizer. |
| `group_delete` | database | Delete a label group (its members are detached, not deleted). |
| `group_list` | read-only | List label groups in a set with their members (for the Structure Visualizer). |
| `group_remove_member` | database | Remove the member at an address from a label group. |
| `label_add_batch` | database | Create/update many labels at once from a JSON array. |
| `label_add_pointer` | database | Define a struct: a named base address whose fields you then map with label_add_pointer_member. |
| `label_add_pointer_member` | database | Add/update a struct field: a named member at a byte offset from the (dereferenced) base. |
| `label_create_set` | database | Create a new, empty label set. |
| `label_delete` | database | Remove a label entry from a label set. |
| `label_delete_pointer` | database | Delete a struct definition by name. |
| `label_delete_set` | database | Permanently delete a label set and all its entries (irreversible). |
| `label_get_pointers` | read-only | List struct definitions (a named base address + its member fields). 'Pointer' = struct here. |
| `label_list_functions` | read-only | List only Function-typed labels (FNC_ prefix) from the active label set. |
| `label_list_sets` | read-only | List all label sets for a game (including the built-in set). |
| `label_repair_orphan_members` | database | Repair labels whose address contains ':+...' but whose ParentId/Offset relationship is missing. |
| `label_search` | read-only | Search labels by name, address, comment, value type, or label type across a game/set. Returns compact hits; fetch full set only when needed. |
| `label_search_sets` | read-only | Search label-set names and count matching labels inside each set. Use before fetching full sets to keep token usage low. |
| `label_set_active` | database | Mark a label set as active (merged with built-in for lookups). |

### Patching (6)

Assemble/disassemble and compile, validate, explain or apply CDS scripts.

| Tool | Risk | Description |
|------|------|-------------|
| `assemble` | read-only | Assemble PS2 MIPS64 EE assembly (one instruction per line) into hex opcodes. |
| `compile_cds` | read-only | Compile a Code Designer Script (.cds) into GameShark/AR codes + MIPS memory patches. |
| `decompile_to_cds` | read-only | Disassemble code words and emit a re-compilable .cds source (branch targets become :target_ labels). Read the words from the running game with Ps2Emulator's MCP (ps2_read_memory). |
| `disassemble` | read-only | Disassemble PS2 MIPS64 EE hex opcodes (space/newline separated) into assembly. |
| `explain_cds` | read-only | Parse a .cds source locally and describe each directive in human-readable terms (no emit, no bridge call). |
| `validate_cds` | read-only | Parse a CDS source without emitting; returns diagnostics (errors + warnings). |

### Cheats (13)

Cheat lists, archive search, format conversion and porting, and the frame-loop cheat engine.

| Tool | Risk | Description |
|------|------|-------------|
| `cheat_list_builds` | read-only | List the saved cheat lists ('builds') for a game. |
| `cheat_list_create` | database | Create a new empty cheat list/build for a game. |
| `cheat_list_delete` | database | Delete a cheat list/build by id. |
| `cheat_list_games` | read-only | List games that own at least one saved cheat list/build. |
| `cheat_list_get` | read-only | Read a full cheat list as editor text. Prefer cheat_list_sections/search/get_section first to keep token usage low. |
| `cheat_list_get_section` | read-only | Read only one section from a cheat list as editor text; optionally filter cheats inside it. |
| `cheat_list_import` | database | Import a cheat list from a file on disk into the archive database. |
| `cheat_list_save` | database | Overwrite a cheat list's entries. Data is the editor text returned by cheat_list_get or cheat_list_get_section. |
| `cheat_list_sections` | read-only | List section headers in one cheat list with cheat/code counts. Use this before reading codes to keep token usage low. |
| `cheat_search` | read-only | Search cheat labels, section headers, descriptions, and code text. Returns compact hits; fetch a section afterward for full codes. |
| `convert_cheat` | read-only | Convert cheat codes between raw and device-specific formats (CodeBreaker / GameShark / Action Replay). Offline. |
| `detect_cheat_format` | read-only | Detect the encryption format of a cheat-code block (offline). |
| `port_cheats` | read-only | Port cheats between game revisions by scanning two memory dumps for matching patterns. |

### Network (4)

SOCOM II network protocols: dissect frames (SCERT, Medius, DME), decrypt SCERT records, build messages from the catalog. Capture frames with Ps2Emulator's `ps2_capture bytes=true`.

| Tool | Risk | Description |
|------|------|-------------|
| `medius_decode` | read-only | Decode a raw Medius payload (bytes after the class+type prefix) against the registered struct schema. Returns named fields. |
| `net_build_message` | read-only | Encode one multiplayer-server message (SCERT, Lobby, LobbyExt, MGCL, DME or SOCOM II Game, see NETCATALOG) from field values into a plaintext SCERT record, returned as base64 for net_inject's payloadBase64. |
| `net_decrypt_scert` | read-only | Decrypt every encrypted SCERT record in a captured Ethernet frame using an RC4 session key, then dissect to layers + fields. |
| `net_disassemble` | read-only | Dissect a raw Ethernet frame into a structured layer tree (Ethernet/ARP/IPv4/ICMP/TCP/UDP/SCERT/Medius) with field-level Medius decoding. |

### Media (46)

Disc, memory card and HDD images; the Zipper asset editors; weapon authoring; GS frames; compatibility list; recompiler.

| Tool | Risk | Description |
|------|------|-------------|
| `assets_animation` | read-only | One animation clip's tracks: per bone (names match the character skeleton from assets_model_mesh) its kind and positions [[x,y,z]] (local translation, units) and rotations [[x,y,z,w]] (local unit quaternions under the skeleton parent), one entry when static or one per sample (frames + 1, evenly over the duration). Extra bones rifle / pistol / launcher / weapon sit under the hand (weapon attachment). |
| `assets_close` | read-only | Close an asset session, discarding unsaved edits. |
| `assets_describe_model` | read-only | Describe one model: visuals (N<node>_<visual>), chunks (texture, vertex/triangle counts, vertex colours), each texture (size, format, palette id, transparency, alpha test) and its GEO definition node (transform, bounds, position unit, children). Use before retexture / tintVertexColors edits. |
| `assets_edit` | database | Apply one edit to a session (undoable, not written to disk until assets_save). opJson is an object with "op":\n- moveClutter {clutterIndex, position?, yaw?, scale?, matrix?}\n- addClutter {model, position, yaw?, scale?} (model = a prop such as oaktree_near, bush2_near)\n- deleteClutter {clutterIndex}\n- moveInstance {geoIndex, position?, yaw?, scale?, matrix?} (world space; scene defaults to the map scene)\n- duplicateInstance {geoIndex, position?, yaw?, scale?, name?}\n- deleteInstance {geoIndex}\n- replaceTexture {texture, pngBase64 \| imagePath, namespace?} (resampled to the texture's size)\n- retexture {archive, model, from, to, visual?} (point a model's sections at another .tif)\n- tintVertexColors {archive, model, multiply:[r,g,b], add?:[r,g,b], visual?, texture?, alpha?}\n- transformVertices {archive, model, translate?:[x,y,z], rotate?:[x,y,z] degrees, scale?: n \| [x,y,z], pivot?, chunks?:[geometryOffset], vertices?:{geometryOffset:[i...]}, matrix?} (model space; default pivot = centre of the affected vertices)\n- setVertices {archive, model, geometryOffset, vertices:[{i, position?, uv?, color?:[r,g,b,a 0..255, 128=1.0], normal?}]} or {archive, model, chunks:[{geometryOffset, vertices:[...]}]} (model space, as assets_model_mesh returns)\n- batch {label?, ops:[{op...}, ...]} (several edits as one undo step; answers each op's result)\n- movePoint {name, position:[x,y,z]} (an AIMAPS point; snaps to its AI grid cell)\n- addAnimation {name, from?: clip (default seal_stand), fromSource?, frames?, duration?, after?} (a new package clip copied from any clip, optionally resampled to `frames` intervals / given a duration in s; answers {result, name})\n- deleteAnimation {name} / renameAnimation {name, newName} (package clips only)\n- setAnimation {name, frames?, duration?, bones?:[{name, positions?:[[x,y,z]], rotations?:[[x,y,z,w]]}]} (replace tracks: 1 key = static or one per sample, as assets_animation returns; frames resamples first)\n- setAnimationKey {name, bone, frame \| allFrames:true, rotation?:[x,y,z,w], position?:[x,y,z]} (one key; a static track becomes animated)\n- addClutter answers {result, newClutterIndex}; duplicateInstance answers {result, newGeoIndex}\n- deleteFaces {archive, model, faces:{geometryOffset:[triangle...]}} (unused vertices and emptied chunks go too)\n- duplicateFaces {archive, model, faces:{...}, translate?, rotate?, scale?, matrix?} (copies into new chunks)\n- addMesh {archive, model, visual:"N000_000", texture:"x.tif", replace?, and one of: obj:"<OBJ text>" (Y up, yUp?, scale?, offset?) \| primitive:{kind:"cube"\|"plane", size, center:[x,y,z]} \| mesh:{positions:[[x,y,z]], triangles:[[a,b,c]], normals?, uvs?, colors?}} (model space; a new texture gets its own section). Topology ops answer {result, newChunks} with the new chunks' indices in assets_model_mesh order.\nPositions are map units [x, y, z] (y is height; the ground is around y = -100..300 depending on map; read nearby instances with assets_map_scene first). yaw is degrees about Y; omitted parts keep current values. Indices come from assets_map_scene and shift after add/delete, so re-read before chaining. |
| `assets_effect` | read-only | One ZANIM effect script fully decoded: names, node refs (its objects: models, model nodes, lights), and per track (tracks run in parallel) the instructions with official names, nesting depth, whether they are IF / ELSEIF conditions, decoded fields (e.g. WAIT frames/seconds, RANDOM_WEIGHT weight, OBJECT_ROTATE_STATE object/reference/euler, OBJECT_MOTION_FROM_TO duration + scaleStart/End/Rate, LIGHT colour/range/time, CALL_ANIMATION animation) and raw words. Objects: self = the effect's object, owner = what started it (a weapon's firepoint). |
| `assets_effects` | read-only | Every ZANIM effect script in the package's CZANIM.ZAR (muzzle flashes, shells, smoke, explosions, sounds, camera shakes ...): name, its tracks, the effects it calls (CALL_ANIMATION), the package models it uses and the opcodes it contains (official names from the game's opcode table). |
| `assets_export` | disk | Convert a model to the other game. From a SOCOM 3 / Combined Assault session: a character, weapon, vehicle or prop becomes a SOCOM II pack <outputDir>/<pack>/<PACK>.CDB (+ its ZEDs; the same bytes as the in-game-confirmed Mods/SOCOMCA converters), optionally with CA clips retargeted into its MOTION_S.ZAR. Templates come from SOCOM II's RUN folder (socom2Run, else found on any drive). Answers {path, pack, model, kind, library, triangles, textures, palettes, clips, warnings, open:{archive, name}} - open the path with assets_open to see it. From a SOCOM II session: the model as a SOCOM 3 / CA package. From a GTA: San Andreas session: a ped (with clips as '<source>/<name>', e.g. ped.ifp/WALK_civi, retargeted onto alg_UN01), weapon, vehicle or object becomes a SOCOM II pack (geometry encoded from triangles; vehicles as static props). |
| `assets_list_animations` | read-only | Character animation clips: the package's own MOTION_S.ZAR (editable) and the shared RUN/MOTION_P.ZAR (read-only; copy one in with assets_edit addAnimation {from}). Each: name, source, editable, duration (s), frames (intervals; samples = frames + 1), fps, bones. |
| `assets_list_models` | read-only | List models in a session: archive, name, kind (prop, weapon, character, world, sky, library), visual count. Filter by kind, name substring or archive (M51_MDL.ZED). |
| `assets_map_points` | read-only | The map's named points from AIMAPS.MPS (player / hostage starts, waypoints) with world X / Z and the AI grid. Move one with assets_edit movePoint. |
| `assets_map_scene` | read-only | List what the map places: kind (model = worldmodel, reference = a placed prop/turret/vehicle/sky, clutter = trees/bushes/grass/rocks from CLUTTER.ZAR), model, position, scale, matrix, and the geoIndex / clutterIndex that edit ops take. Filter by kind or name/path substring; limit defaults to 500. |
| `assets_model_mesh` | read-only | The editable mesh of a model in model space, the geometry the Model Editor shows: per chunk its geometryOffset (the id setVertices / transformVertices take), visual, texture, vertex/triangle counts, unit and placement. With arrays=true also base64 float32 positions/uvs/normals, RGBA colours (128 = 1.0) and uint32 triangles. Skinned characters (MESH_* in CLIB_MDL) come in bind pose (Y up, feet at 0) with a skeleton [{name, matrixId, parent, local, world}] and per vertex skinInfluences slots: skinBones int32 (skeleton index, -1 unused), skinWeights, skinPositions / skinNormals (bone-local); their vertices are view-only for now. |
| `assets_open` | read-only | Open a SOCOM II map package (G:\\RUN\\M51.ZDB, or a folder with its unpacked files) as an asset session, or join the session already open on it. Returns the session id and a summary (models by kind, changed archives). Start here before any other assets_* tool. |
| `assets_save` | disk | Write a session's changed archives. mode=zdb writes the package to outputPath (default: over the source, keeping a one-time .bak); mode=pack writes a memory-card pack <NAME>.CDB (packName must start with 'm', max 7 characters) to outputPath and, with folderCard (a PCSX2 folder memory card), installs it as BASCUS-97275<NAME>/<NAME>.CDB. Prefer writing a copy (outputPath) over the retail file. |
| `assets_sessions` | read-only | List open asset sessions (including ones a person opened in an editor) with revision and unsaved archives. |
| `assets_texture` | read-only | Decode a texture (.tif) with the palette the game binds (TEX0.CBP) and return its info plus a base64 PNG with real alpha. |
| `assets_undo` | database | Undo the last edit in a session (or redo with redo=true). |
| `assets_weapon_fx` | read-only | What firing a weapon plays: its zweapon.rdr record (all scalar fields) and the CZANIM.ZAR effect tree from FireAnimName: each effect's names, the package models it uses (muzzle_flash_* / bullet_shell_* in EFFE_MDL), textures, tracks (target + instructions: opcode, block depth, words as floats) and the effects it spawns (opcode 0x2D). Weapon models carry firepoint / firepoint_shell / aimpoint locators (assets_model_mesh locators) where these spawn. |
| `assets_weapons` | read-only | Weapons whose model is in the package (RUN/ZWEAPON.ZAR zweapon.rdr): internal name, model, the ZANIM effect played when fired (fireAnimName, e.g. muzzle_m4) and fireWait (s between shots). |
| `compat_cover` | read-only | Check whether cover art exists for a disc serial (fetches/caches it; the binary image itself is not returned). |
| `compat_details` | read-only | Get the compatibility details for one game by serial and/or title. |
| `compat_list` | read-only | List every game disc that carries a PCSX2 compatibility status, by title. |
| `compat_sync` | disk | Download the latest PCSX2 compatibility database and update local game statuses. |
| `hdd_create` | disk | Create a new blank HDD image with an APA table (overwrites if exists). |
| `hdd_delete` | disk | Delete a partition from an HDD image (irreversible). |
| `hdd_export` | disk | Export a file from an HDD partition to disk. |
| `hdd_import` | disk | Import a file/directory into an HDD partition (PFS write may be unimplemented in backend). |
| `hdd_list` | read-only | List APA partitions on a PS2 HDD image. |
| `hdd_list_files` | read-only | List files in a specific partition on a PS2 HDD image. |
| `iso_get_info` | read-only | Get metadata about a PS2 disc image (format, sector count, size, title). |
| `iso_list_files` | read-only | List all files/directories on a PS2 disc image (ISO-9660). |
| `iso_read_file` | read-only | Extract a file from a disc image to disk (or return base64 when outputPath is omitted). |
| `list_disc_elfs` | read-only | Open a PS2 disc image and list every ELF/IRX on it (name, size). |
| `mc_delete` | disk | Delete a save-game directory from a memory card image (irreversible). |
| `mc_export` | disk | Export a save-game directory from a memory card image to disk. |
| `mc_format` | disk | Create a new blank 8 MB PS2 memory card image (overwrites if exists). |
| `mc_import` | disk | Import a save-game directory into a memory card image. |
| `mc_list` | read-only | List all save-game directories on a PS2 memory card image. |
| `mc_read_file` | read-only | Read the raw bytes of a file inside a memory card save directory (base64). |
| `recompile_elf` | disk | Extract a named ELF from a disc image and statically recompile it to C++ source. |
| `weapons_create` | disk | End-to-end orchestrator: pick a template, regenerate its texture from a user image (file path OR base64 for chat-attached images) and optionally tweak the mesh, then add the result as new entries (model + texture, plus palette if indexed) alongside the template inside the archive. Originals are untouched. |
| `weapons_describe_template` | read-only | Get a structured DTO for one template weapon: per-chunk vertex/triangle/scale data, vertex bounds, texture metadata, and a short explainer of the .mdl/.tif binary layout. |
| `weapons_encode_model` | read-only | Re-encode a template weapon's .mdl with optional per-chunk vertex transforms and texture-name rebinds. Returns base64 model bytes. Use to preview mesh edits before weapons_create. Edits JSON shape: { "perChunk": [{ "chunkIndex": 0, "transform": { "scaleX": 1.2, "translateY": 100 }, "newTextureName": "new.tif" }] }. |
| `weapons_encode_texture` | read-only | Resample a PNG image into the chosen template weapon's TIM format (matching dimensions, bpp, CLUT layout) and return the encoded .tif bytes as base64. Provide EITHER imagePath OR imageBase64 — for an image attached in chat, base64-encode it and pass via imageBase64. Use before weapons_create to preview the texture without committing. |
| `weapons_list` | read-only | List every weapon entry in a ZED archive with vertex/triangle counts and bound texture names. Use when picking a template that visually resembles a target image. |
| `weapons_thumbnail` | read-only | Render a template weapon's bound texture as a base64 PNG. Use to visually compare templates against a user-supplied weapon image. |

### Admin (26)

Settings, raw DB queries, agent profiles, orchestration plans, navigation history. Never in default profiles.

| Tool | Risk | Description |
|------|------|-------------|
| `clear_nav_history` | database | Clear the navigation history. |
| `create_agent_profile` | database | Create a new AI agent profile. All Markdown fields optional. |
| `create_orchestration_plan` | database | Create a new orchestration plan. All Markdown fields optional. |
| `delete_agent_profile` | database | Permanently delete an agent profile by ID (irreversible). |
| `delete_orchestration_plan` | database | Permanently delete an orchestration plan (irreversible). |
| `delete_progress_report` | database | Delete a progress report by name. Use only when fully complete. |
| `delete_setting` | database | Delete a CodeDesigner setting by key. |
| `export_orchestration_plan` | disk | Export an orchestration plan to a Markdown file with YAML front-matter. |
| `get_active_orchestration_plan` | read-only | Get the active orchestration plan: live stage (current/next step) + all its Markdown fields. |
| `get_address_snapshot_history` | read-only | Read the 4-byte value at an EE address across every memory snapshot in a directory. |
| `get_agent_memories` | read-only | List memory entries scoped to a specific agent (private memories). |
| `get_agent_profile` | read-only | Get full details (Markdown fields) of an agent profile by ID. |
| `get_all_settings` | read-only | List all settings, optionally filtered by key prefix. |
| `get_nav_history` | read-only | Get the navigation history (addresses visited via navigate_to). |
| `get_orchestration_plan` | read-only | Get a specific orchestration plan by ID. |
| `get_setting` | read-only | Read a single CodeDesigner setting by key. |
| `get_shared_memories` | read-only | List shared memory entries (not scoped to any specific agent). |
| `import_orchestration_plan` | database | Import a plan from a Markdown file (with optional YAML front-matter). Updates if name exists. |
| `list_agent_profiles` | read-only | List all AI agent profiles in the database. |
| `list_orchestration_plans` | read-only | List all orchestration plans (active marked). |
| `navigate_to` | database | Record a navigation step to an address (for the nav history). |
| `query_db` | dangerous | Run arbitrary SQL against the CodeDesigner DB. For LABELS prefer the label tools (label_search / label_search_sets / label_list / get_functions) — they are cheaper and indexed; use query_db only for data with no dedicated tool. |
| `set_active_orchestration_plan` | database | Mark an orchestration plan as active (deactivates the previously active one). |
| `set_setting` | database | Set a CodeDesigner setting key/value. |
| `update_agent_profile` | database | Update fields of an existing agent profile (null = unchanged). |
| `update_orchestration_plan` | database | Update fields of an existing orchestration plan (null = unchanged). |

---

## Example workflows

### Find and label a value

```
ps2_scan value=100 type=u32                             # player health at full   (Ps2Emulator)
                                                          # take damage in game, then narrow:
ps2_scan scan=<id> compare=decreased
ps2_find_pointers target=<hit>                           # a chain that survives restarts
label_add address=<hit> label=player_health              # (Code Designer)
progress_save name=socom2-player-health content=<markdown>
```

### Read code around a hit

```
ps2_build_index                                # once per session      (Ps2Emulator)
ps2_functions containing=<address>             # boundaries + name
ps2_disassemble address=<start> count=32       # with state hints
ps2_xrefs address=<start>                      # callers, branches, loads/stores, pointers
ps2_set_breakpoint address=<start>
ps2_wait_for_pause                             # trigger it in game
ps2_registers
ps2_stack
label_add_batch …                              # name what you found   (Code Designer)
```

### Apply a CDS patch

```
validate_cds sourceCode=...                    # (Code Designer)
compile_cds sourceCode=...                     # the raw codes, plus assert/breakpoint/watchpoint sidecars
ps2_read_values addresses=[…]                  # check the asserts      (Ps2Emulator)
ps2_set_patch name=<patch> codes=<raw codes>   # applied every field
```

The `cd-cds-patch` skill walks through it.

### Drive the game

`ps2_press` (a button for some fields) or `ps2_hold` / `ps2_release`, `ps2_frame_advance` to let it take effect, and `ps2_screenshot` to see the result. To build a button activator ("joker") code for a cheat, use the Controller page in VS Code (see [VS Code Extension](vscode-extension.md#controller)).

### Capture packets

```
ps2_network                                    # adapter and address    (Ps2Emulator, started with --network)
ps2_capture seconds=10 port=10075 bytes=true   # while the user plays
net_disassemble frameBase64=<frame>            # SCERT/Medius layers    (Code Designer)
medius_decode …                                # one Medius message
```

`cd_tool_help <tool>` gives each tool's full parameter schema.

---

## Multi-agent sessions

Sub-agents are separate `CodeDesigner.AI` processes that share the same database. The server never spawns processes; the host's own sub-agent feature does that, passing `--agent-id <n>` to each child.

| Need | Tools |
|---|---|
| Register / end a session | `cd_session_begin`, `cd_session_end` |
| See the team | `cd_active_sessions`, `cd_session_status` |
| Message another agent | `cd_send_message`, `cd_inbox_pop` |
| Follow the active plan | `cd_orch_current_step`, `cd_orch_advance` |
| Manage personas and plans (Admin) | `list_agent_profiles`, `get_agent_profile`, `create/update/delete_agent_profile`, `list/get/create/update/delete_orchestration_plan`, `set_active_orchestration_plan` |

The default team (the *[Fatality]* personas) and its plans are kept as Markdown in [`agent-profiles/`](../agent-profiles/README.md), with notes on loading them into the `McpAgents` and `McpOrchestrations` tables. The AI Config page in VS Code edits the same rows. [Architecture §4](ARCHITECTURE.md#4-multi-agent-coordination) explains how coordination works.

Reusable recipes for common jobs live as Agent Skills in [`.github/skills/`](../.github/skills/README.md).

---

## Progress reports

Progress reports are the persistent memory between sessions, stored in the `McpMemories` table. Use `progress_search` / `progress_get` to resume and `progress_save` at each milestone. A useful report has the game and serial, the goal, every confirmed address with its label, structure notes, working codes, and the next steps.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `cd_status` says the bridge is offline | Start the Bridge (the extension starts it with Run, or its *CodeDesigner* terminal). It listens on `localhost:8765`. |
| `ps2_*` tools cannot reach the emulator | Run the game (the panel's **Run**, or `ps2 … --debug-server 28100`); `ps2 mcp --attach <address>` if it listens elsewhere. |
| A tool you expect is missing | It isn't in your profile. `cd_list_domains`, then `cd_enable_domain`, or relaunch with a wider `--profile`. A risk cap (`--readonly`) can still hide it. |
| `CodeDesigner.AI not found` in VS Code | Build `CodeDesigner.AI` so it deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. |
| Database errors at startup | LocalDB isn't running. `sqllocaldb start MSSQLLocalDB`, or pass `--connection-string`. |
