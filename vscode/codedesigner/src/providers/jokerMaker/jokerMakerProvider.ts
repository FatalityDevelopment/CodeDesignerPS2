import * as vscode from 'vscode';
import { getOutputChannel, getPadStateChannel } from '../../extension';
import { SocketClient, PadStateInfo } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

/**
 * Mask layout used by PADGETSTATE responses (PS2 active-low: bit clear means
 * pressed). Mirrors PadDualshock2 wire layout from CodeDesigner.Ps2.EmulatorCore.
 */
const BUTTON_MASK = {
    select:   0x0001,
    l3:       0x0002,
    r3:       0x0004,
    start:    0x0008,
    up:       0x0010,
    right:    0x0020,
    down:     0x0040,
    left:     0x0080,
    l2:       0x0100,
    r2:       0x0200,
    l1:       0x0400,
    r1:       0x0800,
    triangle: 0x1000,
    circle:   0x2000,
    cross:    0x4000,
    square:   0x8000,
} as const;

/**
 * Pressure-array index per PadDualshock2::Inputs (first 16 entries). Order is
 * required by PADSETSTATE; pressures[index] > 0 means pressed.
 */
const PRESSURE_INDEX: Record<keyof typeof BUTTON_MASK, number> = {
    up: 0, right: 1, down: 2, left: 3,
    triangle: 4, circle: 5, cross: 6, square: 7,
    select: 8, start: 9,
    l1: 10, l2: 11, r1: 12, r2: 13,
    l3: 14, r3: 15,
};

/**
 * Snapshot of the virtual controller state shipped from the webview whenever
 * the user changes the press list or moves a stick. The provider translates
 * this into a PADSETSTATE opcode for PCSX2.
 */
interface VirtualPadStateMsg {
    type:    'jokerVirtualState';
    /** Names of currently-pressed buttons (subset of BUTTON_MASK keys). */
    pressed: (keyof typeof BUTTON_MASK)[];
    /** Left stick normalized to [-1, 1] for both axes. */
    leftStick:  { x: number; y: number };
    /** Right stick normalized to [-1, 1] for both axes. */
    rightStick: { x: number; y: number };
}

/** Provider for the standalone Controller / virtual PS2 controller webview. */
export class JokerMakerProvider {
    public static readonly viewType = 'codedesigner.jokerMaker';

    private _panel: vscode.WebviewPanel | undefined;
    private _padPollTimer: ReturnType<typeof setInterval> | undefined;
    private _lastLivePadButtons: number | undefined;
    private _liveEnabled = false;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the joker maker panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Active);
            this._postConnectionStatus();
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            JokerMakerProvider.viewType,
            'Controller',
            vscode.ViewColumn.Active,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this._extensionUri],
            },
        );
        this._panel = panel;

        panel.onDidDispose(() => {
            this._stopLivePoll();
            this._panel = undefined;
        });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg);
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    /** Push fresh connection/pcsx2 status to the open panel (if any). */
    public notifyConnectionChanged(): void {
        if (!this._panel) { return; }
        this._postConnectionStatus();
        // If we lost the connection while polling, stop cleanly.
        if (!this._socketClient.isConnected()) {
            this._stopLivePoll();
        }
    }

    // Message dispatch

    private async _handleMessage(msg: Record<string, unknown>): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
                this._sendInit();
                break;

            case 'jokerVirtualState':
                await this._handleVirtualState(msg as unknown as VirtualPadStateMsg);
                break;

            case 'jokerLog': {
                const text = (msg['text'] as string) ?? '';
                if (text) { getPadStateChannel().appendLine(text); }
                break;
            }

            case 'jokerLiveToggle':
                if (msg['enabled']) { this._startLivePoll(); }
                else { this._stopLivePoll(); }
                break;

            case 'bootstrapError':
                getOutputChannel().error('[JokerMaker] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    // Virtual controller to PCSX2 PINE

    /**
     * Convert the webview's virtual state into a PADSETSTATE call. Each pressed
     * button contributes a cleared bit (active-low) and a non-zero pressure
     * byte. Sticks are mapped from [-1, 1] to [0x00, 0xFF] with 0x7F centered.
     */
    private async _handleVirtualState(state: VirtualPadStateMsg): Promise<void> {
        if (!this._socketClient.isConnected()) { return; }

        let buttons = 0xFFFF;
        const pressures = new Array<number>(16).fill(0);
        for (const name of state.pressed ?? []) {
            const mask = BUTTON_MASK[name];
            if (!mask) { continue; }
            buttons &= ~mask;
            const idx = PRESSURE_INDEX[name];
            if (idx !== undefined) { pressures[idx] = 0xFF; }
        }

        const lx = this._stickAxis(state.leftStick?.x);
        const ly = this._stickAxis(state.leftStick?.y);
        const rx = this._stickAxis(state.rightStick?.x);
        const ry = this._stickAxis(state.rightStick?.y);

        try {
            await this._socketClient.padSetState(0, lx, ly, rx, ry, pressures, buttons);
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            getPadStateChannel().appendLine(`[error] padSetState failed: ${message}`);
        }
    }

    /** Map a normalized stick axis (-1..1) to a PS2 byte axis (0..255, 0x7F center). */
    private _stickAxis(value: number | undefined): number {
        if (typeof value !== 'number' || !isFinite(value)) { return 0x7F; }
        const clamped = Math.max(-1, Math.min(1, value));
        return Math.round(0x7F + clamped * 0x80) & 0xFF;
    }

    // Live pad polling

    private _startLivePoll(): void {
        this._stopLivePoll();
        if (!this._socketClient.isConnected()) { return; }
        this._liveEnabled = true;
        this._lastLivePadButtons = undefined;

        let errorCount = 0;
        const poll = async () => {
            try {
                const state: PadStateInfo = await this._socketClient.padGetState(0);
                errorCount = 0;
                if (state.buttons !== this._lastLivePadButtons) {
                    this._lastLivePadButtons = state.buttons;
                    this._panel?.webview.postMessage({
                        type:    'jokerLivePadState',
                        buttons: state.buttons,
                        lx: state.lx, ly: state.ly,
                        rx: state.rx, ry: state.ry,
                    });
                }
            } catch {
                errorCount++;
                if (errorCount >= 5) {
                    getOutputChannel().warn('[JokerMaker] live pad poll: too many consecutive failures, stopping.');
                    this._stopLivePoll();
                }
            }
        };
        void poll();
        this._padPollTimer = setInterval(() => void poll(), 250);
    }

    private _stopLivePoll(): void {
        if (this._padPollTimer !== undefined) {
            clearInterval(this._padPollTimer);
            this._padPollTimer = undefined;
        }
        this._liveEnabled = false;
        this._lastLivePadButtons = undefined;
        this._panel?.webview.postMessage({ type: 'jokerLivePollStopped' });
    }

    // Webview init / status

    private _sendInit(): void {
        if (!this._panel) { return; }
        const imageUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'resources', 'controller.webp'),
        ).toString();
        this._panel.webview.postMessage({
            type: 'jokerInit',
            controllerImage: imageUri,
        });
        this._postConnectionStatus();
        // Resume polling automatically if the user had it enabled previously.
        if (this._liveEnabled) { this._startLivePoll(); }
    }

    private _postConnectionStatus(): void {
        this._panel?.webview.postMessage({
            type:      'jokerConnectionStatus',
            connected: this._socketClient.isConnected(),
        });
    }

    // HTML scaffold

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'joker-maker.js'),
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               img-src ${webview.cspSource} data: blob:;
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-joker-maker-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Controller...</p></app-joker-maker-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
