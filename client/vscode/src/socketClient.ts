import * as net from 'net';
import { BridgeClient, BridgeTransport, BridgeTransportEvents } from './bridgeClient';

export * from './bridgeClient';

/**
 * Raw TCP to the Bridge, the extension host's transport.
 */
class TcpTransport implements BridgeTransport {
    private client: net.Socket | null = null;

    constructor(private readonly host: string, private readonly port: number) {}

    public open(events: BridgeTransportEvents): Promise<void> {
        return new Promise<void>((resolve, reject) => {
            const client = new net.Socket();
            this.client = client;
            let isResolved = false;

            console.log(`[SOCKET] Attempting to connect to ${this.host}:${this.port}...`);
            client.connect(this.port, this.host, () => {
                isResolved = true;
                resolve();
            });

            client.on('data', (data: Buffer) => events.onData(data.toString('utf8')));

            client.on('error', (error: Error) => {
                const errorMsg = error?.message || error?.toString() || 'Unknown socket error';
                console.error(`[SOCKET] Socket error: ${errorMsg}`);
                console.error(`[SOCKET] Error details:`, error);
                if (!isResolved) {
                    isResolved = true;
                    reject(error);
                }
            });

            client.on('close', () => events.onClose());

            // Set connection timeout
            client.setTimeout(5000);
            client.on('timeout', () => {
                if (!isResolved) {
                    console.error('[SOCKET] Connection timeout');
                    client.destroy();
                    isResolved = true;
                    reject(new Error('Connection timeout'));
                }
            });
        });
    }

    public write(text: string): Promise<void> {
        return new Promise<void>((resolve, reject) => {
            if (!this.client) {
                reject(new Error('Not connected to server'));
                return;
            }
            this.client.write(text, 'utf8', error => (error ? reject(error) : resolve()));
        });
    }

    public close(): void {
        if (this.client) {
            this.client.removeAllListeners();
            this.client.destroy();
            this.client = null;
        }
    }
}

/**
 * Socket client for communication with C# server
 */
export class SocketClient extends BridgeClient {
    constructor(host: string = 'localhost', port: number = 8765) {
        super(new TcpTransport(host, port));
    }
}
