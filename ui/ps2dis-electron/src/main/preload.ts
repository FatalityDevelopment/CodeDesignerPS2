import { contextBridge, ipcRenderer } from 'electron';
import type { HostBridgeApi } from '../shared/host-bridge';

const api: HostBridgeApi = {
  postMessage: (message: unknown) => ipcRenderer.send('codedesigner:message', message),
  onMessage: (listener) => {
    const handler = (_event: unknown, message: unknown) => listener(message);
    ipcRenderer.on('codedesigner:message', handler);
    return () => ipcRenderer.off('codedesigner:message', handler);
  },
  getState: () => undefined,
  setState: () => undefined
};

contextBridge.exposeInMainWorld('codeDesignerHost', api);
