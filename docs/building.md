[← Back to Documentation Index](README.md)

# Building CodeDesigner

This guide explains how to build all components of the CodeDesigner solution from source.

---

## Prerequisites

| Requirement | Version | Purpose |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 or later | All C# projects |
| [Node.js](https://nodejs.org/) | 20 or later | VS Code extension |
| [npm](https://www.npmjs.com/) | bundled with Node.js | Extension dependencies |
| [Git](https://git-scm.com/) | any | Clone with submodules |

Optional (for VS Code extension development):
- [Visual Studio Code](https://code.visualstudio.com/)
- [@vscode/vsce](https://github.com/microsoft/vscode-vsce) — to package the extension as a `.vsix`

---

## Clone

The repository uses Git submodules for PCSX2 and the CheatCodeArchive. Clone with:

```powershell
git clone --recurse-submodules https://github.com/TylerDev888/CodeDesigner.git
cd CodeDesigner
```

If you already cloned without `--recurse-submodules`:

```powershell
git submodule update --init --recursive
```

---

## Solution structure

| Project | Type | Description |
|---|---|---|
| `CodeDesigner.Ps2.Bridge` | Executable | Socket server, emulator host, PCSX2 bridge |
| `CodeDesigner.Ps2.Common` | Library | MIPS R5900 ISA, assembler, disassembler |
| `CodeDesigner.Ps2.EmulatorCore` | Library | Full PS2 hardware emulation (EE + IOP) |
| `CodeDesigner.Ps2.CheatEngine` | Library | Cheat application, cheat engine loop |
| `CodeDesigner.Ps2.CdsLanguage` | Library | CDS script compiler and parser |
| `CodeDesigner.Ps2.LabelManager` | Library | Label set management and storage |
| `CodeDesigner.Ps2.Database` | Executable/Library | SQLite schema and persistence layer |
| `CodeDesigner.Ps2.MCManager` | Library | PS2 memory card emulation |
| `CodeDesigner.Ps2.HddManager` | Library | PS2 HDD image manager |
| `CodeDesigner.Ps2.IsoManager` | Library | PS2 ISO/disc image reader |
| `CodeDesigner.Ps2.Recompiler` | Library | Static EE→C++ recompiler |
| `CodeDesigner.Ps2.McpServer` | Executable | MCP server for AI assistant integration |
| `CodeDesigner.Ps2.Multiplayer` | Executable | Online server emulator (Medius + DNAS) |
| `CodeDesigner.Ps2.WebApi` | Executable | REST API server |
| `CodeDesigner.Ps2.TestProject` | Test | Unit test suite (1000+ tests) |
| `vscode/codedesigner` | VS Code extension | Frontend panel (TypeScript + Angular) |

---

## Build all C# projects

From the repository root:

```powershell
dotnet build CodeDesigner.sln
```

For a release build:

```powershell
dotnet build CodeDesigner.sln -c Release
```

### Build a single project

```powershell
dotnet build CodeDesigner.Ps2.Bridge/CodeDesigner.Ps2.Bridge.csproj
```

---

## Run tests

```powershell
dotnet test CodeDesigner.Ps2.TestProject/CodeDesigner.Ps2.TestProject.csproj --verbosity normal
```

---

## Run the backend

```powershell
# From the Bridge project directory
cd CodeDesigner.Ps2.Bridge

# Control server only (mode selected at runtime via socket commands)
dotnet run

# Built-in emulator with BIOS + disc
dotnet run -- --run --bios "C:\path\to\SCPH10000.BIN" --disc "C:\path\to\game.iso"

# PCSX2 PINE bridge (PCSX2 must be running)
dotnet run -- --pcsx2

# Load a PS2 ELF directly (no BIOS required)
dotnet run -- --file game.elf
```

---

## Build the VS Code extension

The extension is in `vscode/codedesigner/`.

### Install dependencies

```powershell
cd vscode/codedesigner
npm ci
```

### Development build (watch mode)

Open the repository root in VS Code and press **F5**. This runs the `build-extension` pre-launch task and opens an Extension Development Host window with the extension loaded.

Alternatively, build manually:

```powershell
npm run build
```

### Production build

```powershell
npm run build-prod
```

Output goes to `vscode/codedesigner/dist/`.

### Package as VSIX

```powershell
npx @vscode/vsce package --out ../../publish/codedesigner.vsix
```

Install the VSIX in VS Code:

```powershell
code --install-extension publish/codedesigner.vsix
```

---

## Publish / install

To publish a full release (mirrors the CI pipeline):

```powershell
# Publish all C# projects
dotnet publish CodeDesigner.Ps2.Bridge/CodeDesigner.Ps2.Bridge.csproj -c Release -o publish/Bridge
dotnet publish CodeDesigner.Ps2.Multiplayer/CodeDesigner.Ps2.Multiplayer.csproj -c Release -o publish/Multiplayer
dotnet publish CodeDesigner.Ps2.McpServer/CodeDesigner.Ps2.McpServer.csproj -c Release -o publish/McpServer

# Package the VS Code extension
cd vscode/codedesigner
npx @vscode/vsce package --out ../../publish/codedesigner.vsix
```

The CodeDesigner installer copies the published Bridge binaries to `%LOCALAPPDATA%\CodeDesigner\` on Windows (`~/.local/share/CodeDesigner/` on Linux/macOS).

---

## CI / GitHub Actions

The repository includes a GitHub Actions workflow at `.github/workflows/build.yml` that:

1. Restores and builds all C# projects.
2. Runs the test suite.
3. Publishes all C# projects.
4. Packages the VS Code extension as a `.vsix`.
5. Uploads all artifacts as a `CodeDesigner` build artifact.

A separate `build-pcsx2` job builds the PCSX2 submodule on Windows.

---

## Troubleshooting

**`git submodule` errors** — run `git submodule update --init --recursive` to initialise missing submodules.

**npm build fails** — delete `vscode/codedesigner/node_modules` and re-run `npm ci`.

**Extension shows "Help file not found"** — the extension walks up from its install directory to find `docs/README.md`. When running under F5, make sure the repository root is the workspace folder or a workspace folder parent.

**PCSX2 PINE connection refused** — in PCSX2 go to **Settings → Advanced** and enable the PINE socket server. Default slot is `28011`.

**BIOS load error** — the BIOS file must be a raw PS2 BIOS dump (e.g. `SCPH10000.BIN`). Compressed or encrypted BIOS images are not supported.
