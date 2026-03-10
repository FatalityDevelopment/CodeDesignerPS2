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
	const editorProvider = new DisasmEditorProvider(socketClient, context.extensionUri);
	context.subscriptions.push(
		vscode.window.registerCustomEditorProvider('disasm.disasmEditor', editorProvider)
	);

	// Check for already-open .bin files and prompt to reopen with custom editor
	const checkForOpenBinFiles = () => {
		const openTabs = vscode.window.tabGroups.all.flatMap(group => group.tabs);
		const binTabs = openTabs.filter(tab => {
			if (tab.input instanceof vscode.TabInputText) {
				const uri = tab.input.uri;
				return uri.scheme === 'file' && uri.path.toLowerCase().endsWith('.bin');
			}
			return false;
		});

		if (binTabs.length > 0) {
			outputChannel.appendLine(`[INFO] Found ${binTabs.length} already-open .bin file(s)`);
			binTabs.forEach(tab => {
				if (tab.input instanceof vscode.TabInputText) {
					const uri = tab.input.uri;
					outputChannel.appendLine(`[INFO] Reopening ${uri.fsPath} with disassembler`);
					// Close the text editor and reopen with custom editor
					vscode.commands.executeCommand('vscode.openWith', uri, 'disasm.disasmEditor');
				}
			});
		}
	};

	// Check for open .bin files after a short delay to ensure extension is fully activated
	setTimeout(checkForOpenBinFiles, 500);

	// Register webview panels — pass socketClient so the panel can auto-connect
	const disasmPanelProvider = new DisasmPanelProvider(socketClient, editorProvider, context.extensionUri);
	disasmPanelProvider.register(context);

	const asmPanelProvider = new AsmPanelProvider(context.extensionUri);
	asmPanelProvider.register(context);

	// Wire pause-state propagation: when a breakpoint/step pause is detected in the
	// editor panels, the sidebar status bar needs to update too.
	editorProvider.onPauseStateChange = (paused: boolean) => {
		disasmPanelProvider.notifyPauseState(paused);
	};

	// ── Binary tab focus tracking ─────────────────────────────────────────────
	// When the user focuses a binary-file disasm editor tab (registered with
	// viewType 'disasm.disasmEditor' via registerCustomEditorProvider), the sidebar
	// emulator controls should be disabled to avoid confusion.  Live memory view
	// panels use a different viewType ('disasm.liveMemory') created via
	// createWebviewPanel, so they appear as TabInputWebview — not TabInputCustom —
	// and will NOT trigger the binary-focused state.
	const syncBinaryFocus = () => {
		const activeTab = vscode.window.tabGroups.activeTabGroup.activeTab;
		const isBinary = activeTab?.input instanceof vscode.TabInputCustom &&
			activeTab.input.viewType === 'disasm.disasmEditor';
		disasmPanelProvider.notifyBinaryFocused(isBinary);
	};
	context.subscriptions.push(
		vscode.window.tabGroups.onDidChangeTabGroups(syncBinaryFocus),
		vscode.window.tabGroups.onDidChangeTabs(syncBinaryFocus),
	);

	// Register socket event handlers
	socketClient.onConnected(() => {
		outputChannel.appendLine('[INFO] Connected to C# server');
		vscode.window.showInformationMessage('Connected to C# server');
		// Auto-open all live memory views (reuses existing panels if already open)
		disasmPanelProvider.onSocketConnected();
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

	// ── Live memory view command ──────────────────────────────────────────────
	// Called by DisasmPanelProvider after the emulator connects.
	// Opens one editor panel per memory region (EE RAM, IOP RAM, BIOS ROM, Scratchpad).
	const openLiveMemoryCommand = vscode.commands.registerCommand(
		'codedesigner.openLiveMemory',
		async (args: { region: string; address: number; size: number; label: string }) => {
			await editorProvider.openLiveMemoryView(args.region, args.address, args.size, args.label);
		}
	);
	context.subscriptions.push(openLiveMemoryCommand);

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

	// Open all live memory view tabs immediately so they are always visible.
	// When not connected they show placeholder content; when the socket connects
	// they are re-initialized with live data.
	setTimeout(() => {
		disasmPanelProvider.openAllLivePanels().catch(err => {
			outputChannel.appendLine(`[WARN] Could not open live panels at startup: ${err?.message ?? err}`);
		});
	}, 600);
}

// This method is called when your extension is deactivated
export function deactivate() {
	if (socketClient) {
		socketClient.disconnect();
		socketClient = null;
	}
}
