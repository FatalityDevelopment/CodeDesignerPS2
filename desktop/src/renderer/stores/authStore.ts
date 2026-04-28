import { create } from 'zustand';
import type { UserSession } from '@shared/types';

interface AuthState {
  session: UserSession | null;
  loading: boolean;
  hydrate: () => Promise<void>;
  loginWithGithub: (clientId?: string) => Promise<void>;
  /** Called by the login dialog after the user pastes the OAuth code. */
  completeGithubLogin: (code: string) => Promise<void>;
  logout: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set) => ({
  session: null,
  loading: false,

  hydrate: async () => {
    const session = await window.api.auth.getSession();
    set({ session });
  },

  loginWithGithub: async (clientId) => {
    set({ loading: true });
    try {
      await window.api.auth.githubBegin(clientId);
    } finally {
      set({ loading: false });
    }
  },

  completeGithubLogin: async (code) => {
    set({ loading: true });
    try {
      const session = await window.api.auth.githubComplete(code);
      set({ session });
    } finally {
      set({ loading: false });
    }
  },

  logout: async () => {
    await window.api.auth.logout();
    set({ session: null });
  }
}));
