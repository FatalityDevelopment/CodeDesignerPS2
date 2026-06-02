import * as vscode from 'vscode';
import { SocketClient } from '../../socketClient';

/** Auto-resolved game context retained for source compatibility. */
export interface ActivityGameContext {
    gameName: string;
    gameDiscId: string;
}

/**
 * @deprecated AI activity now lives in AgentCtrlPanelProvider. This adapter is
 * intentionally lightweight: it preserves downstream imports without creating
 * a second activity subscription or a second webview.
 */
export class AgentActivityProvider {
    constructor(
        _extensionUri: vscode.Uri,
        _socketClient: SocketClient,
        _gameContextFn: () => ActivityGameContext | null,
    ) {}

    /** Open the integrated Activity tab through the existing command route. */
    public open(): void {
        void vscode.commands.executeCommand('codedesigner.openAgentActivity');
    }
}
