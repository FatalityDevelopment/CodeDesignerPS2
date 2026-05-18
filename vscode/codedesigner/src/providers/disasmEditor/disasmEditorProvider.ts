import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import { SocketClient, LabelEntry, StringEntry, FunctionEntry, PointerEntry, FloatEntry, IntegerEntry, SearchMatch, ReferenceEntry, AiActivityEvent, AiSessionStartEvent, AiLabelUpdateEvent, StructEntry, LabelManagerEntry, LabelSetPayload } from "../../socketClient";
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

/** Cached scanner results stored per panel. */
interface PanelScanCache {
  strings?:   StringEntry[];
  functions?: FunctionEntry[];
  pointers?:  PointerEntry[];
  floats?:    FloatEntry[];
  integers?:  IntegerEntry[];
}

export class DisasmEditorProvider implements vscode.CustomReadonlyEditorProvider {
  private static readonly HEX_VIEW_SIZE = 0x30;
  private static readonly INSTRUCTION_SIZE = 4;
  private static readonly BASE_ADDRESS = 0x00000000;
  private static readonly PAGE_SIZE = 1000;
  private static readonly BUFFER_PAGES = 2;
  private static readonly LIVE_READ_BACKOFF_MS = 5000;
  /** Placeholder game ID used when no disc image is selected; the backend resolves
   *  this to null and returns only the Built-in label set for all read operations. */
  private static readonly NO_GAME_SELECTED = 'no-game-selected';
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
  /** The single open intro/splash panel. Undefined when the user closes it. */
  private _introPanel: vscode.WebviewPanel | undefined;
  /** The single open Label Manager tab panel. Undefined when the user closes it. */
  private _labelManagerPanel: vscode.WebviewPanel | undefined;

  /**
   * Per-panel cache of the most recent scanner results (strings, functions, pointers,
   * floats, integers).  Populated when a scan completes; re-sent to the webview when
   * the symbols panel is opened after a page reload without requiring a re-scan.
   */
  private _panelScanCache: Map<vscode.WebviewPanel, PanelScanCache> = new Map();

  /**
   * All breakpoints the user has toggled across all live panels.  Persisted here
   * in the extension process so they can be reliably re-sent to the C# server on
   * every connect/reconnect/restart — without depending on the Angular webview
   * being bootstrapped or having processed an 'init' message first.
   */
  private _knownBreakpoints: Set<number> = new Set();
  /** Game-specific labels loaded from snapshots/<isoName>/labels.json. Merged with server labels. */
  private _gameLabels: LabelEntry[] = [];
  /** Labels derived from live-resolved Pointer Manager entries. Shown as an overlay in the instruction display. */
  private _pointerLabels: LabelEntry[] = [];
  /** Labels derived from live-resolved struct member definitions (C# backend). Merged with pointer labels. */
  private _structLabels: LabelEntry[] = [];
  /** Labels derived from enabled archive cheats from the CheatCodeArchive. */
  private _archiveLabels: LabelEntry[] = [];
  /** Current game (ISO) name — used to resolve the per-game labels.json path. */
  private _currentGameName: string = '';
  /** Current game disc identifier for label manager DB queries — serial (e.g. "SCUS-97275") or full ISO path. */
  private _currentGameSerial: string = '';
  /** Workspace root — used to resolve the per-game labels.json path. */
  private _workspaceRoot: string = '';
  /** Name of the currently active label set on the server (e.g. "MyLabels"), or null when no user set is active. */
  private _activeSetName: string | null = null;
  /** Suppress repeated live read retries after emulator-side connection failures. */
  private _liveReadBackoffUntil = 0;
  private _liveReadFailureActive = false;
  private _liveReadFailureMessage = '';
  /** True while a live socket read is in-flight; prevents concurrent requests from piling up. */
  private _liveReadInFlight = false;

  /**
   * When true, the editor automatically navigates to the address the AI (MCP
   * server) is currently working with.  Toggled by the "Follow AI" toolbar
   * button in every live disassembly panel.
   */
  private _followAiEnabled = false;

  /**
   * Optional callback invoked when the emulator's pause state changes (e.g. breakpoint hit).
   * Set by DisasmPanelProvider so it can update the sidebar status bar.
   */
  public onPauseStateChange?: (paused: boolean) => void;

  /**
   * Optional callback invoked when a webview requests opening a live memory region by label
   * (e.g. from the memory-region-selector toolbar buttons in the disasm editor).
   * Set by DisasmPanelProvider so it can open the appropriate optional memory view.
   */
  public onOpenMemoryView?: (label: string) => Promise<void>;

  /**
   * Optional callback for retrieving address history from snapshots.
   * Wired to DisasmPanelProvider.getAddressHistory() in extension.ts.
   */
  public onGetAddressHistory?: (address: number) => Array<{ name: string; date: string; value: string; disasm?: string }>;

  // ── Public helpers ──────────────────────────────────────────────────────────

  /**
   * Reveal the live panel whose label matches `label`.
   * Called by DisasmPanelProvider when the user selects a region from the dropdown.
   */
  public focusLivePanelByLabel(label: string): void {
    const info = this._livePanelInfo.find(i => i.label === label);
    if (info) {
      info.panel.reveal(undefined, false);
    }
  }

  /**
   * Navigate the appropriate live panel to `address`.
   * Reveals the panel whose address range contains the address and sends a
   * `gotoAddress` message so the disassembler cursor jumps there.
   * The webview handles `gotoAddress` in `disasm-editor.service.ts` via the
   * `gotoAddressReceived$` subject, which the component uses to move the cursor.
   * Called by external providers (e.g. the Pointer Manager tree view).
   */
  public openStructCalculator(): void {
    this._revealAndPost('openStructCalculator');
  }

  /** Open the Cheat Code Converter overlay in the first live panel. */
  public openConverterOverlay(): void {
    this._revealAndPost('openConverterOverlay');
  }

  /** Open the Cheat Code Porter overlay in the first live panel. */
  public openPorterOverlay(): void {
    this._revealAndPost('openPorterOverlay');
  }

  /** Open the Memory Card Manager overlay in the first live panel. */
  public openMemoryCardOverlay(): void {
    this._revealAndPost('openMemoryCardOverlay');
  }

  /** Open the HDD Image Manager overlay in the first live panel. */
  public openHddOverlay(): void {
    this._revealAndPost('openHddOverlay');
  }

  /** Open the Memory Scanner overlay in the first live panel. */
  public openScannerOverlay(): void {
    this._revealAndPost('openScannerOverlay');
  }

  /** Reveal the first live panel and post a message to it. */
  private _revealAndPost(messageType: string): void {
    if (this._livePanels.length === 0) { return; }
    this._livePanels[0].reveal(undefined, false);
    this._livePanels[0].webview.postMessage({ type: messageType });
  }

  public navigateToAddress(address: number): void {
    const physAddr = DisasmEditorProvider._toPhysAddr(address);
    const target = this._livePanelInfo.find(info =>
        physAddr >= info.physBaseAddress &&
        physAddr < info.physBaseAddress + info.totalBytes
    );
    if (target) {
        target.panel.reveal(undefined, false);
        target.panel.webview.postMessage({ type: 'gotoAddress', address });
        // Record this navigation in the server-side history so the AI can
        // query what the user has been looking at.
        this.socketClient?.recordNav(address, 'user').catch(() => { /* non-fatal */ });
    }
  }

  constructor(socketClient: SocketClient | null, private readonly _extensionUri: vscode.Uri) {
    this.socketClient = socketClient;
    if (socketClient) {
      // Subscribe to AI-activity notifications whenever a connection is established.
      socketClient.onConnected(() => {
        if (this._followAiEnabled) {
          socketClient.subscribeActivity();
        }
      });
      // Navigate the disassembly view when the AI works at a known address.
      socketClient.onActivityNotification((event: AiActivityEvent) => {
        if (this._followAiEnabled) {
          this.navigateToAddress(event.address);
        }
      });
      // Automatically enable Follow AI mode when the MCP server starts a session.
      socketClient.onSessionStart((_event: AiSessionStartEvent) => {
        this.setFollowAiEnabled(true);
      });
      // Refresh the label manager when the AI modifies labels.
      socketClient.onLabelUpdate((event: AiLabelUpdateEvent) => {
        this._handleAiLabelUpdate(event);
      });
    }
  }

  /**
   * Set game-specific labels (from the ISO's labels.json file).
   * Broadcasts the merged label list to all open live panels.
   */
  public setGameLabels(labels: LabelEntry[]): void {
    this._gameLabels = labels;
    // Broadcast updated merged labels to all live panels
    if (this._livePanels.length > 0) {
      this._broadcastMergedLabels();
    }
  }

  /**
   * Set labels derived from enabled archive cheats (CheatCodeArchive).
   * Broadcasts the merged label list to all open live panels so the disassembly
   * display shows archive cheat names at their target addresses.
   */
  public setArchiveLabels(labels: LabelEntry[]): void {
    this._archiveLabels = labels;
    if (this._livePanels.length > 0) {
      this._broadcastMergedLabels();
    }
  }

  /**
   * Set the live-resolved labels from the Pointer Manager.
   * Broadcasts a `pointerLabelsUpdated` message to all open live panels so the
   * instruction display can overlay the label names for matching addresses.
   * Also merges them into the labels list so the Labels tab shows struct labels too.
   */
  public setPointerLabels(labels: LabelEntry[]): void {
    this._pointerLabels = labels;
    const combined = this._mergedPointerAndStructLabels();
    for (const p of this._livePanels) {
      p.webview.postMessage({ type: 'pointerLabelsUpdated', labels: combined });
    }
    // Refresh the merged labels list so the Labels tab is kept in sync.
    if (this._livePanels.length > 0) {
      this._broadcastMergedLabels();
    }
  }

  /** Returns the merged set of pointer-manager labels and struct-derived labels. */
  private _mergedPointerAndStructLabels(): LabelEntry[] {
    if (this._structLabels.length === 0) { return this._pointerLabels; }
    if (this._pointerLabels.length === 0) { return this._structLabels; }
    return this._mergeLabels(this._pointerLabels, this._structLabels);
  }

  /**
   * Fetch all struct definitions from the C# backend, resolve member addresses
   * using live-memory pointer reads, and broadcast the resulting labels via
   * `pointerLabelsUpdated` so they appear as overlays in the instruction display.
   *
   * Each struct's `baseAddress` is treated as a pointer variable in PS2 memory.
   * We dereference it to obtain the actual data-struct base, then add each
   * member's offset to produce the resolved label address.  If the dereference
   * fails (emulator not connected, or invalid pointer) we fall back to using
   * `baseAddress + offset` directly.
   */
  public async refreshStructLabels(): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      if (this._structLabels.length > 0) {
        this._structLabels = [];
        this._broadcastPointerLabels();
      }
      this.onStructsChanged?.([]);
      return;
    }
    try {
      const structs = await this.socketClient.getStructs();
      await this._resolveStructLabelsFrom(structs);
      this.onStructsChanged?.(structs);
    } catch {
      // Non-fatal: struct resolution failures are silently ignored.
    }
  }

  /** Resolve label entries from a freshly-fetched struct list and broadcast if the result has changed. */
  private async _resolveStructLabelsFrom(structs: StructEntry[]): Promise<void> {
    const labels: LabelEntry[] = [];
    for (const s of structs) {
      const baseHex = s.baseAddress.replace(/^0x/i, '');
      const baseNum = parseInt(baseHex, 16) >>> 0;
      if (isNaN(baseNum)) { continue; }

      // Add the struct pointer address itself as a top-level label.
      labels.push({ address: s.baseAddress, label: s.name });

      if (s.members.length === 0) { continue; }

      // Try to dereference the pointer to find the struct data base.
      let dataBase: number | undefined;
      try {
        const bytes = await this.socketClient!.readMemory(baseNum, 4, 'ee');
        if (bytes.length >= 4) {
          const ptrVal = bytes.readUInt32LE(0) >>> 0;
          // Sanity-check: must be a non-zero address in a valid PS2 EE range.
          // Physical (0x00000001–0x1FFFFFFF), scratchpad (0x70000000–0x70003FFF),
          // KSEG0 (0x80000000–0x9FFFFFFF), and KSEG1 (0xA0000000–0xBFFFFFFF)
          // are all legitimate target addresses; reject 0 and anything >= 0xC0000000.
          if (ptrVal > 0 && ptrVal < 0xC0000000) {
            dataBase = ptrVal;
          }
        }
      } catch {
        dataBase = undefined;
      }

      // Fall back to using the base address directly if dereference fails.
      const memberBase = dataBase ?? baseNum;
      for (const m of s.members) {
        const addr = (memberBase + m.offset) >>> 0;
        const addrStr = '0x' + addr.toString(16).toUpperCase().padStart(8, '0');
        labels.push({ address: addrStr, label: m.name });
      }
    }
    this._structLabels = labels;
    this._broadcastPointerLabels();
  }

  /** Broadcast the merged pointer + struct labels to all live panels. */
  private _broadcastPointerLabels(): void {
    const combined = this._mergedPointerAndStructLabels();
    for (const p of this._livePanels) {
      p.webview.postMessage({ type: 'pointerLabelsUpdated', labels: combined });
    }
    if (this._livePanels.length > 0) {
      this._broadcastMergedLabels();
    }
  }


  /** Merge server labels with game-specific labels. Game labels take priority on conflicts. */
  private _mergeLabels(serverLabels: LabelEntry[], gameLabels: LabelEntry[]): LabelEntry[] {
    const map = new Map<string, LabelEntry>();
    for (const l of serverLabels) { map.set(l.address.toLowerCase(), l); }
    for (const l of gameLabels)   { map.set(l.address.toLowerCase(), l); }
    return Array.from(map.values());
  }

  /**
   * Build the fully-merged label list (server + game + pointer + struct + archive labels) and
   * broadcast a `labelsUpdated` message to all live panels.
   */
  private _broadcastMergedLabels(): void {
    const broadcast = (base: LabelEntry[]) => {
      let merged = this._gameLabels.length > 0 ? this._mergeLabels(base, this._gameLabels) : base;
      if (this._pointerLabels.length > 0) { merged = this._mergeLabels(merged, this._pointerLabels); }
      if (this._structLabels.length > 0) { merged = this._mergeLabels(merged, this._structLabels); }
      if (this._archiveLabels.length > 0) { merged = this._mergeLabels(merged, this._archiveLabels); }
      for (const p of this._livePanels) {
        p.webview.postMessage({ type: 'labelsUpdated', labels: merged });
      }
    };

    if (this.socketClient?.isConnected()) {
      this.socketClient.getLabels()
        .then(serverLabels => broadcast(serverLabels))
        .catch(() => broadcast([]));
    } else {
      broadcast([]);
    }
  }

  /**
   * Called by DisasmPanelProvider to propagate sidebar-initiated pause/resume
   * to all editor panels' live-bar state.
   */
  public notifyPauseState(paused: boolean): void {
    this._broadcastStatus(paused);
  }

  /**
   * Enable or disable "Follow AI" mode.
   * When enabled, the disassembly cursor automatically navigates to whichever
   * address the MCP AI server is currently working with.
   * Sends `SUBSCRIBE_ACTIVITY` to the socket server when turning on (if connected).
   */
  public setFollowAiEnabled(enabled: boolean): void {
    if (this._followAiEnabled === enabled) { return; }
    this._followAiEnabled = enabled;
    if (enabled && this.socketClient?.isConnected()) {
      this.socketClient.subscribeActivity();
    }
    this._broadcastFollowAiState();
  }

  /** Returns the current Follow AI toggle state. */
  public get followAiEnabled(): boolean { return this._followAiEnabled; }

  /** Broadcast the current Follow AI state to all live panels. */
  private _broadcastFollowAiState(): void {
    const msg = { type: 'followAiState', enabled: this._followAiEnabled };
    for (const p of this._livePanels) {
      p.webview.postMessage(msg);
    }
  }

  /**
   * Handle an AI label-update notification pushed by the server.
   * Refreshes the label manager UI and navigates to the label set the AI
   * is working with so the user can follow along.
   */
  private async _handleAiLabelUpdate(event: AiLabelUpdateEvent): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    const ctx = this._labelManagerCtx();
    if (!ctx) { return; }
    const { workspaceDir, gameDiscId } = ctx;
    try {
      // Determine which set to display — prefer the set from the notification,
      // fall back to the currently active set.
      const targetSetName = event.setName ?? this._activeSetName;
      if (!targetSetName) { return; }

      // Activate and load the label set.
      const set = await this.socketClient.setActiveLabelSet(workspaceDir, gameDiscId, targetSetName);
      this._activeSetName = targetSetName;

      // Broadcast the updated set to all live panels and the standalone
      // label manager tab (if open).
      this._broadcastLabelSet(set);

      // Also refresh the label set list so newly created sets appear.
      const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
      for (const p of this._livePanels) {
        p.webview.postMessage({ type: 'labelManagerSets', sets });
      }
      this._labelManagerPanel?.webview.postMessage({ type: 'labelManagerSets', sets });
    } catch {
      // Non-fatal — the user can always manually refresh.
    }
  }

  /**
   * Set the currently active game name and workspace root.
   * Called by DisasmPanelProvider when the selected game changes.
   * Used to resolve the per-game labels.json path for user-created labels.
   */
  public setCurrentGame(gameName: string, workspaceRoot: string, gameSerial?: string): void {
    this._currentGameName   = gameName;
    this._workspaceRoot     = workspaceRoot;
    // Always update the serial — clear it when not provided so a stale serial
    // from a previously loaded game isn't used by _labelManagerCtx().
    this._currentGameSerial = gameSerial ?? '';
    // Reset the active label set when the game changes — it will be re-established
    // when the label manager refreshes or the user selects a set.
    this._activeSetName = null;
  }

  /**
   * Fetch every entry in the currently active label set, for the disasm
   * panel's "Recent Labels" list. Picks the set the user is actually viewing
   * (the cached active set, else the first user set, else "Built-in").
   * Returns an empty array when disconnected or no workspace is open.
   */
  public async getActiveLabelEntries(): Promise<LabelManagerEntry[]> {
    if (!this.socketClient?.isConnected()) { return []; }
    const ctx = this._labelManagerCtx();
    if (!ctx) { return []; }
    const { workspaceDir, gameDiscId } = ctx;
    try {
      const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
      const activeName = (this._activeSetName && sets.includes(this._activeSetName))
        ? this._activeSetName
        : (sets.find(s => s !== 'Built-in') ?? 'Built-in');
      const set = await this.socketClient.getLabelSet(workspaceDir, gameDiscId, activeName);
      return set.entries ?? [];
    } catch {
      return [];
    }
  }

  /**
   * Returns the path to the per-game labels.json file, or null if the game
   * name or workspace root is not set.
   */
  private _gameLabelsPath(): string | null {
    if (!this._currentGameName || !this._workspaceRoot) { return null; }
    return path.join(this._workspaceRoot, 'snapshots', this._currentGameName, 'labels.json');
  }

  /**
   * Save a label entry to the per-game labels.json file on disk.
   * Creates the directory and file if they don't exist yet.
   */
  private _saveGameLabel(address: number, labelName: string, comment: string | undefined): void {
    const labelsPath = this._gameLabelsPath();
    if (!labelsPath) { return; }
    try {
      const dir = path.dirname(labelsPath);
      if (!fs.existsSync(dir)) { fs.mkdirSync(dir, { recursive: true }); }

      let entries: LabelEntry[] = [];
      if (fs.existsSync(labelsPath)) {
        const raw = fs.readFileSync(labelsPath, 'utf-8');
        const doc = JSON.parse(raw) as { symbol?: LabelEntry[] };
        entries = doc.symbol ?? (Array.isArray(doc) ? doc as LabelEntry[] : []);
      }

      const addrStr = `0x${address.toString(16).toUpperCase().padStart(8, '0')}`;
      const idx = entries.findIndex(e => e.address.toLowerCase() === addrStr.toLowerCase());
      const entry: LabelEntry = { address: addrStr, label: labelName, comment };
      if (idx >= 0) { entries[idx] = entry; } else { entries.push(entry); }

      fs.writeFileSync(labelsPath, JSON.stringify({ symbol: entries }, null, 2), 'utf-8');
      // Update the in-memory cache so the merged list stays consistent.
      this._gameLabels = entries;
    } catch (err: any) {
      console.error(`[LABELS] Failed to save game label: ${err?.message ?? err}`);
    }
  }

  /**
   * Remove a label entry from the per-game labels.json file on disk.
   */
  private _deleteGameLabel(address: number): void {
    const labelsPath = this._gameLabelsPath();
    if (!labelsPath || !fs.existsSync(labelsPath)) { return; }
    try {
      const raw = fs.readFileSync(labelsPath, 'utf-8');
      const doc = JSON.parse(raw) as { symbol?: LabelEntry[] };
      let entries: LabelEntry[] = doc.symbol ?? (Array.isArray(doc) ? doc as LabelEntry[] : []);

      const addrStr = `0x${address.toString(16).toUpperCase().padStart(8, '0')}`;
      entries = entries.filter(e => e.address.toLowerCase() !== addrStr.toLowerCase());

      fs.writeFileSync(labelsPath, JSON.stringify({ symbol: entries }, null, 2), 'utf-8');
      // Update the in-memory cache so the merged list stays consistent.
      this._gameLabels = entries;
    } catch (err: any) {
      console.error(`[LABELS] Failed to delete game label: ${err?.message ?? err}`);
    }
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
      this._applyBreakpointHitFallback(statusMsg);
      // Attach any live analysis comments produced for the stepped instruction.
      if (stepResult?.instructionAnalysis) {
        statusMsg.instructionAnalysis = stepResult.instructionAnalysis;
      }
      if (status.paused) {
        try {
          const regs = await this.socketClient!.getRegisters();
          statusMsg.registers = regs;
          const sp = regs.gprLo32?.[29] ?? regs.gpr?.[29];
          if (sp !== undefined) {
            try {
              const stackBuf = await this.socketClient!.readMemory(sp, 512, 'ee');
              statusMsg.stackBytes = Array.from(stackBuf);
              statusMsg.stackBase  = sp;
            } catch { /* ignore stack read errors */ }
          }
        } catch { /* ignore register fetch errors */ }
        const hitAddr = statusMsg.breakpointHitAddress ?? status.pc;
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

      // Call C# backend to disassemble.  Passing the current game serial lets
      // the bridge apply any persisted DataFormatOverrides so the response
      // rows have variable byte length (.byte/.half/.word/.dword/.qword).
      const hexCode = hexInstructions.join(' ');
      const gameId = this._currentGameSerial || undefined;
      console.log(`[DISASM] Disassembling ${hexInstructions.length} instructions starting at 0x${startAddr.toString(16)}`);
      const results = await this.socketClient.disassemble(hexCode, startAddr, gameId);
      console.log(`[DISASM] Received ${results.length} results from backend`);

      // Convert results to row format.  Each result carries its own Address
      // and ByteLength when produced by the variable-stride disassembler — we
      // honour those rather than re-deriving from a fixed 4-byte stride.
      for (let i = 0; i < results.length; i++) {
        const result = results[i];
        const byteLen = result.ByteLength ?? DisasmEditorProvider.INSTRUCTION_SIZE;
        const currentAddr = result.Address !== undefined
          ? result.Address
          : (startAddr + i * DisasmEditorProvider.INSTRUCTION_SIZE);
        const offset = currentAddr - startAddr;

        if (result.Success && result.Assembly) {
          // Parse the assembly instruction to extract op and args
          const parts = result.Assembly.trim().split(/\s+/);
          const op = parts[0] || '';
          const args = parts.slice(1).join(' ');

          // Get bytes in display format (space-separated, big-endian)
          const bytesLE = (offset >= 0 && offset + byteLen <= buffer.length)
            ? this.formatBytesForDisplay(buffer, offset, byteLen)
            : '';

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
          const bytesLE = (offset >= 0 && offset + byteLen <= buffer.length)
            ? this.formatBytesForDisplay(buffer, offset, byteLen)
            : '';

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
    const bgLogoUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this._extensionUri, 'dist', 'resources', 'codedesigner-logo.png')
    );
    const nonce = getNonce();
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               img-src ${webview.cspSource};
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<style>:root { --cd-bg-logo: url("${bgLogoUri}"); }</style>
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
      } else if (message.type === "scanPointers") {
        await this.handleFileScan('pointers', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
      } else if (message.type === "scanFloats") {
        await this.handleFileScan('floats', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
      } else if (message.type === "scanIntegers") {
        await this.handleFileScan('integers', webviewPanel, fileData, DisasmEditorProvider.BASE_ADDRESS);
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
      } else if (message.type === "getAddressHistory") {
        const history = this.onGetAddressHistory
            ? this.onGetAddressHistory(message.address as number)
            : [];
        if (this.socketClient?.isConnected()) {
          await Promise.allSettled(history.map(async entry => {
            if (!entry.disasm && entry.value) {
              const results = await this.socketClient!.disassemble(entry.value, message.address as number);
              if (results?.[0]?.Assembly) { entry.disasm = results[0].Assembly; }
            }
          }));
        }
        webviewPanel.webview.postMessage({ type: 'addressHistoryResult', address: message.address, history });
      } else if (message.type === "getAddressRefs") {
        await this.handleGetAddressRefs(webviewPanel, message.address as number,
          DisasmEditorProvider.BASE_ADDRESS, fileData.length, undefined, fileData,
          message.kinds as string[] | undefined);
      } else if (message.type === "assembleInstr") {
        if (this.socketClient?.isConnected()) {
          try {
            const results = await this.socketClient.assemble(message.instruction as string);
            const first = results?.[0];
            if (first?.Success && first.Hex) {
              const hexBytes = first.Hex.replace(/^0x/i, '').toUpperCase();
              webviewPanel.webview.postMessage({ type: 'assembleInstrResult', hexBytes });
            } else {
              webviewPanel.webview.postMessage({ type: 'assembleInstrResult', hexBytes: null });
            }
          } catch {
            webviewPanel.webview.postMessage({ type: 'assembleInstrResult', hexBytes: null });
          }
        }
      } else if (message.type === "convertCheat") {
        if (this.socketClient?.isConnected()) {
          try {
            const result = await this.socketClient.convertCheat(
              message.codes as string,
              message.direction as 'toRaw' | 'toEngine',
              message.format as string | undefined);
            webviewPanel.webview.postMessage({ type: 'convertCheatResult', result });
          } catch (err: unknown) {
            const errMsg = err instanceof Error ? err.message : String(err);
            webviewPanel.webview.postMessage({ type: 'convertCheatResult', error: errMsg });
          }
        } else {
          webviewPanel.webview.postMessage({ type: 'convertCheatResult', error: 'Not connected to emulator server' });
        }
      } else if (message.type === 'browseSourceDump') {
        const uris = await vscode.window.showOpenDialog({
          canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
          filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
          title: 'Select Source EE RAM Dump (r0001)',
        });
        if (uris?.[0]) {
          webviewPanel.webview.postMessage({ type: 'browseSourceDumpResult', filePath: uris[0].fsPath });
        }
      } else if (message.type === 'browseTargetDump') {
        const uris = await vscode.window.showOpenDialog({
          canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
          filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
          title: 'Select Target EE RAM Dump (r0002)',
        });
        if (uris?.[0]) {
          webviewPanel.webview.postMessage({ type: 'browseTargetDumpResult', filePath: uris[0].fsPath });
        }
      } else if (message.type === 'portCheats') {
        if (!this.socketClient?.isConnected()) {
          webviewPanel.webview.postMessage({ type: 'portCheatsResult', error: 'Not connected to emulator server' });
          return;
        }
        try {
          const { results, log } = await this.socketClient.portCheats(
            message.codes as string,
            message.sourcePath as string,
            message.targetPath as string,
          );
          getOutputChannel().info('[Port Cheats]\n' + log);
          webviewPanel.webview.postMessage({ type: 'portCheatsResult', results });
        } catch (err: unknown) {
          const errMsg = err instanceof Error ? err.message : String(err);
          getOutputChannel().error('[Port Cheats] ' + errMsg);
          webviewPanel.webview.postMessage({ type: 'portCheatsResult', error: errMsg });
        }
      } else if (message.type === 'browseMc') {
        await this._handleBrowseMc(webviewPanel.webview);
      } else if (message.type === 'newMc') {
        await this._handleNewMc(webviewPanel.webview);
      } else if (message.type === 'mcList') {
        await this._handleMcList(webviewPanel.webview, message.mcPath as string ?? '');
      } else if (message.type === 'mcExport') {
        await this._handleMcExport(webviewPanel.webview, message.mcPath as string ?? '', message.saveName as string ?? '');
      } else if (message.type === 'mcImport') {
        await this._handleMcImport(webviewPanel.webview, message.mcPath as string ?? '');
      } else if (message.type === 'mcDelete') {
        await this._handleMcDelete(webviewPanel.webview, message.mcPath as string ?? '', message.saveName as string ?? '');
      } else if (message.type === 'browseHdd') {
        await this._handleBrowseHdd(webviewPanel.webview);
      } else if (message.type === 'newHdd') {
        await this._handleNewHdd(webviewPanel.webview);
      } else if (message.type === 'hddList') {
        await this._handleHddList(webviewPanel.webview, message.hddPath as string ?? '');
      } else if (message.type === 'hddExport') {
        await this._handleHddExport(webviewPanel.webview, message.hddPath as string ?? '', message.partitionId as string ?? '');
      } else if (message.type === 'hddListFiles') {
        await this._handleHddListFiles(webviewPanel.webview, message.hddPath as string ?? '', message.partitionId as string ?? '');
      } else if (message.type === 'hddDelete') {
        await this._handleHddDelete(webviewPanel.webview, message.hddPath as string ?? '', message.partitionId as string ?? '');
      } else if (message.type === 'scanValue') {
        await this._handleScanValue(webviewPanel.webview, message);
      } else if (message.type === 'getStructs') {
        await this._handleGetStructs(webviewPanel.webview);
      } else if (message.type === 'addStruct') {
        await this._handleAddStruct(webviewPanel.webview, message.name as string, message.baseAddress as string);
      } else if (message.type === 'addStructMember') {
        await this._handleAddStructMember(webviewPanel.webview, message.structName as string, message.offset as number, message.memberName as string);
      } else if (message.type === 'readPointerAt') {
        await this._handleReadPointerAt(webviewPanel.webview, message.address as string);
      } else if (message.type === 'labelManagerAction') {
        await this._handleLabelManagerAction(webviewPanel.webview, message.action as any);
      } else if (message.type === 'openLabelManagerTab') {
        this.openLabelManagerTab(message.address as string | undefined);
      } else if (message.type === 'setDataFormat') {
        await this._handleSetDataFormat(
          webviewPanel,
          fileData,
          message.address as number,
          message.length as number,
          message.kind as 'byte' | 'half' | 'word' | 'dword' | 'qword',
          message.startIndex as number | undefined,
        );
      } else if (message.type === 'clearDataFormat') {
        await this._handleClearDataFormat(
          webviewPanel,
          fileData,
          message.address as number,
          message.length as number,
          message.startIndex as number | undefined,
        );
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
     * Open a splash-screen intro panel showing the CodeDesigner logo.
     * Called after all live memory panels are opened so it takes focus.
     * If the panel is already open (e.g. after reconnect), it is simply revealed.
     */
    openIntroPanel(): void {
        if (this._introPanel) {
            this._introPanel.reveal(undefined, false);
            return;
        }
        const panel = vscode.window.createWebviewPanel(
            'disasm.intro',
            'CodeDesigner',
            vscode.ViewColumn.One,
            {
                enableScripts: false,
                retainContextWhenHidden: true,
                localResourceRoots: [this._extensionUri]
            }
        );
        this._introPanel = panel;
        panel.onDidDispose(() => { this._introPanel = undefined; });
        const imgUri = panel.webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'resources', 'codedesigner-logo.png')
        );
        panel.webview.html = `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${panel.webview.cspSource};">
<style>
  html, body {
    margin: 0; padding: 0;
    width: 100%; height: 100%;
    background: #1e1e1e;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
  }
  img {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
  }
</style>
</head>
<body>
<img src="${imgUri}" alt="CodeDesigner">
</body>
</html>`;
    }

    /**
     * Open (or focus) the standalone Label Manager tab.
     * If a tab is already open, it is simply revealed and optionally pre-filled.
     * @param prefillAddress Optional hex address to pre-fill in the Add Label form.
     */
    public openLabelManagerTab(prefillAddress?: string): void {
        if (this._labelManagerPanel) {
            this._labelManagerPanel.reveal(undefined, false);
            if (prefillAddress) {
                this._labelManagerPanel.webview.postMessage({ type: 'prefillAddress', address: prefillAddress });
            }
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'disasm.labelManager',
            '🏷 Label Manager',
            vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this._extensionUri],
            }
        );
        this._labelManagerPanel = panel;
        panel.onDidDispose(() => { this._labelManagerPanel = undefined; });

        panel.webview.onDidReceiveMessage(async (message) => {
            if (message.type === 'ready') {
                // Send initial data as soon as the webview bootstraps.
                await this._handleLabelManagerAction(panel.webview, { type: 'refresh' });
                if (prefillAddress) {
                    panel.webview.postMessage({ type: 'prefillAddress', address: prefillAddress });
                    prefillAddress = undefined;
                }
            } else if (message.type === 'labelManagerAction') {
                await this._handleLabelManagerAction(panel.webview, message.action as any);
            } else if (message.type === 'bootstrapError') {
                getOutputChannel().error('[LabelManager] Bootstrap error: ' + message.error);
            }
        });

        panel.webview.html = this._getLabelManagerHtml(panel.webview);
    }

    /** Build the HTML page for the Label Manager standalone webview tab. */
    private _getLabelManagerHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'label-manager.js')
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
<app-label-manager-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Label Manager…</p></app-label-manager-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
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
            existingInfo.physBaseAddress = DisasmEditorProvider._toPhysAddr(baseAddress);
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
                if (this._pointerLabels.length > 0 || this._structLabels.length > 0) {
                    existingInfo.panel.webview.postMessage({ type: 'pointerLabelsUpdated', labels: this._mergedPointerAndStructLabels() });
                }
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
        const physBase = DisasmEditorProvider._toPhysAddr(baseAddress);
        const info = { panel, label, region, baseAddress, totalBytes, physBaseAddress: physBase };
        this._livePanels.push(panel);
        this._livePanelInfo.push(info);
        panel.onDidDispose(() => {
            this._livePanels = this._livePanels.filter(p => p !== panel);
            this._livePanelInfo = this._livePanelInfo.filter(info => info.panel !== panel);
            this._panelScanCache.delete(panel);
        });

        // Pause/resume the webview's live-refresh timers based on panel
        // visibility.  Hidden panels that keep polling waste socket bandwidth
        // and cause the visible panel to receive placeholder zero-rows
        // (flickering) when the in-flight guard blocks concurrent reads.
        panel.onDidChangeViewState(e => {
            panel.webview.postMessage({
                type: 'panelVisibility',
                visible: e.webviewPanel.visible,
            });
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
                // Send any already-resolved struct labels so they appear immediately.
                if (this._pointerLabels.length > 0 || this._structLabels.length > 0) {
                    panel.webview.postMessage({ type: 'pointerLabelsUpdated', labels: this._mergedPointerAndStructLabels() });
                }
                // Sync the Follow AI toggle state so the toolbar button reflects
                // the current setting even when the panel was just opened.
                panel.webview.postMessage({ type: 'followAiState', enabled: this._followAiEnabled });
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
                if (!this.socketClient?.isConnected()) { return; }
                try {
                    await this.socketClient!.pause();
                    const status = await this.socketClient!.getPC();
                    const statusMsg: any = { type: 'status', ...status };
                    this._applyBreakpointHitFallback(statusMsg);
                    if (status.paused) {
                        try {
                            const regs = await this.socketClient!.getRegisters();
                            statusMsg.registers = regs;
                            // Fetch stack memory (128 bytes from $sp = gpr[29])
                            const sp = regs.gprLo32?.[29] ?? regs.gpr?.[29];
                            if (sp !== undefined) {
                                try {
                                    const stackBuf = await this.socketClient!.readMemory(sp, 512, 'ee');
                                    statusMsg.stackBytes = Array.from(stackBuf);
                                    statusMsg.stackBase = sp;
                                } catch { /* ignore stack read errors */ }
                            }
                        } catch { /* ignore register fetch errors */ }
                        const hitAddr = statusMsg.breakpointHitAddress ?? status.pc;
                        this._revealPanelForAddress(hitAddr);
                    }
                    this._broadcastFullStatus(statusMsg);
                    this.onPauseStateChange?.(status.paused);
                } catch (err: any) {
                    const msg = err?.message ?? String(err);
                    console.error(`[LIVE] Failed to pause emulator: ${msg}`);
                    vscode.window.showWarningMessage(`Could not pause emulator: ${msg}`);
                }
            } else if (message.type === 'resume') {
                if (!this.socketClient?.isConnected()) { return; }
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
            } else if (message.type === 'addPineWatch') {
                if (!this._canIssueLiveBackendRequest()) { return; }
                const addr: number = typeof message.address === 'number' ? message.address : 0;
                const end: number  = typeof message.end     === 'number' ? message.end     : addr + 4;
                try {
                    await this.socketClient!.addPineWatch(addr, end);
                } catch (err: any) {
                    console.warn(`[LIVE] AddPineWatch failed: ${err?.message ?? err}`);
                }
                try {
                    const watches = await this.socketClient!.listPineWatches();
                    panel.webview.postMessage({ type: 'pineWatches', watches });
                } catch (err: any) {
                    console.warn(`[LIVE] ListPineWatches failed after add: ${err?.message ?? err}`);
                }
            } else if (message.type === 'removePineWatch') {
                if (!this._canIssueLiveBackendRequest()) { return; }
                const addr: number = typeof message.address === 'number' ? message.address : 0;
                const end: number  = typeof message.end     === 'number' ? message.end     : addr + 4;
                const cpu: number  = typeof message.cpu     === 'number' ? message.cpu     : 0;
                try {
                    await this.socketClient!.removePineWatch(addr, end, cpu);
                } catch (err: any) {
                    console.warn(`[LIVE] RemovePineWatch failed: ${err?.message ?? err}`);
                }
                try {
                    const watches = await this.socketClient!.listPineWatches();
                    panel.webview.postMessage({ type: 'pineWatches', watches });
                } catch (err: any) {
                    console.warn(`[LIVE] ListPineWatches failed after remove: ${err?.message ?? err}`);
                }
            } else if (message.type === 'clearAllPineWatches') {
                if (!this._canIssueLiveBackendRequest()) { return; }
                try {
                    await this.socketClient!.clearAllPineWatches();
                    panel.webview.postMessage({ type: 'pineWatches', watches: [] });
                } catch (err: any) {
                    console.warn(`[LIVE] ClearAllPineWatches failed: ${err?.message ?? err}`);
                }
            } else if (message.type === 'listPineWatches') {
                if (!this._canIssueLiveBackendRequest()) { return; }
                try {
                    const watches = await this.socketClient!.listPineWatches();
                    panel.webview.postMessage({ type: 'pineWatches', watches });
                } catch (err: any) {
                    console.warn(`[LIVE] ListPineWatches failed: ${err?.message ?? err}`);
                }
            } else if (message.type === 'step') {
                await this.stepAndBroadcast();
            } else if (message.type === 'getStatus') {
              if (!this._canIssueLiveBackendRequest()) { return; }
                this._liveReadInFlight = true;
                try {
                    const status = await this.socketClient!.getPC();
                this._handleLiveReadSuccess();
                    const statusMsg: any = { type: 'status', ...status };
                    this._applyBreakpointHitFallback(statusMsg);
                    // When paused, also fetch registers for the debug panel
                    if (status.paused) {
                        try {
                            const regs = await this.socketClient!.getRegisters();
                            statusMsg.registers = regs;
                            // Fetch stack memory (128 bytes from $sp = gpr[29])
                            const sp = regs.gprLo32?.[29] ?? regs.gpr?.[29];
                            if (sp !== undefined) {
                                try {
                                    const stackBuf = await this.socketClient!.readMemory(sp, 512, 'ee');
                                    statusMsg.stackBytes = Array.from(stackBuf);
                                    statusMsg.stackBase = sp;
                                } catch { /* ignore stack read errors */ }
                            }
                        } catch { /* ignore register fetch errors */ }

                        // When a breakpoint was hit, bring the correct panel to front
                        // and broadcast the full status to every live window.
                        const hitAddr = statusMsg.breakpointHitAddress ?? status.pc;
                        this._revealPanelForAddress(hitAddr);
                        this._broadcastFullStatus(statusMsg);
                        // Notify the sidebar so it flips to PAUSED immediately.
                        this.onPauseStateChange?.(true);
                    } else {
                        panel.webview.postMessage(statusMsg);
                    }
                } catch (err: any) {
                    this._handleLiveReadFailure('getStatus', err);
                } finally {
                    this._liveReadInFlight = false;
                }
            } else if (message.type === 'search') {
                await this.handleSearch(panel, message.query, message.mode,
                    baseAddress, totalBytes, region);
            } else if (message.type === 'scanStrings') {
                await this.handleLiveScan('strings', panel, region, baseAddress, totalBytes);
            } else if (message.type === 'scanFunctions') {
                await this.handleLiveScan('functions', panel, region, baseAddress, totalBytes);
            } else if (message.type === 'scanPointers') {
                await this.handleLiveScan('pointers', panel, region, baseAddress, totalBytes);
            } else if (message.type === 'scanFloats') {
                await this.handleLiveScan('floats', panel, region, baseAddress, totalBytes);
            } else if (message.type === 'scanIntegers') {
                await this.handleLiveScan('integers', panel, region, baseAddress, totalBytes);
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
            } else if (message.type === 'getModules') {
                if (!this.socketClient?.isConnected()) {
                    panel.webview.postMessage({ type: 'modules', modules: [] });
                    return;
                }
                try {
                    const modules = await this.socketClient!.getModules();
                    panel.webview.postMessage({ type: 'modules', modules });
                } catch (err: any) {
                    getOutputChannel().appendLine(`[LIVE] Failed to get modules: ${err?.message ?? err}`);
                    panel.webview.postMessage({ type: 'modules', modules: [] });
                }
            } else if (message.type === 'gotoRegion') {
                // The webview could not find the address in its own region.
                // Find which live panel owns the address and instruct it to navigate.
                const addr = message.address as number;
                const physAddr = DisasmEditorProvider._toPhysAddr(addr as number);
                // Prefer a panel in the same region first.  EE RAM sits at
                // physical 0x00000000 and IOP RAM at 0x1C000000, so there is no
                // address overlap; the region preference is kept as a tiebreaker
                // for any future regions that might share a physical range.
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
                        // The bytes field (populated by formatBytesForDisplay) shows bytes in
                        // big-endian (word) order.  Reverse the byte pairs to get the correct
                        // little-endian layout expected by EE memory before writing.
                        const hexStr = message.hexBytes as string;
                        const leHex = (hexStr.match(/.{2}/g) ?? [hexStr]).reverse().join('');
                        await this.socketClient.writeMemory(
                            message.address as number,
                            leHex,
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
                                const buf = await this.socketClient.readMemory(sp, 512, 'ee');
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
            } else if (message.type === 'setPC') {
                // Set the CPU program counter to the requested address, pause the
                // emulator, broadcast the new state to every live panel (so all PC
                // arrows update), reveal the panel that owns the address, and
                // navigate the cursor there.
                if (!this.socketClient?.isConnected()) { return; }
                const addr: number = message.address as number;
                const addrHex = `0x${addr.toString(16).toUpperCase()}`;
                try {
                    // Ensure the emulator is paused before we change the PC.
                    await this.socketClient.pause();
                    // Write the new PC value via the existing SETREG mechanism.
                    await this.socketClient.setRegister('PC', 0, addrHex);
                    // Fetch the fresh status + full register state.
                    const status = await this.socketClient.getPC();
                    const statusMsg: any = { type: 'status', ...status };
                    statusMsg.paused = true;
                    try {
                        const regs = await this.socketClient.getRegisters();
                        statusMsg.registers = regs;
                        const sp = regs.gprLo32?.[29] ?? regs.gpr?.[29] ?? 0;
                        if (sp) {
                            try {
                                const buf = await this.socketClient.readMemory(sp, 512, 'ee');
                                statusMsg.stackBytes = Array.from(buf);
                                statusMsg.stackBase  = sp;
                            } catch { /* ignore stack read errors */ }
                        }
                    } catch { /* ignore register fetch errors */ }
                    // Broadcast the new PC state to ALL live panels so every panel's
                    // PC arrow indicator and register panel update simultaneously.
                    this._broadcastFullStatus(statusMsg);
                    // Reveal and navigate the panel that owns this address.
                    this._revealPanelForAddress(addr);
                    const physAddr = DisasmEditorProvider._toPhysAddr(addr);
                    const target = this._livePanelInfo.find(info =>
                        physAddr >= info.physBaseAddress &&
                        physAddr < info.physBaseAddress + info.totalBytes
                    );
                    if (target) {
                        target.panel.webview.postMessage({ type: 'gotoAddress', address: addr });
                    }
                    // Notify the sidebar so it stays in PAUSED state.
                    this.onPauseStateChange?.(true);
                } catch (err: any) {
                    getOutputChannel().appendLine(`[SETPC] Error: ${err?.message ?? err}`);
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
            } else if (message.type === 'assembleInstr') {
                // Quick assembly for the instruction editor in the edit overlay.
                if (this.socketClient?.isConnected()) {
                    try {
                        const results = await this.socketClient.assemble(message.instruction as string);
                        const first = results?.[0];
                        if (first?.Success && first.Hex) {
                            // Strip the "0x" prefix so patchBytes/saveEditOverlay receive a raw hex string.
                            const hexBytes = first.Hex.replace(/^0x/i, '').toUpperCase();
                            panel.webview.postMessage({ type: 'assembleInstrResult', hexBytes });
                        } else {
                            panel.webview.postMessage({ type: 'assembleInstrResult', hexBytes: null });
                        }
                    } catch {
                        panel.webview.postMessage({ type: 'assembleInstrResult', hexBytes: null });
                    }
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
            } else if (message.type === 'getAddressHistory') {
                const history = this.onGetAddressHistory
                    ? this.onGetAddressHistory(message.address as number)
                    : [];
                if (this.socketClient?.isConnected()) {
                    await Promise.allSettled(history.map(async entry => {
                        if (!entry.disasm && entry.value) {
                            const results = await this.socketClient!.disassemble(entry.value, message.address as number);
                            if (results?.[0]?.Assembly) { entry.disasm = results[0].Assembly; }
                        }
                    }));
                }
                panel.webview.postMessage({ type: 'addressHistoryResult', address: message.address, history });
            } else if (message.type === 'getAddressRefs') {
                await this.handleGetAddressRefs(panel, message.address as number,
                    baseAddress, totalBytes, region, undefined,
                    message.kinds as string[] | undefined);
            } else if (message.type === 'convertCheat') {
                try {
                    const result = await this.socketClient!.convertCheat(
                        message.codes as string,
                        message.direction as 'toRaw' | 'toEngine',
                        message.format as string | undefined);
                    panel.webview.postMessage({ type: 'convertCheatResult', result });
                } catch (err: unknown) {
                    const errMsg = err instanceof Error ? err.message : String(err);
                    panel.webview.postMessage({ type: 'convertCheatResult', error: errMsg });
                }
            } else if (message.type === 'browseSourceDump') {
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
                    filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
                    title: 'Select Source EE RAM Dump (r0001)',
                });
                if (uris?.[0]) {
                    panel.webview.postMessage({ type: 'browseSourceDumpResult', filePath: uris[0].fsPath });
                }
            } else if (message.type === 'browseTargetDump') {
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
                    filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
                    title: 'Select Target EE RAM Dump',
                });
                if (uris?.[0]) {
                    panel.webview.postMessage({ type: 'browseTargetDumpResult', filePath: uris[0].fsPath });
                }
            } else if (message.type === 'portCheats') {
                if (!this.socketClient?.isConnected()) {
                    panel.webview.postMessage({ type: 'portCheatsResult', error: 'Not connected to emulator server' });
                    return;
                }
                try {
                    const { results, log } = await this.socketClient.portCheats(
                        message.codes as string,
                        message.sourcePath as string,
                        message.targetPath as string,
                    );
                    getOutputChannel().info('[Port Cheats]\n' + log);
                    panel.webview.postMessage({ type: 'portCheatsResult', results });
                } catch (err: unknown) {
                    const errMsg = err instanceof Error ? err.message : String(err);
                    getOutputChannel().error('[Port Cheats] ' + errMsg);
                    panel.webview.postMessage({ type: 'portCheatsResult', error: errMsg });
                }
            } else if (message.type === 'browseMc') {
                await this._handleBrowseMc(panel.webview);
            } else if (message.type === 'newMc') {
                await this._handleNewMc(panel.webview);
            } else if (message.type === 'mcList') {
                await this._handleMcList(panel.webview, message.mcPath as string ?? '');
            } else if (message.type === 'mcExport') {
                await this._handleMcExport(panel.webview, message.mcPath as string ?? '', message.saveName as string ?? '');
            } else if (message.type === 'mcImport') {
                await this._handleMcImport(panel.webview, message.mcPath as string ?? '');
            } else if (message.type === 'mcDelete') {
                await this._handleMcDelete(panel.webview, message.mcPath as string ?? '', message.saveName as string ?? '');
            } else if (message.type === 'browseHdd') {
                await this._handleBrowseHdd(panel.webview);
            } else if (message.type === 'newHdd') {
                await this._handleNewHdd(panel.webview);
            } else if (message.type === 'hddList') {
                await this._handleHddList(panel.webview, message.hddPath as string ?? '');
            } else if (message.type === 'hddExport') {
                await this._handleHddExport(panel.webview, message.hddPath as string ?? '', message.partitionId as string ?? '');
            } else if (message.type === 'hddListFiles') {
                await this._handleHddListFiles(panel.webview, message.hddPath as string ?? '', message.partitionId as string ?? '');
            } else if (message.type === 'hddDelete') {
                await this._handleHddDelete(panel.webview, message.hddPath as string ?? '', message.partitionId as string ?? '');
            } else if (message.type === 'scanValue') {
                await this._handleScanValue(panel.webview, message);
            } else if (message.type === 'getStructs') {
                await this._handleGetStructs(panel.webview);
            } else if (message.type === 'addStruct') {
                await this._handleAddStruct(panel.webview, message.name as string, message.baseAddress as string);
            } else if (message.type === 'addStructMember') {
                await this._handleAddStructMember(panel.webview, message.structName as string, message.offset as number, message.memberName as string);
            } else if (message.type === 'readPointerAt') {
                await this._handleReadPointerAt(panel.webview, message.address as string);
            } else if (message.type === 'labelManagerAction') {
                await this._handleLabelManagerAction(panel.webview, message.action as any);
            } else if (message.type === 'openLabelManagerTab') {
                this.openLabelManagerTab(message.address as string | undefined);
            } else if (message.type === 'openMemoryView') {
                await this.onOpenMemoryView?.(message.label as string ?? '');
            } else if (message.type === 'toggleFollowAi') {
                this.setFollowAiEnabled(!this._followAiEnabled);
            } else if (message.type === 'setDataFormat') {
                // Live panel — re-read RAM and re-disassemble the affected page
                // so the new typed rows show up immediately.
                if (!this.socketClient?.isConnected()) { return; }
                try {
                    const gameId = this._currentGameSerial || undefined;
                    await this.socketClient.setDataFormat(
                        message.address as number,
                        message.length as number,
                        message.kind as 'byte' | 'half' | 'word' | 'dword' | 'qword',
                        gameId,
                    );
                } catch (err) {
                    const msg = err instanceof Error ? err.message : String(err);
                    vscode.window.showErrorMessage(`Failed to set data format: ${msg}`);
                    return;
                }
                const refreshStart = (message.startIndex as number | undefined) ?? 0;
                await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, refreshStart);
            } else if (message.type === 'clearDataFormat') {
                if (!this.socketClient?.isConnected()) { return; }
                try {
                    const gameId = this._currentGameSerial || undefined;
                    await this.socketClient.clearDataFormat(
                        message.address as number,
                        message.length as number,
                        gameId,
                    );
                } catch (err) {
                    const msg = err instanceof Error ? err.message : String(err);
                    vscode.window.showErrorMessage(`Failed to clear data format: ${msg}`);
                    return;
                }
                const refreshStart = (message.startIndex as number | undefined) ?? 0;
                await this.sendLiveDisasmPage(panel, region, baseAddress, totalBytes, refreshStart);
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
     * If the backend did not report a breakpoint hit, infer it when the
     * current PC matches a known breakpoint (compare in physical space).
     */
    private _applyBreakpointHitFallback(statusMsg: { paused?: boolean; pc?: number; breakpointHitAddress?: number }): void {
        const existing = (statusMsg as any).breakpointHitAddress;
        if (existing !== undefined && existing !== null) { return; }
        if (existing === null) { delete (statusMsg as any).breakpointHitAddress; }
        if (!statusMsg.paused) { return; }
        const pc = statusMsg.pc;
        if (pc === undefined || pc === null) { return; }
        const candidates = [
            pc >>> 0,
            (pc - 4) >>> 0,
            (pc - 8) >>> 0,
        ];
        for (const cand of candidates) {
            const candPhys = DisasmEditorProvider._toPhysAddr(cand);
            for (const bp of this._knownBreakpoints) {
                if (DisasmEditorProvider._toPhysAddr(bp) === candPhys) {
                    statusMsg.breakpointHitAddress = cand;
                    return;
                }
            }
        }
    }

    /**
     * Find the live panel whose address range contains `addr` and reveal it so
     * the user immediately sees the paused location.  Strips KSEG0/KSEG1 bits
     * and the uncached-user EE mirror prefix before comparing.
     * Panel base addresses are pre-computed as physBaseAddress at creation time.
     */
    private _revealPanelForAddress(addr: number): void {
        const physAddr = DisasmEditorProvider._toPhysAddr(addr);

        for (const info of this._livePanelInfo) {
            if (physAddr >= info.physBaseAddress && physAddr < info.physBaseAddress + info.totalBytes) {
                info.panel.reveal(vscode.ViewColumn.One, false);
                return;
            }
        }
    }

    /**
     * Convert a virtual/logical EE address to its physical equivalent.
     *   KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip top 3 bits.
     *   EE RAM uncached mirror (0x20100000–0x21FFFFFF): subtract 0x20000000.
     *   EE RAM uncached+accelerated (0x30100000–0x31FFFFFF): subtract 0x30000000.
     *   All other addresses (physical or special like scratchpad) pass through.
     */
    private static _toPhysAddr(addr: number): number {
        const v = addr >>> 0;
        if (v >= 0x80000000 && v <= 0xBFFFFFFF) return (v & 0x1FFFFFFF) >>> 0;
        if (v >= 0x20100000 && v <= 0x21FFFFFF) return (v - 0x20000000) >>> 0;
        if (v >= 0x30100000 && v <= 0x31FFFFFF) return (v - 0x30000000) >>> 0;
        return v;
    }

    private _isLiveReadBackoffActive(): boolean {
      return Date.now() < this._liveReadBackoffUntil;
    }

    private _canIssueLiveBackendRequest(): boolean {
      return !!this.socketClient && this.socketClient.isConnected() && !this._isLiveReadBackoffActive() && !this._liveReadInFlight;
    }

    /**
     * Returns true when a live read cannot proceed because the socket is
     * simply busy with another in-flight request (as opposed to the backend
     * being unavailable).  Callers should silently skip the refresh cycle
     * instead of pushing placeholder zero-rows to the webview — sending
     * placeholders in this case is the root cause of the memory view
     * "flickering" between real data and zeroes.
     */
    private _isLiveReadBusy(): boolean {
      return !!this.socketClient && this.socketClient.isConnected() && !this._isLiveReadBackoffActive() && this._liveReadInFlight;
    }

    private _buildPlaceholderRows(baseAddress: number, startOffset: number, readLength: number): DisassembledRow[] {
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
      return rows;
    }

    private _postUnavailableHexView(
      panel: vscode.WebviewPanel,
      baseAddress: number,
      highlightAddr: number,
      highlightLength: number
    ): void {
      const alignedAddr = Math.max(baseAddress, ((highlightAddr & ~0xf) >>> 0) - 0x10);
      panel.webview.postMessage({
        type: 'hexview',
        data: new Array<number>(DisasmEditorProvider.HEX_VIEW_SIZE).fill(0),
        offset: alignedAddr,
        highlight_addr: highlightAddr,
        highlight_length: highlightLength,
      });
    }

    private _isLiveReadConnectionFailure(err: unknown): boolean {
      const msg = String((err as any)?.message ?? err ?? '').toLowerCase();
      return msg.includes('actively refused')
        || msg.includes('client not ready')
        || msg.includes('readmem error')
        || msg.includes('connection refused')
        || msg.includes('command timeout')
        || msg.includes('getpc error')
        || msg.includes('pine getpc error')
        || msg.includes('not connected to server')
        || msg.includes('disconnected')
        || msg.includes('no live memory backend')
        || msg.includes('no scheduler attached');
    }

    private _handleLiveReadSuccess(): void {
      if (!this._liveReadFailureActive) { return; }
      this._liveReadFailureActive = false;
      this._liveReadFailureMessage = '';
      // Intentionally do NOT clear _liveReadBackoffUntil here.  Stale in-flight
      // requests that happen to succeed can call this while the backoff window is
      // still active.  Clearing the backoff at that point would immediately allow
      // new requests, which would also fail, causing the rapid
      // "unavailable → recovered → unavailable" oscillation seen in the logs.
      // The backoff expires on its own; _liveReadBackoffUntil is only reset to 0
      // in _handleLiveReadFailure (refreshed on each failure) and on socket reconnect.
      getOutputChannel().appendLine('[LIVE] Emulator memory reads recovered; resuming normal refresh.');
    }

    private _handleLiveReadFailure(context: string, err: unknown): void {
      const msg = String((err as any)?.message ?? err ?? 'Unknown live read failure');
      if (!this._isLiveReadConnectionFailure(err)) {
        console.error(`[LIVE] ${context} failed: ${msg}`);
        return;
      }

      const now = Date.now();
      this._liveReadBackoffUntil = now + DisasmEditorProvider.LIVE_READ_BACKOFF_MS;
      if (!this._liveReadFailureActive || this._liveReadFailureMessage !== msg) {
        this._liveReadFailureActive = true;
        this._liveReadFailureMessage = msg;
        getOutputChannel().appendLine(
          `[LIVE] Emulator memory reads unavailable; backing off for ${DisasmEditorProvider.LIVE_READ_BACKOFF_MS}ms. ${msg}`
        );
        console.warn(`[LIVE] ${context} unavailable: ${msg}`);
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

        // When the socket is merely busy with another in-flight read, silently
        // skip this refresh cycle.  Pushing placeholder zero-rows while the
        // previous read is still pending causes the visible "flickering" between
        // real memory and zeroes.
        if (this._isLiveReadBusy()) { return; }

        if (!this._canIssueLiveBackendRequest()) {
          panel.webview.postMessage({
            type: 'disasmPage',
            rows: this._buildPlaceholderRows(baseAddress, startOffset, readLength),
            startIndex,
          });
            return;
        }

        const socketClient = this.socketClient!;
        const startAddr = baseAddress + startOffset;
        this._liveReadInFlight = true;
        try {
            const buffer = await socketClient.readMemory(startAddr, readLength, region);
            const rows   = await this.disassembleWithBackend(startAddr, buffer);
          this._handleLiveReadSuccess();
            panel.webview.postMessage({ type: 'disasmPage', rows, startIndex });
        } catch (err: any) {
          this._handleLiveReadFailure(`sendLiveDisasmPage at 0x${startAddr.toString(16)}`, err);
          panel.webview.postMessage({
            type: 'disasmPage',
            rows: this._buildPlaceholderRows(baseAddress, startOffset, readLength),
            startIndex,
          });
        } finally {
          this._liveReadInFlight = false;
        }
    }

    private async sendLiveHexView(
        panel: vscode.WebviewPanel,
        region: string,
        baseAddress: number,
        highlightAddr: number,
        highlightLength: number
    ): Promise<void> {
      // Silently skip when a read is already in-flight — see sendLiveDisasmPage.
      if (this._isLiveReadBusy()) { return; }

      if (!this._canIssueLiveBackendRequest()) {
        this._postUnavailableHexView(panel, baseAddress, highlightAddr, highlightLength);
        return;
      }

        const socketClient = this.socketClient!;
        const alignedAddr = Math.max(baseAddress, ((highlightAddr & ~0xf) >>> 0) - 0x10);
        this._liveReadInFlight = true;
        try {
            const buffer = await socketClient.readMemory(
                alignedAddr, DisasmEditorProvider.HEX_VIEW_SIZE, region);
        this._handleLiveReadSuccess();
            panel.webview.postMessage({
                type: 'hexview',
                data: Array.from(buffer),
                offset: alignedAddr,
                highlight_addr:   highlightAddr,
                highlight_length: highlightLength,
            });
        } catch (err: any) {
        this._handleLiveReadFailure(`sendLiveHexView at 0x${alignedAddr.toString(16)}`, err);
        this._postUnavailableHexView(panel, baseAddress, highlightAddr, highlightLength);
        } finally {
          this._liveReadInFlight = false;
        }
    }

  /**
   * Unified search — works for both live and file-based panels.
   *
   * File-based panels: bytes/string modes are implemented in TypeScript
   * (no C# backend required). text/regex modes use the C# backend when
   * connected, otherwise fall back to searching only the address and raw-byte
   * fields so that search always produces some results without the server.
   *
   * Live panels: always delegate to the C# backend (which reads live memory).
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
    if (fileData) {
      // File-based panel: prefer the C# backend for text/regex/refs (full disassembly
      // search) but fall back to pure TypeScript so search always works.
      if ((mode === 'text' || mode === 'regex' || mode === 'refs') && this.socketClient?.isConnected()) {
        try {
          const matches = await this.socketClient.search(
            baseAddress, length, query, mode, undefined, fileData
          );
          panel.webview.postMessage({ type: 'searchResults', matches });
          return;
        } catch (err: any) {
          console.warn(`[SEARCH] C# backend search failed, falling back to TypeScript: ${err?.message ?? err}`);
        }
      }
      // TypeScript implementation: always available for all modes.
      const matches = this.searchFileData(fileData, baseAddress, query, mode);
      panel.webview.postMessage({ type: 'searchResults', matches });
      return;
    }

    // Live panel: delegate to C# backend.
    if (!this.socketClient?.isConnected()) {
      panel.webview.postMessage({ type: 'searchResults', matches: [] });
      return;
    }
    try {
      const matches = await this.socketClient.search(
        baseAddress, length, query, mode, region
      );
      panel.webview.postMessage({ type: 'searchResults', matches });
    } catch (err: any) {
      console.error(`[SEARCH] Failed: ${err?.message ?? err}`);
      panel.webview.postMessage({ type: 'searchResults', matches: [] });
    }
  }

  /**
   * Handle a `getAddressRefs` message from the webview.
   * Runs the bridge reference scanner for `targetAddress` and returns structured
   * results via an `addressRefsResult` message.
   */
  private async handleGetAddressRefs(
    panel: vscode.WebviewPanel,
    targetAddress: number,
    baseAddress: number,
    length: number,
    region?: string,
    fileData?: Buffer,
    kinds?: string[]
  ): Promise<void> {
    try {
      const selectedKinds = kinds && kinds.length > 0
        ? kinds
        : ['controlFlow', 'resolved', 'pointer', 'stringPattern'];
      let refs: ReferenceEntry[] = [];

      if (fileData) {
        if (this.socketClient?.isConnected()) {
          try {
            refs = await this.socketClient.scanReferences(
              baseAddress, fileData.length, targetAddress, selectedKinds, undefined, fileData);
          } catch {
            refs = [];
          }
        }
      } else if (this.socketClient?.isConnected()) {
        try {
          refs = await this.socketClient.scanReferences(
            baseAddress, length, targetAddress, selectedKinds, region);
        } catch {
          refs = [];
        }
      }

      panel.webview.postMessage({ type: 'addressRefsResult', refs });
    } catch (err: any) {
      console.error(`[REFS] handleGetAddressRefs failed: ${err?.message ?? err}`);
      panel.webview.postMessage({ type: 'addressRefsResult', refs: [] });
    }
  }

  private async handleGetAddressRefsLegacy(
    panel: vscode.WebviewPanel,
    targetAddress: number,
    baseAddress: number,
    length: number,
    region?: string,
    fileData?: Buffer
  ): Promise<void> {
    try {
      const addrHex = targetAddress.toString(16).toUpperCase().padStart(8, '0');
      let matches: Array<{ instructionIndex: number; address: number }> = [];

      if (fileData) {
        // File panel: use C# backend for refs search when connected, else return empty.
        if (this.socketClient?.isConnected()) {
          try {
            matches = await this.socketClient.search(baseAddress, length, addrHex, 'refs', undefined, fileData);
          } catch {
            matches = [];
          }
        }
      } else if (this.socketClient?.isConnected()) {
        // Live panel: delegate to C# backend.
        try {
          matches = await this.socketClient.search(baseAddress, length, addrHex, 'refs', region);
        } catch {
          matches = [];
        }
      }

      // Enrich each match with disassembled instruction text.
      const refs: Array<{ address: string; instruction: string }> = await Promise.all(
        matches.map(async m => {
          const matchAddrHex = m.address.toString(16).toUpperCase().padStart(8, '0');
          let instruction = '';
          try {
            if (fileData) {
              // Extract 4 bytes from fileData at the match offset.
              const offset = m.address - baseAddress;
              if (offset >= 0 && offset + 4 <= fileData.length) {
                const word = fileData.readUInt32LE(offset);
                const wordHex = `0x${word.toString(16).padStart(8, '0')}`;
                const results = await this.socketClient!.disassemble(wordHex, m.address);
                if (results?.[0]?.Assembly) { instruction = results[0].Assembly; }
              }
            } else if (this.socketClient?.isConnected()) {
              // Live panel: read 4 bytes from emulator memory.
              const buf = await this.socketClient!.readMemory(m.address, 4, region ?? 'ee');
              const word = buf.readUInt32LE(0);
              const wordHex = `0x${word.toString(16).padStart(8, '0')}`;
              const results = await this.socketClient!.disassemble(wordHex, m.address);
              if (results?.[0]?.Assembly) { instruction = results[0].Assembly; }
            }
          } catch { /* ignore enrichment failures — address is still shown */ }
          return { address: matchAddrHex, instruction };
        })
      );

      panel.webview.postMessage({ type: 'addressRefsResult', refs });
    } catch (err: any) {
      console.error(`[REFS] handleGetAddressRefs failed: ${err?.message ?? err}`);
      panel.webview.postMessage({ type: 'addressRefsResult', refs: [] });
    }
  }


  private parseBytePattern(pattern: string): number[] | null {
    const cleaned = pattern.replace(/0x/gi, '').replace(/[,\s]+/g, ' ').trim();
    if (!cleaned) { return null; }
    const parts = cleaned.split(/\s+/);
    const result: number[] = [];
    for (const p of parts) {
      if (!/^[0-9a-fA-F]{1,2}$/.test(p)) { return null; }
      result.push(parseInt(p, 16));
    }
    return result.length > 0 ? result : null;
  }

  /**
   * Pure TypeScript implementation of file-based search — no C# backend needed.
   *
   * • bytes  — hex byte-pattern match in the raw binary.
   * • string — UTF-8 byte-sequence match in the raw binary.
   * • text   — case-insensitive substring search across the address and raw-byte
   *            hex fields of each 4-byte instruction (partial; full opcode/args
   *            search is only available when the C# backend is connected).
   * • regex  — same as text but uses a RegExp.
   */
  private searchFileData(
    fileData: Buffer,
    startAddress: number,
    query: string,
    mode: string
  ): SearchMatch[] {
    const matches: SearchMatch[] = [];

    if (mode === 'bytes') {
      const pattern = this.parseBytePattern(query);
      if (!pattern || pattern.length === 0) { return []; }
      for (let i = 0; i <= fileData.length - pattern.length; i++) {
        let found = true;
        for (let j = 0; j < pattern.length; j++) {
          if (fileData[i + j] !== pattern[j]) { found = false; break; }
        }
        if (found) {
          matches.push({
            instructionIndex: Math.floor(i / 4),
            address: startAddress + i,
            field: 'bytes',
            start: 0,
            // Display length of space-separated hex bytes: "AB CD EF" = n*3-1 chars
            length: pattern.length * 3 - 1,
          });
          i += pattern.length - 1;
        }
      }
      return matches;
    }

    if (mode === 'string') {
      const strBytes = Buffer.from(query, 'utf8');
      if (strBytes.length === 0) { return []; }
      for (let i = 0; i <= fileData.length - strBytes.length; i++) {
        let found = true;
        for (let j = 0; j < strBytes.length; j++) {
          if (fileData[i + j] !== strBytes[j]) { found = false; break; }
        }
        if (found) {
          matches.push({
            instructionIndex: Math.floor(i / 4),
            address: startAddress + i,
            field: 'bytes',
            start: 0,
            // Display length of space-separated hex bytes: "AB CD EF" = n*3-1 chars
            length: strBytes.length * 3 - 1,
          });
          i += strBytes.length - 1;
        }
      }
      return matches;
    }

    // refs — scan for MIPS instructions that reference the target address.
    if (mode === 'refs') {
      const cleaned = query.replace(/^0x/i, '');
      const targetAddr = parseInt(cleaned, 16);
      if (isNaN(targetAddr)) { return []; }

      const signExt16 = (v: number): number => ((v & 0x8000) !== 0) ? (v | 0xFFFF0000) : v;

      // Per-register LUI tracking (mirrors BuildCodeReferencedAddresses in C#).
      const luiSlot = new Int32Array(32).fill(-1);
      const luiImm  = new Uint32Array(32);

      for (let i = 0; i + 3 < fileData.length; i += 4) {
        const word      = fileData.readUInt32LE(i);
        const opcode    = (word >>> 26) & 0x3F;
        const rs        = (word >>> 21) & 0x1F;
        const rt        = (word >>> 16) & 0x1F;
        const instrAddr = (startAddress + i) >>> 0;

        // J / JAL (opcode 2/3)
        if (opcode === 0x02 || opcode === 0x03) {
          const target = (((instrAddr + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)) >>> 0;
          if (target === (targetAddr >>> 0)) {
            matches.push({ instructionIndex: Math.floor(i / 4), address: instrAddr, field: 'op', start: 0, length: 3 });
          }
          if (opcode === 0x03) { luiSlot[31] = -1; } // JAL writes $ra
          continue;
        }

        // I-type branches: BEQ/BNE/BLEZ/BGTZ (4-7) + BEQL/BNEL/BLEZL/BGTZL (0x14-0x17)
        if ((opcode >= 0x04 && opcode <= 0x07) || (opcode >= 0x14 && opcode <= 0x17)) {
          const target = (instrAddr + 4 + (signExt16(word & 0xFFFF) << 2)) >>> 0;
          if (target === (targetAddr >>> 0)) {
            matches.push({ instructionIndex: Math.floor(i / 4), address: instrAddr, field: 'op', start: 0, length: 3 });
          }
          continue;
        }

        // REGIMM (opcode 0x01): BLTZ/BGEZ/BLTZL/BGEZL/BLTZAL/BGEZAL/BLTZALL/BGEZALL
        if (opcode === 0x01) {
          const isBranch = rt === 0x00 || rt === 0x01 || rt === 0x02 || rt === 0x03
                        || rt === 0x10 || rt === 0x11 || rt === 0x12 || rt === 0x13;
          if (isBranch) {
            const target = (instrAddr + 4 + (signExt16(word & 0xFFFF) << 2)) >>> 0;
            if (target === (targetAddr >>> 0)) {
              matches.push({ instructionIndex: Math.floor(i / 4), address: instrAddr, field: 'op', start: 0, length: 4 });
            }
          }
          // BLTZAL/BGEZAL/BLTZALL/BGEZALL write $ra
          if (rt === 0x10 || rt === 0x11 || rt === 0x12 || rt === 0x13) { luiSlot[31] = -1; }
          continue;
        }

        // COP1 branches (opcode 0x11, rs=8): BC1F/BC1T/BC1FL/BC1TL
        if (opcode === 0x11 && rs === 0x08) {
          const target = (instrAddr + 4 + (signExt16(word & 0xFFFF) << 2)) >>> 0;
          if (target === (targetAddr >>> 0)) {
            matches.push({ instructionIndex: Math.floor(i / 4), address: instrAddr, field: 'op', start: 0, length: 4 });
          }
          continue;
        }

        // LUI (opcode 0x0F)
        if (opcode === 0x0F) {
          if (rt !== 0) { luiSlot[rt] = i; luiImm[rt] = word & 0xFFFF; }
          continue;
        }

        // ADDIU (opcode 0x09): detect LUI+ADDIU pair
        if (opcode === 0x09) {
          if (rs !== 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= 128) {
            const combined = ((luiImm[rs] << 16) + signExt16(word & 0xFFFF)) >>> 0;
            if (combined === (targetAddr >>> 0)) {
              const luiByteOffset = luiSlot[rs];
              matches.push({ instructionIndex: Math.floor(luiByteOffset / 4), address: (startAddress + luiByteOffset) >>> 0, field: 'args', start: 0, length: 100 });
              matches.push({ instructionIndex: Math.floor(i / 4),             address: instrAddr,                             field: 'args', start: 0, length: 100 });
            }
          }
          if (rt !== 0) { luiSlot[rt] = -1; }
          continue;
        }

        // ORI (opcode 0x0D): detect LUI+ORI pair
        if (opcode === 0x0D) {
          if (rs !== 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= 128) {
            const combined = ((luiImm[rs] << 16) | (word & 0xFFFF)) >>> 0;
            if (combined === (targetAddr >>> 0)) {
              const luiByteOffset = luiSlot[rs];
              matches.push({ instructionIndex: Math.floor(luiByteOffset / 4), address: (startAddress + luiByteOffset) >>> 0, field: 'args', start: 0, length: 100 });
              matches.push({ instructionIndex: Math.floor(i / 4),             address: instrAddr,                             field: 'args', start: 0, length: 100 });
            }
          }
          if (rt !== 0) { luiSlot[rt] = -1; }
          continue;
        }

        // Loads (0x20-0x27): detect LUI+load pair
        if (opcode >= 0x20 && opcode <= 0x27) {
          if (rs !== 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= 128) {
            const combined = ((luiImm[rs] << 16) + signExt16(word & 0xFFFF)) >>> 0;
            if (combined === (targetAddr >>> 0)) {
              const luiByteOffset = luiSlot[rs];
              matches.push({ instructionIndex: Math.floor(luiByteOffset / 4), address: (startAddress + luiByteOffset) >>> 0, field: 'args', start: 0, length: 100 });
              matches.push({ instructionIndex: Math.floor(i / 4),             address: instrAddr,                             field: 'args', start: 0, length: 100 });
            }
          }
          if (rt !== 0) { luiSlot[rt] = -1; }
          continue;
        }

        // Stores (0x28-0x2F): detect LUI+store pair
        if (opcode >= 0x28 && opcode <= 0x2F) {
          if (rs !== 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= 128) {
            const combined = ((luiImm[rs] << 16) + signExt16(word & 0xFFFF)) >>> 0;
            if (combined === (targetAddr >>> 0)) {
              const luiByteOffset = luiSlot[rs];
              matches.push({ instructionIndex: Math.floor(luiByteOffset / 4), address: (startAddress + luiByteOffset) >>> 0, field: 'args', start: 0, length: 100 });
              matches.push({ instructionIndex: Math.floor(i / 4),             address: instrAddr,                             field: 'args', start: 0, length: 100 });
            }
          }
          // Stores don't write $rt — no LUI slot invalidation.
          continue;
        }

        // Register-write invalidation for everything else.
        if (opcode === 0x00) { // SPECIAL (R-type) writes $rd
          const rd = (word >>> 11) & 0x1F;
          if (rd !== 0) { luiSlot[rd] = -1; }
          continue;
        }
        // All other I-type opcodes write $rt — invalidate its LUI slot.
        if (opcode !== 0x02
         && opcode !== 0x04 && opcode !== 0x05
         && opcode !== 0x06 && opcode !== 0x07
         && opcode !== 0x14 && opcode !== 0x15
         && opcode !== 0x16 && opcode !== 0x17
         && opcode !== 0x1C) {
          if (rt !== 0) { luiSlot[rt] = -1; }
        }
      }

      return matches;
    }

    // text / regex — search the address and raw-byte hex fields.
    const useRegex = mode === 'regex';
    let rx: RegExp | null = null;
    if (useRegex) {
      try { rx = new RegExp(query, 'gi'); } catch { return []; }
    }
    const queryLower = query.toLowerCase();

    for (let i = 0; i + 3 < fileData.length; i += 4) {
      const addr = startAddress + i;
      const idx  = Math.floor(i / 4);

      const addrStr  = addr.toString(16).toUpperCase().padStart(8, '0');
      const bytesStr = [
        fileData[i + 3].toString(16).toUpperCase().padStart(2, '0'),
        fileData[i + 2].toString(16).toUpperCase().padStart(2, '0'),
        fileData[i + 1].toString(16).toUpperCase().padStart(2, '0'),
        fileData[i + 0].toString(16).toUpperCase().padStart(2, '0'),
      ].join('');

      const fields: Array<{ name: string; text: string }> = [
        { name: 'addr',  text: addrStr  },
        { name: 'bytes', text: bytesStr },
      ];

      for (const { name, text } of fields) {
        if (useRegex && rx) {
          rx.lastIndex = 0;
          let m: RegExpExecArray | null;
          while ((m = rx.exec(text)) !== null) {
            matches.push({ instructionIndex: idx, address: addr, field: name, start: m.index, length: m[0].length });
            if (m[0].length === 0) { rx.lastIndex++; }
          }
        } else {
          const textLower = text.toLowerCase();
          let fi = 0;
          while ((fi = textLower.indexOf(queryLower, fi)) >= 0) {
            matches.push({ instructionIndex: idx, address: addr, field: name, start: fi, length: query.length });
            fi += query.length;
          }
        }
      }
    }

    return matches;
  }

  /**
   * Scan a live memory region for strings, functions, pointers, floats, or
   * integers using the C# backend.
   * Results are cached per panel; subsequent scans always re-fetch fresh data
   * and update the cache so the webview can restore them after a page reload.
   * Posts the corresponding `*Updated` message to the webview.
   */
  private async handleLiveScan(
    kind: 'strings' | 'functions' | 'pointers' | 'floats' | 'integers',
    panel: vscode.WebviewPanel,
    region: string,
    baseAddress: number,
    totalBytes: number
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      const cache = this._panelScanCache.get(panel) ?? {};
      if (kind === 'strings') {
        const strings = await this.socketClient.scanLiveStrings(baseAddress, totalBytes, region);
        cache.strings = strings;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'stringsUpdated', strings });
      } else if (kind === 'functions') {
        const functions = await this.socketClient.scanLiveFunctions(baseAddress, totalBytes, region);
        cache.functions = functions;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'functionsUpdated', functions });
      } else if (kind === 'pointers') {
        const pointers = await this.socketClient.scanLivePointers(baseAddress, totalBytes, region);
        cache.pointers = pointers;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'pointersUpdated', pointers });
      } else if (kind === 'floats') {
        const floats = await this.socketClient.scanLiveFloats(baseAddress, totalBytes, region);
        cache.floats = floats;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'floatsUpdated', floats });
      } else {
        const integers = await this.socketClient.scanLiveIntegers(baseAddress, totalBytes, region);
        cache.integers = integers;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'integersUpdated', integers });
      }
    } catch (err: any) {
      console.error(`[SYMBOLS] LiveScan(${kind}) failed: ${err?.message ?? err}`);
      // Send empty result so the webview re-enables the scan button
      if (kind === 'strings') {
        panel.webview.postMessage({ type: 'stringsUpdated', strings: [] });
      } else if (kind === 'functions') {
        panel.webview.postMessage({ type: 'functionsUpdated', functions: [] });
      } else if (kind === 'pointers') {
        panel.webview.postMessage({ type: 'pointersUpdated', pointers: [] });
      } else if (kind === 'floats') {
        panel.webview.postMessage({ type: 'floatsUpdated', floats: [] });
      } else {
        panel.webview.postMessage({ type: 'integersUpdated', integers: [] });
      }
    }
  }

  /**
   * Scan file binary data for strings, functions, pointers, floats, or integers
   * using the C# backend.
   * Results are cached per panel; subsequent scans always re-fetch fresh data
   * and update the cache.
   * Posts the corresponding `*Updated` message to the webview.
   */
  private async handleFileScan(
    kind: 'strings' | 'functions' | 'pointers' | 'floats' | 'integers',
    panel: vscode.WebviewPanel,
    fileData: Buffer,
    baseAddress: number
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      const cache = this._panelScanCache.get(panel) ?? {};
      if (kind === 'strings') {
        const strings = await this.socketClient.getStrings(fileData, baseAddress);
        cache.strings = strings;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'stringsUpdated', strings });
      } else if (kind === 'functions') {
        const functions = await this.socketClient.getFunctions(fileData, baseAddress);
        cache.functions = functions;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'functionsUpdated', functions });
      } else if (kind === 'pointers') {
        const pointers = await this.socketClient.getPointers(fileData, baseAddress);
        cache.pointers = pointers;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'pointersUpdated', pointers });
      } else if (kind === 'floats') {
        const floats = await this.socketClient.getFloats(fileData, baseAddress);
        cache.floats = floats;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'floatsUpdated', floats });
      } else {
        const integers = await this.socketClient.getIntegers(fileData, baseAddress);
        cache.integers = integers;
        this._panelScanCache.set(panel, cache);
        panel.webview.postMessage({ type: 'integersUpdated', integers });
      }
    } catch (err: any) {
      console.error(`[SYMBOLS] FileScan(${kind}) failed: ${err?.message ?? err}`);
      if (kind === 'strings') {
        panel.webview.postMessage({ type: 'stringsUpdated', strings: [] });
      } else if (kind === 'functions') {
        panel.webview.postMessage({ type: 'functionsUpdated', functions: [] });
      } else if (kind === 'pointers') {
        panel.webview.postMessage({ type: 'pointersUpdated', pointers: [] });
      } else if (kind === 'floats') {
        panel.webview.postMessage({ type: 'floatsUpdated', floats: [] });
      } else {
        panel.webview.postMessage({ type: 'integersUpdated', integers: [] });
      }
    }
  }

  /**
   * Collect labels, strings, and functions from the backend and post them
   * back to the webview as a single `symbols` message.
   * For live panels that have no backing fileData, pass `undefined`; in that
   * case strings and functions are returned empty.
   * Any previously cached scan results (pointers, floats, integers) are replayed
   * so the webview restores its state without requiring a re-scan.
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

    // Merge game-specific labels with server labels
    if (this._gameLabels.length > 0) {
      labels = this._mergeLabels(labels, this._gameLabels);
    }
    // Merge struct/pointer manager labels so they appear in the Labels tab alongside
    // user and server labels.
    if (this._pointerLabels.length > 0) {
      labels = this._mergeLabels(labels, this._pointerLabels);
    }

    panel.webview.postMessage({ type: 'symbols', labels, strings, functions });

    // Replay any cached scanner results (pointers, floats, integers) so the
    // webview restores its previous scan state without requiring a re-scan.
    const cache = this._panelScanCache.get(panel);
    if (cache) {
      if (cache.strings   !== undefined) { panel.webview.postMessage({ type: 'stringsUpdated',   strings:   cache.strings   }); }
      if (cache.functions !== undefined) { panel.webview.postMessage({ type: 'functionsUpdated', functions: cache.functions }); }
      if (cache.pointers  !== undefined) { panel.webview.postMessage({ type: 'pointersUpdated',  pointers:  cache.pointers  }); }
      if (cache.floats    !== undefined) { panel.webview.postMessage({ type: 'floatsUpdated',    floats:    cache.floats    }); }
      if (cache.integers  !== undefined) { panel.webview.postMessage({ type: 'integersUpdated',  integers:  cache.integers  }); }
    }
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
   * Add (or update) a user-created label.
   *
   * When a game is active (ISO name and workspace root are set), the label is
   * saved to the per-game `snapshots/<isoName>/labels.json` file on disk so
   * it is scoped to that game and does not pollute the global server database.
   * When no game context is available, the label falls back to the server's
   * global database (legacy behaviour, e.g. binary file panels).
   *
   * After saving, the fresh merged label list is broadcast to the panel.
   */
  private async handleAddLabel(
    panel: vscode.WebviewPanel,
    address: number,
    labelName: string,
    comment: string | undefined
  ): Promise<void> {
    try {
      const ctx = this._labelManagerCtx();
      if (ctx && this.socketClient?.isConnected()) {
        // Primary path: route through the DB-backed label manager so that
        // inline label edits and the Label Manager tab share the same database.
        const { workspaceDir, gameDiscId } = ctx;
        const setName = this._activeSetName ?? gameDiscId;
        const addrHex = `0x${address.toString(16).toUpperCase().padStart(8, '0')}`;
        const updatedSet = await this.socketClient.addLabelEntry(workspaceDir, gameDiscId, setName, {
          address: addrHex,
          name: labelName,
          comment: comment,
        });
        // Convert set entries to the LabelEntry format expected by the webview.
        const setLabels: LabelEntry[] = updatedSet.entries
          .filter(e => e.name && !e.hidden)
          .map(e => ({ address: e.address, label: e.name, comment: e.comment }));
        // Broadcast merged labels so the newly saved entry is visible immediately.
        // Merge order: server global < set entries (set entries take priority).
        let labels = await this.socketClient.getLabels().catch(() => [] as LabelEntry[]);
        labels = this._mergeLabels(labels, setLabels);
        labels = this._mergeLabels(labels, this._gameLabels);
        if (this._pointerLabels.length > 0) { labels = this._mergeLabels(labels, this._pointerLabels); }
        panel.webview.postMessage({ type: 'labelsUpdated', labels });
        return;
      } else if (this._gameLabelsPath()) {
        // Offline fallback: socket not connected — save to per-game labels.json.
        this._saveGameLabel(address, labelName, comment);
      } else if (this.socketClient?.isConnected()) {
        // No game context: use legacy global command.
        await this.socketClient.addLabel(address, labelName, comment);
      } else {
        return;
      }

      let labels = this.socketClient?.isConnected()
        ? await this.socketClient.getLabels().catch(() => [] as LabelEntry[])
        : [];
      labels = this._mergeLabels(labels, this._gameLabels);
      if (this._pointerLabels.length > 0) { labels = this._mergeLabels(labels, this._pointerLabels); }
      panel.webview.postMessage({ type: 'labelsUpdated', labels });
    } catch (err: any) {
      console.error(`[SYMBOLS] AddLabel failed: ${err?.message ?? err}`);
    }
  }

  /**
   * Delete a user-created label.
   *
   * Mirrors the per-game / global fallback logic of handleAddLabel.
   */
  private async handleDeleteLabel(
    panel: vscode.WebviewPanel,
    address: number
  ): Promise<void> {
    try {
      const ctx = this._labelManagerCtx();
      if (ctx && this.socketClient?.isConnected()) {
        // Primary path: route through the DB-backed label manager so that
        // inline label deletes and the Label Manager tab share the same database.
        const { workspaceDir, gameDiscId } = ctx;
        const setName = this._activeSetName ?? gameDiscId;
        const addrHex = `0x${address.toString(16).toUpperCase().padStart(8, '0')}`;
        const updatedSet = await this.socketClient.deleteLabelEntry(workspaceDir, gameDiscId, setName, addrHex);
        // Convert set entries to the LabelEntry format expected by the webview.
        const setLabels: LabelEntry[] = updatedSet.entries
          .filter(e => e.name && !e.hidden)
          .map(e => ({ address: e.address, label: e.name, comment: e.comment }));
        let labels = await this.socketClient.getLabels().catch(() => [] as LabelEntry[]);
        labels = this._mergeLabels(labels, setLabels);
        labels = this._mergeLabels(labels, this._gameLabels);
        if (this._pointerLabels.length > 0) { labels = this._mergeLabels(labels, this._pointerLabels); }
        panel.webview.postMessage({ type: 'labelsUpdated', labels });
        return;
      } else if (this._gameLabelsPath()) {
        // Offline fallback: socket not connected — remove from per-game labels.json.
        this._deleteGameLabel(address);
      } else if (this.socketClient?.isConnected()) {
        // No game context: use legacy global command.
        await this.socketClient.deleteLabel(address);
      } else {
        return;
      }

      let labels = this.socketClient?.isConnected()
        ? await this.socketClient.getLabels().catch(() => [] as LabelEntry[])
        : [];
      labels = this._mergeLabels(labels, this._gameLabels);
      if (this._pointerLabels.length > 0) { labels = this._mergeLabels(labels, this._pointerLabels); }
      panel.webview.postMessage({ type: 'labelsUpdated', labels });
    } catch (err: any) {
      console.error(`[SYMBOLS] DeleteLabel failed: ${err?.message ?? err}`);
    }
  }

  /**
   * Persist a data-format override (.byte/.half/.word/.dword/.qword) for the
   * requested span, then re-disassemble the surrounding page so the user sees
   * the change immediately.
   *
   * <p>
   * The override is keyed by the current game serial; when no game is
   * selected, the bridge stores it with a null GameDiscId so the override is
   * still persisted for the duration of the project file.
   * </p>
   */
  private async _handleSetDataFormat(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    address: number,
    length: number,
    kind: 'byte' | 'half' | 'word' | 'dword' | 'qword',
    startIndex?: number,
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      vscode.window.showErrorMessage('Not connected — cannot persist data-format override.');
      return;
    }
    try {
      const gameId = this._currentGameSerial || undefined;
      await this.socketClient.setDataFormat(address, length, kind, gameId);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      vscode.window.showErrorMessage(`Failed to set data format: ${msg}`);
      return;
    }
    // Refresh the affected page so the new typed rows show up.
    const pageStart = this._pageStartForAddress(address);
    await this.sendDisasmPage(webviewPanel, fileData, startIndex ?? pageStart);
  }

  /**
   * Remove every override that intersects the requested span, then refresh the
   * page so the affected rows fall back to standard 4-byte instruction
   * decoding.
   */
  private async _handleClearDataFormat(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    address: number,
    length: number,
    startIndex?: number,
  ): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      vscode.window.showErrorMessage('Not connected — cannot clear data-format override.');
      return;
    }
    try {
      const gameId = this._currentGameSerial || undefined;
      await this.socketClient.clearDataFormat(address, length, gameId);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      vscode.window.showErrorMessage(`Failed to clear data format: ${msg}`);
      return;
    }
    const pageStart = this._pageStartForAddress(address);
    await this.sendDisasmPage(webviewPanel, fileData, startIndex ?? pageStart);
  }

  /** Round <c>address</c> down to the nearest PAGE_SIZE * INSTRUCTION_SIZE boundary, expressed as an instruction index. */
  private _pageStartForAddress(address: number): number {
    const offset = address - DisasmEditorProvider.BASE_ADDRESS;
    const idx    = Math.floor(offset / DisasmEditorProvider.INSTRUCTION_SIZE);
    return Math.floor(idx / DisasmEditorProvider.PAGE_SIZE) * DisasmEditorProvider.PAGE_SIZE;
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
    const alignedAddr = Math.max(baseAddr, ((highlightAddr & ~0xf) >>> 0) - 0x10);
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

  // ── Memory Card Manager ───────────────────────────────────────────────────

  /**
   * Open a file-picker so the user can select an existing .ps2 memory card image.
   * Posts 'browseMcResult' with the chosen path back to the webview.
   */
  private async _handleBrowseMc(webview: vscode.Webview): Promise<void> {
    const uris = await vscode.window.showOpenDialog({
      canSelectFiles: true,
      canSelectFolders: false,
      canSelectMany: false,
      filters: {
        'PS2 Memory Cards': ['ps2'],
        'All Files': ['*'],
      },
      title: 'Open PS2 Memory Card',
    });
    if (uris && uris.length > 0) {
      webview.postMessage({ type: 'browseMcResult', filePath: uris[0].fsPath });
    }
  }

  /**
   * Open a save-file dialog so the user can create a new blank 8 MB memory card.
   * Calls MC_FORMAT on the socket server, then posts 'newMcResult' back.
   */
  private async _handleNewMc(webview: vscode.Webview): Promise<void> {
    const uri = await vscode.window.showSaveDialog({
      filters: {
        'PS2 Memory Cards': ['ps2'],
        'All Files': ['*'],
      },
      title: 'Create New PS2 Memory Card',
    });
    if (!uri) { return; }
    const mcPath = uri.fsPath;
    try {
      if (!this.socketClient?.isConnected()) {
        vscode.window.showErrorMessage('Not connected to the emulator. Cannot create memory card via socket.');
        return;
      }
      await this.socketClient.mcFormat(mcPath);
      webview.postMessage({ type: 'newMcResult', filePath: mcPath });
    } catch (err: any) {
      const msg = err?.message ?? String(err);
      vscode.window.showErrorMessage(`Failed to create memory card: ${msg}`);
      webview.postMessage({ type: 'mcActionResult', error: msg });
    }
  }

  /**
   * List the saves on a PS2 memory card and post the result back to the webview.
   */
  private async _handleMcList(webview: vscode.Webview, mcPath: string): Promise<void> {
    if (!mcPath) { return; }
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'mcListResult', error: 'Not connected to emulator.' });
        return;
      }
      const saves = await this.socketClient.mcList(mcPath);
      webview.postMessage({ type: 'mcListResult', saves });
    } catch (err: any) {
      webview.postMessage({ type: 'mcListResult', error: err?.message ?? String(err) });
    }
  }

  /**
   * Export a named save from a memory card. Prompts the user to pick an output
   * directory, then calls MC_EXPORT and posts the result back.
   */
  private async _handleMcExport(webview: vscode.Webview, mcPath: string, saveName: string): Promise<void> {
    if (!mcPath || !saveName) { return; }
    const uris = await vscode.window.showOpenDialog({
      canSelectFiles: false,
      canSelectFolders: true,
      canSelectMany: false,
      title: `Export "${saveName}" — Choose Destination Folder`,
    });
    if (!uris || uris.length === 0) { return; }
    const exportDir = uris[0].fsPath;
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'mcActionResult', error: 'Not connected to emulator.' });
        return;
      }
      const outPath = await this.socketClient.mcExport(mcPath, saveName, exportDir);
      getOutputChannel().info(`[MC] Exported "${saveName}" → ${outPath}`);
      webview.postMessage({ type: 'mcActionResult', message: `Exported "${saveName}" successfully.` });
    } catch (err: any) {
      webview.postMessage({ type: 'mcActionResult', error: err?.message ?? String(err) });
    }
  }

  /**
   * Import a save into a memory card. Prompts the user to pick a save folder,
   * then calls MC_IMPORT and posts the result back.
   */
  private async _handleMcImport(webview: vscode.Webview, mcPath: string): Promise<void> {
    if (!mcPath) { return; }
    const uris = await vscode.window.showOpenDialog({
      canSelectFiles: false,
      canSelectFolders: true,
      canSelectMany: false,
      title: 'Select Save Folder to Import',
    });
    if (!uris || uris.length === 0) { return; }
    const importDir = uris[0].fsPath;
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'mcActionResult', error: 'Not connected to emulator.' });
        return;
      }
      await this.socketClient.mcImport(mcPath, importDir, true);
      const saveName = path.basename(importDir);
      const msg = `Imported "${saveName}" into memory card.`;
      getOutputChannel().info(`[MC] ${msg}`);
      webview.postMessage({ type: 'mcActionResult', message: msg });
    } catch (err: any) {
      webview.postMessage({ type: 'mcActionResult', error: err?.message ?? String(err) });
    }
  }

  /**
   * Delete a named save from a memory card and post the result back.
   */
  private async _handleMcDelete(webview: vscode.Webview, mcPath: string, saveName: string): Promise<void> {
    if (!mcPath || !saveName) { return; }
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'mcActionResult', error: 'Not connected to emulator.' });
        return;
      }
      await this.socketClient.mcDelete(mcPath, saveName);
      const msg = `Deleted "${saveName}" from memory card.`;
      getOutputChannel().info(`[MC] ${msg}`);
      webview.postMessage({ type: 'mcActionResult', message: msg });
    } catch (err: any) {
      webview.postMessage({ type: 'mcActionResult', error: err?.message ?? String(err) });
    }
  }

  // ── HDD Image Manager ────────────────────────────────────────────────────

  /**
   * Open a file-picker so the user can select an existing HDD.raw image.
   * Posts 'browseHddResult' with the chosen path back to the webview.
   */
  private async _handleBrowseHdd(webview: vscode.Webview): Promise<void> {
    const uris = await vscode.window.showOpenDialog({
      canSelectFiles: true,
      canSelectFolders: false,
      canSelectMany: false,
      filters: {
        'PS2 HDD Images': ['raw', 'img'],
        'All Files': ['*'],
      },
      title: 'Open PS2 HDD Image',
    });
    if (uris && uris.length > 0) {
      webview.postMessage({ type: 'browseHddResult', filePath: uris[0].fsPath });
    }
  }

  /**
   * Open a save-file dialog so the user can create a new blank HDD image.
   * Calls HDD_CREATE on the socket server, then posts 'newHddResult' back.
   */
  private async _handleNewHdd(webview: vscode.Webview): Promise<void> {
    const uri = await vscode.window.showSaveDialog({
      filters: {
        'PS2 HDD Images': ['raw'],
        'All Files': ['*'],
      },
      title: 'Create New PS2 HDD Image',
    });
    if (!uri) { return; }
    const hddPath = uri.fsPath;
    try {
      if (!this.socketClient?.isConnected()) {
        vscode.window.showErrorMessage('Not connected to the emulator. Cannot create HDD image via socket.');
        return;
      }
      await this.socketClient.hddCreate(hddPath, 40);
      webview.postMessage({ type: 'newHddResult', filePath: hddPath });
    } catch (err: any) {
      const msg = err?.message ?? String(err);
      vscode.window.showErrorMessage(`Failed to create HDD image: ${msg}`);
      webview.postMessage({ type: 'hddActionResult', error: msg });
    }
  }

  /**
   * List the partitions on a PS2 HDD image and post the result back to the webview.
   */
  private async _handleHddList(webview: vscode.Webview, hddPath: string): Promise<void> {
    if (!hddPath) { return; }
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'hddListResult', error: 'Not connected to emulator.' });
        return;
      }
      const partitions = await this.socketClient.hddList(hddPath);
      webview.postMessage({ type: 'hddListResult', partitions });
    } catch (err: any) {
      webview.postMessage({ type: 'hddListResult', error: err?.message ?? String(err) });
    }
  }

  /**
   * List files in a specific HDD partition.
   */
  private async _handleHddListFiles(webview: vscode.Webview, hddPath: string, partitionId: string): Promise<void> {
    if (!hddPath || !partitionId) { return; }
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'hddActionResult', error: 'Not connected to emulator.' });
        return;
      }
      const files = await this.socketClient.hddListFiles(hddPath, partitionId);
      const fileList = files.map(f => `${f.Name} (${f.Size} bytes)`).join('\n');
      const msg = files.length === 0
        ? `Partition "${partitionId}" is empty.`
        : `Files in "${partitionId}":\n${fileList}`;
      getOutputChannel().info(`[HDD] ${msg}`);
      webview.postMessage({ type: 'hddActionResult', message: `${files.length} file(s) in "${partitionId}".` });
    } catch (err: any) {
      webview.postMessage({ type: 'hddActionResult', error: err?.message ?? String(err) });
    }
  }

  /**
   * Export all files from a partition. Prompts the user to pick an output
   * directory, then calls HDD_EXPORT for each file and posts the result back.
   */
  private async _handleHddExport(webview: vscode.Webview, hddPath: string, partitionId: string): Promise<void> {
    if (!hddPath || !partitionId) { return; }
    const uris = await vscode.window.showOpenDialog({
      canSelectFiles: false,
      canSelectFolders: true,
      canSelectMany: false,
      title: `Export "${partitionId}" — Choose Destination Folder`,
    });
    if (!uris || uris.length === 0) { return; }
    const exportDir = uris[0].fsPath;
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'hddActionResult', error: 'Not connected to emulator.' });
        return;
      }
      // Get file list first, then export each entry (including directories, which
      // the server exports recursively).
      const files = await this.socketClient.hddListFiles(hddPath, partitionId);
      let exported = 0;
      for (const file of files) {
        try {
          await this.socketClient.hddExport(hddPath, partitionId, file.Name, exportDir);
          exported++;
        } catch (err: any) {
          getOutputChannel().warn(`[HDD] Failed to export "${file.Name}": ${err?.message}`);
        }
      }
      const msg = `Exported ${exported} file(s) from "${partitionId}" to ${exportDir}`;
      getOutputChannel().info(`[HDD] ${msg}`);
      webview.postMessage({ type: 'hddActionResult', message: msg });
    } catch (err: any) {
      webview.postMessage({ type: 'hddActionResult', error: err?.message ?? String(err) });
    }
  }

  /**
   * Delete a partition from an HDD image and post the result back.
   */
  private async _handleHddDelete(webview: vscode.Webview, hddPath: string, partitionId: string): Promise<void> {
    if (!hddPath || !partitionId) { return; }
    try {
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'hddActionResult', error: 'Not connected to emulator.' });
        return;
      }
      await this.socketClient.hddDelete(hddPath, partitionId);
      const msg = `Deleted partition "${partitionId}" from HDD image.`;
      getOutputChannel().info(`[HDD] ${msg}`);
      webview.postMessage({ type: 'hddActionResult', message: msg });
    } catch (err: any) {
      webview.postMessage({ type: 'hddActionResult', error: err?.message ?? String(err) });
    }
  }

  // ── Memory Value Scanner ──────────────────────────────────────────────────

  /**
   * Perform a SCANVALUE request and post the results back to the webview.
   * Handles both full-range scans and filter (refine) scans.
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private async _handleScanValue(webview: vscode.Webview, message: any): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      webview.postMessage({ type: 'scanValueResult', error: 'Not connected to emulator server' });
      return;
    }
    try {
      const { results: entries, message: serverMsg } = await this.socketClient.scanValue(
        message.startAddress as number,
        message.endAddress   as number,
        message.value        as string,
        message.comparison   as string,
        message.dataType     as string,
        message.region       as string,
        message.previousAddresses as number[] | undefined,
      );
      webview.postMessage({ type: 'scanValueResult', entries, serverMessage: serverMsg });
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      getOutputChannel().error('[Scanner] ' + errMsg);
      webview.postMessage({ type: 'scanValueResult', error: errMsg });
    }
  }

  /** Fetch all struct definitions and post back to webview. */
  private async _handleGetStructs(webview: vscode.Webview): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      // Don't clear existing structs — send null so the overlay keeps what it had
      webview.postMessage({ type: 'getStructsResult', structs: [], connected: false });
      return;
    }
    try {
      const structs = await this.socketClient.getStructs();
      webview.postMessage({ type: 'getStructsResult', structs, connected: true });
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      getOutputChannel().error('[Struct] ' + errMsg);
      webview.postMessage({ type: 'getStructsResult', structs: [], connected: false });
    }
  }

  /** Add or update a struct definition. */
  private async _handleAddStruct(webview: vscode.Webview, name: string, baseAddress: string): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      await this.socketClient.addStruct(name, baseAddress);
      const structs = await this.socketClient.getStructs();
      webview.postMessage({ type: 'addStructResult', structs });
      await this._resolveStructLabelsFrom(structs);
      this.onStructsChanged?.(structs);
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      getOutputChannel().error('[Struct] ' + errMsg);
    }
  }

  /** Add a member to an existing struct. */
  private async _handleAddStructMember(webview: vscode.Webview, structName: string, offset: number, memberName: string): Promise<void> {
    if (!this.socketClient?.isConnected()) { return; }
    try {
      await this.socketClient.addStructMember(structName, offset, memberName);
      const structs = await this.socketClient.getStructs();
      webview.postMessage({ type: 'addStructMemberResult', structs });
      await this._resolveStructLabelsFrom(structs);
      this.onStructsChanged?.(structs);
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      getOutputChannel().error('[Struct] ' + errMsg);
    }
  }

  /**
   * Read 4 bytes from the live emulator at the given address and return the
   * little-endian uint32 value as an 8-digit uppercase hex string (e.g. "000C0000").
   */
  private async _handleReadPointerAt(webview: vscode.Webview, addressStr: string): Promise<void> {
    if (!this.socketClient?.isConnected()) {
      webview.postMessage({ type: 'readPointerAtResult', value: '' });
      return;
    }
    try {
      const addr = parseInt(addressStr.startsWith('0x') || addressStr.startsWith('0X')
        ? addressStr.slice(2) : addressStr, 16);
      if (isNaN(addr)) {
        webview.postMessage({ type: 'readPointerAtResult', value: '' });
        return;
      }
      const buf = await this.socketClient.readMemory(addr, 4, 'ee');
      const value = buf.readUInt32LE(0);
      webview.postMessage({ type: 'readPointerAtResult', value: '0x' + value.toString(16).toUpperCase().padStart(8, '0') });
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      getOutputChannel().error('[Struct] readPointerAt: ' + errMsg);
      webview.postMessage({ type: 'readPointerAtResult', value: '' });
    }
  }

  // ── Label Manager ──────────────────────────────────────────────────────────

  /**
   * Optional callback invoked whenever the active label set changes.
   * Extension.ts wires this to refresh the STRUCTS panel.
   */
  public onStructsChanged?: (structs: StructEntry[]) => void;

  /** Helper — build workspaceDir and gameDiscId from current context. */
  private _labelManagerCtx(): { workspaceDir: string; gameDiscId: string } | null {
    // Fall back to the first VS Code workspace folder when the cached root has
    // not been set yet (e.g. on startup before resolveWebviewView fires).
    const workspaceDir = this._workspaceRoot
      || vscode.workspace.workspaceFolders?.[0]?.uri.fsPath
      || '';
    if (!workspaceDir) { return null; }

    // Use serial (e.g. "SCUS-97275") when available so the DB can resolve to
    // the exact GameDisc row.  Fall back to the ISO filename for backward
    // compatibility.  When no game is selected at all, use a placeholder so
    // the label manager still opens and shows the Built-in set; the backend
    // gracefully returns an empty user-set list for an unknown identifier.
    const gameDiscId = this._currentGameSerial || this._currentGameName || DisasmEditorProvider.NO_GAME_SELECTED;
    return { workspaceDir, gameDiscId };
  }

  /**
   * Broadcast the label set to all open panels and optionally notify struct listeners.
   * @param skipWebview When provided, skip posting to this webview (to avoid duplicate sends
   *                    when the caller already posted directly to the originating webview).
   */
  private _broadcastLabelSet(set: LabelSetPayload, skipWebview?: vscode.Webview): void {
    for (const p of this._livePanels) {
      if (p.webview !== skipWebview) {
        p.webview.postMessage({ type: 'labelManagerSet', set });
      }
    }
    // Also notify the standalone label manager tab if open (unless it's the caller).
    const lmWebview = this._labelManagerPanel?.webview;
    if (lmWebview && lmWebview !== skipWebview) {
      lmWebview.postMessage({ type: 'labelManagerSet', set });
    }
    // Convert to StructEntry[] for backward-compat listeners (STRUCTS panel).
    // Children of each struct entry are now full label entries with parentAddress = struct's address.
    // Hidden children are excluded from the struct member display.
    const childrenByParent = new Map<string, LabelManagerEntry[]>();
    for (const e of set.entries) {
      if (e.parentAddress && !e.hidden) {
        const arr = childrenByParent.get(e.parentAddress) ?? [];
        arr.push(e);
        childrenByParent.set(e.parentAddress, arr);
      }
    }
    const structs: StructEntry[] = set.entries
      .filter(e => !e.parentAddress && (e.type ?? 'Address') === 'Struct')
      .map(e => {
        const kids = (childrenByParent.get(e.address) ?? []).sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
        return {
          name:        e.name,
          baseAddress: e.address,
          members:     kids.map(m => ({ offset: m.offset ?? 0, name: m.name })),
        };
      })
      .filter(s => s.members.length > 0);
    this.onStructsChanged?.(structs);
    // Re-resolve struct member addresses from live memory so the instruction
    // display reflects the new label set.  Intentionally fire-and-forget here
    // (errors are non-fatal) so _broadcastLabelSet stays synchronous.
    this.refreshStructLabels().catch(() => { /* non-fatal */ });
  }

  /** Dispatch a LabelManagerAction received from the webview. */
  private async _handleLabelManagerAction(
    webview: vscode.Webview,
    action: { type: string; setName?: string; newSetName?: string; activeSetName?: string; entry?: LabelManagerEntry; address?: string; json?: string; parentAddress?: string; offset?: number; addresses?: string[]; entries?: Array<{ address: string; entryType?: string; valueType?: string }>; members?: Array<{ parentAddress: string; offset: number; path: string; entryType?: string; valueType?: string }>; scannerKind?: string },
  ): Promise<void> {
    // Handle memory-read actions before connectivity/context checks — they fail
    // silently rather than surfacing an error banner.
    if (action.type === 'navigateToLabel') {
      const addrHex = (action.address ?? '').replace(/^0x/i, '');
      const addr = parseInt(addrHex, 16);
      if (!isNaN(addr) && addr > 0) {
        this.navigateToAddress(addr);
      }
      return;
    }

    if (action.type === 'navigateToMember') {
      if (!this.socketClient?.isConnected()) { return; }
      const parentAddr = parseInt((action.parentAddress ?? '0').replace(/^0x/i, ''), 16);
      const offset = action.offset ?? 0;
      try {
        const buf = await this.socketClient.readMemory(parentAddr, 4, 'ee');
        if (buf.length >= 4) {
          const ptrVal = buf.readUInt32LE(0);
          if (ptrVal > 0) {
            this.navigateToAddress((ptrVal + offset) >>> 0);
          }
        }
      } catch { /* non-fatal */ }
      return;
    }

    if (action.type === 'readLiveValues') {
      if (action.setName && this._activeSetName && action.setName !== this._activeSetName) {
        return;
      }
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'labelManagerLiveValues', values: {} });
        return;
      }
      const entries = action.entries ?? [];
      const values: Record<string, string> = {};
      const scalarEntries: Array<{ addrHex: string; addr: number; entryType?: string; valueType?: string }> = [];
      const stringEntries: Array<{ addrHex: string; addr: number }> = [];
      for (const entry of entries) {
        const addr = parseInt((entry.address ?? '').replace(/^0x/i, ''), 16);
        if (isNaN(addr)) { continue; }
        if (entry.entryType === 'String') { stringEntries.push({ addrHex: entry.address, addr }); continue; }
        if (entry.entryType === 'Function') { continue; }
        scalarEntries.push({ addrHex: entry.address, addr, entryType: entry.entryType, valueType: entry.valueType });
      }
      if (scalarEntries.length > 0) {
        try {
          const batch = await this.socketClient.readMemoryBatch(scalarEntries.map(e => e.addr), 4, 'ee');
          for (const e of scalarEntries) {
            const buf = batch.get(e.addr >>> 0);
            if (buf && buf.length >= 4) {
              values[e.addrHex] = formatLiveValueByEntryType(buf, e.entryType, e.valueType);
            }
          }
        } catch {
          // Fallback path below still handles reads individually.
        }
      }
      // Batch 4-byte probes for String entries so we don't issue one READMEM(4) per entry.
      const stringProbe = new Map<number, Buffer>();
      if (stringEntries.length > 0) {
        try {
          const sb = await this.socketClient.readMemoryBatch(
            Array.from(new Set(stringEntries.map(s => s.addr))),
            4,
            'ee',
          );
          for (const [k, v] of sb) stringProbe.set(k, v);
        } catch { /* fallback to per-entry string reads below */ }
      }
      for (const entry of entries) {
        const addrHex = entry.address;
        const addr = parseInt(addrHex.replace(/^0x/i, ''), 16);
        if (isNaN(addr)) { continue; }
        try {
          if (entry.entryType === 'String') {
            // Read up to 256 bytes. If the first byte isn't printable ASCII, try pointer dereference.
            const probe = stringProbe.get(addr >>> 0);
            const firstByteFromProbe = probe && probe.length > 0 ? probe[0] : undefined;
            const buf = await this.socketClient.readMemory(addr, 256, 'ee');
            const firstByte = buf.length > 0 ? buf[0] : 0;
            const fb = firstByteFromProbe ?? firstByte;
            if (fb >= 0x20 && fb < 0x7F) {
              const nullIdx = buf.indexOf(0);
              values[addrHex] = buf.slice(0, nullIdx !== -1 ? nullIdx : buf.length).toString('latin1');
            } else if (buf.length >= 4) {
              // Try reading the value as a pointer and then reading the string from that address.
              const ptrAddr = buf.readUInt32LE(0);
              if (ptrAddr > 0) {
                try {
                  const strBuf = await this.socketClient.readMemory(ptrAddr, 256, 'ee');
                  const nullIdx = strBuf.indexOf(0);
                  values[addrHex] = strBuf.slice(0, nullIdx !== -1 ? nullIdx : strBuf.length).toString('latin1');
                } catch { /* ignore */ }
              }
            }
          } else if (entry.entryType === 'Function') {
            // Scan the function prolog/epilog to detect argument and return registers live.
            try {
              const funcBytes = await this.socketClient.readMemory(addr, 128, 'ee');
              if (funcBytes.length >= 4) {
                const hexWords = Array.from(
                  { length: Math.floor(funcBytes.length / 4) },
                  (_, i) => `0x${funcBytes.readUInt32LE(i * 4).toString(16).padStart(8, '0')}`,
                ).join(' ');
                const instructions = await this.socketClient.disassemble(hexWords, addr);
                const analysis = analyzeFunctionPrologEpilog(instructions);
                if (analysis) {
                  values[addrHex] = analysis;
                }
              }
            } catch { /* non-fatal: keep previous live value */ }
          } else if (values[addrHex] === undefined) {
            // Pointer, Address, Float, Value — read 4 bytes and format by type.
            const buf = await this.socketClient.readMemory(addr, 4, 'ee');
            if (buf.length >= 4) {
              values[addrHex] = formatLiveValueByEntryType(buf, entry.entryType, entry.valueType);
            }
          }
        } catch { /* ignore unreadable addresses */ }
      }
      webview.postMessage({ type: 'labelManagerLiveValues', values });
      return;
    }

    if (action.type === 'readMemberLiveValues') {
      if (action.setName && this._activeSetName && action.setName !== this._activeSetName) {
        return;
      }
      if (!this.socketClient?.isConnected()) {
        webview.postMessage({ type: 'labelManagerMemberLiveValues', values: {} });
        return;
      }
      const members = action.members ?? [];
      const values: Record<string, string> = {};
      const sc = this.socketClient;
      let resolvedByPath = new Map<string, number>();
      try {
        resolvedByPath = await resolveMemberPathsBatch(
          members.map(m => m.path),
          sc,
        );
      } catch {
        // Fallback to legacy per-path pointer walk if batch path resolution fails.
        for (const m of members) {
          try {
            const resolved = await resolveMemberPath(m.path, sc);
            if (resolved === null) { continue; }
            resolvedByPath.set(m.path, resolved >>> 0);
          } catch { /* ignore unreadable members */ }
        }
      }

      // Batch scalar member reads first (Value/Float/Pointer/Address-like paths),
      // then fall back to legacy per-item logic for String handling.
      const scalarMembers = members.filter(m => {
        const t = (m.entryType ?? '').toLowerCase();
        const vt = (m.valueType ?? '').toLowerCase();
        return t !== 'string' && vt !== 'string';
      });
      if (scalarMembers.length > 0) {
        try {
          const addrs = scalarMembers
            .map(m => resolvedByPath.get(m.path))
            .filter((v): v is number => typeof v === 'number');
          const uniqueAddrs = Array.from(new Set(addrs));
          const batch = await sc.readMemoryBatch(uniqueAddrs, 4, 'ee');
          for (const m of scalarMembers) {
            const resolved = resolvedByPath.get(m.path);
            if (resolved === undefined) continue;
            const dataBuf = batch.get(resolved >>> 0);
            if (dataBuf && dataBuf.length >= 4) {
              values[m.path] = formatLiveValueByEntryType(dataBuf, m.entryType, m.valueType);
            }
          }
        } catch { /* fallback path below */ }
      }

      for (const m of members) {
        try {
          const resolved = resolvedByPath.get(m.path);
          if (resolved === undefined) { continue; }
          const entryType: string | undefined = m.entryType;
          if (entryType === 'String' || m.valueType?.toLowerCase() === 'string') {
            const dataBuf = await sc.readMemory(resolved, 256, 'ee');
            const firstByte = dataBuf.length > 0 ? dataBuf[0] : 0;
            if (firstByte >= 0x20 && firstByte < 0x7F) {
              const nullIdx = dataBuf.indexOf(0);
              values[m.path] = dataBuf.slice(0, nullIdx !== -1 ? nullIdx : dataBuf.length).toString('latin1');
            } else if (dataBuf.length >= 4) {
              const strPtr = dataBuf.readUInt32LE(0);
              if (strPtr > 0) {
                try {
                  const strBuf = await sc.readMemory(strPtr, 256, 'ee');
                  const nullIdx = strBuf.indexOf(0);
                  values[m.path] = strBuf.slice(0, nullIdx !== -1 ? nullIdx : strBuf.length).toString('latin1');
                } catch { /* ignore */ }
              }
            }
          } else if (values[m.path] === undefined) {
            const dataBuf = await sc.readMemory(resolved, 4, 'ee');
            if (dataBuf.length >= 4) {
              values[m.path] = formatLiveValueByEntryType(dataBuf, entryType, m.valueType);
            }
          }
        } catch { /* ignore unreadable members */ }
      }
      webview.postMessage({ type: 'labelManagerMemberLiveValues', values });
      return;
    }

    if (!this.socketClient?.isConnected()) {
      webview.postMessage({ type: 'labelManagerError', message: 'Not connected to CodeDesigner server.' });
      return;
    }

    const ctx = this._labelManagerCtx();
    if (!ctx) {
      webview.postMessage({ type: 'labelManagerError', message: 'No workspace folder is open. Open a folder in VS Code to use the Label Manager.' });
      return;
    }
    const { workspaceDir, gameDiscId } = ctx;

    try {
      switch (action.type) {
        case 'refresh': {
          const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
          webview.postMessage({ type: 'labelManagerSets', sets });
          // Use the set the client was already viewing if it still exists, otherwise
          // fall back to the first user set or Built-in.
          const requestedName = action.activeSetName;
          const activeName = (requestedName && sets.includes(requestedName))
            ? requestedName
            : (sets.find(s => s !== 'Built-in') ?? 'Built-in');
          // Activate the set on the server so labels appear in the disassembly view,
          // not just in the Label Manager UI.
          const set = await this.socketClient.setActiveLabelSet(workspaceDir, gameDiscId, activeName);
          this._activeSetName = activeName;
          webview.postMessage({ type: 'labelManagerSet', set });
          this._broadcastLabelSet(set, webview);
          await this.refreshStructLabels();
          break;
        }
        case 'setActive': {
          const set = await this.socketClient.setActiveLabelSet(workspaceDir, gameDiscId, action.setName!);
          this._activeSetName = action.setName!;
          webview.postMessage({ type: 'labelManagerSet', set });
          this._broadcastLabelSet(set, webview);
          // Refresh labels in the disassembly view.
          await this.refreshStructLabels();
          break;
        }
        case 'createSet': {
          const set = await this.socketClient.createLabelSet(workspaceDir, gameDiscId, action.setName!);
          // Activate the new set on the server side so the server's LabelMgr state
          // reflects the new set from the moment it is created.  Without this the
          // server would only activate the set the first time ADDLABELENTRY is called.
          await this.socketClient.setActiveLabelSet(workspaceDir, gameDiscId, action.setName!);
          this._activeSetName = action.setName!;
          const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
          webview.postMessage({ type: 'labelManagerSets', sets });
          webview.postMessage({ type: 'labelManagerSet', set });
          webview.postMessage({ type: 'labelManagerStatus', message: `Created label set "${action.setName}".` });
          break;
        }
        case 'deleteSet': {
          await this.socketClient.deleteLabelSet(workspaceDir, gameDiscId, action.setName!);
          this._activeSetName = null;
          const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
          webview.postMessage({ type: 'labelManagerSets', sets });
          // Switch to Built-in after deletion.
          const builtIn = await this.socketClient.setActiveLabelSet(workspaceDir, gameDiscId, 'Built-in');
          webview.postMessage({ type: 'labelManagerSet', set: builtIn });
          this._broadcastLabelSet(builtIn, webview);
          await this.refreshStructLabels();
          webview.postMessage({ type: 'labelManagerStatus', message: `Deleted label set "${action.setName}".` });
          break;
        }
        case 'renameSet': {
          const set = await this.socketClient.renameLabelSet(workspaceDir, gameDiscId, action.setName!, action.newSetName!);
          this._activeSetName = set.setName;
          const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
          webview.postMessage({ type: 'labelManagerSets', sets });
          webview.postMessage({ type: 'labelManagerSet', set });
          webview.postMessage({ type: 'labelManagerStatus', message: `Renamed label set to "${set.setName}".` });
          break;
        }
        case 'addEntry': {
          const setName = action.setName!;
          await this.socketClient.setActiveLabelSet(workspaceDir, gameDiscId, setName);
          this._activeSetName = setName;
          const set = await this.socketClient.addLabelEntry(workspaceDir, gameDiscId, setName, action.entry!);
          webview.postMessage({ type: 'labelManagerSet', set });
          this._broadcastLabelSet(set, webview);
          await this.refreshStructLabels();
          break;
        }
        case 'deleteEntry': {
          const setName = action.setName!;
          const set = await this.socketClient.deleteLabelEntry(workspaceDir, gameDiscId, setName, action.address ?? '');
          webview.postMessage({ type: 'labelManagerSet', set });
          this._broadcastLabelSet(set, webview);
          await this.refreshStructLabels();
          break;
        }
        case 'importSet': {
          const set = await this.socketClient.importLabelSet(workspaceDir, gameDiscId, action.setName ?? 'Imported', action.json!);
          this._activeSetName = set.setName;
          const sets = await this.socketClient.getLabelSets(workspaceDir, gameDiscId);
          webview.postMessage({ type: 'labelManagerSets', sets });
          webview.postMessage({ type: 'labelManagerSet', set });
          webview.postMessage({ type: 'labelManagerStatus', message: `Imported "${set.setName}" (${set.entries.length} entries).` });
          break;
        }
        case 'exportSet': {
          const json = await this.socketClient.exportLabelSet(workspaceDir, gameDiscId, action.setName!);
          webview.postMessage({ type: 'labelManagerExport', json, setName: action.setName });
          break;
        }
        case 'runScanner': {
          const kind = action.scannerKind as string;
          // Default to EE RAM region for label manager scans.
          const region = 'ee';
          const baseAddress = 0x00000000;
          const totalBytes = 32 * 1024 * 1024; // 32 MB EE RAM
          try {
            let entries: LabelManagerEntry[] = [];
            if (kind === 'strings') {
              const results = await this.socketClient.scanLiveStrings(baseAddress, totalBytes, region);
              entries = results.map(r => ({
                address: r.address,
                name: r.value.length > 40 ? r.value.substring(0, 40) + '…' : r.value,
                comment: r.value.length > 40 ? r.value : undefined,
                type: 'String' as const,
              }));
            } else if (kind === 'functions') {
              const results = await this.socketClient.scanLiveFunctions(baseAddress, totalBytes, region);
              entries = results.map(r => ({
                address: r.address,
                name: r.label || `func_${r.address}`,
                type: 'Function' as const,
              }));
            } else if (kind === 'pointers') {
              const results = await this.socketClient.scanLivePointers(baseAddress, totalBytes, region);
              entries = results.map(r => ({
                address: r.address,
                name: `ptr → ${r.pointsTo}`,
                comment: `Points to ${r.pointsTo}`,
                type: 'Pointer' as const,
              }));
            } else if (kind === 'floats') {
              const results = await this.socketClient.scanLiveFloats(baseAddress, totalBytes, region);
              entries = results.map(r => ({
                address: r.address,
                name: `${r.value}`,
                comment: `raw: ${r.raw}`,
                type: 'Float' as const,
              }));
            } else if (kind === 'hexValues') {
              const results = await this.socketClient.scanLiveIntegers(baseAddress, totalBytes, region);
              entries = results.map(r => ({
                address: r.address,
                name: r.hex,
                comment: `dec: ${r.value}`,
                type: 'Hex' as const,
              }));
            }
            webview.postMessage({ type: 'labelManagerScanResults', scannerKind: kind, entries });
          } catch (scanErr: unknown) {
            const scanErrMsg = scanErr instanceof Error ? scanErr.message : String(scanErr);
            webview.postMessage({ type: 'labelManagerScanResults', scannerKind: kind, entries: [] });
            webview.postMessage({ type: 'labelManagerError', message: `Scan failed: ${scanErrMsg}` });
          }
          break;
        }
        case 'clearScanResults':
          // Nothing to do server-side; the UI already cleared its state.
          break;
        case 'resolveStrings':
          // Legacy: re-route to the scanner for strings.
          await this._handleLabelManagerAction(webview, { type: 'runScanner', scannerKind: 'strings' });
          break;
        default:
          webview.postMessage({ type: 'labelManagerError', message: `Unknown action: ${action.type}` });
      }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      getOutputChannel().error('[LabelManager] ' + errMsg);
      webview.postMessage({ type: 'labelManagerError', message: errMsg });
    }
  }
}

// ── Live-value formatting helpers ───────────────────────────────────────────

/**
 * Returns the number of bytes to read from memory for a given valueType.
 * 64-bit integer and double types require 8 bytes; all others use 4.
 */
function liveValueReadSize(valueType?: string): number {
  switch (valueType?.toLowerCase()) {
    case 'u64':
    case 's64':
    case 'double': return 8;
    default:       return 4;
  }
}

/**
 * Formats a raw memory buffer as a human-readable string according to valueType.
 * Falls back to a 32-bit hex representation when valueType is absent or unrecognised.
 */
function formatLiveValue(buf: Buffer, valueType?: string): string {
  if (!valueType) {
    return `0x${buf.readUInt32LE(0).toString(16).toUpperCase().padStart(8, '0')}`;
  }
  switch (valueType.toLowerCase()) {
    case 'u8':
      return buf[0].toString();
    case 'u16':
      return buf.readUInt16LE(0).toString();
    case 'u32':
      return buf.readUInt32LE(0).toString();
    case 'u64':
      return buf.readBigUInt64LE(0).toString();
    case 's8':
      return buf.readInt8(0).toString();
    case 's16':
      return buf.readInt16LE(0).toString();
    case 's32':
      return buf.readInt32LE(0).toString();
    case 's64':
      return buf.readBigInt64LE(0).toString();
    case 'float': {
      const v = buf.readFloatLE(0);
      if (!isFinite(v)) { return isNaN(v) ? 'NaN' : (v > 0 ? 'Inf' : '-Inf'); }
      return v.toPrecision(7);
    }
    case 'double': {
      const v = buf.readDoubleLE(0);
      if (!isFinite(v)) { return isNaN(v) ? 'NaN' : (v > 0 ? 'Inf' : '-Inf'); }
      return v.toPrecision(15);
    }
    case 'pointer':
      return `0x${buf.readUInt32LE(0).toString(16).toUpperCase().padStart(8, '0')}`;
    default:
      return `0x${buf.readUInt32LE(0).toString(16).toUpperCase().padStart(8, '0')}`;
  }
}

/**
 * Resolves a synthetic member path to a physical memory address by walking the
 * pointer chain encoded in the path string.
 *
 * Path format: `"0xROOT:+0xOFF1:+0xOFF2"` where:
 *   - The root segment (`0xROOT`) is a real PS2 address.
 *   - Each subsequent segment (`:+0xOFF`) means: dereference the current
 *     address as a 32-bit LE pointer, then add the offset to get the next address.
 *
 * Returns `null` when the path cannot be parsed or a null pointer is encountered.
 */
async function resolveMemberPath(
  path: string,
  socketClient: { readMemory(addr: number, length: number, region: string): Promise<Buffer> },
): Promise<number | null> {
  const parts = path.split(':');
  let addr = parseInt(parts[0].replace(/^0x/i, ''), 16);
  if (isNaN(addr)) { return null; }

  for (let i = 1; i < parts.length; i++) {
    const ptrBuf = await socketClient.readMemory(addr, 4, 'ee');
    if (ptrBuf.length < 4) { return null; }
    const base = ptrBuf.readUInt32LE(0);
    if (base === 0) { return null; }
    const offsetStr = parts[i].replace(/^\+0x/i, '').replace(/^\+/, '');
    const offset = parseInt(offsetStr, 16);
    if (isNaN(offset)) { return null; }
    addr = (base + offset) >>> 0;
  }

  return addr;
}

/**
 * Batch-resolves many synthetic member paths by dereferencing each depth level
 * with READMEMBATCH, deduplicating shared intermediate addresses.
 */
async function resolveMemberPathsBatch(
  paths: string[],
  socketClient: {
    readMemory(addr: number, length: number, region: string): Promise<Buffer>;
    readMemoryBatch(addresses: number[], length: number, region: string): Promise<Map<number, Buffer>>;
  },
): Promise<Map<string, number>> {
  type PathState = { parts: string[]; step: number; addr: number; valid: boolean };
  const states = new Map<string, PathState>();
  for (const path of paths) {
    const parts = path.split(':');
    const root = parseInt(parts[0].replace(/^0x/i, ''), 16);
    states.set(path, { parts, step: 1, addr: root >>> 0, valid: !isNaN(root) });
  }

  const maxDepth = Math.max(0, ...Array.from(states.values(), s => s.parts.length - 1));
  for (let depth = 1; depth <= maxDepth; depth++) {
    const needs = Array.from(states.values()).filter(s => s.valid && s.step === depth);
    if (needs.length === 0) continue;
    const uniqueAddrs = Array.from(new Set(needs.map(s => s.addr >>> 0)));
    const ptrBatch = await socketClient.readMemoryBatch(uniqueAddrs, 4, 'ee');

    for (const s of needs) {
      const ptrBuf = ptrBatch.get(s.addr >>> 0);
      if (!ptrBuf || ptrBuf.length < 4) { s.valid = false; continue; }
      const base = ptrBuf.readUInt32LE(0);
      if (base === 0) { s.valid = false; continue; }
      const offsetStr = s.parts[depth].replace(/^\+0x/i, '').replace(/^\+/, '');
      const offset = parseInt(offsetStr, 16);
      if (isNaN(offset)) { s.valid = false; continue; }
      s.addr = (base + offset) >>> 0;
      s.step++;
    }
  }

  const out = new Map<string, number>();
  for (const [path, s] of states) {
    if (s.valid && s.step === s.parts.length) {
      out.set(path, s.addr >>> 0);
    }
  }
  return out;
}

/**
 * Formats a raw 4-byte memory buffer using the semantic label entry type first,
 * falling back to the legacy valueType annotation.
 * Entry type values are PascalCase strings matching the LabelType enum.
 */
function formatLiveValueByEntryType(buf: Buffer, entryType?: string, valueType?: string): string {
  switch (entryType) {
    case 'Pointer':
    case 'Address':
      return `0x${buf.readUInt32LE(0).toString(16).toUpperCase().padStart(8, '0')}`;
    case 'Float': {
      const v = buf.readFloatLE(0);
      if (!isFinite(v)) { return isNaN(v) ? 'NaN' : (v > 0 ? 'Inf' : '-Inf'); }
      return v.toPrecision(7);
    }
    case 'Value':
      return buf.readUInt32LE(0).toString();
    default:
      return formatLiveValue(buf, valueType);
  }
}

/**
 * Analyze MIPS EE function prolog and epilog instructions to detect:
 *   - Argument registers (a0–a3) that are read before being written in the first 16 instructions.
 *   - Return registers (v0–v1) that are written before the terminal `jr ra` in the last 16 instructions.
 *
 * Returns a compact string like `"args[a0,a1] rets[v0]"` or an empty string when nothing is detected.
 * Each InstructionResult is expected to carry the disassembly text in its `Assembly` field.
 */
function analyzeFunctionPrologEpilog(instructions: { Assembly?: string }[]): string {
  const ARG_REGS = ['a0', 'a1', 'a2', 'a3'] as const;
  const RET_REGS = ['v0', 'v1'] as const;

  // Opcodes where the first operand token is a SOURCE register, not a destination.
  // Stores: sw/sh/sb/sd/sq/swc1/sdc1 — first operand is the value being stored.
  // Branches/jumps: beq/bne/bgez/bgtz/blez/bltz/jr/jalr — no destination register.
  const SOURCE_FIRST_RE =
    /^(sw|sh|sb|sd|sq|swl|swr|swc1|swc2|sdc1|sdc2|beq|bne|bgez|bgtz|blez|bltz|bgezal|bltzal|beql|bnel|bgezl|bgtzl|blezl|bltzl|jr|jalr|eret)\b/;

  /**
   * Return the destination register name for a disassembly line, or null when
   * the first operand is a source (stores, branches, jr/jalr).
   */
  function destReg(asm: string): string | null {
    if (SOURCE_FIRST_RE.test(asm)) { return null; }
    // "opcode dest, ..." — first whitespace-separated token after the mnemonic.
    const m = /^\S+\s+(\w+)/.exec(asm);
    return m ? m[1] : null;
  }

  /**
   * Return all register-name-sized alphanumeric tokens from the instruction text.
   * Assumes `asm` has already been lowercased — the pattern intentionally uses
   * only `[a-z0-9]` so that it matches the canonical lowercase register names
   * (a0, v0, ra, sp, …) produced by the EE disassembler.
   * Includes the destination token — the caller decides whether to treat it as source or dest.
   */
  function allTokens(asm: string): string[] {
    return asm.split(/[^a-z0-9]+/).filter(Boolean);
  }

  const argFound   = new Set<string>();
  const argWritten = new Set<string>();
  const retFound   = new Set<string>();

  // Find the last `jr ra` to mark the epilog boundary.
  let jrRaIdx = -1;
  for (let i = instructions.length - 1; i >= 0; i--) {
    const asm = (instructions[i]?.Assembly ?? '').toLowerCase().trim();
    if (/^jr\s+ra$/.test(asm)) { jrRaIdx = i; break; }
  }

  // ── Prolog scan: first 16 instructions (or up to jr ra) ─────────────────────
  const prologEnd = Math.min(16, jrRaIdx >= 0 ? jrRaIdx : instructions.length);
  for (let i = 0; i < prologEnd; i++) {
    const asm = (instructions[i]?.Assembly ?? '').toLowerCase().trim();
    if (!asm) { continue; }
    const dest   = destReg(asm);
    const tokens = allTokens(asm);
    for (const reg of ARG_REGS) {
      if (argFound.has(reg) || argWritten.has(reg)) { continue; }
      if (dest === reg) {
        // First occurrence is a write — register is not an argument.
        argWritten.add(reg);
      } else if (tokens.includes(reg)) {
        // Register is read before any write — it's a passed argument.
        argFound.add(reg);
      }
    }
  }

  // ── Epilog scan: 16 instructions before `jr ra` ──────────────────────────────
  if (jrRaIdx >= 0) {
    const epilogStart = Math.max(0, jrRaIdx - 16);
    for (let i = epilogStart; i < jrRaIdx; i++) {
      const asm = (instructions[i]?.Assembly ?? '').toLowerCase().trim();
      if (!asm) { continue; }
      const dest = destReg(asm);
      for (const reg of RET_REGS) {
        if (dest === reg) { retFound.add(reg); }
      }
    }
  }

  const parts: string[] = [];
  const args = ARG_REGS.filter(r => argFound.has(r));
  const rets = RET_REGS.filter(r => retFound.has(r));
  if (args.length > 0) { parts.push(`args[${args.join(',')}]`); }
  if (rets.length > 0) { parts.push(`rets[${rets.join(',')}]`); }
  return parts.join(' ');
}
