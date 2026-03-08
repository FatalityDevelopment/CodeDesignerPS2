import * as vscode from "vscode";
import * as fs from "fs";
import { getNonce } from "../../helpers";
import { getDisasmEditorHtml, DisasmEditorData } from "./disasmEditorTemplate";
import { SocketClient, LabelEntry, StringEntry, FunctionEntry } from "../../socketClient";
import { getOutputChannel } from "../../extension";

/**
 * Allowed searchable fields in a disassembled instruction row
 */
type SearchField = 'addr' | 'bytes' | 'label' | 'op' | 'args' | 'comment';

interface SearchMatch {
  instructionIndex: number;
  address: number;
  field: SearchField;
  start: number;
  length: number;
}

/**
 * Structure for disassembled instruction row
 */
interface DisassembledRow {
  addr: string;
  bytes: string;
  label: string;
  op: string;
  args: string;
  comment: string;
}

export class DisasmEditorProvider implements vscode.CustomReadonlyEditorProvider {
  private static readonly HEX_VIEW_SIZE = 0x30;
  private static readonly INSTRUCTION_SIZE = 4;
  private static readonly BASE_ADDRESS = 0x00000000;
  private static readonly PAGE_SIZE = 1000;
  private static readonly BUFFER_PAGES = 2;
  private socketClient: SocketClient | null;
  /** All currently-open live memory panels. Used to broadcast pause/resume state. */
  private _livePanels: vscode.WebviewPanel[] = [];
  /** Metadata for each live panel: label, address range. Used for reuse and address lookup. */
  private _livePanelInfo: Array<{
    panel: vscode.WebviewPanel;
    label: string;
    region: string;
    baseAddress: number;
    totalBytes: number;
    physBaseAddress: number;  // pre-computed physical base for fast address lookup
  }> = [];

  constructor(socketClient: SocketClient | null) {
    this.socketClient = socketClient;
  }

  /**
   * Format bytes for display (no spaces, big-endian)
   */
  private formatBytesForDisplay(buffer: Buffer, offset: number, length: number): string {
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
  private async disassembleWithBackend(startAddr: number, buffer: Buffer): Promise<DisassembledRow[]> {
    const rows: DisassembledRow[] = [];
    
    // If no socket client or not connected, return empty array
    if (!this.socketClient || !this.socketClient.isConnected()) {
      const msg = "Socket client not available or not connected, cannot disassemble";
      console.warn(msg);
      const outputChannel = getOutputChannel();
      if (outputChannel) {
        outputChannel.appendLine(`[WARN] ${msg}`);
      }
      return rows;
    }

    try {
      // Convert buffer to hex strings for each 4-byte instruction
      const hexInstructions: string[] = [];
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
            comment: result.Comment || "",  // Use comment from backend analyzer
          });
        } else if (result.Error) {
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
    } catch (error) {
      const errorMsg = error instanceof Error ? error.message : String(error);
      console.error(`[DISASM] Failed to disassemble with backend: ${errorMsg}`);
      vscode.window.showErrorMessage(`Disassembly failed: ${errorMsg}`);
      // Return empty array on error
    }

    return rows;
  }

  openCustomDocument(uri: vscode.Uri): vscode.CustomDocument {
    return { uri, dispose: () => {} };
  }

  async resolveCustomEditor(
    document: vscode.CustomDocument,
    webviewPanel: vscode.WebviewPanel
  ): Promise<void> {
    // Setup webview options
    webviewPanel.webview.options = {
      enableScripts: true
    };

    // Set HTML from template
    const templateData: DisasmEditorData = {};
    webviewPanel.webview.html = getDisasmEditorHtml(templateData, getNonce());
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
      const totalInstructions = Math.floor(
        fileData.length / DisasmEditorProvider.INSTRUCTION_SIZE
      );

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
      this.sendHexView(
        webviewPanel,
        fileData,
        DisasmEditorProvider.BASE_ADDRESS,
        DisasmEditorProvider.BASE_ADDRESS,
        DisasmEditorProvider.INSTRUCTION_SIZE
      );

      webviewPanel.webview.onDidReceiveMessage(async (message) => {
        console.log(`[DISASM] Received message from webview:`, message.type);
        if (message.type === "updateHex") {
          this.sendHexView(
            webviewPanel,
            fileData,
            DisasmEditorProvider.BASE_ADDRESS,
            message.address,
            message.length || DisasmEditorProvider.INSTRUCTION_SIZE
          );
        } else if (message.type === "requestPage") {
          await this.sendDisasmPage(webviewPanel, fileData, message.startIndex);
        } else if (message.type === "search") {
          await this.performSearch(webviewPanel, fileData, message.query, message.mode);
        } else if (message.type === "getSymbols") {
          await this.handleGetSymbols(webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
        } else if (message.type === "addLabel") {
          await this.handleAddLabel(webviewPanel, message.address, message.labelName, message.comment);
        } else if (message.type === "deleteLabel") {
          await this.handleDeleteLabel(webviewPanel, message.address);
        }
      });
    });
  }

    /**
     * Open a live memory view panel (no backing file — pages fetched via socket).
     * Called once per region after connecting to the embedded emulator server.
     * If a panel for this label already exists (e.g. reconnect), it is revealed
     * instead of creating a duplicate window.
     */
    async openLiveMemoryView(
        region: string,
        baseAddress: number,
        totalBytes: number,
        label: string
    ): Promise<void> {
        if (!this.socketClient || !this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator. Connect first.');
            return;
        }

        // Reuse an already-open panel with the same label (e.g. after reconnect)
        const existingInfo = this._livePanelInfo.find(i => i.label === label);
        if (existingInfo) {
            existingInfo.panel.reveal(vscode.ViewColumn.One, true);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'disasm.liveMemory',
            label,
            vscode.ViewColumn.One,
            { enableScripts: true, retainContextWhenHidden: true }
        );

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

        panel.webview.html = getDisasmEditorHtml({} as DisasmEditorData, getNonce());

        const totalInstructions = Math.floor(totalBytes / DisasmEditorProvider.INSTRUCTION_SIZE);

        panel.webview.postMessage({
            type: 'init',
            totalInstructions,
            pageSize: DisasmEditorProvider.PAGE_SIZE,
            baseAddress,
            isLive: true,
        });

        // Send first page and hex view
        await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, 0);
        await this.sendLiveHexView(panel, region, baseAddress, baseAddress,
            DisasmEditorProvider.INSTRUCTION_SIZE);

        panel.webview.onDidReceiveMessage(async (message) => {
            if (message.type === 'requestPage') {
                await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, message.startIndex);
            } else if (message.type === 'updateHex') {
                await this.sendLiveHexView(panel, region, baseAddress, message.address,
                    message.length || DisasmEditorProvider.INSTRUCTION_SIZE);
            } else if (message.type === 'pause') {
                try {
                    await this.socketClient!.pause();
                    // Immediately reflect the paused state in every live window so all
                    // panels show "PAUSED" without waiting for their next status poll.
                    this._broadcastStatus(true);
                } catch (err: any) {
                    const msg = err?.message ?? String(err);
                    console.error(`[LIVE] Failed to pause emulator: ${msg}`);
                    vscode.window.showWarningMessage(`Could not pause emulator: ${msg}`);
                }
            } else if (message.type === 'resume') {
                try {
                    await this.socketClient!.resume();
                    // Immediately reflect the resumed state in every live window.
                    this._broadcastStatus(false);
                } catch (err: any) {
                    const msg = err?.message ?? String(err);
                    console.error(`[LIVE] Failed to resume emulator: ${msg}`);
                    vscode.window.showWarningMessage(`Could not resume emulator: ${msg}`);
                }
            } else if (message.type === 'setBreakpoint') {
                try {
                    await this.socketClient!.setBreakpoint(message.address);
                } catch (err: any) {
                    console.error(`[LIVE] Failed to set breakpoint: ${err?.message ?? err}`);
                }
            } else if (message.type === 'clearBreakpoint') {
                try {
                    await this.socketClient!.clearBreakpoint(message.address);
                } catch (err: any) {
                    console.error(`[LIVE] Failed to clear breakpoint: ${err?.message ?? err}`);
                }
            } else if (message.type === 'clearAllBreakpoints') {
                try {
                    await this.socketClient!.clearAllBreakpoints();
                } catch (err: any) {
                    console.error(`[LIVE] Failed to clear all breakpoints: ${err?.message ?? err}`);
                }
            } else if (message.type === 'getStatus') {
                try {
                    const status = await this.socketClient!.getPC();
                    const statusMsg: any = { type: 'status', ...status };
                    // When paused, also fetch registers for the debug panel
                    if (status.paused) {
                        try {
                            const regs = await this.socketClient!.getRegisters();
                            statusMsg.registers = regs;
                            // Fetch stack memory (128 bytes from $sp = gpr[29])
                            const sp = regs.gpr[29];
                            if (sp !== undefined) {
                                try {
                                    const stackBuf = await this.socketClient!.readMemory(sp, 128, 'ee');
                                    statusMsg.stackBytes = Array.from(stackBuf);
                                    statusMsg.stackBase = sp;
                                } catch { /* ignore stack read errors */ }
                            }
                        } catch { /* ignore register fetch errors */ }

                        // When a breakpoint was hit, bring the correct panel to front
                        // and broadcast the full status to every live window.
                        const hitAddr = status.breakpointHitAddress ?? status.pc;
                        this._revealPanelForAddress(hitAddr);
                        this._broadcastFullStatus(statusMsg);
                    } else {
                        panel.webview.postMessage(statusMsg);
                    }
                } catch (err: any) {
                    console.error(`[LIVE] Failed to get status: ${err?.message ?? err}`);
                }
            } else if (message.type === 'search') {
                // Full-region live search is expensive; return empty for now
                panel.webview.postMessage({ type: 'searchResults', matches: [] });
            } else if (message.type === 'getSymbols') {
                // Live mode: labels from backend; no fileData so strings/functions empty
                await this.handleGetSymbols(panel, undefined, baseAddress);
            } else if (message.type === 'addLabel') {
                await this.handleAddLabel(panel, message.address, message.labelName, message.comment);
            } else if (message.type === 'deleteLabel') {
                await this.handleDeleteLabel(panel, message.address);
            }
        });
    }

    /**
     * Reset all live panels: clears their page caches and shows zeroed-out rows.
     * Called by DisasmPanelProvider after triggering an emulator restart.
     */
    public resetLivePanels(): void {
        for (const p of this._livePanels) {
            p.webview.postMessage({ type: 'reset' });
        }
    }

    /**
     * Send a minimal `status` message to all registered live panels.
     * Called immediately after a user-initiated pause or resume so every window
     * updates its live-bar indicator without waiting for the next status-poll cycle.
     */
    private _broadcastStatus(paused: boolean): void {
        const msg = { type: 'status', paused };
        for (const p of this._livePanels) {
            p.webview.postMessage(msg);
        }
    }

    /**
     * Broadcast a full status message (including PC, registers, etc.) to every
     * live panel.  Called when a breakpoint is hit so all windows update at once.
     */
    private _broadcastFullStatus(statusMsg: object): void {
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
    private _revealPanelForAddress(addr: number): void {
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

    private async sendLiveDisasmPage(
        panel: vscode.WebviewPanel,
        region: string,
        baseAddress: number,
        totalBytes: number,
        startIndex: number
    ): Promise<void> {
        if (!this.socketClient || !this.socketClient.isConnected()) { return; }

        const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
        const maxBytes    = DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE;
        const readLength  = Math.min(totalBytes - startOffset, maxBytes);
        if (readLength <= 0) { return; }

        const startAddr = baseAddress + startOffset;
        try {
            const buffer = await this.socketClient.readMemory(startAddr, readLength, region);
            const rows   = await this.disassembleWithBackend(startAddr, buffer);
            panel.webview.postMessage({ type: 'disasmPage', rows, startIndex });
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            console.error(`[LIVE] sendLiveDisasmPage failed at 0x${startAddr.toString(16)}: ${msg}`);
        }
    }

    private async sendLiveHexView(
        panel: vscode.WebviewPanel,
        region: string,
        baseAddress: number,
        highlightAddr: number,
        highlightLength: number
    ): Promise<void> {
        if (!this.socketClient || !this.socketClient.isConnected()) { return; }

        const alignedAddr = Math.max(baseAddress, (highlightAddr & ~0xf) - 0x10);
        try {
            const buffer = await this.socketClient.readMemory(
                alignedAddr, DisasmEditorProvider.HEX_VIEW_SIZE, region);
            panel.webview.postMessage({
                type: 'hexview',
                data: Array.from(buffer),
                offset: alignedAddr,
                highlight_addr:   highlightAddr,
                highlight_length: highlightLength,
            });
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            console.error(`[LIVE] sendLiveHexView failed at 0x${alignedAddr.toString(16)}: ${msg}`);
        }
    }

    private async performSearch(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    query: string,
    mode: 'text' | 'bytes' | 'string' | 'regex'
  ): Promise<void> {
    const matches: SearchMatch[] = [];

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
    } else if (mode === 'string') {
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
    } else {
      const totalInstructions = Math.floor(
        fileData.length / DisasmEditorProvider.INSTRUCTION_SIZE
      );

      for (let instrIndex = 0; instrIndex < totalInstructions; instrIndex++) {
        const offset = instrIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
        const slice = fileData.slice(offset, offset + DisasmEditorProvider.INSTRUCTION_SIZE);
        if (slice.length < DisasmEditorProvider.INSTRUCTION_SIZE) break;

        const address = DisasmEditorProvider.BASE_ADDRESS + offset;
        const rows = await this.disassembleWithBackend(address, slice);

        if (rows.length === 0) continue;

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

  private parseBytePattern(pattern: string): number[] | null {
    const cleaned = pattern.replace(/0x/gi, '').replace(/[,\s]+/g, ' ').trim();
    if (!cleaned) return null;

    const bytes = cleaned.split(/\s+/);
    const result: number[] = [];

    for (const byte of bytes) {
      if (!/^[0-9a-f]{1,2}$/i.test(byte)) return null;
      result.push(parseInt(byte, 16));
    }

    return result.length ? result : null;
  }

  private searchInRow(
    row: any,
    query: string,
    mode: 'text' | 'regex'
  ): Array<{ field: SearchField; start: number; length: number }> {
    const matches: Array<{ field: SearchField; start: number; length: number }> = [];

    const fields: Array<{ text: string; field: SearchField }> = [
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
    } else {
      try {
        const regex = new RegExp(query, 'gi');

        for (const { text, field } of fields) {
          let match: RegExpExecArray | null;
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
      } catch {
        return [];
      }
    }

    return matches;
  }

  /**
   * Collect labels, strings, and functions from the backend and post them
   * back to the webview as a single `symbols` message.
   * For live panels that have no backing fileData, pass `undefined`; in that
   * case strings and functions are returned empty.
   */
  private async handleGetSymbols(
    panel: vscode.WebviewPanel,
    fileData: Buffer | undefined,
    baseAddress: number
  ): Promise<void> {
    let labels:    LabelEntry[]    = [];
    let strings:   StringEntry[]   = [];
    let functions: FunctionEntry[] = [];

    if (this.socketClient?.isConnected()) {
      try { labels = await this.socketClient.getLabels(); } catch { /* ignore */ }

      if (fileData && fileData.length > 0) {
        try { strings   = await this.socketClient.getStrings(fileData, baseAddress); }   catch { /* ignore */ }
        try { functions = await this.socketClient.getFunctions(fileData, baseAddress); } catch { /* ignore */ }
      }
    }

    panel.webview.postMessage({ type: 'symbols', labels, strings, functions });
  }

  /**
   * Add (or update) a label in the backend database and send back a fresh
   * label list so the symbols panel refreshes instantly.
   */
  private async handleAddLabel(
    panel: vscode.WebviewPanel,
    address: number,
    labelName: string,
    comment: string | undefined
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      await this.socketClient.addLabel(address, labelName, comment);
      const labels = await this.socketClient.getLabels();
      panel.webview.postMessage({ type: 'labelsUpdated', labels });
    } catch (err: any) {
      console.error(`[SYMBOLS] AddLabel failed: ${err?.message ?? err}`);
    }
  }

  /**
   * Delete a label in the backend database and send back a fresh label list.
   */
  private async handleDeleteLabel(
    panel: vscode.WebviewPanel,
    address: number
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      await this.socketClient.deleteLabel(address);
      const labels = await this.socketClient.getLabels();
      panel.webview.postMessage({ type: 'labelsUpdated', labels });
    } catch (err: any) {
      console.error(`[SYMBOLS] DeleteLabel failed: ${err?.message ?? err}`);
    }
  }

  private async sendDisasmPage(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    startIndex: number
  ): Promise<void> {
    console.log(`[DISASM] sendDisasmPage called: startIndex=${startIndex}`);
    const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
    const endOffset = Math.min(
      fileData.length,
      startOffset + DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE
    );

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

  private sendHexView(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    baseAddr: number,
    highlightAddr: number,
    highlightLength: number
  ): void {
    const alignedAddr = Math.max(baseAddr, (highlightAddr & ~0xf) - 0x10);
    const fileOffset = alignedAddr - baseAddr;
    const endOffset = Math.min(
      fileData.length,
      fileOffset + DisasmEditorProvider.HEX_VIEW_SIZE
    );

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