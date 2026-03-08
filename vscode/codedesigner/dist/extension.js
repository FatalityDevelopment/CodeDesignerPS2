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
    // Register webview panels
    const disasmPanelProvider = new disasmPanelProvider_1.DisasmPanelProvider();
    disasmPanelProvider.register(context);
    const asmPanelProvider = new asmPanelProvider_1.AsmPanelProvider();
    asmPanelProvider.register(context);
    // Register socket event handlers
    socketClient.onConnected(() => {
        outputChannel.appendLine('[INFO] Connected to C# server');
        vscode.window.showInformationMessage('Connected to C# server');
    });
    socketClient.onMessage((message) => {
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
const helpers_1 = __webpack_require__(4);
const disasmEditorTemplate_1 = __webpack_require__(5);
const extension_1 = __webpack_require__(0);
class DisasmEditorProvider {
    static HEX_VIEW_SIZE = 0x30;
    static INSTRUCTION_SIZE = 4;
    static BASE_ADDRESS = 0x00000000;
    static PAGE_SIZE = 1000;
    static BUFFER_PAGES = 2;
    socketClient;
    constructor(socketClient) {
        this.socketClient = socketClient;
    }
    /**
     * Format bytes for display (space-separated, big-endian)
     */
    formatBytesForDisplay(buffer, offset, length) {
        const slice = buffer.slice(offset, offset + length);
        return Array.from(slice)
            .reverse()
            .map((b) => b.toString(16).padStart(2, "0").toUpperCase())
            .join(" ");
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
                        label: "",
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
        webviewPanel.webview.html = (0, disasmEditorTemplate_1.getDisasmEditorHtml)(templateData, (0, helpers_1.getNonce)());
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
                    await this.performSearch(webviewPanel, fileData, message.query, message.mode);
                }
            });
        });
    }
    async performSearch(webviewPanel, fileData, query, mode) {
        const matches = [];
        if (mode === 'bytes') {
            const pattern = this.parseBytePattern(query);
            if (!pattern) {
                webviewPanel.webview.postMessage({ type: 'searchResults', matches: [] });
                return;
            }
            for (let offset = 0; offset <= fileData.length - pattern.length; offset++) {
                let found = true;
                for (let i = 0; i < pattern.length; i++) {
                    if (fileData[offset + i] !== pattern[i]) {
                        found = false;
                        break;
                    }
                }
                if (found) {
                    const instructionIndex = Math.floor(offset / DisasmEditorProvider.INSTRUCTION_SIZE);
                    const address = DisasmEditorProvider.BASE_ADDRESS + offset;
                    matches.push({
                        instructionIndex,
                        address,
                        field: 'bytes',
                        start: 0,
                        length: pattern.length * 3 - 1
                    });
                    offset += pattern.length - 1;
                }
            }
        }
        else if (mode === 'string') {
            const stringBytes = Buffer.from(query, 'utf8');
            for (let offset = 0; offset <= fileData.length - stringBytes.length; offset++) {
                let found = true;
                for (let i = 0; i < stringBytes.length; i++) {
                    if (fileData[offset + i] !== stringBytes[i]) {
                        found = false;
                        break;
                    }
                }
                if (found) {
                    const instructionIndex = Math.floor(offset / DisasmEditorProvider.INSTRUCTION_SIZE);
                    const address = DisasmEditorProvider.BASE_ADDRESS + offset;
                    matches.push({
                        instructionIndex,
                        address,
                        field: 'bytes',
                        start: 0,
                        length: stringBytes.length * 3 - 1
                    });
                    offset += stringBytes.length - 1;
                }
            }
        }
        else {
            const totalInstructions = Math.floor(fileData.length / DisasmEditorProvider.INSTRUCTION_SIZE);
            for (let instrIndex = 0; instrIndex < totalInstructions; instrIndex++) {
                const offset = instrIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
                const slice = fileData.slice(offset, offset + DisasmEditorProvider.INSTRUCTION_SIZE);
                if (slice.length < DisasmEditorProvider.INSTRUCTION_SIZE)
                    break;
                const address = DisasmEditorProvider.BASE_ADDRESS + offset;
                const rows = await this.disassembleWithBackend(address, slice);
                if (rows.length === 0)
                    continue;
                const row = rows[0];
                const rowMatches = this.searchInRow(row, query, mode);
                for (const match of rowMatches) {
                    matches.push({
                        instructionIndex: instrIndex,
                        address,
                        field: match.field,
                        start: match.start,
                        length: match.length
                    });
                }
            }
        }
        webviewPanel.webview.postMessage({
            type: 'searchResults',
            matches
        });
    }
    parseBytePattern(pattern) {
        const cleaned = pattern.replace(/0x/gi, '').replace(/[,\s]+/g, ' ').trim();
        if (!cleaned)
            return null;
        const bytes = cleaned.split(/\s+/);
        const result = [];
        for (const byte of bytes) {
            if (!/^[0-9a-f]{1,2}$/i.test(byte))
                return null;
            result.push(parseInt(byte, 16));
        }
        return result.length ? result : null;
    }
    searchInRow(row, query, mode) {
        const matches = [];
        const fields = [
            { text: row.addr, field: 'addr' },
            { text: row.bytes, field: 'bytes' },
            { text: row.label || '', field: 'label' },
            { text: row.op, field: 'op' },
            { text: row.args, field: 'args' },
            { text: row.comment || '', field: 'comment' }
        ];
        if (mode === 'text') {
            const searchLower = query.toLowerCase();
            for (const { text, field } of fields) {
                const lower = text.toLowerCase();
                let index = 0;
                while ((index = lower.indexOf(searchLower, index)) !== -1) {
                    matches.push({ field, start: index, length: query.length });
                    index += query.length;
                }
            }
        }
        else {
            try {
                const regex = new RegExp(query, 'gi');
                for (const { text, field } of fields) {
                    let match;
                    const fieldRegex = new RegExp(regex);
                    while ((match = fieldRegex.exec(text)) !== null) {
                        matches.push({
                            field,
                            start: match.index,
                            length: match[0].length
                        });
                        if (match.index === fieldRegex.lastIndex) {
                            fieldRegex.lastIndex++;
                        }
                    }
                }
            }
            catch {
                return [];
            }
        }
        return matches;
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
exports.formatBytes = formatBytes;
exports.disassemble = disassemble;
exports.analyzePointers = analyzePointers;
function getNonce() {
    let text = "";
    const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    for (let i = 0; i < 32; i++)
        text += chars.charAt(Math.floor(Math.random() * chars.length));
    return text;
}
function formatBytes(bytes) {
    return [...bytes]
        .reverse()
        .map((b) => b.toString(16).padStart(2, "0"))
        .join(" ");
}
/**
 * Disassemble MIPS binary code with inline pointer calculation comments
 * @param startAddr - Virtual address where disassembly begins
 * @param buffer - Binary data to disassemble
 * @returns Array of disassembled instructions with analysis comments
 */
function disassemble(startAddr, buffer) {
    const rows = [];
    const regs = new Map(); // Track register values for inline comments
    /**
     * Parse hex immediate from instruction args
     */
    const parseInlineImm = (s) => {
        const cleaned = s.replace("$", "");
        return parseInt(cleaned, 16);
    };
    /**
     * Get signed 16-bit immediate
     */
    const getSignedInline = (val) => {
        return (val << 16) >> 16;
    };
    for (let offset = 0; offset < buffer.length; offset += 4) {
        const slice = buffer.slice(offset, offset + 4);
        if (slice.length < 4)
            break;
        const bytesLE = Array.from(slice)
            .reverse()
            .map((b) => b.toString(16).padStart(2, "0").toUpperCase())
            .join(" ");
        const instr = slice.readUInt32LE(0);
        const currentAddr = startAddr + offset;
        const decoded = decodeMIPS(instr, currentAddr);
        if (decoded) {
            let inlineComment = decoded.comment || "";
            const op = decoded.op.toLowerCase();
            const args = decoded.args.split(",").map((a) => a.trim());
            // Track and comment on pointer calculations
            if (op === "lui" && args.length >= 2) {
                const rt = args[0];
                const imm = parseInlineImm(args[1]);
                const value = (imm << 16) >>> 0;
                regs.set(rt, value);
            }
            else if ((op === "ori" || op === "addiu" || op === "addi") && args.length >= 3) {
                const rt = args[0];
                const rs = args[1];
                const imm = parseInlineImm(args[2]);
                const baseValue = regs.get(rs);
                if (baseValue !== undefined) {
                    const signedImm = getSignedInline(imm);
                    const result = op === "ori" ? (baseValue | imm) >>> 0 : (baseValue + signedImm) >>> 0;
                    regs.set(rt, result);
                    inlineComment = `; ${rt} = 0x${result.toString(16).toUpperCase()} (calculated addr)`;
                }
                else {
                    regs.delete(rt);
                }
            }
            else if ((op === "addu" || op === "add") && args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rt = args[2];
                const a = regs.get(rs);
                const b = regs.get(rt);
                if (a !== undefined && b !== undefined) {
                    const result = (a + b) >>> 0;
                    regs.set(rd, result);
                    inlineComment = `; ${rd} = 0x${result.toString(16).toUpperCase()} (calculated addr)`;
                }
                else {
                    regs.delete(rd);
                }
            }
            else if ((op === "subu" || op === "sub") && args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rt = args[2];
                const a = regs.get(rs);
                const b = regs.get(rt);
                if (a !== undefined && b !== undefined) {
                    const result = (a - b) >>> 0;
                    regs.set(rd, result);
                    inlineComment = `; ${rd} = 0x${result.toString(16).toUpperCase()} (calculated addr)`;
                }
                else {
                    regs.delete(rd);
                }
            }
            else if (op === "sll" && args.length >= 3) {
                const rd = args[0];
                const rt = args[1];
                const sa = parseInt(args[2], 10);
                const val = regs.get(rt);
                if (val !== undefined) {
                    const result = (val << sa) >>> 0;
                    regs.set(rd, result);
                    inlineComment = `; ${rd} = 0x${result.toString(16).toUpperCase()} (shifted ptr)`;
                }
                else {
                    regs.delete(rd);
                }
            }
            // Clear register tracking for operations that modify registers unpredictably
            else if (args.length > 0 && !op.startsWith("b") && op !== "j" && op !== "jal" && op !== "nop") {
                const destReg = args[0];
                if (destReg && !destReg.includes("(") && !destReg.includes("$")) {
                    regs.delete(destReg);
                }
            }
            rows.push({
                addr: currentAddr.toString(16).toUpperCase().padStart(8, "0"),
                bytes: bytesLE,
                label: decoded.label || "",
                op: decoded.op,
                args: decoded.args,
                comment: inlineComment,
            });
        }
    }
    console.log("Disassembled", rows.length, "instructions");
    if (rows.length > 0) {
        console.log("First instruction:", rows[0].addr, rows[0].bytes, rows[0].op, rows[0].args);
    }
    return rows;
}
/**
 * Analyze disassembled instructions to track register values and identify pointer references
 * This performs data flow analysis to resolve memory addresses and function calls
 * @param rows - Array of disassembled instructions
 * @returns Array of resolved pointer references with metadata
 */
function analyzePointers(rows) {
    const refs = [];
    const regs = new Map();
    /**
     * Parse hexadecimal immediate value from string (e.g., "$1234" -> 0x1234)
     */
    const parseImm = (s) => {
        const cleaned = s.replace("$", "");
        return parseInt(cleaned, 16);
    };
    /**
     * Convert 16-bit value to signed integer
     */
    const getSignedImm = (val) => {
        return (val << 16) >> 16;
    };
    /**
     * Determine memory access size from instruction mnemonic
     */
    const getAccessSize = (op) => {
        if (op.includes("b"))
            return 1; // byte (lb, sb, lbu)
        if (op.includes("h"))
            return 2; // halfword (lh, sh, lhu)
        if (op.includes("w") && !op.includes("q"))
            return 4; // word (lw, sw)
        if (op === "ld" || op === "sd")
            return 8; // doubleword
        if (op === "lq" || op === "sq")
            return 16; // quadword (PS2 128-bit)
        return 4; // default to word
    };
    /**
     * Infer data type from instruction and context
     */
    const inferDataType = (op, targetAddr) => {
        // Floating point operations
        if (op.includes("c1") || op.includes(".s") || op.includes(".w")) {
            return "float";
        }
        // Vector operations (PS2)
        if (op.startsWith("v") || op.includes("c2") || op === "lq" || op === "sq") {
            return "vector";
        }
        // Byte operations might be strings
        if (op === "lb" || op === "lbu" || op === "sb") {
            return "byte/char";
        }
        // Check if address looks like code (aligned to 4 bytes)
        if (targetAddr % 4 === 0 && targetAddr >= 0x00100000) {
            return "code/function";
        }
        return undefined;
    };
    /**
     * Check if an address is likely a function entry point
     */
    const isLikelyFunction = (addr, rows) => {
        // Find the row at this address
        const targetRow = rows.find(r => parseInt(r.addr, 16) === addr);
        if (!targetRow)
            return false;
        // Common function prologue patterns
        const op = targetRow.op.toLowerCase();
        const args = targetRow.args.toLowerCase();
        // Stack frame allocation: addiu sp, sp, -X
        if (op === "addiu" && args.includes("sp, sp") && args.includes("-")) {
            return true;
        }
        // Save return address: sw ra, X(sp)
        if (op === "sw" && args.includes("ra") && args.includes("sp")) {
            return true;
        }
        return false;
    };
    for (const row of rows) {
        const instrAddr = parseInt(row.addr, 16);
        const op = row.op.toLowerCase();
        const args = row.args.split(",").map((a) => a.trim());
        // LUI - Load Upper Immediate
        if (op === "lui") {
            if (args.length >= 2) {
                const rt = args[0];
                const imm = parseImm(args[1]);
                regs.set(rt, (imm << 16) >>> 0);
                console.log(`LUI: ${rt} = 0x${((imm << 16) >>> 0).toString(16).toUpperCase()}`);
            }
            continue;
        }
        // ADDIU/ADDI - Add Immediate (Unsigned)
        if (op === "addiu" || op === "addi") {
            if (args.length >= 3) {
                const rt = args[0];
                const rs = args[1];
                const imm = parseImm(args[2]);
                const base = regs.get(rs);
                if (base !== undefined) {
                    const signedImm = getSignedImm(imm);
                    const result = (base + signedImm) >>> 0;
                    regs.set(rt, result);
                    console.log(`ADDIU: ${rt} = ${rs}(0x${base.toString(16)}) + 0x${imm.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rt);
                    console.log(`ADDIU: ${rt} = unknown (${rs} not tracked)`);
                }
            }
            continue;
        }
        // ADDU/ADD - Add Unsigned
        if (op === "addu" || op === "add") {
            if (args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rt = args[2];
                const a = regs.get(rs);
                const b = regs.get(rt);
                if (a !== undefined && b !== undefined) {
                    const result = (a + b) >>> 0;
                    regs.set(rd, result);
                    console.log(`ADDU: ${rd} = ${rs}(0x${a.toString(16)}) + ${rt}(0x${b.toString(16)}) = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rd);
                }
            }
            continue;
        }
        // SUBU/SUB - Subtract
        if (op === "subu" || op === "sub") {
            if (args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rt = args[2];
                const a = regs.get(rs);
                const b = regs.get(rt);
                if (a !== undefined && b !== undefined) {
                    const result = (a - b) >>> 0;
                    regs.set(rd, result);
                    console.log(`SUBU: ${rd} = ${rs}(0x${a.toString(16)}) - ${rt}(0x${b.toString(16)}) = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rd);
                }
            }
            continue;
        }
        // AND/ANDI - Bitwise AND
        if (op === "and" || op === "andi") {
            if (args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rtOrImm = args[2];
                const a = regs.get(rs);
                const b = op === "andi" ? parseImm(rtOrImm) : regs.get(rtOrImm);
                if (a !== undefined && b !== undefined) {
                    const result = (a & b) >>> 0;
                    regs.set(rd, result);
                    console.log(`${op.toUpperCase()}: ${rd} = 0x${a.toString(16)} & 0x${b.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rd);
                }
            }
            continue;
        }
        // OR - Bitwise OR
        if (op === "or") {
            if (args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rt = args[2];
                const a = regs.get(rs);
                const b = regs.get(rt);
                if (a !== undefined && b !== undefined) {
                    const result = (a | b) >>> 0;
                    regs.set(rd, result);
                    console.log(`OR: ${rd} = 0x${a.toString(16)} | 0x${b.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rd);
                }
            }
            continue;
        }
        // ORI - OR Immediate
        if (op === "ori") {
            if (args.length >= 3) {
                const rt = args[0];
                const rs = args[1];
                const imm = parseImm(args[2]);
                const base = regs.get(rs);
                if (base !== undefined) {
                    const result = (base | imm) >>> 0;
                    regs.set(rt, result);
                    console.log(`ORI: ${rt} = 0x${base.toString(16)} | 0x${imm.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rt);
                }
            }
            continue;
        }
        // XOR/XORI - Bitwise XOR
        if (op === "xor" || op === "xori") {
            if (args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rtOrImm = args[2];
                const a = regs.get(rs);
                const b = op === "xori" ? parseImm(rtOrImm) : regs.get(rtOrImm);
                if (a !== undefined && b !== undefined) {
                    const result = (a ^ b) >>> 0;
                    regs.set(rd, result);
                    console.log(`${op.toUpperCase()}: ${rd} = 0x${a.toString(16)} ^ 0x${b.toString(16)} = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rd);
                }
            }
            continue;
        }
        // NOR - Bitwise NOR
        if (op === "nor") {
            if (args.length >= 3) {
                const rd = args[0];
                const rs = args[1];
                const rt = args[2];
                const a = regs.get(rs);
                const b = regs.get(rt);
                if (a !== undefined && b !== undefined) {
                    const result = (~(a | b)) >>> 0;
                    regs.set(rd, result);
                    console.log(`NOR: ${rd} = ~(0x${a.toString(16)} | 0x${b.toString(16)}) = 0x${result.toString(16).toUpperCase()}`);
                }
                else {
                    regs.delete(rd);
                }
            }
            continue;
        }
        // SLL - Shift Left Logical
        if (op === "sll" && args.length >= 3) {
            const rd = args[0];
            const rt = args[1];
            const sa = parseInt(args[2], 10);
            const val = regs.get(rt);
            if (val !== undefined) {
                const result = (val << sa) >>> 0;
                regs.set(rd, result);
                console.log(`SLL: ${rd} = 0x${val.toString(16)} << ${sa} = 0x${result.toString(16).toUpperCase()}`);
            }
            else {
                regs.delete(rd);
            }
            continue;
        }
        // SRL - Shift Right Logical
        if (op === "srl" && args.length >= 3) {
            const rd = args[0];
            const rt = args[1];
            const sa = parseInt(args[2], 10);
            const val = regs.get(rt);
            if (val !== undefined) {
                const result = (val >>> sa) >>> 0;
                regs.set(rd, result);
                console.log(`SRL: ${rd} = 0x${val.toString(16)} >> ${sa} = 0x${result.toString(16).toUpperCase()}`);
            }
            else {
                regs.delete(rd);
            }
            continue;
        }
        // SRA - Shift Right Arithmetic
        if (op === "sra" && args.length >= 3) {
            const rd = args[0];
            const rt = args[1];
            const sa = parseInt(args[2], 10);
            const val = regs.get(rt);
            if (val !== undefined) {
                const result = (val >> sa) >>> 0;
                regs.set(rd, result);
                console.log(`SRA: ${rd} = 0x${val.toString(16)} >> ${sa} (arith) = 0x${result.toString(16).toUpperCase()}`);
            }
            else {
                regs.delete(rd);
            }
            continue;
        }
        // Memory operations with offset(base) format
        const memArg = args.find((a) => a.includes("(") && a.includes(")"));
        if (memArg) {
            const match = memArg.match(/\$([0-9A-Fa-f]+)\((\w+)\)/);
            if (match) {
                const offset = parseImm(match[1]);
                const baseReg = match[2];
                const baseValue = regs.get(baseReg);
                // Only add reference if we know the base value
                if (baseValue !== undefined) {
                    const signedOffset = getSignedImm(offset);
                    const targetAddr = (baseValue + signedOffset) >>> 0;
                    const size = getAccessSize(op);
                    const dataType = inferDataType(op, targetAddr);
                    const isFunction = isLikelyFunction(targetAddr, rows);
                    refs.push({
                        instrAddr,
                        kind: op.startsWith("l") ? "load" : "store",
                        baseReg,
                        baseValue,
                        offset: signedOffset,
                        targetAddr,
                        size,
                        type: op,
                        dataType,
                        isFunction,
                    });
                    console.log(`${op.toUpperCase()}: @0x${instrAddr.toString(16)} -> 0x${targetAddr.toString(16).toUpperCase()} ` +
                        `[${baseReg}=0x${baseValue.toString(16)} + ${signedOffset}] (${size} bytes${dataType ? ', ' + dataType : ''}${isFunction ? ', FUNCTION' : ''})`);
                }
            }
            continue;
        }
        // JAL - Jump and Link (function call)
        if (op === "jal") {
            const targetMatch = args[0].match(/\$([0-9A-Fa-f]+)/);
            if (targetMatch) {
                const targetAddr = parseImm(targetMatch[1]);
                const isFunction = isLikelyFunction(targetAddr, rows);
                refs.push({
                    instrAddr,
                    kind: "call",
                    baseReg: "pc",
                    baseValue: instrAddr,
                    offset: 0,
                    targetAddr,
                    type: "jal",
                    isFunction: true, // JAL always calls a function
                    dataType: "function",
                });
                console.log(`JAL: @0x${instrAddr.toString(16)} -> 0x${targetAddr.toString(16).toUpperCase()} (function call)`);
            }
            continue;
        }
        // JR/JALR - Jump Register (indirect jumps/calls)
        if (op === "jr" || op === "jalr") {
            const reg = args[op === "jalr" && args.length > 1 ? 1 : 0];
            const baseValue = regs.get(reg);
            if (baseValue !== undefined) {
                const isFunction = op === "jalr" || isLikelyFunction(baseValue, rows);
                refs.push({
                    instrAddr,
                    kind: op === "jalr" ? "call" : "jump",
                    baseReg: reg,
                    baseValue,
                    offset: 0,
                    targetAddr: baseValue,
                    type: op,
                    isFunction,
                    dataType: isFunction ? "function" : undefined,
                });
                console.log(`${op.toUpperCase()}: @0x${instrAddr.toString(16)} -> 0x${baseValue.toString(16).toUpperCase()}${isFunction ? ' (function)' : ''}`);
            }
        }
        // Clear destination register for most other operations to avoid stale data
        if (args.length > 0 && !op.startsWith("b") && op !== "j" && op !== "jal") {
            const destReg = args[0];
            if (!memArg && destReg && !destReg.includes("(")) {
                // Only clear if it's not a memory operation and not a branch/jump
                if (!["nop", "syscall", "break", "sync", "jr", "jalr"].includes(op)) {
                    // Don't clear if we already handled it above
                    const handledOps = ["lui", "addiu", "addi", "addu", "add", "subu", "sub",
                        "and", "andi", "or", "ori", "xor", "xori", "nor",
                        "sll", "srl", "sra"];
                    if (!handledOps.includes(op)) {
                        regs.delete(destReg);
                    }
                }
            }
        }
    }
    console.log(`Found ${refs.length} resolved pointer references`);
    return refs;
}
function decodeMIPS(instr, pc) {
    const opcode = (instr >> 26) & 0x3f;
    const rs = (instr >> 21) & 0x1f;
    const rt = (instr >> 16) & 0x1f;
    const rd = (instr >> 11) & 0x1f;
    const sa = (instr >> 6) & 0x1f;
    const funct = instr & 0x3f;
    const imm = instr & 0xffff;
    const signedImm = imm & 0x8000 ? imm | 0xffff0000 : imm;
    const target = instr & 0x3ffffff;
    const code = (instr >> 6) & 0xfffff;
    const reg = (r) => {
        const names = [
            "zero",
            "at",
            "v0",
            "v1",
            "a0",
            "a1",
            "a2",
            "a3",
            "t0",
            "t1",
            "t2",
            "t3",
            "t4",
            "t5",
            "t6",
            "t7",
            "s0",
            "s1",
            "s2",
            "s3",
            "s4",
            "s5",
            "s6",
            "s7",
            "t8",
            "t9",
            "k0",
            "k1",
            "gp",
            "sp",
            "fp",
            "ra",
        ];
        return names[r];
    };
    const freg = (r) => `f${r}`;
    // Format signed immediate with 4-digit hex padding
    const formatImm = (val) => {
        const hex = (val & 0xffff).toString(16).toUpperCase().padStart(4, "0");
        return `$${hex}`;
    };
    // Format branch/jump target address with 8-digit hex padding
    const formatAddr = (addr) => {
        const hex = (addr >>> 0).toString(16).toUpperCase().padStart(8, "0");
        return `$${hex}`;
    };
    // Format branch with target address and instruction count
    const formatBranch = (targetAddr, offset) => {
        const addrHex = (targetAddr >>> 0)
            .toString(16)
            .toUpperCase()
            .padStart(8, "0");
        const instrCount = offset / 4; // Convert byte offset to instruction count
        const sign = instrCount >= 0 ? "+" : "";
        const arrow = instrCount >= 0 ? "▼" : "▲";
        return `$${addrHex} (${sign}${instrCount + 1}${arrow})`;
    };
    if (instr === 0) {
        return { op: "nop", args: "" };
    }
    // SPECIAL opcode (0x00)
    if (opcode === 0x00) {
        switch (funct) {
            case 0x00:
                return sa === 0
                    ? { op: "nop", args: "" }
                    : { op: "sll", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x02:
                return { op: "srl", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x03:
                return { op: "sra", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x04:
                return { op: "sllv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
            case 0x06:
                return { op: "srlv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
            case 0x07:
                return { op: "srav", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
            case 0x08:
                return { op: "jr", args: reg(rs) };
            case 0x09:
                return rd === 31
                    ? { op: "jalr", args: reg(rs) }
                    : { op: "jalr", args: `${reg(rd)}, ${reg(rs)}` };
            case 0x0a:
                return { op: "movz", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x0b:
                return { op: "movn", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x0c:
                return { op: "syscall", args: code > 0 ? `${code}` : "" };
            case 0x0d:
                return { op: "break", args: code > 0 ? `${code}` : "" };
            case 0x0f:
                return { op: "sync", args: sa > 0 ? `${sa}` : "" };
            case 0x10:
                return { op: "mfhi", args: reg(rd) };
            case 0x11:
                return { op: "mthi", args: reg(rs) };
            case 0x12:
                return { op: "mflo", args: reg(rd) };
            case 0x13:
                return { op: "mtlo", args: reg(rs) };
            case 0x14:
                return { op: "dsllv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
            case 0x16:
                return { op: "dsrlv", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
            case 0x17:
                return { op: "dsrav", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
            case 0x18:
                return rd === 0
                    ? { op: "mult", args: `${reg(rs)}, ${reg(rt)}` }
                    : { op: "mult", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x19:
                return rd === 0
                    ? { op: "multu", args: `${reg(rs)}, ${reg(rt)}` }
                    : { op: "multu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x1a:
                return { op: "div", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x1b:
                return { op: "divu", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x1c:
                return {
                    op: "dmult",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x1d:
                return {
                    op: "dmultu",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x1e:
                return { op: "ddiv", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x1f:
                return { op: "ddivu", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x20:
                return { op: "add", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x21:
                return { op: "addu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x22:
                return { op: "sub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x23:
                return { op: "subu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x24:
                return { op: "and", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x25:
                return { op: "or", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x26:
                return { op: "xor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x27:
                return { op: "nor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x28:
                return { op: "mfsa", args: reg(rd) };
            case 0x29:
                return { op: "mtsa", args: reg(rs) };
            case 0x2a:
                return { op: "slt", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x2b:
                return { op: "sltu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x2c:
                return { op: "dadd", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x2d:
                return { op: "daddu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x2e:
                return { op: "dsub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x2f:
                return { op: "dsubu", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
            case 0x30:
                return { op: "tge", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x31:
                return { op: "tgeu", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x32:
                return { op: "tlt", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x33:
                return { op: "tltu", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x34:
                return { op: "teq", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x36:
                return { op: "tne", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x38:
                return { op: "dsll", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3a:
                return { op: "dsrl", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3b:
                return { op: "dsra", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3c:
                return { op: "dsll32", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3e:
                return { op: "dsrl32", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3f:
                return { op: "dsra32", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
        }
    }
    // REGIMM opcode (0x01)
    if (opcode === 0x01) {
        switch (rt) {
            case 0x00:
                return {
                    op: "bltz",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x01:
                return {
                    op: "bgez",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x02:
                return {
                    op: "bltzl",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x03:
                return {
                    op: "bgezl",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x08:
                return { op: "tgei", args: `${reg(rs)}, ${formatImm(signedImm)}` };
            case 0x09:
                return { op: "tgeiu", args: `${reg(rs)}, ${formatImm(signedImm)}` };
            case 0x0a:
                return { op: "tlti", args: `${reg(rs)}, ${formatImm(signedImm)}` };
            case 0x0b:
                return { op: "tltiu", args: `${reg(rs)}, ${formatImm(signedImm)}` };
            case 0x0c:
                return { op: "teqi", args: `${reg(rs)}, ${formatImm(signedImm)}` };
            case 0x0e:
                return { op: "tnei", args: `${reg(rs)}, ${formatImm(signedImm)}` };
            case 0x10:
                return {
                    op: "bltzal",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x11:
                return {
                    op: "bgezal",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x12:
                return {
                    op: "bltzall",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x13:
                return {
                    op: "bgezall",
                    args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
                };
            case 0x18:
                return { op: "mtsab", args: `${reg(rs)}, ${formatImm(imm)}` };
            case 0x19:
                return { op: "mtsah", args: `${reg(rs)}, ${formatImm(imm)}` };
        }
    }
    // J and JAL (0x02, 0x03)
    if (opcode === 0x02) {
        return { op: "j", args: formatAddr((pc & 0xf0000000) | (target << 2)) };
    }
    if (opcode === 0x03) {
        return { op: "jal", args: formatAddr((pc & 0xf0000000) | (target << 2)) };
    }
    // Branch instructions (0x04-0x07, 0x14-0x17)
    if (opcode === 0x04)
        return {
            op: "beq",
            args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x05)
        return {
            op: "bne",
            args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x06)
        return {
            op: "blez",
            args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x07)
        return {
            op: "bgtz",
            args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x14)
        return {
            op: "beql",
            args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x15)
        return {
            op: "bnel",
            args: `${reg(rs)}, ${reg(rt)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x16)
        return {
            op: "blezl",
            args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    if (opcode === 0x17)
        return {
            op: "bgtzl",
            args: `${reg(rs)}, ${formatBranch(pc + 4 + (signedImm << 2), signedImm << 2)}`,
        };
    // Immediate arithmetic/logical (0x08-0x0f, 0x18-0x19)
    if (opcode === 0x08)
        return {
            op: "addi",
            args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
        };
    if (opcode === 0x09)
        return {
            op: "addiu",
            args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
        };
    if (opcode === 0x0a)
        return {
            op: "slti",
            args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
        };
    if (opcode === 0x0b)
        return { op: "sltiu", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
    if (opcode === 0x0c)
        return { op: "andi", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
    if (opcode === 0x0d)
        return { op: "ori", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
    if (opcode === 0x0e)
        return { op: "xori", args: `${reg(rt)}, ${reg(rs)}, ${formatImm(imm)}` };
    if (opcode === 0x0f)
        return { op: "lui", args: `${reg(rt)}, ${formatImm(imm)}` };
    if (opcode === 0x18)
        return {
            op: "daddi",
            args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
        };
    if (opcode === 0x19)
        return {
            op: "daddiu",
            args: `${reg(rt)}, ${reg(rs)}, ${formatImm(signedImm)}`,
        };
    // Load instructions (0x1a-0x1b, 0x20-0x27, 0x37)
    if (opcode === 0x1a)
        return {
            op: "ldl",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x1b)
        return {
            op: "ldr",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x20)
        return {
            op: "lb",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x21)
        return {
            op: "lh",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x22)
        return {
            op: "lwl",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x23)
        return {
            op: "lw",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x24)
        return {
            op: "lbu",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x25)
        return {
            op: "lhu",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x26)
        return {
            op: "lwr",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x27)
        return {
            op: "lwu",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x37)
        return {
            op: "ld",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    // Store instructions (0x28-0x2f, 0x3f)
    if (opcode === 0x28)
        return {
            op: "sb",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x29)
        return {
            op: "sh",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x2a)
        return {
            op: "swl",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x2b)
        return {
            op: "sw",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x2c)
        return {
            op: "sdl",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x2d)
        return {
            op: "sdr",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x2e)
        return {
            op: "swr",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x2f)
        return { op: "cache", args: `${rt}, ${formatImm(signedImm)}(${reg(rs)})` };
    if (opcode === 0x33)
        return { op: "pref", args: `${rt}, ${formatImm(signedImm)}(${reg(rs)})` };
    if (opcode === 0x3f)
        return {
            op: "sd",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    // Quadword loads/stores (PS2 specific - 128-bit)
    if (opcode === 0x1e)
        return {
            op: "lq",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x1f)
        return {
            op: "sq",
            args: `${reg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    // Floating point loads/stores
    if (opcode === 0x31)
        return {
            op: "lwc1",
            args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x35)
        return {
            op: "ldc1",
            args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x36)
        return {
            op: "lqc2",
            args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x39)
        return {
            op: "swc1",
            args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x3d)
        return {
            op: "sdc1",
            args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    if (opcode === 0x3e)
        return {
            op: "sqc2",
            args: `${freg(rt)}, ${formatImm(signedImm)}(${reg(rs)})`,
        };
    // MMI opcode (0x1C) - PS2 specific
    if (opcode === 0x1c) {
        const mmiFunc = funct & 0x3f;
        const mmiFmt = (instr >> 6) & 0x1f;
        switch (mmiFunc) {
            case 0x00:
                return {
                    op: "madd",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x01:
                return {
                    op: "maddu",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x04:
                return { op: "plzcw", args: `${reg(rd)}, ${reg(rs)}` };
            case 0x08:
                switch (mmiFmt) {
                    case 0x00:
                        return { op: "paddw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x01:
                        return { op: "psubw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x02:
                        return { op: "pcgtw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x03:
                        return { op: "pmaxw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x04:
                        return { op: "paddh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x05:
                        return { op: "psubh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x06:
                        return { op: "pcgth", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x07:
                        return { op: "pmaxh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x08:
                        return { op: "paddb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x09:
                        return { op: "psubb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0a:
                        return { op: "pcgtb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x10:
                        return { op: "paddsw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x11:
                        return { op: "psubsw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x12:
                        return { op: "pextlw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x13:
                        return { op: "ppacw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x14:
                        return { op: "paddsh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x15:
                        return { op: "psubsh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x16:
                        return { op: "pextlh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x17:
                        return { op: "ppach", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x18:
                        return { op: "paddsb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x19:
                        return { op: "psubsb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x1a:
                        return { op: "pextlb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x1b:
                        return { op: "ppacb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x1e:
                        return { op: "pext5", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x1f:
                        return { op: "ppac5", args: `${reg(rd)}, ${reg(rt)}` };
                }
                break;
            case 0x09:
                switch (mmiFmt) {
                    case 0x00:
                        return { op: "pmaddw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x01:
                        return { op: "pmsubw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x02:
                        return { op: "psllvw", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
                    case 0x03:
                        return { op: "psrlvw", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
                    case 0x04:
                        return { op: "pmfhi", args: reg(rd) };
                    case 0x05:
                        return { op: "pmflo", args: reg(rd) };
                    case 0x06:
                        return { op: "pinth", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x08:
                        return { op: "pmultw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x09:
                        return { op: "pdivw", args: `${reg(rs)}, ${reg(rt)}` };
                    case 0x0a:
                        return { op: "pcpyld", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0c:
                        return { op: "pmaddh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0d:
                        return { op: "phmadh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0e:
                        return { op: "pand", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0f:
                        return { op: "pxor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x10:
                        return { op: "pmsubh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x11:
                        return { op: "phmsbh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x12:
                        return { op: "pexeh", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x13:
                        return { op: "prevh", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x14:
                        return { op: "pmulth", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x15:
                        return { op: "pdivbw", args: `${reg(rs)}, ${reg(rt)}` };
                    case 0x16:
                        return { op: "pexew", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x17:
                        return { op: "prot3w", args: `${reg(rd)}, ${reg(rt)}` };
                }
                break;
            case 0x10:
                return { op: "mfhi1", args: reg(rd) };
            case 0x11:
                return { op: "mthi1", args: reg(rs) };
            case 0x12:
                return { op: "mflo1", args: reg(rd) };
            case 0x13:
                return { op: "mtlo1", args: reg(rs) };
            case 0x18:
                return {
                    op: "mult1",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x19:
                return {
                    op: "multu1",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x1a:
                return { op: "div1", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x1b:
                return { op: "divu1", args: `${reg(rs)}, ${reg(rt)}` };
            case 0x20:
                return {
                    op: "madd1",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x21:
                return {
                    op: "maddu1",
                    args: rd === 0
                        ? `${reg(rs)}, ${reg(rt)}`
                        : `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                };
            case 0x28:
                switch (mmiFmt) {
                    case 0x00:
                        return {
                            op: "pmadduw",
                            args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                        };
                    case 0x02:
                        return { op: "psravw", args: `${reg(rd)}, ${reg(rt)}, ${reg(rs)}` };
                    case 0x03:
                        return { op: "pmthi", args: reg(rs) };
                    case 0x04:
                        return { op: "pmtlo", args: reg(rs) };
                    case 0x05:
                        return { op: "pinteh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x08:
                        return {
                            op: "pmultuw",
                            args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}`,
                        };
                    case 0x09:
                        return { op: "pdivuw", args: `${reg(rs)}, ${reg(rt)}` };
                    case 0x0a:
                        return { op: "pcpyud", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0e:
                        return { op: "por", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0f:
                        return { op: "pnor", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x12:
                        return { op: "pexch", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x13:
                        return { op: "pcpyh", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x16:
                        return { op: "pexcw", args: `${reg(rd)}, ${reg(rt)}` };
                }
                break;
            case 0x29:
                switch (mmiFmt) {
                    case 0x00:
                        return { op: "psubuw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x01:
                        return { op: "pextuw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x02:
                        return { op: "pceqw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x03:
                        return { op: "pminw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x04:
                        return { op: "padsbh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x05:
                        return { op: "psubuh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x06:
                        return { op: "pceqh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x07:
                        return { op: "pminh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x0a:
                        return { op: "pceqb", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x10:
                        return { op: "padduw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x11:
                        return { op: "pextuw", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x14:
                        return { op: "padduh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x15:
                        return { op: "psubuh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x16:
                        return { op: "pextuh", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x18:
                        return { op: "paddub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x19:
                        return { op: "psubub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x1a:
                        return { op: "pextub", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x1b:
                        return { op: "qfsrv", args: `${reg(rd)}, ${reg(rs)}, ${reg(rt)}` };
                    case 0x1d:
                        return { op: "pabsw", args: `${reg(rd)}, ${reg(rt)}` };
                    case 0x1e:
                        return { op: "pabsh", args: `${reg(rd)}, ${reg(rt)}` };
                }
                break;
            case 0x30:
                const fmt = mmiFmt & 0x1f;
                switch (fmt) {
                    case 0x00:
                        return { op: "pmfhl.lw", args: reg(rd) };
                    case 0x01:
                        return { op: "pmfhl.uw", args: reg(rd) };
                    case 0x02:
                        return { op: "pmfhl.slw", args: reg(rd) };
                    case 0x03:
                        return { op: "pmfhl.lh", args: reg(rd) };
                    case 0x04:
                        return { op: "pmfhl.sh", args: reg(rd) };
                }
                break;
            case 0x31:
                if (mmiFmt === 0x00)
                    return { op: "pmthl.lw", args: reg(rs) };
                break;
            case 0x34:
                return { op: "psllh", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x36:
                return { op: "psrlh", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x37:
                return { op: "psrah", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3c:
                return { op: "psllw", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3e:
                return { op: "psrlw", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
            case 0x3f:
                return { op: "psraw", args: `${reg(rd)}, ${reg(rt)}, ${sa}` };
        }
    }
    // COP0 opcode (0x10)
    if (opcode === 0x10) {
        const cop0Func = rs;
        if (cop0Func === 0x00)
            return { op: "mfc0", args: `${reg(rt)}, $${rd}` };
        if (cop0Func === 0x04)
            return { op: "mtc0", args: `${reg(rt)}, $${rd}` };
        if (cop0Func === 0x08) {
            const bc0 = rt;
            if (bc0 === 0x00)
                return {
                    op: "bc0f",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc0 === 0x01)
                return {
                    op: "bc0t",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc0 === 0x02)
                return {
                    op: "bc0fl",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc0 === 0x03)
                return {
                    op: "bc0tl",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
        }
        if (cop0Func === 0x10) {
            if (funct === 0x01)
                return { op: "tlbr", args: "" };
            if (funct === 0x02)
                return { op: "tlbwi", args: "" };
            if (funct === 0x06)
                return { op: "tlbwr", args: "" };
            if (funct === 0x08)
                return { op: "tlbp", args: "" };
            if (funct === 0x18)
                return { op: "eret", args: "" };
            if (funct === 0x38)
                return { op: "ei", args: "" };
            if (funct === 0x39)
                return { op: "di", args: "" };
        }
    }
    // COP1 (FPU) opcode (0x11)
    if (opcode === 0x11) {
        const cop1Func = rs;
        if (cop1Func === 0x00)
            return { op: "mfc1", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop1Func === 0x02)
            return { op: "cfc1", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop1Func === 0x04)
            return { op: "mtc1", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop1Func === 0x06)
            return { op: "ctc1", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop1Func === 0x08) {
            const bc1 = rt;
            if (bc1 === 0x00)
                return {
                    op: "bc1f",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc1 === 0x01)
                return {
                    op: "bc1t",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc1 === 0x02)
                return {
                    op: "bc1fl",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc1 === 0x03)
                return {
                    op: "bc1tl",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
        }
        if (cop1Func === 0x10) {
            // S format (single precision)
            switch (funct) {
                case 0x00:
                    return { op: "add.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
                case 0x01:
                    return { op: "sub.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
                case 0x02:
                    return { op: "mul.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
                case 0x03:
                    return { op: "div.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
                case 0x04:
                    return { op: "sqrt.s", args: `${freg(sa)}, ${freg(rt)}` };
                case 0x05:
                    return { op: "abs.s", args: `${freg(sa)}, ${freg(rd)}` };
                case 0x06:
                    return { op: "mov.s", args: `${freg(sa)}, ${freg(rd)}` };
                case 0x07:
                    return { op: "neg.s", args: `${freg(sa)}, ${freg(rd)}` };
                case 0x16:
                    return {
                        op: "rsqrt.s",
                        args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}`,
                    };
                case 0x18:
                    return { op: "adda.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x19:
                    return { op: "suba.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x1a:
                    return { op: "mula.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x1c:
                    return {
                        op: "madd.s",
                        args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}`,
                    };
                case 0x1d:
                    return {
                        op: "msub.s",
                        args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}`,
                    };
                case 0x1e:
                    return { op: "madda.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x1f:
                    return { op: "msuba.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x24:
                    return { op: "cvt.w.s", args: `${freg(sa)}, ${freg(rd)}` };
                case 0x28:
                    return { op: "max.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
                case 0x29:
                    return { op: "min.s", args: `${freg(sa)}, ${freg(rd)}, ${freg(rt)}` };
                case 0x30:
                    return { op: "c.f.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x32:
                    return { op: "c.eq.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x34:
                    return { op: "c.lt.s", args: `${freg(rd)}, ${freg(rt)}` };
                case 0x36:
                    return { op: "c.le.s", args: `${freg(rd)}, ${freg(rt)}` };
            }
        }
        if (cop1Func === 0x14) {
            // W format (word fixed point)
            if (funct === 0x20)
                return { op: "cvt.s.w", args: `${freg(sa)}, ${freg(rd)}` };
        }
    }
    // COP2 (VU0) opcode (0x12) - PS2 Vector Unit
    if (opcode === 0x12) {
        const cop2Func = rs;
        if (cop2Func === 0x01)
            return { op: "qmfc2", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop2Func === 0x02)
            return { op: "cfc2", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop2Func === 0x05)
            return { op: "qmtc2", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop2Func === 0x06)
            return { op: "ctc2", args: `${reg(rt)}, ${freg(rd)}` };
        if (cop2Func === 0x08) {
            const bc2 = rt;
            if (bc2 === 0x00)
                return {
                    op: "bc2f",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc2 === 0x01)
                return {
                    op: "bc2t",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc2 === 0x02)
                return {
                    op: "bc2fl",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
            if (bc2 === 0x03)
                return {
                    op: "bc2tl",
                    args: formatBranch(pc + 4 + (signedImm << 2), signedImm << 2),
                };
        }
        // VU0 macro mode instructions (COP2 special functions)
        if ((cop2Func & 0x10) === 0x10) {
            const vuFunc = funct & 0x3f;
            const dest = (instr >> 21) & 0xf; // destination mask (xyzw)
            const ft = (instr >> 16) & 0x1f;
            const fs = (instr >> 11) & 0x1f;
            const fd = (instr >> 6) & 0x1f;
            const bc = (instr >> 0) & 0x3; // broadcast field
            const vreg = (r) => `vf${r}`;
            const vireg = (r) => `vi${r}`;
            const destStr = (d) => {
                if (d === 0xf)
                    return "";
                let s = "";
                if (d & 8)
                    s += "x";
                if (d & 4)
                    s += "y";
                if (d & 2)
                    s += "z";
                if (d & 1)
                    s += "w";
                return s ? `.${s}` : "";
            };
            const bcStr = (b) => ["x", "y", "z", "w"][b];
            // VU0 computational instructions
            switch (vuFunc) {
                case 0x00:
                    return {
                        op: "vadd",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x01:
                    return {
                        op: "vsub",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x02:
                    return {
                        op: "vmul",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x03:
                    return {
                        op: "vmax",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x04:
                    return {
                        op: "vmin",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x05:
                    return {
                        op: "vmul",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x06:
                    return {
                        op: "vmulq",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
                    };
                case 0x07:
                    return {
                        op: "vmaxi",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
                    };
                case 0x08:
                    return {
                        op: "vaddq",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
                    };
                case 0x09:
                    return {
                        op: "vsubq",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
                    };
                case 0x0a:
                    return {
                        op: "vaddi",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
                    };
                case 0x0b:
                    return {
                        op: "vsubi",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
                    };
                case 0x0c:
                    return {
                        op: "vmini",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
                    };
                case 0x0d:
                    return {
                        op: "vadd",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x0e:
                    return {
                        op: "vsub",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x0f:
                    return {
                        op: "vopmsub",
                        args: `${vreg(fd)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x10:
                    return {
                        op: "vacc",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x11:
                    return {
                        op: "vmsub",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x12:
                    return {
                        op: "vmadd",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x13:
                    return {
                        op: "vmmax",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x14:
                    return {
                        op: "vmmin",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x15:
                    return {
                        op: "vmsub",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x16:
                    return {
                        op: "vmsubq",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
                    };
                case 0x17:
                    return {
                        op: "vmmini",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
                    };
                case 0x18:
                    return {
                        op: "vaddabc",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x19:
                    return {
                        op: "vsubabc",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x1a:
                    return {
                        op: "vmulabc",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x1b:
                    return {
                        op: "vmadda",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x1c:
                    return {
                        op: "vmsuba",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x1d:
                    return {
                        op: "vmadd",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}.${bcStr(bc)}`,
                    };
                case 0x1e:
                    return {
                        op: "vmaddq",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, Q`,
                    };
                case 0x1f:
                    return {
                        op: "vmmaxi",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}, I`,
                    };
                case 0x20:
                    return { op: "vaddai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
                case 0x21:
                    return { op: "vsubai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
                case 0x22:
                    return { op: "vmulai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
                case 0x23:
                    return { op: "vmaddai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
                case 0x24:
                    return { op: "vmsubai", args: `ACC${destStr(dest)}, ${vreg(fs)}, I` };
                case 0x25:
                    return {
                        op: "vadda",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x26:
                    return {
                        op: "vsuba",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x27:
                    return {
                        op: "vmula",
                        args: `ACC${destStr(dest)}, ${vreg(fs)}, ${vreg(ft)}`,
                    };
                case 0x28:
                    return { op: "vopmula", args: `ACC, ${vreg(fs)}, ${vreg(ft)}` };
                case 0x29:
                    return { op: "vnop", args: "" };
                case 0x2a:
                    return {
                        op: "vmove",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}`,
                    };
                case 0x2b:
                    return {
                        op: "vmr32",
                        args: `${vreg(fd)}${destStr(dest)}, ${vreg(fs)}`,
                    };
                case 0x2c:
                    return {
                        op: "vlqi",
                        args: `${vreg(fd)}${destStr(dest)}, (${vireg(fs)}++)`,
                    };
                case 0x2d:
                    return {
                        op: "vsqi",
                        args: `${vreg(fs)}${destStr(dest)}, (${vireg(ft)}++)`,
                    };
                case 0x2e:
                    return {
                        op: "vlqd",
                        args: `${vreg(fd)}${destStr(dest)}, (--${vireg(fs)})`,
                    };
                case 0x2f:
                    return {
                        op: "vsqd",
                        args: `${vreg(fs)}${destStr(dest)}, (--${vireg(ft)})`,
                    };
                case 0x30:
                    return { op: "vdiv", args: `Q, ${vreg(fs)}, ${vreg(ft)}` };
                case 0x31:
                    return { op: "vsqrt", args: `Q, ${vreg(ft)}` };
                case 0x32:
                    return { op: "vrsqrt", args: `Q, ${vreg(fs)}, ${vreg(ft)}` };
                case 0x33:
                    return { op: "vwaitq", args: "" };
                case 0x34:
                    return { op: "vmtir", args: `${vireg(ft)}, ${vreg(fs)}` };
                case 0x35:
                    return {
                        op: "vmfir",
                        args: `${vreg(fd)}${destStr(dest)}, ${vireg(fs)}`,
                    };
                case 0x36:
                    return { op: "vilwr", args: `${vireg(ft)}, (${vireg(fs)})` };
                case 0x37:
                    return { op: "viswr", args: `${vireg(ft)}, (${vireg(fs)})` };
                case 0x38:
                    return { op: "vrnext", args: `${vreg(fd)}${destStr(dest)}, R` };
                case 0x39:
                    return { op: "vrget", args: `${vreg(fd)}${destStr(dest)}, R` };
                case 0x3a:
                    return { op: "vrinit", args: `R, ${vreg(fs)}` };
                case 0x3b:
                    return { op: "vrxor", args: `R, ${vreg(fs)}` };
                case 0x3c:
                    return { op: "vclip", args: `${vreg(fs)}, ${vreg(ft)}` };
                case 0x3d:
                    return { op: "vmaddaq", args: `ACC${destStr(dest)}, ${vreg(fs)}, Q` };
                case 0x3e:
                    return { op: "vmsubaq", args: `ACC${destStr(dest)}, ${vreg(fs)}, Q` };
                case 0x3f:
                    return { op: "vmulaq", args: `ACC${destStr(dest)}, ${vreg(fs)}, Q` };
            }
            // VU0 special instructions (lower instructions with different encoding)
            const vuLower = (instr >> 25) & 0x7f;
            if (vuLower === 0x30 ||
                vuLower === 0x31 ||
                vuLower === 0x32 ||
                vuLower === 0x33) {
                const it = (instr >> 16) & 0x1f;
                const is = (instr >> 11) & 0x1f;
                const id = (instr >> 6) & 0x1f;
                const vireg = (r) => `vi${r}`;
                switch (funct) {
                    case 0x30:
                        return {
                            op: "viadd",
                            args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
                        };
                    case 0x31:
                        return {
                            op: "visub",
                            args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
                        };
                    case 0x32:
                        return {
                            op: "viaddi",
                            args: `${vireg(it)}, ${vireg(is)}, ${(instr & 0x1f) - (instr & 0x10 ? 32 : 0)}`,
                        };
                    case 0x33:
                        return {
                            op: "viand",
                            args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
                        };
                    case 0x34:
                        return {
                            op: "vior",
                            args: `${vireg(id)}, ${vireg(is)}, ${vireg(it)}`,
                        };
                }
            }
        }
    }
    // VU1 instructions via VCALLMS/VCALLMSR (executed through COP2 interface)
    // VU1 uses same instruction encoding as VU0 but executes on VU1 unit
    // These are typically triggered via special COP2 instructions or DMA
    // If we reach here, instruction is unknown
    return { op: "unknown", args: "", label: "", comment: "" };
}


/***/ }),
/* 5 */
/***/ ((__unused_webpack_module, exports, __webpack_require__) => {


/**
 * Template for the Disassembly Editor
 * Large complex HTML moved from external file to TypeScript module
 */
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getDisasmEditorHtml = getDisasmEditorHtml;
const disasmEditorStyles_1 = __webpack_require__(6);
function getDisasmEditorHtml(data, nonce) {
    return `<!DOCTYPE html> 
<html> 
<head> 
<meta charset="UTF-8"> 
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">

${(0, disasmEditorStyles_1.getDisasmEditorStyles)()}

</head> 
 
<body>

${getGotoPanel()}
${getSearchPanel()}
${getHexContainer()}
${getGrid()}

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
function getHexContainer() {
    return `<div id="hex-container"></div>`;
}
function getGrid() {
    return `<div id="grid" tabindex="0"></div>`;
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

const state = {
    cursor: 0,
    disasmRows: [],
    totalInstructions: 0,
    pageSize: 1000,
    baseAddress: 0,
    hexData: [],
    hexOffset: 0,
    loadedPages: new Map(),
    searchMode: 'text',
    searchQuery: '',
    searchMatches: [],
    currentMatch: -1,
    searchActive: false,
    gotoActive: false
};

const clamp = (val, min, max) => Math.max(min, Math.min(val, max));
const getPageStart = index => Math.floor(index / state.pageSize) * state.pageSize;
const getLocalIndex = index => index - getPageStart(index);

window.addEventListener("message", ({ data: msg }) => {
    console.log('[WEBVIEW] Received message:', msg.type, msg);
    switch (msg.type) {
        case "init":
            console.log('[WEBVIEW] Init:', msg.totalInstructions, 'instructions');
            state.totalInstructions = msg.totalInstructions;
            state.pageSize = msg.pageSize;
            state.baseAddress = msg.baseAddress;
            break;
        case "disasmPage":
            console.log('[WEBVIEW] disasmPage:', msg.rows?.length || 0, 'rows, startIndex:', msg.startIndex);
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
    checkAndLoadPage();
    updateCursor();
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
    if (addr < state.baseAddress) {
        gotoInfo.textContent = 'Address below base';
        return;
    }
    const instrIndex = Math.floor((addr - state.baseAddress) / 4);
    if (instrIndex < 0 || instrIndex >= state.totalInstructions) {
        gotoInfo.textContent = 'Address out of range';
        return;
    }
    state.cursor = instrIndex;
    state.searchActive = false;
    searchPanel.classList.remove('visible');
    checkAndLoadPage();
    updateCursor();
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
        row.innerHTML = buildDisasmRowHtml(rowData, globalIndex);
        row.onclick = () => {
            state.cursor = globalIndex;
            updateCursor();
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
    const delta = { ArrowDown: 1, ArrowUp: -1, PageDown: 50, PageUp: -50 }[e.key];
    if (!delta) return;
    e.preventDefault();
    state.cursor = clamp(state.cursor + delta, 0, state.totalInstructions - 1);
    checkAndLoadPage();
    updateCursor();
});

function checkAndLoadPage() {
    const startIndex = getPageStart(state.cursor);
    if (!state.loadedPages.has(startIndex)) {
        vscode.postMessage({ type: "requestPage", startIndex });
    } else {
        const cachedRows = state.loadedPages.get(startIndex);
        if (state.disasmRows !== cachedRows) {
            state.disasmRows = cachedRows;
            renderDisassembly();
        }
    }
}

window.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyA') {
        e.preventDefault();
        e.stopPropagation();
        toggleGoto();
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
    rowEl.scrollIntoView({ block: "nearest", behavior: "smooth" });
    const row = state.disasmRows[localIndex];
    if (!row) return;
    handleBranchHighlight(row, localIndex);
    updateHexForRow(row);
}

function updateHexForRow(row) {
    const addr = parseInt(row.addr, 16);
    const length = row.bytes?.trim().split(/\\s+/).length || 4;
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
`;
}


/***/ }),
/* 6 */
/***/ ((__unused_webpack_module, exports) => {


/**
 * Styles for the Disassembly Editor
 */
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getDisasmEditorStyles = getDisasmEditorStyles;
function getDisasmEditorStyles() {
    return `<style> 
body { 
    margin: 0; 
    font-family: monospace; 
    background: #1e1e1e;
    color: #d4d4d4;
} 

#search-panel {
    display: none;
    background: #252526;
    border-bottom: 1px solid #3e3e42;
    padding: 8px 12px;
    position: sticky;
    top: 0;
    z-index: 100;
}

#search-panel.visible {
    display: block;
}

.search-row {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 6px;
}

.search-input-wrapper {
    position: relative;
    flex: 1;
    max-width: 400px;
}

.search-input {
    width: 100%;
    background: #3c3c3c;
    border: 1px solid #3e3e42;
    color: #cccccc;
    padding: 4px 24px 4px 8px;
    font-family: monospace;
    font-size: 13px;
    outline: none;
}

.search-input:focus {
    border-color: #007acc;
}

.search-input.error {
    border-color: #f48771;
}

.search-clear {
    position: absolute;
    right: 4px;
    top: 50%;
    transform: translateY(-50%);
    background: none;
    border: none;
    color: #cccccc;
    cursor: pointer;
    padding: 2px 4px;
    font-size: 16px;
    line-height: 1;
    display: none;
}

.search-clear:hover {
    color: #fff;
}

.search-input:not(:placeholder-shown) ~ .search-clear {
    display: block;
}

.search-mode {
    display: flex;
    gap: 4px;
}

.mode-btn {
    background: #3c3c3c;
    border: 1px solid #3e3e42;
    color: #cccccc;
    padding: 4px 8px;
    font-family: monospace;
    font-size: 11px;
    cursor: pointer;
    outline: none;
}

.mode-btn:hover {
    background: #505050;
}

.mode-btn.active {
    background: #007acc;
    border-color: #007acc;
    color: #fff;
}

.search-nav {
    display: flex;
    gap: 4px;
}

.nav-btn {
    background: #3c3c3c;
    border: 1px solid #3e3e42;
    color: #cccccc;
    padding: 4px 8px;
    cursor: pointer;
    font-size: 11px;
    outline: none;
    min-width: 24px;
}

.nav-btn:hover:not(:disabled) {
    background: #505050;
}

.nav-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.search-info {
    color: #cccccc;
    font-size: 11px;
    white-space: nowrap;
}

.close-search {
    background: none;
    border: none;
    color: #cccccc;
    cursor: pointer;
    padding: 4px 8px;
    font-size: 16px;
    margin-left: auto;
}

.close-search:hover {
    color: #fff;
}

#hex-container {
    font-family: monospace;
    overflow: auto;
    max-height: 120px;
    background: #2d2d30;
    border-bottom: 1px solid #3e3e42;
    padding: 4px;
}

.hex-row {
    display: flex;
    gap: 8px;
    padding: 2px 6px;
    line-height: 1.4;
}

.hex-addr {
    color: #858585;
    width: 10ch;
}

.hex-bytes {
    display: flex;
    gap: 8px;
    flex: 1;
}

.hex-byte {
    color: #d4d4d4;
    width: 2ch;
    text-align: center;
}

.hex-byte.highlight {
    background: #264f78;
    color: #fff;
}

.hex-ascii {
    color: #608b4e;
    width: 16ch;
    border-left: 1px solid #3e3e42;
    padding-left: 8px;
    display: flex;
}

.hex-char {
    width: 1ch;
    display: inline-block;
    text-align: center;
}

.hex-char.highlight {
    background: #264f78;
    color: #fff;
}
 
#grid { 
    outline: none; 
    max-height: calc(100vh - 120px); 
    overflow-y: auto;
} 
 
.row {
    display: grid;
    grid-template-columns: 10ch 12ch 18ch 8ch 40ch 1fr;
    padding: 2px 6px;
    cursor: default;
}
 
.row.active {
    background: #264f78;
}

.row.branch-highlight {
    background: #3a3d41;
}

.row.active.branch-highlight {
    background: #264f78;
}

.row.search-match {
    background: #613214;
}

.row.search-current {
    background: #4e3c1e;
}

.row.active.search-match,
.row.active.search-current {
    background: #264f78;
}
 
.addr { color: #c586c0; } 
.bytes { color: #9cdcfe; } 
.label { color: #4ec9b0; } 
.op { color: #dcdcaa; } 
.args { color: #ce9178; } 
.comment { color: #6a9955; } 

.search-match-highlight {
    background: #f9826c;
    color: #000;
    padding: 0 2px;
}

.search-current-highlight {
    background: #ea5c00;
    color: #fff;
    padding: 0 2px;
}

#goto-panel {
    display: none;
    background: #252526;
    border-bottom: 1px solid #3e3e42;
    padding: 8px 12px;
    position: sticky;
    top: 0;
    z-index: 100;
}

#goto-panel.visible {
    display: block;
}

</style>`;
}


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
const child_process_1 = __webpack_require__(9);
const helpers_1 = __webpack_require__(4);
const disasmPanelTemplate_1 = __webpack_require__(10);
const extension_1 = __webpack_require__(0);
class DisasmPanelProvider {
    webview;
    emulatorProcess;
    settings = {};
    workspaceRoot = '';
    // Register the provider with VS Code
    register(context) {
        context.subscriptions.push(vscode.window.registerWebviewViewProvider('disasm.disasmPanel', this));
    }
    resolveWebviewView(webviewView) {
        this.webview = webviewView.webview;
        // Resolve workspace root (used to turn relative paths into absolute)
        this.workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';
        // Load emu-settings.json from the workspace root
        this.loadEmuSettings();
        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true
        };
        // Render the panel with settings data
        webviewView.webview.html = this.buildHtml();
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async (msg) => {
            switch (msg.command) {
                case 'connect':
                    this.handleConnect(msg.emulatorPath, msg.biosPath, msg.gamePath);
                    break;
                case 'disassemble':
                    await this.handleDisassemble(msg.data);
                    break;
                // Dropdown change notifications (no-op for now – stored server-side if needed)
                case 'emulatorChanged':
                case 'biosChanged':
                case 'gameChanged':
                    break;
            }
        });
    }
    // ── Settings ──────────────────────────────────────────────────────────────
    loadEmuSettings() {
        if (!this.workspaceRoot) {
            return;
        }
        const settingsPath = path.join(this.workspaceRoot, 'emu-settings.json');
        try {
            const raw = fs.readFileSync(settingsPath, 'utf-8');
            this.settings = JSON.parse(raw);
        }
        catch (err) {
            const outputChannel = (0, extension_1.getOutputChannel)();
            outputChannel.appendLine(`[WARN] Could not load emu-settings.json: ${err?.message ?? err}`);
            this.settings = {};
        }
    }
    buildHtml() {
        const emulators = (this.settings.emulators ?? []).map(e => ({
            type: e.type,
            path: e.path
        }));
        const biosOptions = (this.settings.bios ?? []).map(b => ({
            name: b.name,
            path: b.path
        }));
        const gameOptions = (this.settings.games ?? []).map(g => ({
            name: g.name,
            path: g.path
        }));
        const templateData = {
            connected: this.emulatorProcess != null,
            emulators,
            biosOptions,
            gameOptions
        };
        return (0, disasmPanelTemplate_1.getDisasmPanelHtml)(templateData, (0, helpers_1.getNonce)());
    }
    // ── Emulator launch ───────────────────────────────────────────────────────
    handleConnect(emulatorPath, biosPath, gamePath) {
        const outputChannel = (0, extension_1.getOutputChannel)();
        // If already running, kill it (toggle behaviour)
        if (this.emulatorProcess) {
            this.emulatorProcess.kill();
            outputChannel.appendLine('[INFO] Emulator stopped by user.');
            this.cleanupProcess();
            return;
        }
        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }
        // Resolve relative paths against the workspace root
        const resolvedExe = this.resolvePath(emulatorPath);
        const resolvedBios = biosPath ? this.resolvePath(biosPath) : undefined;
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : undefined;
        // Build argument list:  --run [--bios <path>] [--disc <path>]
        const cliArgs = ['--run'];
        if (resolvedBios) {
            cliArgs.push('--bios', resolvedBios);
        }
        if (resolvedDisc) {
            cliArgs.push('--disc', resolvedDisc);
        }
        outputChannel.show(true);
        outputChannel.appendLine(`[INFO] Starting emulator: ${resolvedExe} ${cliArgs.join(' ')}`);
        // Spawn without a visible console window
        const proc = (0, child_process_1.spawn)(resolvedExe, cliArgs, {
            windowsHide: true,
            stdio: ['ignore', 'pipe', 'pipe']
        });
        this.emulatorProcess = proc;
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });
        proc.stdout?.on('data', (chunk) => {
            outputChannel.append(chunk.toString());
        });
        proc.stderr?.on('data', (chunk) => {
            outputChannel.append(chunk.toString());
        });
        proc.on('error', (err) => {
            outputChannel.appendLine(`[ERROR] Failed to start emulator: ${err.message}`);
            vscode.window.showErrorMessage(`Failed to start emulator: ${err.message}`);
            this.cleanupProcess();
        });
        proc.on('close', (code) => {
            outputChannel.appendLine(`[INFO] Emulator exited with code ${code ?? 'null'}.`);
            this.cleanupProcess();
        });
    }
    cleanupProcess() {
        this.emulatorProcess = undefined;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
    }
    /** Resolve a workspace-relative path to an absolute path. */
    resolvePath(p) {
        if (path.isAbsolute(p)) {
            return p;
        }
        return this.workspaceRoot ? path.join(this.workspaceRoot, p) : p;
    }
    // ── Disassembly ───────────────────────────────────────────────────────────
    async handleDisassemble(hexCode) {
        const socketClient = (0, extension_1.getSocketClient)();
        if (!socketClient || !socketClient.isConnected()) {
            this.sendDisasmResult(false, '', 'Not connected to C# server. Use "Connect to Server" command first.');
            return;
        }
        try {
            const results = await socketClient.disassemble(hexCode);
            const asmLines = [];
            for (const result of results) {
                if (result.Success) {
                    const line = result.Comment
                        ? `${result.Hex}  ${result.Assembly}  ${result.Comment}`
                        : `${result.Hex}  ${result.Assembly}`;
                    asmLines.push(line);
                }
                else {
                    asmLines.push(`${result.Hex}  ERROR: ${result.Error}`);
                }
            }
            this.sendDisasmResult(true, asmLines.join('\n'), '');
        }
        catch (error) {
            this.sendDisasmResult(false, '', error.message || 'Disassembly failed');
        }
    }
    sendDisasmResult(success, data, error) {
        if (this.webview) {
            this.webview.postMessage({
                command: 'disassembleResult',
                success,
                data,
                error
            });
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
/***/ ((module) => {

module.exports = require("child_process");

/***/ }),
/* 10 */
/***/ ((__unused_webpack_module, exports, __webpack_require__) => {


/**
 * Template for the Disassembly Panel
 */
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getDisasmPanelHtml = getDisasmPanelHtml;
const disasmPanelStyles_1 = __webpack_require__(11);
function getDisasmPanelHtml(data, nonce) {
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">

${(0, disasmPanelStyles_1.getDisasmPanelStyles)()}

</head>
<body>

<h3>MIPS Disassembler</h3>
<div class="container">

    ${getEmulatorControls(data)}

    <hr class="section-divider">

    <label for="hexInput">Hex Code (one per line or space-separated):</label>
    <textarea id="hexInput" rows="10" placeholder="0x02114020&#10;0x21090064&#10;0x11090010"></textarea>

    <button id="disassembleBtn">Disassemble</button>

    <label for="asmOutput">Assembly Output:</label>
    <textarea id="asmOutput" rows="10" readonly></textarea>

    <div id="status" class="status"></div>
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
</div>`;
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

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulatorPath: e.target.value });
};

document.getElementById('biosSelect').onchange = (e) => {
    vscode.postMessage({ command: 'biosChanged', biosPath: e.target.value });
};

document.getElementById('gameSelect').onchange = (e) => {
    vscode.postMessage({ command: 'gameChanged', gamePath: e.target.value });
};

document.getElementById('disassembleBtn').onclick = () => {
    const hexInput = document.getElementById('hexInput').value;
    const statusDiv = document.getElementById('status');

    statusDiv.textContent = 'Disassembling...';
    statusDiv.className = 'status';

    vscode.postMessage({
        command: 'disassemble',
        data: hexInput
    });
};

// Listen for messages from the extension
window.addEventListener('message', event => {
    const message = event.data;

    switch (message.command) {
        case 'disassembleResult':
            const asmOutput = document.getElementById('asmOutput');
            const statusDiv = document.getElementById('status');

            if (message.success) {
                asmOutput.value = message.data;
                statusDiv.textContent = 'Disassembly complete!';
                statusDiv.className = 'status success';
            } else {
                statusDiv.textContent = 'Error: ' + message.error;
                statusDiv.className = 'status error';
            }
            break;

        case 'connectionStatus':
            const btn = document.getElementById('connectBtn');
            btn.textContent = message.connected ? 'Disconnect' : 'Connect';
            break;
    }
});
</script>`;
}


/***/ }),
/* 11 */
/***/ ((__unused_webpack_module, exports) => {


/**
 * Styles for the Disassembly Panel
 */
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getDisasmPanelStyles = getDisasmPanelStyles;
function getDisasmPanelStyles() {
    return `<style>
body {
    padding: 10px;
    font-family: var(--vscode-editor-font-family);
    color: var(--vscode-editor-foreground);
    background: var(--vscode-editor-background);
}

.container {
    display: flex;
    flex-direction: column;
    gap: 10px;
}

label {
    font-weight: bold;
    margin-top: 10px;
}

.row {
    display: flex;
    gap: 8px;
    margin-bottom: 4px;
}

.row select,
.row button {
    flex: 1;
}

select, textarea, button {
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

select {
    width: 100%;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border);
    padding: 4px;
}

textarea {
    width: 100%;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border);
    padding: 8px;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
    resize: vertical;
}

button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 8px 16px;
    cursor: pointer;
    font-size: 14px;
}

button:hover {
    background: var(--vscode-button-hoverBackground);
}

.section-divider {
    border: none;
    border-top: 1px solid var(--vscode-input-border);
    margin: 8px 0;
}

.status {
    padding: 8px;
    margin-top: 10px;
    border-radius: 4px;
    font-size: 12px;
}

.status.success {
    background: var(--vscode-testing-iconPassed);
    color: white;
}

.status.error {
    background: var(--vscode-testing-iconFailed);
    color: white;
}
</style>`;
}


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
const child_process_1 = __webpack_require__(9);
const helpers_1 = __webpack_require__(4);
const asmPanelTemplate_1 = __webpack_require__(13);
const extension_1 = __webpack_require__(0);
let outputChannel;
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
        webviewView.webview.html = (0, asmPanelTemplate_1.getAsmPanelHtml)(templateData, (0, helpers_1.getNonce)());
        // Ensure output channel exists
        if (!outputChannel) {
            outputChannel = vscode.window.createOutputChannel('Assembler');
        }
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async (message) => {
            switch (message.command) {
                case 'connect':
                    vscode.window.showInformationMessage('Connect clicked (emulator hookup later)');
                    break;
                case 'assemble':
                    await this.handleAssemble(message.data);
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
    runCds(option) {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showErrorMessage('No active file.');
            return;
        }
        const sourcePath = editor.document.uri.fsPath;
        const exePath = 'C:\\Users\\tholu\\source\\repos\\CodeDesignerAI\\CodeDesigner.ConsoleApp\\bin\\Debug\\net9.0\\CodeDesigner.ConsoleApp.exe';
        const command = `"${exePath}" CDS ${option} -f "${sourcePath}"`;
        outputChannel.clear();
        outputChannel.show(true);
        outputChannel.appendLine(`> ${command}\n`);
        //outputChannel.appendLine(this.getWorkspaceRoot() ?? 'No workspace root');
        (0, child_process_1.exec)(command, (error, stdout, stderr) => {
            if (stderr) {
                outputChannel.appendLine(stderr);
            }
            if (error) {
                outputChannel.appendLine(`ERROR: ${error.message}`);
                return;
            }
            try {
                const result = JSON.parse(stdout);
                // Show debug messages
                result.debugMessages?.forEach(msg => outputChannel.appendLine(msg));
                // Push cheatCodes back into the panel textarea
                if (result.cheatCodes && this.webview) {
                    this.webview.postMessage({
                        command: 'setCode',
                        code: result.cheatCodes
                    });
                }
            }
            catch (e) {
                outputChannel.appendLine('Failed to parse compiler JSON output');
                outputChannel.appendLine(stdout);
            }
        });
    }
}
exports.AsmPanelProvider = AsmPanelProvider;


/***/ }),
/* 13 */
/***/ ((__unused_webpack_module, exports, __webpack_require__) => {


/**
 * Template for the Assembler Panel
 * Separated from component logic for better organization (Angular-style)
 */
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getAsmPanelHtml = getAsmPanelHtml;
const asmPanelStyles_1 = __webpack_require__(14);
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

${(0, asmPanelStyles_1.getAsmPanelStyles)()}

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
/***/ ((__unused_webpack_module, exports) => {


/**
 * Styles for the Assembler Panel
 */
Object.defineProperty(exports, "__esModule", ({ value: true }));
exports.getAsmPanelStyles = getAsmPanelStyles;
function getAsmPanelStyles() {
    return `<style>
body {
    margin: 0;
    padding: 10px;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
    background: var(--vscode-editor-background);
    color: var(--vscode-editor-foreground);
}

select, textarea, button {
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

select, textarea {
    width: 100%;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border);
    margin-bottom: 8px;
    padding: 4px;
}

textarea {
    height: 150px;
    resize: vertical;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

label {
    display: block;
    font-weight: bold;
    margin-top: 10px;
    margin-bottom: 4px;
}

.textarea-container {
    display: flex;
    flex-direction: column;
}

button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 6px 12px;
    cursor: pointer;
}

button:hover {
    background: var(--vscode-button-hoverBackground);
}

button:disabled {
    opacity: 0.5;
    cursor: default;
}

.row {
    display: flex;
    gap: 8px;
    margin-bottom: 8px;
}

.status {
    padding: 8px;
    margin-top: 10px;
    border-radius: 4px;
    font-size: 12px;
}

.status.success {
    background: var(--vscode-testing-iconPassed);
    color: white;
}

.status.error {
    background: var(--vscode-testing-iconFailed);
    color: white;
}
</style>`;
}


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
    sendCommand(command) {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }
            let responseReceived = false;
            const timeoutMs = 10000; // 10 second timeout
            // Set up one-time response handler
            const responseHandler = (message) => {
                if (responseReceived)
                    return;
                responseReceived = true;
                clearTimeout(timeout);
                // Remove the handler
                const index = this.messageHandlers.indexOf(responseHandler);
                if (index > -1) {
                    this.messageHandlers.splice(index, 1);
                }
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
            // Add temporary handler
            this.messageHandlers.push(responseHandler);
            // Set timeout
            const timeout = setTimeout(() => {
                if (!responseReceived) {
                    responseReceived = true;
                    // Remove the handler
                    const index = this.messageHandlers.indexOf(responseHandler);
                    if (index > -1) {
                        this.messageHandlers.splice(index, 1);
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
                    // Remove the handler
                    const index = this.messageHandlers.indexOf(responseHandler);
                    if (index > -1) {
                        this.messageHandlers.splice(index, 1);
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