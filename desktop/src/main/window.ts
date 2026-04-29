import { BrowserWindow, shell } from 'electron';
import { join } from 'path';

export function createMainWindow(): BrowserWindow {
  const win = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 900,
    minHeight: 600,
    show: false,
    title: 'CodeDesigner',
    backgroundColor: '#1e1e1e',
    autoHideMenuBar: true,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  win.on('ready-to-show', () => win.show());

  // Open external links in the default browser, never in-app.
  win.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  if (!app_isPackaged() && process.env['ELECTRON_RENDERER_URL']) {
    void win.loadURL(process.env['ELECTRON_RENDERER_URL']);
  } else {
    void win.loadFile(join(__dirname, '../renderer/index.html'));
  }

  return win;
}

function app_isPackaged(): boolean {
  // Avoid pulling app to keep this function focused/easy to mock.
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  return require('electron').app.isPackaged;
}
