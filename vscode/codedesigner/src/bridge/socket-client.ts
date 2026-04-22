import * as net from 'net';
import {
    CommandMessage,
    CommandResponse,
    RegisterDump,
    AiActivityEvent,
    AiSessionStartEvent,
    AiLabelUpdateEvent,
} from './types';

/**
 * Base socket client for communication with C# server.
 * Domain-specific client classes compose this via constructor injection.
 */
export class BaseSocketClient {
    private client: net.Socket | null = null;
    private host: string;
    private port: number;
    private connected: boolean = false;
    private wasConnected: boolean = false;
    private messageBuffer: string = '';
    private messageHandlers: Array<(message: string) => void> = [];
    private connectionHandlers: Array<() => void> = [];
    private disconnectionHandlers: Array<() => void> = [];
    private _activityHandlers: Array<(event: AiActivityEvent) => void> = [];
    private _sessionStartHandlers: Array<(event: AiSessionStartEvent) => void> = [];
    private _labelUpdateHandlers: Array<(event: AiLabelUpdateEvent) => void> = [];
    private _pendingResponses: Array<(message: string) => void> = [];
    private _connectingPromise: Promise<void> | null = null;

    constructor(host: string = 'localhost', port: number = 8765) {
        this.host = host;
        this.port = port;
    }

    public connect(): Promise<void> {
        if (this.connected) {
            console.log('Already connected to server');
            return Promise.resolve();
        }

        if (this._connectingPromise) {
            console.log('[SOCKET] Connection already in progress, sharing existing attempt');
            return this._connectingPromise;
        }

        if (this.client) {
            this.client.removeAllListeners();
            this.client.destroy();
            this.client = null;
        }

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

                this.messageBuffer += chunk;

                let newlineIndex: number;
                while ((newlineIndex = this.messageBuffer.indexOf('\n')) !== -1) {
                    const message = this.messageBuffer.substring(0, newlineIndex).trim();
                    this.messageBuffer = this.messageBuffer.substring(newlineIndex + 1);

                    if (message.length > 0) {
                        console.log(`[SOCKET] Complete message received (${message.length} chars): ${message.substring(0, 100)}...`);

                        let parsed: Record<string, unknown> | null = null;
                        try { parsed = JSON.parse(message); } catch { /* not JSON */ }
                        if (parsed && typeof parsed['type'] === 'string' && !('Success' in parsed)) {
                            if (parsed['type'] === 'ai_activity') {
                                const evt = parsed as unknown as AiActivityEvent;
                                this._activityHandlers.forEach(h => h(evt));
                            } else if (parsed['type'] === 'ai_session_start') {
                                const evt = parsed as unknown as AiSessionStartEvent;
                                this._sessionStartHandlers.forEach(h => h(evt));
                            } else if (parsed['type'] === 'ai_label_update') {
                                const evt = parsed as unknown as AiLabelUpdateEvent;
                                this._labelUpdateHandlers.forEach(h => h(evt));
                            }
                            this.messageHandlers.forEach(handler => handler(message));
                            continue;
                        }

                        const pending = this._pendingResponses.shift();
                        if (pending) {
                            pending(message);
                        }
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
                const stale = this._pendingResponses.splice(0);
                for (const handler of stale) {
                    try { handler('{"Success":false,"Error":"Connection closed"}'); } catch { /* ignore */ }
                }
                if (wasActuallyConnected) {
                    this.disconnectionHandlers.forEach(handler => handler());
                }
            });

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

    public disconnect(): void {
        if (this.client) {
            this.client.destroy();
            this.client = null;
        }
        this.connected = false;
        this.wasConnected = false;
        const stale = this._pendingResponses.splice(0);
        for (const handler of stale) {
            try { handler('{"Success":false,"Error":"Disconnected"}'); } catch { /* ignore */ }
        }
        console.log('[SOCKET] Disconnected from server');
    }

    public onMessage(handler: (message: string) => void): void {
        this.messageHandlers.push(handler);
    }

    public onConnected(handler: () => void): void {
        this.connectionHandlers.push(handler);
    }

    public onDisconnected(handler: () => void): void {
        this.disconnectionHandlers.push(handler);
    }

    public onActivityNotification(handler: (event: AiActivityEvent) => void): void {
        this._activityHandlers.push(handler);
    }

    public onSessionStart(handler: (event: AiSessionStartEvent) => void): void {
        this._sessionStartHandlers.push(handler);
    }

    public onLabelUpdate(handler: (event: AiLabelUpdateEvent) => void): void {
        this._labelUpdateHandlers.push(handler);
    }

    public async subscribeActivity(): Promise<void> {
        try {
            await this.sendCommand({ Command: 'SUBSCRIBE_ACTIVITY' });
        } catch (err) {
            console.warn('[SOCKET] subscribeActivity failed:', err);
        }
    }

    public isConnected(): boolean {
        return this.connected;
    }

    public sendCommand(command: CommandMessage, timeoutMs: number = 10000): Promise<CommandResponse> {
        return new Promise((resolve, reject) => {
            if (!this.connected || !this.client) {
                reject(new Error('Not connected to server'));
                return;
            }

            let responseReceived = false;

            const responseHandler = (message: string) => {
                if (responseReceived) return;
                responseReceived = true;
                clearTimeout(timeout);

                try {
                    let cleanMessage = message;
                    const echoPrefix = 'Echo: ';
                    if (cleanMessage.startsWith(echoPrefix)) {
                        cleanMessage = cleanMessage.substring(echoPrefix.length);
                        console.log('[SOCKET] Stripped Echo prefix from response');
                    }

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

            this._pendingResponses.push(responseHandler);

            const timeout = setTimeout(() => {
                if (!responseReceived) {
                    responseReceived = true;
                    const index = this._pendingResponses.indexOf(responseHandler);
                    if (index > -1) {
                        this._pendingResponses.splice(index, 1);
                    }
                    reject(new Error('Command timeout'));
                }
            }, timeoutMs);

            const commandStr = JSON.stringify(command) + '\n';
            console.log(`[SOCKET] Sending command: ${commandStr.trim()}`);
            this.client.write(commandStr, 'utf8', (error) => {
                if (error) {
                    clearTimeout(timeout);
                    responseReceived = true;
                    const index = this._pendingResponses.indexOf(responseHandler);
                    if (index > -1) {
                        this._pendingResponses.splice(index, 1);
                    }
                    reject(error);
                }
            });
        });
    }

    public parseRegisterDump(response: CommandResponse): RegisterDump {
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
}
