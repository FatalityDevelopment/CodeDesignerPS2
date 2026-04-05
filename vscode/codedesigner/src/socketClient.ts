import * as net from 'net';

/**
 * Command message format for socket communication
 */
interface CommandMessage {
    Command: string;
    Data?: string;
    Address?: number;
    Length?: number;
    Region?: string;
    OutputPath?: string;
    InputPath?: string;
    BreakpointAddress?: number;
    Label?: string;
    Comment?: string;
    Query?: string;
    SearchMode?: string;
    RegisterName?: string;
    RegisterIndex?: number;
    RegisterValue?: string;
    // LISTDISCELFS / RECOMPILE fields
    IsoPath?: string;
    ElfName?: string;
    OutputDirectory?: string;
    // Runtime mode switch fields (control-server mode)
    BiosPath?: string;
    DiscPath?: string;
    Steps?: number;
    Pcsx2Host?: string;
    Pcsx2Slot?: number;
    // CONVERTCHEAT fields
    Format?: string;
    Direction?: string;
    // PORTCHEATS fields
    SourceDumpPath?: string;
    TargetDumpPath?: string;
    // COMPILECDS — pre-resolved pointer-manager labels (e.g. { address:'0x80100000', label:'seal.health' })
    Labels?: LabelEntry[];
    // MC_* fields
    McPath?: string;
    SaveName?: string;
    ExportDir?: string;
    ImportDir?: string;
    McOverwrite?: boolean;
    // HDD_* fields
    HddPath?: string;
    PartitionId?: string;
    FileName?: string;
    HddSizeGb?: number;
    // Save-state file path (SAVESTATETOFILE / LOADSTATEFROMFILE)
    FilePath?: string;
    // SCANVALUE fields
    Comparison?: string;
    DataType?: string;
    ScanValue?: string;
    EndAddress?: number;
    ScanAddresses?: number[];
    // ADDSTRUCT / DELETESTRUCT / ADDSTRUCTMEMBER fields
    StructName?: string;
    StructBaseAddress?: string;
    MemberOffset?: number;
    MemberName?: string;
    // READMEMREGION fields
    SearchPattern?: number[];
    // SETWATCHPOINT / CLEARWATCHPOINT fields
    WatchpointId?: string;
    WatchpointType?: string;
    // TRACECALLS fields
    MaxInstructions?: number;
    // RUNUNTILPC fields
    TargetPc?: number;
    TimeoutMs?: number;
    // RECORDNAV fields
    NavSource?: string;
    // GETADDRESSHISTORY fields
    SnapshotDir?: string;
    // Label Manager fields
    WorkspaceDir?: string;
    GameId?: string;
    SetName?: string;
    LabelEntry?: { address: string; name: string; comment?: string; members: { offset: number; name: string }[] };
}

/**
 * One entry in the symbol / label database.
 */
export interface LabelEntry {
    address: string;
    label:   string;
    comment?: string;
}

/**
 * A null-terminated ASCII string found in the binary.
 */
export interface StringEntry {
    address: string;
    value:   string;
}

/**
 * A detected function entry point.
 */
export interface FunctionEntry {
    address: string;
    label:   string;
}

/**
 * A 4-byte aligned value that looks like a valid EE or IOP memory pointer.
 */
export interface PointerEntry {
    address:  string;
    pointsTo: string;
}

/**
 * A plausible 32-bit IEEE-754 float found at a 4-byte aligned address.
 */
export interface FloatEntry {
    address: string;
    value:   number;
    raw:     string;
}

/**
 * A non-zero 4-byte aligned 32-bit signed integer found in memory.
 */
export interface IntegerEntry {
    address: string;
    value:   number;
    hex:     string;
}

/**
 * One result entry returned by the SCANVALUE command.
 */
export interface ScanValueResult {
    address: string;
    value:   string;
}

/**
 * One member field inside a struct definition.
 */
export interface StructMemberEntry {
    offset: number;
    name:   string;
}

/**
 * A user-defined struct with a name, base address, and optional member list.
 */
export interface StructEntry {
    name:        string;
    baseAddress: string;
    members:     StructMemberEntry[];
}

// ── Label Manager (unified label/struct) ──────────────────────────────────────

/** One member of a label that acts as a struct pointer. */
export interface LabelMemberEntry {
    offset: number;
    name:   string;
}

/**
 * A unified label / struct entry.
 * When members is empty this is a plain address label.
 * When members is non-empty it is a struct pointer definition.
 */
export interface LabelManagerEntry {
    address:  string;
    name:     string;
    comment?: string;
    members:  LabelMemberEntry[];
}

/** A named collection of LabelManagerEntry records for a specific game. */
export interface LabelSetPayload {
    setName:   string;
    gameId?:   string;
    isBuiltIn: boolean;
    entries:   LabelManagerEntry[];
}

/**
 * One entry in the server-side navigation history.
 */
export interface NavHistoryEntry {
    Address:   string;
    Label?:    string;
    Source:    string;
    Timestamp: string;
}

/**
 * One historical 4-byte value at an address, read from a saved snapshot.
 */
export interface AddressHistoryEntry {
    SnapshotName: string;
    Timestamp:    string;
    Value:        string;
    Disasm?:      string;
}

/**
 * One search match returned by the SEARCH command.
 */
export interface SearchMatch {
    instructionIndex: number;
    address:          number;
    field:            string;
    start:            number;
    length:           number;
}

/**
 * Register dump returned by GETREGS
 */
export interface RegisterDump {
    // GPR (lower 32 bits only — use gprLo32/gprHi32 for full 64-bit precision)
    gpr: number[];
    // 128-bit GPR precise representation as four uint32 arrays
    gprLo32: number[];    // bits[31:0]  of RegsLo[i]
    gprHi32: number[];    // bits[63:32] of RegsLo[i]
    gprUpLo32: number[];  // bits[31:0]  of RegsHi[i] (upper 64-bit lane)
    gprUpHi32: number[];  // bits[63:32] of RegsHi[i]
    pc: number;
    hi: number;
    lo: number;
    hi1: number;
    lo1: number;
    sa: number;
    // COP0 legacy convenience fields
    cop0Status: number;
    cop0Cause: number;
    cop0Epc: number;
    cop0Count: number;
    // COP0 full register file (32 entries)
    cop0Regs: number[];
    // FPR – 32 × raw IEEE-754 uint bit patterns; use rawBitsToFloat() to display
    fpr: number[];
    fprAcc: number;
    // FCR
    fcr0: number;
    fcr31: number;
    // VU0 float registers – 32 × 4 components, each raw IEEE-754 uint
    vu0fX: number[];
    vu0fY: number[];
    vu0fZ: number[];
    vu0fW: number[];
    // VU0 ACC / special
    vu0AccX: number;
    vu0AccY: number;
    vu0AccZ: number;
    vu0AccW: number;
    vu0Q: number;
    vu0P: number;
    vu0I: number;
    // VU0 integer registers (16 entries)
    vu0i: number[];
    // VU0 flags
    vu0Status: number;
    vu0Mac: number;
    vu0Clip: number;
    // GS privileged registers (19 entries – see order in HandleGetRegisters)
    gsPriv: number[];
}

/**
 * Response format for socket communication
 */
interface CommandResponse {
    Success: boolean;
    Command?: string;
    Message?: string;
    Error?: string;
    Data?: string;
    Results?: InstructionResult[];
    Regions?: SnapshotRegion[];
    Paused?: boolean;
    PC?: number;
    BreakpointHitAddress?: number;
    // GETREGS fields
    GPR?: number[];
    // Precise 32-bit halves of each 128-bit GPR (no floating-point precision loss)
    GPR_Lo32?: number[];
    GPR_Hi32?: number[];
    GPR_UpLo32?: number[];
    GPR_UpHi32?: number[];
    GPR_PC?: number;
    GPR_HI?: number;
    GPR_LO?: number;
    GPR_HI1?: number;
    GPR_LO1?: number;
    GPR_SA?: number;
    COP0_Status?: number;
    COP0_Cause?: number;
    COP0_EPC?: number;
    COP0_Count?: number;
    COP0_Regs?: number[];
    FPR?: number[];
    FPR_ACC?: number;
    FCR0?: number;
    FCR31?: number;
    VU0F_X?: number[];
    VU0F_Y?: number[];
    VU0F_Z?: number[];
    VU0F_W?: number[];
    VU0F_ACC_X?: number;
    VU0F_ACC_Y?: number;
    VU0F_ACC_Z?: number;
    VU0F_ACC_W?: number;
    VU0F_Q?: number;
    VU0F_P?: number;
    VU0F_I?: number;
    VU0I?: number[];
    VU0_Status?: number;
    VU0_MAC?: number;
    VU0_Clip?: number;
    GS_Priv?: number[];
    // Symbol panel results
    Labels?:    LabelEntry[];
    Strings?:   StringEntry[];
    Functions?: FunctionEntry[];
    Pointers?:  PointerEntry[];
    Floats?:    FloatEntry[];
    Integers?:  IntegerEntry[];
    Matches?:   SearchMatch[];
    // GETTHREADS
    Threads?: ThreadEntry[];
    // GETMODULES
    Modules?: ModuleEntry[];
    // Per-address analysis comments produced when an instruction executes.
    // Keyed by hex address string (e.g. "80001234"), value is the analysis comment.
    InstructionAnalysis?: Record<string, string>;
    // LISTDISCELFS fields
    ElfFiles?: DiscElfEntry[];
    BootElf?: string;
    // RECOMPILE fields
    FunctionCount?: number;
    WrittenFiles?: string[];
    // PORTCHEATS fields
    PortCheatResults?: Array<Record<string, unknown>>;
    // MC_LIST fields
    McSaves?: McSaveInfo[];
    // HDD_LIST / HDD_LIST_FILES fields
    HddPartitions?: HddPartitionInfo[];
    HddFiles?: HddFileEntryInfo[];
    // SCANVALUE results
    ScanResults?: ScanValueResult[];
    // GETSTRUCTS results
    Structs?: StructEntry[];
    // READMEMREGION results
    MatchAddresses?: number[];
    // SETWATCHPOINT / LISTWATCHPOINTS results
    WatchpointId?: string;
    Watchpoints?: WatchpointInfo[];
    // Watchpoint hit info (GETPC / GETWATCHPOINTHIT)
    WatchpointHitAddress?: number;
    WatchpointHitPC?: number;
    WatchpointHitId?: string;
    // TRACECALLS result
    TracedCalls?: CallTraceEntry[];
}

/**
 * One save-game directory entry returned by MC_LIST.
 */
export interface McSaveInfo {
    Name:       string;
    FileCount:  number;
    Created:    string;
    Modified:   string;
    TotalBytes: number;
    Files:      McFileInfo[];
}

/**
 * One file within a PS2 save-game directory.
 */
export interface McFileInfo {
    Name: string;
    Size: number;
}

/**
 * One partition entry returned by HDD_LIST.
 */
export interface HddPartitionInfo {
    Id:          string;
    Name:        string;
    StartSector: number;
    TotalBytes:  number;
    TypeName:    string;
    FileCount:   number;
    Created:     string;
    Files:       HddFileEntryInfo[];
}

/**
 * One file entry within a PS2 HDD partition.
 */
export interface HddFileEntryInfo {
    Name:        string;
    Size:        number;
    IsDirectory: boolean;
    Modified:    string;
}

/**
 * Summary of a single active memory watchpoint returned by listWatchpoints().
 */
export interface WatchpointInfo {
    /** Unique watchpoint identifier (e.g. "wp-0"). */
    Id:      string;
    /** Virtual EE address as an 8-digit hex string (e.g. "01E7EA60"). */
    Address: string;
    /** Number of bytes watched. */
    Size:    number;
    /** Access type: "write", "read", or "readwrite". */
    Type:    string;
}

/**
 * A single JAL/JALR call-site record returned by traceCalls().
 * From = address of the call instruction (KSEG0 hex string).
 * To   = resolved call target after the delay slot (KSEG0 hex string).
 */
export interface CallTraceEntry {
    From: string;
    To:   string;
}

/**
 * Result for individual instruction assembly/disassembly
 */
interface InstructionResult {
    Success: boolean;
    Assembly?: string;
    Hex?: string;
    Error?: string;
    Comment?: string;
    Label?: string;
    LineNumber?: number;
}

/**
 * One memory region written by a SNAPSHOT command.
 */
export interface SnapshotRegion {
    Label: string;
    File: string;
    BaseAddress: number;
    Size: number;
}

/**
 * One EE OS thread entry returned by GETTHREADS.
 */
export interface ThreadEntry {
    id: number;
    status: string;
    pc: number;
    sp: number;
    priority: number;
    entryPc: number;
}

/**
 * One IOP module entry returned by GETMODULES.
 */
export interface ModuleEntry {
    name:     string;
    version:  number;
    textAddr: string;
    entry:    string;
    gp:       string;
    textSize: number;
    dataSize: number;
    bssSize:  number;
}

/**
 * One ELF or IRX file found on a PS2 disc image.
 */
export interface DiscElfEntry {
    /** Absolute disc path, e.g. "/SCUS_971.98;1" or "/MODULES/CDVD.IRX;1". */
    Path: string;
    /** Filename without path or version suffix, e.g. "SCUS_971.98". */
    Name: string;
    /** File size in bytes. */
    Size: number;
    /** True when this is the BOOT2 executable listed in SYSTEM.CNF. */
    IsBootElf: boolean;
}

/**
 * Result for a single cheat code line produced by PORTCHEATS.
 */
export interface PortCheatResult {
    /** Zero-based index of the cheat block; use this to distinguish blocks that share the same label. */
    blockIndex: number;
    /** The #Name label of the cheat block this line belongs to. */
    blockName: string;
    /** Any non-label, non-code description text found in the cheat block. Null when absent. */
    blockText: string | null;
    /** Original GameShark code line, e.g. "20123456 000003E8". */
    originalCode: string;
    /** Ported code line with the updated address, e.g. "20124ABC 000003E8". Null when not found. */
    newCode: string | null;
    /** Whether a match was found in the target dump. */
    found: boolean;
    /** Description of the method used, e.g. "HexPattern-48". */
    method: string;
    /** 0–100 confidence score. */
    confidence: number;
    /** True when more than one candidate address was found. */
    ambiguous: boolean;
    /** All candidate new code lines when ambiguous. */
    candidates: string[] | null;
}

/**
 * Result returned by the RECOMPILE socket command.
 */
export interface RecompileResult {
    /** Number of functions that were recompiled. */
    functionCount: number;
    /** Absolute paths of all files written by the recompiler. */
    writtenFiles: string[];
    /** The ELF path (or name) that was recompiled. */
    bootElf: string;
}

/**
 * Pushed unsolicited by the server when an MCP client performs an address-based
 * operation (READMEM, WRITEMEM, DISASSEMBLE, GETPC, …).  Used by the VSCode
 * extension to implement "Follow AI" mode — navigate the disassembly cursor to
 * the address the AI is currently working with.
 */
export interface AiActivityEvent {
    type:    'ai_activity';
    command: string;
    address: number;
    region:  string;
}

/**
 * Socket client for communication with C# server
 */
export class SocketClient {
    private client: net.Socket | null = null;
    private host: string;
    private port: number;
    private connected: boolean = false;
    private wasConnected: boolean = false;
    private messageBuffer: string = '';
    private messageHandlers: Array<(message: string) => void> = [];
    private connectionHandlers: Array<() => void> = [];
    private disconnectionHandlers: Array<() => void> = [];
    /**
     * Handlers registered via onActivityNotification() — invoked when the server
     * pushes an unsolicited AI-activity event (type === "ai_activity").
     */
    private _activityHandlers: Array<(event: AiActivityEvent) => void> = [];
    /**
     * FIFO queue of one-time response handlers added by sendCommand().
     * Using a dedicated queue (with shift()) avoids the Array.forEach +
     * splice-during-iteration bug: when forEach iterates [A, B, C] and A
     * splices itself out at index 0, forEach advances to index 1 which is
     * now C (B shifted to 0), so B is skipped and C receives A's response.
     * With shift(), each incoming message pops exactly one handler off the
     * front of the queue in FIFO order — matching the order commands were sent.
     */
    private _pendingResponses: Array<(message: string) => void> = [];
    /**
     * If a connection attempt is in progress, this holds the shared Promise so
     * concurrent callers (e.g. extension auto-connect + connectSocketAndOpenViews)
     * both await the same attempt rather than creating a second socket that
     * destroys the first one's listeners and leaves its Promise permanently pending.
     */
    private _connectingPromise: Promise<void> | null = null;

    constructor(host: string = 'localhost', port: number = 8765) {
        this.host = host;
        this.port = port;
    }

    /**
     * Connect to the C# server
     */
    public connect(): Promise<void> {
        if (this.connected) {
            console.log('Already connected to server');
            return Promise.resolve();
        }

        // If a connection attempt is already in progress, return the shared
        // promise so concurrent callers wait on the same attempt rather than
        // each spawning a new socket that would destroy the other's listeners
        // and leave its Promise permanently pending.
        if (this._connectingPromise) {
            console.log('[SOCKET] Connection already in progress, sharing existing attempt');
            return this._connectingPromise;
        }

        // Destroy any previous socket that may still be open (e.g. a
        // failed prior connection attempt that was never cleaned up).
        // This prevents a stale 'close' event from resetting state after
        // the new socket has connected successfully.
        if (this.client) {
            this.client.removeAllListeners();
            this.client.destroy();
            this.client = null;
        }

        // Reset the message buffer so that any partial JSON left over from
        // a previous (dropped) connection cannot combine with data from the
        // new connection and form a garbled/incorrect response message.
        this.messageBuffer = '';

        this._connectingPromise = new Promise<void>((resolve, reject) => {
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

            this.client.on('data', (data: Buffer) => {
                const chunk = data.toString('utf8');
                console.log(`[SOCKET] Received chunk (${chunk.length} chars)`);
                
                // Add to buffer
                this.messageBuffer += chunk;
                
                // Process complete messages (delimited by newline)
                let newlineIndex: number;
                while ((newlineIndex = this.messageBuffer.indexOf('\n')) !== -1) {
                    const message = this.messageBuffer.substring(0, newlineIndex).trim();
                    this.messageBuffer = this.messageBuffer.substring(newlineIndex + 1);
                    
                    if (message.length > 0) {
                        console.log(`[SOCKET] Complete message received (${message.length} chars): ${message.substring(0, 100)}...`);

                        // Detect server-pushed notifications (have a "type" field but no
                        // "Success" field — all CommandResponse objects carry "Success").
                        // These must NOT consume a pending FIFO response slot.
                        let parsed: Record<string, unknown> | null = null;
                        try { parsed = JSON.parse(message); } catch { /* not JSON */ }
                        if (parsed && typeof parsed['type'] === 'string' && !('Success' in parsed)) {
                            if (parsed['type'] === 'ai_activity') {
                                const evt = parsed as unknown as AiActivityEvent;
                                this._activityHandlers.forEach(h => h(evt));
                            }
                            this.messageHandlers.forEach(handler => handler(message));
                            continue;
                        }

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

            this.client.on('error', (error: Error) => {
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
                // Cancel any in-flight requests: they will never receive a response
                // now that the connection has been dropped.  This mirrors what
                // disconnect() does for intentional disconnects.
                const stale = this._pendingResponses.splice(0);
                for (const handler of stale) {
                    try { handler('{"Success":false,"Error":"Connection closed"}'); } catch { /* ignore */ }
                }
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
        }).finally(() => {
            this._connectingPromise = null;
        });

        return this._connectingPromise;
    }

    /**
     * Send a message to the C# server
     */
    public sendMessage(message: string): Promise<void> {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }

            this.client.write(message + '\n', 'utf8', (error) => {
                if (error) {
                    console.error(`[SOCKET] Error sending message: ${error.message}`);
                    reject(error);
                } else {
                    console.log(`[SOCKET] Sent: ${message}`);
                    resolve();
                }
            });
        });
    }

    /**
     * Disconnect from the server
     */
    public disconnect(): void {
        if (this.client) {
            this.client.destroy();
            this.client = null;
        }
        this.connected = false;
        this.wasConnected = false;
        // Cancel any in-flight requests that will never receive a response.
        const stale = this._pendingResponses.splice(0);
        for (const handler of stale) {
            try { handler('{"Success":false,"Error":"Disconnected"}'); } catch { /* ignore */ }
        }
        console.log('[SOCKET] Disconnected from server');
    }

    /**
     * Register a message handler
     */
    public onMessage(handler: (message: string) => void): void {
        this.messageHandlers.push(handler);
    }

    /**
     * Register a connection handler
     */
    public onConnected(handler: () => void): void {
        this.connectionHandlers.push(handler);
    }

    /**
     * Register a disconnection handler
     */
    public onDisconnected(handler: () => void): void {
        this.disconnectionHandlers.push(handler);
    }

    /**
     * Register a handler for server-pushed AI-activity notifications.
     * Called when the server broadcasts an unsolicited ai_activity event
     * (e.g. because the MCP AI assistant performed a memory read or write).
     */
    public onActivityNotification(handler: (event: AiActivityEvent) => void): void {
        this._activityHandlers.push(handler);
    }

    /**
     * Send a SUBSCRIBE_ACTIVITY command to opt-in to AI-activity push notifications.
     * After calling this, the server will push {"type":"ai_activity",...} messages
     * whenever another client (e.g. the MCP server) performs an address-based operation.
     */
    public async subscribeActivity(): Promise<void> {
        try {
            await this.sendCommand({ Command: 'SUBSCRIBE_ACTIVITY' });
        } catch (err) {
            console.warn('[SOCKET] subscribeActivity failed:', err);
        }
    }

    /**
     * Check if connected
     */
    public isConnected(): boolean {
        return this.connected;
    }

    /**
     * Send a command and wait for response
     */
    private sendCommand(command: CommandMessage, timeoutMs: number = 10000): Promise<CommandResponse> {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }

            let responseReceived = false;

            // Set up one-time response handler
            const responseHandler = (message: string) => {
                if (responseReceived) return;
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
                    const response: CommandResponse = JSON.parse(cleanMessage);
                    console.log('[SOCKET] Successfully parsed response:', response.Success ? 'SUCCESS' : 'FAILED');
                    resolve(response);
                } catch (error) {
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
     * Compile a .cds (Code Designer Script) assembly file.
     * Forwards the raw source text to the backend COMPILECDS command.
     * @param labels Optional pre-resolved pointer-manager labels (e.g. from PointerManagerProvider.getResolvedLabels()).
     *               These are injected into the compiler so CDS source can reference them as `:seal.health` or `@seal.health`.
     */
    public async compileCds(sourceCode: string, labels?: LabelEntry[]): Promise<InstructionResult[]> {
        const command: CommandMessage = {
            Command: 'COMPILECDS',
            Data: sourceCode
        };
        if (labels && labels.length > 0) {
            command.Labels = labels;
        }

        const response = await this.sendCommand(command);

        if (!response.Success) {
            throw new Error(response.Error || 'Compilation failed');
        }

        return response.Results || [];
    }

    /**
     * Assemble MIPS assembly code to hex
     */
    public async assemble(asmCode: string): Promise<InstructionResult[]> {
        const command: CommandMessage = {
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
    public async disassemble(hexCode: string, startAddress?: number): Promise<InstructionResult[]> {
        const command: CommandMessage = {
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
    public async readMemory(address: number, length: number, region: string = 'ee'): Promise<Buffer> {
        const command: CommandMessage = {
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
     * Read a contiguous region of EE RAM (up to 16 MB) in one call.
     * Optionally search for a byte pattern within the result.
     *
     * @param start          - Inclusive start virtual EE address.
     * @param end            - Exclusive end virtual EE address (must be > start).
     * @param searchPattern  - Optional byte pattern to search for.
     * @param region         - "ee" (default) or "iop".
     * @returns              - Hex-string of all bytes and list of absolute match addresses.
     */
    public async readMemoryRegion(
        start: number,
        end: number,
        searchPattern?: number[],
        region: string = 'ee',
    ): Promise<{ bytes: string; matches: number[] }> {
        const command: CommandMessage = {
            Command: 'READMEMREGION',
            Address: start,
            EndAddress: end,
            SearchPattern: searchPattern,
            Region: region,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ReadMemRegion failed');
        }
        return {
            bytes:   response.Data ?? '',
            matches: response.MatchAddresses ?? [],
        };
    }

    /**
     * Set a write watchpoint on [address, address+size).
     * When any EE store instruction writes to the range the emulator pauses.
     *
     * @param address - Start virtual EE address to watch.
     * @param size    - Number of bytes to watch (default 4).
     * @param type    - "write" (default); "read"/"readwrite" reserved for future use.
     * @returns       - Assigned watchpoint ID (e.g. "wp-0").
     */
    public async setWatchpoint(
        address: number,
        size: number = 4,
        type: string = 'write',
    ): Promise<string> {
        const command: CommandMessage = {
            Command: 'SETWATCHPOINT',
            Address: address,
            Length: size,
            WatchpointType: type,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'SetWatchpoint failed');
        }
        return response.WatchpointId ?? '';
    }

    /**
     * Remove the watchpoint with the given ID.
     * @param id - Watchpoint ID returned by setWatchpoint().
     */
    public async clearWatchpoint(id: string): Promise<void> {
        const command: CommandMessage = { Command: 'CLEARWATCHPOINT', WatchpointId: id };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ClearWatchpoint failed');
        }
    }

    /** Remove every registered watchpoint. */
    public async clearAllWatchpoints(): Promise<void> {
        const command: CommandMessage = { Command: 'CLEARALLWATCHPOINTS' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ClearAllWatchpoints failed');
        }
    }

    /** Return all currently active watchpoints. */
    public async listWatchpoints(): Promise<WatchpointInfo[]> {
        const command: CommandMessage = { Command: 'LISTWATCHPOINTS' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ListWatchpoints failed');
        }
        return response.Watchpoints ?? [];
    }

    /**
     * Return the details of the most recently fired watchpoint.
     * Throws if no watchpoint has fired since the last resume.
     */
    public async getWatchpointHit(): Promise<{
        watchpointId: string;
        physAddress: number;
        hitPC: number;
    }> {
        const command: CommandMessage = { Command: 'GETWATCHPOINTHIT' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetWatchpointHit failed');
        }
        return {
            watchpointId: response.WatchpointHitId   ?? '',
            physAddress:  response.WatchpointHitAddress ?? 0,
            hitPC:        response.WatchpointHitPC    ?? 0,
        };
    }

    /**
     * Step the emulator instruction-by-instruction from the current PC to endPc,
     * recording every JAL/JALR call site encountered.
     * The emulator must be paused before calling this method.
     *
     * @param endPc           - Exclusive target PC at which tracing stops.
     * @param maxInstructions - Safety cap on instructions stepped (default 10 000).
     * @returns               - Ordered list of { From, To } call-site entries.
     */
    public async traceCalls(
        endPc: number,
        maxInstructions: number = 10_000,
    ): Promise<CallTraceEntry[]> {
        const command: CommandMessage = {
            Command: 'TRACECALLS',
            EndAddress: endPc,
            MaxInstructions: maxInstructions,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'TraceCalls failed');
        }
        return response.TracedCalls ?? [];
    }

    /**
     * Resume the emulator and block (server-side) until the EE PC reaches
     * targetPc.  Returns the full register dump at the hit point.
     *
     * @param targetPc  - EE virtual address to run to.
     * @param timeoutMs - Timeout in milliseconds (default 10 000).
     * @returns         - Full register dump (same shape as getRegisters()).
     */
    public async runUntilPc(targetPc: number, timeoutMs: number = 10_000): Promise<RegisterDump> {
        const command: CommandMessage = {
            Command: 'RUNUNTILPC',
            TargetPc: targetPc,
            TimeoutMs: timeoutMs,
        };
        // Give the TCP socket extra headroom over the server-side timeout.
        const response = await this.sendCommand(command, timeoutMs + 5_000);
        if (!response.Success) {
            throw new Error(response.Error || 'RunUntilPc failed');
        }
        return this.parseRegisterDump(response);
    }

    /**
     * Resume PCSX2 and block until any registered memory watchpoint fires
     * (the watched address value changes), then return hit details.
     *
     * Only supported in PINE mode. Uses memory polling (~5 ms interval);
     * the returned PC is approximate.
     *
     * @param timeoutMs - Maximum wait time in milliseconds (default 15 000).
     * @returns Watchpoint hit info: watchpoint ID, virtual address, and approximate PC.
     */
    public async runUntilWatchpoint(timeoutMs: number = 15_000): Promise<{
        watchpointId: string;
        hitAddress:   number;
        hitPC:        number;
    }> {
        const command: CommandMessage = {
            Command: 'RUNUNTILWATCHPOINT',
            TimeoutMs: timeoutMs,
        };
        const response = await this.sendCommand(command, timeoutMs + 5_000);
        if (!response.Success) {
            throw new Error(response.Error || 'RunUntilWatchpoint failed');
        }
        return {
            watchpointId: response.WatchpointHitId      ?? '',
            hitAddress:   response.WatchpointHitAddress ?? 0,
            hitPC:        response.WatchpointHitPC      ?? 0,
        };
    }

    /**
     * Take a snapshot: write all memory regions to .bin files in outputPath.
     * Returns the list of written region descriptors.
     */
    public async snapshot(outputPath: string): Promise<SnapshotRegion[]> {
        const command: CommandMessage = {
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
     * Reload a snapshot as a save state: read .bin files from inputPath and
     * restore them into the live emulator memory.  The emulator is paused
     * automatically and remains paused after the restore completes.
     * Returns the list of restored region descriptors.
     */
    public async loadSnapshot(inputPath: string): Promise<SnapshotRegion[]> {
        const command: CommandMessage = {
            Command: 'LOADSNAPSHOT',
            InputPath: inputPath,
        };

        const response = await this.sendCommand(command);

        if (!response.Success) {
            throw new Error(response.Error || 'Load snapshot failed');
        }

        return response.Regions || [];
    }

    /**
     * Pause emulator execution
     */
    public async pause(): Promise<void> {
        const command: CommandMessage = { Command: 'PAUSE' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Pause failed');
        }
    }

    /**
     * Resume emulator execution
     */
    public async resume(): Promise<void> {
        const command: CommandMessage = { Command: 'RESUME' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Resume failed');
        }
    }

    /**
     * Set a breakpoint at the given EE virtual address
     */
    public async setBreakpoint(address: number): Promise<void> {
        const command: CommandMessage = { Command: 'SETBP', BreakpointAddress: address };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'SetBreakpoint failed');
        }
    }

    /**
     * Clear the breakpoint at the given EE virtual address
     */
    public async clearBreakpoint(address: number): Promise<void> {
        const command: CommandMessage = { Command: 'CLEARBP', BreakpointAddress: address };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ClearBreakpoint failed');
        }
    }

    /**
     * Clear all breakpoints
     */
    public async clearAllBreakpoints(): Promise<void> {
        const command: CommandMessage = { Command: 'CLEARBP_ALL' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ClearAllBreakpoints failed');
        }
    }

    /**
     * Get the current EE PC and paused/breakpoint state.
     * When a memory watchpoint fires the response also includes
     * watchpointHitAddress, watchpointHitPC, and watchpointHitId.
     */
    public async getPC(): Promise<{
        paused: boolean;
        pc: number;
        breakpointHitAddress?: number;
        watchpointHitAddress?: number;
        watchpointHitPC?: number;
        watchpointHitId?: string;
    }> {
        const command: CommandMessage = { Command: 'GETPC' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetPC failed');
        }
        return {
            paused: response.Paused ?? false,
            pc: response.PC ?? 0,
            breakpointHitAddress: response.BreakpointHitAddress ?? undefined,
            watchpointHitAddress: response.WatchpointHitAddress ?? undefined,
            watchpointHitPC:      response.WatchpointHitPC      ?? undefined,
            watchpointHitId:      response.WatchpointHitId      ?? undefined,
        };
    }

    /**
     * Execute exactly one EE instruction while keeping the emulator paused.
     * Returns the new PC / paused / breakpoint state and any per-address
     * analysis comments produced by executing the instruction with the real
     * register state.
     */
    public async step(): Promise<{ paused: boolean; pc: number; breakpointHitAddress?: number; instructionAnalysis?: Record<string, string> }> {
        const command: CommandMessage = { Command: 'STEP' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Step failed');
        }
        return {
            paused: response.Paused ?? true,
            pc: response.PC ?? 0,
            breakpointHitAddress: response.BreakpointHitAddress ?? undefined,
            instructionAnalysis: response.InstructionAnalysis,
        };
    }

    /**
     * PCSX2 PINE: step into (non-blocking). Emulator will run until the next pause/breakpoint.
     */
    public async stepInto(): Promise<void> {
        const response = await this.sendCommand({ Command: 'STEPINTO' });
        if (!response.Success) {
            throw new Error(response.Error || 'StepInto failed');
        }
    }

    /**
     * PCSX2 PINE: step over (non-blocking). Emulator will run until the next pause/breakpoint.
     */
    public async stepOver(): Promise<void> {
        const response = await this.sendCommand({ Command: 'STEPOVER' });
        if (!response.Success) {
            throw new Error(response.Error || 'StepOver failed');
        }
    }

    /**
     * PCSX2 PINE: save emulator state to a named file path.
     * Uses the SAVESTATETOFILE command which delegates to PineClient.SaveStateFile (opcode 0x22).
     */
    public async saveStateFile(filePath: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'SAVESTATETOFILE', FilePath: filePath });
        if (!response.Success) {
            throw new Error(response.Error || 'SaveStateFile failed');
        }
    }

    /**
     * Restart the emulator (reset both CPUs to power-on state, then pause)
     */
    public async restart(): Promise<void> {
        const command: CommandMessage = { Command: 'RESTART' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'Restart failed');
        }
    }

    /**
     * Runtime mode switch: start (or keep) the built-in emulator in control-server mode.
     */
    public async startEmulator(biosPath?: string, discPath?: string, steps?: number): Promise<void> {
        const command: CommandMessage = { Command: 'STARTEMULATOR' };
        if (biosPath) { command.BiosPath = biosPath; }
        if (discPath) { command.DiscPath = discPath; }
        if (steps !== undefined) { command.Steps = steps; }

        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'STARTEMULATOR failed');
        }
    }

    /**
     * Runtime mode switch: start PCSX2 bridge mode in control-server mode.
     */
    public async startPcsx2(host?: string, slot?: number): Promise<void> {
        const command: CommandMessage = { Command: 'STARTPCSX2' };
        if (host) { command.Pcsx2Host = host; }
        if (slot !== undefined) { command.Pcsx2Slot = slot; }

        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'STARTPCSX2 failed');
        }
    }

    /**
     * Get a full CPU register dump from the running emulator
     */
    public async getRegisters(): Promise<RegisterDump> {
        const command: CommandMessage = { Command: 'GETREGS' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetRegisters failed');
        }
        return this.parseRegisterDump(response);
    }

    /** Convert a raw CommandResponse (from GETREGS or RUNUNTILPC) to a RegisterDump. */
    private parseRegisterDump(response: CommandResponse): RegisterDump {
        return {
            gpr:        response.GPR        ?? new Array(32).fill(0),
            gprLo32:    response.GPR_Lo32   ?? new Array(32).fill(0),
            gprHi32:    response.GPR_Hi32   ?? new Array(32).fill(0),
            gprUpLo32:  response.GPR_UpLo32 ?? new Array(32).fill(0),
            gprUpHi32:  response.GPR_UpHi32 ?? new Array(32).fill(0),
            pc:         response.GPR_PC     ?? 0,
            hi:         response.GPR_HI     ?? 0,
            lo:         response.GPR_LO     ?? 0,
            hi1:        response.GPR_HI1    ?? 0,
            lo1:        response.GPR_LO1    ?? 0,
            sa:         response.GPR_SA     ?? 0,
            cop0Status: response.COP0_Status ?? 0,
            cop0Cause:  response.COP0_Cause  ?? 0,
            cop0Epc:    response.COP0_EPC    ?? 0,
            cop0Count:  response.COP0_Count  ?? 0,
            cop0Regs:   response.COP0_Regs  ?? new Array(32).fill(0),
            fpr:        response.FPR        ?? new Array(32).fill(0),
            fprAcc:     response.FPR_ACC    ?? 0,
            fcr0:       response.FCR0       ?? 0,
            fcr31:      response.FCR31      ?? 0,
            vu0fX:      response.VU0F_X     ?? new Array(32).fill(0),
            vu0fY:      response.VU0F_Y     ?? new Array(32).fill(0),
            vu0fZ:      response.VU0F_Z     ?? new Array(32).fill(0),
            vu0fW:      response.VU0F_W     ?? new Array(32).fill(0),
            vu0AccX:    response.VU0F_ACC_X ?? 0,
            vu0AccY:    response.VU0F_ACC_Y ?? 0,
            vu0AccZ:    response.VU0F_ACC_Z ?? 0,
            vu0AccW:    response.VU0F_ACC_W ?? 0,
            vu0Q:       response.VU0F_Q     ?? 0,
            vu0P:       response.VU0F_P     ?? 0,
            vu0I:       response.VU0F_I     ?? 0,
            vu0i:       response.VU0I       ?? new Array(16).fill(0),
            vu0Status:  response.VU0_Status ?? 0,
            vu0Mac:     response.VU0_MAC    ?? 0,
            vu0Clip:    response.VU0_Clip   ?? 0,
            gsPriv:     response.GS_Priv    ?? new Array(19).fill(0),
        };
    }

    /**
     * Write raw bytes to live EE or IOP memory.
     * @param address  Target virtual address
     * @param hexBytes Byte data as a hex string (e.g. "24020005" or "24 02 00 05")
     * @param region   'ee' (default) or 'iop'
     */
    public async writeMemory(address: number, hexBytes: string, region: string = 'ee'): Promise<void> {
        const command: CommandMessage = {
            Command: 'WRITEMEM',
            Address: address,
            Data:    hexBytes,
            Region:  region,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'WriteMemory failed');
        }
    }

    /**
     * Load a binary file from disk directly into live EE or IOP memory.
     * The file is read server-side, avoiding the need to transfer its contents
     * over the socket (which would time out for large dumps like 32 MB EE RAM).
     * @param filePath Absolute path to the binary file on the server machine
     * @param address  Target virtual address
     * @param region   'ee' (default) or 'iop'
     */
    public async loadBinFromFile(filePath: string, address: number, region: string = 'ee'): Promise<void> {
        const command: CommandMessage = {
            Command:   'LOADBIN',
            InputPath: filePath,
            Address:   address,
            Region:    region,
        };
        // Use a generous timeout — large files (e.g. 32 MB EE RAM) take time to
        // read from disk and copy into emulator memory even though no file data
        // is transferred over the socket.
        const response = await this.sendCommand(command, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'LoadBin failed');
        }
    }

    /**
     * Update a single EE CPU register.
     * @param name   Register type: 'GPR', 'PC', 'HI', 'LO', 'HI1', 'LO1', 'SA', 'COP0', 'FPR'
     * @param index  Register index (0-31 for GPR/COP0/FPR; ignored for PC/HI/LO/…)
     * @param value  New value as a decimal string or "0x"-prefixed hex string
     */
    public async setRegister(name: string, index: number, value: string): Promise<void> {
        const command: CommandMessage = {
            Command:       'SETREG',
            RegisterName:  name,
            RegisterIndex: index,
            RegisterValue: value,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'SetRegister failed');
        }
    }

    /**
     * Fetch all labels from the server's symbol database.
     */
    public async getLabels(): Promise<LabelEntry[]> {
        const response = await this.sendCommand({ Command: 'GETLABELS' });
        if (!response.Success) {
            throw new Error(response.Error || 'GetLabels failed');
        }
        return response.Labels ?? [];
    }

    /**
     * Add or update a label in the server's symbol database.
     * The server persists the change to ps2MemMap.json immediately.
     */
    public async addLabel(address: number, label: string, comment?: string): Promise<void> {
        const cmd: CommandMessage = { Command: 'ADDLABEL', Address: address, Label: label };
        if (comment) { cmd.Comment = comment; }
        const response = await this.sendCommand(cmd);
        if (!response.Success) {
            throw new Error(response.Error || 'AddLabel failed');
        }
    }

    /**
     * Delete a label from the server's symbol database by address.
     */
    public async deleteLabel(address: number): Promise<void> {
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
    public async getStrings(binaryData: Buffer, startAddress: number): Promise<StringEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETSTRINGS', Address: startAddress, Data: binaryData.toString('base64') },
            60000   // large binary may take a few seconds
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
    public async getFunctions(binaryData: Buffer, startAddress: number): Promise<FunctionEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETFUNCTIONS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetFunctions failed');
        }
        return response.Functions ?? [];
    }

    /**
     * Scan binary data for 4-byte aligned values that look like valid EE memory pointers.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getPointers(binaryData: Buffer, startAddress: number): Promise<PointerEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETPOINTERS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetPointers failed');
        }
        return response.Pointers ?? [];
    }

    /**
     * Scan binary data for plausible 32-bit IEEE-754 float values.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getFloats(binaryData: Buffer, startAddress: number): Promise<FloatEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETFLOATS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetFloats failed');
        }
        return response.Floats ?? [];
    }

    /**
     * Scan binary data for non-zero 4-byte aligned 32-bit signed integers.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getIntegers(binaryData: Buffer, startAddress: number): Promise<IntegerEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETINTEGERS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetIntegers failed');
        }
        return response.Integers ?? [];
    }

    /**
     * Scan a live emulator memory region for null-terminated ASCII strings.
     * The C# server reads the bytes directly — no binary data is transferred.
     * @param address  Virtual start address of the region.
     * @param length   Number of bytes to scan.
     * @param region   "ee" or "iop".
     */
    public async scanLiveStrings(address: number, length: number, region: string): Promise<StringEntry[]> {
        const response = await this.sendCommand(
            { Command: 'SCANSTRINGS', Address: address, Length: length, Region: region },
            120000
        );
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
    public async scanLiveFunctions(address: number, length: number, region: string): Promise<FunctionEntry[]> {
        const response = await this.sendCommand(
            { Command: 'SCANFUNCTIONS', Address: address, Length: length, Region: region },
            120000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ScanFunctions failed');
        }
        return response.Functions ?? [];
    }

    /**
     * Scan a live emulator memory region for values that look like valid memory pointers.
     * The C# server reads the bytes directly — no binary data is transferred.
     * @param address  Virtual start address of the region.
     * @param length   Number of bytes to scan.
     * @param region   "ee" or "iop".
     */
    public async scanLivePointers(address: number, length: number, region: string): Promise<PointerEntry[]> {
        const response = await this.sendCommand(
            { Command: 'SCANPOINTERS', Address: address, Length: length, Region: region },
            120000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ScanPointers failed');
        }
        return response.Pointers ?? [];
    }

    /**
     * Scan a live emulator memory region for plausible 32-bit IEEE-754 float values.
     * The C# server reads the bytes directly — no binary data is transferred.
     * @param address  Virtual start address of the region.
     * @param length   Number of bytes to scan.
     * @param region   "ee" or "iop".
     */
    public async scanLiveFloats(address: number, length: number, region: string): Promise<FloatEntry[]> {
        const response = await this.sendCommand(
            { Command: 'SCANFLOATS', Address: address, Length: length, Region: region },
            120000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ScanFloats failed');
        }
        return response.Floats ?? [];
    }

    /**
     * Scan a live emulator memory region for non-zero 32-bit signed integers.
     * The C# server reads the bytes directly — no binary data is transferred.
     * @param address  Virtual start address of the region.
     * @param length   Number of bytes to scan.
     * @param region   "ee" or "iop".
     */
    public async scanLiveIntegers(address: number, length: number, region: string): Promise<IntegerEntry[]> {
        const response = await this.sendCommand(
            { Command: 'SCANINTEGERS', Address: address, Length: length, Region: region },
            120000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ScanIntegers failed');
        }
        return response.Integers ?? [];
    }

    /**
     * Scan live memory for addresses whose value matches a comparison against a target value.
     * On first scan (no previousAddresses) the entire [startAddress, endAddress) range is searched.
     * On subsequent scans pass previousAddresses to refine (filter) the existing result set.
     *
     * @param startAddress    Inclusive start of the scan range.
     * @param endAddress      Exclusive end of the scan range.
     * @param value           Value to compare (decimal or 0x-prefixed hex).
     * @param comparison      "eq" | "ne" | "lt" | "gt" | "le" | "ge"
     * @param dataType        "uint8"|"int8"|"uint16"|"int16"|"uint32"|"int32"|"uint64"|"int64"|"float32"|"float64"
     * @param region          "ee" or "iop".
     * @param previousAddresses  Previous result addresses for refinement scans.
     */
    public async scanValue(
        startAddress: number,
        endAddress: number,
        value: string,
        comparison: string,
        dataType: string,
        region: string,
        previousAddresses?: number[]
    ): Promise<{ results: ScanValueResult[]; message?: string }> {
        const command: CommandMessage = {
            Command:       'SCANVALUE',
            Address:       startAddress,
            EndAddress:    endAddress,
            ScanValue:     value,
            Comparison:    comparison,
            DataType:      dataType,
            Region:        region,
            ScanAddresses: previousAddresses,
        };
        const response = await this.sendCommand(command, 300000);
        if (!response.Success) {
            throw new Error(
                response.Error
                    || `ScanValue failed (value="${value}", type=${dataType}, range=0x${startAddress.toString(16)}-0x${endAddress.toString(16)})`
            );
        }
        return { results: response.ScanResults ?? [], message: response.Message ?? undefined };
    }

    /**
     * For live panels pass address+length+region; for file panels pass fileData.
     * @param address   Virtual start address (used for live searches and match address calculation).
     * @param length    Byte length (required for live searches, ignored when fileData provided).
     * @param query     The search string, byte pattern, or regex.
     * @param mode      "text" | "bytes" | "string" | "regex"
     * @param region    "ee" or "iop" — only used for live searches.
     * @param fileData  Binary data buffer — when provided the server searches this instead of live memory.
     */
    public async search(
        address: number,
        length: number,
        query: string,
        mode: string,
        region?: string,
        fileData?: Buffer
    ): Promise<SearchMatch[]> {
        const command: CommandMessage = {
            Command:    'SEARCH',
            Address:    address,
            Length:     length,
            Query:      query,
            SearchMode: mode,
            Region:     region,
            Data:       fileData ? fileData.toString('base64') : undefined,
        };
        const response = await this.sendCommand(command, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'Search failed');
        }
        return response.Matches ?? [];
    }

    /**
     * Get EE OS thread list from the emulator.
     */
    public async getThreads(): Promise<ThreadEntry[]> {
        const command: CommandMessage = { Command: 'GETTHREADS' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetThreads failed');
        }
        return (response.Threads ?? []).map(t => ({
            id:       t.id       ?? 0,
            status:   t.status   ?? '',
            pc:       t.pc       ?? 0,
            sp:       t.sp       ?? 0,
            priority: t.priority ?? 0,
            entryPc:  t.entryPc  ?? 0,
        }));
    }

    /**
     * Get the list of loaded IOP modules (PINE mode only).
     */
    public async getModules(): Promise<ModuleEntry[]> {
        const command: CommandMessage = { Command: 'GETMODULES' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetModules failed');
        }
        return (response.Modules ?? []).map(m => ({
            name:     m.name     ?? '',
            version:  m.version  ?? 0,
            textAddr: m.textAddr ?? '0x00000000',
            entry:    m.entry    ?? '0x00000000',
            gp:       m.gp       ?? '0x00000000',
            textSize: m.textSize ?? 0,
            dataSize: m.dataSize ?? 0,
            bssSize:  m.bssSize  ?? 0,
        }));
    }

    /**
     * PCSX2 PINE: load emulator state from a named .p2s file path.
     * Uses the LOADSTATEFROMFILE command which delegates to PineClient.LoadStateFile (opcode 0x23).
     */
    public async loadStateFile(filePath: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'LOADSTATEFROMFILE', FilePath: filePath });
        if (!response.Success) {
            throw new Error(response.Error || 'LoadStateFile failed');
        }
    }

    /**
     * Extract memory dump files from a PCSX2 .p2s save-state archive to a
     * companion directory (<path>.extracted/) via the EXTRACTP2S server command.
     * Returns the path to the extracted directory on success.
     */
    public async extractP2s(filePath: string): Promise<string> {
        const response = await this.sendCommand({ Command: 'EXTRACTP2S', FilePath: filePath });
        if (!response.Success) {
            throw new Error(response.Error || 'ExtractP2s failed');
        }
        return response.Message ?? (filePath + '.extracted');
    }

    /**
     * Ping the server
     */
    public async ping(): Promise<string> {
        const command: CommandMessage = {
            Command: 'PING'
        };

        const response = await this.sendCommand(command);
        
        if (!response.Success) {
            throw new Error(response.Error || 'Ping failed');
        }

        return response.Message || 'PONG';
    }

    /**
     * Apply PS2 GameShark/Action Replay cheat codes to live emulator memory.
     * @param cheatText  Multi-line cheat code text (blocks separated by blank lines).
     */
    public async applyCheats(cheatText: string): Promise<void> {
        const command: CommandMessage = {
            Command: 'APPLYCHEATS',
            Data:    cheatText,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ApplyCheats failed');
        }
    }

    /**
     * PCSX2 PINE: query the currently running game's metadata.
     * Returns the game title, serial ID (e.g. "SLUS-20265"), UUID, and version.
     * Only available when the server is in PINE bridge mode (--pcsx2).
     */
    public async getPcsx2GameInfo(): Promise<{ Title: string; ID: string; UUID: string; Version: string }> {
        const response = await this.sendCommand({ Command: 'PCSX2GAMEINFO' });
        if (!response.Success) {
            throw new Error(response.Error || 'PCSX2GAMEINFO failed');
        }
        const data = JSON.parse(response.Data ?? '{}') as Record<string, string>;
        return {
            Title:   data['Title']   ?? '',
            ID:      data['ID']      ?? '',
            UUID:    data['UUID']    ?? '',
            Version: data['Version'] ?? '',
        };
    }

    /**
     * Convert PS2 cheat codes between raw and a device-specific encrypted format.
     * @param codes      Multi-line cheat code text to convert.
     * @param direction  'toRaw' — decrypt device codes → raw hex pairs;
     *                   'toEngine' — encrypt raw hex pairs → device format.
     * @param format     Cheat engine format name (Raw, ActionReplay1, ActionReplay2,
     *                   ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7).
     *                   Required for 'toEngine'; omit for auto-detect on 'toRaw'.
     * @returns The converted cheat codes as a multi-line string.
     */
    public async convertCheat(codes: string, direction: 'toRaw' | 'toEngine', format?: string): Promise<string> {
        const command: CommandMessage = {
            Command:   'CONVERTCHEAT',
            Data:      codes,
            Direction: direction,
            Format:    format,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ConvertCheat failed');
        }
        return response.Data ?? '';
    }

    /**
     * Port a list of raw GameShark cheat codes from a source EE-RAM dump to a target
     * EE-RAM dump by scanning for hex patterns around each cheat address.
     *
     * @param cheatText  Raw GameShark codes (optional #Name headers).
     * @param sourcePath Absolute path to the source (r0001) raw EE-RAM dump file.
     * @param targetPath Absolute path to the target (r0002) raw EE-RAM dump file.
     * @returns Tuple of structured per-code results and a detailed log string.
     */
    public async portCheats(
        cheatText:  string,
        sourcePath: string,
        targetPath: string,
    ): Promise<{ results: PortCheatResult[]; log: string }> {
        const command: CommandMessage = {
            Command:        'PORTCHEATS',
            Data:           cheatText,
            SourceDumpPath: sourcePath,
            TargetDumpPath: targetPath,
        };
        const response = await this.sendCommand(command, 600_000);
        if (!response.Success) {
            throw new Error(response.Error || 'PortCheats failed');
        }
        const raw = response.PortCheatResults as Array<Record<string, unknown>> ?? [];
        const results: PortCheatResult[] = raw.map(r => ({
            blockIndex:   (r['BlockIndex']   as number)  ?? 0,
            blockName:    (r['BlockName']    as string)  ?? '',
            blockText:    (r['BlockText']    as string | null) ?? null,
            originalCode: (r['OriginalCode'] as string)  ?? '',
            newCode:      (r['NewCode']      as string | null) ?? null,
            found:        (r['Found']        as boolean) ?? false,
            method:       (r['Method']       as string)  ?? '',
            confidence:   (r['Confidence']   as number)  ?? 0,
            ambiguous:    (r['Ambiguous']    as boolean) ?? false,
            candidates:   (r['Candidates']   as string[] | null) ?? null,
        }));
        return { results, log: response.Data ?? '' };
    }

    /**
     * List all ELF and IRX files found on a PS2 disc image.
     * Also identifies which file is the BOOT2 main executable from SYSTEM.CNF.
     *
     * @param isoPath Absolute path to the ISO-9660 disc image.
     */
    public async listDiscElfs(isoPath: string): Promise<{ elfFiles: DiscElfEntry[]; bootElf: string }> {
        const command: CommandMessage = {
            Command: 'LISTDISCELFS',
            IsoPath: isoPath,
        };
        const response = await this.sendCommand(command, 30_000);
        if (!response.Success) {
            throw new Error(response.Error || 'LISTDISCELFS failed');
        }
        return {
            elfFiles: response.ElfFiles ?? [],
            bootElf:  response.BootElf  ?? '',
        };
    }

    /**
     * Extract a named ELF from a PS2 disc image and run the static recompiler on it,
     * generating C++ source files in the specified output directory.
     *
     * @param isoPath         Absolute path to the ISO-9660 disc image.
     * @param elfName         Name of the ELF on the disc (e.g. "SCUS_971.98").
     *                        Pass an empty string to use the BOOT2 executable from SYSTEM.CNF.
     * @param outputDirectory Directory where the generated C++ files will be written.
     */
    public async recompile(
        isoPath: string,
        elfName: string,
        outputDirectory: string,
    ): Promise<RecompileResult> {
        const command: CommandMessage = {
            Command:         'RECOMPILE',
            IsoPath:         isoPath,
            ElfName:         elfName,
            OutputDirectory: outputDirectory,
        };
        // Recompilation can take a while for large game ELFs — allow up to 5 minutes.
        const response = await this.sendCommand(command, 300_000);
        if (!response.Success) {
            throw new Error(response.Error || 'RECOMPILE failed');
        }
        return {
            functionCount: response.FunctionCount ?? 0,
            writtenFiles:  response.WrittenFiles  ?? [],
            bootElf:       response.BootElf       ?? elfName,
        };
    }

    // ── Memory Card Manager ──────────────────────────────────────────────────

    /**
     * List all save-game directories on a PS2 memory card image.
     * @param mcPath Absolute path to the .ps2 memory card file.
     */
    public async mcList(mcPath: string): Promise<McSaveInfo[]> {
        const response = await this.sendCommand({ Command: 'MC_LIST', McPath: mcPath });
        if (!response.Success) {
            throw new Error(response.Error || 'MC_LIST failed');
        }
        return response.McSaves ?? [];
    }

    /**
     * Export a save-game directory from a PS2 memory card image to a host directory.
     * @param mcPath    Absolute path to the .ps2 memory card file.
     * @param saveName  Name of the save directory (e.g. "BASLUS-20386SPEOPS").
     * @param exportDir Absolute host directory where the save folder will be written.
     */
    public async mcExport(mcPath: string, saveName: string, exportDir: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'MC_EXPORT', McPath: mcPath, SaveName: saveName, ExportDir: exportDir }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_EXPORT failed');
        }
        return response.Data ?? exportDir;
    }

    /**
     * Import a save-game directory from the host into a PS2 memory card image.
     * @param mcPath    Absolute path to the .ps2 memory card file.
     * @param importDir Absolute path to the save-game folder on the host.
     * @param overwrite Whether to overwrite an existing save with the same name.
     */
    public async mcImport(mcPath: string, importDir: string, overwrite: boolean): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'MC_IMPORT', McPath: mcPath, ImportDir: importDir, McOverwrite: overwrite }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_IMPORT failed');
        }
    }

    /**
     * Delete a save-game directory from a PS2 memory card image.
     * @param mcPath   Absolute path to the .ps2 memory card file.
     * @param saveName Name of the save directory to delete.
     */
    public async mcDelete(mcPath: string, saveName: string): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'MC_DELETE', McPath: mcPath, SaveName: saveName }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_DELETE failed');
        }
    }

    /**
     * Create a new blank 8 MB PS2 memory card image at the given path.
     * @param mcPath Absolute path for the new .ps2 file.
     */
    public async mcFormat(mcPath: string): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'MC_FORMAT', McPath: mcPath }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_FORMAT failed');
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HDD Image Manager
    // ═══════════════════════════════════════════════════════════════════════

    /**
     * List all partitions on a PS2 HDD raw image.
     * @param hddPath Absolute path to the HDD.raw file.
     */
    public async hddList(hddPath: string): Promise<HddPartitionInfo[]> {
        const response = await this.sendCommand({ Command: 'HDD_LIST', HddPath: hddPath });
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_LIST failed');
        }
        return response.HddPartitions ?? [];
    }

    /**
     * List files within a specific partition on a PS2 HDD image.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID (e.g. "__common", "__system").
     */
    public async hddListFiles(hddPath: string, partitionId: string): Promise<HddFileEntryInfo[]> {
        const response = await this.sendCommand(
            { Command: 'HDD_LIST_FILES', HddPath: hddPath, PartitionId: partitionId }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_LIST_FILES failed');
        }
        return response.HddFiles ?? [];
    }

    /**
     * Export a file from an HDD partition to the host filesystem.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID.
     * @param fileName    Name of the file to export.
     * @param exportDir   Destination directory on the host.
     */
    public async hddExport(hddPath: string, partitionId: string, fileName: string, exportDir: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'HDD_EXPORT', HddPath: hddPath, PartitionId: partitionId, FileName: fileName, ExportDir: exportDir }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_EXPORT failed');
        }
        return response.Data ?? exportDir;
    }

    /**
     * Import a file from the host into an HDD partition.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID.
     * @param importDir   Path to the file/directory to import.
     * @param overwrite   Whether to overwrite existing files.
     */
    public async hddImport(hddPath: string, partitionId: string, importDir: string, overwrite: boolean): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'HDD_IMPORT', HddPath: hddPath, PartitionId: partitionId, ImportDir: importDir, McOverwrite: overwrite }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_IMPORT failed');
        }
    }

    /**
     * Delete a partition from an HDD image.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID to delete.
     */
    public async hddDelete(hddPath: string, partitionId: string): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'HDD_DELETE', HddPath: hddPath, PartitionId: partitionId }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_DELETE failed');
        }
    }

    /**
     * Create a new blank PS2 HDD raw image.
     * @param hddPath Absolute path for the new HDD.raw file.
     * @param sizeGb  Size in GB (default 40).
     */
    public async hddCreate(hddPath: string, sizeGb: number = 40): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'HDD_CREATE', HddPath: hddPath, HddSizeGb: sizeGb }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_CREATE failed');
        }
    }

    /**
     * Fetch all struct definitions from the server.
     */
    public async getStructs(): Promise<StructEntry[]> {
        const response = await this.sendCommand({ Command: 'GETSTRUCTS' });
        if (!response.Success) {
            throw new Error(response.Error || 'GETSTRUCTS failed');
        }
        return response.Structs ?? [];
    }

    /**
     * Add or update a struct definition (name + base address).
     * @param name        Unique struct name.
     * @param baseAddress Base address as a hex string (e.g. "0x80200000").
     */
    public async addStruct(name: string, baseAddress: string): Promise<void> {
        const response = await this.sendCommand({
            Command: 'ADDSTRUCT',
            StructName: name,
            StructBaseAddress: baseAddress,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'ADDSTRUCT failed');
        }
    }

    /**
     * Delete a struct definition by name.
     */
    public async deleteStruct(name: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETESTRUCT', StructName: name });
        if (!response.Success) {
            throw new Error(response.Error || 'DELETESTRUCT failed');
        }
    }

    /**
     * Add or update a member field in an existing struct.
     * @param structName   Name of the parent struct.
     * @param offset       Byte offset of the member within the struct.
     * @param memberName   Human-readable name for the member.
     */
    public async addStructMember(structName: string, offset: number, memberName: string): Promise<void> {
        const response = await this.sendCommand({
            Command: 'ADDSTRUCTMEMBER',
            StructName: structName,
            MemberOffset: offset,
            MemberName: memberName,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'ADDSTRUCTMEMBER failed');
        }
    }

    // ── Navigation history ────────────────────────────────────────────────────

    /**
     * Record a navigation event in the server-side history.
     * Called by the VS Code extension whenever the user navigates in the disasm editor.
     * @param address  EE virtual address navigated to.
     * @param source   'user' for VS Code user action, 'mcp' for AI-driven navigation.
     */
    public async recordNav(address: number, source: 'user' | 'mcp' = 'user'): Promise<void> {
        try {
            await this.sendCommand({ Command: 'RECORDNAV', Address: address, NavSource: source });
        } catch { /* non-fatal */ }
    }

    /**
     * Fetch the server-side navigation history.
     */
    public async getNavHistory(): Promise<NavHistoryEntry[]> {
        const response = await this.sendCommand({ Command: 'GETNAVHISTORY' });
        if (!response.Success) {
            throw new Error(response.Error || 'GETNAVHISTORY failed');
        }
        return (response as any).NavHistory ?? [];
    }

    /**
     * Clear the server-side navigation history.
     */
    public async clearNavHistory(): Promise<void> {
        const response = await this.sendCommand({ Command: 'CLEARNAVHISTORY' });
        if (!response.Success) {
            throw new Error(response.Error || 'CLEARNAVHISTORY failed');
        }
    }

    /**
     * Tell the VS Code disasm editor to navigate to the given address.
     * The server pushes an ai_activity notification; Follow AI mode in the
     * VS Code extension automatically scrolls the disasm view.
     */
    public async navigateTo(address: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'NAVIGATETO', Address: address });
        if (!response.Success) {
            throw new Error(response.Error || 'NAVIGATETO failed');
        }
    }

    /**
     * Read historical 4-byte values at `address` across all snapshots in `snapshotDir`.
     * @param address      EE virtual address to look up.
     * @param snapshotDir  Root directory to scan for snapshot-info.json files.
     */
    public async getAddressSnapshotHistory(
        address: number,
        snapshotDir: string,
    ): Promise<AddressHistoryEntry[]> {
        const response = await this.sendCommand({
            Command: 'GETADDRESSHISTORY',
            Address: address,
            SnapshotDir: snapshotDir,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'GETADDRESSHISTORY failed');
        }
        return (response as any).AddressHistory ?? [];
    }

    // ── Label Manager ─────────────────────────────────────────────────────────

    /** List all label set names for a game (always includes "Built-in"). */
    public async getLabelSets(workspaceDir: string, gameId: string): Promise<string[]> {
        const response = await this.sendCommand({ Command: 'GETLABELSETS', WorkspaceDir: workspaceDir, GameId: gameId });
        if (!response.Success) { throw new Error(response.Error || 'GETLABELSETS failed'); }
        return (response as any).LabelSets ?? [];
    }

    /** Return the full contents of a label set. Pass "Built-in" for the built-in set. */
    public async getLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'GETLABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'GETLABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Make a label set active; server reloads _labels/_structs from merged result. */
    public async setActiveLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'SETACTIVELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'SETACTIVELABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Create a new empty label set. */
    public async createLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'CREATELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'CREATELABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Delete a label set from disk. */
    public async deleteLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'DELETELABELSET failed'); }
    }

    /** Add or update a label entry in the given set. */
    public async addLabelEntry(workspaceDir: string, gameId: string, setName: string, entry: LabelManagerEntry): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'ADDLABELENTRY', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName, LabelEntry: entry as any });
        if (!response.Success) { throw new Error(response.Error || 'ADDLABELENTRY failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Delete a label entry by address from the given set. */
    public async deleteLabelEntry(workspaceDir: string, gameId: string, setName: string, address: number): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'DELETELABELENTRY', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName, Address: address });
        if (!response.Success) { throw new Error(response.Error || 'DELETELABELENTRY failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Import a label set from a JSON string. Returns the imported set. */
    public async importLabelSet(workspaceDir: string, gameId: string, setName: string, json: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'IMPORTLABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName, Data: json });
        if (!response.Success) { throw new Error(response.Error || 'IMPORTLABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Export a label set; the server returns the JSON string in Data. */
    public async exportLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<string> {
        const response = await this.sendCommand({ Command: 'EXPORTLABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'EXPORTLABELSET failed'); }
        return (response as any).Data ?? '';
    }
}

