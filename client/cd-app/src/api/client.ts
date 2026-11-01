// Commands with no typed BridgeClient wrapper (FS_* and the like). Replies carry their result as JSON text in Data.

import { bridge } from './bridge';

export type CommandFields = Record<string, string | number | boolean | undefined | null>;

export async function command<T>(name: string, fields: CommandFields = {}): Promise<T> {
  const reply = await bridge.sendRawCommand({ Command: name, ...fields });
  if (!reply.Success) { throw new Error(reply.Error ?? `${name} failed`); }
  return (reply.Data ? JSON.parse(reply.Data) : reply.Message) as T;
}
