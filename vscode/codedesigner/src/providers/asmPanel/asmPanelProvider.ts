import * as vscode from 'vscode';
import { exec } from 'child_process';
import { BasePanelProvider } from '../baseProviders/basePanelProvider';

let outputChannel: vscode.OutputChannel;

interface CompilerResult {
    cheatCodes?: string;
    debugMessages?: string[];
}

export class AsmPanelProvider extends BasePanelProvider {
    private webview?: vscode.Webview;

    constructor() {
        super('asm.asmPanel', 'asmPanelProvider.html', 'asmPanel');
    }

    protected onDidResolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Ensure output channel exists
        if (!outputChannel) {
            outputChannel = vscode.window.createOutputChannel('Assembler');
        }

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(message => {
            switch (message.command) {
                case 'connect':
                    vscode.window.showInformationMessage(
                        'Connect clicked (emulator hookup later)'
                    );
                    break;

                case 'compile':
                    this.runCds('-c');
                    break;

                case 'decompile':
                    this.runCds('-d');
                    break;
            }
        });
    }

    private runCds(option: string) {

        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showErrorMessage('No active file.');
            return;
        }

        const sourcePath = editor.document.uri.fsPath;

        const exePath =
            'C:\\Users\\tholu\\source\\repos\\CodeDesignerAI\\CodeDesigner.ConsoleApp\\bin\\Debug\\net9.0\\CodeDesigner.ConsoleApp.exe';

        const command = `"${exePath}" CDS ${option} -f "${sourcePath}"`;

        outputChannel.clear();
        outputChannel.show(true);
        outputChannel.appendLine(`> ${command}\n`);
        //outputChannel.appendLine(this.getWorkspaceRoot() ?? 'No workspace root');

        exec(command, (error, stdout, stderr) => {

            if (stderr) {
                outputChannel.appendLine(stderr);
            }

            if (error) {
                outputChannel.appendLine(`ERROR: ${error.message}`);
                return;
            }

            try {
                const result: CompilerResult = JSON.parse(stdout);

                // Show debug messages
                result.debugMessages?.forEach(msg =>
                    outputChannel.appendLine(msg)
                );

                // Push cheatCodes back into the panel textarea
                if (result.cheatCodes && this.webview) {
                    this.webview.postMessage({
                        command: 'setCode',
                        code: result.cheatCodes
                    });
                }

            } catch (e) {
                outputChannel.appendLine('Failed to parse compiler JSON output');
                outputChannel.appendLine(stdout);
            }
        });
    }
}
