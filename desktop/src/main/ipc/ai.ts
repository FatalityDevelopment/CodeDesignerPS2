import { ipcMain } from 'electron';
import { IpcChannels } from '@shared/ipc-channels';
import { settingsStore } from '../services/settingsStore';
import { DEFAULT_AI_SETTINGS, AiSettings, ChatMessage } from '@shared/types';

const AI_KEY = 'ai';

export function registerAiHandlers(): void {
  ipcMain.handle(IpcChannels.AiSettingsGet, () => {
    return (settingsStore.get(AI_KEY) as AiSettings | undefined) ?? DEFAULT_AI_SETTINGS;
  });

  ipcMain.handle(IpcChannels.AiSettingsSet, (_e, settings: AiSettings) => {
    settingsStore.set(AI_KEY, settings);
    return settings;
  });

  // Stub: echo the user's message back. Replace with real provider integration.
  ipcMain.handle(IpcChannels.AiChatSend, async (_e, messages: ChatMessage[]) => {
    const last = messages[messages.length - 1];
    return {
      id: cryptoRandom(),
      role: 'assistant' as const,
      content: `(stub reply) You said: ${last?.content ?? ''}`,
      timestamp: Date.now()
    } satisfies ChatMessage;
  });
}

function cryptoRandom(): string {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}
