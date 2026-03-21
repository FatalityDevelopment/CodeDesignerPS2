# CodeDesigner Installer

This folder contains two PowerShell scripts that automate setup for end-users and
developers.

---

## `Install-CodeDesigner.ps1` — end-user installer

Run this script on a fresh machine to get CodeDesigner working in minutes.

### What it does

1. **Workspace folder** — asks where to place `emu-settings.json`.
2. **CodeDesigner binary folder** — path to the compiled `CodeDesigner.Ps2.Bridge.exe`.
3. **PCSX2 binary folder** *(optional)* — path to a PCSX2 build with the CodeDesigner
   PINE server patch.
4. **BIOS file(s)** — one or more PS2 BIOS dumps (required for the built-in emulator).
5. **Game ISO(s)** — `.iso` / `.zip` paths or a folder to scan.
6. **Writes `emu-settings.json`** into the chosen workspace folder.
7. **Installs the `.vsix` extension** into VS Code via `code --install-extension`.

### Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10/11 | Script uses PowerShell 5.1+ |
| [Visual Studio Code](https://code.visualstudio.com/) | `code` must be on the PATH |
| `.NET 8 Runtime` | [Download](https://dotnet.microsoft.com/download) |
| PS2 BIOS dump | Legally obtained from your own PS2 console |
| CodeDesigner binaries | Build yourself or obtain from a release |

### Running the installer

```powershell
# Right-click the script → Run with PowerShell
# — or —
# Open PowerShell and run:
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Install-CodeDesigner.ps1
```

#### Unattended / scripted install

You can pass all values as parameters to skip the interactive prompts:

```powershell
.\Install-CodeDesigner.ps1 `
    -CodeDesignerBinDir "C:\Tools\CodeDesigner\bin" `
    -Pcsx2BinDir        "C:\Tools\pcsx2\bin" `
    -BiosPaths          @("C:\PS2\bios\SCPH-70012.bin") `
    -GamePaths          @("C:\PS2\isos") `
    -WorkspaceDir       "C:\MyProject" `
    -VsixPath           "C:\Tools\CodeDesigner\codedesigner.vsix"
```

> **Tip:** Pass a folder path to `-GamePaths` and every `.iso` / `.zip` inside it
> will be added automatically.

---

## `Build-Artifacts.ps1` — developer build helper

Developers use this script to compile everything from source and produce a
distributable artifact folder that `Install-CodeDesigner.ps1` can consume.

### What it does

1. **Builds the .NET solution** (`dotnet build`) in Release mode.
2. **Copies the Bridge binaries** to `<OutputDir>/CodeDesigner/`.
3. **Packages the VS Code extension** (`npm install` + `vsce package`) to
   `<OutputDir>/codedesigner.vsix`.
4. *(Optional)* **Builds PCSX2** from source and copies binaries to
   `<OutputDir>/PCSX2/`.
5. **Prints the exact `Install-CodeDesigner.ps1` command** you can share with
   end-users.

### Prerequisites

| Requirement | Notes |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | `dotnet` on PATH |
| [Node.js](https://nodejs.org/) | `node` + `npm` on PATH |
| For PCSX2 build: CMake + MSVC | [CMake](https://cmake.org/download/) |

### Running the build script

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Artifacts.ps1
```

#### Fully specified (CI-friendly)

```powershell
.\Build-Artifacts.ps1 `
    -RepoRoot      "C:\Dev\CodeDesigner" `
    -Pcsx2RepoRoot "C:\Dev\pcsx2" `
    -OutputDir     "C:\Build\CodeDesignerDist"
```

After the build, the script prints the install command you can share with users:

```
.\Install-CodeDesigner.ps1 `
    -CodeDesignerBinDir "C:\Build\CodeDesignerDist\CodeDesigner" `
    -Pcsx2BinDir        "C:\Build\CodeDesignerDist\PCSX2" `
    -VsixPath           "C:\Build\CodeDesignerDist\codedesigner.vsix"
```

---

## Typical first-time setup flow

```
Developer machine                    End-user machine
────────────────                     ────────────────
1. git clone <repo>
2. .\installer\Build-Artifacts.ps1
   → produces dist/ folder
3. Share dist/ folder              → 4. .\Install-CodeDesigner.ps1
                                          (prompts for BIOS & games)
                                       5. Open VS Code workspace
                                       6. Click "Connect" in the sidebar
```

---

## `emu-settings.json` reference

The installer generates this file.  You can also edit it by hand at any time —
VS Code reloads it automatically when the panel is opened.

```jsonc
{
  "bios": [
    { "name": "SCPH-70012 USA v12.0", "path": "C:\\PS2\\bios\\SCPH-70012.bin" }
  ],
  "games": [
    { "name": "My Game", "path": "C:\\PS2\\isos\\mygame.iso" }
  ],
  "emulators": [
    { "type": "code-designer", "path": "C:\\Tools\\CodeDesigner\\CodeDesigner.Ps2.Bridge.exe" },
    { "type": "pcsx2",         "path": "C:\\Tools\\pcsx2\\bin\\pcsx2-qtx64.exe" }
  ],
  "pcsx2": {
    "host": "localhost",
    "slot": 28011
  }
}
```

| Field | Description |
|---|---|
| `bios[].name` | Display name shown in the Settings tab BIOS dropdown |
| `bios[].path` | Absolute path to the PS2 BIOS dump (`.bin` is typical; other extensions are accepted) |
| `games[].name` | Display name shown in the Settings tab game dropdown |
| `games[].path` | Absolute path to an `.iso` or `.zip` disc image |
| `emulators[].type` | `"code-designer"` or `"pcsx2"` |
| `emulators[].path` | Absolute path to the executable |
| `pcsx2.host` | Hostname for the PINE connection (default: `localhost`) |
| `pcsx2.slot` | PINE slot number (default: `28011`) |
