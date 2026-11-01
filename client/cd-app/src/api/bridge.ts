// The one connection from the page to the Bridge. Commands go out one at a time through BridgeClient's queues, the same
// client the VS Code extension uses, so every screen sees the extension's typed calls and push events.

import { BridgeClient } from '@bridge-client';
import { signal } from '../shared/signals';
import { WsTransport } from './wsTransport';

export type BridgeState = 'connecting' | 'online' | 'offline';

const RETRY_MS = 2000;

export const bridgeState = signal<BridgeState>('connecting');

const wsUrl = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/api/bridge`;

export const bridge = new BridgeClient(new WsTransport(wsUrl));

let retry: ReturnType<typeof setTimeout> | undefined;

async function connect(): Promise<void> {
  clearTimeout(retry);
  if (bridgeState() !== 'offline') { bridgeState.set('connecting'); }
  try {
    await bridge.connect();
  } catch {
    bridgeState.set('offline');
    retry = setTimeout(() => void connect(), RETRY_MS);
  }
}

bridge.onConnected(() => {
  // Subscribing first marks this client as a UI, so its commands are not logged as AI activity.
  void bridge.subscribeActivity().then(() => bridgeState.set('online'));
});

bridge.onDisconnected(() => {
  bridgeState.set('offline');
  retry = setTimeout(() => void connect(), RETRY_MS);
});

export function startBridge(): void {
  void connect();
}
