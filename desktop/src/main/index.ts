import { app, BrowserWindow } from 'electron';
import { createMainWindow } from './window';
import { registerAllIpcHandlers } from './ipc';

function bootstrap(): void {
  registerAllIpcHandlers();
  createMainWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createMainWindow();
    }
  });
}

app.whenReady().then(bootstrap);

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});
