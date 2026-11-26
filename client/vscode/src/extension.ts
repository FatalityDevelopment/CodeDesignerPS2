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
import { ANIMATION_EDITOR_PAGE, MAP_EDITOR_PAGE, ModelEditorProvider } from './providers/modelEditor/modelEditorProvider';
import { WelcomeProvider, WelcomeDataDelegate } from './providers/welcome/welcomeProvider';
import { SocketClient } from './socketClient';
import { Ps2DebugClient, DEFAULT_PS2DEBUG_ADDRESS } from './ps2debug/ps2DebugClient';
import type { DirectChain } from './ps2debug/directEmulator';
import { registerWebviewDevReload } from './utils/devReload';

let socketClient: SocketClient | null = null;
let outputChannel: vscode.LogOutputChannel;
let debugOutputChannel: vscode.LogOutputChannel;
let cdsCompilerOutputChannel: vscode.LogOutputChannel;
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
 * Get the debug channel instance for verbose extension diagnostics.
 */
export function getDebugChannel(): vscode.LogOutputChannel {
    return debugOutputChannel;
}

/**
 * Get the CDS compiler channel — contains generated codes and compiler logs.
 */
export function getCdsCompilerChannel(): vscode.LogOutputChannel {
    return cdsCompilerOutputChannel;
}

/**
 * Get the PadState channel - receives Controller virtual controller events
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

	// Dev host only: reload open webviews when `npm run watch` rebuilds them.
	registerWebviewDevReload(context);

	// Create output channel for logging — log: true gives colour-coded severity
	// levels (info/warn/error) with icons rendered in the Output panel.
	outputChannel = vscode.window.createOutputChannel('CodeDesigner', { log: true });
	context.subscriptions.push(outputChannel);

	// Create a separate debug channel for verbose extension diagnostics.
	debugOutputChannel = vscode.window.createOutputChannel('CodeDesigner - Debug', { log: true });
	context.subscriptions.push(debugOutputChannel);

	// CDS compiler output lives in its own channel so generated codes, parser
	// diagnostics, sidecar directives, and compiler status do not get mixed into
	// the general extension logs.
	cdsCompilerOutputChannel = vscode.window.createOutputChannel('CodeDesigner - CDS Compiler', { log: true });
	context.subscriptions.push(cdsCompilerOutputChannel);

	// Controller / live pad-state events land in their own channel so they
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
		const what = socketClient?.isBridgeConnected() ? 'C# server' : 'Ps2Emulator';
		outputChannel.info(`Connected to ${what}`);
		vscode.window.showInformationMessage(`Connected to ${what}`);
		// Auto-open all live memory views (reuses existing panels if already open)
		disasmPanelProvider.onSocketConnected();
	});

	socketClient.onDisconnected(() => {
		if (socketClient?.isDirect()) {
			// The Bridge went away; the directly connected emulator is still up.
			outputChannel.warn('Disconnected from C# server (Ps2Emulator is still connected)');
			return;
		}
		outputChannel.warn('Disconnected from server');
		vscode.window.showWarningMessage('Disconnected from server');
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

	// Ps2Emulator's debug server, connected directly: live memory, registers,
	// stepping, breakpoints and disassembly go to it instead of the Bridge.
	// EE and IOP console text from a directly connected Ps2Emulator.
	const ps2ConsoleChannel = vscode.window.createOutputChannel('Ps2Emulator Console');
	context.subscriptions.push(ps2ConsoleChannel);
	context.subscriptions.push(vscode.commands.registerCommand('codedesigner.connectPs2Emulator', async () => {
		const client = socketClient;
		if (!client) {
			return;
		}
		const address = vscode.workspace.getConfiguration('codedesigner')
			.get<string>('ps2Emulator.debugServer', DEFAULT_PS2DEBUG_ADDRESS);
		try {
			const emulator = await Ps2DebugClient.connect(address, {
				onPaused: pause => {
					outputChannel.info(`Ps2Emulator paused (${pause.reason}) at 0x${pause.pc.toString(16).padStart(8, '0')}`
						+ (pause.error ? `: ${pause.error}` : ''));
					disasmPanelProvider.notifyPauseState(true);
				},
				onResumed: () => disasmPanelProvider.notifyPauseState(false),
				onClosed: () => {
					outputChannel.warn(`Ps2Emulator debug server at ${address} closed the connection`);
					client.detachDirect();
				},
			});
			emulator.console((_source, text) => ps2ConsoleChannel.append(text));
			client.attachDirect(emulator);
			outputChannel.info(`Connected to Ps2Emulator at ${address}`);
		} catch (error) {
			vscode.window.showErrorMessage(`Failed to connect to Ps2Emulator: ${(error as Error).message}`);
		}
	}));

	context.subscriptions.push(vscode.commands.registerCommand('codedesigner.disconnectPs2Emulator', () => {
		socketClient?.detachDirect();
	}));

	// Pointer chains to a value, found in Ps2Emulator (memory never leaves it).
	let lastChains: DirectChain[] = [];
	const chainText = (c: DirectChain) =>
		`${c.base.toString(16).padStart(8, '0')} ${c.offsets.map(o => `+${o.toString(16)}`).join(' ')}`;
	const parseAddress = (text: string) => {
		const t = text.trim().replace(/^0x/i, '');
		return /^[0-9a-f]{1,8}$/i.test(t) ? parseInt(t, 16) >>> 0 : undefined;
	};
	const pickChain = async (chains: DirectChain[], title: string) => {
		const picked = await vscode.window.showQuickPick(chains.map(c => ({
			label: chainText(c),
			description: `${c.offsets.length} pointer${c.offsets.length === 1 ? '' : 's'}`
				+ (c.leadsTo !== undefined ? ` → ${c.leadsTo.toString(16).padStart(8, '0')}` : ''),
		})), { title, placeHolder: 'Pick a chain to copy it' });
		if (picked) {
			await vscode.env.clipboard.writeText(picked.label);
			vscode.window.setStatusBarMessage(`Copied ${picked.label}`, 3000);
		}
	};
	context.subscriptions.push(vscode.commands.registerCommand('codedesigner.findPs2Pointers', async () => {
		const direct = socketClient?.getDirect();
		if (!direct) {
			vscode.window.showErrorMessage('Connect to Ps2Emulator first (CodeDesigner: Connect to Ps2Emulator).');
			return;
		}
		const target = await vscode.window.showInputBox({ title: 'Find pointers to', prompt: 'EE address of the value (hex)' });
		const address = target === undefined ? undefined : parseAddress(target);
		if (address === undefined) { return; }
		const bases = await vscode.window.showInputBox({
			title: 'Chains start in',
			prompt: 'Base range, hex start-end (empty: 0x100000 to the end of RAM). Static game data makes the best bases.',
		});
		if (bases === undefined) { return; }
		const [from, to] = bases.split('-').map(parseAddress);
		try {
			const found = await direct.findPointers(address, 4, 0x1000, from ?? 0, to ?? 0, 1000);
			lastChains = found.chains;
			if (found.chains.length === 0) {
				vscode.window.showInformationMessage(`No pointer chains to ${address.toString(16)} (${found.millis} ms).`);
				return;
			}
			await pickChain(found.chains, `${found.chains.length}${found.limited ? '+' : ''} chains to ${address.toString(16)} (${found.millis} ms)`);
		} catch (error) {
			vscode.window.showErrorMessage(`Pointer scan failed: ${(error as Error).message}`);
		}
	}));
	context.subscriptions.push(vscode.commands.registerCommand('codedesigner.checkPs2Pointers', async () => {
		const direct = socketClient?.getDirect();
		if (!direct || lastChains.length === 0) {
			vscode.window.showErrorMessage('Find pointers in Ps2Emulator first.');
			return;
		}
		const target = await vscode.window.showInputBox({
			title: 'Check pointer chains',
			prompt: 'Where the value is now (hex); empty to see where every chain leads',
		});
		if (target === undefined) { return; }
		const address = target.trim() ? parseAddress(target) : undefined;
		try {
			const kept = await direct.checkPointers(lastChains, address);
			if (address !== undefined) { lastChains = kept; }
			if (kept.length === 0) {
				vscode.window.showInformationMessage('No chain leads there now.');
				return;
			}
			await pickChain(kept, `${kept.length} chains`);
		} catch (error) {
			vscode.window.showErrorMessage(`Pointer check failed: ${(error as Error).message}`);
		}
	}));

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

	const compileCdsFileCommand = vscode.commands.registerCommand(
		'codedesigner.compileCdsFile',
		async (uri?: vscode.Uri) => disasmPanelProvider.compileCdsDocument(uri),
	);

	context.subscriptions.push(connectCommand, disconnectCommand, sendMessageCommand, compileCdsFileCommand);

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
	const gsViewerProvider = new GsViewerProvider(context.extensionUri, socketClient);
	const openGsFrameViewerCommand = vscode.commands.registerCommand(
		'codedesigner.openGsFrameViewer',
		() => gsViewerProvider.open(),
	);
	context.subscriptions.push(openGsFrameViewerCommand);

	// ── AI activity feed ────────────────────────────────────────────────────────
	// AI activity now lives inside the shared AI agent control panel. Keep the
	// existing command so command-palette and toolbar entry points remain valid.
	const openAgentActivityCommand = vscode.commands.registerCommand(
		'codedesigner.openAgentActivity',
		() => disasmPanelProvider.openAgentCtrlPanel({ tab: 'activity' }),
	);
	context.subscriptions.push(openAgentActivityCommand);

	// Save a PCSX2 screenshot — surfaced as a command so the GS frame viewer's
	// in-webview Screenshot button can trigger it without a panel round-trip.
	const saveScreenshotCommand = vscode.commands.registerCommand(
		'codedesigner.saveScreenshot',
		() => disasmPanelProvider.handleSaveScreenshot(),
	);
	context.subscriptions.push(saveScreenshotCommand);

	// Controller (virtual PS2 controller)
	// Standalone webview that builds GameShark joker codes from a canvas-rendered
	// controller, mirrors live pad state via PINE, and can drive PCSX2 pad input.
	const jokerMakerProvider = new JokerMakerProvider(context.extensionUri, socketClient);
	const openJokerMakerCommand = vscode.commands.registerCommand(
		'codedesigner.openJokerMaker',
		() => jokerMakerProvider.open(),
	);
	context.subscriptions.push(openJokerMakerCommand);
	disasmPanelProvider.setJokerMakerProvider(jokerMakerProvider);
	jokerMakerProvider.setCheatSource(() => disasmPanelProvider.getGameCheatsForJoker());
	socketClient.onConnected(()    => jokerMakerProvider.notifyConnectionChanged());
	socketClient.onDisconnected(() => jokerMakerProvider.notifyConnectionChanged());
	socketClient.onConnected(()    => gsViewerProvider.notifyConnectionChanged());
	socketClient.onDisconnected(() => gsViewerProvider.notifyConnectionChanged());

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

	// ── Welcome page ────────────────────────────────────────────────────────
	// Landing page shown at activation in place of the old logo splash. Pulls
	// live status from the disasm panel provider via a small data delegate.
	const welcomeDelegate: WelcomeDataDelegate = {
		isBridgeConnected:       () => disasmPanelProvider.isBridgeConnected(),
		isEmulatorRunning:       () => disasmPanelProvider.isSelectedEmulatorRunning(),
		getSelectedEmulatorPath: () => disasmPanelProvider.getSelectedEmulatorPath(),
		getSelectedEmulatorLabel: () => disasmPanelProvider.getSelectedEmulatorLabel(),
		getSelectedBiosPath:     () => disasmPanelProvider.getSelectedBiosPath(),
		getSelectedBiosLabel:    () => disasmPanelProvider.getSelectedBiosLabel(),
		getLoadedGameTitle:      () => disasmPanelProvider.getSelectedGameName(),
		getLoadedGameSerial:     () => disasmPanelProvider.getCurrentGameSerial(),
		connectEmulator:         () => disasmPanelProvider.toggleConnectionFromCommand(),
	};
	const welcomeProvider = new WelcomeProvider(
		context.extensionUri, context.extensionPath, welcomeDelegate,
	);
	context.subscriptions.push(
		vscode.commands.registerCommand('codedesigner.openWelcome', () => welcomeProvider.open()),
	);
	socketClient.onConnected(()    => welcomeProvider.notifyStatusChanged());
	socketClient.onDisconnected(() => welcomeProvider.notifyStatusChanged());

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

	// Model Editor: SOCOM II models and textures through bridge asset sessions
	// (the same ASSET_* commands the assets_* MCP tools use).
	const modelEditorProvider = new ModelEditorProvider(context.extensionUri, socketClient);
	context.subscriptions.push(vscode.commands.registerCommand(
		'codedesigner.openModelEditor',
		(target?: vscode.Uri | string, model?: { archive: string; name: string }) => {
			const filePath = typeof target === 'string' ? target : target?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
			modelEditorProvider.open(filePath, model);
		},
	));

	// Map Editor: the composed map (props, clutter, AIMAPS points) on the same asset sessions.
	const mapEditorProvider = new ModelEditorProvider(context.extensionUri, socketClient, MAP_EDITOR_PAGE);
	context.subscriptions.push(vscode.commands.registerCommand(
		'codedesigner.openMapEditor',
		(target?: vscode.Uri | string) => {
			const filePath = typeof target === 'string' ? target : target?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
			mapEditorProvider.open(filePath);
		},
	));

	// Animation Editor: character clips (package MOTION_S.ZAR + shared MOTION_P.ZAR) played on the package's characters.
	const animationEditorProvider = new ModelEditorProvider(context.extensionUri, socketClient, ANIMATION_EDITOR_PAGE);
	context.subscriptions.push(vscode.commands.registerCommand(
		'codedesigner.openAnimationEditor',
		(target?: vscode.Uri | string) => {
			const filePath = typeof target === 'string' ? target : target?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
			animationEditorProvider.open(filePath);
		},
	));

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
	const cheatsEditorProvider = new CheatsEditorProvider(context.extensionUri, socketClient, editorProvider);
	cheatsEditorProvider.getCurrentGame   = () => disasmPanelProvider.getCurrentGameForCheats();
	cheatsEditorProvider.archiveCloneRoot = () => disasmPanelProvider.getArchiveCloneRoot();
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
