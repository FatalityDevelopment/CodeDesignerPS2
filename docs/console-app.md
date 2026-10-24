[← Back to Documentation Index](README.md)

# Command-line tools

CodeDesigner's backend is two executables: **`CodeDesigner.Ps2.Bridge`**, the control server everything talks to, and **`CodeDesigner.Ps2.Emulator`**, the built-in PS2 emulator. Building the solution deploys both to `%LOCALAPPDATA%\CodeDesigner\`.

You rarely need to run them by hand: the VS Code extension starts the Bridge in its *CodeDesigner* terminal and launches the emulator when you click **Run**. This page is for scripting, debugging the backend, or running headless.

---

## CodeDesigner.Ps2.Bridge

```
CodeDesigner.Ps2.Bridge.exe [mode] [options]
```

| Mode | Command | What it does |
|---|---|---|
| Control server | *(no flags)* | Listens on `8765` for JSON commands. Attach an emulator later with the `STARTPCSX2`, `STARTEMULATOR` or `STARTFILEMODE` commands. This is what the extension runs. |
| PCSX2 bridge | `--pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011] [--port 8765]` | Connects to a running PCSX2 over PINE first, then serves on `8765`. Exits if PCSX2 isn't reachable. |
| Recompiler | `--recompile --elf <game.elf> [--out recomp_out] [--single-file] [--ghidra <map>] [--patch-cop0] [--stub <fn>]… [--skip <fn>]…` | Statically recompiles an ELF to C++ and exits. See [Recompiler](recompiler.md). |
| Help | `--help` | Prints usage. |

Besides the JSON socket on `8765`, the Bridge serves the GS stream (Preview frames) on `8766`. Stop it with `Ctrl+C`.

Startup applies database migrations to LocalDB (`(localdb)\MSSQLLocalDB`, database `CodeDesigner`). If LocalDB is stopped, start it with `sqllocaldb start MSSQLLocalDB`.

---

## CodeDesigner.Ps2.Emulator

The built-in emulator runs as its own process and exposes a PINE-compatible socket that the Bridge connects to, the same way it connects to PCSX2.

```
CodeDesigner.Ps2.Emulator.exe [--bios <bios.bin>] [--disc <game.iso|zip>] [--steps N] [--port 28012]
CodeDesigner.Ps2.Emulator.exe --file <game.elf|bin> [--load-address 0xN] [--port 28012]
CodeDesigner.Ps2.Emulator.exe --bare [--port 28012]
```

| Mode | What it does |
|---|---|
| BIOS + disc | Boots the BIOS (and the disc, if given; `.zip` images are extracted to a temp folder and cached). Starts paused at the reset vector. `--steps` caps the number of EE steps. |
| File | Loads an ELF or raw binary into a bare machine and pauses at its entry point (or `--load-address` for raw files). |
| Bare | No BIOS or file; waits for the Bridge to load code over the socket. |

The default port is `28012` (PCSX2 uses `28011`).

---

## Talking to the Bridge

Send one JSON object per line to `localhost:8765`; each gets one JSON response:

```json
{"Command":"PING"}
{"Command":"READMEM","Address":1048576,"Length":16}
{"Command":"PINEGETAUDIOSTATE"}
```

From PowerShell:

```powershell
$c = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 8765)
$s = $c.GetStream(); $w = [IO.StreamWriter]::new($s); $r = [IO.StreamReader]::new($s)
$w.WriteLine('{"Command":"PING"}'); $w.Flush(); $r.ReadLine()
```

The full command list is in [Socket Communication](socket-communication.md). For AI clients, use the [MCP server](mcp-server.md) instead of raw commands.
