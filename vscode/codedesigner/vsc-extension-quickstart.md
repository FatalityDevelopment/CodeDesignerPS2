# CodeDesigner Extension — Developer Quick Start

## Project Structure

```
vscode/codedesigner/
├── src/
│   ├── extension.ts               # Extension entry point; registers providers and commands
│   ├── socketClient.ts            # TCP socket client connecting to the C# backend (port 8765)
│   ├── providers/
│   │   ├── disasmPanel/
│   │   │   └── disasmPanelProvider.ts  # WebviewViewProvider for the Emulator sidebar panel
│   │   ├── disasmEditor/
│   │   │   └── disasmEditorProvider.ts # CustomReadonlyEditorProvider for live memory panels
│   │   └── pointerManager/
│   │       └── pointerManagerProvider.ts # TreeDataProvider for the Structs tree view
│   └── webview/
│       ├── disasm-panel/          # Angular app for the Emulator sidebar panel
│       └── disasm-editor/         # Angular app for live disassembler / binary editor panels
│           └── components/
│               └── hex-view.component.ts  # Standalone hex dump component
├── package.json                   # Extension manifest (commands, activation events, contributes)
├── tsconfig.json                  # Extension-host TypeScript configuration
├── tsconfig.webview.json          # Angular/webview TypeScript configuration
├── webpack.config.js              # Webpack: extension (node) + webview (web) bundles
└── README.md                      # User-facing documentation
```

## Build

```bash
cd vscode/codedesigner
npm install
npm run compile
```

## Run / Debug

1. Start the C# emulator (embedded server on port 8765):
   ```bash
   cd CodeDesigner.Ps2
   dotnet run -- --run
   ```
2. In VS Code, press **F5** to launch the Extension Development Host.
3. In the new VS Code window, open the CodeDesigner panel and click **Connect**, or open the Command Palette and run `CodeDesigner: Connect to Server`.

## Extending the Extension

- Add new socket commands in `socketClient.ts` by handling new `Command` values in `sendCommand()`.
- Add new UI features to the Emulator sidebar panel by editing `src/webview/disasm-panel/disasm-panel.component.ts`.
- Add new Angular sub-components to the disasm editor under `src/webview/disasm-editor/components/` following the existing `hex-view.component.ts` pattern.
- The JSON protocol is documented in `SOCKET_COMMUNICATION.md` at the repository root.
- Architecture details (webpack config, Angular conventions, provider pattern) are in `ARCHITECTURE.md`.
