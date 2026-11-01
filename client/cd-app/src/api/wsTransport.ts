import type { BridgeTransport, BridgeTransportEvents } from '@bridge-client';

// The page's way to the Bridge: one WebSocket on /api/bridge, which the Bridge pipes to its command port. Each frame is
// a run of the same newline-delimited JSON, so BridgeClient reads it exactly as it reads TCP.
export class WsTransport implements BridgeTransport {
  private socket: WebSocket | null = null;

  constructor(private readonly url: string, private readonly create: (url: string) => WebSocket = u => new WebSocket(u)) {}

  open(events: BridgeTransportEvents): Promise<void> {
    return new Promise((resolve, reject) => {
      const socket = this.create(this.url);
      this.socket = socket;
      let opened = false;
      socket.onopen = () => { opened = true; resolve(); };
      socket.onmessage = e => { if (typeof e.data === 'string') { events.onData(e.data); } };
      socket.onerror = () => { if (!opened) { reject(new Error('Cannot reach the Bridge')); } };
      socket.onclose = () => {
        if (this.socket === socket) { this.socket = null; }
        if (opened) { events.onClose(); } else { reject(new Error('Cannot reach the Bridge')); }
      };
    });
  }

  write(text: string): Promise<void> {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) { return Promise.reject(new Error('Not connected to server')); }
    this.socket.send(text);
    return Promise.resolve();
  }

  close(): void {
    const socket = this.socket;
    this.socket = null;
    if (socket) { socket.onclose = null; socket.close(); }
  }
}
