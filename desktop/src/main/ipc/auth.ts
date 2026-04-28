import { ipcMain } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';
import { GithubAuthService } from '../services/githubAuth';

const auth = new GithubAuthService();

export function registerAuthHandlers(): void {
  ipcMain.handle(IpcChannels.AuthGithubBegin, async (_e, clientId?: string) => {
    return auth.begin(clientId);
  });

  ipcMain.handle(IpcChannels.AuthGithubComplete, async (_e, code: string) => {
    return auth.complete(code);
  });

  ipcMain.handle(IpcChannels.AuthGetSession, () => auth.getSession());

  ipcMain.handle(IpcChannels.AuthLogout, () => auth.logout());
}
