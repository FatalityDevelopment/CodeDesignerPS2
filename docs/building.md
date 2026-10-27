[← Back to Documentation Index](README.md)

# Building CodeDesigner

This guide covers building the C# backend, the VS Code extension and the PCSX2 fork from source, and running the tests.

> **Fastest route:** run `CodeDesignerInstaller.exe` from the [latest release](https://github.com/FatalityDevelopment/CodeDesignerPS2/releases/latest) and choose **Developer setup**. It installs the prerequisites below, clones the repository, builds it and installs the extension. In an existing checkout, the same steps are scripts:
>
> ```powershell
> .\CodeDesigner.Ps2.Installer\Scripts\Install-Prerequisites.ps1 -Mode Developer
> .\CodeDesigner.Ps2.Installer\Scripts\Setup-DevEnvironment.ps1 -RepoDir .
> ```

---

## Prerequisites

| Requirement | Version | Purpose |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 | All C# projects |
| SQL Server **LocalDB** | any recent (ships with Visual Studio) | The CodeDesigner database (`(localdb)\MSSQLLocalDB`) |
| [Node.js](https://nodejs.org/) + npm | 20 or later | VS Code extension |
| [Git](https://git-scm.com/) | any | Clone with submodules |
| Visual Studio 2022+ with C++ | optional | Building the PCSX2 fork |

---

## Clone

The repository uses submodules for the PCSX2 fork and the CheatCodeArchive:

```powershell
git clone --recurse-submodules https://github.com/FatalityDevelopment/CodeDesignerPS2.git
cd CodeDesignerPS2
```

If you cloned without them: `git submodule update --init --recursive`.

---

## Build the C# projects

```powershell
dotnet build CodeDesigner.sln
```

See [Projects](projects.md) for what each project is.

### What a build also does

- **Deploys.** `CodeDesigner.Ps2.Bridge` and `CodeDesigner.Ps2.Emulator` copy their output to `%LOCALAPPDATA%\CodeDesigner\` (`~/.local/share/CodeDesigner/` elsewhere). `CodeDesigner.AI` deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. The VS Code extension runs the binaries from there.
- **Migrates the database.** Building `CodeDesigner.Ps2.Database` (and anything that references it) runs its migrations against LocalDB. Pass `-p:SkipDatabaseInit=true` to skip that, for example in a side build.

> If the Bridge is running, the deploy copy can fail on locked files. Stop it first (it runs in the extension's *CodeDesigner* terminal), build, then reload the VS Code window so the extension starts the new build.

### Build one project

```powershell
dotnet build CodeDesigner.Ps2.Bridge/CodeDesigner.Ps2.Bridge.csproj
dotnet build CodeDesigner.AI/CodeDesigner.AI.csproj
```

---

## Run the tests

```powershell
dotnet test CodeDesigner.Ps2.TestProject/    # ~1,400 tests: ISA, emulator, CDS, cheats, file formats, bridge, multiplayer
dotnet test CodeDesigner.AI.Tests/           # ~120 tests: MCP profiles, tools, coordination
```

See [Test Project](test-project.md).

---

## Run the backend by hand

The VS Code extension normally starts everything. To run pieces yourself:

```powershell
# Control server on :8765 (attach later with STARTPCSX2 / STARTEMULATOR / STARTFILEMODE)
CodeDesigner.Ps2.Bridge.exe

# Bridge attached to a running PCSX2 over PINE
CodeDesigner.Ps2.Bridge.exe --pcsx2 [--pcsx2-slot 28011] [--pcsx2-host 127.0.0.1] [--port 8765]

# Static recompiler
CodeDesigner.Ps2.Bridge.exe --recompile --elf game.elf [--out recomp_out] [--single-file]

# Built-in emulator (its own process; the bridge talks to it on :28012)
CodeDesigner.Ps2.Emulator.exe --bios SCPH-70012.bin [--disc game.iso] [--port 28012]
CodeDesigner.Ps2.Emulator.exe --file game.elf [--load-address 0x00100000]
```

`--help` on either exe prints the full usage. The socket protocol is described in [Socket Communication](socket-communication.md).

---

## Build the VS Code extension

The extension lives in `vscode/codedesigner/` (TypeScript host + Angular webviews, bundled with webpack).

```powershell
cd vscode/codedesigner
npm ci
npm run compile     # development build into dist/
npm run watch       # rebuild on change
npm run package     # production build
npm run lint
```

To debug it, open `vscode/codedesigner` in VS Code and press **F5**. An Extension Development Host opens with the extension loaded.

To install it into your normal VS Code:

```powershell
npx @vscode/vsce package --out ../../publish/codedesigner.vsix
code --install-extension ../../publish/codedesigner.vsix
```

---

## Build the PCSX2 fork

`pcsx2/` is CodeDesigner's PCSX2 fork. It adds the PINE debugger opcodes (breakpoints, stepping, symbols, pad, DEV9, GS stream, audio; see [PINE debugger extension](pine-debugger-extension.md)) and the GS stream server the Preview window uses. Stock PCSX2 works for basic memory access, but those features need the fork.

Open `pcsx2/PCSX2_qt.sln` in Visual Studio and build `pcsx2-qt` (x64). Full instructions: [PCSX2 Building](pcsx2-building.md).

---

## CI

`.github/workflows/build.yml` restores and builds the .NET projects in Release, runs `CodeDesigner.Ps2.TestProject`, packages the release assets (below) and builds the PCSX2 fork. Every run uploads the assets as the `CodeDesigner` and `PCSX2` workflow artifacts.

---

## Releases

Push a tag to publish a release:

```powershell
git tag v1.5.0
git push origin v1.5.0
```

The workflow's `release` job attaches these to a GitHub release with generated notes:

| Asset | Contents | Installed to |
|---|---|---|
| `CodeDesigner.zip` | Bridge, emulator, MCP server, database tool, AI, Multiplayer | `%LOCALAPPDATA%\CodeDesigner\` |
| `PCSX2.zip` | The PCSX2 fork (Release AVX2) | `%LOCALAPPDATA%\CodeDesigner\pcsx2\` |
| `CheatCodeArchive.zip` | `CheatCodeArchive/Database/` | `%LOCALAPPDATA%\CodeDesigner\CheatCodeArchive\`, then imported |
| `codedesigner.vsix` | The VS Code extension | VS Code |
| `CodeDesignerInstaller.exe` | Self-contained installer, pinned to the tag | |

To build the same assets locally (everything except `PCSX2.zip`; pass `-Pcsx2BinDir` to include it):

```powershell
pwsh tools/scripts/Build-Release.ps1 -Version v1.5.0     # output in dist/
```

`CodeDesigner.zip` is made by the projects' own deploy targets, pointed at a staging folder with `-p:CodeDesignerInstallDir=<dir>`, so it always matches what a local `dotnet build` deploys.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Submodule errors | `git submodule update --init --recursive` |
| Build hangs or fails in `InitialiseDatabase` | LocalDB isn't healthy. `sqllocaldb stop MSSQLLocalDB`, `sqllocaldb start MSSQLLocalDB`, then rebuild. Or build with `-p:SkipDatabaseInit=true`. |
| Deploy step fails with "file in use" | The Bridge or emulator is running from `%LOCALAPPDATA%\CodeDesigner\`. Stop it and rebuild. |
| npm build fails | Delete `vscode/codedesigner/node_modules` and run `npm ci` again. |
| "CodeDesigner.AI not found" in VS Code | Build `CodeDesigner.AI` so it deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. |
| PCSX2 PINE connection refused | In PCSX2, **Settings → Advanced → Enable PINE**. The default slot is `28011`. |
| BIOS fails to load | Use a raw PS2 BIOS dump (e.g. `SCPH-70012.bin`); compressed images aren't supported. |
