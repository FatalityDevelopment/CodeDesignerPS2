# CodeDesigner — PS2 Emotion Engine Toolset

CodeDesigner is a complete C# toolset for the Sony PlayStation 2 **Emotion Engine (EE)** MIPS R5900 processor. It provides an assembler, disassembler, instruction encoder/decoder, full emulator, cheat engine, code-injection scripting language, static recompiler, memory-card manager, HDD image manager, and a socket server that connects all of this to the VS Code extension and GitHub Copilot via MCP.

---

## Dev environment

Clone repository and sub-repositories(i.e pcsx2 and CheatCodeArchive)
```powershell
git clone --recurse-submodules https://github.com/TylerDev888/CodeDesigner.git
```

#### Download pcsx2 dependencies
```powershell
cd CodeDesigner

Invoke-WebRequest "https://github.com/PCSX2/pcsx2-windows-dependencies/releases/download/latest-windows-dependencies/pcsx2-windows-dependencies.7z" -OutFile deps.7z; New-Item -ItemType Directory -Force pcsx2 | Out-Null; & "C:\Program Files\7-Zip\7z.exe" x deps.7z -o"pcsx2" -y; Remove-Item deps.7z
```

#### Build Code Designer
```
1. Open CodDesigner folder and open CodeDesigner.sln
2. Build CodeDesigner.sln
3. Build CodeDesigner.Ps2.Mcp
```

#### Build Pcsx2
```
1. Open pcsx2 folder and open PCSX2_qt.sln
2. build pcsx2-qt
```

#### Debug Extension
```
1. open CodeDesigner\vscode\codedesigner in vs code
2. press F5 to debug extension
```

#### Connect MCP server to co pilot
```
1. make sure the copilot extension is installed in vs code
2. located this folder "C:\Users\tholu\AppData\Roaming\Code\User\mcp.json", you should replace "tholu" with your setup.
3. add the following, update the "ADD_PATH_HERE" placeholder and save
```

```json
{
	"servers": {
		"CodeDesigner": {
			"type": "stdio",
			"command": "dotnet",
			"args": [
				"run",
				"--no-build",
				"--project",
				"ADD_PATH_HERE\\CodeDesigner\\CodeDesigner.Ps2.McpServer"
			]
		}
	},
	"inputs": []
}

```

```
4. Now restart vs code
```
