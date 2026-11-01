import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BridgeClient } from '@bridge-client';
import { WsTransport } from './wsTransport';

// Stands in for the browser's WebSocket.
class FakeSocket {
  static readonly OPEN = 1;
  readyState = FakeSocket.OPEN;
  sent: string[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: unknown }) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;
  send(text: string) { this.sent.push(text); }
  close() { /* the transport clears onclose first */ }
  push(text: string) { this.onmessage?.({ data: text }); }
}

async function connected() {
  const socket = new FakeSocket();
  const client = new BridgeClient(new WsTransport('ws://test', () => socket as unknown as WebSocket));
  const open = client.connect();
  socket.onopen?.();
  await open;
  return { socket, client };
}

const flush = () => new Promise(resolve => setTimeout(resolve));

describe('BridgeClient over WsTransport', () => {
  beforeEach(() => {
    vi.stubGlobal('WebSocket', FakeSocket);
    vi.spyOn(console, 'log').mockImplementation(() => undefined);
  });

  it('matches replies to commands in order, one at a time', async () => {
    const { socket, client } = await connected();
    const first = client.sendRawCommand({ Command: 'A' });
    const second = client.sendRawCommand({ Command: 'B' });
    await flush();
    expect(socket.sent).toEqual(['{"Command":"A"}\n']);

    socket.push('{"Success":true,"Message":"one"}\n');
    expect((await first).Message).toBe('one');
    await flush();
    expect(socket.sent[1]).toBe('{"Command":"B"}\n');

    socket.push('{"Success":true,"Message":"two"}\n');
    expect((await second).Message).toBe('two');
  });

  it('does not let a push take a command\'s reply slot', async () => {
    const { socket, client } = await connected();
    const seen: string[] = [];
    client.onActivityNotification(e => seen.push(e.type));
    const reply = client.sendRawCommand({ Command: 'A' });
    await flush();
    socket.push('{"type":"ai_activity","command":"READMEM"}\n{"Success":true,"Message":"done"}\n');
    expect((await reply).Message).toBe('done');
    expect(seen).toEqual(['ai_activity']);
  });

  it('runs interactive commands ahead of queued background work', async () => {
    const { socket, client } = await connected();
    const running = client.sendRawCommand({ Command: 'A' });
    void client.sendRawCommand({ Command: 'B' });
    void client.sendRawCommand({ Command: 'PAUSE' });
    await flush();
    socket.push('{"Success":true}\n');
    await running;
    await flush();
    expect(socket.sent[1]).toBe('{"Command":"PAUSE"}\n');
  });

  it('fails what is in flight when the connection drops', async () => {
    const { socket, client } = await connected();
    let closed = false;
    client.onDisconnected(() => { closed = true; });
    const reply = client.sendRawCommand({ Command: 'A' });
    await flush();
    socket.onclose?.();
    expect((await reply).Success).toBe(false);
    expect(closed).toBe(true);
    expect(client.isConnected()).toBe(false);
  });

  it('rejects connect when the socket closes before opening', async () => {
    const socket = new FakeSocket();
    const client = new BridgeClient(new WsTransport('ws://test', () => socket as unknown as WebSocket));
    const open = client.connect();
    socket.onclose?.();
    await expect(open).rejects.toThrow('Cannot reach the Bridge');
  });
});
