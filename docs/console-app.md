[← Back to Documentation Index](README.md)

# Command-line tools

CodeDesigner's backend is **`CodeDesigner.Ps2.Bridge`**, the workspace service everything talks to. Building the solution deploys it to `%LOCALAPPDATA%\CodeDesigner\`.

You rarely need to run it by hand: the VS Code extension starts the Bridge in its *CodeDesigner* terminal, and Ps2Emulator (see [Emulator](emulator.md)) when you click **Run**. This page is for scripting, debugging the backend, or running headless.

---

## CodeDesigner.Ps2.Bridge

```
CodeDesigner.Ps2.Bridge.exe [mode] [options]
```

| Mode | Command | What it does |
|---|---|---|
| Workspace service | *(no flags)* | Listens on `8765` for JSON commands. This is what the extension runs. |
| Recompiler | `--recompile --elf <game.elf> [--out recomp_out] [--single-file] [--ghidra <map>] [--patch-cop0] [--stub <fn>]… [--skip <fn>]…` | Statically recompiles an ELF to C++ and exits. See [Recompiler](recompiler.md). |
| Help | `--help` | Prints usage. |

Stop it with `Ctrl+C`.

Startup applies database migrations to LocalDB (`(localdb)\MSSQLLocalDB`, database `CodeDesigner`). If LocalDB is stopped, start it with `sqllocaldb start MSSQLLocalDB`.

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
