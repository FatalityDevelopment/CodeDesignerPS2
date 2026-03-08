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
    BreakpointAddress?: number;
}

/**
 * Register dump returned by GETREGS
 */
export interface RegisterDump {
    // GPR
    gpr: number[];       // 32 GPR values (lower 64 bits as JS number)
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

    constructor(host: string = 'localhost', port: number = 8765) {
        this.host = host;
        this.port = port;
    }

    /**
     * Connect to the C# server
     */
    public connect(): Promise<void> {
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
        });
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
    private sendCommand(command: CommandMessage): Promise<CommandResponse> {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }

            let responseReceived = false;
            const timeoutMs = 10000; // 10 second timeout

            // Set up one-time response handler
            const responseHandler = (message: string) => {
                if (responseReceived) return;
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
}
