import { app, ipcMain } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';

export function registerAppHandlers(): void {
  ipcMain.handle(IpcChannels.AppGetVersion, () => app.getVersion());
  ipcMain.handle(IpcChannels.AppQuit, () => app.quit());
}
