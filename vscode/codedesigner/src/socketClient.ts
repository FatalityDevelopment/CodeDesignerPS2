import * as net from 'net';

/**
 * Command message format for socket communication
 */
interface CommandMessage {
    command: string;
    data?: string;
}

/**
 * Response format for socket communication
 */
interface CommandResponse {
    success: boolean;
    command?: string;
    message?: string;
    error?: string;
    results?: InstructionResult[];
}

/**
 * Result for individual instruction assembly/disassembly
 */
interface InstructionResult {
    success: boolean;
    assembly?: string;
    hex?: string;
    error?: string;
}

/**
 * Socket client for communication with C# server
 */
export class SocketClient {
    private client: net.Socket | null = null;
    private host: string;
    private port: number;
    private connected: boolean = false;
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

            this.client.connect(this.port, this.host, () => {
                console.log(`Connected to C# server at ${this.host}:${this.port}`);
                this.connected = true;
                isResolved = true;
                this.connectionHandlers.forEach(handler => handler());
                resolve();
            });

            this.client.on('data', (data: Buffer) => {
                const message = data.toString('utf8');
                console.log(`Received from server: ${message}`);
                this.messageHandlers.forEach(handler => handler(message));
            });

            this.client.on('error', (error: Error) => {
                console.error(`Socket error: ${error.message}`);
                this.connected = false;
                if (!isResolved) {
                    isResolved = true;
                    reject(error);
                }
            });

            this.client.on('close', () => {
                console.log('Connection closed');
                this.connected = false;
                this.disconnectionHandlers.forEach(handler => handler());
            });

            // Set connection timeout
            this.client.setTimeout(5000);
            this.client.on('timeout', () => {
                if (!isResolved) {
                    console.error('Connection timeout');
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
                    console.error(`Error sending message: ${error.message}`);
                    reject(error);
                } else {
                    console.log(`Sent: ${message}`);
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
        console.log('Disconnected from server');
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
                } else {
                    // Wait for response
                    setTimeout(() => {
                        if (!responseReceived) {
                            clearTimeout(timeout);
                            // Remove the handler after getting response
                            const index = this.messageHandlers.indexOf(responseHandler);
                            if (index > -1) {
                                this.messageHandlers.splice(index, 1);
                            }
                        }
                    }, timeoutMs);
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
        
        if (!response.success) {
            throw new Error(response.error || 'Assembly failed');
        }

        return response.results || [];
    }

    /**
     * Disassemble hex code to MIPS assembly
     */
    public async disassemble(hexCode: string): Promise<InstructionResult[]> {
        const command: CommandMessage = {
            command: 'DISASSEMBLE',
            data: hexCode
        };

        const response = await this.sendCommand(command);
        
        if (!response.success) {
            throw new Error(response.error || 'Disassembly failed');
        }

        return response.results || [];
    }

    /**
     * Ping the server
     */
    public async ping(): Promise<string> {
        const command: CommandMessage = {
            command: 'PING'
        };

        const response = await this.sendCommand(command);
        
        if (!response.success) {
            throw new Error(response.error || 'Ping failed');
        }

        return response.message || 'PONG';
    }
}
