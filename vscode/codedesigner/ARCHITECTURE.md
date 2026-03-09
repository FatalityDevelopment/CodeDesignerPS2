# VS Code Extension — Architecture Guide

## Current Structure

```
vscode/codedesigner/src/
├── declarations.d.ts              # TypeScript module declarations for .html/.css imports
├── extension.ts                   # Extension entry point — registers providers & commands
├── socketClient.ts                # TCP socket client — communicates with the C# backend
├── utils/
│   └── nonce.ts                   # CSP nonce generator (single-responsibility utility)
└── providers/
    ├── asmPanel/
    │   ├── asmPanel.css           # Assembler panel styles (standalone CSS file)
    │   ├── asmPanelProvider.ts    # VS Code WebviewViewProvider implementation
    │   └── asmPanelTemplate.ts    # HTML template builder (imports CSS, returns string)
    ├── disasmPanel/
    │   ├── disasmPanel.css        # Disasm panel styles (standalone CSS file)
    │   ├── disasmPanelProvider.ts # VS Code WebviewViewProvider implementation
    │   └── disasmPanelTemplate.ts # HTML template builder (imports CSS, returns string)
    └── disasmEditor/
        ├── disasmEditor.css       # Disasm editor styles (single source of truth)
        ├── disasmEditor.html      # Full HTML + JS for the editor webview
        ├── disasmEditorProvider.ts# VS Code CustomReadonlyEditorProvider implementation
        └── disasmEditorTemplate.ts# Thin wrapper: loads .html, injects nonce + CSS
```

### How `.html` and `.css` imports work

Webpack 5's built-in `asset/source` rule (see `webpack.config.js`) bundles any
`.html` or `.css` file imported in TypeScript as its raw string content:

```typescript
import disasmEditorHtml from './disasmEditor.html'; // string
import disasmEditorCss  from './disasmEditor.css';  // string
```

`src/declarations.d.ts` provides the TypeScript module declarations so the
compiler understands these imports. No extra npm packages are required.

### Dynamic injection — the nonce pattern

VS Code webviews require a Content Security Policy nonce to allow inline scripts.
Template files use `{{NONCE}}` and `{{STYLES}}` as placeholder tokens that are
substituted at panel-open time:

```typescript
export function getDisasmEditorHtml(_data: DisasmEditorData, nonce: string): string {
    return disasmEditorHtml
        .replace(/\{\{NONCE\}\}/g, nonce)
        .replace('{{STYLES}}',     disasmEditorCss);
}
```

---

## What Was Removed

The original `helpers.ts` contained ~1,950 lines of client-side MIPS instruction
decoding (`decodeMIPS`, `disassemble`, `analyzePointers`) left over from an early
prototype. All decoding is now handled server-side by the C# backend and delivered
over the socket connection. These functions had no callers and were deleted.

---

## Going Forward — Recommended Architecture

### Problem with the current approach

The `asmPanelTemplate.ts` and `disasmPanelTemplate.ts` files still build HTML via
TypeScript template literals with dynamic server-side injection (emulator options,
connection state, etc.). This means:

- No IDE support for the HTML markup inside the string
- JavaScript inside the webview cannot be type-checked
- Logic mixes HTML generation with TypeScript business logic

### Option 1 — Static HTML + message-driven state (recommended, no new deps)

Move *all* dynamic state out of the initial HTML and into the webview's JavaScript
via `postMessage`. The TypeScript provider sends an `init` message immediately after
the panel opens; the webview JS populates all the fields.

**Benefits:**
- The HTML becomes fully static (only nonce substitution needed)
- All webview HTML moves to `.html` files — proper IDE support
- Webview JS stays vanilla JS (no framework overhead)

**Steps:**
1. Refactor `asmPanelTemplate.ts` and `disasmPanelTemplate.ts` to static `.html`
   files (same pattern as `disasmEditor.html` — `{{NONCE}}` only)
2. Move emulator-list population and connection-state rendering into the webview
   JavaScript (already done this way in `disasmPanel`)
3. Delete `asmPanelTemplate.ts` / `disasmPanelTemplate.ts`; replace with slim
   wrappers that import the `.html` file

### Option 2 — Compiled webview bundles (cleanest long-term)

Move webview JavaScript into proper TypeScript files and compile them as separate
webpack bundles served via `webview.asWebviewUri(...)`.

**Structure:**

```
src/providers/disasmEditor/
├── disasmEditor.html          # Skeleton HTML — <script src="{{WEBVIEW_URI}}">
├── disasmEditor.css           # Styles
├── disasmEditorProvider.ts    # Extension host: opens panel, sends messages
└── webview/
    ├── disasmEditorWebview.ts # Webview main — runs in browser context
    ├── grid.ts                # Disassembly grid renderer
    ├── hexView.ts             # Hex viewer
    ├── symbolsPanel.ts        # Symbols panel logic
    └── debugPanel.ts          # Register/stack view
```

**Webpack config addition:**

```javascript
const webviewConfig = {
    target: 'web',                          // Webview runs in a browser-like sandbox
    entry: {
        disasmEditorWebview: './src/providers/disasmEditor/webview/disasmEditorWebview.ts',
        // add more webview entry points here
    },
    output: {
        path: path.resolve(__dirname, 'dist/webview'),
        filename: '[name].js',
    },
    resolve: { extensions: ['.ts', '.js'] },
    module: { rules: [{ test: /\.ts$/, use: 'ts-loader' }] },
};

module.exports = [extensionConfig, webviewConfig];
```

**Benefits:**
- Full TypeScript type-checking for webview code
- IDE navigation, refactoring, and IntelliSense in webview files
- Tree-shaking and bundler optimisations apply to webview code
- Easy to split into smaller focused modules

**Provider side:**

```typescript
const webviewUri = panel.webview.asWebviewUri(
    vscode.Uri.joinPath(this._context.extensionUri, 'dist', 'webview', 'disasmEditorWebview.js')
);
panel.webview.html = templateHtml.replace('{{WEBVIEW_URI}}', webviewUri.toString());
```

### Option 3 — React / Angular / Svelte (most overhead, most familiar to web devs)

VS Code officially supports framework-based webviews via the
[webview UI toolkit](https://github.com/microsoft/vscode-webview-ui-toolkit) for
React, and the [vscode-webview-react-boilerplate](https://github.com/microsoft/vscode-webview-ui-toolkit/tree/main/docs/getting-started) pattern.

**Why Angular specifically:**
- Declarative templates replace all manual `innerHTML` string concatenation
- Two-way data binding makes the register/symbol/hex views trivial
- Angular CDK virtual-scroll is perfect for the 8M-instruction disassembly grid
- The injector/service pattern maps cleanly onto the message-passing architecture

**Cost:**
- ~50 KB gzip bundle overhead (Angular runtime)
- Requires a second webpack config targeting `web` (same as Option 2)
- Build times increase slightly

**Recommended approach if you go this route:**
1. Follow Option 2 first to get the webview as a compiled TypeScript bundle
2. Incrementally replace vanilla JS with Angular components one panel at a time
3. Use `@vscode/webview-ui-toolkit` components for buttons/dropdowns so they
   automatically match the active VS Code theme

---

## Summary of decisions made in this refactor

| Change | Why |
|---|---|
| Deleted `helpers.ts` MIPS decoder | Dead code — server handles all decoding |
| `getNonce()` → `src/utils/nonce.ts` | Single-responsibility; easy to find |
| `*Styles.ts` → `*.css` files | Proper CSS files get IDE highlighting, linting, and colour preview |
| `disasmEditorTemplate.ts` (1583 → 30 lines) | HTML + JS moved to `disasmEditor.html`; template is now a pure-data injection wrapper |
| Webpack `asset/source` for `.html`/`.css` | Zero new npm deps; built-in webpack 5 feature |
