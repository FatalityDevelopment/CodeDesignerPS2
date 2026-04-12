# CodeDesigner Installer

Three components — a **GUI installer exe**, and two PowerShell scripts — one for
**developers** who build the package, one for **end users** who just want to
install it.

---

## GUI Installer — `CodeDesigner.Ps2.Installer.exe`

A modern dark-themed WPF application that discovers and runs PowerShell scripts
from a folder.  It is shipped alongside the PowerShell scripts so end users get
a familiar installer experience instead of needing to right-click a `.ps1` file.

### How it works

1. On launch, the installer looks for a `scripts` subfolder next to the exe.
2. It lists all `*.ps1` files found, sorted alphabetically.
3. The user clicks **Start** and each script is executed in order via
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>`.
4. Real-time output is streamed into a log panel with a progress bar.
5. A final summary screen shows which scripts succeeded or failed.

### Naming convention for scripts

Prefix script filenames with numbers to control execution order:

```
scripts\
    01-Install-CodeDesigner.ps1
    02-Configure-Settings.ps1
    03-Install-Extension.ps1
```

### Building

```bash
cd CodeDesigner.Ps2.Installer
dotnet build -c Release
```

The output exe is at
`bin\Release\net8.0-windows\CodeDesigner.Ps2.Installer.exe`.

### Packaging for distribution

Place the exe and a `scripts\` folder together:

```
CodeDesignerSetup\
    CodeDesigner.Ps2.Installer.exe
    scripts\
        01-Install-CodeDesigner.ps1
    CodeDesigner\
        CodeDesigner.Ps2.Bridge.exe
        (supporting DLLs)
    codedesigner.vsix
```

---

## For end users — `Install-CodeDesigner.ps1`

You received a file called **`CodeDesignerSetup.zip`** from the developer.

1. **Extract** the zip anywhere (e.g. `C:\CodeDesignerSetup\`).
2. **Install prerequisites** if you have not already:
   - [Visual Studio Code](https://code.visualstudio.com/)
   - [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
     (any of: **.NET Runtime**, **.NET Desktop Runtime**, or **ASP.NET Core Runtime**)
3. **Right-click** `Install-CodeDesigner.ps1` → **Run with PowerShell**.

   If you see a security warning, run this first in a PowerShell window:
   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   ```

4. Follow the three prompts:
   - Where to create your workspace folder
   - Path to your PS2 BIOS dump file
   - Optionally, paths to your game `.iso` / `.zip` files

The script auto-detects the bundled CodeDesigner executable and `.vsix` file —
**you do not need to know anything about source code or compilers**.

### What gets installed

| Item | What happens |
|---|---|
| `emu-settings.json` | Written into your workspace folder |
| `codedesigner.vsix` | Installed into VS Code automatically |

### After install

```powershell
code "C:\<your workspace folder>"
```

The **CodeDesigner** icon appears in the VS Code sidebar.  Go to the
**Settings** tab, pick your BIOS and game, then click **Connect**.

---

## For developers — `Build-Artifacts.ps1`

Run this once on your development machine to produce a distributable zip.

### Prerequisites

| Tool | Download |
|---|---|
| .NET 8 SDK | https://dotnet.microsoft.com/download |
| Node.js + npm | https://nodejs.org/ |
| `@vscode/vsce` | installed automatically via `npx` if absent |
| CMake (for PCSX2 only) | https://cmake.org/download/ |

### Running

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Build-Artifacts.ps1
```

You will be prompted for:
1. The **CodeDesigner repository root** (the folder containing `CodeDesigner.Ps2.sln`)
2. An **output folder** where the distributable will be assembled
3. Optionally, the **PCSX2 repository root** if you want to bundle PCSX2

### What gets produced

```
<OutputDir>\
    CodeDesignerSetup\          ← extracted distributable
        Install-CodeDesigner.ps1
        codedesigner.vsix
        CodeDesigner\
            CodeDesigner.Ps2.Bridge.exe
            (supporting DLLs)
        PCSX2\                  ← only if PCSX2 was built
            pcsx2-qtx64.exe
            (supporting DLLs)
        README.txt
    CodeDesignerSetup.zip       ← send this to end users
```

### Fully scripted (CI / automated builds)

```powershell
.\Build-Artifacts.ps1 `
    -RepoRoot      "C:\Dev\CodeDesigner" `
    -Pcsx2RepoRoot "C:\Dev\pcsx2" `
    -OutputDir     "C:\Build\dist"
```

---

## End-to-end flow

```
Developer machine                   End-user machine
─────────────────                   ────────────────
1. git clone <repo>
2. .\installer\Build-Artifacts.ps1
   → produces CodeDesignerSetup.zip
3. Send the zip                  → 4. Extract zip
                                    5. Run Install-CodeDesigner.ps1
                                       (prompts: BIOS + games only)
                                    6. Open VS Code workspace
                                    7. Click Connect in sidebar
```

---

## `emu-settings.json` reference

The installer writes this file automatically.  You can also edit it by hand
at any time — VS Code reloads it when you open the panel.

```jsonc
{
  "bios": [
    { "name": "SCPH-70012 USA v12.0", "path": "C:\\PS2\\bios\\SCPH-70012.bin" }
  ],
  "games": [
    { "name": "My Game", "path": "C:\\PS2\\isos\\mygame.iso" }
  ],
  "emulators": [
    {
      "type": "code-designer",
      "path": "C:\\CodeDesignerSetup\\CodeDesigner\\CodeDesigner.Ps2.Bridge.exe"
    },
    {
      "type": "pcsx2",
      "path": "C:\\CodeDesignerSetup\\PCSX2\\pcsx2-qtx64.exe"
    }
  ],
  "pcsx2": {
    "host": "localhost",
    "slot": 28011
  }
}
```

| Field | Description |
|---|---|
| `bios[].name` | Display name in the BIOS dropdown |
| `bios[].path` | Absolute path to the PS2 BIOS dump (`.bin` or other extension) |
| `games[].name` | Display name in the game dropdown |
| `games[].path` | Absolute path to a `.iso` or `.zip` disc image |
| `emulators[].type` | `"code-designer"` or `"pcsx2"` |
| `emulators[].path` | Absolute path to the executable |
| `pcsx2.host` | PINE hostname (default: `localhost`) |
| `pcsx2.slot` | PINE slot number (default: `28011`) |
