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
    FilePath?: string;
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
    // CONVERTCHEAT fields
    Format?: string;
    Direction?: string;
    // COMPILECDS — pre-resolved pointer-manager labels (e.g. { address:'0x80100000', label:'seal.health' })
    Labels?: LabelEntry[];
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
    // Per-address analysis comments produced when an instruction executes.
    // Keyed by hex address string (e.g. "80001234"), value is the analysis comment.
    InstructionAnalysis?: Record<string, string>;
    // LISTDISCELFS fields
    ElfFiles?: DiscElfEntry[];
    BootElf?: string;
    // RECOMPILE fields
    FunctionCount?: number;
    WrittenFiles?: string[];
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
     * Load an arbitrary binary file into EE Main RAM at the given base address.
     * The emulator is paused automatically and remains paused after the load.
     * Use setRegister('PC', 0, address) to set the program counter afterwards.
     * @param filePath    Absolute path to the .bin file on the server's filesystem.
     * @param baseAddress Physical base address in EE Main RAM to load the file at.
     * @returns           The loaded region descriptor (label, file, baseAddress, size).
     */
    public async loadBinFile(filePath: string, baseAddress: number): Promise<SnapshotRegion> {
        const command: CommandMessage = {
            Command:  'LOADBIN',
            FilePath: filePath,
            Address:  baseAddress,
        };

        const response = await this.sendCommand(command);

        if (!response.Success) {
            throw new Error(response.Error || 'LoadBin failed');
        }

        const regions = response.Regions || [];
        if (regions.length === 0) {
            throw new Error('LoadBin succeeded but returned no region info');
        }
        return regions[0];
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
     * Get the current EE PC and paused/breakpoint state
     */
    public async getPC(): Promise<{ paused: boolean; pc: number; breakpointHitAddress?: number }> {
        const command: CommandMessage = { Command: 'GETPC' };
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
            breakpointHitAddress: response.BreakpointHitAddress,
            instructionAnalysis: response.InstructionAnalysis,
        };
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
     * Get a full CPU register dump from the running emulator
     */
    public async getRegisters(): Promise<RegisterDump> {
        const command: CommandMessage = { Command: 'GETREGS' };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GetRegisters failed');
        }
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
     * Search for a query string/pattern in a memory region using the C# backend.
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
}
