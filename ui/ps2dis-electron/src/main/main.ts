import { app, BrowserWindow, ipcMain } from 'electron';
import { join } from 'node:path';

const isDev = process.env['VITE_DEV_SERVER_URL'] !== undefined;

function createWindow(): void {
  const window = new BrowserWindow({
    width: 720,
    height: 560,
    minWidth: 640,
    minHeight: 420,
    title: 'ps2dis',
    backgroundColor: '#c0c0c0',
    webPreferences: {
      preload: join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  window.setMenuBarVisibility(false);

  if (isDev) {
    void window.loadURL(process.env['VITE_DEV_SERVER_URL']!);
  } else {
    void window.loadFile(join(__dirname, '../renderer/index.html'));
  }
}

ipcMain.on('codedesigner:message', (event, message) => {
  // Electron shell equivalent of VS Code webview postMessage. The bridge is
  // intentionally generic so the renderer can be hosted by the extension later.
  event.sender.send('codedesigner:message', { type: 'hostAck', received: message });
});

app.whenReady().then(createWindow);
app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });
app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
