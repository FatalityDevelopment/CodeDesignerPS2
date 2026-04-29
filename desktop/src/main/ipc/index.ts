import { registerAuthHandlers } from './auth';
import { registerSettingsHandlers } from './settings';
import { registerDocsHandlers } from './docs';
import { registerDebuggerHandlers } from './debugger';
import { registerAiHandlers } from './ai';
import { registerAppHandlers } from './app';

/**
 * Registers every IPC handler. Called once during app bootstrap.
 * Each module owns its own channel set and is independently testable.
 */
export function registerAllIpcHandlers(): void {
  registerAppHandlers();
  registerAuthHandlers();
  registerSettingsHandlers();
  registerDocsHandlers();
  registerDebuggerHandlers();
  registerAiHandlers();
}
