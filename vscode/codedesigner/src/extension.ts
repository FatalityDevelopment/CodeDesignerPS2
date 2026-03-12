// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import { DisasmEditorProvider } from './providers/disasmEditor/disasmEditorProvider';
import { DisasmPanelProvider } from './providers/disasmPanel/disasmPanelProvider';
import { PointerManagerProvider, PointerTreeItem } from './providers/pointerManager/pointerManagerProvider';
import { SocketClient } from './socketClient';

let socketClient: SocketClient | null = null;
let outputChannel: vscode.LogOutputChannel;
let debugOutputChannel: vscode.LogOutputChannel;

/**
 * Get the socket client instance
 */
export function getSocketClient(): SocketClient | null {
    return socketClient;
}

/**
 * Get the output channel instance
 */
export function getOutputChannel(): vscode.LogOutputChannel {
    return outputChannel;
}

/**
 * Get the debug channel instance — used for CDS compilation errors shown with red text.
 */
export function getDebugChannel(): vscode.LogOutputChannel {
    return debugOutputChannel;
}

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {

	// Create output channel for logging — log: true gives colour-coded severity
	// levels (info/warn/error) with icons rendered in the Output panel.
	outputChannel = vscode.window.createOutputChannel('CodeDesigner', { log: true });
	context.subscriptions.push(outputChannel);

	// Create a separate debug channel for CDS compilation errors (shown in red).
	debugOutputChannel = vscode.window.createOutputChannel('CodeDesigner - Debug', { log: true });
	context.subscriptions.push(debugOutputChannel);

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
			outputChannel.info(`Found ${binTabs.length} already-open .bin file(s)`);
			binTabs.forEach(tab => {
				if (tab.input instanceof vscode.TabInputText) {
					const uri = tab.input.uri;
					outputChannel.info(`Reopening ${uri.fsPath} with disassembler`);
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

	// ── Pointer Manager tree view ─────────────────────────────────────────────
	const pointerManagerProvider = new PointerManagerProvider(context);
	context.subscriptions.push(
		vscode.window.registerTreeDataProvider('disasm.pointerManager', pointerManagerProvider)
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('codedesigner.pointerManager.addPointer', () =>
			pointerManagerProvider.addPointer()
		),
		vscode.commands.registerCommand('codedesigner.pointerManager.addChildLabel', (item?: PointerTreeItem) => {
			if (item?.itemType === 'pointer') {
				pointerManagerProvider.addChildLabel(item.address);
			} else {
				vscode.window.showWarningMessage('Select a pointer node first to add a child label.');
			}
		}),
		vscode.commands.registerCommand('codedesigner.pointerManager.editNode', (item?: PointerTreeItem) => {
			if (item) { pointerManagerProvider.editNode(item); }
		}),
		vscode.commands.registerCommand('codedesigner.pointerManager.deleteNode', (item?: PointerTreeItem) => {
			if (item) { pointerManagerProvider.deleteNode(item); }
		}),
		vscode.commands.registerCommand('codedesigner.pointerManager.goToAddress', (addressOrItem: string | PointerTreeItem | undefined) => {
			let addrStr: string | undefined;
			if (typeof addressOrItem === 'string') {
				addrStr = addressOrItem;
			} else if (addressOrItem instanceof PointerTreeItem) {
				addrStr = addressOrItem.address;
			}
			if (!addrStr) { return; }
			const addr = parseInt(addrStr.replace(/^0x/i, ''), 16);
			// Validate the parsed value is a 32-bit address before navigating.
			if (isNaN(addr) || addr < 0 || addr > 0xFFFFFFFF) { return; }
			editorProvider.navigateToAddress(addr >>> 0);
		}),
		vscode.commands.registerCommand('codedesigner.pointerManager.refresh', () =>
			pointerManagerProvider.refresh()
		)
	);

	// Wire pause-state propagation: when a breakpoint/step pause is detected in the
	// editor panels, the sidebar status bar needs to update too.
	editorProvider.onPauseStateChange = (paused: boolean) => {
		disasmPanelProvider.notifyPauseState(paused);
	};

	// Wire snapshot history lookup: when the edit overlay requests address history,
	// delegate to the panel provider which manages snapshots.
	editorProvider.onGetAddressHistory = (address: number) =>
		disasmPanelProvider.getAddressHistory(address);

	// Register socket event handlers
	socketClient.onConnected(() => {
		outputChannel.info('Connected to C# server');
		vscode.window.showInformationMessage('Connected to C# server');
		// Auto-open all live memory views (reuses existing panels if already open)
		disasmPanelProvider.onSocketConnected();
	});

	socketClient.onDisconnected(() => {
		outputChannel.warn('Disconnected from C# server');
		vscode.window.showWarningMessage('Disconnected from C# server');
		// Update the sidebar status bar to STOPPED so the user knows the
		// connection dropped (covers server crashes / unexpected disconnects).
		disasmPanelProvider.notifyDisconnected();
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

	// ── Recompile command ─────────────────────────────────────────────────────
	// Allows the user to pick a disc image, choose an ELF on the disc (defaulting
	// to the BOOT2 main executable), pick an output directory, and run the static
	// recompiler — all from the command palette.
	const recompileCommand = vscode.commands.registerCommand('codedesigner.recompile', async () => {
		if (!socketClient?.isConnected()) {
			vscode.window.showWarningMessage(
				'Not connected to CodeDesigner server. Start the emulator first.');
			return;
		}

		// 1. Pick the ISO file
		const isoUris = await vscode.window.showOpenDialog({
			canSelectFiles: true,
			canSelectFolders: false,
			canSelectMany: false,
			filters: { 'Disc Images': ['iso', 'bin', 'img'], 'All Files': ['*'] },
			title: 'Select PS2 Disc Image',
		});
		if (!isoUris || isoUris.length === 0) { return; }
		const isoPath = isoUris[0].fsPath;

		// 2. Fetch ELF list from the disc
		let elfFiles: import('./socketClient').DiscElfEntry[] = [];
		let bootElf = '';
		try {
			const result = await socketClient.listDiscElfs(isoPath);
			elfFiles = result.elfFiles;
			bootElf  = result.bootElf;
		} catch (err: any) {
			vscode.window.showErrorMessage(`Failed to read disc: ${err?.message ?? err}`);
			return;
		}

		// 3. Let the user pick which ELF to recompile
		const items = elfFiles.map(e => ({
			label:       `${e.IsBootElf ? '★ ' : ''}${e.Name}`,
			description: `${(e.Size / 1024).toFixed(1)} KB  —  ${e.Path}`,
			detail:      e.IsBootElf ? 'Main BOOT2 executable (recommended)' : undefined,
			elfName:     e.Name,
		}));
		if (items.length === 0) {
			vscode.window.showWarningMessage('No ELF or IRX files found on disc.');
			return;
		}
		const picked = await vscode.window.showQuickPick(items, {
			title:             'Select ELF to Recompile',
			placeHolder:       'Choose the ELF file you want to recompile (★ = main game executable)',
			matchOnDescription: true,
		});
		if (!picked) { return; }

		// 4. Pick output directory
		const outUris = await vscode.window.showOpenDialog({
			canSelectFiles: false,
			canSelectFolders: true,
			canSelectMany: false,
			title: 'Select Output Directory for Generated C++ Files',
		});
		const outputDirectory = outUris?.[0]?.fsPath ?? '';

		// 5. Run the recompiler
		await vscode.window.withProgress(
			{
				location: vscode.ProgressLocation.Notification,
				title:    `Recompiling ${picked.elfName}…`,
				cancellable: false,
			},
			async () => {
				try {
					const result = await socketClient!.recompile(
						isoPath, picked.elfName, outputDirectory);
					vscode.window.showInformationMessage(
						`Recompilation complete — ${result.functionCount} function(s) written to ${outputDirectory || 'recomp_out'}.`
					);
				} catch (err: any) {
					vscode.window.showErrorMessage(`Recompilation failed: ${err?.message ?? err}`);
				}
			}
		);
	});
	context.subscriptions.push(recompileCommand);

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

	// Auto-connect on activation — try to reach an already-running server first;
	// if that fails, auto-start the CodeDesigner console app with --server.
	outputChannel.info('Extension activated, attempting to connect to server...');
	setTimeout(() => {
		socketClient?.connect().then(() => {
			outputChannel.info('Auto-connected to server');
		}).catch(err => {
			const errorMsg = err?.message || err?.toString() || 'Unknown error';
			outputChannel.warn(`Failed to auto-connect: ${errorMsg}`);
			outputChannel.info('Auto-starting CodeDesigner server...');
			disasmPanelProvider.autoStartServer().then(started => {
				if (!started) {
					outputChannel.info('No CodeDesigner executable found for auto-start. Use "Run" to start manually.');
				}
				// connectSocketAndOpenViews() is called inside autoStartServer() when
				// a terminal is spawned, so no additional retry is needed here.
			});
		});
	}, 1000);

	// Open all live memory view tabs immediately so they are always visible.
	// When not connected they show placeholder content; when the socket connects
	// they are re-initialized with live data.
	setTimeout(() => {
		disasmPanelProvider.openAllLivePanels().catch(err => {
			outputChannel.warn(`Could not open live panels at startup: ${err?.message ?? err}`);
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
