[← Back to Documentation Index](README.md)

# Building CodeDesigner

This guide covers building the C# backend and the VS Code extension from source, and running the tests. Games run in [Ps2Emulator](emulator.md), built from its own repository.

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

---

## Clone

The repository uses submodules for the PCSX2 sources (read by the label generators) and the CheatCodeArchive:

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

- **Deploys.** `CodeDesigner.Ps2.Bridge` copies its output to `%LOCALAPPDATA%\CodeDesigner\` (`~/.local/share/CodeDesigner/` elsewhere). `CodeDesigner.AI` deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. The VS Code extension runs the binaries from there.
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
# The workspace service on :8765
CodeDesigner.Ps2.Bridge.exe

# Static recompiler
CodeDesigner.Ps2.Bridge.exe --recompile --elf game.elf [--out recomp_out] [--single-file]

# Ps2Emulator with its debug server (what Run does)
ps2 bios SCPH-70012.bin --disc game.iso --debug-server 127.0.0.1:28100 --window
```

`--help` prints the full usage. The socket protocol is described in [Socket Communication](socket-communication.md).

---

## Build cd-app

The standalone app lives in `client/cd-app/` (React, TypeScript and Vite). It's early work: so far it has the Emulator and Disassembler pages, and it attaches to a Bridge and emulator you start yourself.

```powershell
cd client/cd-app
npm ci
npm run dev         # dev server on http://localhost:5174
npm test
npm run build       # production build into dist/
```

The Bridge reaches the app over `WebGateway` on `http://localhost:8771`, with one WebSocket on `/api/bridge` that it pipes to the command port. It accepts only the app's own origin, plus the dev server's in Debug builds.

To have the Bridge serve the page, run `npm run build` first, then build the Bridge. It copies `dist/` into `wwwroot`, and the app is then at `http://localhost:8771`.

---

## Build the VS Code extension

The extension lives in `client/vscode/` (TypeScript host + Angular webviews, bundled with webpack).

```powershell
cd client/vscode
npm ci
npm run compile     # development build into dist/
npm run watch       # rebuild on change
npm run package     # production build
npm run lint
```

To debug it, open `client/vscode` in VS Code and press **F5**. An Extension Development Host opens with the extension loaded.

To install it into your normal VS Code:

```powershell
npx @vscode/vsce package --out ../../publish/codedesigner.vsix
code --install-extension ../../publish/codedesigner.vsix
```

---

## CI

`.github/workflows/build.yml` restores and builds the .NET projects in Release, runs `CodeDesigner.Ps2.TestProject`, and packages the release assets (below). Every run uploads them as the `CodeDesigner` workflow artifact.

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
| `CodeDesigner.zip` | Bridge, MCP server, database tool, AI, Multiplayer | `%LOCALAPPDATA%\CodeDesigner\` |
| `CheatCodeArchive.zip` | `CheatCodeArchive/Database/` | `%LOCALAPPDATA%\CodeDesigner\CheatCodeArchive\`, then imported |
| `codedesigner.vsix` | The VS Code extension | VS Code |
| `CodeDesignerInstaller.exe` | Self-contained installer, pinned to the tag | |

To build the same assets locally:

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
| Deploy step fails with "file in use" | The Bridge or MCP server is running from `%LOCALAPPDATA%\CodeDesigner\`. Stop it and rebuild. |
| npm build fails | Delete `client/vscode/node_modules` and run `npm ci` again. |
| "CodeDesigner.AI not found" in VS Code | Build `CodeDesigner.AI` so it deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. |
| Run says the debug server did not start | Check the *Ps2Emulator* terminal: the selected `ps2.exe`, BIOS and port (`codedesigner.ps2Emulator.debugServer`). |
| BIOS fails to load | Use a raw PS2 BIOS dump (e.g. `SCPH-70012.bin`); compressed images aren't supported. |
