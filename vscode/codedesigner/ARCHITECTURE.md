# VS Code Extension — Architecture Guide

## Project Structure

```
vscode/codedesigner/
├── src/
│   ├── declarations.d.ts              # TypeScript module declarations for .html/.css imports
│   ├── extension.ts                   # Extension entry point — registers providers & commands
│   ├── socketClient.ts                # TCP socket client — communicates with the C# backend
│   ├── utils/
│   │   └── nonce.ts                   # CSP nonce generator
│   ├── providers/                     # Extension-host (Node.js) side — one folder per panel
│   │   ├── asmPanel/
│   │   │   ├── asmPanel.css           # CSS reference (canonical source in webview folder)
│   │   │   └── asmPanelProvider.ts    # VS Code WebviewViewProvider — serves Angular bundle
│   │   ├── disasmPanel/
│   │   │   ├── disasmPanel.css        # CSS reference
│   │   │   └── disasmPanelProvider.ts # VS Code WebviewViewProvider — serves Angular bundle
│   │   └── disasmEditor/
│   │       ├── disasmEditor.css       # CSS reference
│   │       └── disasmEditorProvider.ts# VS Code CustomReadonlyEditorProvider — serves Angular bundle
│   └── webview/                       # Angular 19 webview apps (compiled by webviewConfig)
│       ├── typings.d.ts               # CSS import declarations for webview TypeScript
│       ├── shared/
│       │   ├── vscode-api.ts          # Singleton wrapper for acquireVsCodeApi()
│       │   └── vscode-bridge.service.ts  # Angular service: postMessage + messages$ RxJS stream
│       ├── asm-panel/
│       │   ├── main.ts                # bootstrapApplication(AsmPanelComponent)
│       │   ├── asm-panel.component.ts # Standalone Angular 19 component
│       │   └── asm-panel.css          # Styles (style-loader injects at runtime)
│       ├── disasm-panel/
│       │   ├── main.ts                # bootstrapApplication(DisasmPanelComponent)
│       │   ├── disasm-panel.component.ts
│       │   └── disasm-panel.css
│       └── disasm-editor/
│           ├── main.ts                # bootstrapApplication(DisasmEditorComponent)
│           ├── disasm-editor.component.ts  # Full editor: grid, hex, search, debug panels
│           └── disasm-editor.css
├── tsconfig.json                      # Extension-host TypeScript config
├── tsconfig.webview.json              # Angular/webview TypeScript config
└── webpack.config.js                  # Exports [extensionConfig, webviewConfig]
```

## Two webpack configurations

### `extensionConfig` (target: `node`)
Compiles the extension host code (`src/extension.ts`) to `dist/extension.js`.

### `webviewConfig` (target: `web`)
Compiles three Angular 19 apps to `dist/webview/`:

| Entry point | Output |
|-------------|--------|
| `src/webview/asm-panel/main.ts` | `dist/webview/asm-panel.js` |
| `src/webview/disasm-panel/main.ts` | `dist/webview/disasm-panel.js` |
| `src/webview/disasm-editor/main.ts` | `dist/webview/disasm-editor.js` |

**Build:** `npm run compile`

## How providers serve Angular apps

Each provider converts the compiled bundle path to a webview-accessible URI:

```typescript
private getHtml(webview: vscode.Webview): string {
    const scriptUri = webview.asWebviewUri(
        vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'asm-panel.js')
    );
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; style-src 'unsafe-inline'; script-src ${webview.cspSource};">
</head>
<body>
  <app-asm-panel></app-asm-panel>
  <script src="${scriptUri}"></script>
</body>
</html>`;
}
```

- `script-src ${webview.cspSource}` — allows scripts from the extension's `dist/` via `vscode-webview-resource:`; no nonce needed for external scripts
- `style-src 'unsafe-inline'` — Angular's `style-loader` injects `<style>` tags at runtime

## VS Code ↔ Angular communication

`VsCodeBridgeService` wraps the VS Code postMessage API as RxJS observables:

```typescript
// Send to extension host
bridge.postMessage({ command: 'assemble', data: code });

// Listen for a specific message type
bridge.on<AssembleResultMsg>('assembleResult').subscribe(msg => {
    this.output = msg.data;
});
```

The extension host sends back via `webview.postMessage(...)` and receives via
`webview.onDidReceiveMessage(...)` — unchanged from before the Angular migration.

## Angular 19 conventions

All components are **standalone** (no `NgModule`) with `ViewEncapsulation.None`
so global CSS (VS Code theme variables) applies without scoping:

```typescript
@Component({
  selector: 'app-asm-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  template: `...`,
})
export class AsmPanelComponent implements OnInit, OnDestroy { ... }
```

`zone.js` is imported as the **first** import in every `main.ts`.

## Going further

### Adding a new panel

1. Create `src/webview/my-panel/` with `main.ts`, `my-panel.component.ts`, `my-panel.css`
2. Add entry in `webviewConfig.entry` in `webpack.config.js`
3. Create provider in `src/providers/my-panel/myPanelProvider.ts`
4. Register in `extension.ts`

### Splitting the disasm-editor into sub-components

`disasm-editor.component.ts` (~1,357 lines) is the largest single component. A
natural decomposition:

```
disasm-editor/
  disasm-editor.component.ts        # Root — bootstraps, owns state
  components/
    disasm-grid.component.ts        # Virtual instruction rows
    hex-view.component.ts           # Hex viewer
    search-panel.component.ts       # Search bar
    goto-panel.component.ts         # Goto address
    symbols-panel.component.ts      # Labels / Strings / Functions tabs
    debug-panel.component.ts        # GPR / COP0 / FPR / VU0 / GS / Stack tabs
    edit-overlay.component.ts       # Inline label/comment editor modal
```

Pass state with `@Input()` / `@Output()` or inject a shared service.

### Virtual scrolling

For large binaries (BIOS = 1M instructions), Angular CDK virtual scroll improves
performance significantly:

```bash
npm install @angular/cdk
```

```html
<cdk-virtual-scroll-viewport itemSize="20" style="height:100%">
  <div *cdkVirtualFor="let row of disasmRows" class="row">...</div>
</cdk-virtual-scroll-viewport>
```

### Angular Signals (replacing mutable state)

Angular 19 signals are the recommended reactive primitive — no extra dependencies:

```typescript
disasmRows = signal<DisasmRow[]>([]);
cursor      = signal(0);

// Mutation:
this.disasmRows.set(newRows);
this.cursor.update(c => clamp(c + delta, 0, this.totalInstructions - 1));
```
