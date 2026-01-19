// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import { DisasmEditorProvider } from './providers/disasmEditor/disasmEditorProvider';
import { DisasmPanelProvider } from './providers/disasmPanel/disasmPanelProvider';
import { AsmPanelProvider } from './providers/asmPanel/asmPanelProvider';
import { SocketClient } from './socketClient';

let socketClient: SocketClient | null = null;

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {

	// Register custom editor for disassembly
	const editorProvider = new DisasmEditorProvider();
	context.subscriptions.push(
		vscode.window.registerCustomEditorProvider('disasm.disasmEditor', editorProvider)
	);

	// Register webview panels
	const disasmPanelProvider = new DisasmPanelProvider();
	disasmPanelProvider.register(context);

	const asmPanelProvider = new AsmPanelProvider();
	asmPanelProvider.register(context);

	// Initialize socket client
	socketClient = new SocketClient('localhost', 8765);

	// Register socket event handlers
	socketClient.onConnected(() => {
		vscode.window.showInformationMessage('Connected to C# server');
	});

	socketClient.onMessage((message: string) => {
		console.log(`Message from server: ${message}`);
		vscode.window.showInformationMessage(`Server: ${message}`);
	});

	socketClient.onDisconnected(() => {
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

	// Auto-connect on activation (optional)
	// Uncomment if you want automatic connection
	// setTimeout(() => {
	// 	socketClient?.connect().catch(err => {
	// 		console.log('Failed to auto-connect:', err.message);
	// 	});
	// }, 1000);
}

// This method is called when your extension is deactivated
export function deactivate() {
	if (socketClient) {
		socketClient.disconnect();
		socketClient = null;
	}
}
