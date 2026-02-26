# CodeDesigner Extension — Developer Quick Start

## Project Structure

```
vscode/codedesigner/
├── src/
│   ├── extension.ts        # Extension entry point; registers commands and panels
│   ├── socketClient.ts     # TCP socket client connecting to the C# backend (port 8765)
│   ├── assemblerPanel.ts   # WebviewPanel for the assembler UI
│   └── disassemblerPanel.ts # WebviewPanel for the disassembler UI
├── package.json            # Extension manifest (commands, activation events, contributes)
├── tsconfig.json           # TypeScript configuration
└── README.md               # User-facing documentation
```

## Build

```bash
cd vscode/codedesigner
npm install
npm run compile
```

## Run / Debug

1. Start the C# backend:
   ```bash
   cd CodeDesigner.Mips64LE
   dotnet run -- --server
   ```
2. In VS Code, press **F5** to launch the Extension Development Host.
3. Open the Command Palette and run `CodeDesigner: Connect to Server`.

## Extending the Extension

- Add new socket commands in `socketClient.ts` by handling new `command` values in `sendCommand()`.
- Add new UI panels by following the pattern in `assemblerPanel.ts` / `disassemblerPanel.ts`.
- The JSON protocol is documented in `SOCKET_COMMUNICATION.md` at the repository root.
