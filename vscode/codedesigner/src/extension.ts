// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import { DisasmEditorProvider } from './providers/disasmEditor/disasmEditorProvider';
import { DisasmPanelProvider } from './providers/disasmPanel/disasmPanelProvider';
import { GsViewerProvider } from './providers/gsViewer/gsViewerProvider';
import { JokerMakerProvider } from './providers/jokerMaker/jokerMakerProvider';
import { Ps2FileEditorProvider } from './providers/ps2FileEditor/ps2FileEditorProvider';
import { CompatBrowserProvider } from './providers/compatBrowser/compatBrowserProvider';
import { CheatsEditorProvider } from './providers/cheatsEditor/cheatsEditorProvider';
import { StructureVisualizerProvider } from './providers/structureVisualizer/structureVisualizerProvider';
import { MemoryCardBrowserProvider } from './providers/memoryCardBrowser/memoryCardBrowserProvider';
import { HddBrowserProvider } from './providers/hddBrowser/hddBrowserProvider';
import { ZipperWorkbenchProvider } from './providers/zipperWorkbench/zipperWorkbenchProvider';
import { SocketClient } from './socketClient';

let socketClient: SocketClient | null = null;
let outputChannel: vscode.LogOutputChannel;
let debugOutputChannel: vscode.LogOutputChannel;
let padStateChannel: vscode.OutputChannel;
let zdbChannel: vscode.OutputChannel;

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

/**
 * Get the PadState channel - receives Joker Maker virtual controller events
 * (button presses, releases, stick motion) and live pad-state updates.
 */
export function getPadStateChannel(): vscode.OutputChannel {
    return padStateChannel;
}

/**
 * Get the ZDB channel - receives debug logs from the zipper workbench webview
 * (file loaded, decoded N textures, material reports, RDR / MPS dumps, etc.).
 * Mirrors the on-page log pane the HTML reference viewers print to.
 */
export function getZdbChannel(): vscode.OutputChannel {
    return zdbChannel;
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

	// Joker Maker / live pad-state events land in their own channel so they
	// don't drown out the main extension log.
	padStateChannel = vscode.window.createOutputChannel('Code Designer PadState');
	context.subscriptions.push(padStateChannel);

	// Zipper workbench debug stream — file-loaded summaries, decode warnings,
	// material / GEO / RDR dump reports. Mirrors the on-page log pane in the
	// HTML reference viewers so users can correlate webview state with logs.
	zdbChannel = vscode.window.createOutputChannel('Code Designer ZDB');
	context.subscriptions.push(zdbChannel);

	// Initialize socket client
	socketClient = new SocketClient('localhost', 8765);

	// Register custom editor for disassembly (after socket client is initialized)
	const editorProvider = new DisasmEditorProvider(socketClient, context.extensionUri);
	context.subscriptions.push(
		vscode.window.registerCustomEditorProvider('disasm.disasmEditor', editorProvider)
	);

	// Register webview panels — pass socketClient so the panel can auto-connect
	const disasmPanelProvider = new DisasmPanelProvider(socketClient, editorProvider, context.extensionUri);
	disasmPanelProvider.register(context);

	// Emulator Connect/Disconnect toggle — surfaced as a panel header action
	// (view/title) so the main panel body stays clean.
	context.subscriptions.push(
		vscode.commands.registerCommand(
			'codedesigner.toggleEmulatorConnection',
			() => disasmPanelProvider.toggleConnectionFromCommand(),
		),
	);

	// Register custom editor for PS2 binary files (.elf, .bin, .raw).
	// This makes VS Code route File → Open for these extensions through
	// CodeDesigner instead of showing a raw text/hex view.
	const ps2FileEditor = new Ps2FileEditorProvider(socketClient, disasmPanelProvider, context.extensionUri);
	context.subscriptions.push(
		vscode.window.registerCustomEditorProvider(Ps2FileEditorProvider.viewType, ps2FileEditor)
	);

	// Wire pause-state propagation: when a breakpoint/step pause is detected in the
	// editor panels, the sidebar status bar needs to update too.
	editorProvider.onPauseStateChange = (paused: boolean) => {
		disasmPanelProvider.notifyPauseState(paused);
	};
// Wire memory-region navigation from the disasm-editor toolbar to the panel provider.
	editorProvider.onOpenMemoryView = async (label: string) => {
		await disasmPanelProvider.openOptionalMemoryView(label);
	};

	// Wire struct / label set changes from the DisasmEditorProvider to the STRUCTS panel.
	// This keeps the tree view in sync with whatever label set is active in the editor.
	// (Commented out — structs panel is disabled for now)
	// editorProvider.onStructsChanged = (structs) => {
	// 	pointerManagerProvider.setServerStructs(structs);
	// };

	// Wire pointer-manager label changes to the disasm instruction display.
	// Whenever the user adds, edits, or deletes a pointer/child label, resolve
	// all live addresses and push the results to any open live panels.
	// NOTE: Do NOT call refreshStructLabels() here — struct labels come from the
	// server and are independent of local pointer definitions.  Calling it here
	// creates an infinite loop: refreshStructLabels → onStructsChanged →
	// setServerStructs → _onDidChangeTreeData → refreshStructLabels → …
	//context.subscriptions.push(
	//	pointerManagerProvider.onDidChangeTreeData(() => {
	//		pointerManagerProvider.getResolvedLabels().then(labels => {
	//			editorProvider.setPointerLabels(labels);
	//		}).catch(() => { /* ignore — not connected */ });
	//	})
	//);

	// Wire pointer-manager labels into the CDS compiler: whenever CDS source is
	// compiled, the current live-resolved labels (e.g. "seal.health") are forwarded
	// to the backend so they can be referenced as :seal.health or @seal.health.
	//disasmPanelProvider.getPointerLabels = () => pointerManagerProvider.getResolvedLabels();

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
		// (Structs panel disabled — pointerManagerProvider.setServerStructs([]) not needed)
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

	// ── File mode: open .elf / .bin / .raw in the internal emulator ──────────
	// Registered as a command so it can be invoked from the command palette
	// or from the command palette.
	const openElfFileCommand = vscode.commands.registerCommand(
		'codedesigner.openElfFile',
		async (uri?: vscode.Uri) => {
			const filePath = uri?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
			if (!filePath) { return; }
			await disasmPanelProvider.openFileMode(filePath);
		}
	);
	context.subscriptions.push(openElfFileCommand);

	// ── GS frame viewer ───────────────────────────────────────────────────────
	// Live PCSX2 display surfaced inside VS Code via the bridge's binary fan-out
	// socket on port 8766. Decoupled from the JSON command channel (port 8765)
	// so 60fps frame traffic never backs up command/response.
	const gsViewerProvider = new GsViewerProvider(context.extensionUri);
	const openGsFrameViewerCommand = vscode.commands.registerCommand(
		'codedesigner.openGsFrameViewer',
		() => gsViewerProvider.open(),
	);
	context.subscriptions.push(openGsFrameViewerCommand);

	// Save a PCSX2 screenshot — surfaced as a command so the GS frame viewer's
	// in-webview Screenshot button can trigger it without a panel round-trip.
	const saveScreenshotCommand = vscode.commands.registerCommand(
		'codedesigner.saveScreenshot',
		() => disasmPanelProvider.handleSaveScreenshot(),
	);
	context.subscriptions.push(saveScreenshotCommand);

	// Joker Maker (virtual PS2 controller)
	// Standalone webview that builds GameShark joker codes from a canvas-rendered
	// controller, mirrors live pad state via PINE, and can drive PCSX2 pad input.
	const jokerMakerProvider = new JokerMakerProvider(context.extensionUri, socketClient);
	const openJokerMakerCommand = vscode.commands.registerCommand(
		'codedesigner.openJokerMaker',
		() => jokerMakerProvider.open(),
	);
	context.subscriptions.push(openJokerMakerCommand);
	disasmPanelProvider.setJokerMakerProvider(jokerMakerProvider);
	socketClient.onConnected(()    => jokerMakerProvider.notifyConnectionChanged());
	socketClient.onDisconnected(() => jokerMakerProvider.notifyConnectionChanged());

	// ── PS2 Game Compatibility browser ──────────────────────────────────────
	// Standalone webview that mirrors PCSX2's published compatibility list into
	// the local database and displays it with cover art.
	const compatBrowserProvider = new CompatBrowserProvider(context.extensionUri, socketClient);
	const openCompatBrowserCommand = vscode.commands.registerCommand(
		'codedesigner.openCompatBrowser',
		() => compatBrowserProvider.open(),
	);
	context.subscriptions.push(openCompatBrowserCommand);

	// Standalone PS2 memory-card browser with save metadata and 3D icon preview.
	const memoryCardBrowserProvider = new MemoryCardBrowserProvider(context.extensionUri, socketClient);
	const openMemoryCardBrowserCommand = vscode.commands.registerCommand(
		'codedesigner.openMemoryCardBrowser',
		(uri?: vscode.Uri) => memoryCardBrowserProvider.open(uri?.fsPath),
	);
	context.subscriptions.push(openMemoryCardBrowserCommand);

	// Standalone PS2 HDD browser with APA/PFS partition tables and save/app icons.
	const hddBrowserProvider = new HddBrowserProvider(context.extensionUri, socketClient);
	const openHddBrowserCommand = vscode.commands.registerCommand(
		'codedesigner.openHddBrowser',
		(uri?: vscode.Uri) => {
			const filePath = uri?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
			hddBrowserProvider.open(filePath);
		},
	);
	context.subscriptions.push(openHddBrowserCommand);

	// Standalone FileFormats/Zipper workbench for ZDB/ZAR/ZED containers,
	// SOCOM II saves, and APACHE00.ZDB patch builds.
	const zipperWorkbenchProvider = new ZipperWorkbenchProvider(context.extensionUri, socketClient);
	const openZipperWorkbenchCommand = vscode.commands.registerCommand(
		'codedesigner.openZipperWorkbench',
		(uri?: vscode.Uri) => {
			const filePath = uri?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
			zipperWorkbenchProvider.open(filePath);
		},
	);
	context.subscriptions.push(openZipperWorkbenchCommand);

	// ── Structure Visualizer ────────────────────────────────────────────────
	// Standalone webview that renders Mermaid diagrams of a game's structure:
	// classes/objects, vtables, and call-graph chains back to a hook.
	const structureVisualizerProvider = new StructureVisualizerProvider(context.extensionUri, socketClient, editorProvider);
	const openStructureVisualizerCommand = vscode.commands.registerCommand(
		'codedesigner.openStructureVisualizer',
		() => structureVisualizerProvider.open(),
	);
	context.subscriptions.push(openStructureVisualizerCommand);

	// ── PS2 Cheats editor ───────────────────────────────────────────────────
	// Three-panel cheat-code editor; also opened from the compatibility
	// browser's per-game Cheats button (passing the game title to pre-select).
	const cheatsEditorProvider = new CheatsEditorProvider(context.extensionUri, socketClient);
	const openCheatsEditorCommand = vscode.commands.registerCommand(
		'codedesigner.openCheatsEditor',
		(gameTitle?: string) => cheatsEditorProvider.open(gameTitle),
	);
	context.subscriptions.push(openCheatsEditorCommand);

	// NOTE: File → Open for .elf/.bin/.raw is now handled by the
	// Ps2FileEditorProvider custom editor registered above (via
	// contributes.customEditors in package.json).  The previous
	// onDidOpenTextDocument handler was removed because VS Code does not
	// fire that event for binary files.

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
