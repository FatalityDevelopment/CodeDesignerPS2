import * as vscode from "vscode";
import * as fs from "fs";
import { analyzePointers, getNonce } from "../../helpers";
import { getDisasmEditorHtml, DisasmEditorData } from "./disasmEditorTemplate";
import { SocketClient } from "../../socketClient";

/**
 * Allowed searchable fields in a disassembled instruction row
 */
type SearchField = 'addr' | 'bytes' | 'label' | 'op' | 'args' | 'comment';

interface SearchMatch {
  instructionIndex: number;
  address: number;
  field: SearchField;
  start: number;
  length: number;
}

/**
 * Structure for disassembled instruction row
 */
interface DisassembledRow {
  addr: string;
  bytes: string;
  label: string;
  op: string;
  args: string;
  comment: string;
}

export class DisasmEditorProvider implements vscode.CustomReadonlyEditorProvider {
  private static readonly HEX_VIEW_SIZE = 0x30;
  private static readonly INSTRUCTION_SIZE = 4;
  private static readonly BASE_ADDRESS = 0x00000000;
  private static readonly PAGE_SIZE = 1000;
  private static readonly BUFFER_PAGES = 2;
  private socketClient: SocketClient | null;

  constructor(socketClient: SocketClient | null) {
    this.socketClient = socketClient;
  }

  /**
   * Format bytes for display (space-separated, big-endian)
   */
  private formatBytesForDisplay(buffer: Buffer, offset: number, length: number): string {
    const slice = buffer.slice(offset, offset + length);
    return Array.from(slice)
      .reverse()
      .map((b) => b.toString(16).padStart(2, "0").toUpperCase())
      .join(" ");
  }

  /**
   * Disassemble binary data using the C# backend via socket client
   * @param startAddr - Starting address for disassembly
   * @param buffer - Binary data to disassemble
   * @returns Array of disassembled instruction rows
   */
  private async disassembleWithBackend(startAddr: number, buffer: Buffer): Promise<DisassembledRow[]> {
    const rows: DisassembledRow[] = [];
    
    // If no socket client or not connected, return empty array
    if (!this.socketClient || !this.socketClient.isConnected()) {
      console.warn("Socket client not available or not connected, cannot disassemble");
      return rows;
    }

    try {
      // Convert buffer to hex strings for each 4-byte instruction
      const hexInstructions: string[] = [];
      for (let offset = 0; offset < buffer.length; offset += 4) {
        if (offset + 4 <= buffer.length) {
          // Read as little-endian 32-bit integer
          const instruction = buffer.readUInt32LE(offset);
          hexInstructions.push(`0x${instruction.toString(16).padStart(8, '0')}`);
        }
      }

      if (hexInstructions.length === 0) {
        return rows;
      }

      // Call C# backend to disassemble
      const hexCode = hexInstructions.join(' ');
      const results = await this.socketClient.disassemble(hexCode);

      // Convert results to row format
      for (let i = 0; i < results.length; i++) {
        const result = results[i];
        const offset = i * DisasmEditorProvider.INSTRUCTION_SIZE;
        const currentAddr = startAddr + offset;

        if (result.success && result.assembly) {
          // Parse the assembly instruction to extract op and args
          const parts = result.assembly.trim().split(/\s+/);
          const op = parts[0] || '';
          const args = parts.slice(1).join(' ');

          // Get bytes in display format (space-separated, big-endian)
          const bytesLE = this.formatBytesForDisplay(buffer, offset, 4);

          rows.push({
            addr: currentAddr.toString(16).toUpperCase().padStart(8, "0"),
            bytes: bytesLE,
            label: "",
            op: op,
            args: args,
            comment: "",
          });
        } else if (result.error) {
          // Handle error case
          console.error(`Disassembly error at offset ${offset}: ${result.error}`);
          const bytesLE = this.formatBytesForDisplay(buffer, offset, 4);

          rows.push({
            addr: currentAddr.toString(16).toUpperCase().padStart(8, "0"),
            bytes: bytesLE,
            label: "",
            op: "???",
            args: "",
            comment: `; Error: ${result.error}`,
          });
        }
      }
    } catch (error) {
      console.error("Failed to disassemble with backend:", error);
      // Return empty array on error
    }

    return rows;
  }

  openCustomDocument(uri: vscode.Uri): vscode.CustomDocument {
    return { uri, dispose: () => {} };
  }

  async resolveCustomEditor(
    document: vscode.CustomDocument,
    webviewPanel: vscode.WebviewPanel
  ): Promise<void> {
    // Setup webview options
    webviewPanel.webview.options = {
      enableScripts: true
    };

    // Set HTML from template
    const templateData: DisasmEditorData = {};
    webviewPanel.webview.html = getDisasmEditorHtml(templateData, getNonce());
    fs.readFile(document.uri.fsPath, (err, fileData) => {
      if (err) {
        webviewPanel.webview.postMessage({
          type: "error",
          message: err.message,
        });
        return;
      }

      const totalInstructions = Math.floor(
        fileData.length / DisasmEditorProvider.INSTRUCTION_SIZE
      );

      webviewPanel.webview.postMessage({
        type: "init",
        totalInstructions,
        pageSize: DisasmEditorProvider.PAGE_SIZE,
        baseAddress: DisasmEditorProvider.BASE_ADDRESS,
      });

      this.sendDisasmPage(webviewPanel, fileData, 0);

      this.sendHexView(
        webviewPanel,
        fileData,
        DisasmEditorProvider.BASE_ADDRESS,
        DisasmEditorProvider.BASE_ADDRESS,
        DisasmEditorProvider.INSTRUCTION_SIZE
      );

      webviewPanel.webview.onDidReceiveMessage(async (message) => {
        if (message.type === "updateHex") {
          this.sendHexView(
            webviewPanel,
            fileData,
            DisasmEditorProvider.BASE_ADDRESS,
            message.address,
            message.length || DisasmEditorProvider.INSTRUCTION_SIZE
          );
        } else if (message.type === "requestPage") {
          await this.sendDisasmPage(webviewPanel, fileData, message.startIndex);
        } else if (message.type === "search") {
          await this.performSearch(webviewPanel, fileData, message.query, message.mode);
        }
      });
    });
  }

  private async performSearch(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    query: string,
    mode: 'text' | 'bytes' | 'string' | 'regex'
  ): Promise<void> {
    const matches: SearchMatch[] = [];

    if (mode === 'bytes') {
      const pattern = this.parseBytePattern(query);
      if (!pattern) {
        webviewPanel.webview.postMessage({ type: 'searchResults', matches: [] });
        return;
      }

      for (let offset = 0; offset <= fileData.length - pattern.length; offset++) {
        let found = true;
        for (let i = 0; i < pattern.length; i++) {
          if (fileData[offset + i] !== pattern[i]) {
            found = false;
            break;
          }
        }

        if (found) {
          const instructionIndex = Math.floor(offset / DisasmEditorProvider.INSTRUCTION_SIZE);
          const address = DisasmEditorProvider.BASE_ADDRESS + offset;

          matches.push({
            instructionIndex,
            address,
            field: 'bytes',
            start: 0,
            length: pattern.length * 3 - 1
          });

          offset += pattern.length - 1;
        }
      }
    } else if (mode === 'string') {
      const stringBytes = Buffer.from(query, 'utf8');

      for (let offset = 0; offset <= fileData.length - stringBytes.length; offset++) {
        let found = true;
        for (let i = 0; i < stringBytes.length; i++) {
          if (fileData[offset + i] !== stringBytes[i]) {
            found = false;
            break;
          }
        }

        if (found) {
          const instructionIndex = Math.floor(offset / DisasmEditorProvider.INSTRUCTION_SIZE);
          const address = DisasmEditorProvider.BASE_ADDRESS + offset;

          matches.push({
            instructionIndex,
            address,
            field: 'bytes',
            start: 0,
            length: stringBytes.length * 3 - 1
          });

          offset += stringBytes.length - 1;
        }
      }
    } else {
      const totalInstructions = Math.floor(
        fileData.length / DisasmEditorProvider.INSTRUCTION_SIZE
      );

      for (let instrIndex = 0; instrIndex < totalInstructions; instrIndex++) {
        const offset = instrIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
        const slice = fileData.slice(offset, offset + DisasmEditorProvider.INSTRUCTION_SIZE);
        if (slice.length < DisasmEditorProvider.INSTRUCTION_SIZE) break;

        const address = DisasmEditorProvider.BASE_ADDRESS + offset;
        const rows = await this.disassembleWithBackend(address, slice);

        if (rows.length === 0) continue;

        const row = rows[0];
        const rowMatches = this.searchInRow(row, query, mode);

        for (const match of rowMatches) {
          matches.push({
            instructionIndex: instrIndex,
            address,
            field: match.field,
            start: match.start,
            length: match.length
          });
        }
      }
    }

    webviewPanel.webview.postMessage({
      type: 'searchResults',
      matches
    });
  }

  private parseBytePattern(pattern: string): number[] | null {
    const cleaned = pattern.replace(/0x/gi, '').replace(/[,\s]+/g, ' ').trim();
    if (!cleaned) return null;

    const bytes = cleaned.split(/\s+/);
    const result: number[] = [];

    for (const byte of bytes) {
      if (!/^[0-9a-f]{1,2}$/i.test(byte)) return null;
      result.push(parseInt(byte, 16));
    }

    return result.length ? result : null;
  }

  private searchInRow(
    row: any,
    query: string,
    mode: 'text' | 'regex'
  ): Array<{ field: SearchField; start: number; length: number }> {
    const matches: Array<{ field: SearchField; start: number; length: number }> = [];

    const fields: Array<{ text: string; field: SearchField }> = [
      { text: row.addr, field: 'addr' },
      { text: row.bytes, field: 'bytes' },
      { text: row.label || '', field: 'label' },
      { text: row.op, field: 'op' },
      { text: row.args, field: 'args' },
      { text: row.comment || '', field: 'comment' }
    ];

    if (mode === 'text') {
      const searchLower = query.toLowerCase();

      for (const { text, field } of fields) {
        const lower = text.toLowerCase();
        let index = 0;

        while ((index = lower.indexOf(searchLower, index)) !== -1) {
          matches.push({ field, start: index, length: query.length });
          index += query.length;
        }
      }
    } else {
      try {
        const regex = new RegExp(query, 'gi');

        for (const { text, field } of fields) {
          let match: RegExpExecArray | null;
          const fieldRegex = new RegExp(regex);

          while ((match = fieldRegex.exec(text)) !== null) {
            matches.push({
              field,
              start: match.index,
              length: match[0].length
            });

            if (match.index === fieldRegex.lastIndex) {
              fieldRegex.lastIndex++;
            }
          }
        }
      } catch {
        return [];
      }
    }

    return matches;
  }

  private async sendDisasmPage(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    startIndex: number
  ): Promise<void> {
    const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
    const endOffset = Math.min(
      fileData.length,
      startOffset + DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE
    );

    const slice = fileData.slice(startOffset, endOffset);
    const startAddr = DisasmEditorProvider.BASE_ADDRESS + startOffset;

    const rows = await this.disassembleWithBackend(startAddr, slice);
    const refs = analyzePointers(rows);

    const refMap = new Map<number, any[]>();
    for (const ref of refs) {
      if (!refMap.has(ref.instrAddr)) {
        refMap.set(ref.instrAddr, []);
      }
      refMap.get(ref.instrAddr)!.push(ref);
    }

    for (const row of rows) {
      const addr = parseInt(row.addr, 16);
      const rowRefs = refMap.get(addr);
      if (!rowRefs) continue;

      const annotations = rowRefs.map(ref => {
        if (ref.targetAddr !== undefined) {
          const addrStr = `0x${ref.targetAddr.toString(16).toUpperCase()}`;
          return ref.isFunction
            ? `; ptr to function at ${addrStr}`
            : `; ptr [${addrStr}]`;
        }
        return `; ptr: ${ref.baseReg ?? '?'} (unresolved)`;
      });

      row.comment = row.comment
        ? `${row.comment} ${annotations.join(', ')}`
        : annotations.join(', ');
    }

    webviewPanel.webview.postMessage({
      type: "disasmPage",
      rows,
      startIndex,
    });
  }

  private sendHexView(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    baseAddr: number,
    highlightAddr: number,
    highlightLength: number
  ): void {
    const alignedAddr = Math.max(baseAddr, (highlightAddr & ~0xf) - 0x10);
    const fileOffset = alignedAddr - baseAddr;
    const endOffset = Math.min(
      fileData.length,
      fileOffset + DisasmEditorProvider.HEX_VIEW_SIZE
    );

    const slice = fileData.slice(fileOffset, endOffset);

    webviewPanel.webview.postMessage({
      type: "hexview",
      data: Array.from(slice),
      offset: alignedAddr,
      highlight_addr: highlightAddr,
      highlight_length: highlightLength,
    });
  }
}