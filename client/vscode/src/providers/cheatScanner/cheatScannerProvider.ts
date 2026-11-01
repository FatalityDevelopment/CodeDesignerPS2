import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SocketClient, LabelManagerEntry } from '../../socketClient';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import { ValueScanEngine, ScanRequest, ScanSummary } from './valueScanEngine';

/** Structure scans moved here from the Label Manager. */
type StructScanKind = 'strings' | 'functions' | 'pointers' | 'floats' | 'integers';

/** One structure-scan hit as the page shows it. */
interface StructRow {
    address: string;
    value:   string;
    detail?: string;
}

/**
 * The disassembler's typed memory watch list (`memWatchAdd`) lives on the
 * memory-diff-github-style branch (cd35c53) and is not on master yet. The page
 * hides "Add to watch list" until this is flipped together with that merge.
 */
const WATCH_LIST_AVAILABLE = false;

const PAGE_SIZE = 200;

/**
 * Standalone Cheat Scanner page: a Cheat Engine-style first scan / next scan
 * value scanner (see {@link ValueScanEngine}) plus the strings / functions /
 * pointers / floats / integers structure scans that used to live in the
 * Label Manager. Results can jump the disassembler, become labels, or be
 * copied as raw cheat codes.
 */
export class CheatScannerProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private readonly _engine: ValueScanEngine;
    /** True while a scan owns the socket; live-value polls are skipped meanwhile. */
    private _busy = false;
    private _lastStruct: { kind: StructScanKind; rows: StructRow[] } | null = null;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
        private readonly editorProvider: DisasmEditorProvider,
    ) {
        this._engine = new ValueScanEngine(socketClient);
    }

    /** Open (or reveal) the Cheat Scanner panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.cheatScanner',
            'Cheat Scanner',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'search');
        this._panel = panel;

        panel.onDidDispose(() => { this._panel = undefined; });
        panel.onDidChangeViewState(e => {
            if (e.webviewPanel.visible) { this._postContext(panel.webview); }
        });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            try {
                await this._handleMessage(msg, panel.webview);
            } catch (err: unknown) {
                const message = err instanceof Error ? err.message : String(err);
                getOutputChannel().error(`[CheatScanner] ${message}`);
                panel.webview.postMessage({ type: 'status', message, isError: true });
            }
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
                this._postContext(webview);
                this._postSession(webview, this._engine.summary());
                if (this._lastStruct) { webview.postMessage({ type: 'structResult', ...this._lastStruct }); }
                break;

            case 'firstScan':
            case 'nextScan':
                await this._runScan(webview, msg['request'] as ScanRequest, type === 'nextScan');
                break;

            case 'undoScan':
                this._postSession(webview, this._engine.undo());
                break;

            case 'newScan':
                this._engine.reset();
                this._postSession(webview, null);
                break;

            case 'page':
                webview.postMessage({
                    type: 'scanPage',
                    offset: msg['offset'] as number,
                    rows: this._engine.page(msg['offset'] as number, PAGE_SIZE),
                });
                break;

            case 'readCurrent': {
                if (this._busy || !this.socketClient.isConnected()) { break; }
                try {
                    const values = await this._engine.readCurrent((msg['addresses'] as number[]) ?? []);
                    webview.postMessage({ type: 'currentValues', values });
                } catch { /* transient read failure — the next poll retries */ }
                break;
            }

            case 'writeValue':
                await this._writeValue(webview, (msg['addresses'] as number[]) ?? []);
                break;

            case 'structScan':
                await this._runStructScan(
                    webview,
                    msg['kind'] as StructScanKind,
                    msg['region'] as 'ee' | 'iop',
                    msg['start'] as number,
                    msg['end'] as number,
                );
                break;

            case 'goto':
                this.editorProvider.navigateToAddress((msg['address'] as number) >>> 0);
                break;

            case 'addLabels': {
                const entries = (msg['entries'] as LabelManagerEntry[]) ?? [];
                if (entries.length === 0) { break; }
                const setName = await this.editorProvider.addLabelEntries(entries);
                webview.postMessage({
                    type: 'status',
                    message: `Added ${entries.length} label${entries.length === 1 ? '' : 's'} to "${setName}".`,
                });
                break;
            }

            case 'addStructLabels': {
                const rows = (msg['rows'] as StructRow[]) ?? [];
                const kind = msg['kind'] as StructScanKind;
                await this._handleMessage({ type: 'addLabels', entries: rows.map(r => structRowToLabel(kind, r)) }, webview);
                break;
            }

            case 'memWatchAdd':
                // Hidden in the page until the watch list lands on this branch (see WATCH_LIST_AVAILABLE).
                vscode.window.showInformationMessage('The memory watch list is not available in this build yet.');
                break;

            case 'openLabelManager':
                this.editorProvider.openLabelManagerTab();
                break;

            case 'copy':
                await vscode.env.clipboard.writeText(msg['text'] as string ?? '');
                webview.postMessage({ type: 'status', message: msg['summary'] as string ?? 'Copied.' });
                break;

            default:
                getOutputChannel().warn(`[CheatScanner] Unknown message type: ${type}`);
                break;
        }
    }

    // ── Value scan ────────────────────────────────────────────────────────────

    private async _runScan(webview: vscode.Webview, request: ScanRequest, next: boolean): Promise<void> {
        if (!this.socketClient.isConnected()) {
            webview.postMessage({ type: 'scanError', message: 'Not connected to the CodeDesigner bridge.' });
            return;
        }
        if (this._busy) { return; }
        this._busy = true;
        const started = Date.now();
        try {
            const summary = next ? await this._engine.nextScan(request) : await this._engine.firstScan(request);
            const secs = ((Date.now() - started) / 1000).toFixed(1);
            this._postSession(webview, { ...summary, message: [summary.message, `Scan took ${secs}s.`].filter(Boolean).join(' ') });
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            getOutputChannel().error(`[CheatScanner] ${message}`);
            webview.postMessage({ type: 'scanError', message });
        } finally {
            this._busy = false;
        }
    }

    private _postSession(webview: vscode.Webview, summary: ScanSummary | null): void {
        webview.postMessage({
            type: 'scanSession',
            summary,
            rows: summary ? this._engine.page(0, PAGE_SIZE) : [],
            pageSize: PAGE_SIZE,
        });
    }

    private async _writeValue(webview: vscode.Webview, addresses: number[]): Promise<void> {
        const region = this._engine.regionTag;
        if (addresses.length === 0 || !region) { return; }
        const text = await vscode.window.showInputBox({
            title: addresses.length === 1
                ? `Write value to 0x${(addresses[0] >>> 0).toString(16).toUpperCase().padStart(8, '0')}`
                : `Write value to ${addresses.length} addresses`,
            prompt: 'Decimal, 0x-prefixed hex, text or a byte pattern — matching the scan type.',
        });
        if (text === undefined) { return; }
        const hexBytes = this._engine.encodeForWrite(text, false);
        for (const addr of addresses) {
            await this.socketClient.writeMemory(addr >>> 0, hexBytes, region);
        }
        webview.postMessage({ type: 'status', message: `Wrote ${hexBytes.length / 2} byte(s) to ${addresses.length} address(es).` });
        const values = await this._engine.readCurrent(addresses);
        webview.postMessage({ type: 'currentValues', values });
    }

    // ── Structure scans (formerly in the Label Manager) ──────────────────────

    private async _runStructScan(webview: vscode.Webview, kind: StructScanKind, region: 'ee' | 'iop', start: number, end: number): Promise<void> {
        if (!this.socketClient.isConnected()) {
            webview.postMessage({ type: 'structError', message: 'Not connected to the CodeDesigner bridge.' });
            return;
        }
        if (this._busy) { return; }
        this._busy = true;
        try {
            const length = end - start;
            if (length <= 0) { throw new Error('End address must be greater than start address.'); }
            let rows: StructRow[];
            if (kind === 'strings') {
                rows = (await this.socketClient.scanLiveStrings(start, length, region))
                    .map(r => ({ address: r.address, value: r.value }));
            } else if (kind === 'functions') {
                rows = (await this.socketClient.scanLiveFunctions(start, length, region))
                    .map(r => ({ address: r.address, value: r.label || `func_${bareHex(r.address)}` }));
            } else if (kind === 'pointers') {
                rows = (await this.socketClient.scanLivePointers(start, length, region))
                    .map(r => ({ address: r.address, value: r.pointsTo }));
            } else if (kind === 'floats') {
                rows = (await this.socketClient.scanLiveFloats(start, length, region))
                    .map(r => ({ address: r.address, value: String(r.value), detail: r.raw }));
            } else {
                rows = (await this.socketClient.scanLiveIntegers(start, length, region))
                    .map(r => ({ address: r.address, value: r.hex, detail: String(r.value) }));
            }
            this._lastStruct = { kind, rows };
            webview.postMessage({ type: 'structResult', kind, rows });
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'structError', message: `Scan failed: ${message}` });
        } finally {
            this._busy = false;
        }
    }

    // ── Context ───────────────────────────────────────────────────────────────

    private _postContext(webview: vscode.Webview): void {
        const ctx = this.editorProvider.getLabelManagerContext();
        webview.postMessage({
            type: 'context',
            gameName: ctx?.gameName ?? '',
            connected: this.socketClient.isConnected(),
            watchListAvailable: WATCH_LIST_AVAILABLE,
        });
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'cheat-scanner.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               img-src ${webview.cspSource} data:;
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-cheat-scanner-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Cheat Scanner…</p></app-cheat-scanner-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}

function bareHex(address: string): string {
    return address.replace(/^0x/i, '');
}

/** Same label shapes the Label Manager's scanner produced. */
function structRowToLabel(kind: StructScanKind, r: StructRow): LabelManagerEntry {
    switch (kind) {
        case 'strings':
            return {
                address: r.address,
                name: r.value.length > 40 ? r.value.substring(0, 40) + '…' : r.value,
                comment: r.value.length > 40 ? r.value : undefined,
                type: 'String',
            };
        case 'functions':
            return { address: r.address, name: r.value, type: 'Function' };
        case 'pointers':
            return { address: r.address, name: `ptr → ${r.value}`, comment: `Points to ${r.value}`, type: 'Pointer' };
        case 'floats':
            return { address: r.address, name: r.value, comment: r.detail ? `raw: ${r.detail}` : undefined, type: 'Float' };
        default:
            return { address: r.address, name: r.value, comment: r.detail ? `dec: ${r.detail}` : undefined, type: 'Hex' };
    }
}
