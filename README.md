# CodeDesigner — PS2 Emotion Engine Toolset

CodeDesigner is a complete toolset for the Sony PlayStation 2 **Emotion Engine (EE)** MIPS R5900 processor. It lets you assemble, disassemble, debug, and modify PS2 games live — from a VS Code sidebar connected to a real emulator or hardware.

---

## What it does

| Capability | Description |
|---|---|
| **Assembler / Disassembler** | Full EE MIPS R5900 instruction set, including MMI, COP1 (FPU), COP2 (VU0 macro mode), and VU1 micro instructions |
| **Live Disassembler** | Scrollable disassembly view over live EE memory; auto-updates on pause, supports breakpoints, single-step, and PC tracking |
| **Emulator** | Built-in dual-CPU PS2 emulator (EE + IOP) with full hardware peripherals; boots a real BIOS and loads game discs |
| **PCSX2 Bridge** | Connect to a running PCSX2 process via the PINE protocol for real-time memory inspection and patching |
| **CDS Language** | High-level patch scripting language that compiles to MIPS assembly, GameShark codes, or cheat-engine table entries |
| **Cheat Engine** | Apply one-shot cheat codes or inject a persistent frame-loop routine that keeps re-applying cheats every frame |
| **Static Recompiler** | Translate PS2 ELF binaries to portable C++ source |
| **Memory Card Manager** | Read, write, and export PS2 memory card saves |
| **HDD Image Manager** | Browse and extract PS2 HDD partition images |
| **MCP Server** | GitHub Copilot / AI agent integration via the Model Context Protocol |
| **Multiplayer Server** | Online infrastructure for PS2 LAN/online games |

---

## Documentation

See the **[docs/ catalog](docs/README.md)** for all guides and project references.

---

## Quick start

```powershell
# Clone with submodules (pcsx2, CheatCodeArchive)
git clone --recurse-submodules https://github.com/TylerDev888/CodeDesigner.git
cd CodeDesigner

# Build
dotnet build CodeDesigner.sln

# Run tests
dotnet test CodeDesigner.Ps2.TestProject/

# Debug the VS Code extension
# Open vscode/codedesigner in VS Code and press F5
```

---

## Connecting to PCSX2

1. Start PCSX2 with the PINE socket enabled.
2. In the VS Code sidebar open **Emulator Settings**, select **PCSX2**, and click **Connect**.
3. The live disassembler view opens automatically on the first pause.

## Connecting the MCP server to GitHub Copilot

1. Install the GitHub Copilot extension in VS Code.
2. Add an entry to `%APPDATA%\Code\User\mcp.json` (replace `<PATH>` with the repo root):

```json
{
  "servers": {
    "CodeDesigner": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--no-build", "--project", "<PATH>\\CodeDesigner.Ps2.McpServer"]
    }
  }
}
```

3. Restart VS Code.
