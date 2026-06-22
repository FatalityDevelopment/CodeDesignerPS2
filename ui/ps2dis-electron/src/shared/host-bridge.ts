export interface HostBridgeApi {
  postMessage(message: unknown): void;
  onMessage(listener: (message: unknown) => void): () => void;
  getState?(): unknown;
  setState?(state: unknown): void;
}

declare global {
  interface Window {
    acquireVsCodeApi?: () => Pick<HostBridgeApi, 'postMessage' | 'getState' | 'setState'>;
    codeDesignerHost?: HostBridgeApi;
  }
}

let api: HostBridgeApi | undefined;

/**
 * Shared CodeDesigner host bridge, modeled after the VS Code disassembler
 * webview bridge. It prefers VS Code's acquireVsCodeApi when available and
 * otherwise uses Electron's preload-provided codeDesignerHost API.
 */
export function getHostBridge(): HostBridgeApi {
  if (api) return api;

  if (window.acquireVsCodeApi) {
    const vscode = window.acquireVsCodeApi();
    api = {
      postMessage: vscode.postMessage,
      getState: vscode.getState,
      setState: vscode.setState,
      onMessage: (listener) => {
        const handler = (event: MessageEvent) => listener(event.data);
        window.addEventListener('message', handler);
        return () => window.removeEventListener('message', handler);
      }
    };
    return api;
  }

  if (window.codeDesignerHost) {
    api = window.codeDesignerHost;
    return api;
  }

  api = {
    postMessage: (message) => console.debug('[CodeDesigner bridge fallback]', message),
    onMessage: () => () => undefined,
    getState: () => undefined,
    setState: () => undefined
  };
  return api;
}

export class BridgeMessageBus {
  private readonly bridge = getHostBridge();

  postMessage(message: unknown): void {
    this.bridge.postMessage(message);
  }

  on<T extends Record<string, unknown>>(type: string, listener: (message: T) => void): () => void {
    return this.bridge.onMessage((message) => {
      const record = message as Record<string, unknown> | undefined;
      if (record?.['type'] === type || record?.['command'] === type) listener(record as T);
    });
  }
}
