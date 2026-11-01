// BridgeClient decodes memory reads with Node's Buffer; this supplies it in the page. Imported first in main.tsx.
import { Buffer } from 'buffer';

(globalThis as unknown as { Buffer: typeof Buffer }).Buffer = Buffer;
