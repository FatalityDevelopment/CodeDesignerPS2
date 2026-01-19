import * as net from 'net';

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

            this.client.connect(this.port, this.host, () => {
                console.log(`Connected to C# server at ${this.host}:${this.port}`);
                this.connected = true;
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
                reject(error);
            });

            this.client.on('close', () => {
                console.log('Connection closed');
                this.connected = false;
                this.disconnectionHandlers.forEach(handler => handler());
            });

            // Set connection timeout
            this.client.setTimeout(5000);
            this.client.on('timeout', () => {
                console.error('Connection timeout');
                this.client?.destroy();
                reject(new Error('Connection timeout'));
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
}
