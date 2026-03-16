# VS Code Extension — Architecture Guide

## Project Structure

```
vscode/codedesigner/
├── src/
│   ├── declarations.d.ts              # TypeScript module declarations for .html/.css imports
│   ├── extension.ts                   # Extension entry point — registers providers & commands
│   ├── socketClient.ts                # TCP socket client — communicates with the C# backend
│   ├── language-configuration.json    # .cds language bracket/comment configuration
│   ├── utils/
│   │   └── nonce.ts                   # CSP nonce generator
│   ├── providers/                     # Extension-host (Node.js) side — one folder per panel
│   │   ├── disasmPanel/
│   │   │   ├── disasmPanel.css        # CSS reference
│   │   │   └── disasmPanelProvider.ts # VS Code WebviewViewProvider — serves Angular bundle
│   │   ├── disasmEditor/
│   │   │   ├── disasmEditor.css       # CSS reference
│   │   │   └── disasmEditorProvider.ts# VS Code CustomReadonlyEditorProvider — serves Angular bundle
│   │   └── pointerManager/
│   │       └── pointerManagerProvider.ts # VS Code TreeDataProvider — struct/label tree view
│   └── webview/                       # Angular 19 webview apps (compiled by webviewConfig)
│       ├── typings.d.ts               # CSS import declarations for webview TypeScript
│       ├── shared/
│       │   ├── vscode-api.ts          # Singleton wrapper for acquireVsCodeApi()
│       │   └── vscode-bridge.service.ts  # Angular service: postMessage + messages$ RxJS stream
│       ├── disasm-panel/
│       │   ├── main.ts                # bootstrapApplication(DisasmPanelComponent)
│       │   ├── disasm-panel.component.ts
│       │   └── disasm-panel.css
│       └── disasm-editor/
│           ├── main.ts                # bootstrapApplication(DisasmEditorComponent)
│           ├── disasm-editor.component.ts  # Full editor: grid, search, debug, bottom, symbols panels
│           ├── disasm-editor.service.ts    # All editor state + VS Code message handling
│           ├── disasm-editor.css
│           └── components/
│               └── hex-view.component.ts   # Standalone hex dump panel
├── tsconfig.json                      # Extension-host TypeScript config
├── tsconfig.webview.json              # Angular/webview TypeScript config
└── webpack.config.js                  # Exports [extensionConfig, webviewConfig]
```

## Two webpack configurations

### `extensionConfig` (target: `node`)
Compiles the extension host code (`src/extension.ts`) to `dist/extension.js`.

### `webviewConfig` (target: `web`)
Compiles two Angular 19 apps to `dist/webview/`:

| Entry point | Output |
|-------------|--------|
| `src/webview/disasm-panel/main.ts` | `dist/webview/disasm-panel.js` |
| `src/webview/disasm-editor/main.ts` | `dist/webview/disasm-editor.js` |

**Build:** `npm run compile`

## How providers serve Angular apps

Each provider converts the compiled bundle path to a webview-accessible URI:

```typescript
private getHtml(webview: vscode.Webview): string {
    const scriptUri = webview.asWebviewUri(
        vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'disasm-panel.js')
    );
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; style-src 'unsafe-inline'; script-src ${webview.cspSource};">
</head>
<body>
  <app-disasm-panel></app-disasm-panel>
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
`webview.onDidReceiveMessage(...)`.

`DisasmEditorService` holds all disassembler state and handles VS Code messages via `VsCodeBridgeService` with Subject observables. `HexViewComponent` is a standalone Angular component for the hex view.

## Angular 19 conventions

All components are **standalone** (no `NgModule`) with `ViewEncapsulation.None`
so global CSS (VS Code theme variables) applies without scoping:

```typescript
@Component({
  selector: 'app-disasm-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  template: `...`,
})
export class DisasmPanelComponent implements OnInit, OnDestroy { ... }
```

`zone.js` is imported as the **first** import in every `main.ts`.

## Going further

### Adding a new panel

1. Create `src/webview/my-panel/` with `main.ts`, `my-panel.component.ts`, `my-panel.css`
2. Add entry in `webviewConfig.entry` in `webpack.config.js`
3. Create provider in `src/providers/my-panel/myPanelProvider.ts`
4. Register in `extension.ts`

### Adding sub-components to disasm-editor

`disasm-editor.component.ts` is the root component. New standalone sub-components
follow the existing `components/hex-view.component.ts` pattern — create the file under
`components/`, declare it `standalone: true`, import it in `DisasmEditorComponent.imports`,
and use it in the template.

Pass state with `@Input()` / `@Output()` or inject the shared `DisasmEditorService`.

### Virtual scrolling

For large binaries (BIOS = 1 M instructions), Angular CDK virtual scroll improves
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
