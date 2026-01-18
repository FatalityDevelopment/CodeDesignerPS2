import * as vscode from "vscode";
import * as fs from "fs";
import { disassemble, analyzePointers } from "../../helpers";
import { BaseEditorProvider } from "../baseProviders/baseEditorProvider";

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

export class DisasmEditorProvider extends BaseEditorProvider {
  private static readonly HEX_VIEW_SIZE = 0x30;
  private static readonly INSTRUCTION_SIZE = 4;
  private static readonly BASE_ADDRESS = 0x00000000;
  private static readonly PAGE_SIZE = 1000;
  private static readonly BUFFER_PAGES = 2;

  constructor() {
    super("disasmEditorProvider.html", "disasmEditor");
  }

  protected async onDidResolveWebview(
    webviewPanel: vscode.WebviewPanel,
    document: vscode.CustomDocument
  ): Promise<void> {
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

      webviewPanel.webview.onDidReceiveMessage((message) => {
        if (message.type === "updateHex") {
          this.sendHexView(
            webviewPanel,
            fileData,
            DisasmEditorProvider.BASE_ADDRESS,
            message.address,
            message.length || DisasmEditorProvider.INSTRUCTION_SIZE
          );
        } else if (message.type === "requestPage") {
          this.sendDisasmPage(webviewPanel, fileData, message.startIndex);
        } else if (message.type === "search") {
          this.performSearch(webviewPanel, fileData, message.query, message.mode);
        }
      });
    });
  }

  private performSearch(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    query: string,
    mode: 'text' | 'bytes' | 'string' | 'regex'
  ): void {
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
        const rows = disassemble(address, slice);

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

  private sendDisasmPage(
    webviewPanel: vscode.WebviewPanel,
    fileData: Buffer,
    startIndex: number
  ): void {
    const startOffset = startIndex * DisasmEditorProvider.INSTRUCTION_SIZE;
    const endOffset = Math.min(
      fileData.length,
      startOffset + DisasmEditorProvider.PAGE_SIZE * DisasmEditorProvider.INSTRUCTION_SIZE
    );

    const slice = fileData.slice(startOffset, endOffset);
    const startAddr = DisasmEditorProvider.BASE_ADDRESS + startOffset;

    const rows = disassemble(startAddr, slice);
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