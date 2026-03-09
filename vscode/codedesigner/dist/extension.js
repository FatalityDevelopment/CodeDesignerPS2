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
const disasmPanelProvider_1 = __webpack_require__(4);
const asmPanelProvider_1 = __webpack_require__(7);
const socketClient_1 = __webpack_require__(8);
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
    const editorProvider = new disasmEditorProvider_1.DisasmEditorProvider(socketClient, context.extensionUri);
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
    const disasmPanelProvider = new disasmPanelProvider_1.DisasmPanelProvider(socketClient, editorProvider, context.extensionUri);
    disasmPanelProvider.register(context);
    const asmPanelProvider = new asmPanelProvider_1.AsmPanelProvider(context.extensionUri);
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
const extension_1 = __webpack_require__(0);
class DisasmEditorProvider {
    _extensionUri;
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
    constructor(socketClient, _extensionUri) {
        this._extensionUri = _extensionUri;
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
    getHtml(webview) {
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'disasm-editor.js'));
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource};">
</head>
<body>
<app-disasm-editor><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading...</p></app-disasm-editor>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
    async resolveCustomEditor(document, webviewPanel) {
        webviewPanel.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };
        // Track both async preconditions: file loaded + webview ready.
        // init is sent only when both are satisfied (order-independent).
        let fileData = null;
        let readyReceived = false;
        const sendInit = async (data) => {
            const totalInstructions = Math.floor(data.length / DisasmEditorProvider.INSTRUCTION_SIZE);
            console.log(`[DISASM] Sending init: ${totalInstructions} instructions`);
            webviewPanel.webview.postMessage({
                type: 'init',
                totalInstructions,
                pageSize: DisasmEditorProvider.PAGE_SIZE,
                baseAddress: DisasmEditorProvider.BASE_ADDRESS,
            });
            console.log(`[DISASM] Sending first page and hex view`);
            await this.sendDisasmPage(webviewPanel, data, 0);
            this.sendHexView(webviewPanel, data, DisasmEditorProvider.BASE_ADDRESS, DisasmEditorProvider.BASE_ADDRESS, DisasmEditorProvider.INSTRUCTION_SIZE);
        };
        // Register the message handler BEFORE setting html so that any 'ready'
        // message from the webview is never missed.
        webviewPanel.webview.onDidReceiveMessage(async (message) => {
            console.log(`[DISASM] Received message from webview:`, message.type);
            if (message.type === 'ready') {
                // Webview has bootstrapped and its listener is registered.
                readyReceived = true;
                if (fileData) {
                    await sendInit(fileData);
                }
                return;
            }
            if (message.type === 'bootstrapError') {
                (0, extension_1.getOutputChannel)().appendLine(`[ERROR] Disasm editor Angular bootstrap failed: ${message.error ?? '(no details)'}`);
                return;
            }
            if (!fileData) {
                return;
            }
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
        // Set HTML after registering the message handler.
        webviewPanel.webview.html = this.getHtml(webviewPanel.webview);
        fs.readFile(document.uri.fsPath, async (err, data) => {
            if (err) {
                console.error(`[DISASM] Failed to read file: ${err.message}`);
                webviewPanel.webview.postMessage({ type: 'error', message: err.message });
                return;
            }
            console.log(`[DISASM] File loaded: ${data.length} bytes`);
            fileData = data;
            // Send init if the webview already signalled ready before the file was read.
            if (readyReceived) {
                await sendInit(fileData);
            }
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
        const panel = vscode.window.createWebviewPanel('disasm.liveMemory', label, vscode.ViewColumn.One, {
            enableScripts: true,
            retainContextWhenHidden: true,
            localResourceRoots: [this._extensionUri]
        });
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
        const totalInstructions = Math.floor(totalBytes / DisasmEditorProvider.INSTRUCTION_SIZE);
        // Register the message handler BEFORE setting html so 'ready' is never missed.
        panel.webview.onDidReceiveMessage(async (message) => {
            if (message.type === 'ready') {
                // Webview has bootstrapped — safe to send init and the first page.
                panel.webview.postMessage({
                    type: 'init',
                    totalInstructions,
                    pageSize: DisasmEditorProvider.PAGE_SIZE,
                    baseAddress,
                    isLive: true,
                    regionLabel: label,
                    totalBytes,
                });
                await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, 0);
                // Check live connection state at ready-time (more accurate than a
                // snapshot taken at panel-creation time, since Angular bootstrap can
                // take several hundred ms during which the socket state may change).
                if (this.socketClient?.isConnected()) {
                    await this.sendLiveHexView(panel, region, baseAddress, baseAddress, DisasmEditorProvider.INSTRUCTION_SIZE);
                }
                return;
            }
            if (message.type === 'bootstrapError') {
                (0, extension_1.getOutputChannel)().appendLine(`[ERROR] Live memory view Angular bootstrap failed (${label}): ${message.error ?? '(no details)'}`);
                return;
            }
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
        // Set html AFTER registering the message handler so 'ready' is captured.
        panel.webview.html = this.getHtml(panel.webview);
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
const path = __importStar(__webpack_require__(5));
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
    _extensionUri;
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
    constructor(socketClient, editorProvider, _extensionUri) {
        this.socketClient = socketClient;
        this.editorProvider = editorProvider;
        this._extensionUri = _extensionUri;
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
        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };
        // Render the Angular shell.
        // NOTE: Do NOT call postInitMessage() here — wait for the Angular component
        // to send { command: 'ready' } before sending init data, so nothing is lost
        // during the async Angular bootstrap.
        webviewView.webview.html = this.getHtml(webviewView.webview);
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async (msg) => {
            if (msg.type === 'bootstrapError') {
                (0, extension_1.getOutputChannel)().appendLine(`[ERROR] Disasm panel Angular bootstrap failed: ${msg.error ?? '(no details)'}`);
                return;
            }
            switch (msg.command) {
                case 'ready':
                    // Angular component has bootstrapped and its message listener
                    // is registered — now safe to send the initial settings.
                    this.postInitMessage();
                    break;
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
    /** Reload settings and update the Angular component via postMessage. */
    reloadAndRefresh() {
        this.loadEmuSettings();
        this.postInitMessage();
    }
    postInitMessage() {
        if (!this.webview) {
            return;
        }
        const emulators = (this.settings.emulators ?? []).map(e => ({ label: e.type, value: e.path }));
        const biosOptions = (this.settings.bios ?? []).map(b => ({ label: b.name, value: b.path }));
        const gameOptions = (this.settings.games ?? []).map(g => ({ label: g.name, value: g.path }));
        this.webview.postMessage({
            command: 'init',
            emulators,
            biosOptions,
            gameOptions,
            snapshots: this.snapshots,
        });
    }
    getHtml(webview) {
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'disasm-panel.js'));
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource};">
</head>
<body>
<app-disasm-panel><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading...</p></app-disasm-panel>
<script src="${scriptUri}"></script>
</body>
</html>`;
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
            : path.join((__webpack_require__(6).tmpdir)(), 'ps2snapshots', timestamp);
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
/* 5 */
/***/ ((module) => {

module.exports = require("path");

/***/ }),
/* 6 */
/***/ ((module) => {

module.exports = require("os");

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
exports.AsmPanelProvider = void 0;
const vscode = __importStar(__webpack_require__(1));
const extension_1 = __webpack_require__(0);
class AsmPanelProvider {
    _extensionUri;
    webview;
    constructor(_extensionUri) {
        this._extensionUri = _extensionUri;
    }
    // Register the provider with VS Code
    register(context) {
        context.subscriptions.push(vscode.window.registerWebviewViewProvider('asm.asmPanel', this));
    }
    resolveWebviewView(webviewView) {
        this.webview = webviewView.webview;
        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };
        webviewView.webview.html = this.getHtml(webviewView.webview);
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async (message) => {
            if (message.type === 'bootstrapError') {
                (0, extension_1.getOutputChannel)().appendLine(`[ERROR] Asm panel Angular bootstrap failed: ${message.error ?? '(no details)'}`);
                return;
            }
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
    getHtml(webview) {
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'asm-panel.js'));
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource};">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
</head>
<body>
<app-asm-panel><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading...</p></app-asm-panel>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
exports.AsmPanelProvider = AsmPanelProvider;


/***/ }),
/* 8 */
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
const net = __importStar(__webpack_require__(9));
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
/* 9 */
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