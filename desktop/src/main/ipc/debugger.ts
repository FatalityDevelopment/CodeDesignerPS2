import { ipcMain } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';

/**
 * Stub debugger IPC. The intent is to wire this through to the existing
 * `CodeDesigner.Ps2.Bridge` socket once the renderer feature panel is ready.
 */
export function registerDebuggerHandlers(): void {
  ipcMain.handle(IpcChannels.DebuggerConnect, async (_e, _opts: { host: string; port: number }) => {
    return { connected: false, error: 'Bridge wiring not yet implemented' };
  });

  ipcMain.handle(IpcChannels.DebuggerStep, async () => ({ ok: false }));
  ipcMain.handle(IpcChannels.DebuggerContinue, async () => ({ ok: false }));
  ipcMain.handle(IpcChannels.DebuggerPause, async () => ({ ok: false }));
}
