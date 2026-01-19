import * as vscode from 'vscode';
import { exec } from 'child_process';
import { getNonce } from '../../helpers';
import { getAsmPanelHtml, AsmPanelData } from './asmPanelTemplate';

let outputChannel: vscode.OutputChannel;

interface CompilerResult {
    cheatCodes?: string;
    debugMessages?: string[];
}

export class AsmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('asm.asmPanel', this)
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true
        };

        // Generate HTML from template
        const templateData: AsmPanelData = {
            code: '',
            emulators: ['Project64', 'Mupen64Plus', 'RetroArch']
        };
        webviewView.webview.html = getAsmPanelHtml(templateData, getNonce());

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
