import { dialog, ipcMain } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';
import { listMarkdownFiles, readMarkdownFile } from '../services/docsService';

export function registerDocsHandlers(): void {
  ipcMain.handle(IpcChannels.DocsList, async (_e, folder: string) => {
    return listMarkdownFiles(folder);
  });

  ipcMain.handle(IpcChannels.DocsRead, async (_e, folder: string, relativePath: string) => {
    return readMarkdownFile(folder, relativePath);
  });

  ipcMain.handle(IpcChannels.DocsPickFolder, async () => {
    const result = await dialog.showOpenDialog({
      title: 'Select docs folder',
      properties: ['openDirectory']
    });
    if (result.canceled || result.filePaths.length === 0) return null;
    return result.filePaths[0];
  });
}
