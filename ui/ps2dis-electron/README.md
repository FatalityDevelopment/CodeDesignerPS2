# CodeDesigner ps2dis Electron UI

TypeScript/Electron recreation of the classic **ps2dis** desktop layout. The renderer intentionally shares the same message pattern as the CodeDesigner VS Code disassembler webview: a generic `postMessage`/typed listener bridge emits `ready`, accepts host `init` messages, and reports selected addresses as `gotoAddress` payloads.

The visual model is based on the old ps2dis UI and workflow notes: PS2/PSX ELF support, EE/COP opcode display, analyzer labels, address navigation, and patch/editor-oriented rows.

## Scripts

- `npm run dev` starts the Vite renderer for browser iteration.
- `npm run build` type-checks and bundles the renderer.
- `npm start` launches Electron after a build.
