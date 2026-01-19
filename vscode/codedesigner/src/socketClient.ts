import * as net from 'net';

/**
 * Command message format for socket communication
 */
interface CommandMessage {
    command: string;
    data?: string;
    address?: number;
}

/**
 * Response format for socket communication
 */
interface CommandResponse {
    Success: boolean;
    Command?: string;
    Message?: string;
    Error?: string;
    Results?: InstructionResult[];
}

/**
 * Result for individual instruction assembly/disassembly
 */
interface InstructionResult {
    Success: boolean;
    Assembly?: string;
    Hex?: string;
    Error?: string;
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
                const message = data.toString('utf8');
                console.log(`[SOCKET] Received from server: ${message}`);
                this.messageHandlers.forEach(handler => handler(message));
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

            this.client.write(message, 'utf8', (error) => {
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
                    const response: CommandResponse = JSON.parse(message);
                    resolve(response);
                } catch (error) {
                    reject(new Error(`Failed to parse response: ${message}`));
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
            const commandStr = JSON.stringify(command);
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
            command: 'ASSEMBLE',
            data: asmCode
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
            command: 'DISASSEMBLE',
            data: hexCode
        };
        
        // Include address if provided
        if (startAddress !== undefined) {
            command.address = startAddress;
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
    public async ping(): Promise<string> {
        const command: CommandMessage = {
            command: 'PING'
        };

        const response = await this.sendCommand(command);
        
        if (!response.Success) {
            throw new Error(response.Error || 'Ping failed');
        }

        return response.Message || 'PONG';
    }
}
