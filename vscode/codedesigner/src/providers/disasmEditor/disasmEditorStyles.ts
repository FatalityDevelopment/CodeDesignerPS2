/**
 * Styles for the Disassembly Editor
 */

export function getDisasmEditorStyles(): string {
    return `<style> 
body { 
    margin: 0; 
    font-family: monospace; 
    background: #1e1e1e;
    color: #d4d4d4;
} 

#search-panel {
    display: none;
    background: #252526;
    border-bottom: 1px solid #3e3e42;
    padding: 8px 12px;
    position: sticky;
    top: 0;
    z-index: 100;
}

#search-panel.visible {
    display: block;
}

.search-row {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 6px;
}

.search-input-wrapper {
    position: relative;
    flex: 1;
    max-width: 400px;
}

.search-input {
    width: 100%;
    background: #3c3c3c;
    border: 1px solid #3e3e42;
    color: #cccccc;
    padding: 4px 24px 4px 8px;
    font-family: monospace;
    font-size: 13px;
    outline: none;
}

.search-input:focus {
    border-color: #007acc;
}

.search-input.error {
    border-color: #f48771;
}

.search-clear {
    position: absolute;
    right: 4px;
    top: 50%;
    transform: translateY(-50%);
    background: none;
    border: none;
    color: #cccccc;
    cursor: pointer;
    padding: 2px 4px;
    font-size: 16px;
    line-height: 1;
    display: none;
}

.search-clear:hover {
    color: #fff;
}

.search-input:not(:placeholder-shown) ~ .search-clear {
    display: block;
}

.search-mode {
    display: flex;
    gap: 4px;
}

.mode-btn {
    background: #3c3c3c;
    border: 1px solid #3e3e42;
    color: #cccccc;
    padding: 4px 8px;
    font-family: monospace;
    font-size: 11px;
    cursor: pointer;
    outline: none;
}

.mode-btn:hover {
    background: #505050;
}

.mode-btn.active {
    background: #007acc;
    border-color: #007acc;
    color: #fff;
}

.search-nav {
    display: flex;
    gap: 4px;
}

.nav-btn {
    background: #3c3c3c;
    border: 1px solid #3e3e42;
    color: #cccccc;
    padding: 4px 8px;
    cursor: pointer;
    font-size: 11px;
    outline: none;
    min-width: 24px;
}

.nav-btn:hover:not(:disabled) {
    background: #505050;
}

.nav-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.search-info {
    color: #cccccc;
    font-size: 11px;
    white-space: nowrap;
}

.close-search {
    background: none;
    border: none;
    color: #cccccc;
    cursor: pointer;
    padding: 4px 8px;
    font-size: 16px;
    margin-left: auto;
}

.close-search:hover {
    color: #fff;
}

#hex-container {
    font-family: monospace;
    overflow: auto;
    max-height: 120px;
    background: #2d2d30;
    border-bottom: 1px solid #3e3e42;
    padding: 4px;
}

.hex-row {
    display: flex;
    gap: 8px;
    padding: 2px 6px;
    line-height: 1.4;
}

.hex-addr {
    color: #858585;
    width: 10ch;
}

.hex-bytes {
    display: flex;
    gap: 8px;
    flex: 1;
}

.hex-byte {
    color: #d4d4d4;
    width: 2ch;
    text-align: center;
}

.hex-byte.highlight {
    background: #264f78;
    color: #fff;
}

.hex-ascii {
    color: #608b4e;
    width: 16ch;
    border-left: 1px solid #3e3e42;
    padding-left: 8px;
    display: flex;
}

.hex-char {
    width: 1ch;
    display: inline-block;
    text-align: center;
}

.hex-char.highlight {
    background: #264f78;
    color: #fff;
}
 
#grid { 
    outline: none; 
    max-height: calc(100vh - 120px); 
    overflow-y: auto;
} 
 
.row {
    display: grid;
    grid-template-columns: 10ch 12ch 18ch 8ch 40ch 1fr;
    padding: 2px 6px;
    cursor: default;
}
 
.row.active {
    background: #264f78;
}

.row.branch-highlight {
    background: #3a3d41;
}

.row.active.branch-highlight {
    background: #264f78;
}

.row.search-match {
    background: #613214;
}

.row.search-current {
    background: #4e3c1e;
}

.row.active.search-match,
.row.active.search-current {
    background: #264f78;
}
 
.addr { color: #c586c0; } 
.bytes { color: #9cdcfe; } 
.label { color: #4ec9b0; } 
.op { color: #dcdcaa; } 
.args { color: #ce9178; } 
.comment { color: #6a9955; } 

.search-match-highlight {
    background: #f9826c;
    color: #000;
    padding: 0 2px;
}

.search-current-highlight {
    background: #ea5c00;
    color: #fff;
    padding: 0 2px;
}

#goto-panel {
    display: none;
    background: #252526;
    border-bottom: 1px solid #3e3e42;
    padding: 8px 12px;
    position: sticky;
    top: 0;
    z-index: 100;
}

#goto-panel.visible {
    display: block;
}

</style>`;
}
