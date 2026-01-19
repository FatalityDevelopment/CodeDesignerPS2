// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import { DisasmEditorProvider } from './providers/disasmEditor/disasmEditorProvider';
import { DisasmPanelProvider } from './providers/disasmPanel/disasmPanelProvider';
import { AsmPanelProvider } from './providers/asmPanel/asmPanelProvider';
import { SocketClient } from './socketClient';

let socketClient: SocketClient | null = null;
let outputChannel: vscode.OutputChannel;

/**
 * Get the socket client instance
 */
export function getSocketClient(): SocketClient | null {
    return socketClient;
}

/**
 * Get the output channel instance
 */
export function getOutputChannel(): vscode.OutputChannel {
    return outputChannel;
}

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {

	// Create output channel for logging
	outputChannel = vscode.window.createOutputChannel('CodeDesigner');
	context.subscriptions.push(outputChannel);

	// Initialize socket client
	socketClient = new SocketClient('localhost', 8765);

	// Register custom editor for disassembly (after socket client is initialized)
	const editorProvider = new DisasmEditorProvider(socketClient);
	context.subscriptions.push(
		vscode.window.registerCustomEditorProvider('disasm.disasmEditor', editorProvider)
	);

	// Register webview panels
	const disasmPanelProvider = new DisasmPanelProvider();
	disasmPanelProvider.register(context);

	const asmPanelProvider = new AsmPanelProvider();
	asmPanelProvider.register(context);

	// Register socket event handlers
	socketClient.onConnected(() => {
		outputChannel.appendLine('[INFO] Connected to C# server');
		vscode.window.showInformationMessage('Connected to C# server');
	});

	socketClient.onMessage((message: string) => {
		outputChannel.appendLine(`[MESSAGE] ${message}`);
	});

	socketClient.onDisconnected(() => {
		outputChannel.appendLine('[WARN] Disconnected from C# server');
		vscode.window.showWarningMessage('Disconnected from C# server');
	});

	// Register commands for socket communication
	const connectCommand = vscode.commands.registerCommand('codedesigner.connectServer', async () => {
		try {
			await socketClient?.connect();
		} catch (error) {
			vscode.window.showErrorMessage(`Failed to connect: ${error}`);
		}
	});

	const disconnectCommand = vscode.commands.registerCommand('codedesigner.disconnectServer', () => {
		socketClient?.disconnect();
		vscode.window.showInformationMessage('Disconnected from C# server');
	});

	const sendMessageCommand = vscode.commands.registerCommand('codedesigner.sendMessage', async () => {
		const message = await vscode.window.showInputBox({
			prompt: 'Enter message to send to C# server',
			placeHolder: 'Hello from VSCode!'
		});

		if (message && socketClient?.isConnected()) {
			try {
				await socketClient.sendMessage(message);
				vscode.window.showInformationMessage(`Sent: ${message}`);
			} catch (error) {
				vscode.window.showErrorMessage(`Failed to send message: ${error}`);
			}
		} else if (!socketClient?.isConnected()) {
			vscode.window.showWarningMessage('Not connected to server. Use "Connect to Server" command first.');
		}
	});

	context.subscriptions.push(connectCommand, disconnectCommand, sendMessageCommand);

	// Auto-connect on activation
	outputChannel.appendLine('[INFO] Extension activated, attempting to connect to server...');
	setTimeout(() => {
		socketClient?.connect().then(() => {
			outputChannel.appendLine('[SUCCESS] Auto-connected to server');
		}).catch(err => {
			const errorMsg = err?.message || err?.toString() || 'Unknown error';
			outputChannel.appendLine(`[ERROR] Failed to auto-connect: ${errorMsg}`);
			outputChannel.appendLine('[INFO] You can manually connect using "CodeDesigner: Connect to Server" command');
			outputChannel.appendLine('[INFO] Make sure the C# server is running on localhost:8765');
		});
	}, 1000);
}

// This method is called when your extension is deactivated
export function deactivate() {
	if (socketClient) {
		socketClient.disconnect();
		socketClient = null;
	}
}
