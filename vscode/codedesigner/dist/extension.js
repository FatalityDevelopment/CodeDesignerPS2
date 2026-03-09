/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	var __webpack_modules__ = ([
/* 0 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getSocketClient = getSocketClient;
exports.getOutputChannel = getOutputChannel;
exports.activate = activate;
exports.deactivate = deactivate;
// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
const vscode = __importStar(__webpack_require__(1));
const disasmEditorProvider_1 = __webpack_require__(2);
const disasmPanelProvider_1 = __webpack_require__(7);
const asmPanelProvider_1 = __webpack_require__(12);
const socketClient_1 = __webpack_require__(15);
let socketClient = null;
let outputChannel;
/**
 * Get the socket client instance
 */
function getSocketClient() {
    return socketClient;
}
/**
 * Get the output channel instance
 */
function getOutputChannel() {
    return outputChannel;
}
// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
function activate(context) {
    // Create output channel for logging
    outputChannel = vscode.window.createOutputChannel('CodeDesigner');
    context.subscriptions.push(outputChannel);
    // Initialize socket client
    socketClient = new socketClient_1.SocketClient('localhost', 8765);
    // Register custom editor for disassembly (after socket client is initialized)
    const editorProvider = new disasmEditorProvider_1.DisasmEditorProvider(socketClient);
    context.subscriptions.push(vscode.window.registerCustomEditorProvider('disasm.disasmEditor', editorProvider));
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
    const disasmPanelProvider = new disasmPanelProvider_1.DisasmPanelProvider(socketClient, editorProvider);
    disasmPanelProvider.register(context);
    const asmPanelProvider = new asmPanelProvider_1.AsmPanelProvider();
    asmPanelProvider.register(context);
    // Wire pause-state propagation: when a breakpoint/step pause is detected in the
    // editor panels, the sidebar status bar needs to update too.
    editorProvider.onPauseStateChange = (paused) => {
        disasmPanelProvider.notifyPauseState(paused);
    };
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
        }
        catch (error) {
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
            }
            catch (error) {
                vscode.window.showErrorMessage(`Failed to send message: ${error}`);
            }
        }
        else if (!socketClient?.isConnected()) {
            vscode.window.showWarningMessage('Not connected to server. Use "Connect to Server" command first.');
        }
    });
    context.subscriptions.push(connectCommand, disconnectCommand, sendMessageCommand);
    // ── Live memory view command ──────────────────────────────────────────────
    // Called by DisasmPanelProvider after the emulator connects.
    // Opens one editor panel per memory region (EE RAM, IOP RAM, BIOS ROM, Scratchpad).
    const openLiveMemoryCommand = vscode.commands.registerCommand('codedesigner.openLiveMemory', async (args) => {
        await editorProvider.openLiveMemoryView(args.region, args.address, args.size, args.label);
    });
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
function deactivate() {
    if (socketClient) {
        socketClient.disconnect();
        socketClient = null;
    }
}


/***/ }),
/* 1 */
/***/ ((module) => {

module.exports = require("vscode");

/***/ }),
/* 2 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.DisasmEditorProvider = void 0;
const vscode = __importStar(__webpack_require__(1));
const fs = __importStar(__webpack_require__(3));
const nonce_1 = __webpack_require__(4);
const disasmEditorTemplate_1 = __webpack_require__(5);
const extension_1 = __webpack_require__(0);
class DisasmEditorProvider {
    static HEX_VIEW_SIZE = 0x30;
    static INSTRUCTION_SIZE = 4;
    static BASE_ADDRESS = 0x00000000;
    static PAGE_SIZE = 1000;
    static BUFFER_PAGES = 2;
    socketClient;
    /** All currently-open live memory panels. Used to broadcast pause/resume state. */
    _livePanels = [];
    /** Metadata for each live panel: label, address range. Used for reuse and address lookup. */
    _livePanelInfo = [];
    /**
     * Optional callback invoked when the emulator's pause state changes (e.g. breakpoint hit).
     * Set by DisasmPanelProvider so it can update the sidebar status bar.
     */
    onPauseStateChange;
    constructor(socketClient) {
        this.socketClient = socketClient;
    }
    /**
     * Called by DisasmPanelProvider to propagate sidebar-initiated pause/resume
     * to all editor panels' live-bar state.
     */
    notifyPauseState(paused) {
        this._broadcastStatus(paused);
    }
    /**
     * Format bytes for display (no spaces, big-endian)
     */
    formatBytesForDisplay(buffer, offset, length) {
        const slice = buffer.slice(offset, offset + length);
        return Array.from(slice)
            .reverse()
            .map((b) => b.toString(16).padStart(2, "0").toUpperCase())
            .join("");
    }
    /**
     * Disassemble binary data using the C# backend via socket client
     * @param startAddr - Starting address for disassembly
     * @param buffer - Binary data to disassemble
     * @returns Array of disassembled instruction rows
     */
    async disassembleWithBackend(startAddr, buffer) {
        const rows = [];
        // If no socket client or not connected, return empty array
        if (!this.socketClient || !this.socketClient.isConnected()) {
            const msg = "Socket client not available or not connected, cannot disassemble";
            console.warn(msg);
            const outputChannel = (0, extension_1.getOutputChannel)();
            if (outputChannel) {
                outputChannel.appendLine(`[WARN] ${msg}`);
            }
            return rows;
        }
        try {
            // Convert buffer to hex strings for each 4-byte instruction
            const hexInstructions = [];
            for (let offset = 0; offset < buffer.length; offset += 4) {
                if (offset + 4 <= buffer.length) {
                    // Read 4-byte instruction as little-endian and convert to hex string
                    const instruction = buffer.readUInt32LE(offset);
                    hexInstructions.push(`0x${instruction.toString(16).padStart(8, '0')}`);
                }
            }
            if (hexInstructions.length === 0) {
                return rows;
            }
            // Call C# backend to disassemble
            const hexCode = hexInstructions.join(' ');
            console.log(`[DISASM] Disassembling ${hexInstructions.length} instructions starting at 0x${startAddr.toString(16)}`);
            const results = await this.socketClient.disassemble(hexCode, startAddr);
            console.log(`[DISASM] Received ${results.length} results from backend`);
            // Convert results to row format
            for (let i = 0; i < results.length; i++) {
                const result = results[i];
                const offset = i * DisasmEditorProvider.INSTRUCTION_SIZE;
                const currentAddr = startAddr + offset;
                if (result.Success && result.Assembly) {
                    // Parse the assembly instruction to extract op and args
                    const parts = result.Assembly.trim().split(/\s+/);
                    const op = parts[0] || '';
                    const args = parts.slice(1).join(' ');
                    // Get bytes in display format (space-separated, big-endian)
                    const bytesLE = this.formatBytesForDisplay(buffer, offset, 4);
                    rows.push({
                        addr: currentAddr.toString(16).toUpperCase().padStart(8, "0"),
                        bytes: bytesLE,
                        label: result.Label || "",
                        op: op,
                        args: args,
                        comment: result.Comment || "", // Use comment from backend analyzer
                    });
                }
                else if (result.Error) {
                    // Handle error case
                    const errorMsg = `Disassembly error at offset ${offset}: ${result.Error}`;
                    console.error(`[DISASM] ${errorMsg}`);
                    const bytesLE = this.formatBytesForDisplay(buffer, offset, 4);
                    rows.push({
                        addr: currentAddr.toString(16).toUpperCase().padStart(8, "0"),
                        bytes: bytesLE,
                        label: "",
                        op: "???",
                        args: "",
                        comment: `; Error: ${result.Error}`,
                    });
                }
            }
        }
        catch (error) {
            const errorMsg = error instanceof Error ? error.message : String(error);
            console.error(`[DISASM] Failed to disassemble with backend: ${errorMsg}`);
            vscode.window.showErrorMessage(`Disassembly failed: ${errorMsg}`);
            // Return empty array on error
        }
        return rows;
    }
    openCustomDocument(uri) {
        return { uri, dispose: () => { } };
    }
    async resolveCustomEditor(document, webviewPanel) {
        // Setup webview options
        webviewPanel.webview.options = {
            enableScripts: true
        };
        // Set HTML from template
        const templateData = {};
        webviewPanel.webview.html = (0, disasmEditorTemplate_1.getDisasmEditorHtml)(templateData, (0, nonce_1.getNonce)());
        fs.readFile(document.uri.fsPath, (err, fileData) => {
            if (err) {
                console.error(`[DISASM] Failed to read file: ${err.message}`);
                webviewPanel.webview.postMessage({
                    type: "error",
                    message: err.message,
                });
                return;
            }
            console.log(`[DISASM] File loaded: ${fileData.length} bytes`);
            const totalInstructions = Math.floor(fileData.length / DisasmEditorProvider.INSTRUCTION_SIZE);
            console.log(`[DISASM] Sending init message: ${totalInstructions} total instructions, page size: ${DisasmEditorProvider.PAGE_SIZE}`);
            webviewPanel.webview.postMessage({
                type: "init",
                totalInstructions,
                pageSize: DisasmEditorProvider.PAGE_SIZE,
                baseAddress: DisasmEditorProvider.BASE_ADDRESS,
            });
            console.log(`[DISASM] Requesting first page (startIndex: 0)`);
            this.sendDisasmPage(webviewPanel, fileData, 0);
            console.log(`[DISASM] Sending initial hex view`);
            this.sendHexView(webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS, DisasmEditorProvider.BASE_ADDRESS, DisasmEditorProvider.INSTRUCTION_SIZE);
            webviewPanel.webview.onDidReceiveMessage(async (message) => {
                console.log(`[DISASM] Received message from webview:`, message.type);
                if (message.type === "updateHex") {
                    this.sendHexView(webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS, message.address, message.length || DisasmEditorProvider.INSTRUCTION_SIZE);
                }
                else if (message.type === "requestPage") {
                    await this.sendDisasmPage(webviewPanel, fileData, message.startIndex);
                }
                else if (message.type === "search") {
                    await this.handleSearch(webviewPanel, message.query, message.mode, DisasmEditorProvider.BASE_ADDRESS, fileData.length, undefined, fileData);
                }
                else if (message.type === "scanStrings") {
                    await this.handleFileScan('strings', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
                }
                else if (message.type === "scanFunctions") {
                    await this.handleFileScan('functions', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
                }
                else if (message.type === "getSymbols") {
                    await this.handleGetSymbols(webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
                }
                else if (message.type === "addLabel") {
                    await this.handleAddLabel(webviewPanel, message.address, message.labelName, message.comment);
                }
                else if (message.type === "deleteLabel") {
                    await this.handleDeleteLabel(webviewPanel, message.address);
                }
                else if (message.type === "editLabel") {
                    await this.handleEditLabel(webviewPanel, message.address, message.addressHex, message.dataBytes, message.currentLabel ?? '', message.currentComment ?? '');
                }
                else if (message.type === "saveLabel") {
                    await this.handleSaveLabel(webviewPanel, message.address, message.newLabel ?? '', message.newComment ?? '');
                }
            });
        });
    }
    /**
     * Open a live memory view panel (no backing file — pages fetched via socket).
     * Called once per region after connecting to the embedded emulator server.
     * If a panel for this label already exists (e.g. reconnect), it is revealed
     * and re-initialized with fresh data when connected.
     */
    async openLiveMemoryView(region, baseAddress, totalBytes, label) {
        const connected = this.socketClient?.isConnected() ?? false;
        // Reuse an already-open panel with the same label (e.g. after reconnect)
        const existingInfo = this._livePanelInfo.find(i => i.label === label);
        if (existingInfo) {
            existingInfo.panel.reveal(vscode.ViewColumn.One, true);
            // Re-initialize with live data now that we have a connection
            if (connected) {
                const totalInstructions = Math.floor(totalBytes / DisasmEditorProvider.INSTRUCTION_SIZE);
                existingInfo.panel.webview.postMessage({
                    type: 'init',
                    totalInstructions,
                    pageSize: DisasmEditorProvider.PAGE_SIZE,
                    baseAddress,
                    isLive: true,
                    regionLabel: label,
                    totalBytes,
                });
                await this.sendLiveDisasmPage(existingInfo.panel, region, baseAddress, totalBytes, 0);
                await this.sendLiveHexView(existingInfo.panel, region, baseAddress, baseAddress, DisasmEditorProvider.INSTRUCTION_SIZE);
            }
            return;
        }
        const panel = vscode.window.createWebviewPanel('disasm.liveMemory', label, vscode.ViewColumn.One, { enableScripts: true, retainContextWhenHidden: true });
        // Track this panel for pause/resume broadcast and address lookup.
        const physBase = (baseAddress >= 0x80000000 && baseAddress <= 0xBFFFFFFF)
            ? (baseAddress & 0x1FFFFFFF) >>> 0
            : baseAddress >>> 0;
        const info = { panel, label, region, baseAddress, totalBytes, physBaseAddress: physBase };
        this._livePanels.push(panel);
        this._livePanelInfo.push(info);
        panel.onDidDispose(() => {
            this._livePanels = this._livePanels.filter(p => p !== panel);
            this._livePanelInfo = this._livePanelInfo.filter(info => info.panel !== panel);
        });
        panel.webview.html = (0, disasmEditorTemplate_1.getDisasmEditorHtml)({}, (0, nonce_1.getNonce)());
        const totalInstructions = Math.floor(totalBytes / DisasmEditorProvider.INSTRUCTION_SIZE);
        panel.webview.postMessage({
            type: 'init',
            totalInstructions,
            pageSize: DisasmEditorProvider.PAGE_SIZE,
            baseAddress,
            isLive: true,
            regionLabel: label,
            totalBytes,
        });
        // Always send the first page immediately. When not connected this
        // returns address-only placeholder rows so the grid is visible right
        // away instead of showing a perpetual loading bar. When connected it
        // sends real disassembly data. This direct call is belt-and-suspenders
        // alongside the requestPage roundtrip that fires from the webview's
        // own init handler.
        await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, 0);
        if (connected) {
            await this.sendLiveHexView(panel, region, baseAddress, baseAddress, DisasmEditorProvider.INSTRUCTION_SIZE);
        }
        panel.webview.onDidReceiveMessage(async (message) => {
            if (message.type === 'requestPage') {
                await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, message.startIndex);
            }
            else if (message.type === 'updateHex') {
                await this.sendLiveHexView(panel, region, baseAddress, message.address, message.length || DisasmEditorProvider.INSTRUCTION_SIZE);
            }
            else if (message.type === 'pause') {
                try {
                    await this.socketClient.pause();
                    // Immediately reflect the paused state in every live window so all
                    // panels show "PAUSED" without waiting for their next status poll.
                    this._broadcastStatus(true);
                }
                catch (err) {
                    const msg = err?.message ?? String(err);
                    console.error(`[LIVE] Failed to pause emulator: ${msg}`);
                    vscode.window.showWarningMessage(`Could not pause emulator: ${msg}`);
                }
            }
            else if (message.type === 'resume') {
                try {
                    await this.socketClient.resume();
                    // Immediately reflect the resumed state in every live window.
                    this._broadcastStatus(false);
                }
                catch (err) {
                    const msg = err?.message ?? String(err);
                    console.error(`[LIVE] Failed to resume emulator: ${msg}`);
                    vscode.window.showWarningMessage(`Could not resume emulator: ${msg}`);
                }
            }
            else if (message.type === 'setBreakpoint') {
                try {
                    await this.socketClient.setBreakpoint(message.address);
                }
                catch (err) {
                    console.error(`[LIVE] Failed to set breakpoint: ${err?.message ?? err}`);
                }
            }
            else if (message.type === 'clearBreakpoint') {
                try {
                    await this.socketClient.clearBreakpoint(message.address);
                }
                catch (err) {
                    console.error(`[LIVE] Failed to clear breakpoint: ${err?.message ?? err}`);
                }
            }
            else if (message.type === 'clearAllBreakpoints') {
                try {
                    await this.socketClient.clearAllBreakpoints();
                }
                catch (err) {
                    console.error(`[LIVE] Failed to clear all breakpoints: ${err?.message ?? err}`);
                }
            }
            else if (message.type === 'getStatus') {
                try {
                    const status = await this.socketClient.getPC();
                    const statusMsg = { type: 'status', ...status };
                    // When paused, also fetch registers for the debug panel
                    if (status.paused) {
                        try {
                            const regs = await this.socketClient.getRegisters();
                            statusMsg.registers = regs;
                            // Fetch stack memory (128 bytes from $sp = gpr[29])
                            const sp = regs.gpr[29];
                            if (sp !== undefined) {
                                try {
                                    const stackBuf = await this.socketClient.readMemory(sp, 128, 'ee');
                                    statusMsg.stackBytes = Array.from(stackBuf);
                                    statusMsg.stackBase = sp;
                                }
                                catch { /* ignore stack read errors */ }
                            }
                        }
                        catch { /* ignore register fetch errors */ }
                        // When a breakpoint was hit, bring the correct panel to front
                        // and broadcast the full status to every live window.
                        const hitAddr = status.breakpointHitAddress ?? status.pc;
                        this._revealPanelForAddress(hitAddr);
                        this._broadcastFullStatus(statusMsg);
                    }
                    else {
                        panel.webview.postMessage(statusMsg);
                    }
                }
                catch (err) {
                    console.error(`[LIVE] Failed to get status: ${err?.message ?? err}`);
                }
            }
            else if (message.type === 'search') {
                await this.handleSearch(panel, message.query, message.mode, baseAddress, totalBytes, region);
            }
            else if (message.type === 'scanStrings') {
                await this.handleLiveScan('strings', panel, region, baseAddress, totalBytes);
            }
            else if (message.type === 'scanFunctions') {
                await this.handleLiveScan('functions', panel, region, baseAddress, totalBytes);
            }
            else if (message.type === 'getSymbols') {
                // Live mode: labels from backend; strings/functions are loaded on-demand via scan buttons
                await this.handleGetSymbols(panel, undefined, baseAddress);
            }
            else if (message.type === 'addLabel') {
                await this.handleAddLabel(panel, message.address, message.labelName, message.comment);
            }
            else if (message.type === 'deleteLabel') {
                await this.handleDeleteLabel(panel, message.address);
            }
            else if (message.type === 'editLabel') {
                await this.handleEditLabel(panel, message.address, message.addressHex, message.dataBytes, message.currentLabel ?? '', message.currentComment ?? '');
            }
        });
    }
    /**
     * Reset all live panels: clears their page caches and shows zeroed-out rows.
     * Called by DisasmPanelProvider after triggering an emulator restart.
     */
    resetLivePanels() {
        for (const p of this._livePanels) {
            p.webview.postMessage({ type: 'reset' });
        }
    }
    /**
     * Send a minimal `status` message to all registered live panels.
     * Called immediately after a user-initiated pause or resume so every window
     * updates its live-bar indicator without waiting for the next status-poll cycle.
     * Also fires the onPauseStateChange callback so the sidebar can update its bar.
     */
    _broadcastStatus(paused) {
        const msg = { type: 'status', paused };
        for (const p of this._livePanels) {
            p.webview.postMessage(msg);
        }
        this.onPauseStateChange?.(paused);
    }
    /**
     * Broadcast a full status message (including PC, registers, etc.) to every
     * live panel.  Called when a breakpoint is hit so all windows update at once.
     */
    _broadcastFullStatus(statusMsg) {
        for (const p of this._livePanels) {
            p.webview.postMessage(statusMsg);
        }
    }
    /**
     * Find the live panel whose address range contains `addr` and reveal it so
     * the user immediately sees the paused location.  Strips KSEG0/KSEG1 bits
     * before comparing, matching the MIPS physical-address mapping.
     * Panel base addresses are pre-computed as physBaseAddress at creation time.
     */
    _revealPanelForAddress(addr) {
        const physAddr = (addr >= 0x80000000 && addr <= 0xBFFFFFFF)
            ? (addr & 0x1FFFFFFF) >>> 0
            : addr >>> 0;
        for (const info of this._livePanelInfo) {
            if (physAddr >= info.physBaseAddress && physAddr < info.physBaseAddress + info.totalBytes) {
                info.panel.reveal(vscode.ViewColumn.One, false);
                return;
            }
        }
    }
    async sendLiveDisasmPage(panel, region, baseAddress, totalBytes, startIndex) {
        const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
        const maxBytes = DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE;
        const readLength = Math.min(totalBytes - startOffset, maxBytes);
        if (readLength <= 0) {
            return;
        }
        if (!this.socketClient || !this.socketClient.isConnected()) {
            // When not connected, send placeholder rows with default zero data so the
            // grid is navigable and shows 00000000 / nop before the server connects.
            const count = Math.floor(readLength / DisasmEditorProvider.INSTRUCTION_SIZE);
            const rows = [];
            for (let i = 0; i < count; i++) {
                const addr = baseAddress + startOffset + i * DisasmEditorProvider.INSTRUCTION_SIZE;
                rows.push({
                    addr: addr.toString(16).toUpperCase().padStart(8, '0'),
                    bytes: '00000000',
                    label: '',
                    op: 'nop',
                    args: '',
                    comment: '',
                });
            }
            panel.webview.postMessage({ type: 'disasmPage', rows, startIndex });
            return;
        }
        const startAddr = baseAddress + startOffset;
        try {
            const buffer = await this.socketClient.readMemory(startAddr, readLength, region);
            const rows = await this.disassembleWithBackend(startAddr, buffer);
            panel.webview.postMessage({ type: 'disasmPage', rows, startIndex });
        }
        catch (err) {
            const msg = err?.message ?? String(err);
            console.error(`[LIVE] sendLiveDisasmPage failed at 0x${startAddr.toString(16)}: ${msg}`);
        }
    }
    async sendLiveHexView(panel, region, baseAddress, highlightAddr, highlightLength) {
        if (!this.socketClient || !this.socketClient.isConnected()) {
            return;
        }
        const alignedAddr = Math.max(baseAddress, (highlightAddr & ~0xf) - 0x10);
        try {
            const buffer = await this.socketClient.readMemory(alignedAddr, DisasmEditorProvider.HEX_VIEW_SIZE, region);
            panel.webview.postMessage({
                type: 'hexview',
                data: Array.from(buffer),
                offset: alignedAddr,
                highlight_addr: highlightAddr,
                highlight_length: highlightLength,
            });
        }
        catch (err) {
            const msg = err?.message ?? String(err);
            console.error(`[LIVE] sendLiveHexView failed at 0x${alignedAddr.toString(16)}: ${msg}`);
        }
    }
    /**
     * Unified search via C# backend — works for both live and file-based panels.
     * For live panels pass region and omit fileData.
     * For file panels pass fileData and omit region.
     */
    async handleSearch(panel, query, mode, baseAddress, length, region, fileData) {
        if (!this.socketClient?.isConnected()) {
            panel.webview.postMessage({ type: 'searchResults', matches: [] });
            return;
        }
        try {
            const matches = await this.socketClient.search(baseAddress, length, query, mode, region, fileData);
            panel.webview.postMessage({ type: 'searchResults', matches });
        }
        catch (err) {
            console.error(`[SEARCH] Failed: ${err?.message ?? err}`);
            panel.webview.postMessage({ type: 'searchResults', matches: [] });
        }
    }
    /**
     * Scan a live memory region for strings or functions using the C# backend.
     * Posts a `stringsUpdated` or `functionsUpdated` message to the webview.
     */
    async handleLiveScan(kind, panel, region, baseAddress, totalBytes) {
        if (!this.socketClient?.isConnected()) {
            return;
        }
        try {
            if (kind === 'strings') {
                const strings = await this.socketClient.scanLiveStrings(baseAddress, totalBytes, region);
                panel.webview.postMessage({ type: 'stringsUpdated', strings });
            }
            else {
                const functions = await this.socketClient.scanLiveFunctions(baseAddress, totalBytes, region);
                panel.webview.postMessage({ type: 'functionsUpdated', functions });
            }
        }
        catch (err) {
            console.error(`[SYMBOLS] LiveScan(${kind}) failed: ${err?.message ?? err}`);
            // Send empty result so the webview re-enables the scan button
            if (kind === 'strings') {
                panel.webview.postMessage({ type: 'stringsUpdated', strings: [] });
            }
            else {
                panel.webview.postMessage({ type: 'functionsUpdated', functions: [] });
            }
        }
    }
    /**
     * Scan file binary data for strings or functions using the C# backend.
     * Posts a `stringsUpdated` or `functionsUpdated` message to the webview.
     */
    async handleFileScan(kind, panel, fileData, baseAddress) {
        if (!this.socketClient?.isConnected()) {
            return;
        }
        try {
            if (kind === 'strings') {
                const strings = await this.socketClient.getStrings(fileData, baseAddress);
                panel.webview.postMessage({ type: 'stringsUpdated', strings });
            }
            else {
                const functions = await this.socketClient.getFunctions(fileData, baseAddress);
                panel.webview.postMessage({ type: 'functionsUpdated', functions });
            }
        }
        catch (err) {
            console.error(`[SYMBOLS] FileScan(${kind}) failed: ${err?.message ?? err}`);
            if (kind === 'strings') {
                panel.webview.postMessage({ type: 'stringsUpdated', strings: [] });
            }
            else {
                panel.webview.postMessage({ type: 'functionsUpdated', functions: [] });
            }
        }
    }
    /**
     * Collect labels, strings, and functions from the backend and post them
     * back to the webview as a single `symbols` message.
     * For live panels that have no backing fileData, pass `undefined`; in that
     * case strings and functions are returned empty.
     */
    async handleGetSymbols(panel, fileData, baseAddress) {
        let labels = [];
        let strings = [];
        let functions = [];
        if (this.socketClient?.isConnected()) {
            try {
                labels = await this.socketClient.getLabels();
            }
            catch { /* ignore */ }
            if (fileData && fileData.length > 0) {
                try {
                    strings = await this.socketClient.getStrings(fileData, baseAddress);
                }
                catch { /* ignore */ }
                try {
                    functions = await this.socketClient.getFunctions(fileData, baseAddress);
                }
                catch { /* ignore */ }
            }
        }
        panel.webview.postMessage({ type: 'symbols', labels, strings, functions });
    }
    /**
     * Open a VSCode multi-step input dialog to edit the label and comment for an
     * address.  The title bar shows the address and raw instruction bytes so the
     * user has full context.  Pressing Escape on either step cancels the whole
     * operation with no changes saved.
     *
     * @param addressHex - Hex digits only (no "0x" prefix), e.g. "80010000"
     */
    async handleEditLabel(panel, address, addressHex, dataBytes, currentLabel, currentComment) {
        // addressHex is plain hex digits (no "0x" prefix) as produced by
        // toString(16).toUpperCase().padStart(8,"0") in the webview.
        const addrDisplay = `0x${addressHex.toUpperCase()}`;
        const dataDisplay = dataBytes ? ` | Data: ${dataBytes}` : '';
        const newLabel = await vscode.window.showInputBox({
            title: `Edit Label — ${addrDisplay}${dataDisplay}`,
            prompt: 'Label (leave empty to remove)',
            value: currentLabel,
            placeHolder: 'Label name',
            ignoreFocusOut: true,
        });
        if (newLabel === undefined) {
            return;
        } // cancelled
        const newComment = await vscode.window.showInputBox({
            title: `Edit Comment — ${addrDisplay}`,
            prompt: 'Comment (leave empty to remove)',
            value: currentComment,
            placeHolder: 'Comment',
            ignoreFocusOut: true,
        });
        if (newComment === undefined) {
            return;
        } // cancelled
        if (!newLabel && !newComment) {
            // Both empty — delete the label if it existed
            if (currentLabel || currentComment) {
                await this.handleDeleteLabel(panel, address);
            }
            return;
        }
        await this.handleAddLabel(panel, address, newLabel, newComment || undefined);
    }
    /**
     * Save a label + comment directly (without showInputBox prompts).
     * Called when the user submits the inline edit overlay in the webview.
     */
    async handleSaveLabel(panel, address, newLabel, newComment) {
        if (!newLabel && !newComment) {
            // Both empty — delete the entry if it existed
            await this.handleDeleteLabel(panel, address);
            return;
        }
        await this.handleAddLabel(panel, address, newLabel, newComment || undefined);
    }
    /**
     * Add (or update) a label in the backend database and send back a fresh
     * label list so the symbols panel refreshes instantly.
     */
    async handleAddLabel(panel, address, labelName, comment) {
        if (!this.socketClient?.isConnected()) {
            return;
        }
        try {
            await this.socketClient.addLabel(address, labelName, comment);
            const labels = await this.socketClient.getLabels();
            panel.webview.postMessage({ type: 'labelsUpdated', labels });
        }
        catch (err) {
            console.error(`[SYMBOLS] AddLabel failed: ${err?.message ?? err}`);
        }
    }
    /**
     * Delete a label in the backend database and send back a fresh label list.
     */
    async handleDeleteLabel(panel, address) {
        if (!this.socketClient?.isConnected()) {
            return;
        }
        try {
            await this.socketClient.deleteLabel(address);
            const labels = await this.socketClient.getLabels();
            panel.webview.postMessage({ type: 'labelsUpdated', labels });
        }
        catch (err) {
            console.error(`[SYMBOLS] DeleteLabel failed: ${err?.message ?? err}`);
        }
    }
    async sendDisasmPage(webviewPanel, fileData, startIndex) {
        console.log(`[DISASM] sendDisasmPage called: startIndex=${startIndex}`);
        const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
        const endOffset = Math.min(fileData.length, startOffset + DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE);
        console.log(`[DISASM] Extracting bytes ${startOffset}-${endOffset} (${endOffset - startOffset} bytes)`);
        const slice = fileData.slice(startOffset, endOffset);
        const startAddr = DisasmEditorProvider.BASE_ADDRESS + startOffset;
        console.log(`[DISASM] Calling disassembleWithBackend...`);
        const rows = await this.disassembleWithBackend(startAddr, slice);
        console.log(`[DISASM] Got ${rows.length} disassembled rows`);
        // Comments are now provided by the backend analyzer, no need to call analyzePointers
        // The backend already includes analysis in the Comment field
        console.log(`[DISASM] Sending disasmPage message with ${rows.length} rows, startIndex: ${startIndex}`);
        if (rows.length > 0) {
            console.log(`[DISASM] First row sample:`, rows[0]);
        }
        webviewPanel.webview.postMessage({
            type: "disasmPage",
            rows,
            startIndex,
        });
    }
    sendHexView(webviewPanel, fileData, baseAddr, highlightAddr, highlightLength) {
        const alignedAddr = Math.max(baseAddr, (highlightAddr & ~0xf) - 0x10);
        const fileOffset = alignedAddr - baseAddr;
        const endOffset = Math.min(fileData.length, fileOffset + DisasmEditorProvider.HEX_VIEW_SIZE);
        const slice = fileData.slice(fileOffset, endOffset);
        console.log(`[DISASM] Sending hexview: ${slice.length} bytes at offset ${alignedAddr}`);
        webviewPanel.webview.postMessage({
            type: "hexview",
            data: Array.from(slice),
            offset: alignedAddr,
            highlight_addr: highlightAddr,
            highlight_length: highlightLength,
        });
    }
}
exports.DisasmEditorProvider = DisasmEditorProvider;


/***/ }),
/* 3 */
/***/ ((module) => {

module.exports = require("fs");

/***/ }),
/* 4 */
/***/ ((__unused_webpack_module, exports) => {


Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getNonce = getNonce;
/**
 * Generates a random nonce string for use in Content-Security-Policy headers.
 * The nonce allows specific inline scripts/styles to execute while blocking others.
 */
function getNonce() {
    let text = "";
    const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    for (let i = 0; i < 32; i++) {
        text += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return text;
}


/***/ }),
/* 5 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


/**
 * Template for the Disassembly Editor
 * Large complex HTML moved from external file to TypeScript module
 */
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getDisasmEditorHtml = getDisasmEditorHtml;
const disasmEditor_css_1 = __importDefault(__webpack_require__(6));
function getDisasmEditorHtml(data, nonce) {
    return `<!DOCTYPE html> 
<html> 
<head> 
<meta charset="UTF-8"> 
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">

<style>${disasmEditor_css_1.default}</style>

</head> 
 
<body>

<div id="header">
${getHeaderToolbar()}
${getGotoPanel()}
${getSearchPanel()}
${getHexContainer()}
</div>
<div id="content-wrapper">
${getGrid()}
${getSymbolsPanel()}
${getDebugPanel()}
</div>
${getEditOverlay()}

${getScript(nonce)}

</body> 
</html>`;
}
function getGotoPanel() {
    return `<div id="goto-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input
                type="text"
                id="goto-input"
                class="search-input"
                placeholder="Go to address (hex)"
            />
            <button class="search-clear" id="goto-clear">×</button>
        </div>

        <div class="search-nav">
            <button class="nav-btn" id="goto-btn" title="Go to address (Enter)">
                Go
            </button>
        </div>

        <span class="search-info" id="goto-info"></span>

        <button class="close-search" id="close-goto" title="Close (Escape)">×</button>
    </div>
</div>`;
}
function getSearchPanel() {
    return `<div id="search-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input type="text" id="search-input" class="search-input" placeholder="Search (text or bytes: 0x12 34 AB)" />
            <button class="search-clear" id="search-clear">×</button>
        </div>
        <div class="search-mode">
            <button class="mode-btn active" data-mode="text" title="Text Search">Aa</button>
            <button class="mode-btn" data-mode="bytes" title="Byte Pattern Search">0x</button>
            <button class="mode-btn" data-mode="string" title="String Search (ASCII/UTF-8)">"ab"</button>
            <button class="mode-btn" data-mode="regex" title="Regular Expression">.*</button>
        </div>
        <div class="search-nav">
            <button class="nav-btn" id="prev-match" title="Previous Match (Shift+Enter)">↑</button>
            <button class="nav-btn" id="next-match" title="Next Match (Enter)">↓</button>
        </div>
        <span class="search-info" id="search-info">No results</span>
        <button class="close-search" id="close-search" title="Close (Escape)">×</button>
    </div>
</div>`;
}
function getHeaderToolbar() {
    return `<div id="header-toolbar">
    <button class="header-tool-btn" id="symbols-toggle-btn" title="Toggle Symbols Panel (Ctrl+Alt+S)">⊞ Symbols</button>
</div>`;
}
function getHexContainer() {
    return `<div id="hex-container"></div>`;
}
function getGrid() {
    return `<div id="grid" tabindex="0"></div>`;
}
function getSymbolsPanel() {
    return `<div id="symbols-panel">
    <div id="symbols-panel-header">
        <span class="symbols-title">Symbols</span>
        <span id="sym-region-label" class="sym-region-label"></span>
        <button class="sym-header-btn" id="symbols-refresh-btn" title="Refresh symbols">↻</button>
        <button class="sym-header-btn" id="close-symbols-btn" title="Close">×</button>
    </div>
    <div id="sym-tabs">
        <button class="sym-tab active" data-symtab="labels">Labels</button>
        <button class="sym-tab" data-symtab="strings">Strings</button>
        <button class="sym-tab" data-symtab="functions">Functions</button>
    </div>
    <div id="sym-labels-content" class="sym-tab-content active">
        <div id="sym-add-row">
            <input type="text" id="sym-new-addr"    class="sym-add-input" placeholder="0x00000000" maxlength="10" />
            <input type="text" id="sym-new-name"    class="sym-add-input" placeholder="Label name" />
            <input type="text" id="sym-new-comment" class="sym-add-input" placeholder="Comment (opt.)" />
            <button id="sym-add-btn" class="sym-add-btn" title="Add label">+</button>
        </div>
        <div id="sym-labels-list" class="sym-list"></div>
    </div>
    <div id="sym-strings-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-strings-btn" class="sym-scan-btn">Scan for strings</button>
            <span id="sym-strings-status" class="sym-status"></span>
        </div>
        <div id="sym-strings-list" class="sym-list"></div>
    </div>
    <div id="sym-functions-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-functions-btn" class="sym-scan-btn">Scan for functions</button>
            <span id="sym-functions-status" class="sym-status"></span>
        </div>
        <div id="sym-functions-list" class="sym-list"></div>
    </div>
</div>`;
}
function getDebugPanel() {
    return `<div id="debug-panel">
    <div id="debug-tabs">
        <button class="debug-tab active" data-tab="registers">GPR</button>
        <button class="debug-tab" data-tab="cop0">CP0</button>
        <button class="debug-tab" data-tab="fpr">FPR</button>
        <button class="debug-tab" data-tab="fcr">FCR</button>
        <button class="debug-tab" data-tab="vu0f">VU0f</button>
        <button class="debug-tab" data-tab="vu0i">VU0i</button>
        <button class="debug-tab" data-tab="gs">GS</button>
        <button class="debug-tab" data-tab="stack">Stack</button>
    </div>
    <div id="registers-content" class="debug-tab-content active"></div>
    <div id="cop0-content" class="debug-tab-content"></div>
    <div id="fpr-content" class="debug-tab-content"></div>
    <div id="fcr-content" class="debug-tab-content"></div>
    <div id="vu0f-content" class="debug-tab-content"></div>
    <div id="vu0i-content" class="debug-tab-content"></div>
    <div id="gs-content" class="debug-tab-content"></div>
    <div id="stack-content" class="debug-tab-content"></div>
</div>`;
}
function getEditOverlay() {
    return `<div id="edit-overlay">
    <div id="edit-overlay-box">
        <div id="edit-overlay-header">
            <span id="edit-overlay-title">Edit Address</span>
            <button id="edit-overlay-close" title="Close (Escape)">×</button>
        </div>
        <div id="edit-overlay-info"></div>
        <div class="edit-overlay-field">
            <label class="edit-overlay-label" for="edit-overlay-label-input">Label</label>
            <input type="text" id="edit-overlay-label-input" class="edit-overlay-input" placeholder="Label name (leave empty to remove)" />
        </div>
        <div class="edit-overlay-field">
            <label class="edit-overlay-label" for="edit-overlay-comment-input">Comment</label>
            <input type="text" id="edit-overlay-comment-input" class="edit-overlay-input" placeholder="Comment (leave empty to remove)" />
        </div>
        <div id="edit-overlay-actions">
            <button id="edit-overlay-save">Save</button>
            <button id="edit-overlay-cancel">Cancel</button>
        </div>
    </div>
</div>`;
}
function getScript(nonce) {
    // The JavaScript content from the HTML file - too large to inline
    // Reading from file would be better, but for now returning the massive script
    return `<script nonce="${nonce}">
${getJavaScriptContent()}
</script>`;
}
function getJavaScriptContent() {
    // Return the entire JavaScript from the HTML file
    // This is the 600+ line script - I'll include it inline
    return `
const vscode = acquireVsCodeApi();
const hexContainer = document.getElementById("hex-container");
const grid = document.getElementById("grid");
const searchPanel = document.getElementById("search-panel");
const searchInput = document.getElementById("search-input");
const searchClear = document.getElementById("search-clear");
const searchInfo = document.getElementById("search-info");
const closeSearch = document.getElementById("close-search");
const prevMatch = document.getElementById("prev-match");
const nextMatch = document.getElementById("next-match");

const gotoPanel = document.getElementById("goto-panel");
const gotoInput = document.getElementById("goto-input");
const gotoClear = document.getElementById("goto-clear");
const gotoBtn = document.getElementById("goto-btn");
const gotoInfo = document.getElementById("goto-info");
const closeGoto = document.getElementById("close-goto");
const headerToolbar = document.getElementById("header-toolbar");
const debugPanel = document.getElementById("debug-panel");
const regContent = document.getElementById("registers-content");
const stackContent = document.getElementById("stack-content");

// ── Symbols panel DOM refs ────────────────────────────────────────────────
const symbolsPanel       = document.getElementById("symbols-panel");
const symbolsToggleBtn   = document.getElementById("symbols-toggle-btn");
const symbolsRefreshBtn  = document.getElementById("symbols-refresh-btn");
const closeSymbolsBtn    = document.getElementById("close-symbols-btn");
const symNewAddr         = document.getElementById("sym-new-addr");
const symNewName         = document.getElementById("sym-new-name");
const symNewComment      = document.getElementById("sym-new-comment");
const symAddBtn          = document.getElementById("sym-add-btn");
const symLabelsList      = document.getElementById("sym-labels-list");
const symStringsList     = document.getElementById("sym-strings-list");
const symFunctionsList   = document.getElementById("sym-functions-list");
const symStringsStatus   = document.getElementById("sym-strings-status");
const symFunctionsStatus = document.getElementById("sym-functions-status");
const symScanStringsBtn  = document.getElementById("sym-scan-strings-btn");
const symScanFunctionsBtn= document.getElementById("sym-scan-functions-btn");

// ── Edit overlay DOM refs ─────────────────────────────────────────────────
const editOverlay         = document.getElementById("edit-overlay");
const editOverlayTitle    = document.getElementById("edit-overlay-title");
const editOverlayInfo     = document.getElementById("edit-overlay-info");
const editOverlayLabelIn  = document.getElementById("edit-overlay-label-input");
const editOverlayCommentIn= document.getElementById("edit-overlay-comment-input");
const editOverlaySave     = document.getElementById("edit-overlay-save");
const editOverlayCancel   = document.getElementById("edit-overlay-cancel");
const editOverlayClose    = document.getElementById("edit-overlay-close");

let _editOverlayAddr = 0;
let _editOverlayAddrHex = '';

function openEditOverlay(address, addressHex, op, args, bytes, currentLabel, currentComment) {
    _editOverlayAddr = address;
    _editOverlayAddrHex = addressHex;
    editOverlayTitle.textContent = 'Edit Address — 0x' + addressHex.toUpperCase();
    const instrStr = (op || '') + (args ? '  ' + args : '');
    editOverlayInfo.textContent = (bytes ? bytes + '   ' : '') + instrStr;
    editOverlayLabelIn.value = currentLabel || '';
    editOverlayCommentIn.value = currentComment || '';
    editOverlay.classList.add('visible');
    editOverlayLabelIn.focus();
    editOverlayLabelIn.select();
}

function closeEditOverlay() {
    editOverlay.classList.remove('visible');
}

function saveEditOverlay() {
    const newLabel   = editOverlayLabelIn.value.trim();
    const newComment = editOverlayCommentIn.value.trim();
    vscode.postMessage({
        type: 'saveLabel',
        address:    _editOverlayAddr,
        addressHex: _editOverlayAddrHex,
        newLabel,
        newComment,
    });
    closeEditOverlay();
}

editOverlaySave.addEventListener('click', saveEditOverlay);
editOverlayCancel.addEventListener('click', closeEditOverlay);
editOverlayClose.addEventListener('click', closeEditOverlay);

editOverlayLabelIn.addEventListener('keydown', e => {
    if (e.key === 'Enter') { editOverlayCommentIn.focus(); editOverlayCommentIn.select(); }
    if (e.key === 'Escape') { closeEditOverlay(); }
});
editOverlayCommentIn.addEventListener('keydown', e => {
    if (e.key === 'Enter') { saveEditOverlay(); }
    if (e.key === 'Escape') { closeEditOverlay(); }
});

editOverlay.addEventListener('click', e => {
    if (e.target === editOverlay) { closeEditOverlay(); }
});

const state = {
    cursor: 0,
    disasmRows: [],
    totalInstructions: 0,
    pageSize: 1000,
    baseAddress: 0,
    totalBytes: 0,
    regionLabel: '',
    hexData: [],
    hexOffset: 0,
    loadedPages: new Map(),
    searchMode: 'text',
    searchQuery: '',
    searchMatches: [],
    currentMatch: -1,
    searchActive: false,
    gotoActive: false,
    symbolsActive: false,
    isLive: false,
    breakpoints: new Set(),
    breakpointHitAddress: undefined,
    // cached symbols data
    symLabels: [],
    symStrings: [],
    symFunctions: [],
};

let liveRefreshTimer = null;
let liveStatusTimer = null;
let liveRefreshPending = false;
let liveRefreshInterval = 1000;
let livePaused = false;

const clamp = (val, min, max) => Math.max(min, Math.min(val, max));
const getPageStart = index => Math.floor(index / state.pageSize) * state.pageSize;
const getLocalIndex = index => index - getPageStart(index);

window.addEventListener("message", ({ data: msg }) => {
    console.log('[WEBVIEW] Received message:', msg.type, msg);
    switch (msg.type) {
        case "init": {
            console.log('[WEBVIEW] Init:', msg.totalInstructions, 'instructions');
            state.totalInstructions = msg.totalInstructions;
            state.pageSize = msg.pageSize;
            state.baseAddress = msg.baseAddress;
            state.totalBytes = msg.totalBytes ?? (msg.totalInstructions * 4);
            state.regionLabel = msg.regionLabel ?? '';
            // Clear stale page cache and reset pending flag so live refresh
            // can fire immediately after a reconnect.
            state.loadedPages.clear();
            state.disasmRows = [];
            // Always reset cursor to the first instruction so the direct
            // sendLiveDisasmPage(startIndex=0) call from the provider always
            // matches getPageStart(cursor) and triggers renderDisassembly().
            // Without this reset a non-zero cursor (e.g. from a previous scroll
            // within the same VS Code session) causes the page-0 disasmPage
            // to be cached but never rendered, leaving the grid blank.
            state.cursor = 0;
            liveRefreshPending = false;
            if (state.regionLabel) {
                const rl = document.getElementById('sym-region-label');
                if (rl) rl.textContent = \`— \${state.regionLabel}\`;
            }
            if (msg.isLive) {
                state.isLive = true;
                headerToolbar.classList.add('live-active');
                startLiveRefresh();
            }
            // Request the current page immediately. The provider responds with
            // address-only placeholder rows when not connected, or real
            // disassembly data when connected — so the grid becomes visible
            // right away in both cases.
            if (state.totalInstructions > 0) {
                vscode.postMessage({ type: 'requestPage', startIndex: getPageStart(state.cursor) });
            }
            break;
        }
        case "disasmPage":
            console.log('[WEBVIEW] disasmPage:', msg.rows?.length || 0, 'rows, startIndex:', msg.startIndex);
            liveRefreshPending = false;
            state.loadedPages.set(msg.startIndex, msg.rows);
            if (msg.startIndex === getPageStart(state.cursor)) {
                state.disasmRows = msg.rows;
                console.log('[WEBVIEW] Rendering', state.disasmRows.length, 'rows');
                renderDisassembly();
            } else {
                console.log('[WEBVIEW] Page cached but not rendering (cursor at different page)');
            }
            break;
        case "hexview":
            console.log('[WEBVIEW] hexview:', msg.data?.length || 0, 'bytes');
            state.hexData = msg.data;
            state.hexOffset = msg.offset || 0;
            renderHexView(msg.data, state.hexOffset, msg.highlight_addr, msg.highlight_length);
            break;
        case "searchResults":
            console.log('[WEBVIEW] searchResults:', msg.matches?.length || 0, 'matches');
            handleSearchResults(msg.matches);
            break;
        case "status":
            handleStatusUpdate(msg.paused, msg.pc, msg.breakpointHitAddress, msg.registers, msg.stackBytes, msg.stackBase);
            break;
        case "reset":
            handleReset();
            break;
        case "symbols":
            state.symLabels    = msg.labels    ?? [];
            state.symStrings   = msg.strings   ?? [];
            state.symFunctions = msg.functions ?? [];
            renderSymLabels();
            renderSymStrings();
            renderSymFunctions();
            break;
        case "labelsUpdated":
            state.symLabels = msg.labels ?? [];
            renderSymLabels();
            // Invalidate and reload the current page so the disassembly grid reflects the new label
            state.loadedPages.delete(getPageStart(state.cursor));
            checkAndLoadPage();
            break;
        case "stringsUpdated":
            state.symStrings = msg.strings ?? [];
            renderSymStrings();
            if (symStringsStatus) {
                symStringsStatus.textContent = state.symStrings.length > 0
                    ? \`\${state.symStrings.length} found\`
                    : 'None found';
            }
            if (symScanStringsBtn) symScanStringsBtn.disabled = false;
            break;
        case "functionsUpdated":
            state.symFunctions = msg.functions ?? [];
            renderSymFunctions();
            if (symFunctionsStatus) {
                symFunctionsStatus.textContent = state.symFunctions.length > 0
                    ? \`\${state.symFunctions.length} found\`
                    : 'None found';
            }
            if (symScanFunctionsBtn) symScanFunctionsBtn.disabled = false;
            break;
    }
});

function parseAddress(input) {
    const cleaned = input.trim().toLowerCase().replace(/^0x/, '');
    if (!/^[0-9a-f]+$/.test(cleaned)) return null;
    return parseInt(cleaned, 16);
}

function toggleSearch() {
    state.searchActive = !state.searchActive;
    searchPanel.classList.toggle('visible', state.searchActive);
    if (state.searchActive) {
        searchInput.focus();
        searchInput.select();
    } else {
        clearSearchMatches();
    }
}

function parseBytePattern(pattern) {
    const cleaned = pattern.replace(/0x/gi, '').replace(/[,\\s]+/g, ' ').trim();
    if (!cleaned) return null;
    const bytes = cleaned.split(/\\s+/);
    const result = [];
    for (const byte of bytes) {
        if (!/^[0-9a-f]{1,2}$/i.test(byte)) return null;
        result.push(parseInt(byte, 16));
    }
    return result.length > 0 ? result : null;
}

function performSearch() {
    const query = searchInput.value.trim();
    const mode = state.searchMode;
    if (!query) {
        clearSearchMatches();
        return;
    }
    if (mode === 'bytes') {
        const pattern = parseBytePattern(query);
        if (!pattern) {
            searchInput.classList.add('error');
            searchInfo.textContent = 'Invalid byte pattern';
            state.searchMatches = [];
            state.currentMatch = -1;
            updateSearchInfo();
            return;
        }
    }
    searchInput.classList.remove('error');
    state.searchQuery = query;
    vscode.postMessage({ type: 'search', query: query, mode: mode });
    searchInfo.textContent = 'Searching...';
}

function handleSearchResults(matches) {
    state.searchMatches = matches || [];
    if (state.searchMatches.length > 0) {
        state.currentMatch = 0;
        jumpToMatch(0);
    } else {
        state.currentMatch = -1;
        updateSearchInfo();
        renderDisassembly();
    }
}

function jumpToMatch(matchIndex) {
    if (matchIndex < 0 || matchIndex >= state.searchMatches.length) return;
    state.currentMatch = matchIndex;
    const match = state.searchMatches[matchIndex];
    state.cursor = match.instructionIndex;
    const pageReady = checkAndLoadPage();
    if (pageReady) {
        updateCursor();
    }
    updateSearchInfo();
}

function nextSearchMatch() {
    if (state.searchMatches.length === 0) return;
    const next = (state.currentMatch + 1) % state.searchMatches.length;
    jumpToMatch(next);
}

function prevSearchMatch() {
    if (state.searchMatches.length === 0) return;
    const prev = (state.currentMatch - 1 + state.searchMatches.length) % state.searchMatches.length;
    jumpToMatch(prev);
}

function clearSearchMatches() {
    state.searchMatches = [];
    state.currentMatch = -1;
    state.searchQuery = '';
    searchInput.value = '';
    searchInput.classList.remove('error');
    updateSearchInfo();
    renderDisassembly();
}

function updateSearchInfo() {
    if (state.searchMatches.length === 0) {
        searchInfo.textContent = state.searchQuery ? 'No results' : '';
        prevMatch.disabled = true;
        nextMatch.disabled = true;
    } else {
        searchInfo.textContent = \`\${state.currentMatch + 1} of \${state.searchMatches.length}\`;
        prevMatch.disabled = false;
        nextMatch.disabled = false;
    }
}

function isRowSearchMatch(globalIndex) {
    return state.searchMatches.some(m => m.instructionIndex === globalIndex);
}

function isRowCurrentMatch(globalIndex) {
    if (state.currentMatch === -1) return false;
    const match = state.searchMatches[state.currentMatch];
    return match && match.instructionIndex === globalIndex;
}

function toggleGoto() {
    state.gotoActive = !state.gotoActive;
    gotoPanel.classList.toggle('visible', state.gotoActive);
    if (state.gotoActive) {
        gotoInput.focus();
        gotoInput.select();
    } else {
        gotoInput.value = '';
        gotoInfo.textContent = '';
    }
}

function goToAddress() {
    const value = gotoInput.value;
    const addr = parseAddress(value);
    if (addr === null) {
        gotoInput.classList.add('error');
        gotoInfo.textContent = 'Invalid hex address';
        return;
    }
    gotoInput.classList.remove('error');
    const physAddr  = toPhysAddr(addr);
    const physBase  = toPhysAddr(state.baseAddress);
    const instrIndex = Math.floor((physAddr - physBase) / 4);
    if (instrIndex < 0 || instrIndex >= state.totalInstructions) {
        gotoInfo.textContent = 'Address out of range';
        return;
    }
    state.cursor = instrIndex;
    state.searchActive = false;
    searchPanel.classList.remove('visible');
    const pageReady = checkAndLoadPage();
    if (pageReady) {
        updateCursor();
    }
    gotoInfo.textContent = \`Moved to 0x\${addr.toString(16).toUpperCase()}\`;
}

document.querySelectorAll('.mode-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.searchMode = btn.dataset.mode;
        if (state.searchMode === 'bytes') {
            searchInput.placeholder = 'Byte pattern: 0x12 34 AB CD';
        } else if (state.searchMode === 'string') {
            searchInput.placeholder = 'String in binary (case-sensitive)';
        } else if (state.searchMode === 'regex') {
            searchInput.placeholder = 'Regular expression';
        } else {
            searchInput.placeholder = 'Search text';
        }
        if (searchInput.value.trim()) {
            performSearch();
        }
    });
});

searchInput.addEventListener('input', () => {
    if (!searchInput.value.trim()) {
        clearSearchMatches();
    }
});

searchInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
        e.preventDefault();
        if (e.shiftKey) {
            prevSearchMatch();
        } else {
            if (searchInput.value.trim() && state.searchMatches.length === 0) {
                performSearch();
            } else {
                nextSearchMatch();
            }
        }
    } else if (e.key === 'Escape') {
        toggleSearch();
        grid.focus();
    }
});

searchClear.addEventListener('click', () => {
    clearSearchMatches();
    searchInput.focus();
});

prevMatch.addEventListener('click', prevSearchMatch);
nextMatch.addEventListener('click', nextSearchMatch);

closeSearch.addEventListener('click', () => {
    toggleSearch();
    grid.focus();
});

document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault();
        toggleSearch();
    }
    if (e.key === 'F3' && !e.shiftKey && state.searchActive) {
        e.preventDefault();
        nextSearchMatch();
    }
    if (e.key === 'F3' && e.shiftKey && state.searchActive) {
        e.preventDefault();
        prevSearchMatch();
    }
});

gotoBtn.addEventListener('click', goToAddress);

gotoInput.addEventListener('keydown', e => {
    if (e.key === 'Enter') {
        e.preventDefault();
        goToAddress();
    } else if (e.key === 'Escape') {
        toggleGoto();
        grid.focus();
    }
});

gotoClear.addEventListener('click', () => {
    gotoInput.value = '';
    gotoInfo.textContent = '';
    gotoInput.focus();
});

closeGoto.addEventListener('click', () => {
    toggleGoto();
    grid.focus();
});

function renderHexView(data, offset, highlightAddr = -1, highlightLength = 4) {
    hexContainer.innerHTML = "";
    hexContainer.appendChild(buildHexHeader());
    for (let i = 0; i < data.length; i += 16) {
        hexContainer.appendChild(buildHexRow(data, offset, i, highlightAddr, highlightLength));
    }
}

function buildHexHeader() {
    const header = document.createElement("div");
    header.className = "hex-row";
    header.style.cssText = \`border-bottom: 1px solid #3e3e42; padding-bottom: 4px; margin-bottom: 2px;\`;
    let bytes = "";
    for (let i = 0; i < 16; i++) {
        if (i && i % 4 === 0) bytes += '<span style="width:8px"></span>';
        bytes += \`<span class="hex-byte">+\${i.toString(16).toUpperCase()}</span>\`;
    }
    header.innerHTML = \`<span class="hex-addr">Address:</span><span class="hex-bytes">\${bytes}</span><span class="hex-ascii">0123456789ABCDEF</span>\`;
    return header;
}

function buildHexRow(data, offset, rowOffset, highlightAddr, highlightLength) {
    const row = document.createElement("div");
    row.className = "hex-row";
    const addr = (offset + rowOffset).toString(16).padStart(8, "0").toUpperCase();
    let bytesHtml = "";
    let asciiHtml = "";
    for (let i = 0; i < 16; i++) {
        if (i && i % 4 === 0) bytesHtml += '<span style="width:8px"></span>';
        let byteStr = "  ";
        let char = " ";
        let highlight = false;
        const index = rowOffset + i;
        if (index < data.length) {
            const byte = data[index];
            const byteAddr = offset + index;
            byteStr = byte.toString(16).padStart(2, "0").toUpperCase();
            char = byte >= 32 && byte <= 126 ? String.fromCharCode(byte) : ".";
            highlight = highlightAddr !== -1 && byteAddr >= highlightAddr && byteAddr < highlightAddr + highlightLength;
        }
        bytesHtml += \`<span class="hex-byte\${highlight ? " highlight" : ""}">\${byteStr}</span>\`;
        asciiHtml += \`<span class="hex-char\${highlight ? " highlight" : ""}">\${char}</span>\`;
    }
    row.innerHTML = \`<span class="hex-addr">\${addr}</span><span class="hex-bytes">\${bytesHtml}</span><span class="hex-ascii">\${asciiHtml}</span>\`;
    return row;
}

function renderDisassembly() {
    grid.innerHTML = "";
    const pageStart = getPageStart(state.cursor);
    const localCursor = getLocalIndex(state.cursor);
    state.disasmRows.forEach((rowData, i) => {
        const globalIndex = pageStart + i;
        const row = document.createElement("div");
        row.className = "row";
        row.dataset.index = i;
        row.dataset.globalIndex = globalIndex;
        if (i === localCursor) row.classList.add("active");
        if (isRowSearchMatch(globalIndex)) row.classList.add("search-match");
        if (isRowCurrentMatch(globalIndex)) row.classList.add("search-current");
        const rowAddr = parseInt(rowData.addr, 16);
        if (state.breakpoints.has(rowAddr)) {
            row.classList.add("breakpoint");
            row.classList.add("breakpoint-active");
        }
        // Distinct highlight for the row where execution actually stopped
        if (state.breakpointHitAddress !== undefined) {
            const hitPhys = toPhysAddr(state.breakpointHitAddress);
            const rowPhys = toPhysAddr(rowAddr);
            if (hitPhys === rowPhys) {
                row.classList.add("breakpoint-hit");
            }
        }
        row.innerHTML = buildDisasmRowHtml(rowData, globalIndex);
        row.onclick = () => {
            state.cursor = globalIndex;
            updateCursor();
        };
        row.ondblclick = () => {
            state.cursor = globalIndex;
            updateCursor();
            openEditOverlay(
                parseInt(rowData.addr, 16),
                rowData.addr,
                rowData.op || '',
                rowData.args || '',
                rowData.bytes || '',
                rowData.label || '',
                rowData.comment || ''
            );
        };
        grid.appendChild(row);
    });
    updateCursor();
}

function highlightSearchInText(text, globalIndex, field) {
    if (!state.searchQuery || !text) return text;
    const match = state.searchMatches.find(m => m.instructionIndex === globalIndex && m.field === field);
    if (!match) return text;
    const isCurrent = isRowCurrentMatch(globalIndex);
    const highlightClass = isCurrent ? 'search-current-highlight' : 'search-match-highlight';
    const before = text.substring(0, match.start);
    const highlighted = text.substring(match.start, match.start + match.length);
    const after = text.substring(match.start + match.length);
    return \`\${before}<span class="\${highlightClass}">\${highlighted}</span>\${after}\`;
}

function buildDisasmRowHtml(r, globalIndex) {
    const branchMatch = r.args.match(/^(.*\\$[0-9A-F]{8})\\s*(\\([+-]\\d+[▼▲]\\))(.*)$/);
    let args = branchMatch ? \`\${branchMatch[1]} <span style="color:#6a9955">\${branchMatch[2]}</span>\${branchMatch[3]}\` : r.args;
    const addr = highlightSearchInText(r.addr, globalIndex, 'addr');
    const bytes = highlightSearchInText(r.bytes, globalIndex, 'bytes');
    const label = highlightSearchInText(r.label || "", globalIndex, 'label');
    const op = highlightSearchInText(r.op, globalIndex, 'op');
    args = highlightSearchInText(args, globalIndex, 'args');
    const comment = highlightSearchInText(r.comment || "", globalIndex, 'comment');
    return \`<span class="addr">\${addr}</span><span class="bytes">\${bytes}</span><span class="label">\${label}</span><span class="op">\${op}</span><span class="args">\${args}</span><span class="comment">\${comment}</span>\`;
}

grid.addEventListener("keydown", e => {
    if (state.searchActive && document.activeElement === searchInput) {
        return;
    }
    // ArrowRight: navigate to branch/jump target address on current row
    if (e.key === 'ArrowRight') {
        e.preventDefault();
        navigateToCurrentRowTarget();
        return;
    }
    // F9: toggle breakpoint on current row
    if (e.key === 'F9') {
        e.preventDefault();
        toggleBreakpointAtCursor();
        return;
    }
    const delta = { ArrowDown: 1, ArrowUp: -1, PageDown: 50, PageUp: -50 }[e.key];
    if (!delta) return;
    e.preventDefault();
    state.cursor = clamp(state.cursor + delta, 0, state.totalInstructions - 1);
    const pageReady = checkAndLoadPage();
    if (pageReady) {
        updateCursor();
    }
});

function navigateToCurrentRowTarget() {
    const localIndex = getLocalIndex(state.cursor);
    const row = state.disasmRows[localIndex];
    if (!row) return;

    // 1. Branch target: $XXXXXXXX in args
    const branchMatch = row.args.match(/\\$([0-9A-F]{8})/i);
    if (branchMatch) {
        navigateToAddress(parseInt(branchMatch[1], 16));
        return;
    }

    // 2. Jump target: 0xXXXXXXXX in args (e.g. j/jal instructions)
    const jumpMatch = row.args.match(/0x([0-9A-F]{8})/i);
    if (jumpMatch) {
        navigateToAddress(parseInt(jumpMatch[1], 16));
        return;
    }

    // 3. Any 8-digit hex address in the comment (analyzer-generated)
    if (row.comment) {
        const commentMatch = row.comment.match(/0x([0-9A-F]{8})/i)
            || row.comment.match(/\\b([0-9A-F]{8})\\b/i);
        if (commentMatch) {
            navigateToAddress(parseInt(commentMatch[1], 16));
            return;
        }
    }
}

function navigateToAddress(addr) {
    const physAddr  = toPhysAddr(addr);
    const physBase  = toPhysAddr(state.baseAddress);
    const instrIndex = Math.floor((physAddr - physBase) / 4);
    if (instrIndex < 0 || instrIndex >= state.totalInstructions) return;
    state.cursor = instrIndex;
    const pageReady = checkAndLoadPage();
    if (pageReady) { updateCursor(); }
}

function toggleBreakpointAtCursor() {
    const localIndex = getLocalIndex(state.cursor);
    const row = state.disasmRows[localIndex];
    if (!row) return;
    const addr = parseInt(row.addr, 16);
    if (state.breakpoints.has(addr)) {
        state.breakpoints.delete(addr);
        vscode.postMessage({ type: 'clearBreakpoint', address: addr });
    } else {
        state.breakpoints.add(addr);
        vscode.postMessage({ type: 'setBreakpoint', address: addr });
    }
    renderDisassembly();
}

function checkAndLoadPage() {
    const startIndex = getPageStart(state.cursor);
    if (!state.loadedPages.has(startIndex)) {
        vscode.postMessage({ type: "requestPage", startIndex });
        return false;
    } else {
        const cachedRows = state.loadedPages.get(startIndex);
        if (state.disasmRows !== cachedRows) {
            state.disasmRows = cachedRows;
            renderDisassembly();
        }
        return true;
    }
}

window.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyA') {
        e.preventDefault();
        e.stopPropagation();
        toggleGoto();
    }
    if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyS') {
        e.preventDefault();
        e.stopPropagation();
        toggleSymbolsPanel();
    }
});

function updateCursor() {
    const localIndex = getLocalIndex(state.cursor);
    [...grid.children].forEach(r => {
        r.classList.remove("active", "branch-highlight");
    });
    const rowEl = grid.children[localIndex];
    if (!rowEl) return;
    rowEl.classList.add("active");
    rowEl.scrollIntoView({ block: "nearest", behavior: "instant" });
    const row = state.disasmRows[localIndex];
    if (!row) return;
    handleBranchHighlight(row, localIndex);
    updateHexForRow(row);
}

function updateHexForRow(row) {
    const addr = parseInt(row.addr, 16);
    const byteStr = row.bytes?.trim() || '';
    const length = byteStr.length > 0 ? Math.floor(byteStr.length / 2) : 4;
    const inView = addr >= state.hexOffset && addr + length <= state.hexOffset + state.hexData.length;
    if (inView) {
        renderHexView(state.hexData, state.hexOffset, addr, length);
    } else {
        vscode.postMessage({ type: "updateHex", address: addr, length });
    }
}

function handleBranchHighlight(row, rowIndex) {
    const match = row.args.match(/\\$([0-9A-F]{8})\\s*\\(([+-]\\d+)/);
    if (!match) return;
    const targetAddr = parseInt(match[1], 16);
    const targetIndex = state.disasmRows.findIndex(r => parseInt(r.addr, 16) === targetAddr);
    if (targetIndex === -1) return;
    const start = Math.min(rowIndex, targetIndex);
    const end = Math.max(rowIndex, targetIndex);
    for (let i = start; i <= end; i++) {
        if (i !== rowIndex) {
            grid.children[i]?.classList.add("branch-highlight");
        }
    }
}

// Prevent mouse wheel scrolling — keyboard is the only way to navigate
document.addEventListener('wheel', e => {
    e.preventDefault();
}, { passive: false });

function startLiveRefresh() {
    if (liveRefreshTimer !== null) { return; }
    if (livePaused) { return; }
    liveRefreshTimer = setInterval(doLiveRefresh, liveRefreshInterval);
    // Poll emulator status every 800ms to detect breakpoint-triggered pauses
    if (liveStatusTimer === null) {
        liveStatusTimer = setInterval(() => {
            vscode.postMessage({ type: 'getStatus' });
        }, 800);
    }
}

function stopLiveRefresh() {
    if (liveRefreshTimer !== null) {
        clearInterval(liveRefreshTimer);
        liveRefreshTimer = null;
    }
    if (liveStatusTimer !== null) {
        clearInterval(liveStatusTimer);
        liveStatusTimer = null;
    }
}

function restartLiveRefresh() {
    stopLiveRefresh();
    if (!livePaused) {
        startLiveRefresh();
    }
}

function doLiveRefresh() {
    if (liveRefreshPending) { return; }
    liveRefreshPending = true;
    const startIndex = getPageStart(state.cursor);
    state.loadedPages.delete(startIndex);
    vscode.postMessage({ type: 'requestPage', startIndex });
}

function setLivePausedUi(paused) {
    if (paused) {
        headerToolbar.classList.remove('live-active');
        headerToolbar.classList.add('live-paused');
    } else {
        headerToolbar.classList.remove('live-paused');
        headerToolbar.classList.add('live-active');
    }
}

// Normalize a MIPS virtual address to its physical counterpart.
// KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1 (0xA0000000–0xBFFFFFFF) are both
// direct-mapped: physical = VA & 0x1FFFFFFF.  All other segments are returned
// unchanged.  This lets panels whose baseAddress is a physical address (e.g.
// BIOS ROM at 0x1FC00000) correctly match a PC in KSEG1 (e.g. 0xBFC00000).
// The >>> 0 ensures the result is an unsigned 32-bit JS number regardless of
// JavaScript's signed-integer bitwise semantics.
function toPhysAddr(va) {
    return (va >= 0x80000000 && va <= 0xBFFFFFFF) ? ((va & 0x1FFFFFFF) >>> 0) : (va >>> 0);
}

function handleStatusUpdate(paused, pc, breakpointHitAddress, registers, stackBytes, stackBase) {
    // If the emulator paused itself (e.g. breakpoint) and the user didn't
    // manually pause, mirror that state in the UI.
    if (paused && !livePaused) {
        livePaused = true;
        stopLiveRefresh();
        setLivePausedUi(true);
    }

    // Track which address triggered the pause (undefined when running)
    if (paused) {
        state.breakpointHitAddress = breakpointHitAddress ?? pc;
    } else {
        state.breakpointHitAddress = undefined;
    }

    // Whenever paused (first detection or re-polled), navigate the cursor to the
    // current PC so the disassembler always shows the correct instruction — even
    // for early BIOS addresses where the PC is a KSEG1 virtual address but the
    // panel's baseAddress is a physical address.
    if (paused && pc !== undefined && pc !== null) {
        const physPc   = toPhysAddr(pc);
        const physBase = toPhysAddr(state.baseAddress);
        const instrIndex = Math.floor((physPc - physBase) / 4);
        if (instrIndex >= 0 && instrIndex < state.totalInstructions) {
            state.cursor = instrIndex;
            const pageReady = checkAndLoadPage();
            if (pageReady) { updateCursor(); }
        }
    }

    if (!paused && livePaused) {
        // Emulator resumed externally
        livePaused = false;
        state.breakpointHitAddress = undefined;
        setLivePausedUi(false);
        startLiveRefresh();
        hideDebugPanel();
        return;
    }
    // Update debug panel whenever we are paused and have register data
    if (paused && registers) {
        showDebugPanel(registers, stackBytes, stackBase);
    }
}

function handleReset() {
    // Clear all cached pages so zeroed-out rows are displayed immediately
    state.loadedPages.clear();
    state.disasmRows = [];
    state.breakpointHitAddress = undefined;
    const startIndex = getPageStart(state.cursor);
    const zeroRows = generateZeroPage(startIndex);
    state.loadedPages.set(startIndex, zeroRows);
    state.disasmRows = zeroRows;
    renderDisassembly();
    hideDebugPanel();
    // Restart live refresh so the emulator re-runs from the reset vector
    livePaused = false;
    setLivePausedUi(false);
    restartLiveRefresh();
}

function generateZeroPage(startIndex) {
    const rows = [];
    const count = Math.min(state.pageSize, state.totalInstructions - startIndex);
    for (let i = 0; i < count; i++) {
        const addr = state.baseAddress + (startIndex + i) * 4;
        rows.push({
            addr: addr.toString(16).toUpperCase().padStart(8, '0'),
            bytes: '00000000',
            label: '',
            op: 'nop',
            args: '',
            comment: ''
        });
    }
    return rows;
}

// ── Symbols Panel ─────────────────────────────────────────────────────────────

function toggleSymbolsPanel() {
    state.symbolsActive = !state.symbolsActive;
    symbolsPanel.classList.toggle('visible', state.symbolsActive);
    symbolsToggleBtn.classList.toggle('active', state.symbolsActive);
    if (state.symbolsActive) {
        loadSymbols();
    }
}

function loadSymbols() {
    vscode.postMessage({ type: 'getSymbols' });
}

function symNavigate(addrHex) {
    // Strip leading 0x if present, then navigate
    goToAddress(addrHex.replace(/^0x/i, ''));
}

// ── Region helpers ────────────────────────────────────────────────────────────

/** Parse an address hex string (with or without 0x prefix) to a number. */
function parseAddrHex(addrHex) {
    return parseInt(String(addrHex).replace(/^0x/i, ''), 16);
}

/**
 * Returns true when the given address hex string falls within the address
 * range of the currently open panel.  For live panels with a known region
 * this filters out symbols that belong to a different region.  File-based
 * panels (totalBytes == 0) show everything.
 */
function isAddrInCurrentRegion(addrHex) {
    if (!state.isLive || state.totalBytes <= 0) return true;
    const addr = parseAddrHex(addrHex);
    if (isNaN(addr)) return false;
    const physAddr = toPhysAddr(addr);
    const physBase = toPhysAddr(state.baseAddress);
    return physAddr >= physBase && physAddr < physBase + state.totalBytes;
}

/**
 * Maps a numeric address to a short PS2 memory region name shown as a badge.
 * Returns an empty string when the address does not fall in a named region.
 */
function getPs2RegionName(addr) {
    const a = addr >>> 0;
    const p = toPhysAddr(a);
    // EE Main RAM — physical 0x00000000–0x01FFFFFF, or uncached mirror 0x20000000–0x21FFFFFF
    if ((a >= 0x20000000 && a <= 0x21FFFFFF) || p < 0x02000000) return 'EE RAM';
    // IOP RAM — EE VTLB mirror 0x1C000000–0x1DFFFFFF, or IOP physical 0x00000000–0x001FFFFF
    if (a >= 0x1C000000 && a <= 0x1DFFFFFF) return 'IOP';
    // BIOS ROM — physical 0x1FC00000–0x1FFFFFFF (also KSEG1 0xBFC00000)
    if (p >= 0x1FC00000 && p <= 0x1FFFFFFF) return 'BIOS';
    // Scratchpad — 0x70000000–0x70003FFF
    if (a >= 0x70000000 && a <= 0x70003FFF) return 'SPD';
    return '';
}

function renderSymLabels() {
    if (!symLabelsList) return;
    const items = (state.symLabels || []).filter(item => isAddrInCurrentRegion(item.address));
    if (items.length === 0) {
        symLabelsList.innerHTML = '<div class="sym-empty">No labels defined.</div>';
        return;
    }
    symLabelsList.innerHTML = items.map(item => {
        const escapedComment = item.comment ? escHtml(item.comment) : '';
        const comment = escapedComment ? \`<span class="sym-item-comment" title="\${escapedComment}">\${escapedComment}</span>\` : '';
        return \`<div class="sym-item" data-addr="\${escHtml(item.address)}">
            <span class="sym-item-addr">\${escHtml(item.address)}</span>
            <span class="sym-item-name" title="\${escHtml(item.label)}">\${escHtml(item.label)}</span>
            \${comment}
            <button class="sym-delete-btn" data-addr="\${escHtml(item.address)}" title="Delete label">×</button>
        </div>\`;
    }).join('');

    // Navigate on row click (but not on delete button)
    symLabelsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', e => {
            if (e.target.classList.contains('sym-delete-btn')) return;
            symNavigate(el.dataset.addr);
        });
    });
    // Delete button handler
    symLabelsList.querySelectorAll('.sym-delete-btn').forEach(btn => {
        btn.addEventListener('click', e => {
            e.stopPropagation();
            const addrStr = btn.dataset.addr;
            const addrNum = parseInt(addrStr.replace(/^0x/i, ''), 16);
            vscode.postMessage({ type: 'deleteLabel', address: addrNum });
        });
    });
}

function renderSymStrings() {
    if (!symStringsList) return;
    const items = (state.symStrings || []).filter(item => isAddrInCurrentRegion(item.address));
    if (items.length === 0) {
        symStringsList.innerHTML = '<div class="sym-empty">No strings found. Click "Scan for strings".</div>';
        return;
    }
    symStringsList.innerHTML = items.map(item => {
        return \`<div class="sym-item" data-addr="\${escHtml(item.address)}">
            <span class="sym-item-addr">\${escHtml(item.address)}</span>
            <span class="sym-item-value" title="\${escHtml(item.value)}">\${escHtml(item.value)}</span>
        </div>\`;
    }).join('');

    symStringsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate(el.dataset.addr));
    });
}

function renderSymFunctions() {
    if (!symFunctionsList) return;
    const items = (state.symFunctions || []).filter(item => isAddrInCurrentRegion(item.address));
    if (items.length === 0) {
        symFunctionsList.innerHTML = '<div class="sym-empty">No functions found. Click "Scan for functions".</div>';
        return;
    }
    symFunctionsList.innerHTML = items.map(item => {
        return \`<div class="sym-item" data-addr="\${escHtml(item.address)}">
            <span class="sym-item-addr">\${escHtml(item.address)}</span>
            <span class="sym-item-name" title="\${escHtml(item.label)}">\${escHtml(item.label)}</span>
        </div>\`;
    }).join('');

    symFunctionsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate(el.dataset.addr));
    });
}

function escHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// Symbols toggle button
symbolsToggleBtn.addEventListener('click', () => toggleSymbolsPanel());

// Close / refresh buttons inside the panel
closeSymbolsBtn.addEventListener('click', () => {
    state.symbolsActive = false;
    symbolsPanel.classList.remove('visible');
    symbolsToggleBtn.classList.remove('active');
});

symbolsRefreshBtn.addEventListener('click', () => loadSymbols());

// Tab switching inside the symbols panel
document.querySelectorAll('.sym-tab').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.sym-tab').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.sym-tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        const tab = btn.dataset.symtab;
        document.getElementById('sym-' + tab + '-content')?.classList.add('active');
    });
});

// Add label button
symAddBtn.addEventListener('click', () => {
    const addrRaw = symNewAddr.value.trim();
    const name    = symNewName.value.trim();
    const comment = symNewComment.value.trim();
    if (!addrRaw || !name) { return; }
    const addrNum = parseInt(addrRaw.replace(/^0x/i, ''), 16);
    if (isNaN(addrNum)) { return; }
    vscode.postMessage({ type: 'addLabel', address: addrNum, labelName: name, comment: comment || undefined });
    symNewAddr.value = '';
    symNewName.value = '';
    symNewComment.value = '';
});

// Allow Enter key in the add-label inputs to submit
[symNewAddr, symNewName, symNewComment].forEach(input => {
    input?.addEventListener('keydown', e => {
        if (e.key === 'Enter') { symAddBtn.click(); }
    });
});

// Scan buttons — ask the backend to scan the current region directly
symScanStringsBtn.addEventListener('click', () => {
    symStringsStatus.textContent = 'Scanning…';
    symScanStringsBtn.disabled = true;
    vscode.postMessage({ type: 'scanStrings' });
    setTimeout(() => {
        symScanStringsBtn.disabled = false;
        if (symStringsStatus.textContent === 'Scanning…') symStringsStatus.textContent = '';
    }, 120000);
});

symScanFunctionsBtn.addEventListener('click', () => {
    symFunctionsStatus.textContent = 'Scanning…';
    symScanFunctionsBtn.disabled = true;
    vscode.postMessage({ type: 'scanFunctions' });
    setTimeout(() => {
        symScanFunctionsBtn.disabled = false;
        if (symFunctionsStatus.textContent === 'Scanning…') symFunctionsStatus.textContent = '';
    }, 120000);
});

// ── Debug Panel ──────────────────────────────────────────────────────────────

const MIPS_REG_NAMES = [
    '$zero','$at','$v0','$v1','$a0','$a1','$a2','$a3',
    '$t0','$t1','$t2','$t3','$t4','$t5','$t6','$t7',
    '$s0','$s1','$s2','$s3','$s4','$s5','$s6','$s7',
    '$t8','$t9','$k0','$k1','$gp','$sp','$fp','$ra'
];

const COP0_REG_NAMES = [
    'Index','Random','EntryLo0','EntryLo1','Context','PageMask','Wired','Reserved7',
    'BadVAddr','Count','EntryHi','Compare','Status','Cause','EPC','PRId',
    'Config','LLAddr','WatchLo','WatchHi','XContext','Reserved21','Reserved22','Reserved23',
    'Reserved24','PerfCnt','ErrCtl','CacheErr','TagLo','TagHi','ErrorEPC','Reserved31'
];

const GS_PRIV_NAMES = [
    'PMODE','SMODE1','SMODE2','SRFSH','SYNCH1','SYNCH2','SYNCV',
    'DISPFB1','DISPLAY1','DISPFB2','DISPLAY2',
    'EXTBUF','EXTDATA','EXTWRITE','BGCOLOR',
    'CSR','IMR','BUSDIR','SIGLBLID'
];

function hex32(n) {
    return (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

function hex64(lo, hi) {
    // lo and hi are 32-bit halves; hi may be 0 for most regs
    const loStr = (lo >>> 0).toString(16).toUpperCase().padStart(8, '0');
    const hiStr = (hi >>> 0).toString(16).toUpperCase().padStart(8, '0');
    return hiStr + '_' + loStr;
}

// Reinterpret a raw IEEE-754 uint32 bit pattern as a float string.
// Uses a DataView trick to avoid precision loss.
function rawBitsToFloat(bits) {
    const buf = new ArrayBuffer(4);
    new Uint32Array(buf)[0] = bits >>> 0;
    const f = new DataView(buf).getFloat32(0, true);
    // Show up to 6 significant digits; strip trailing zeros for brevity
    return f.toPrecision(6).replace(/\\.?0+$/, '');
}

function showDebugPanel(regs, stackBytes, stackBase) {
    debugPanel.classList.add('visible');
    renderGprPanel(regs);
    renderCop0Panel(regs);
    renderFprPanel(regs);
    renderFcrPanel(regs);
    renderVu0fPanel(regs);
    renderVu0iPanel(regs);
    renderGsPanel(regs);
    renderStackPanel(regs, stackBytes, stackBase);
}

function hideDebugPanel() {
    debugPanel.classList.remove('visible');
}

// ── GPR tab ──────────────────────────────────────────────────────────────────
function renderGprPanel(regs) {
    if (!regs) { regContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">General Purpose Registers</div>';
    html += \`<div class="dbg-row"><span class="dbg-name">PC  </span><span class="dbg-val">\${hex32(regs.pc)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">HI  </span><span class="dbg-val">\${hex32(regs.hi)}</span><span class="dbg-name" style="margin-left:8px">LO  </span><span class="dbg-val">\${hex32(regs.lo)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">HI1 </span><span class="dbg-val">\${hex32(regs.hi1)}</span><span class="dbg-name" style="margin-left:8px">LO1 </span><span class="dbg-val">\${hex32(regs.lo1)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">SA  </span><span class="dbg-val">\${hex32(regs.sa)}</span></div>\`;
    html += '<div class="dbg-divider"></div>';
    const gpr = regs.gpr ?? [];
    for (let i = 0; i < 32; i++) {
        const name = MIPS_REG_NAMES[i].padEnd(5);
        const val = hex32(gpr[i] ?? 0);
        const nz  = (gpr[i] ?? 0) !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name">\${name}</span><span class="dbg-val">\${val}</span></div>\`;
    }
    regContent.innerHTML = html;
}

// ── CP0 tab ──────────────────────────────────────────────────────────────────
function renderCop0Panel(regs) {
    const el = document.getElementById('cop0-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">System Control (COP0)</div>';
    const cop0 = regs.cop0Regs ?? [];
    for (let i = 0; i < 32; i++) {
        const name = ('r' + i + ' ' + COP0_REG_NAMES[i]).substring(0, 14).padEnd(14);
        const val  = hex32(cop0[i] ?? 0);
        const nz   = (cop0[i] ?? 0) !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">\${name}</span><span class="dbg-val">\${val}</span></div>\`;
    }
    el.innerHTML = html;
}

// ── FPR tab ──────────────────────────────────────────────────────────────────
function renderFprPanel(regs) {
    const el = document.getElementById('fpr-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">Floating Point Registers (COP1)</div>';
    const fpr = regs.fpr ?? [];
    for (let i = 0; i < 32; i++) {
        const bits = (fpr[i] ?? 0) >>> 0;
        const fval = rawBitsToFloat(bits);
        const nz   = bits !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name">f\${String(i).padStart(2,'0')} </span><span class="dbg-val">\${hex32(bits)}</span><span class="dbg-dim" style="margin-left:6px">\${fval}</span></div>\`;
    }
    html += '<div class="dbg-divider"></div>';
    const accBits = (regs.fprAcc ?? 0) >>> 0;
    html += \`<div class="dbg-row\${accBits !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">ACC  </span><span class="dbg-val">\${hex32(accBits)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat(accBits)}</span></div>\`;
    el.innerHTML = html;
}

// ── FCR tab ──────────────────────────────────────────────────────────────────
function renderFcrPanel(regs) {
    const el = document.getElementById('fcr-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">FPU Control Registers (FCR)</div>';
    const fcr0  = (regs.fcr0  ?? 0) >>> 0;
    const fcr31 = (regs.fcr31 ?? 0) >>> 0;
    html += \`<div class="dbg-row"><span class="dbg-name">FCR0  </span><span class="dbg-val">\${hex32(fcr0)}</span><span class="dbg-dim" style="margin-left:6px">FIR (implementation)</span></div>\`;
    html += \`<div class="dbg-row\${fcr31 !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">FCR31 </span><span class="dbg-val">\${hex32(fcr31)}</span><span class="dbg-dim" style="margin-left:6px">FCSR (control/status)</span></div>\`;
    // Decode key FCR31 fields
    html += '<div class="dbg-divider"></div>';
    html += '<div class="dbg-section-title">FCR31 decode</div>';
    const rm    = fcr31 & 0x3;
    const flags = (fcr31 >> 7)  & 0x1F;
    const ena   = (fcr31 >> 12) & 0x1F;
    const cause = (fcr31 >> 18) & 0x3F;
    const cc    = (fcr31 >> 23) & 0x1;
    const fs    = (fcr31 >> 24) & 0x1;
    const rmStr = ['RN (nearest)','RZ (zero)','RP (+inf)','RM (-inf)'][rm];
    html += \`<div class="dbg-row"><span class="dbg-name">RM    </span><span class="dbg-val">\${rmStr}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Flags </span><span class="dbg-val">\${flags.toString(2).padStart(5,'0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I (sticky)</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Enable</span><span class="dbg-val">\${ena.toString(2).padStart(5,'0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Cause </span><span class="dbg-val">\${cause.toString(2).padStart(6,'0')}</span><span class="dbg-dim" style="margin-left:6px">E V Z O U I</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">CC    </span><span class="dbg-val">\${cc}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">FS    </span><span class="dbg-val">\${fs}</span><span class="dbg-dim" style="margin-left:6px">flush-to-zero</span></div>\`;
    el.innerHTML = html;
}

// ── VU0f tab ─────────────────────────────────────────────────────────────────
function renderVu0fPanel(regs) {
    const el = document.getElementById('vu0f-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">VU0 Float Registers (vf0–vf31)</div>';
    const xs = regs.vu0fX ?? [];
    const ys = regs.vu0fY ?? [];
    const zs = regs.vu0fZ ?? [];
    const ws = regs.vu0fW ?? [];
    for (let i = 0; i < 32; i++) {
        const xb = (xs[i] ?? 0) >>> 0; const yb = (ys[i] ?? 0) >>> 0;
        const zb = (zs[i] ?? 0) >>> 0; const wb = (ws[i] ?? 0) >>> 0;
        const nz = xb !== 0 || yb !== 0 || zb !== 0 || wb !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}">
            <span class="dbg-name">vf\${String(i).padStart(2,'0')}</span>
            <span class="dbg-dim" style="font-size:9px;margin-left:4px">x:\${rawBitsToFloat(xb)} y:\${rawBitsToFloat(yb)} z:\${rawBitsToFloat(zb)} w:\${rawBitsToFloat(wb)}</span>
        </div>\`;
    }
    html += '<div class="dbg-divider"></div>';
    const axb = (regs.vu0AccX ?? 0) >>> 0; const ayb = (regs.vu0AccY ?? 0) >>> 0;
    const azb = (regs.vu0AccZ ?? 0) >>> 0; const awb = (regs.vu0AccW ?? 0) >>> 0;
    html += \`<div class="dbg-row"><span class="dbg-name">ACC  </span><span class="dbg-dim" style="font-size:9px;margin-left:4px">x:\${rawBitsToFloat(axb)} y:\${rawBitsToFloat(ayb)} z:\${rawBitsToFloat(azb)} w:\${rawBitsToFloat(awb)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Q    </span><span class="dbg-val">\${hex32((regs.vu0Q ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat((regs.vu0Q ?? 0) >>> 0)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">P    </span><span class="dbg-val">\${hex32((regs.vu0P ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat((regs.vu0P ?? 0) >>> 0)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">I    </span><span class="dbg-val">\${hex32((regs.vu0I ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat((regs.vu0I ?? 0) >>> 0)}</span></div>\`;
    html += '<div class="dbg-divider"></div>';
    html += \`<div class="dbg-row"><span class="dbg-name">Status</span><span class="dbg-val">\${hex32(regs.vu0Status ?? 0)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">MAC   </span><span class="dbg-val">\${((regs.vu0Mac ?? 0) >>> 0).toString(16).toUpperCase().padStart(4,'0')}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Clip  </span><span class="dbg-val">\${hex32(regs.vu0Clip ?? 0)}</span></div>\`;
    el.innerHTML = html;
}

// ── VU0i tab ─────────────────────────────────────────────────────────────────
function renderVu0iPanel(regs) {
    const el = document.getElementById('vu0i-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">VU0 Integer Registers (vi0–vi15)</div>';
    const vi = regs.vu0i ?? [];
    for (let i = 0; i < 16; i++) {
        const val = (vi[i] ?? 0) & 0xFFFF;
        const nz  = val !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name">vi\${String(i).padStart(2,'0')} </span><span class="dbg-val">\${val.toString(16).toUpperCase().padStart(4,'0')}</span><span class="dbg-dim" style="margin-left:6px">\${val}</span></div>\`;
    }
    el.innerHTML = html;
}

// ── GS tab ───────────────────────────────────────────────────────────────────
function renderGsPanel(regs) {
    const el = document.getElementById('gs-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">Graphics Synthesizer Registers</div>';
    const priv = regs.gsPriv ?? [];
    for (let i = 0; i < GS_PRIV_NAMES.length; i++) {
        const raw = priv[i] ?? 0;
        // Treat as two 32-bit halves (JSON loses upper bits beyond 53-bit safe int)
        const lo = (raw & 0xFFFFFFFF) >>> 0;
        const hi = Math.floor(raw / 0x100000000) >>> 0;
        const valStr = hi !== 0 ? hex64(lo, hi) : hex32(lo);
        const nz = raw !== 0;
        const name = GS_PRIV_NAMES[i].padEnd(10);
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">\${name}</span><span class="dbg-val" style="font-size:10px">\${valStr}</span></div>\`;
    }
    el.innerHTML = html;
}

function renderStackPanel(regs, stackBytes, stackBase) {
    if (!regs) { stackContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    const sp = (regs.gpr ?? [])[29] ?? 0;
    let html = \`<div class="dbg-section-title">Stack  SP=\${hex32(sp)}</div>\`;
    if (!stackBytes || stackBytes.length === 0) {
        html += '<div class="dbg-row dbg-dim">Stack data unavailable</div>';
        stackContent.innerHTML = html;
        return;
    }
    // Show 32 slots (128 bytes)
    for (let i = 0; i < Math.min(stackBytes.length, 128); i += 4) {
        const addr = (stackBase + i) >>> 0;
        const word = ((stackBytes[i+3] << 24) | (stackBytes[i+2] << 16) | (stackBytes[i+1] << 8) | stackBytes[i]) >>> 0;
        const addrStr = hex32(addr);
        const valStr  = hex32(word);
        // Highlight potential return addresses (KSEG0/KSEG1 range)
        const isCodeAddr = word >= 0x80000000 && word < 0xC0000000;
        const cls = isCodeAddr ? ' dbg-retaddr' : '';
        const annotation = isCodeAddr ? ' <span class="dbg-dim">← code ptr</span>' : '';
        html += \`<div class="dbg-row\${cls}"><span class="dbg-addr">\${addrStr}</span><span class="dbg-val">\${valStr}</span>\${annotation}</div>\`;
    }
    stackContent.innerHTML = html;
}

// Debug panel tab switching
document.querySelectorAll('.debug-tab').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.debug-tab').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.debug-tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        const tab = btn.dataset.tab;
        document.getElementById(tab + '-content')?.classList.add('active');
    });
});

window.addEventListener('unload', () => {
    if (liveRefreshTimer !== null) {
        clearInterval(liveRefreshTimer);
        liveRefreshTimer = null;
    }
    if (liveStatusTimer !== null) {
        clearInterval(liveStatusTimer);
        liveStatusTimer = null;
    }
});
`;
}


/***/ }),
/* 6 */
/***/ ((module) => {

module.exports = " \nbody { \n    margin: 0; \n    font-family: monospace; \n    background: #1e1e1e;\n    color: #d4d4d4;\n    height: 100vh;\n    overflow: hidden;\n    display: flex;\n    flex-direction: column;\n}\n\n#header {\n    flex-shrink: 0;\n}\n\n#search-panel {\n    display: none;\n    background: #252526;\n    border-bottom: 1px solid #3e3e42;\n    padding: 8px 12px;\n    position: sticky;\n    top: 0;\n    z-index: 100;\n}\n\n#search-panel.visible {\n    display: block;\n}\n\n.search-row {\n    display: flex;\n    align-items: center;\n    gap: 8px;\n    margin-bottom: 6px;\n}\n\n.search-input-wrapper {\n    position: relative;\n    flex: 1;\n    max-width: 400px;\n}\n\n.search-input {\n    width: 100%;\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 4px 24px 4px 8px;\n    font-family: monospace;\n    font-size: 13px;\n    outline: none;\n}\n\n.search-input:focus {\n    border-color: #007acc;\n}\n\n.search-input.error {\n    border-color: #f48771;\n}\n\n.search-clear {\n    position: absolute;\n    right: 4px;\n    top: 50%;\n    transform: translateY(-50%);\n    background: none;\n    border: none;\n    color: #cccccc;\n    cursor: pointer;\n    padding: 2px 4px;\n    font-size: 16px;\n    line-height: 1;\n    display: none;\n}\n\n.search-clear:hover {\n    color: #fff;\n}\n\n.search-input:not(:placeholder-shown) ~ .search-clear {\n    display: block;\n}\n\n.search-mode {\n    display: flex;\n    gap: 4px;\n}\n\n.mode-btn {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 4px 8px;\n    font-family: monospace;\n    font-size: 11px;\n    cursor: pointer;\n    outline: none;\n}\n\n.mode-btn:hover {\n    background: #505050;\n}\n\n.mode-btn.active {\n    background: #007acc;\n    border-color: #007acc;\n    color: #fff;\n}\n\n.search-nav {\n    display: flex;\n    gap: 4px;\n}\n\n.nav-btn {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 4px 8px;\n    cursor: pointer;\n    font-size: 11px;\n    outline: none;\n    min-width: 24px;\n}\n\n.nav-btn:hover:not(:disabled) {\n    background: #505050;\n}\n\n.nav-btn:disabled {\n    opacity: 0.5;\n    cursor: not-allowed;\n}\n\n.search-info {\n    color: #cccccc;\n    font-size: 11px;\n    white-space: nowrap;\n}\n\n.close-search {\n    background: none;\n    border: none;\n    color: #cccccc;\n    cursor: pointer;\n    padding: 4px 8px;\n    font-size: 16px;\n    margin-left: auto;\n}\n\n.close-search:hover {\n    color: #fff;\n}\n\n#hex-container {\n    font-family: monospace;\n    overflow: auto;\n    max-height: 120px;\n    background: #2d2d30;\n    border-bottom: 1px solid #3e3e42;\n    padding: 4px;\n}\n\n.hex-row {\n    display: flex;\n    gap: 8px;\n    padding: 2px 6px;\n    line-height: 1.4;\n}\n\n.hex-addr {\n    color: #858585;\n    width: 10ch;\n}\n\n.hex-bytes {\n    display: flex;\n    gap: 8px;\n    flex: 1;\n}\n\n.hex-byte {\n    color: #d4d4d4;\n    width: 2ch;\n    text-align: center;\n}\n\n.hex-byte.highlight {\n    background: #264f78;\n    color: #fff;\n}\n\n.hex-ascii {\n    color: #608b4e;\n    width: 16ch;\n    border-left: 1px solid #3e3e42;\n    padding-left: 8px;\n    display: flex;\n}\n\n.hex-char {\n    width: 1ch;\n    display: inline-block;\n    text-align: center;\n}\n\n.hex-char.highlight {\n    background: #264f78;\n    color: #fff;\n}\n \n#grid { \n    outline: none;\n    flex: 1;\n    overflow-y: scroll;\n    scrollbar-width: none; /* Firefox */\n}\n\n#grid::-webkit-scrollbar {\n    display: none; /* Chrome/Safari/Edge */\n} \n\n#content-wrapper {\n    flex: 1;\n    display: flex;\n    overflow: hidden;\n}\n \n.row {\n    display: grid;\n    grid-template-columns: 10ch 12ch 26ch 8ch 34ch 66ch;\n    padding: 2px 6px;\n    cursor: default;\n}\n \n.row.active {\n    background: #264f78;\n}\n\n.row.branch-highlight {\n    background: #3a3d41;\n}\n\n.row.active.branch-highlight {\n    background: #264f78;\n}\n\n.row.breakpoint .addr::before {\n    content: '●';\n    color: #e05555;\n    margin-right: 4px;\n    font-size: 10px;\n}\n\n.row.breakpoint-active {\n    background: #3a1414;\n}\n\n.row.active.breakpoint-active {\n    background: #264f78;\n}\n\n/* Row where execution actually stopped (breakpoint hit) — amber highlight */\n.row.breakpoint-hit {\n    background: #4a3800;\n}\n\n.row.breakpoint-hit .addr::before {\n    content: '▶';\n    color: #f0c040;\n    margin-right: 4px;\n    font-size: 10px;\n}\n\n.row.active.breakpoint-hit {\n    background: #6a5000;\n}\n\n.row.search-match {\n    background: #613214;\n}\n\n.row.search-current {\n    background: #4e3c1e;\n}\n\n.row.active.search-match,\n.row.active.search-current {\n    background: #264f78;\n}\n \n.addr { color: #c586c0; } \n.bytes { color: #9cdcfe; } \n.label { color: #4ec9b0; } \n.op { color: #dcdcaa; } \n.args { color: #ce9178; } \n.comment { color: #6a9955; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } \n\n.search-match-highlight {\n    background: #f9826c;\n    color: #000;\n    padding: 0 2px;\n}\n\n.search-current-highlight {\n    background: #ea5c00;\n    color: #fff;\n    padding: 0 2px;\n}\n\n#goto-panel {\n    display: none;\n    background: #252526;\n    border-bottom: 1px solid #3e3e42;\n    padding: 8px 12px;\n    position: sticky;\n    top: 0;\n    z-index: 100;\n}\n\n#goto-panel.visible {\n    display: block;\n}\n\n/* ── Live mode indicator (inside header-toolbar) ─────────────────────── */\n\n.live-indicator {\n    color: #4ec94e;\n    font-size: 11px;\n    font-weight: bold;\n    animation: live-pulse 1.5s ease-in-out infinite;\n}\n\n.live-indicator.paused {\n    color: #e05555;\n    animation: none;\n}\n\n@keyframes live-pulse {\n    0%, 100% { opacity: 1; }\n    50% { opacity: 0.35; }\n}\n\n#play-pause-btn {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 2px 10px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n}\n\n#play-pause-btn:hover {\n    background: #505050;\n}\n\n#interval-select {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 2px 6px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n}\n\n#interval-select:hover {\n    background: #505050;\n}\n\n/* ── Debug Panel (Registers + Stack) ─────────────────────────────────── */\n\n#debug-panel {\n    width: 320px;\n    min-width: 320px;\n    display: none;\n    flex-direction: column;\n    background: #1e1e1e;\n    border-left: 1px solid #3e3e42;\n    overflow: hidden;\n    font-family: monospace;\n    font-size: 12px;\n}\n\n#debug-panel.visible {\n    display: flex;\n}\n\n#debug-tabs {\n    display: flex;\n    background: #252526;\n    border-bottom: 1px solid #3e3e42;\n    flex-shrink: 0;\n}\n\n.debug-tab {\n    background: none;\n    border: none;\n    border-bottom: 2px solid transparent;\n    color: #858585;\n    padding: 6px 14px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n}\n\n.debug-tab:hover {\n    color: #d4d4d4;\n}\n\n.debug-tab.active {\n    color: #d4d4d4;\n    border-bottom-color: #007acc;\n}\n\n.debug-tab-content {\n    display: none;\n    flex: 1;\n    overflow-y: auto;\n    padding: 4px 0;\n}\n\n.debug-tab-content.active {\n    display: block;\n}\n\n.debug-tab-content::-webkit-scrollbar {\n    width: 6px;\n}\n.debug-tab-content::-webkit-scrollbar-thumb {\n    background: #424242;\n}\n\n.dbg-section-title {\n    color: #858585;\n    font-size: 10px;\n    text-transform: uppercase;\n    padding: 6px 8px 2px;\n    letter-spacing: 0.05em;\n}\n\n.dbg-divider {\n    border-bottom: 1px solid #3e3e42;\n    margin: 4px 8px;\n}\n\n.dbg-row {\n    display: flex;\n    align-items: center;\n    padding: 1px 8px;\n    line-height: 1.6;\n}\n\n.dbg-row:hover {\n    background: #2a2d2e;\n}\n\n.dbg-name {\n    color: #9cdcfe;\n    width: 7ch;\n    flex-shrink: 0;\n    white-space: pre;\n}\n\n.dbg-addr {\n    color: #c586c0;\n    width: 9ch;\n    flex-shrink: 0;\n}\n\n.dbg-val {\n    color: #d4d4d4;\n    font-family: monospace;\n}\n\n.dbg-nonzero .dbg-val {\n    color: #dcdcaa;\n}\n\n.dbg-retaddr .dbg-val {\n    color: #4ec9b0;\n}\n\n.dbg-dim {\n    color: #858585;\n    font-size: 10px;\n    margin-left: 6px;\n}\n\n/* ── Header toolbar (always-visible strip for panel toggles + live status) ── */\n\n#header-toolbar {\n    display: flex;\n    align-items: center;\n    gap: 6px;\n    background: #252526;\n    border-bottom: 1px solid #3e3e42;\n    padding: 2px 8px;\n}\n\n#header-toolbar.live-active {\n}\n\n#header-toolbar.live-paused {\n}\n\n#live-controls {\n    display: none;\n    align-items: center;\n    gap: 8px;\n    margin-left: auto;\n}\n\n#live-controls.visible {\n    display: flex;\n}\n\n.header-tool-btn {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 2px 8px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n    border-radius: 2px;\n}\n\n.header-tool-btn:hover {\n    background: #505050;\n    color: #fff;\n}\n\n.header-tool-btn.active {\n    background: #007acc;\n    border-color: #007acc;\n    color: #fff;\n}\n\n/* ── Symbols Panel ────────────────────────────────────────────────────── */\n\n#symbols-panel {\n    width: 340px;\n    min-width: 340px;\n    display: none;\n    flex-direction: column;\n    background: #1e1e1e;\n    border-left: 1px solid #3e3e42;\n    overflow: hidden;\n    font-family: monospace;\n    font-size: 12px;\n}\n\n#symbols-panel.visible {\n    display: flex;\n}\n\n#symbols-panel-header {\n    display: flex;\n    align-items: center;\n    background: #252526;\n    border-bottom: 1px solid #3e3e42;\n    padding: 4px 8px;\n    flex-shrink: 0;\n    gap: 6px;\n}\n\n.symbols-title {\n    color: #d4d4d4;\n    font-size: 11px;\n    font-weight: bold;\n    text-transform: uppercase;\n    letter-spacing: 0.06em;\n    flex-shrink: 0;\n}\n\n/* Region label shown in the symbols panel header (e.g. \"— IOP RAM\") */\n#sym-region-label {\n    color: #858585;\n    font-size: 11px;\n    flex: 1;\n    white-space: nowrap;\n    overflow: hidden;\n    text-overflow: ellipsis;\n}\n\n/* Compact region badge shown on each symbol row */\n.sym-region-badge {\n    display: inline-block;\n    font-size: 9px;\n    font-family: monospace;\n    background: #2d4a2d;\n    color: #7ec87e;\n    border: 1px solid #3a5c3a;\n    border-radius: 2px;\n    padding: 0 4px;\n    line-height: 14px;\n    flex-shrink: 0;\n    letter-spacing: 0.02em;\n}\n\n.sym-header-btn {\n    background: none;\n    border: none;\n    color: #858585;\n    cursor: pointer;\n    padding: 2px 6px;\n    font-size: 14px;\n    line-height: 1;\n    outline: none;\n}\n\n.sym-header-btn:hover {\n    color: #fff;\n}\n\n#sym-tabs {\n    display: flex;\n    background: #252526;\n    border-bottom: 1px solid #3e3e42;\n    flex-shrink: 0;\n}\n\n.sym-tab {\n    background: none;\n    border: none;\n    border-bottom: 2px solid transparent;\n    color: #858585;\n    padding: 6px 14px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n    flex: 1;\n}\n\n.sym-tab:hover {\n    color: #d4d4d4;\n}\n\n.sym-tab.active {\n    color: #d4d4d4;\n    border-bottom-color: #007acc;\n}\n\n.sym-tab-content {\n    display: none;\n    flex: 1;\n    flex-direction: column;\n    overflow: hidden;\n}\n\n.sym-tab-content.active {\n    display: flex;\n}\n\n/* ── Labels tab ── */\n\n#sym-add-row {\n    display: flex;\n    gap: 4px;\n    padding: 6px 6px 4px;\n    border-bottom: 1px solid #3e3e42;\n    flex-shrink: 0;\n}\n\n.sym-add-input {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 3px 6px;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n    min-width: 0;\n}\n\n.sym-add-input:focus {\n    border-color: #007acc;\n}\n\n#sym-new-addr {\n    width: 9ch;\n    flex-shrink: 0;\n}\n\n#sym-new-name {\n    flex: 1;\n}\n\n#sym-new-comment {\n    flex: 1;\n}\n\n.sym-add-btn {\n    background: #007acc;\n    border: 1px solid #007acc;\n    color: #fff;\n    padding: 3px 8px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 13px;\n    outline: none;\n    flex-shrink: 0;\n}\n\n.sym-add-btn:hover {\n    background: #1a8fd1;\n}\n\n/* ── Scan row (Strings / Functions tabs) ── */\n\n.sym-scan-row {\n    display: flex;\n    align-items: center;\n    gap: 8px;\n    padding: 6px 8px;\n    border-bottom: 1px solid #3e3e42;\n    flex-shrink: 0;\n    background: #252526;\n}\n\n.sym-scan-btn {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 3px 10px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n}\n\n.sym-scan-btn:hover {\n    background: #505050;\n}\n\n.sym-scan-btn:disabled {\n    opacity: 0.5;\n    cursor: not-allowed;\n}\n\n.sym-status {\n    color: #858585;\n    font-size: 10px;\n    flex: 1;\n    white-space: nowrap;\n    overflow: hidden;\n    text-overflow: ellipsis;\n}\n\n/* ── Shared list styles ── */\n\n.sym-list {\n    flex: 1;\n    overflow-y: auto;\n    padding: 2px 0;\n}\n\n.sym-list::-webkit-scrollbar {\n    width: 6px;\n}\n\n.sym-list::-webkit-scrollbar-thumb {\n    background: #424242;\n}\n\n.sym-item {\n    display: flex;\n    align-items: center;\n    padding: 2px 8px;\n    cursor: pointer;\n    gap: 6px;\n    line-height: 1.7;\n}\n\n.sym-item:hover {\n    background: #2a2d2e;\n}\n\n.sym-item-addr {\n    color: #c586c0;\n    width: 10ch;\n    flex-shrink: 0;\n    font-size: 11px;\n}\n\n.sym-item-name {\n    color: #4ec9b0;\n    flex: 1;\n    white-space: nowrap;\n    overflow: hidden;\n    text-overflow: ellipsis;\n    font-size: 11px;\n}\n\n.sym-item-value {\n    color: #ce9178;\n    flex: 1;\n    white-space: nowrap;\n    overflow: hidden;\n    text-overflow: ellipsis;\n    font-size: 11px;\n}\n\n.sym-item-comment {\n    color: #6a9955;\n    font-size: 10px;\n    white-space: nowrap;\n    overflow: hidden;\n    text-overflow: ellipsis;\n    max-width: 100px;\n}\n\n.sym-delete-btn {\n    background: none;\n    border: none;\n    color: #555;\n    cursor: pointer;\n    padding: 0 4px;\n    font-size: 14px;\n    line-height: 1;\n    outline: none;\n    flex-shrink: 0;\n    visibility: hidden;\n}\n\n.sym-item:hover .sym-delete-btn {\n    visibility: visible;\n}\n\n.sym-delete-btn:hover {\n    color: #e05555;\n}\n\n.sym-empty {\n    color: #555;\n    font-size: 11px;\n    padding: 12px 8px;\n    text-align: center;\n}\n\n/* ── Inline address edit overlay ─────────────────────────────────────── */\n\n#edit-overlay {\n    display: none;\n    position: fixed;\n    inset: 0;\n    background: rgba(0,0,0,0.55);\n    z-index: 1000;\n    align-items: center;\n    justify-content: center;\n}\n\n#edit-overlay.visible {\n    display: flex;\n}\n\n#edit-overlay-box {\n    background: #252526;\n    border: 1px solid #3e3e42;\n    border-radius: 4px;\n    width: 420px;\n    max-width: 90vw;\n    padding: 0;\n    font-family: monospace;\n    box-shadow: 0 8px 32px rgba(0,0,0,0.6);\n}\n\n#edit-overlay-header {\n    display: flex;\n    align-items: center;\n    background: #2d2d30;\n    border-bottom: 1px solid #3e3e42;\n    padding: 8px 12px;\n}\n\n#edit-overlay-title {\n    color: #d4d4d4;\n    font-size: 12px;\n    font-weight: bold;\n    flex: 1;\n}\n\n#edit-overlay-close {\n    background: none;\n    border: none;\n    color: #858585;\n    cursor: pointer;\n    font-size: 18px;\n    line-height: 1;\n    padding: 0 4px;\n}\n\n#edit-overlay-close:hover {\n    color: #fff;\n}\n\n#edit-overlay-info {\n    padding: 8px 12px 4px;\n    font-size: 11px;\n    color: #9cdcfe;\n    border-bottom: 1px solid #3e3e42;\n    white-space: pre;\n    font-family: monospace;\n}\n\n.edit-overlay-field {\n    display: flex;\n    align-items: center;\n    gap: 8px;\n    padding: 8px 12px 4px;\n}\n\n.edit-overlay-label {\n    color: #858585;\n    font-size: 11px;\n    width: 7ch;\n    flex-shrink: 0;\n}\n\n.edit-overlay-input {\n    flex: 1;\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 4px 8px;\n    font-family: monospace;\n    font-size: 12px;\n    outline: none;\n}\n\n.edit-overlay-input:focus {\n    border-color: #007acc;\n}\n\n#edit-overlay-actions {\n    display: flex;\n    gap: 8px;\n    padding: 10px 12px 12px;\n    justify-content: flex-end;\n}\n\n#edit-overlay-save {\n    background: #007acc;\n    border: 1px solid #007acc;\n    color: #fff;\n    padding: 4px 16px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 12px;\n    outline: none;\n    border-radius: 2px;\n}\n\n#edit-overlay-save:hover {\n    background: #1a88d8;\n}\n\n#edit-overlay-cancel {\n    background: #3c3c3c;\n    border: 1px solid #3e3e42;\n    color: #cccccc;\n    padding: 4px 16px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 12px;\n    outline: none;\n    border-radius: 2px;\n}\n\n#edit-overlay-cancel:hover {\n    background: #505050;\n}\n\n";

/***/ }),
/* 7 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.DisasmPanelProvider = void 0;
const vscode = __importStar(__webpack_require__(1));
const fs = __importStar(__webpack_require__(3));
const path = __importStar(__webpack_require__(8));
const nonce_1 = __webpack_require__(4);
const disasmPanelTemplate_1 = __webpack_require__(9);
const extension_1 = __webpack_require__(0);
/** Memory regions exposed as separate editor windows when the emulator is running. */
const LIVE_REGIONS = [
    // EE Main RAM: show via the uncached direct-mapped window (0x20000000–0x21FFFFFF).
    // The PS2 kernel TLB maps physical RAM here without caching, so user-mode code
    // typically accesses / patches memory through this range.
    { region: 'ee', address: 0x20000000, size: 32 * 1024 * 1024, label: 'EE Main RAM' },
    // IOP RAM: show via the EE VTLB mirror at 0x1C000000–0x1DFFFFFF (2 MB window).
    // PCSX2 vtlb.cpp maps psxM (IOP RAM) to EE virtual 0x1C000000; the server
    // translates this virtual address back to a physical IOP RAM offset.
    // Size is capped at 2 MB to match EeIopMirrorEnd (0x1E000000) and avoid the
    // range overlapping with BIOS ROM at 0x1FC00000 which would cause the wrong
    // panel to be revealed on breakpoints.
    { region: 'iop', address: 0x1C000000, size: 2 * 1024 * 1024, label: 'IOP RAM' },
    { region: 'ee', address: 0x1FC00000, size: 4 * 1024 * 1024, label: 'BIOS ROM' },
    { region: 'ee', address: 0x70000000, size: 16 * 1024, label: 'Scratchpad' },
];
class DisasmPanelProvider {
    socketClient;
    editorProvider;
    webview;
    emulatorTerminal;
    settings = {};
    workspaceRoot = '';
    snapshots = [];
    extensionPath = '';
    selectedEmulatorPath = '';
    selectedBiosPath = '';
    selectedGamePath = '';
    _paused = false;
    constructor(socketClient, editorProvider) {
        this.socketClient = socketClient;
        this.editorProvider = editorProvider;
    }
    // Register the provider with VS Code
    register(context) {
        this.extensionPath = context.extensionPath;
        context.subscriptions.push(vscode.window.registerWebviewViewProvider('disasm.disasmPanel', this));
        // Reload settings whenever emu-settings.json is created or changed
        const watcher = vscode.workspace.createFileSystemWatcher('**/emu-settings.json');
        watcher.onDidCreate(() => this.reloadAndRefresh());
        watcher.onDidChange(() => this.reloadAndRefresh());
        watcher.onDidDelete(() => this.reloadAndRefresh());
        context.subscriptions.push(watcher);
        // Reload when the user opens a workspace folder after the panel is shown
        context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(() => this.reloadAndRefresh()));
        // Detect when the emulator terminal is closed externally
        context.subscriptions.push(vscode.window.onDidCloseTerminal(t => {
            if (t === this.emulatorTerminal) {
                (0, extension_1.getOutputChannel)().appendLine('[INFO] Emulator terminal closed.');
                try {
                    this.socketClient.disconnect();
                }
                catch { /* ignore */ }
                this.cleanupProcess();
            }
        }));
    }
    resolveWebviewView(webviewView) {
        this.webview = webviewView.webview;
        // Load emu-settings.json (searches workspace folders then walks up from
        // the extension directory, so this works even when no workspace is open).
        this.loadEmuSettings();
        // Setup webview options
        webviewView.webview.options = { enableScripts: true };
        // Render the panel with settings data
        webviewView.webview.html = this.buildHtml();
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async (msg) => {
            switch (msg.command) {
                case 'connect':
                    this.handleConnect(msg.emulatorPath, msg.biosPath, msg.gamePath);
                    break;
                case 'snapshot':
                    await this.handleSnapshot();
                    break;
                case 'openSnapshot':
                    await this.handleOpenSnapshot(msg.path);
                    break;
                case 'restart':
                    await this.handleRestart();
                    break;
                case 'togglePause':
                    await this.handleTogglePause();
                    break;
                // Dropdown change notifications — save current selections
                case 'emulatorChanged':
                    this.selectedEmulatorPath = msg.emulatorPath ?? '';
                    break;
                case 'biosChanged':
                    this.selectedBiosPath = msg.biosPath ?? '';
                    break;
                case 'gameChanged':
                    this.selectedGamePath = msg.gamePath ?? '';
                    break;
            }
        });
    }
    // ── Settings ──────────────────────────────────────────────────────────────
    /** Maximum number of parent directories to climb when searching for emu-settings.json. */
    static MAX_PARENT_SEARCH_DEPTH = 6;
    loadEmuSettings() {
        // 1. Try every open workspace folder
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            if (this.tryLoadSettingsFrom(path.join(folder.uri.fsPath, 'emu-settings.json'))) {
                return;
            }
        }
        // 2. Walk up from the extension install directory (handles the case where
        //    the developer opens vscode/codedesigner directly and the repo root is
        //    a few levels above).
        if (this.extensionPath) {
            let dir = this.extensionPath;
            for (let i = 0; i < DisasmPanelProvider.MAX_PARENT_SEARCH_DEPTH; i++) {
                if (this.tryLoadSettingsFrom(path.join(dir, 'emu-settings.json'))) {
                    return;
                }
                const parent = path.dirname(dir);
                if (parent === dir) {
                    break;
                } // reached filesystem root
                dir = parent;
            }
        }
        (0, extension_1.getOutputChannel)().appendLine('[WARN] Could not find emu-settings.json in any workspace folder or extension directory tree.');
        this.settings = {};
    }
    /** Attempt to load settings from a specific file path. Returns true on success. */
    tryLoadSettingsFrom(settingsPath) {
        try {
            const raw = fs.readFileSync(settingsPath, 'utf-8');
            this.settings = JSON.parse(raw);
            // Keep workspaceRoot in sync with wherever the file was found
            this.workspaceRoot = path.dirname(settingsPath);
            return true;
        }
        catch (err) {
            // ENOENT (file not found) is expected during the directory walk; only
            // log unexpected errors (e.g. parse failure, permission denied).
            if (err?.code !== 'ENOENT') {
                (0, extension_1.getOutputChannel)().appendLine(`[DEBUG] Could not read ${settingsPath}: ${err?.message ?? err}`);
            }
            return false;
        }
    }
    /** Reload settings and re-render the webview panel. */
    reloadAndRefresh() {
        this.loadEmuSettings();
        if (this.webview) {
            this.webview.html = this.buildHtml();
        }
    }
    buildHtml() {
        const emulators = (this.settings.emulators ?? []).map(e => ({
            type: e.type, path: e.path
        }));
        const biosOptions = (this.settings.bios ?? []).map(b => ({
            name: b.name, path: b.path
        }));
        const gameOptions = (this.settings.games ?? []).map(g => ({
            name: g.name, path: g.path
        }));
        const templateData = {
            connected: this.emulatorTerminal != null || this.socketClient.isConnected(),
            emulators,
            biosOptions,
            gameOptions,
            snapshots: this.snapshots,
        };
        return (0, disasmPanelTemplate_1.getDisasmPanelHtml)(templateData, (0, nonce_1.getNonce)());
    }
    // ── Emulator launch ───────────────────────────────────────────────────────
    handleConnect(emulatorPath, biosPath, gamePath) {
        const out = (0, extension_1.getOutputChannel)();
        // Save current selections for use by Restart
        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;
        // Toggle: if already running or already socket-connected, stop/disconnect
        if (this.emulatorTerminal || this.socketClient.isConnected()) {
            if (this.emulatorTerminal) {
                this.emulatorTerminal.dispose();
                out.appendLine('[INFO] Emulator stopped by user.');
            }
            else {
                out.appendLine('[INFO] Disconnected from server.');
            }
            this.socketClient.disconnect();
            this.cleanupProcess();
            return;
        }
        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }
        out.show(true);
        // Try connecting to an already-running server first. If the C# server
        // was started manually (e.g. `dotnet run -- --server` or `--run`), we
        // can reuse it without spawning a new process.
        // NOTE: do NOT open memory views here — the socketClient.onConnected
        // event (registered in extension.ts) will fire onSocketConnected() which
        // opens/reinits all panels.  Duplicating that work here would queue two
        // concurrent sets of READMEM requests against the server, which (before
        // the _pendingResponses fix) caused response mis-routing and is still
        // wasteful.
        this.socketClient.connect().then(() => {
            out.appendLine('[INFO] Connected to existing emulator server. Memory views will open via onSocketConnected.');
        }).catch(() => {
            // No existing server — spawn the emulator and then connect
            this.spawnAndConnect(emulatorPath, biosPath, gamePath);
        });
    }
    /** Launch the emulator in a VS Code terminal tab and, once the server is up, connect. */
    spawnAndConnect(emulatorPath, biosPath, gamePath) {
        const out = (0, extension_1.getOutputChannel)();
        const resolvedExe = this.resolvePath(emulatorPath);
        const resolvedBios = biosPath ? this.resolvePath(biosPath) : undefined;
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : undefined;
        // Verify the executable actually exists before trying to launch it.
        // Using shellPath: resolvedExe directly fails silently on some VS Code
        // versions when the file is missing; this gives the user a clear message.
        if (!fs.existsSync(resolvedExe)) {
            out.appendLine(`[ERROR] Emulator executable not found: ${resolvedExe}`);
            vscode.window.showErrorMessage(`Emulator not found: ${resolvedExe}\n` +
                'Build the project first: cd CodeDesigner.Mips64LE && dotnet build');
            this.cleanupProcess();
            return;
        }
        const cliArgs = ['--run'];
        if (resolvedBios) {
            cliArgs.push('--bios', resolvedBios);
        }
        if (resolvedDisc) {
            cliArgs.push('--disc', resolvedDisc);
        }
        out.appendLine(`[INFO] Starting emulator: ${resolvedExe} ${cliArgs.join(' ')}`);
        // Use shellPath/shellArgs to pass the exe and its arguments directly to
        // the OS without any shell quoting — this avoids shell-injection concerns
        // and works cross-platform regardless of the user's default shell.
        const terminal = vscode.window.createTerminal({
            name: 'PS2 Emulator',
            shellPath: resolvedExe,
            shellArgs: cliArgs,
        });
        terminal.show(true); // reveal but keep editor focus
        this.emulatorTerminal = terminal;
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });
        // After the emulator starts the embedded socket server, connect the
        // socket client and open a live memory view for every region.
        this.connectSocketAndOpenViews();
    }
    /** Formats a Date into a filesystem-safe snapshot folder name. */
    static formatSnapshotTimestamp(d) {
        return d.toISOString()
            .replace(/[:.]/g, '-')
            .replace('T', '_')
            .slice(0, 19); // YYYY-MM-DD_HH-MM-SS
    }
    /** Polls until the socket server is up (max ~10 s), then opens region windows. */
    connectSocketAndOpenViews() {
        const out = (0, extension_1.getOutputChannel)();
        const maxAttempts = 20;
        let attempts = 0;
        const tryConnect = async () => {
            attempts++;
            try {
                if (!this.socketClient.isConnected()) {
                    await this.socketClient.connect();
                }
                out.appendLine('[INFO] Socket connected to emulator. Opening memory views…');
                for (const r of LIVE_REGIONS) {
                    await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
                }
            }
            catch {
                if (attempts < maxAttempts) {
                    setTimeout(tryConnect, 500);
                }
                else {
                    out.appendLine('[WARN] Could not connect to emulator socket server.');
                }
            }
        };
        // Give the emulator ~1 s to start listening before first attempt
        setTimeout(tryConnect, 1000);
    }
    cleanupProcess() {
        this.emulatorTerminal = undefined;
        this._paused = false;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
    }
    /** Toggle emulator pause/resume via the socket connection. */
    async handleTogglePause() {
        const out = (0, extension_1.getOutputChannel)();
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        try {
            if (this._paused) {
                await this.socketClient.resume();
                this._paused = false;
            }
            else {
                await this.socketClient.pause();
                this._paused = true;
            }
            this.webview?.postMessage({ command: 'paused', paused: this._paused });
            this.editorProvider.notifyPauseState(this._paused);
        }
        catch (err) {
            out.appendLine(`[ERROR] Pause/Resume failed: ${err?.message ?? err}`);
        }
    }
    /**
     * Called by DisasmEditorProvider when the emulator's pause state changes
     * (e.g. a breakpoint was hit).  Updates the sidebar status bar accordingly.
     */
    notifyPauseState(paused) {
        this._paused = paused;
        this.webview?.postMessage({ command: 'paused', paused });
    }
    /** Restart the emulator: send RESTART command via socket, keeping the connection alive. */
    async handleRestart() {
        const out = (0, extension_1.getOutputChannel)();
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator. Cannot restart.');
            return;
        }
        out.appendLine('[INFO] Restarting emulator via socket…');
        // Blank live panels so stale content is not shown during the restart
        this.editorProvider.resetLivePanels();
        try {
            await this.socketClient.restart();
            out.appendLine('[INFO] Emulator restarted — paused at reset vector.');
            // After restart, emulator is paused; reflect that in the sidebar
            this._paused = true;
            this.webview?.postMessage({ command: 'paused', paused: true });
            this.editorProvider.notifyPauseState(true);
        }
        catch (err) {
            out.appendLine(`[ERROR] Restart failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Restart failed: ${err?.message ?? err}`);
        }
    }
    // ── Snapshot ──────────────────────────────────────────────────────────────
    async handleSnapshot() {
        const out = (0, extension_1.getOutputChannel)();
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        const timestamp = DisasmPanelProvider.formatSnapshotTimestamp(new Date());
        const snapshotsDir = this.workspaceRoot
            ? path.join(this.workspaceRoot, 'snapshots', timestamp)
            : path.join((__webpack_require__(11).tmpdir)(), 'ps2snapshots', timestamp);
        out.appendLine(`[INFO] Taking snapshot → ${snapshotsDir}`);
        try {
            const regions = await this.socketClient.snapshot(snapshotsDir);
            out.appendLine(`[INFO] Snapshot complete — ${regions.length} region(s) written.`);
            // Record in the panel's snapshot list
            const entry = {
                label: timestamp,
                timestamp: new Date().toLocaleTimeString(),
                path: snapshotsDir,
            };
            this.snapshots.unshift(entry);
            this.webview?.postMessage({
                command: 'updateSnapshots',
                snapshots: this.snapshots,
            });
            // Open each dumped .bin in the disassembler editor
            for (const r of regions) {
                const uri = vscode.Uri.file(r.File);
                await vscode.commands.executeCommand('vscode.openWith', uri, 'disasm.disasmEditor');
            }
        }
        catch (err) {
            out.appendLine(`[ERROR] Snapshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Snapshot failed: ${err?.message ?? err}`);
        }
    }
    /** Re-open all .bin files from an earlier snapshot folder. */
    async handleOpenSnapshot(snapshotPath) {
        try {
            const files = fs.readdirSync(snapshotPath)
                .filter(f => f.endsWith('.bin'))
                .map(f => path.join(snapshotPath, f));
            for (const f of files) {
                const uri = vscode.Uri.file(f);
                await vscode.commands.executeCommand('vscode.openWith', uri, 'disasm.disasmEditor');
            }
        }
        catch (err) {
            vscode.window.showErrorMessage(`Could not open snapshot: ${err?.message ?? err}`);
        }
    }
    /** Resolve a workspace-relative path to an absolute path. */
    resolvePath(p) {
        if (path.isAbsolute(p)) {
            return p;
        }
        return this.workspaceRoot ? path.join(this.workspaceRoot, p) : p;
    }
    /**
     * Called when the socket connects (including auto-connect on VS Code startup).
     * Opens live memory views for all regions, reusing any already-open panels.
     */
    async onSocketConnected() {
        this._paused = false;
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });
        for (const r of LIVE_REGIONS) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
    }
    /**
     * Open all live memory view panels immediately, even if not connected.
     * Panels will show placeholder content until the socket connects.
     * Called at extension activation so tabs are always visible.
     */
    async openAllLivePanels() {
        for (const r of LIVE_REGIONS) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
    }
}
exports.DisasmPanelProvider = DisasmPanelProvider;


/***/ }),
/* 8 */
/***/ ((module) => {

module.exports = require("path");

/***/ }),
/* 9 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


/**
 * Template for the Disassembly Panel
 */
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getDisasmPanelHtml = getDisasmPanelHtml;
const disasmPanel_css_1 = __importDefault(__webpack_require__(10));
function getDisasmPanelHtml(data, nonce) {
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">

<style>${disasmPanel_css_1.default}</style>

</head>
<body>

<div id="emu-status-bar" class="${data.connected ? 'status-connected' : 'status-disconnected'}">
    <span id="emu-status-dot">●</span>
    <span id="emu-status-text">${data.connected ? 'LIVE' : 'STOPPED'}</span>
    <button id="emu-play-pause-btn" title="Pause emulator" aria-label="Pause emulator">⏸ Pause</button>
</div>

<h3>Emulator</h3>
<div class="container">

    ${getEmulatorControls(data)}

</div>

${getScript(nonce)}

</body>
</html>`;
}
function getEmulatorControls(data) {
    const emulators = data.emulators || [];
    const biosOptions = data.biosOptions || [];
    const gameOptions = data.gameOptions || [];
    const selEmu = data.selectedEmulator || '';
    const selBios = data.selectedBios || '';
    const selGame = data.selectedGame || '';
    const emuOptions = emulators.map(e => {
        const label = e.type ?? e.name ?? '';
        const val = e.path;
        return `<option value="${escapeHtml(val)}" ${selEmu === val ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }).join('');
    const biosOptHtml = biosOptions.map(b => {
        const label = b.name ?? b.type ?? '';
        const val = b.path;
        return `<option value="${escapeHtml(val)}" ${selBios === val ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }).join('');
    const gameOptHtml = gameOptions.map(g => {
        const label = g.name ?? g.type ?? '';
        const val = g.path;
        return `<option value="${escapeHtml(val)}" ${selGame === val ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }).join('');
    return `<div class="row">
    <select id="emulatorSelect">
        <option value="">Select Emulator...</option>
        ${emuOptions}
    </select>
</div>
<div class="row">
    <select id="biosSelect">
        <option value="">Select BIOS...</option>
        ${biosOptHtml}
    </select>
</div>
<div class="row">
    <select id="gameSelect">
        <option value="">Select Game...</option>
        ${gameOptHtml}
    </select>
</div>
<div class="row">
    <button id="connectBtn">${data.connected ? 'Disconnect' : 'Connect'}</button>
</div>
<div class="row">
    <button id="restartBtn" ${data.connected ? '' : 'disabled'}>⟳ Restart</button>
</div>
<div class="row">
    <button id="snapshotBtn" ${data.connected ? '' : 'disabled'}>Take Snapshot</button>
</div>
${data.snapshots && data.snapshots.length > 0 ? getSnapshotList(data.snapshots) : ''}`;
}
function getSnapshotList(snapshots) {
    const items = snapshots.map(s => `
<div class="snapshot-item">
    <span class="snapshot-label">${escapeHtml(s.label)}</span>
    <span class="snapshot-time">${escapeHtml(s.timestamp)}</span>
    <button class="open-snapshot-btn" data-path="${escapeHtml(s.path)}">Open</button>
</div>`).join('');
    return `<div class="snapshot-list"><h4>Snapshots</h4>${items}</div>`;
}
function escapeHtml(str) {
    return str
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}
function getScript(nonce) {
    return `<script nonce="${nonce}">
const vscode = acquireVsCodeApi();

document.getElementById('connectBtn').onclick = () => {
    const emulatorPath = document.getElementById('emulatorSelect').value;
    const biosPath = document.getElementById('biosSelect').value;
    const gamePath = document.getElementById('gameSelect').value;
    vscode.postMessage({ command: 'connect', emulatorPath, biosPath, gamePath });
};

document.getElementById('snapshotBtn')?.addEventListener('click', () => {
    vscode.postMessage({ command: 'snapshot' });
});

document.getElementById('restartBtn')?.addEventListener('click', () => {
    vscode.postMessage({ command: 'restart' });
});

document.getElementById('emu-play-pause-btn')?.addEventListener('click', () => {
    vscode.postMessage({ command: 'togglePause' });
});

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulatorPath: e.target.value });
};

document.getElementById('biosSelect').onchange = (e) => {
    vscode.postMessage({ command: 'biosChanged', biosPath: e.target.value });
};

document.getElementById('gameSelect').onchange = (e) => {
    vscode.postMessage({ command: 'gameChanged', gamePath: e.target.value });
};

// Delegate clicks on dynamically-rendered Open buttons
document.addEventListener('click', (e) => {
    const btn = e.target.closest('.open-snapshot-btn');
    if (btn) {
        vscode.postMessage({ command: 'openSnapshot', path: btn.dataset.path });
    }
});

// Listen for messages from the extension
window.addEventListener('message', event => {
    const message = event.data;

    switch (message.command) {
        case 'connectionStatus': {
            const btn = document.getElementById('connectBtn');
            btn.textContent = message.connected ? 'Disconnect' : 'Connect';
            const snapBtn = document.getElementById('snapshotBtn');
            snapBtn.disabled = !message.connected;
            const restartBtn = document.getElementById('restartBtn');
            if (restartBtn) { restartBtn.disabled = !message.connected; }
            // Update the live status bar
            const statusBar = document.getElementById('emu-status-bar');
            const statusText = document.getElementById('emu-status-text');
            if (statusBar && statusText) {
                statusBar.className = message.connected ? 'status-connected' : 'status-disconnected';
                statusText.textContent = message.connected ? 'LIVE' : 'STOPPED';
            }
            // Reset play/pause button when (dis)connecting
            const ppBtn = document.getElementById('emu-play-pause-btn');
            if (ppBtn) {
                ppBtn.textContent = '⏸ Pause';
                ppBtn.title = 'Pause emulator';
            }
            break;
        }
        case 'paused': {
            const statusBar = document.getElementById('emu-status-bar');
            const statusText = document.getElementById('emu-status-text');
            const ppBtn = document.getElementById('emu-play-pause-btn');
            if (message.paused) {
                if (statusBar) { statusBar.className = 'status-paused'; }
                if (statusText) { statusText.textContent = '⏸ PAUSED'; }
                if (ppBtn) { ppBtn.textContent = '▶ Resume'; ppBtn.title = 'Resume emulator'; }
            } else {
                if (statusBar) { statusBar.className = 'status-connected'; }
                if (statusText) { statusText.textContent = 'LIVE'; }
                if (ppBtn) { ppBtn.textContent = '⏸ Pause'; ppBtn.title = 'Pause emulator'; }
            }
            break;
        }
        case 'updateSnapshots': {
            let listEl = document.querySelector('.snapshot-list');
            if (!listEl) {
                listEl = document.createElement('div');
                listEl.className = 'snapshot-list';
                document.querySelector('.container').appendChild(listEl);
            }
            const esc = s => s.replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
            listEl.innerHTML = '<h4>Snapshots</h4>' +
                message.snapshots.map(s =>
                    '<div class="snapshot-item">' +
                    '<span class="snapshot-label">' + esc(s.label) + '</span>' +
                    '<span class="snapshot-time">' + esc(s.timestamp) + '</span>' +
                    '<button class="open-snapshot-btn" data-path="' + esc(s.path) + '">Open</button>' +
                    '</div>'
                ).join('');
            break;
        }
    }
});
</script>`;
}


/***/ }),
/* 10 */
/***/ ((module) => {

module.exports = "body {\n    padding: 10px;\n    font-family: var(--vscode-editor-font-family);\n    color: var(--vscode-editor-foreground);\n    background: var(--vscode-editor-background);\n}\n\n#emu-status-bar {\n    display: flex;\n    align-items: center;\n    gap: 6px;\n    padding: 6px 10px;\n    margin: -10px -10px 10px;\n    font-family: monospace;\n    font-size: 12px;\n    font-weight: bold;\n    letter-spacing: 0.04em;\n}\n\n#emu-status-bar.status-connected {\n    background: #1e3a1e;\n    border-bottom: 1px solid #3a5c3a;\n    color: #7ec87e;\n}\n\n#emu-status-bar.status-disconnected {\n    background: #2d2d30;\n    border-bottom: 1px solid #3e3e42;\n    color: #858585;\n}\n\n#emu-status-bar.status-paused {\n    background: #3a1a1a;\n    border-bottom: 1px solid #6a2a2a;\n    color: #e07070;\n}\n\n#emu-status-dot {\n    font-size: 10px;\n    animation: none;\n}\n\n#emu-status-bar.status-connected #emu-status-dot {\n    animation: live-pulse 1.5s ease-in-out infinite;\n    color: #4ec94e;\n}\n\n#emu-status-bar.status-paused #emu-status-dot {\n    animation: none;\n    color: #e07070;\n}\n\n@keyframes live-pulse {\n    0%, 100% { opacity: 1; }\n    50% { opacity: 0.35; }\n}\n\n#emu-play-pause-btn {\n    margin-left: auto;\n    background: rgba(255,255,255,0.08);\n    border: 1px solid rgba(255,255,255,0.15);\n    color: inherit;\n    padding: 1px 8px;\n    cursor: pointer;\n    font-family: monospace;\n    font-size: 11px;\n    outline: none;\n    border-radius: 2px;\n    display: none;\n}\n\n#emu-status-bar.status-connected #emu-play-pause-btn,\n#emu-status-bar.status-paused #emu-play-pause-btn {\n    display: inline-block;\n}\n\n#emu-play-pause-btn:hover {\n    background: rgba(255,255,255,0.18);\n}\n\n.container {\n    display: flex;\n    flex-direction: column;\n    gap: 10px;\n}\n\n.row {\n    display: flex;\n    gap: 8px;\n    margin-bottom: 4px;\n}\n\n.row select,\n.row button {\n    flex: 1;\n}\n\nselect, button {\n    font-family: var(--vscode-editor-font-family);\n    font-size: var(--vscode-editor-font-size);\n}\n\nselect {\n    width: 100%;\n    background: var(--vscode-input-background);\n    color: var(--vscode-input-foreground);\n    border: 1px solid var(--vscode-input-border);\n    padding: 4px;\n}\n\nbutton {\n    background: var(--vscode-button-background);\n    color: var(--vscode-button-foreground);\n    border: none;\n    padding: 8px 16px;\n    cursor: pointer;\n    font-size: 14px;\n}\n\nbutton:hover {\n    background: var(--vscode-button-hoverBackground);\n}\n\nbutton:disabled {\n    opacity: 0.4;\n    cursor: not-allowed;\n}\n\n.snapshot-list {\n    margin-top: 12px;\n    border-top: 1px solid var(--vscode-input-border);\n    padding-top: 8px;\n}\n\n.snapshot-list h4 {\n    margin: 0 0 6px;\n    font-size: 12px;\n    color: var(--vscode-descriptionForeground);\n    text-transform: uppercase;\n    letter-spacing: 0.05em;\n}\n\n.snapshot-item {\n    display: flex;\n    align-items: center;\n    gap: 6px;\n    margin-bottom: 4px;\n    font-size: 12px;\n}\n\n.snapshot-label {\n    flex: 1;\n    overflow: hidden;\n    text-overflow: ellipsis;\n    white-space: nowrap;\n}\n\n.snapshot-time {\n    color: var(--vscode-descriptionForeground);\n    white-space: nowrap;\n}\n\n.open-snapshot-btn {\n    padding: 2px 8px;\n    font-size: 11px;\n}\n";

/***/ }),
/* 11 */
/***/ ((module) => {

module.exports = require("os");

/***/ }),
/* 12 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.AsmPanelProvider = void 0;
const vscode = __importStar(__webpack_require__(1));
const nonce_1 = __webpack_require__(4);
const asmPanelTemplate_1 = __webpack_require__(13);
const extension_1 = __webpack_require__(0);
class AsmPanelProvider {
    webview;
    // Register the provider with VS Code
    register(context) {
        context.subscriptions.push(vscode.window.registerWebviewViewProvider('asm.asmPanel', this));
    }
    resolveWebviewView(webviewView) {
        this.webview = webviewView.webview;
        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true
        };
        // Generate HTML from template
        const templateData = {
            code: '',
            emulators: ['Project64', 'Mupen64Plus', 'RetroArch']
        };
        webviewView.webview.html = (0, asmPanelTemplate_1.getAsmPanelHtml)(templateData, (0, nonce_1.getNonce)());
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async (message) => {
            switch (message.command) {
                case 'connect':
                    vscode.window.showInformationMessage('Connect clicked (emulator hookup later)');
                    break;
                case 'assemble':
                    await this.handleAssemble(message.data);
                    break;
            }
        });
    }
    async handleAssemble(asmCode) {
        const socketClient = (0, extension_1.getSocketClient)();
        if (!socketClient || !socketClient.isConnected()) {
            this.sendResult(false, '', 'Not connected to C# server. Use "Connect to Server" command first.');
            return;
        }
        try {
            const results = await socketClient.assemble(asmCode);
            // Format results as hex output
            const hexLines = [];
            for (const result of results) {
                if (result.Success) {
                    hexLines.push(`${result.Hex}  # ${result.Assembly}`);
                }
                else {
                    hexLines.push(`ERROR: ${result.Error} (${result.Assembly})`);
                }
            }
            this.sendResult(true, hexLines.join('\n'), '');
        }
        catch (error) {
            this.sendResult(false, '', error.message || 'Assembly failed');
        }
    }
    sendResult(success, data, error) {
        if (this.webview) {
            this.webview.postMessage({
                command: 'assembleResult',
                success: success,
                data: data,
                error: error
            });
        }
    }
}
exports.AsmPanelProvider = AsmPanelProvider;


/***/ }),
/* 13 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


/**
 * Template for the Assembler Panel
 * Separated from component logic for better organization (Angular-style)
 */
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getAsmPanelHtml = getAsmPanelHtml;
const asmPanel_css_1 = __importDefault(__webpack_require__(14));
/**
 * Main panel template - returns complete HTML
 */
function getAsmPanelHtml(data, nonce) {
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">

<style>${asmPanel_css_1.default}</style>

</head>
<body>

${getControlsSection(data)}
${getTextAreaSection(data)}
${getButtonSection()}

${getScript(nonce)}

</body>
</html>`;
}
/**
 * Controls section (emulator dropdown + connect button)
 */
function getControlsSection(data) {
    const emulators = data.emulators || ['Project64', 'Mupen64Plus', 'RetroArch'];
    const selected = data.selectedEmulator || '';
    return `<div class="row">
    <select id="emulatorSelect">
        <option value="">Select Emulator...</option>
        ${emulators.map(emu => `<option value="${emu}" ${selected === emu ? 'selected' : ''}>${emu}</option>`).join('')}
    </select>
    <button id="connectBtn">Connect</button>
    <button id="sendBtn" disabled>Send</button>
</div>`;
}
/**
 * Text area section
 */
function getTextAreaSection(data) {
    const code = data.code || '';
    return `<div class="textarea-container">
    <label for="asmInput">Assembly Code:</label>
    <textarea id="asmInput"
        placeholder="add $t0, $s0, $s1&#10;addi $t1, $t0, 100&#10;j 0x400000">${code}</textarea>
    
    <label for="hexOutput">Hex Output:</label>
    <textarea id="hexOutput" readonly
        placeholder="Assembled hex will appear here..."></textarea>
        
    <div id="status" class="status"></div>
</div>`;
}
/**
 * Button section (compile/decompile)
 */
function getButtonSection() {
    return `<div class="row">
    <button id="assembleBtn">Assemble</button>
</div>`;
}
/**
 * Script section (event handlers)
 */
function getScript(nonce) {
    return `<script nonce="${nonce}">
const vscode = acquireVsCodeApi();

document.getElementById('connectBtn').onclick = () => {
    vscode.postMessage({ command: 'connect' });
};

document.getElementById('sendBtn').onclick = () => {
    const code = document.getElementById('asmInput').value;
    vscode.postMessage({ command: 'send', code });
};

document.getElementById('assembleBtn').onclick = () => {
    const asmInput = document.getElementById('asmInput').value;
    const statusDiv = document.getElementById('status');
    
    statusDiv.textContent = 'Assembling...';
    statusDiv.className = 'status';
    
    vscode.postMessage({ 
        command: 'assemble',
        data: asmInput
    });
};

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulator: e.target.value });
};

window.addEventListener('message', event => {
    const message = event.data;
    
    switch (message.command) {
        case 'setCode':
            document.getElementById('asmInput').value = message.code;
            break;
            
        case 'assembleResult':
            const hexOutput = document.getElementById('hexOutput');
            const statusDiv = document.getElementById('status');
            
            if (message.success) {
                hexOutput.value = message.data;
                statusDiv.textContent = 'Assembly complete!';
                statusDiv.className = 'status success';
            } else {
                statusDiv.textContent = 'Error: ' + message.error;
                statusDiv.className = 'status error';
            }
            break;
    }
});
</script>`;
}


/***/ }),
/* 14 */
/***/ ((module) => {

module.exports = "body {\n    margin: 0;\n    padding: 10px;\n    font-family: var(--vscode-editor-font-family);\n    font-size: var(--vscode-editor-font-size);\n    background: var(--vscode-editor-background);\n    color: var(--vscode-editor-foreground);\n}\n\nselect, textarea, button {\n    font-family: var(--vscode-editor-font-family);\n    font-size: var(--vscode-editor-font-size);\n}\n\nselect, textarea {\n    width: 100%;\n    background: var(--vscode-input-background);\n    color: var(--vscode-input-foreground);\n    border: 1px solid var(--vscode-input-border);\n    margin-bottom: 8px;\n    padding: 4px;\n}\n\ntextarea {\n    height: 150px;\n    resize: vertical;\n    font-family: var(--vscode-editor-font-family);\n    font-size: var(--vscode-editor-font-size);\n}\n\nlabel {\n    display: block;\n    font-weight: bold;\n    margin-top: 10px;\n    margin-bottom: 4px;\n}\n\n.textarea-container {\n    display: flex;\n    flex-direction: column;\n}\n\nbutton {\n    background: var(--vscode-button-background);\n    color: var(--vscode-button-foreground);\n    border: none;\n    padding: 6px 12px;\n    cursor: pointer;\n}\n\nbutton:hover {\n    background: var(--vscode-button-hoverBackground);\n}\n\nbutton:disabled {\n    opacity: 0.5;\n    cursor: default;\n}\n\n.row {\n    display: flex;\n    gap: 8px;\n    margin-bottom: 8px;\n}\n\n.status {\n    padding: 8px;\n    margin-top: 10px;\n    border-radius: 4px;\n    font-size: 12px;\n}\n\n.status.success {\n    background: var(--vscode-testing-iconPassed);\n    color: white;\n}\n\n.status.error {\n    background: var(--vscode-testing-iconFailed);\n    color: white;\n}\n";

/***/ }),
/* 15 */
/***/ (function(__unused_webpack_module, exports, __webpack_require__) {


var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.SocketClient = void 0;
const net = __importStar(__webpack_require__(16));
/**
 * Socket client for communication with C# server
 */
class SocketClient {
    client = null;
    host;
    port;
    connected = false;
    wasConnected = false;
    messageBuffer = '';
    messageHandlers = [];
    connectionHandlers = [];
    disconnectionHandlers = [];
    /**
     * FIFO queue of one-time response handlers added by sendCommand().
     * Using a dedicated queue (with shift()) avoids the Array.forEach +
     * splice-during-iteration bug: when forEach iterates [A, B, C] and A
     * splices itself out at index 0, forEach advances to index 1 which is
     * now C (B shifted to 0), so B is skipped and C receives A's response.
     * With shift(), each incoming message pops exactly one handler off the
     * front of the queue in FIFO order — matching the order commands were sent.
     */
    _pendingResponses = [];
    constructor(host = 'localhost', port = 8765) {
        this.host = host;
        this.port = port;
    }
    /**
     * Connect to the C# server
     */
    connect() {
        return new Promise((resolve, reject) => {
            if (this.connected) {
                console.log('Already connected to server');
                resolve();
                return;
            }
            this.client = new net.Socket();
            let isResolved = false;
            console.log(`[SOCKET] Attempting to connect to ${this.host}:${this.port}...`);
            this.client.connect(this.port, this.host, () => {
                console.log(`[SOCKET] Connected to C# server at ${this.host}:${this.port}`);
                this.connected = true;
                this.wasConnected = true;
                isResolved = true;
                this.connectionHandlers.forEach(handler => handler());
                resolve();
            });
            this.client.on('data', (data) => {
                const chunk = data.toString('utf8');
                console.log(`[SOCKET] Received chunk (${chunk.length} chars)`);
                // Add to buffer
                this.messageBuffer += chunk;
                // Process complete messages (delimited by newline)
                let newlineIndex;
                while ((newlineIndex = this.messageBuffer.indexOf('\n')) !== -1) {
                    const message = this.messageBuffer.substring(0, newlineIndex).trim();
                    this.messageBuffer = this.messageBuffer.substring(newlineIndex + 1);
                    if (message.length > 0) {
                        console.log(`[SOCKET] Complete message received (${message.length} chars): ${message.substring(0, 100)}...`);
                        // Dispatch to the first pending one-time response handler (FIFO).
                        // shift() is safe here: it removes the element before calling it,
                        // so there is no splice-during-iteration issue.
                        const pending = this._pendingResponses.shift();
                        if (pending) {
                            pending(message);
                        }
                        // Also dispatch to any persistent broadcast handlers.
                        this.messageHandlers.forEach(handler => handler(message));
                    }
                }
            });
            this.client.on('error', (error) => {
                const errorMsg = error?.message || error?.toString() || 'Unknown socket error';
                console.error(`[SOCKET] Socket error: ${errorMsg}`);
                console.error(`[SOCKET] Error details:`, error);
                this.connected = false;
                if (!isResolved) {
                    isResolved = true;
                    reject(error);
                }
            });
            this.client.on('close', () => {
                console.log('[SOCKET] Connection closed');
                const wasActuallyConnected = this.wasConnected;
                this.connected = false;
                this.wasConnected = false;
                // Only call disconnect handlers if we were actually connected
                if (wasActuallyConnected) {
                    this.disconnectionHandlers.forEach(handler => handler());
                }
            });
            // Set connection timeout
            this.client.setTimeout(5000);
            this.client.on('timeout', () => {
                if (!isResolved) {
                    console.error('[SOCKET] Connection timeout');
                    this.client?.destroy();
                    isResolved = true;
                    reject(new Error('Connection timeout'));
                }
            });
        });
    }
    /**
     * Send a message to the C# server
     */
    sendMessage(message) {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }
            this.client.write(message + '\n', 'utf8', (error) => {
                if (error) {
                    console.error(`[SOCKET] Error sending message: ${error.message}`);
                    reject(error);
                }
                else {
                    console.log(`[SOCKET] Sent: ${message}`);
                    resolve();
                }
            });
        });
    }
    /**
     * Disconnect from the server
     */
    disconnect() {
        if (this.client) {
            this.client.destroy();
            this.client = null;
        }
        this.connected = false;
        this.wasConnected = false;
        // Cancel any in-flight requests that will never receive a response.
        const stale = this._pendingResponses.splice(0);
        for (const handler of stale) {
            try {
                handler('{"Success":false,"Error":"Disconnected"}');
            }
            catch { /* ignore */ }
        }
        console.log('[SOCKET] Disconnected from server');
    }
    /**
     * Register a message handler
     */
    onMessage(handler) {
        this.messageHandlers.push(handler);
    }
    /**
     * Register a connection handler
     */
    onConnected(handler) {
        this.connectionHandlers.push(handler);
    }
    /**
     * Register a disconnection handler
     */
    onDisconnected(handler) {
        this.disconnectionHandlers.push(handler);
    }
    /**
     * Check if connected
     */
    isConnected() {
        return this.connected;
    }
    /**
     * Send a command and wait for response
     */
    sendCommand(command, timeoutMs = 10000) {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }
            let responseReceived = false;
            // Set up one-time response handler
            const responseHandler = (message) => {
                if (responseReceived)
                    return;
                responseReceived = true;
                clearTimeout(timeout);
                try {
                    // Strip "Echo: " prefix if present
                    let cleanMessage = message;
                    const echoPrefix = 'Echo: ';
                    if (cleanMessage.startsWith(echoPrefix)) {
                        cleanMessage = cleanMessage.substring(echoPrefix.length);
                        console.log('[SOCKET] Stripped Echo prefix from response');
                    }
                    // Try to parse JSON
                    const response = JSON.parse(cleanMessage);
                    console.log('[SOCKET] Successfully parsed response:', response.Success ? 'SUCCESS' : 'FAILED');
                    resolve(response);
                }
                catch (error) {
                    const errorMsg = error instanceof Error ? error.message : String(error);
                    console.error('[SOCKET] Failed to parse response:', errorMsg);
                    console.error('[SOCKET] Message length:', message.length);
                    console.error('[SOCKET] First 200 chars:', message.substring(0, 200));
                    console.error('[SOCKET] Last 200 chars:', message.substring(Math.max(0, message.length - 200)));
                    reject(new Error(`Failed to parse response: ${errorMsg}`));
                }
            };
            // Enqueue handler — must be done before writing to the socket so
            // there is no window where the response arrives before the handler
            // is registered.
            this._pendingResponses.push(responseHandler);
            // Set timeout
            const timeout = setTimeout(() => {
                if (!responseReceived) {
                    responseReceived = true;
                    // Remove the handler from the pending queue
                    const index = this._pendingResponses.indexOf(responseHandler);
                    if (index > -1) {
                        this._pendingResponses.splice(index, 1);
                    }
                    reject(new Error('Command timeout'));
                }
            }, timeoutMs);
            // Send command
            const commandStr = JSON.stringify(command) + '\n';
            console.log(`[SOCKET] Sending command: ${commandStr.trim()}`);
            this.client.write(commandStr, 'utf8', (error) => {
                if (error) {
                    clearTimeout(timeout);
                    responseReceived = true;
                    // Remove the handler from the pending queue
                    const index = this._pendingResponses.indexOf(responseHandler);
                    if (index > -1) {
                        this._pendingResponses.splice(index, 1);
                    }
                    reject(error);
                }
            });
        });
    }
    /**
     * Assemble MIPS assembly code to hex
     */
    async assemble(asmCode) {
        const command = {
            Command: 'ASSEMBLE',
            Data: asmCode
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Assembly failed');
        }
        return response.Results || [];
    }
    /**
     * Disassemble hex code to MIPS assembly
     * @param hexCode - Hex instruction codes to disassemble (space/comma separated)
     * @param startAddress - Optional starting address for branch calculation
     */
    async disassemble(hexCode, startAddress) {
        const command = {
            Command: 'DISASSEMBLE',
            Data: hexCode
        };
        // Include address if provided
        if (startAddress !== undefined) {
            command.Address = startAddress;
        }
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Disassembly failed');
        }
        return response.Results || [];
    }
    /**
     * Read raw bytes from the live emulator memory.
     * @param address  - Start virtual address (EE or IOP space)
     * @param length   - Number of bytes to read (1–65536 per call)
     * @param region   - "ee" (default) or "iop"
     */
    async readMemory(address, length, region = 'ee') {
        const command = {
            Command: 'READMEM',
            Address: address,
            Length: length,
            Region: region,
        };
        const response = await this.sendCommand(command);
        if (!response.Success || !response.Data) {
            throw new Error(response.Error || 'ReadMem failed');
        }
        return Buffer.from(response.Data, 'base64');
    }
    /**
     * Take a snapshot: write all memory regions to .bin files in outputPath.
     * Returns the list of written region descriptors.
     */
    async snapshot(outputPath) {
        const command = {
            Command: 'SNAPSHOT',
            OutputPath: outputPath,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Snapshot failed');
        }
        return response.Regions || [];
    }
    /**
     * Pause emulator execution
     */
    async pause() {
        const command = { Command: 'PAUSE' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Pause failed');
        }
    }
    /**
     * Resume emulator execution
     */
    async resume() {
        const command = { Command: 'RESUME' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Resume failed');
        }
    }
    /**
     * Set a breakpoint at the given EE virtual address
     */
    async setBreakpoint(address) {
        const command = { Command: 'SETBP', BreakpointAddress: address };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'SetBreakpoint failed');
        }
    }
    /**
     * Clear the breakpoint at the given EE virtual address
     */
    async clearBreakpoint(address) {
        const command = { Command: 'CLEARBP', BreakpointAddress: address };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ClearBreakpoint failed');
        }
    }
    /**
     * Clear all breakpoints
     */
    async clearAllBreakpoints() {
        const command = { Command: 'CLEARBP_ALL' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ClearAllBreakpoints failed');
        }
    }
    /**
     * Get the current EE PC and paused/breakpoint state
     */
    async getPC() {
        const command = { Command: 'GETPC' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetPC failed');
        }
        return {
            paused: response.Paused ?? false,
            pc: response.PC ?? 0,
            breakpointHitAddress: response.BreakpointHitAddress,
        };
    }
    /**
     * Restart the emulator (reset both CPUs to power-on state, then pause)
     */
    async restart() {
        const command = { Command: 'RESTART' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Restart failed');
        }
    }
    /**
     * Get a full CPU register dump from the running emulator
     */
    async getRegisters() {
        const command = { Command: 'GETREGS' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetRegisters failed');
        }
        return {
            gpr: response.GPR ?? new Array(32).fill(0),
            pc: response.GPR_PC ?? 0,
            hi: response.GPR_HI ?? 0,
            lo: response.GPR_LO ?? 0,
            hi1: response.GPR_HI1 ?? 0,
            lo1: response.GPR_LO1 ?? 0,
            sa: response.GPR_SA ?? 0,
            cop0Status: response.COP0_Status ?? 0,
            cop0Cause: response.COP0_Cause ?? 0,
            cop0Epc: response.COP0_EPC ?? 0,
            cop0Count: response.COP0_Count ?? 0,
            cop0Regs: response.COP0_Regs ?? new Array(32).fill(0),
            fpr: response.FPR ?? new Array(32).fill(0),
            fprAcc: response.FPR_ACC ?? 0,
            fcr0: response.FCR0 ?? 0,
            fcr31: response.FCR31 ?? 0,
            vu0fX: response.VU0F_X ?? new Array(32).fill(0),
            vu0fY: response.VU0F_Y ?? new Array(32).fill(0),
            vu0fZ: response.VU0F_Z ?? new Array(32).fill(0),
            vu0fW: response.VU0F_W ?? new Array(32).fill(0),
            vu0AccX: response.VU0F_ACC_X ?? 0,
            vu0AccY: response.VU0F_ACC_Y ?? 0,
            vu0AccZ: response.VU0F_ACC_Z ?? 0,
            vu0AccW: response.VU0F_ACC_W ?? 0,
            vu0Q: response.VU0F_Q ?? 0,
            vu0P: response.VU0F_P ?? 0,
            vu0I: response.VU0F_I ?? 0,
            vu0i: response.VU0I ?? new Array(16).fill(0),
            vu0Status: response.VU0_Status ?? 0,
            vu0Mac: response.VU0_MAC ?? 0,
            vu0Clip: response.VU0_Clip ?? 0,
            gsPriv: response.GS_Priv ?? new Array(19).fill(0),
        };
    }
    /**
     * Fetch all labels from the server's symbol database.
     */
    async getLabels() {
        const response = await this.sendCommand({ Command: 'GETLABELS' });
        if (!response.Success) {
            throw new Error(response.Error || 'GetLabels failed');
        }
        return response.Labels ?? [];
    }
    /**
     * Add or update a label in the server's symbol database.
     * The server persists the change to ps2-labels.json immediately.
     */
    async addLabel(address, label, comment) {
        const cmd = { Command: 'ADDLABEL', Address: address, Label: label };
        if (comment) {
            cmd.Comment = comment;
        }
        const response = await this.sendCommand(cmd);
        if (!response.Success) {
            throw new Error(response.Error || 'AddLabel failed');
        }
    }
    /**
     * Delete a label from the server's symbol database by address.
     */
    async deleteLabel(address) {
        const response = await this.sendCommand({ Command: 'DELETELABEL', Address: address });
        if (!response.Success) {
            throw new Error(response.Error || 'DeleteLabel failed');
        }
    }
    /**
     * Scan binary data for null-terminated ASCII strings (min 4 chars).
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    async getStrings(binaryData, startAddress) {
        const response = await this.sendCommand({ Command: 'GETSTRINGS', Address: startAddress, Data: binaryData.toString('base64') }, 60000 // large binary may take a few seconds
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetStrings failed');
        }
        return response.Strings ?? [];
    }
    /**
     * Scan binary data for MIPS function entry points.
     * Detects ADDIU $sp,$sp,-N prologues and JAL targets.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    async getFunctions(binaryData, startAddress) {
        const response = await this.sendCommand({ Command: 'GETFUNCTIONS', Address: startAddress, Data: binaryData.toString('base64') }, 60000);
        if (!response.Success) {
            throw new Error(response.Error || 'GetFunctions failed');
        }
        return response.Functions ?? [];
    }
    /**
     * Scan a live emulator memory region for null-terminated ASCII strings.
     * The C# server reads the bytes directly — no binary data is transferred.
     * @param address  Virtual start address of the region.
     * @param length   Number of bytes to scan.
     * @param region   "ee" or "iop".
     */
    async scanLiveStrings(address, length, region) {
        const response = await this.sendCommand({ Command: 'SCANSTRINGS', Address: address, Length: length, Region: region }, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'ScanStrings failed');
        }
        return response.Strings ?? [];
    }
    /**
     * Scan a live emulator memory region for MIPS function entry points.
     * The C# server reads the bytes directly — no binary data is transferred.
     * @param address  Virtual start address of the region.
     * @param length   Number of bytes to scan.
     * @param region   "ee" or "iop".
     */
    async scanLiveFunctions(address, length, region) {
        const response = await this.sendCommand({ Command: 'SCANFUNCTIONS', Address: address, Length: length, Region: region }, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'ScanFunctions failed');
        }
        return response.Functions ?? [];
    }
    /**
     * Search for a query string/pattern in a memory region using the C# backend.
     * For live panels pass address+length+region; for file panels pass fileData.
     * @param address   Virtual start address (used for live searches and match address calculation).
     * @param length    Byte length (required for live searches, ignored when fileData provided).
     * @param query     The search string, byte pattern, or regex.
     * @param mode      "text" | "bytes" | "string" | "regex"
     * @param region    "ee" or "iop" — only used for live searches.
     * @param fileData  Binary data buffer — when provided the server searches this instead of live memory.
     */
    async search(address, length, query, mode, region, fileData) {
        const command = {
            Command: 'SEARCH',
            Address: address,
            Length: length,
            Query: query,
            SearchMode: mode,
            Region: region,
            Data: fileData ? fileData.toString('base64') : undefined,
        };
        const response = await this.sendCommand(command, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'Search failed');
        }
        return response.Matches ?? [];
    }
    /**
     * Ping the server
     */
    async ping() {
        const command = {
            Command: 'PING'
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Ping failed');
        }
        return response.Message || 'PONG';
    }
}
exports.SocketClient = SocketClient;


/***/ }),
/* 16 */
/***/ ((module) => {

module.exports = require("net");

/***/ })
/******/ 	]);
/************************************************************************/
/******/ 	// The module cache
/******/ 	var __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		var cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		__webpack_modules__[moduleId].call(module.exports, module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	
/******/ 	// startup
/******/ 	// Load entry module and return exports
/******/ 	// This entry module is referenced by other modules so it can't be inlined
/******/ 	var __webpack_exports__ = __webpack_require__(0);
/******/ 	module.exports = __webpack_exports__;
/******/ 	
/******/ })()
;
//# sourceMappingURL=extension.js.map