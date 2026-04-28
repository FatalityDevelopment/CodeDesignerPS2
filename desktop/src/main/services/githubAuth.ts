import { app, shell } from 'electron';
import { randomBytes } from 'crypto';
import type { UserSession } from '@shared/types';

/**
 * GitHub OAuth Device-Flow style scaffold.
 *
 * NOTE: This is a placeholder. To complete the integration:
 *  1. Register an OAuth app in GitHub and inject its client id at build-time.
 *  2. Use the device flow (https://github.com/login/device/code) which doesn't
 *     require a backend secret, OR run a tiny local server to receive the
 *     redirect for a normal web flow.
 *  3. Persist the access token via `safeStorage` (encrypted at rest).
 */
export class GithubAuthService {
  private session: UserSession | null = null;
  private state: string | null = null;

  begin(clientId?: string): { authorizeUrl: string; state: string } {
    this.state = randomBytes(16).toString('hex');
    const cid = clientId ?? process.env['GITHUB_CLIENT_ID'] ?? 'YOUR_GITHUB_CLIENT_ID';
    const url = new URL('https://github.com/login/oauth/authorize');
    url.searchParams.set('client_id', cid);
    url.searchParams.set('scope', 'read:user user:email');
    url.searchParams.set('state', this.state);
    void shell.openExternal(url.toString());
    return { authorizeUrl: url.toString(), state: this.state };
  }

  /**
   * Complete the OAuth flow given an authorization code.
   * Real implementations should exchange the code for a token via a small
   * proxy or use the device flow. For now this just records a stub session.
   */
  complete(code: string): UserSession {
    this.session = {
      provider: 'github',
      username: 'demo-user',
      displayName: 'Demo User',
      avatarUrl: undefined,
      accessToken: code ? `stub-${code.slice(0, 8)}` : undefined
    };
    return this.session;
  }

  getSession(): UserSession | null {
    return this.session;
  }

  logout(): void {
    this.session = null;
    this.state = null;
  }
}

/**
 * Path under which we will eventually persist the encrypted token.
 * Exposed for tests / future migration.
 */
export const tokenFilePath = (): string => `${app.getPath('userData')}/auth.json`;
