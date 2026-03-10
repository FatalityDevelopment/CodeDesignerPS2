import * as vscode from "vscode";
import * as fs from "fs";
import { SocketClient, LabelEntry, StringEntry, FunctionEntry, SearchMatch } from "../../socketClient";
import { getOutputChannel } from "../../extension";
import { getNonce, getWebviewErrorHandlerScript } from "../../utils/nonce";

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

  /**
   * All breakpoints the user has toggled across all live panels.  Persisted here
   * in the extension process so they can be reliably re-sent to the C# server on
   * every connect/reconnect/restart — without depending on the Angular webview
   * being bootstrapped or having processed an 'init' message first.
   */
  private _knownBreakpoints: Set<number> = new Set();

  /**
   * Optional callback invoked when the emulator's pause state changes (e.g. breakpoint hit).
   * Set by DisasmPanelProvider so it can update the sidebar status bar.
   */
  public onPauseStateChange?: (paused: boolean) => void;

  constructor(socketClient: SocketClient | null, private readonly _extensionUri: vscode.Uri) {
    this.socketClient = socketClient;
  }

  /**
   * Called by DisasmPanelProvider to propagate sidebar-initiated pause/resume
   * to all editor panels' live-bar state.
   */
  public notifyPauseState(paused: boolean): void {
    this._broadcastStatus(paused);
  }

  /**
   * Execute one EE instruction via the STEP socket command, fetch the updated
   * PC + register state, broadcast it to every live panel, and notify the
   * sidebar of the continued paused state.  Called by DisasmPanelProvider
   * (sidebar Step button) and by the live-panel message handler (toolbar
   * Step button).
   */
  public async stepAndBroadcast(): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    let stepResult: { paused: boolean; pc: number; breakpointHitAddress?: number; instructionAnalysis?: Record<string, string> } | undefined;
    try {
      stepResult = await this.socketClient.step();
    } catch (err: any) {
      getOutputChannel().appendLine(`[STEP] Error: ${err?.message ?? err}`);
      return;
    }
    // Fetch and broadcast the full post-step status (same path as getStatus).
    try {
      const status = await this.socketClient!.getPC();
      const statusMsg: any = { type: 'status', ...status };
      // Attach any live analysis comments produced for the stepped instruction.
      if (stepResult?.instructionAnalysis) {
        statusMsg.instructionAnalysis = stepResult.instructionAnalysis;
      }
      if (status.paused) {
        try {
          const regs = await this.socketClient!.getRegisters();
          statusMsg.registers = regs;
          const sp = regs.gpr[29];
          if (sp !== undefined) {
            try {
              const stackBuf = await this.socketClient!.readMemory(sp, 128, 'ee');
              statusMsg.stackBytes = Array.from(stackBuf);
              statusMsg.stackBase  = sp;
            } catch { /* ignore stack read errors */ }
          }
        } catch { /* ignore register fetch errors */ }
        const hitAddr = status.breakpointHitAddress ?? status.pc;
        this._revealPanelForAddress(hitAddr);
      }
      this._broadcastFullStatus(statusMsg);
      // Always notify the sidebar so it stays in sync (step keeps emulator paused).
      this.onPauseStateChange?.(status.paused);
    } catch (err: any) {
      getOutputChannel().appendLine(`[STEP] Status error: ${err?.message ?? err}`);
    }
  }

  /**
   * Returns a snapshot of all breakpoints the user has set across all live
   * panels.  Used by DisasmPanelProvider to re-register BPs with the C# server
   * on every connect/restart — before sending RESUME — so no timing dependency
   * on Angular bootstrap.
   */
  public getKnownBreakpoints(): ReadonlySet<number> {
    return this._knownBreakpoints;
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

  private getHtml(webview: vscode.Webview): string {
    const scriptUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'disasm-editor.js')
    );
    const nonce = getNonce();
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-disasm-editor><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading...</p></app-disasm-editor>
<script src="${scriptUri}"></script>
</body>
</html>`;
  }

  async resolveCustomEditor(
    document: vscode.CustomDocument,
    webviewPanel: vscode.WebviewPanel
  ): Promise<void> {
    webviewPanel.webview.options = {
      enableScripts: true,
      localResourceRoots: [this._extensionUri]
    };

    // Track both async preconditions: file loaded + webview ready.
    // init is sent only when both are satisfied (order-independent).
    let fileData: Buffer | null = null;
    let readyReceived = false;

    const sendInit = async (data: Buffer) => {
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
      this.sendHexView(webviewPanel, data,
        DisasmEditorProvider.BASE_ADDRESS,
        DisasmEditorProvider.BASE_ADDRESS,
        DisasmEditorProvider.INSTRUCTION_SIZE);
    };

    // Register the message handler BEFORE setting html so that any 'ready'
    // message from the webview is never missed.
    webviewPanel.webview.onDidReceiveMessage(async (message) => {
      console.log(`[DISASM] Received message from webview:`, message.type);
      if (message.type === 'ready') {
        // Webview has bootstrapped and its listener is registered.
        readyReceived = true;
        if (fileData) { await sendInit(fileData); }
        return;
      }
      if (message.type === 'bootstrapError') {
        getOutputChannel().appendLine(
          `[ERROR] Disasm editor Angular bootstrap failed: ${message.error ?? '(no details)'}`
        );
        return;
      }
      if (!fileData) { return; }
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
        await this.handleSearch(webviewPanel, message.query, message.mode,
          DisasmEditorProvider.BASE_ADDRESS, fileData.length, undefined, fileData);
      } else if (message.type === "scanStrings") {
        await this.handleFileScan('strings', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
      } else if (message.type === "scanFunctions") {
        await this.handleFileScan('functions', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
      } else if (message.type === "getSymbols") {
        await this.handleGetSymbols(webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
      } else if (message.type === "addLabel") {
        await this.handleAddLabel(webviewPanel, message.address, message.labelName, message.comment);
      } else if (message.type === "deleteLabel") {
        await this.handleDeleteLabel(webviewPanel, message.address);
      } else if (message.type === "editLabel") {
        await this.handleEditLabel(webviewPanel, message.address, message.addressHex,
          message.dataBytes, message.currentLabel ?? '', message.currentComment ?? '');
      } else if (message.type === "saveLabel") {
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
      if (readyReceived) { await sendInit(fileData); }
    });
  }

    /**
     * Open a live memory view panel (no backing file — pages fetched via socket).
     * Called once per region after connecting to the embedded emulator server.
     * If a panel for this label already exists (e.g. reconnect), it is revealed
     * and re-initialized with fresh data when connected.
     */
    async openLiveMemoryView(
        region: string,
        baseAddress: number,
        totalBytes: number,
        label: string
    ): Promise<void> {
        const connected = this.socketClient?.isConnected() ?? false;

        // Reuse an already-open panel with the same label (e.g. after reconnect)
        const existingInfo = this._livePanelInfo.find(i => i.label === label);
        if (existingInfo) {
            existingInfo.panel.reveal(vscode.ViewColumn.One, true);
            // Update the stored base address and physical base so that
            // _revealPanelForAddress uses the correct range after an address change
            // (e.g. BIOS ROM shown at KSEG0 0x9FC00000 instead of physical 0x1FC00000).
            existingInfo.baseAddress = baseAddress;
            existingInfo.physBaseAddress = (baseAddress >= 0x80000000 && baseAddress <= 0xBFFFFFFF)
                ? (baseAddress & 0x1FFFFFFF) >>> 0
                : baseAddress >>> 0;
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
                await this.sendLiveHexView(existingInfo.panel, region, baseAddress, baseAddress,
                    DisasmEditorProvider.INSTRUCTION_SIZE);
            }
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'disasm.liveMemory',
            label,
            vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this._extensionUri]
            }
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
                    await this.sendLiveHexView(panel, region, baseAddress, baseAddress,
                        DisasmEditorProvider.INSTRUCTION_SIZE);
                }
                return;
            }
            if (message.type === 'bootstrapError') {
                getOutputChannel().appendLine(
                    `[ERROR] Live memory view Angular bootstrap failed (${label}): ${message.error ?? '(no details)'}`
                );
                return;
            }
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
                this._knownBreakpoints.add(message.address);
                try {
                    await this.socketClient!.setBreakpoint(message.address);
                } catch (err: any) {
                    console.error(`[LIVE] Failed to set breakpoint: ${err?.message ?? err}`);
                }
            } else if (message.type === 'clearBreakpoint') {
                this._knownBreakpoints.delete(message.address);
                try {
                    await this.socketClient!.clearBreakpoint(message.address);
                } catch (err: any) {
                    console.error(`[LIVE] Failed to clear breakpoint: ${err?.message ?? err}`);
                }
            } else if (message.type === 'clearAllBreakpoints') {
                this._knownBreakpoints.clear();
                try {
                    await this.socketClient!.clearAllBreakpoints();
                } catch (err: any) {
                    console.error(`[LIVE] Failed to clear all breakpoints: ${err?.message ?? err}`);
                }
            } else if (message.type === 'step') {
                await this.stepAndBroadcast();
            } else if (message.type === 'pause') {
                if (this.socketClient?.isConnected()) {
                    try {
                        await this.socketClient!.pause();
                        this._broadcastStatus(true);
                        this.onPauseStateChange?.(true);
                    } catch (err: any) {
                        console.error(`[LIVE] Pause failed: ${err?.message ?? err}`);
                    }
                }
            } else if (message.type === 'resume') {
                if (this.socketClient?.isConnected()) {
                    try {
                        await this.socketClient!.resume();
                        this._broadcastStatus(false);
                        this.onPauseStateChange?.(false);
                    } catch (err: any) {
                        console.error(`[LIVE] Resume failed: ${err?.message ?? err}`);
                    }
                }
            } else if (message.type === 'getStatus') {
                if (!this.socketClient?.isConnected()) { return; }
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
                        // Notify the sidebar so it flips to PAUSED immediately.
                        this.onPauseStateChange?.(true);
                    } else {
                        panel.webview.postMessage(statusMsg);
                    }
                } catch (err: any) {
                    console.error(`[LIVE] Failed to get status: ${err?.message ?? err}`);
                }
            } else if (message.type === 'search') {
                await this.handleSearch(panel, message.query, message.mode,
                    baseAddress, totalBytes, region);
            } else if (message.type === 'scanStrings') {
                await this.handleLiveScan('strings', panel, region, baseAddress, totalBytes);
            } else if (message.type === 'scanFunctions') {
                await this.handleLiveScan('functions', panel, region, baseAddress, totalBytes);
            } else if (message.type === 'getSymbols') {
                // Live mode: labels from backend; strings/functions are loaded on-demand via scan buttons
                await this.handleGetSymbols(panel, undefined, baseAddress);
            } else if (message.type === 'addLabel') {
                await this.handleAddLabel(panel, message.address, message.labelName, message.comment);
            } else if (message.type === 'deleteLabel') {
                await this.handleDeleteLabel(panel, message.address);
            } else if (message.type === 'editLabel') {
                await this.handleEditLabel(panel, message.address, message.addressHex,
                    message.dataBytes, message.currentLabel ?? '', message.currentComment ?? '');
            } else if (message.type === 'getThreads') {
                if (!this.socketClient?.isConnected()) {
                    panel.webview.postMessage({ type: 'threads', threads: [] });
                    return;
                }
                try {
                    const threads = await this.socketClient!.getThreads();
                    panel.webview.postMessage({ type: 'threads', threads });
                } catch (err: any) {
                    getOutputChannel().appendLine(`[LIVE] Failed to get threads: ${err?.message ?? err}`);
                    panel.webview.postMessage({ type: 'threads', threads: [] });
                }
            } else if (message.type === 'gotoRegion') {
                // The webview could not find the address in its own region.
                // Find which live panel owns the address and instruct it to navigate.
                const addr = message.address as number;
                const physAddr = (addr >= 0x80000000 && addr <= 0xBFFFFFFF)
                    ? (addr & 0x1FFFFFFF) >>> 0
                    : addr >>> 0;
                // Prefer a panel in the same region first.  This disambiguates the
                // overlap between EE Main RAM and IOP RAM, which both start at
                // physical address 0x0: a native IOP address like 0x0000ae94 sent
                // from the IOP panel should land in the IOP panel, not EE Main RAM.
                const target =
                    this._livePanelInfo.find(info =>
                        info.region === region &&
                        physAddr >= info.physBaseAddress &&
                        physAddr < info.physBaseAddress + info.totalBytes
                    ) ??
                    this._livePanelInfo.find(info =>
                        physAddr >= info.physBaseAddress &&
                        physAddr < info.physBaseAddress + info.totalBytes
                    );
                if (target) {
                    // Reveal the panel in its current column (undefined preserves the
                    // panel's existing position rather than forcing it into column one).
                    target.panel.reveal(undefined, false);
                    target.panel.webview.postMessage({ type: 'gotoAddress', address: addr });
                } else {
                    // No region found — tell the requesting panel so it can show "out of range".
                    panel.webview.postMessage({ type: 'gotoRegionNotFound', address: addr });
                }
            } else if (message.type === 'patchBytes') {
                if (this.socketClient?.isConnected()) {
                    try {
                        await this.socketClient.writeMemory(
                            message.address as number,
                            message.hexBytes as string,
                            'ee'
                        );
                        // Recompute the page that contains this address and refresh it.
                        const instrIndex = Math.floor(
                            ((message.address as number) - baseAddress) / DisasmEditorProvider.INSTRUCTION_SIZE
                        );
                        const pageStart = Math.floor(instrIndex / DisasmEditorProvider.PAGE_SIZE)
                            * DisasmEditorProvider.PAGE_SIZE;
                        await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, pageStart);
                    } catch (err: any) {
                        console.error(`[LIVE] PatchBytes failed: ${err?.message ?? err}`);
                    }
                }
            } else if (message.type === 'setRegister') {
                if (this.socketClient?.isConnected()) {
                    try {
                        await this.socketClient.setRegister(
                            message.regType as string,
                            message.regIdx as number,
                            message.regValue as string
                        );
                        // Re-fetch and broadcast the updated register state.
                        const regs = await this.socketClient.getRegisters();
                        const sp = regs.gprLo32?.[29] ?? regs.gpr?.[29] ?? 0;
                        let stackBytes: number[] = [];
                        let stackBase  = 0;
                        if (sp) {
                            try {
                                const buf = await this.socketClient.readMemory(sp, 128, 'ee');
                                stackBytes = Array.from(buf);
                                stackBase  = sp;
                            } catch { /* ignore */ }
                        }
                        panel.webview.postMessage({
                            type: 'status', paused: true, pc: regs.pc,
                            registers: regs, stackBytes, stackBase,
                        });
                    } catch (err: any) {
                        console.error(`[LIVE] SetRegister failed: ${err?.message ?? err}`);
                    }
                }
            } else if (message.type === 'disasmBytes') {
                // Quick disassembly preview for the bytes editor in the edit overlay.
                if (this.socketClient?.isConnected()) {
                    try {
                        const results = await this.socketClient.disassemble(
                            message.hexBytes as string,
                            message.address as number
                        );
                        const first = results?.[0];
                        panel.webview.postMessage({
                            type: 'disasmBytesResult',
                            preview: first?.Success && first.Assembly ? first.Assembly.trim() : '(invalid)',
                        });
                    } catch { /* ignore */ }
                }
            } else if (message.type === 'saveLabel') {
                await this.handleSaveLabel(panel, message.address, message.newLabel ?? '', message.newComment ?? '');
            } else if (message.type === 'syncGotoInput') {
                // Propagate the goto-input value to all other live panels so every
                // region panel stays in sync when the user types an address.
                const value = message.value as string;
                for (const info of this._livePanelInfo) {
                    if (info.panel !== panel) {
                        info.panel.webview.postMessage({ type: 'syncGotoInput', value });
                    }
                }
            }
        });

        // Set html AFTER registering the message handler so 'ready' is captured.
        panel.webview.html = this.getHtml(panel.webview);
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
     * Also fires the onPauseStateChange callback so the sidebar can update its bar.
     */
    private _broadcastStatus(paused: boolean): void {
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
        const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
        const maxBytes    = DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE;
        const readLength  = Math.min(totalBytes - startOffset, maxBytes);
        if (readLength <= 0) { return; }

        if (!this.socketClient || !this.socketClient.isConnected()) {
            // When not connected, send placeholder rows with default zero data so the
            // grid is navigable and shows 00000000 / nop before the server connects.
            const count = Math.floor(readLength / DisasmEditorProvider.INSTRUCTION_SIZE);
            const rows: DisassembledRow[] = [];
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

  /**
   * Unified search via C# backend — works for both live and file-based panels.
   * For live panels pass region and omit fileData.
   * For file panels pass fileData and omit region.
   */
  private async handleSearch(
    panel: vscode.WebviewPanel,
    query: string,
    mode: string,
    baseAddress: number,
    length: number,
    region?: string,
    fileData?: Buffer
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      panel.webview.postMessage({ type: 'searchResults', matches: [] });
      return;
    }
    try {
      const matches = await this.socketClient.search(
        baseAddress, length, query, mode, region, fileData
      );
      panel.webview.postMessage({ type: 'searchResults', matches });
    } catch (err: any) {
      console.error(`[SEARCH] Failed: ${err?.message ?? err}`);
      panel.webview.postMessage({ type: 'searchResults', matches: [] });
    }
  }

  /**
   * Scan a live memory region for strings or functions using the C# backend.
   * Posts a `stringsUpdated` or `functionsUpdated` message to the webview.
   */
  private async handleLiveScan(
    kind: 'strings' | 'functions',
    panel: vscode.WebviewPanel,
    region: string,
    baseAddress: number,
    totalBytes: number
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      if (kind === 'strings') {
        const strings = await this.socketClient.scanLiveStrings(baseAddress, totalBytes, region);
        panel.webview.postMessage({ type: 'stringsUpdated', strings });
      } else {
        const functions = await this.socketClient.scanLiveFunctions(baseAddress, totalBytes, region);
        panel.webview.postMessage({ type: 'functionsUpdated', functions });
      }
    } catch (err: any) {
      console.error(`[SYMBOLS] LiveScan(${kind}) failed: ${err?.message ?? err}`);
      // Send empty result so the webview re-enables the scan button
      if (kind === 'strings') {
        panel.webview.postMessage({ type: 'stringsUpdated', strings: [] });
      } else {
        panel.webview.postMessage({ type: 'functionsUpdated', functions: [] });
      }
    }
  }

  /**
   * Scan file binary data for strings or functions using the C# backend.
   * Posts a `stringsUpdated` or `functionsUpdated` message to the webview.
   */
  private async handleFileScan(
    kind: 'strings' | 'functions',
    panel: vscode.WebviewPanel,
    fileData: Buffer,
    baseAddress: number
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      if (kind === 'strings') {
        const strings = await this.socketClient.getStrings(fileData, baseAddress);
        panel.webview.postMessage({ type: 'stringsUpdated', strings });
      } else {
        const functions = await this.socketClient.getFunctions(fileData, baseAddress);
        panel.webview.postMessage({ type: 'functionsUpdated', functions });
      }
    } catch (err: any) {
      console.error(`[SYMBOLS] FileScan(${kind}) failed: ${err?.message ?? err}`);
      if (kind === 'strings') {
        panel.webview.postMessage({ type: 'stringsUpdated', strings: [] });
      } else {
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
   * Open a VSCode multi-step input dialog to edit the label and comment for an
   * address.  The title bar shows the address and raw instruction bytes so the
   * user has full context.  Pressing Escape on either step cancels the whole
   * operation with no changes saved.
   *
   * @param addressHex - Hex digits only (no "0x" prefix), e.g. "80010000"
   */
  private async handleEditLabel(
    panel: vscode.WebviewPanel,
    address: number,
    addressHex: string,
    dataBytes: string,
    currentLabel: string,
    currentComment: string
  ): Promise<void> {
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
    if (newLabel === undefined) { return; } // cancelled

    const newComment = await vscode.window.showInputBox({
      title: `Edit Comment — ${addrDisplay}`,
      prompt: 'Comment (leave empty to remove)',
      value: currentComment,
      placeHolder: 'Comment',
      ignoreFocusOut: true,
    });
    if (newComment === undefined) { return; } // cancelled

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
  private async handleSaveLabel(
    panel: vscode.WebviewPanel,
    address: number,
    newLabel: string,
    newComment: string
  ): Promise<void> {
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