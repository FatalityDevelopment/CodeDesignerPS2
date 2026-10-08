#!/usr/bin/env node
// Claude Code "Stop" hook: sends the AI's final reply text for the turn to the
// CodeDesigner Bridge as an AI_MESSAGE command. The Bridge stores it in the
// AiActivityLogs table (Kind = "message") and the Discord bot posts it, so the
// activity feed carries the AI's own explanation alongside its tool calls.
//
// Best-effort by design: any failure (no transcript, Bridge not running, …)
// exits 0 silently so it never blocks or disturbs the session.

const fs = require('fs');
const net = require('net');

const BRIDGE_HOST = '127.0.0.1';
const BRIDGE_PORT = Number(process.env.CODEDESIGNER_BRIDGE_PORT || 8765);
const TIMEOUT_MS = 3000;

function readStdin() {
    return new Promise((resolve) => {
        let data = '';
        process.stdin.setEncoding('utf8');
        process.stdin.on('data', (chunk) => { data += chunk; });
        process.stdin.on('end', () => resolve(data));
        process.stdin.on('error', () => resolve(data));
    });
}

/**
 * The final reply = the assistant text blocks after the turn's last tool call.
 * Walk the JSONL transcript backwards, collecting text until we reach a
 * tool_use (the AI was still working) or a user entry (start of the turn).
 */
function finalAssistantText(transcriptPath) {
    const lines = fs.readFileSync(transcriptPath, 'utf8').split('\n');
    const parts = [];

    for (let i = lines.length - 1; i >= 0; i--) {
        const line = lines[i].trim();
        if (!line) { continue; }

        let entry;
        try { entry = JSON.parse(line); } catch { continue; }

        if (entry.type === 'user' && !entry.isMeta) { break; }
        if (entry.type !== 'assistant') { continue; }

        const content = entry.message && entry.message.content;
        const blocks = Array.isArray(content) ? content : [{ type: 'text', text: String(content || '') }];

        let hitToolUse = false;
        for (let b = blocks.length - 1; b >= 0; b--) {
            const block = blocks[b];
            if (block.type === 'tool_use') { hitToolUse = true; break; }
            if (block.type === 'text' && block.text && block.text.trim()) { parts.unshift(block.text.trim()); }
        }
        if (hitToolUse) { break; }
    }

    return parts.join('\n\n');
}

function sendToBridge(text) {
    return new Promise((resolve) => {
        const socket = net.createConnection({ host: BRIDGE_HOST, port: BRIDGE_PORT });
        const done = () => { socket.destroy(); resolve(); };
        socket.setTimeout(TIMEOUT_MS, done);
        socket.on('error', done);
        socket.on('connect', () => {
            socket.write(JSON.stringify({ Command: 'AI_MESSAGE', Data: text }) + '\n');
        });
        // Wait for the Bridge's one-line ack so the command is processed before we exit.
        socket.on('data', (chunk) => { if (chunk.toString().includes('\n')) { done(); } });
    });
}

async function main() {
    try {
        const input = JSON.parse((await readStdin()) || '{}');
        if (!input.transcript_path || !fs.existsSync(input.transcript_path)) { return; }

        const text = finalAssistantText(input.transcript_path);
        if (text) { await sendToBridge(text); }
    } catch {
        // Best-effort — never fail the hook.
    }
}

if (require.main === module) {
    main().finally(() => process.exit(0));
} else {
    module.exports = { finalAssistantText };
}
