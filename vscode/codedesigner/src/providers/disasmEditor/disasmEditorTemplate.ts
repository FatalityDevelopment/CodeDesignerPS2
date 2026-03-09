/**
 * Template wrapper for the Disassembly Editor.
 *
 * The full HTML structure, CSS, and webview JavaScript all live in
 * disasmEditor.html and disasmEditor.css (loaded at build time as raw strings
 * via webpack's asset/source rule — see webpack.config.js).
 *
 * At runtime this function simply swaps in the CSP nonce so that the
 * inline <script> tag is allowed to execute inside the VS Code webview.
 */

import disasmEditorHtml from './disasmEditor.html';
import disasmEditorCss  from './disasmEditor.css';

export interface DisasmEditorData {
    // Reserved for future server-side data injection at panel open time.
}

/**
 * Returns the complete HTML string for the disassembler webview panel.
 *
 * @param _data  - Unused for now; placeholder for future initialisation data.
 * @param nonce  - Cryptographic nonce injected into the Content-Security-Policy
 *                 and the <script> tag so VS Code permits the inline script.
 */
export function getDisasmEditorHtml(_data: DisasmEditorData, nonce: string): string {
    return disasmEditorHtml
        .replace(/\{\{NONCE\}\}/g,  nonce)
        .replace('{{STYLES}}',      disasmEditorCss);
}
