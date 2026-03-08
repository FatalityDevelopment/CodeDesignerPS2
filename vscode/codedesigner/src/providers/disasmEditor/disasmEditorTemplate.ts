/**
 * Template for the Disassembly Editor
 * Large complex HTML moved from external file to TypeScript module
 */

import { getDisasmEditorStyles } from './disasmEditorStyles';

export interface DisasmEditorData {
    // Empty for now - can add initialization data later
}

export function getDisasmEditorHtml(data: DisasmEditorData, nonce: string): string {
    return `<!DOCTYPE html> 
<html> 
<head> 
<meta charset="UTF-8"> 
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">

${getDisasmEditorStyles()}

</head> 
 
<body>

<div id="header">
${getHeaderToolbar()}
${getGotoPanel()}
${getSearchPanel()}
${getHexContainer()}
</div>
<div id="content-wrapper">
${getGrid()}
${getSymbolsPanel()}
${getDebugPanel()}
</div>
${getEditOverlay()}

${getScript(nonce)}

</body> 
</html>`;
}

function getGotoPanel(): string {
    return `<div id="goto-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input
                type="text"
                id="goto-input"
                class="search-input"
                placeholder="Go to address (hex)"
            />
            <button class="search-clear" id="goto-clear">×</button>
        </div>

        <div class="search-nav">
            <button class="nav-btn" id="goto-btn" title="Go to address (Enter)">
                Go
            </button>
        </div>

        <span class="search-info" id="goto-info"></span>

        <button class="close-search" id="close-goto" title="Close (Escape)">×</button>
    </div>
</div>`;
}

function getSearchPanel(): string {
    return `<div id="search-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input type="text" id="search-input" class="search-input" placeholder="Search (text or bytes: 0x12 34 AB)" />
            <button class="search-clear" id="search-clear">×</button>
        </div>
        <div class="search-mode">
            <button class="mode-btn active" data-mode="text" title="Text Search">Aa</button>
            <button class="mode-btn" data-mode="bytes" title="Byte Pattern Search">0x</button>
            <button class="mode-btn" data-mode="string" title="String Search (ASCII/UTF-8)">"ab"</button>
            <button class="mode-btn" data-mode="regex" title="Regular Expression">.*</button>
        </div>
        <div class="search-nav">
            <button class="nav-btn" id="prev-match" title="Previous Match (Shift+Enter)">↑</button>
            <button class="nav-btn" id="next-match" title="Next Match (Enter)">↓</button>
        </div>
        <span class="search-info" id="search-info">No results</span>
        <button class="close-search" id="close-search" title="Close (Escape)">×</button>
    </div>
</div>`;
}

function getHeaderToolbar(): string {
    return `<div id="header-toolbar">
    <button class="header-tool-btn" id="symbols-toggle-btn" title="Toggle Symbols Panel (Ctrl+Alt+S)">⊞ Symbols</button>
    <div id="live-controls">
        <span class="live-indicator" id="live-indicator">● LIVE</span>
        <button id="play-pause-btn" title="Pause live feed" aria-label="Pause live feed">⏸ Pause</button>
    </div>
</div>`;
}

function getHexContainer(): string {
    return `<div id="hex-container"></div>`;
}

function getGrid(): string {
    return `<div id="grid" tabindex="0"></div>`;
}

function getSymbolsPanel(): string {
    return `<div id="symbols-panel">
    <div id="symbols-panel-header">
        <span class="symbols-title">Symbols</span>
        <span id="sym-region-label" class="sym-region-label"></span>
        <button class="sym-header-btn" id="symbols-refresh-btn" title="Refresh symbols">↻</button>
        <button class="sym-header-btn" id="close-symbols-btn" title="Close">×</button>
    </div>
    <div id="sym-tabs">
        <button class="sym-tab active" data-symtab="labels">Labels</button>
        <button class="sym-tab" data-symtab="strings">Strings</button>
        <button class="sym-tab" data-symtab="functions">Functions</button>
    </div>
    <div id="sym-labels-content" class="sym-tab-content active">
        <div id="sym-add-row">
            <input type="text" id="sym-new-addr"    class="sym-add-input" placeholder="0x00000000" maxlength="10" />
            <input type="text" id="sym-new-name"    class="sym-add-input" placeholder="Label name" />
            <input type="text" id="sym-new-comment" class="sym-add-input" placeholder="Comment (opt.)" />
            <button id="sym-add-btn" class="sym-add-btn" title="Add label">+</button>
        </div>
        <div id="sym-labels-list" class="sym-list"></div>
    </div>
    <div id="sym-strings-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-strings-btn" class="sym-scan-btn">Scan for strings</button>
            <span id="sym-strings-status" class="sym-status"></span>
        </div>
        <div id="sym-strings-list" class="sym-list"></div>
    </div>
    <div id="sym-functions-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-functions-btn" class="sym-scan-btn">Scan for functions</button>
            <span id="sym-functions-status" class="sym-status"></span>
        </div>
        <div id="sym-functions-list" class="sym-list"></div>
    </div>
</div>`;
}

function getDebugPanel(): string {
    return `<div id="debug-panel">
    <div id="debug-tabs">
        <button class="debug-tab active" data-tab="registers">GPR</button>
        <button class="debug-tab" data-tab="cop0">CP0</button>
        <button class="debug-tab" data-tab="fpr">FPR</button>
        <button class="debug-tab" data-tab="fcr">FCR</button>
        <button class="debug-tab" data-tab="vu0f">VU0f</button>
        <button class="debug-tab" data-tab="vu0i">VU0i</button>
        <button class="debug-tab" data-tab="gs">GS</button>
        <button class="debug-tab" data-tab="stack">Stack</button>
    </div>
    <div id="registers-content" class="debug-tab-content active"></div>
    <div id="cop0-content" class="debug-tab-content"></div>
    <div id="fpr-content" class="debug-tab-content"></div>
    <div id="fcr-content" class="debug-tab-content"></div>
    <div id="vu0f-content" class="debug-tab-content"></div>
    <div id="vu0i-content" class="debug-tab-content"></div>
    <div id="gs-content" class="debug-tab-content"></div>
    <div id="stack-content" class="debug-tab-content"></div>
</div>`;
}

function getEditOverlay(): string {
    return `<div id="edit-overlay">
    <div id="edit-overlay-box">
        <div id="edit-overlay-header">
            <span id="edit-overlay-title">Edit Address</span>
            <button id="edit-overlay-close" title="Close (Escape)">×</button>
        </div>
        <div id="edit-overlay-info"></div>
        <div class="edit-overlay-field">
            <label class="edit-overlay-label" for="edit-overlay-label-input">Label</label>
            <input type="text" id="edit-overlay-label-input" class="edit-overlay-input" placeholder="Label name (leave empty to remove)" />
        </div>
        <div class="edit-overlay-field">
            <label class="edit-overlay-label" for="edit-overlay-comment-input">Comment</label>
            <input type="text" id="edit-overlay-comment-input" class="edit-overlay-input" placeholder="Comment (leave empty to remove)" />
        </div>
        <div id="edit-overlay-actions">
            <button id="edit-overlay-save">Save</button>
            <button id="edit-overlay-cancel">Cancel</button>
        </div>
    </div>
</div>`;
}

function getScript(nonce: string): string {
    // The JavaScript content from the HTML file - too large to inline
    // Reading from file would be better, but for now returning the massive script
    return `<script nonce="${nonce}">
${getJavaScriptContent()}
</script>`;
}

function getJavaScriptContent(): string {
    // Return the entire JavaScript from the HTML file
    // This is the 600+ line script - I'll include it inline
    return `
const vscode = acquireVsCodeApi();
const hexContainer = document.getElementById("hex-container");
const grid = document.getElementById("grid");
const searchPanel = document.getElementById("search-panel");
const searchInput = document.getElementById("search-input");
const searchClear = document.getElementById("search-clear");
const searchInfo = document.getElementById("search-info");
const closeSearch = document.getElementById("close-search");
const prevMatch = document.getElementById("prev-match");
const nextMatch = document.getElementById("next-match");

const gotoPanel = document.getElementById("goto-panel");
const gotoInput = document.getElementById("goto-input");
const gotoClear = document.getElementById("goto-clear");
const gotoBtn = document.getElementById("goto-btn");
const gotoInfo = document.getElementById("goto-info");
const closeGoto = document.getElementById("close-goto");
const headerToolbar = document.getElementById("header-toolbar");
const liveControls = document.getElementById("live-controls");
const liveIndicator = document.getElementById("live-indicator");
const playPauseBtn = document.getElementById("play-pause-btn");
const debugPanel = document.getElementById("debug-panel");
const regContent = document.getElementById("registers-content");
const stackContent = document.getElementById("stack-content");

// ── Symbols panel DOM refs ────────────────────────────────────────────────
const symbolsPanel       = document.getElementById("symbols-panel");
const symbolsToggleBtn   = document.getElementById("symbols-toggle-btn");
const symbolsRefreshBtn  = document.getElementById("symbols-refresh-btn");
const closeSymbolsBtn    = document.getElementById("close-symbols-btn");
const symNewAddr         = document.getElementById("sym-new-addr");
const symNewName         = document.getElementById("sym-new-name");
const symNewComment      = document.getElementById("sym-new-comment");
const symAddBtn          = document.getElementById("sym-add-btn");
const symLabelsList      = document.getElementById("sym-labels-list");
const symStringsList     = document.getElementById("sym-strings-list");
const symFunctionsList   = document.getElementById("sym-functions-list");
const symStringsStatus   = document.getElementById("sym-strings-status");
const symFunctionsStatus = document.getElementById("sym-functions-status");
const symScanStringsBtn  = document.getElementById("sym-scan-strings-btn");
const symScanFunctionsBtn= document.getElementById("sym-scan-functions-btn");

// ── Edit overlay DOM refs ─────────────────────────────────────────────────
const editOverlay         = document.getElementById("edit-overlay");
const editOverlayTitle    = document.getElementById("edit-overlay-title");
const editOverlayInfo     = document.getElementById("edit-overlay-info");
const editOverlayLabelIn  = document.getElementById("edit-overlay-label-input");
const editOverlayCommentIn= document.getElementById("edit-overlay-comment-input");
const editOverlaySave     = document.getElementById("edit-overlay-save");
const editOverlayCancel   = document.getElementById("edit-overlay-cancel");
const editOverlayClose    = document.getElementById("edit-overlay-close");

let _editOverlayAddr = 0;
let _editOverlayAddrHex = '';

function openEditOverlay(address, addressHex, op, args, bytes, currentLabel, currentComment) {
    _editOverlayAddr = address;
    _editOverlayAddrHex = addressHex;
    editOverlayTitle.textContent = 'Edit Address — 0x' + addressHex.toUpperCase();
    const instrStr = (op || '') + (args ? '  ' + args : '');
    editOverlayInfo.textContent = (bytes ? bytes + '   ' : '') + instrStr;
    editOverlayLabelIn.value = currentLabel || '';
    editOverlayCommentIn.value = currentComment || '';
    editOverlay.classList.add('visible');
    editOverlayLabelIn.focus();
    editOverlayLabelIn.select();
}

function closeEditOverlay() {
    editOverlay.classList.remove('visible');
}

function saveEditOverlay() {
    const newLabel   = editOverlayLabelIn.value.trim();
    const newComment = editOverlayCommentIn.value.trim();
    vscode.postMessage({
        type: 'saveLabel',
        address:    _editOverlayAddr,
        addressHex: _editOverlayAddrHex,
        newLabel,
        newComment,
    });
    closeEditOverlay();
}

editOverlaySave.addEventListener('click', saveEditOverlay);
editOverlayCancel.addEventListener('click', closeEditOverlay);
editOverlayClose.addEventListener('click', closeEditOverlay);

editOverlayLabelIn.addEventListener('keydown', e => {
    if (e.key === 'Enter') { editOverlayCommentIn.focus(); editOverlayCommentIn.select(); }
    if (e.key === 'Escape') { closeEditOverlay(); }
});
editOverlayCommentIn.addEventListener('keydown', e => {
    if (e.key === 'Enter') { saveEditOverlay(); }
    if (e.key === 'Escape') { closeEditOverlay(); }
});

editOverlay.addEventListener('click', e => {
    if (e.target === editOverlay) { closeEditOverlay(); }
});

const state = {
    cursor: 0,
    disasmRows: [],
    totalInstructions: 0,
    pageSize: 1000,
    baseAddress: 0,
    totalBytes: 0,
    regionLabel: '',
    hexData: [],
    hexOffset: 0,
    loadedPages: new Map(),
    searchMode: 'text',
    searchQuery: '',
    searchMatches: [],
    currentMatch: -1,
    searchActive: false,
    gotoActive: false,
    symbolsActive: false,
    isLive: false,
    breakpoints: new Set(),
    breakpointHitAddress: undefined,
    // cached symbols data
    symLabels: [],
    symStrings: [],
    symFunctions: [],
};

let liveRefreshTimer = null;
let liveStatusTimer = null;
let liveRefreshPending = false;
let liveRefreshInterval = 1000;
let livePaused = false;

const clamp = (val, min, max) => Math.max(min, Math.min(val, max));
const getPageStart = index => Math.floor(index / state.pageSize) * state.pageSize;
const getLocalIndex = index => index - getPageStart(index);

window.addEventListener("message", ({ data: msg }) => {
    console.log('[WEBVIEW] Received message:', msg.type, msg);
    switch (msg.type) {
        case "init":
            console.log('[WEBVIEW] Init:', msg.totalInstructions, 'instructions');
            state.totalInstructions = msg.totalInstructions;
            state.pageSize = msg.pageSize;
            state.baseAddress = msg.baseAddress;
            state.totalBytes = msg.totalBytes ?? (msg.totalInstructions * 4);
            state.regionLabel = msg.regionLabel ?? '';
            if (state.regionLabel) {
                const rl = document.getElementById('sym-region-label');
                if (rl) rl.textContent = \`— \${state.regionLabel}\`;
            }
            if (msg.isLive) {
                state.isLive = true;
                liveControls.classList.add('visible');
                headerToolbar.classList.add('live-active');
                startLiveRefresh();
            }
            break;
        case "disasmPage":
            console.log('[WEBVIEW] disasmPage:', msg.rows?.length || 0, 'rows, startIndex:', msg.startIndex);
            liveRefreshPending = false;
            state.loadedPages.set(msg.startIndex, msg.rows);
            if (msg.startIndex === getPageStart(state.cursor)) {
                state.disasmRows = msg.rows;
                console.log('[WEBVIEW] Rendering', state.disasmRows.length, 'rows');
                renderDisassembly();
            } else {
                console.log('[WEBVIEW] Page cached but not rendering (cursor at different page)');
            }
            break;
        case "hexview":
            console.log('[WEBVIEW] hexview:', msg.data?.length || 0, 'bytes');
            state.hexData = msg.data;
            state.hexOffset = msg.offset || 0;
            renderHexView(msg.data, state.hexOffset, msg.highlight_addr, msg.highlight_length);
            break;
        case "searchResults":
            console.log('[WEBVIEW] searchResults:', msg.matches?.length || 0, 'matches');
            handleSearchResults(msg.matches);
            break;
        case "status":
            handleStatusUpdate(msg.paused, msg.pc, msg.breakpointHitAddress, msg.registers, msg.stackBytes, msg.stackBase);
            break;
        case "reset":
            handleReset();
            break;
        case "symbols":
            state.symLabels    = msg.labels    ?? [];
            state.symStrings   = msg.strings   ?? [];
            state.symFunctions = msg.functions ?? [];
            renderSymLabels();
            renderSymStrings();
            renderSymFunctions();
            break;
        case "labelsUpdated":
            state.symLabels = msg.labels ?? [];
            renderSymLabels();
            // Invalidate and reload the current page so the disassembly grid reflects the new label
            state.loadedPages.delete(getPageStart(state.cursor));
            checkAndLoadPage();
            break;
        case "stringsUpdated":
            state.symStrings = msg.strings ?? [];
            renderSymStrings();
            if (symStringsStatus) {
                symStringsStatus.textContent = state.symStrings.length > 0
                    ? \`\${state.symStrings.length} found\`
                    : 'None found';
            }
            if (symScanStringsBtn) symScanStringsBtn.disabled = false;
            break;
        case "functionsUpdated":
            state.symFunctions = msg.functions ?? [];
            renderSymFunctions();
            if (symFunctionsStatus) {
                symFunctionsStatus.textContent = state.symFunctions.length > 0
                    ? \`\${state.symFunctions.length} found\`
                    : 'None found';
            }
            if (symScanFunctionsBtn) symScanFunctionsBtn.disabled = false;
            break;
    }
});

function parseAddress(input) {
    const cleaned = input.trim().toLowerCase().replace(/^0x/, '');
    if (!/^[0-9a-f]+$/.test(cleaned)) return null;
    return parseInt(cleaned, 16);
}

function toggleSearch() {
    state.searchActive = !state.searchActive;
    searchPanel.classList.toggle('visible', state.searchActive);
    if (state.searchActive) {
        searchInput.focus();
        searchInput.select();
    } else {
        clearSearchMatches();
    }
}

function parseBytePattern(pattern) {
    const cleaned = pattern.replace(/0x/gi, '').replace(/[,\\s]+/g, ' ').trim();
    if (!cleaned) return null;
    const bytes = cleaned.split(/\\s+/);
    const result = [];
    for (const byte of bytes) {
        if (!/^[0-9a-f]{1,2}$/i.test(byte)) return null;
        result.push(parseInt(byte, 16));
    }
    return result.length > 0 ? result : null;
}

function performSearch() {
    const query = searchInput.value.trim();
    const mode = state.searchMode;
    if (!query) {
        clearSearchMatches();
        return;
    }
    if (mode === 'bytes') {
        const pattern = parseBytePattern(query);
        if (!pattern) {
            searchInput.classList.add('error');
            searchInfo.textContent = 'Invalid byte pattern';
            state.searchMatches = [];
            state.currentMatch = -1;
            updateSearchInfo();
            return;
        }
    }
    searchInput.classList.remove('error');
    state.searchQuery = query;
    vscode.postMessage({ type: 'search', query: query, mode: mode });
    searchInfo.textContent = 'Searching...';
}

function handleSearchResults(matches) {
    state.searchMatches = matches || [];
    if (state.searchMatches.length > 0) {
        state.currentMatch = 0;
        jumpToMatch(0);
    } else {
        state.currentMatch = -1;
        updateSearchInfo();
        renderDisassembly();
    }
}

function jumpToMatch(matchIndex) {
    if (matchIndex < 0 || matchIndex >= state.searchMatches.length) return;
    state.currentMatch = matchIndex;
    const match = state.searchMatches[matchIndex];
    state.cursor = match.instructionIndex;
    const pageReady = checkAndLoadPage();
    if (pageReady) {
        updateCursor();
    }
    updateSearchInfo();
}

function nextSearchMatch() {
    if (state.searchMatches.length === 0) return;
    const next = (state.currentMatch + 1) % state.searchMatches.length;
    jumpToMatch(next);
}

function prevSearchMatch() {
    if (state.searchMatches.length === 0) return;
    const prev = (state.currentMatch - 1 + state.searchMatches.length) % state.searchMatches.length;
    jumpToMatch(prev);
}

function clearSearchMatches() {
    state.searchMatches = [];
    state.currentMatch = -1;
    state.searchQuery = '';
    searchInput.value = '';
    searchInput.classList.remove('error');
    updateSearchInfo();
    renderDisassembly();
}

function updateSearchInfo() {
    if (state.searchMatches.length === 0) {
        searchInfo.textContent = state.searchQuery ? 'No results' : '';
        prevMatch.disabled = true;
        nextMatch.disabled = true;
    } else {
        searchInfo.textContent = \`\${state.currentMatch + 1} of \${state.searchMatches.length}\`;
        prevMatch.disabled = false;
        nextMatch.disabled = false;
    }
}

function isRowSearchMatch(globalIndex) {
    return state.searchMatches.some(m => m.instructionIndex === globalIndex);
}

function isRowCurrentMatch(globalIndex) {
    if (state.currentMatch === -1) return false;
    const match = state.searchMatches[state.currentMatch];
    return match && match.instructionIndex === globalIndex;
}

function toggleGoto() {
    state.gotoActive = !state.gotoActive;
    gotoPanel.classList.toggle('visible', state.gotoActive);
    if (state.gotoActive) {
        gotoInput.focus();
        gotoInput.select();
    } else {
        gotoInput.value = '';
        gotoInfo.textContent = '';
    }
}

function goToAddress() {
    const value = gotoInput.value;
    const addr = parseAddress(value);
    if (addr === null) {
        gotoInput.classList.add('error');
        gotoInfo.textContent = 'Invalid hex address';
        return;
    }
    gotoInput.classList.remove('error');
    const physAddr  = toPhysAddr(addr);
    const physBase  = toPhysAddr(state.baseAddress);
    const instrIndex = Math.floor((physAddr - physBase) / 4);
    if (instrIndex < 0 || instrIndex >= state.totalInstructions) {
        gotoInfo.textContent = 'Address out of range';
        return;
    }
    state.cursor = instrIndex;
    state.searchActive = false;
    searchPanel.classList.remove('visible');
    const pageReady = checkAndLoadPage();
    if (pageReady) {
        updateCursor();
    }
    gotoInfo.textContent = \`Moved to 0x\${addr.toString(16).toUpperCase()}\`;
}

document.querySelectorAll('.mode-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.searchMode = btn.dataset.mode;
        if (state.searchMode === 'bytes') {
            searchInput.placeholder = 'Byte pattern: 0x12 34 AB CD';
        } else if (state.searchMode === 'string') {
            searchInput.placeholder = 'String in binary (case-sensitive)';
        } else if (state.searchMode === 'regex') {
            searchInput.placeholder = 'Regular expression';
        } else {
            searchInput.placeholder = 'Search text';
        }
        if (searchInput.value.trim()) {
            performSearch();
        }
    });
});

searchInput.addEventListener('input', () => {
    if (!searchInput.value.trim()) {
        clearSearchMatches();
    }
});

searchInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
        e.preventDefault();
        if (e.shiftKey) {
            prevSearchMatch();
        } else {
            if (searchInput.value.trim() && state.searchMatches.length === 0) {
                performSearch();
            } else {
                nextSearchMatch();
            }
        }
    } else if (e.key === 'Escape') {
        toggleSearch();
        grid.focus();
    }
});

searchClear.addEventListener('click', () => {
    clearSearchMatches();
    searchInput.focus();
});

prevMatch.addEventListener('click', prevSearchMatch);
nextMatch.addEventListener('click', nextSearchMatch);

closeSearch.addEventListener('click', () => {
    toggleSearch();
    grid.focus();
});

document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault();
        toggleSearch();
    }
    if (e.key === 'F3' && !e.shiftKey && state.searchActive) {
        e.preventDefault();
        nextSearchMatch();
    }
    if (e.key === 'F3' && e.shiftKey && state.searchActive) {
        e.preventDefault();
        prevSearchMatch();
    }
});

gotoBtn.addEventListener('click', goToAddress);

gotoInput.addEventListener('keydown', e => {
    if (e.key === 'Enter') {
        e.preventDefault();
        goToAddress();
    } else if (e.key === 'Escape') {
        toggleGoto();
        grid.focus();
    }
});

gotoClear.addEventListener('click', () => {
    gotoInput.value = '';
    gotoInfo.textContent = '';
    gotoInput.focus();
});

closeGoto.addEventListener('click', () => {
    toggleGoto();
    grid.focus();
});

function renderHexView(data, offset, highlightAddr = -1, highlightLength = 4) {
    hexContainer.innerHTML = "";
    hexContainer.appendChild(buildHexHeader());
    for (let i = 0; i < data.length; i += 16) {
        hexContainer.appendChild(buildHexRow(data, offset, i, highlightAddr, highlightLength));
    }
}

function buildHexHeader() {
    const header = document.createElement("div");
    header.className = "hex-row";
    header.style.cssText = \`border-bottom: 1px solid #3e3e42; padding-bottom: 4px; margin-bottom: 2px;\`;
    let bytes = "";
    for (let i = 0; i < 16; i++) {
        if (i && i % 4 === 0) bytes += '<span style="width:8px"></span>';
        bytes += \`<span class="hex-byte">+\${i.toString(16).toUpperCase()}</span>\`;
    }
    header.innerHTML = \`<span class="hex-addr">Address:</span><span class="hex-bytes">\${bytes}</span><span class="hex-ascii">0123456789ABCDEF</span>\`;
    return header;
}

function buildHexRow(data, offset, rowOffset, highlightAddr, highlightLength) {
    const row = document.createElement("div");
    row.className = "hex-row";
    const addr = (offset + rowOffset).toString(16).padStart(8, "0").toUpperCase();
    let bytesHtml = "";
    let asciiHtml = "";
    for (let i = 0; i < 16; i++) {
        if (i && i % 4 === 0) bytesHtml += '<span style="width:8px"></span>';
        let byteStr = "  ";
        let char = " ";
        let highlight = false;
        const index = rowOffset + i;
        if (index < data.length) {
            const byte = data[index];
            const byteAddr = offset + index;
            byteStr = byte.toString(16).padStart(2, "0").toUpperCase();
            char = byte >= 32 && byte <= 126 ? String.fromCharCode(byte) : ".";
            highlight = highlightAddr !== -1 && byteAddr >= highlightAddr && byteAddr < highlightAddr + highlightLength;
        }
        bytesHtml += \`<span class="hex-byte\${highlight ? " highlight" : ""}">\${byteStr}</span>\`;
        asciiHtml += \`<span class="hex-char\${highlight ? " highlight" : ""}">\${char}</span>\`;
    }
    row.innerHTML = \`<span class="hex-addr">\${addr}</span><span class="hex-bytes">\${bytesHtml}</span><span class="hex-ascii">\${asciiHtml}</span>\`;
    return row;
}

function renderDisassembly() {
    grid.innerHTML = "";
    const pageStart = getPageStart(state.cursor);
    const localCursor = getLocalIndex(state.cursor);
    state.disasmRows.forEach((rowData, i) => {
        const globalIndex = pageStart + i;
        const row = document.createElement("div");
        row.className = "row";
        row.dataset.index = i;
        row.dataset.globalIndex = globalIndex;
        if (i === localCursor) row.classList.add("active");
        if (isRowSearchMatch(globalIndex)) row.classList.add("search-match");
        if (isRowCurrentMatch(globalIndex)) row.classList.add("search-current");
        const rowAddr = parseInt(rowData.addr, 16);
        if (state.breakpoints.has(rowAddr)) {
            row.classList.add("breakpoint");
            row.classList.add("breakpoint-active");
        }
        // Distinct highlight for the row where execution actually stopped
        if (state.breakpointHitAddress !== undefined) {
            const hitPhys = toPhysAddr(state.breakpointHitAddress);
            const rowPhys = toPhysAddr(rowAddr);
            if (hitPhys === rowPhys) {
                row.classList.add("breakpoint-hit");
            }
        }
        row.innerHTML = buildDisasmRowHtml(rowData, globalIndex);
        row.onclick = () => {
            state.cursor = globalIndex;
            updateCursor();
        };
        row.ondblclick = () => {
            state.cursor = globalIndex;
            updateCursor();
            openEditOverlay(
                parseInt(rowData.addr, 16),
                rowData.addr,
                rowData.op || '',
                rowData.args || '',
                rowData.bytes || '',
                rowData.label || '',
                rowData.comment || ''
            );
        };
        grid.appendChild(row);
    });
    updateCursor();
}

function highlightSearchInText(text, globalIndex, field) {
    if (!state.searchQuery || !text) return text;
    const match = state.searchMatches.find(m => m.instructionIndex === globalIndex && m.field === field);
    if (!match) return text;
    const isCurrent = isRowCurrentMatch(globalIndex);
    const highlightClass = isCurrent ? 'search-current-highlight' : 'search-match-highlight';
    const before = text.substring(0, match.start);
    const highlighted = text.substring(match.start, match.start + match.length);
    const after = text.substring(match.start + match.length);
    return \`\${before}<span class="\${highlightClass}">\${highlighted}</span>\${after}\`;
}

function buildDisasmRowHtml(r, globalIndex) {
    const branchMatch = r.args.match(/^(.*\\$[0-9A-F]{8})\\s*(\\([+-]\\d+[▼▲]\\))(.*)$/);
    let args = branchMatch ? \`\${branchMatch[1]} <span style="color:#6a9955">\${branchMatch[2]}</span>\${branchMatch[3]}\` : r.args;
    const addr = highlightSearchInText(r.addr, globalIndex, 'addr');
    const bytes = highlightSearchInText(r.bytes, globalIndex, 'bytes');
    const label = highlightSearchInText(r.label || "", globalIndex, 'label');
    const op = highlightSearchInText(r.op, globalIndex, 'op');
    args = highlightSearchInText(args, globalIndex, 'args');
    const comment = highlightSearchInText(r.comment || "", globalIndex, 'comment');
    return \`<span class="addr">\${addr}</span><span class="bytes">\${bytes}</span><span class="label">\${label}</span><span class="op">\${op}</span><span class="args">\${args}</span><span class="comment">\${comment}</span>\`;
}

grid.addEventListener("keydown", e => {
    if (state.searchActive && document.activeElement === searchInput) {
        return;
    }
    // ArrowRight: navigate to branch/jump target address on current row
    if (e.key === 'ArrowRight') {
        e.preventDefault();
        navigateToCurrentRowTarget();
        return;
    }
    // F9: toggle breakpoint on current row
    if (e.key === 'F9') {
        e.preventDefault();
        toggleBreakpointAtCursor();
        return;
    }
    const delta = { ArrowDown: 1, ArrowUp: -1, PageDown: 50, PageUp: -50 }[e.key];
    if (!delta) return;
    e.preventDefault();
    state.cursor = clamp(state.cursor + delta, 0, state.totalInstructions - 1);
    const pageReady = checkAndLoadPage();
    if (pageReady) {
        updateCursor();
    }
});

function navigateToCurrentRowTarget() {
    const localIndex = getLocalIndex(state.cursor);
    const row = state.disasmRows[localIndex];
    if (!row) return;

    // 1. Branch target: $XXXXXXXX in args
    const branchMatch = row.args.match(/\\$([0-9A-F]{8})/i);
    if (branchMatch) {
        navigateToAddress(parseInt(branchMatch[1], 16));
        return;
    }

    // 2. Jump target: 0xXXXXXXXX in args (e.g. j/jal instructions)
    const jumpMatch = row.args.match(/0x([0-9A-F]{8})/i);
    if (jumpMatch) {
        navigateToAddress(parseInt(jumpMatch[1], 16));
        return;
    }

    // 3. Any 8-digit hex address in the comment (analyzer-generated)
    if (row.comment) {
        const commentMatch = row.comment.match(/0x([0-9A-F]{8})/i)
            || row.comment.match(/\\b([0-9A-F]{8})\\b/i);
        if (commentMatch) {
            navigateToAddress(parseInt(commentMatch[1], 16));
            return;
        }
    }
}

function navigateToAddress(addr) {
    const physAddr  = toPhysAddr(addr);
    const physBase  = toPhysAddr(state.baseAddress);
    const instrIndex = Math.floor((physAddr - physBase) / 4);
    if (instrIndex < 0 || instrIndex >= state.totalInstructions) return;
    state.cursor = instrIndex;
    const pageReady = checkAndLoadPage();
    if (pageReady) { updateCursor(); }
}

function toggleBreakpointAtCursor() {
    const localIndex = getLocalIndex(state.cursor);
    const row = state.disasmRows[localIndex];
    if (!row) return;
    const addr = parseInt(row.addr, 16);
    if (state.breakpoints.has(addr)) {
        state.breakpoints.delete(addr);
        vscode.postMessage({ type: 'clearBreakpoint', address: addr });
    } else {
        state.breakpoints.add(addr);
        vscode.postMessage({ type: 'setBreakpoint', address: addr });
    }
    renderDisassembly();
}

function checkAndLoadPage() {
    const startIndex = getPageStart(state.cursor);
    if (!state.loadedPages.has(startIndex)) {
        vscode.postMessage({ type: "requestPage", startIndex });
        return false;
    } else {
        const cachedRows = state.loadedPages.get(startIndex);
        if (state.disasmRows !== cachedRows) {
            state.disasmRows = cachedRows;
            renderDisassembly();
        }
        return true;
    }
}

window.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyA') {
        e.preventDefault();
        e.stopPropagation();
        toggleGoto();
    }
    if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyS') {
        e.preventDefault();
        e.stopPropagation();
        toggleSymbolsPanel();
    }
});

function updateCursor() {
    const localIndex = getLocalIndex(state.cursor);
    [...grid.children].forEach(r => {
        r.classList.remove("active", "branch-highlight");
    });
    const rowEl = grid.children[localIndex];
    if (!rowEl) return;
    rowEl.classList.add("active");
    rowEl.scrollIntoView({ block: "nearest", behavior: "instant" });
    const row = state.disasmRows[localIndex];
    if (!row) return;
    handleBranchHighlight(row, localIndex);
    updateHexForRow(row);
}

function updateHexForRow(row) {
    const addr = parseInt(row.addr, 16);
    const byteStr = row.bytes?.trim() || '';
    const length = byteStr.length > 0 ? Math.floor(byteStr.length / 2) : 4;
    const inView = addr >= state.hexOffset && addr + length <= state.hexOffset + state.hexData.length;
    if (inView) {
        renderHexView(state.hexData, state.hexOffset, addr, length);
    } else {
        vscode.postMessage({ type: "updateHex", address: addr, length });
    }
}

function handleBranchHighlight(row, rowIndex) {
    const match = row.args.match(/\\$([0-9A-F]{8})\\s*\\(([+-]\\d+)/);
    if (!match) return;
    const targetAddr = parseInt(match[1], 16);
    const targetIndex = state.disasmRows.findIndex(r => parseInt(r.addr, 16) === targetAddr);
    if (targetIndex === -1) return;
    const start = Math.min(rowIndex, targetIndex);
    const end = Math.max(rowIndex, targetIndex);
    for (let i = start; i <= end; i++) {
        if (i !== rowIndex) {
            grid.children[i]?.classList.add("branch-highlight");
        }
    }
}

// Prevent mouse wheel scrolling — keyboard is the only way to navigate
document.addEventListener('wheel', e => {
    e.preventDefault();
}, { passive: false });

function startLiveRefresh() {
    if (liveRefreshTimer !== null) { return; }
    if (livePaused) { return; }
    liveRefreshTimer = setInterval(doLiveRefresh, liveRefreshInterval);
    // Poll emulator status every 800ms to detect breakpoint-triggered pauses
    if (liveStatusTimer === null) {
        liveStatusTimer = setInterval(() => {
            vscode.postMessage({ type: 'getStatus' });
        }, 800);
    }
}

function stopLiveRefresh() {
    if (liveRefreshTimer !== null) {
        clearInterval(liveRefreshTimer);
        liveRefreshTimer = null;
    }
    if (liveStatusTimer !== null) {
        clearInterval(liveStatusTimer);
        liveStatusTimer = null;
    }
}

function restartLiveRefresh() {
    stopLiveRefresh();
    if (!livePaused) {
        startLiveRefresh();
    }
}

function doLiveRefresh() {
    if (liveRefreshPending) { return; }
    liveRefreshPending = true;
    const startIndex = getPageStart(state.cursor);
    state.loadedPages.delete(startIndex);
    vscode.postMessage({ type: 'requestPage', startIndex });
}

function setLivePausedUi(paused) {
    if (paused) {
        headerToolbar.classList.remove('live-active');
        headerToolbar.classList.add('live-paused');
        liveIndicator.classList.add('paused');
        liveIndicator.textContent = '⏸ PAUSED';
        playPauseBtn.textContent = '▶ Resume';
        playPauseBtn.title = 'Resume live feed';
        playPauseBtn.setAttribute('aria-label', 'Resume live feed');
    } else {
        headerToolbar.classList.remove('live-paused');
        headerToolbar.classList.add('live-active');
        liveIndicator.classList.remove('paused');
        liveIndicator.textContent = '● LIVE';
        playPauseBtn.textContent = '⏸ Pause';
        playPauseBtn.title = 'Pause live feed';
        playPauseBtn.setAttribute('aria-label', 'Pause live feed');
    }
}

// Normalize a MIPS virtual address to its physical counterpart.
// KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1 (0xA0000000–0xBFFFFFFF) are both
// direct-mapped: physical = VA & 0x1FFFFFFF.  All other segments are returned
// unchanged.  This lets panels whose baseAddress is a physical address (e.g.
// BIOS ROM at 0x1FC00000) correctly match a PC in KSEG1 (e.g. 0xBFC00000).
// The >>> 0 ensures the result is an unsigned 32-bit JS number regardless of
// JavaScript's signed-integer bitwise semantics.
function toPhysAddr(va) {
    return (va >= 0x80000000 && va <= 0xBFFFFFFF) ? ((va & 0x1FFFFFFF) >>> 0) : (va >>> 0);
}

function handleStatusUpdate(paused, pc, breakpointHitAddress, registers, stackBytes, stackBase) {
    // If the emulator paused itself (e.g. breakpoint) and the user didn't
    // manually pause, mirror that state in the UI.
    if (paused && !livePaused) {
        livePaused = true;
        stopLiveRefresh();
        setLivePausedUi(true);
    }

    // Track which address triggered the pause (undefined when running)
    if (paused) {
        state.breakpointHitAddress = breakpointHitAddress ?? pc;
    } else {
        state.breakpointHitAddress = undefined;
    }

    // Whenever paused (first detection or re-polled), navigate the cursor to the
    // current PC so the disassembler always shows the correct instruction — even
    // for early BIOS addresses where the PC is a KSEG1 virtual address but the
    // panel's baseAddress is a physical address.
    if (paused && pc !== undefined && pc !== null) {
        const physPc   = toPhysAddr(pc);
        const physBase = toPhysAddr(state.baseAddress);
        const instrIndex = Math.floor((physPc - physBase) / 4);
        if (instrIndex >= 0 && instrIndex < state.totalInstructions) {
            state.cursor = instrIndex;
            const pageReady = checkAndLoadPage();
            if (pageReady) { updateCursor(); }
        }
    }

    if (!paused && livePaused) {
        // Emulator resumed externally
        livePaused = false;
        state.breakpointHitAddress = undefined;
        setLivePausedUi(false);
        startLiveRefresh();
        hideDebugPanel();
        return;
    }
    // Update debug panel whenever we are paused and have register data
    if (paused && registers) {
        showDebugPanel(registers, stackBytes, stackBase);
    }
}

function handleReset() {
    // Clear all cached pages so zeroed-out rows are displayed immediately
    state.loadedPages.clear();
    state.disasmRows = [];
    state.breakpointHitAddress = undefined;
    const startIndex = getPageStart(state.cursor);
    const zeroRows = generateZeroPage(startIndex);
    state.loadedPages.set(startIndex, zeroRows);
    state.disasmRows = zeroRows;
    renderDisassembly();
    hideDebugPanel();
    // Restart live refresh so the emulator re-runs from the reset vector
    livePaused = false;
    setLivePausedUi(false);
    restartLiveRefresh();
}

function generateZeroPage(startIndex) {
    const rows = [];
    const count = Math.min(state.pageSize, state.totalInstructions - startIndex);
    for (let i = 0; i < count; i++) {
        const addr = state.baseAddress + (startIndex + i) * 4;
        rows.push({
            addr: addr.toString(16).toUpperCase().padStart(8, '0'),
            bytes: '00000000',
            label: '',
            op: 'nop',
            args: '',
            comment: ''
        });
    }
    return rows;
}

// ── Symbols Panel ─────────────────────────────────────────────────────────────

function toggleSymbolsPanel() {
    state.symbolsActive = !state.symbolsActive;
    symbolsPanel.classList.toggle('visible', state.symbolsActive);
    symbolsToggleBtn.classList.toggle('active', state.symbolsActive);
    if (state.symbolsActive) {
        loadSymbols();
    }
}

function loadSymbols() {
    vscode.postMessage({ type: 'getSymbols' });
}

function symNavigate(addrHex) {
    // Strip leading 0x if present, then navigate
    goToAddress(addrHex.replace(/^0x/i, ''));
}

// ── Region helpers ────────────────────────────────────────────────────────────

/** Parse an address hex string (with or without 0x prefix) to a number. */
function parseAddrHex(addrHex) {
    return parseInt(String(addrHex).replace(/^0x/i, ''), 16);
}

/**
 * Returns true when the given address hex string falls within the address
 * range of the currently open panel.  For live panels with a known region
 * this filters out symbols that belong to a different region.  File-based
 * panels (totalBytes == 0) show everything.
 */
function isAddrInCurrentRegion(addrHex) {
    if (!state.isLive || state.totalBytes <= 0) return true;
    const addr = parseAddrHex(addrHex);
    if (isNaN(addr)) return false;
    const physAddr = toPhysAddr(addr);
    const physBase = toPhysAddr(state.baseAddress);
    return physAddr >= physBase && physAddr < physBase + state.totalBytes;
}

/**
 * Maps a numeric address to a short PS2 memory region name shown as a badge.
 * Returns an empty string when the address does not fall in a named region.
 */
function getPs2RegionName(addr) {
    const a = addr >>> 0;
    const p = toPhysAddr(a);
    // EE Main RAM — physical 0x00000000–0x01FFFFFF, or uncached mirror 0x20000000–0x21FFFFFF
    if ((a >= 0x20000000 && a <= 0x21FFFFFF) || p < 0x02000000) return 'EE RAM';
    // IOP RAM — EE VTLB mirror 0x1C000000–0x1DFFFFFF, or IOP physical 0x00000000–0x001FFFFF
    if (a >= 0x1C000000 && a <= 0x1DFFFFFF) return 'IOP';
    // BIOS ROM — physical 0x1FC00000–0x1FFFFFFF (also KSEG1 0xBFC00000)
    if (p >= 0x1FC00000 && p <= 0x1FFFFFFF) return 'BIOS';
    // Scratchpad — 0x70000000–0x70003FFF
    if (a >= 0x70000000 && a <= 0x70003FFF) return 'SPD';
    return '';
}

function renderSymLabels() {
    if (!symLabelsList) return;
    const items = (state.symLabels || []).filter(item => isAddrInCurrentRegion(item.address));
    if (items.length === 0) {
        symLabelsList.innerHTML = '<div class="sym-empty">No labels defined.</div>';
        return;
    }
    symLabelsList.innerHTML = items.map(item => {
        const escapedComment = item.comment ? escHtml(item.comment) : '';
        const comment = escapedComment ? \`<span class="sym-item-comment" title="\${escapedComment}">\${escapedComment}</span>\` : '';
        return \`<div class="sym-item" data-addr="\${escHtml(item.address)}">
            <span class="sym-item-addr">\${escHtml(item.address)}</span>
            <span class="sym-item-name" title="\${escHtml(item.label)}">\${escHtml(item.label)}</span>
            \${comment}
            <button class="sym-delete-btn" data-addr="\${escHtml(item.address)}" title="Delete label">×</button>
        </div>\`;
    }).join('');

    // Navigate on row click (but not on delete button)
    symLabelsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', e => {
            if (e.target.classList.contains('sym-delete-btn')) return;
            symNavigate(el.dataset.addr);
        });
    });
    // Delete button handler
    symLabelsList.querySelectorAll('.sym-delete-btn').forEach(btn => {
        btn.addEventListener('click', e => {
            e.stopPropagation();
            const addrStr = btn.dataset.addr;
            const addrNum = parseInt(addrStr.replace(/^0x/i, ''), 16);
            vscode.postMessage({ type: 'deleteLabel', address: addrNum });
        });
    });
}

function renderSymStrings() {
    if (!symStringsList) return;
    const items = (state.symStrings || []).filter(item => isAddrInCurrentRegion(item.address));
    if (items.length === 0) {
        symStringsList.innerHTML = '<div class="sym-empty">No strings found. Click "Scan for strings".</div>';
        return;
    }
    symStringsList.innerHTML = items.map(item => {
        return \`<div class="sym-item" data-addr="\${escHtml(item.address)}">
            <span class="sym-item-addr">\${escHtml(item.address)}</span>
            <span class="sym-item-value" title="\${escHtml(item.value)}">\${escHtml(item.value)}</span>
        </div>\`;
    }).join('');

    symStringsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate(el.dataset.addr));
    });
}

function renderSymFunctions() {
    if (!symFunctionsList) return;
    const items = (state.symFunctions || []).filter(item => isAddrInCurrentRegion(item.address));
    if (items.length === 0) {
        symFunctionsList.innerHTML = '<div class="sym-empty">No functions found. Click "Scan for functions".</div>';
        return;
    }
    symFunctionsList.innerHTML = items.map(item => {
        return \`<div class="sym-item" data-addr="\${escHtml(item.address)}">
            <span class="sym-item-addr">\${escHtml(item.address)}</span>
            <span class="sym-item-name" title="\${escHtml(item.label)}">\${escHtml(item.label)}</span>
        </div>\`;
    }).join('');

    symFunctionsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate(el.dataset.addr));
    });
}

function escHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// Symbols toggle button
symbolsToggleBtn.addEventListener('click', () => toggleSymbolsPanel());

// Close / refresh buttons inside the panel
closeSymbolsBtn.addEventListener('click', () => {
    state.symbolsActive = false;
    symbolsPanel.classList.remove('visible');
    symbolsToggleBtn.classList.remove('active');
});

symbolsRefreshBtn.addEventListener('click', () => loadSymbols());

// Tab switching inside the symbols panel
document.querySelectorAll('.sym-tab').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.sym-tab').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.sym-tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        const tab = btn.dataset.symtab;
        document.getElementById('sym-' + tab + '-content')?.classList.add('active');
    });
});

// Add label button
symAddBtn.addEventListener('click', () => {
    const addrRaw = symNewAddr.value.trim();
    const name    = symNewName.value.trim();
    const comment = symNewComment.value.trim();
    if (!addrRaw || !name) { return; }
    const addrNum = parseInt(addrRaw.replace(/^0x/i, ''), 16);
    if (isNaN(addrNum)) { return; }
    vscode.postMessage({ type: 'addLabel', address: addrNum, labelName: name, comment: comment || undefined });
    symNewAddr.value = '';
    symNewName.value = '';
    symNewComment.value = '';
});

// Allow Enter key in the add-label inputs to submit
[symNewAddr, symNewName, symNewComment].forEach(input => {
    input?.addEventListener('keydown', e => {
        if (e.key === 'Enter') { symAddBtn.click(); }
    });
});

// Scan buttons — ask the backend to scan the current region directly
symScanStringsBtn.addEventListener('click', () => {
    symStringsStatus.textContent = 'Scanning…';
    symScanStringsBtn.disabled = true;
    vscode.postMessage({ type: 'scanStrings' });
    setTimeout(() => {
        symScanStringsBtn.disabled = false;
        if (symStringsStatus.textContent === 'Scanning…') symStringsStatus.textContent = '';
    }, 120000);
});

symScanFunctionsBtn.addEventListener('click', () => {
    symFunctionsStatus.textContent = 'Scanning…';
    symScanFunctionsBtn.disabled = true;
    vscode.postMessage({ type: 'scanFunctions' });
    setTimeout(() => {
        symScanFunctionsBtn.disabled = false;
        if (symFunctionsStatus.textContent === 'Scanning…') symFunctionsStatus.textContent = '';
    }, 120000);
});

// ── Debug Panel ──────────────────────────────────────────────────────────────

const MIPS_REG_NAMES = [
    '$zero','$at','$v0','$v1','$a0','$a1','$a2','$a3',
    '$t0','$t1','$t2','$t3','$t4','$t5','$t6','$t7',
    '$s0','$s1','$s2','$s3','$s4','$s5','$s6','$s7',
    '$t8','$t9','$k0','$k1','$gp','$sp','$fp','$ra'
];

const COP0_REG_NAMES = [
    'Index','Random','EntryLo0','EntryLo1','Context','PageMask','Wired','Reserved7',
    'BadVAddr','Count','EntryHi','Compare','Status','Cause','EPC','PRId',
    'Config','LLAddr','WatchLo','WatchHi','XContext','Reserved21','Reserved22','Reserved23',
    'Reserved24','PerfCnt','ErrCtl','CacheErr','TagLo','TagHi','ErrorEPC','Reserved31'
];

const GS_PRIV_NAMES = [
    'PMODE','SMODE1','SMODE2','SRFSH','SYNCH1','SYNCH2','SYNCV',
    'DISPFB1','DISPLAY1','DISPFB2','DISPLAY2',
    'EXTBUF','EXTDATA','EXTWRITE','BGCOLOR',
    'CSR','IMR','BUSDIR','SIGLBLID'
];

function hex32(n) {
    return (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

function hex64(lo, hi) {
    // lo and hi are 32-bit halves; hi may be 0 for most regs
    const loStr = (lo >>> 0).toString(16).toUpperCase().padStart(8, '0');
    const hiStr = (hi >>> 0).toString(16).toUpperCase().padStart(8, '0');
    return hiStr + '_' + loStr;
}

// Reinterpret a raw IEEE-754 uint32 bit pattern as a float string.
// Uses a DataView trick to avoid precision loss.
function rawBitsToFloat(bits) {
    const buf = new ArrayBuffer(4);
    new Uint32Array(buf)[0] = bits >>> 0;
    const f = new DataView(buf).getFloat32(0, true);
    // Show up to 6 significant digits; strip trailing zeros for brevity
    return f.toPrecision(6).replace(/\\.?0+$/, '');
}

function showDebugPanel(regs, stackBytes, stackBase) {
    debugPanel.classList.add('visible');
    renderGprPanel(regs);
    renderCop0Panel(regs);
    renderFprPanel(regs);
    renderFcrPanel(regs);
    renderVu0fPanel(regs);
    renderVu0iPanel(regs);
    renderGsPanel(regs);
    renderStackPanel(regs, stackBytes, stackBase);
}

function hideDebugPanel() {
    debugPanel.classList.remove('visible');
}

// ── GPR tab ──────────────────────────────────────────────────────────────────
function renderGprPanel(regs) {
    if (!regs) { regContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">General Purpose Registers</div>';
    html += \`<div class="dbg-row"><span class="dbg-name">PC  </span><span class="dbg-val">\${hex32(regs.pc)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">HI  </span><span class="dbg-val">\${hex32(regs.hi)}</span><span class="dbg-name" style="margin-left:8px">LO  </span><span class="dbg-val">\${hex32(regs.lo)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">HI1 </span><span class="dbg-val">\${hex32(regs.hi1)}</span><span class="dbg-name" style="margin-left:8px">LO1 </span><span class="dbg-val">\${hex32(regs.lo1)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">SA  </span><span class="dbg-val">\${hex32(regs.sa)}</span></div>\`;
    html += '<div class="dbg-divider"></div>';
    const gpr = regs.gpr ?? [];
    for (let i = 0; i < 32; i++) {
        const name = MIPS_REG_NAMES[i].padEnd(5);
        const val = hex32(gpr[i] ?? 0);
        const nz  = (gpr[i] ?? 0) !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name">\${name}</span><span class="dbg-val">\${val}</span></div>\`;
    }
    regContent.innerHTML = html;
}

// ── CP0 tab ──────────────────────────────────────────────────────────────────
function renderCop0Panel(regs) {
    const el = document.getElementById('cop0-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">System Control (COP0)</div>';
    const cop0 = regs.cop0Regs ?? [];
    for (let i = 0; i < 32; i++) {
        const name = ('r' + i + ' ' + COP0_REG_NAMES[i]).substring(0, 14).padEnd(14);
        const val  = hex32(cop0[i] ?? 0);
        const nz   = (cop0[i] ?? 0) !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">\${name}</span><span class="dbg-val">\${val}</span></div>\`;
    }
    el.innerHTML = html;
}

// ── FPR tab ──────────────────────────────────────────────────────────────────
function renderFprPanel(regs) {
    const el = document.getElementById('fpr-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">Floating Point Registers (COP1)</div>';
    const fpr = regs.fpr ?? [];
    for (let i = 0; i < 32; i++) {
        const bits = (fpr[i] ?? 0) >>> 0;
        const fval = rawBitsToFloat(bits);
        const nz   = bits !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name">f\${String(i).padStart(2,'0')} </span><span class="dbg-val">\${hex32(bits)}</span><span class="dbg-dim" style="margin-left:6px">\${fval}</span></div>\`;
    }
    html += '<div class="dbg-divider"></div>';
    const accBits = (regs.fprAcc ?? 0) >>> 0;
    html += \`<div class="dbg-row\${accBits !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">ACC  </span><span class="dbg-val">\${hex32(accBits)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat(accBits)}</span></div>\`;
    el.innerHTML = html;
}

// ── FCR tab ──────────────────────────────────────────────────────────────────
function renderFcrPanel(regs) {
    const el = document.getElementById('fcr-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">FPU Control Registers (FCR)</div>';
    const fcr0  = (regs.fcr0  ?? 0) >>> 0;
    const fcr31 = (regs.fcr31 ?? 0) >>> 0;
    html += \`<div class="dbg-row"><span class="dbg-name">FCR0  </span><span class="dbg-val">\${hex32(fcr0)}</span><span class="dbg-dim" style="margin-left:6px">FIR (implementation)</span></div>\`;
    html += \`<div class="dbg-row\${fcr31 !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">FCR31 </span><span class="dbg-val">\${hex32(fcr31)}</span><span class="dbg-dim" style="margin-left:6px">FCSR (control/status)</span></div>\`;
    // Decode key FCR31 fields
    html += '<div class="dbg-divider"></div>';
    html += '<div class="dbg-section-title">FCR31 decode</div>';
    const rm    = fcr31 & 0x3;
    const flags = (fcr31 >> 7)  & 0x1F;
    const ena   = (fcr31 >> 12) & 0x1F;
    const cause = (fcr31 >> 18) & 0x3F;
    const cc    = (fcr31 >> 23) & 0x1;
    const fs    = (fcr31 >> 24) & 0x1;
    const rmStr = ['RN (nearest)','RZ (zero)','RP (+inf)','RM (-inf)'][rm];
    html += \`<div class="dbg-row"><span class="dbg-name">RM    </span><span class="dbg-val">\${rmStr}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Flags </span><span class="dbg-val">\${flags.toString(2).padStart(5,'0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I (sticky)</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Enable</span><span class="dbg-val">\${ena.toString(2).padStart(5,'0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Cause </span><span class="dbg-val">\${cause.toString(2).padStart(6,'0')}</span><span class="dbg-dim" style="margin-left:6px">E V Z O U I</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">CC    </span><span class="dbg-val">\${cc}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">FS    </span><span class="dbg-val">\${fs}</span><span class="dbg-dim" style="margin-left:6px">flush-to-zero</span></div>\`;
    el.innerHTML = html;
}

// ── VU0f tab ─────────────────────────────────────────────────────────────────
function renderVu0fPanel(regs) {
    const el = document.getElementById('vu0f-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">VU0 Float Registers (vf0–vf31)</div>';
    const xs = regs.vu0fX ?? [];
    const ys = regs.vu0fY ?? [];
    const zs = regs.vu0fZ ?? [];
    const ws = regs.vu0fW ?? [];
    for (let i = 0; i < 32; i++) {
        const xb = (xs[i] ?? 0) >>> 0; const yb = (ys[i] ?? 0) >>> 0;
        const zb = (zs[i] ?? 0) >>> 0; const wb = (ws[i] ?? 0) >>> 0;
        const nz = xb !== 0 || yb !== 0 || zb !== 0 || wb !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}">
            <span class="dbg-name">vf\${String(i).padStart(2,'0')}</span>
            <span class="dbg-dim" style="font-size:9px;margin-left:4px">x:\${rawBitsToFloat(xb)} y:\${rawBitsToFloat(yb)} z:\${rawBitsToFloat(zb)} w:\${rawBitsToFloat(wb)}</span>
        </div>\`;
    }
    html += '<div class="dbg-divider"></div>';
    const axb = (regs.vu0AccX ?? 0) >>> 0; const ayb = (regs.vu0AccY ?? 0) >>> 0;
    const azb = (regs.vu0AccZ ?? 0) >>> 0; const awb = (regs.vu0AccW ?? 0) >>> 0;
    html += \`<div class="dbg-row"><span class="dbg-name">ACC  </span><span class="dbg-dim" style="font-size:9px;margin-left:4px">x:\${rawBitsToFloat(axb)} y:\${rawBitsToFloat(ayb)} z:\${rawBitsToFloat(azb)} w:\${rawBitsToFloat(awb)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Q    </span><span class="dbg-val">\${hex32((regs.vu0Q ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat((regs.vu0Q ?? 0) >>> 0)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">P    </span><span class="dbg-val">\${hex32((regs.vu0P ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat((regs.vu0P ?? 0) >>> 0)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">I    </span><span class="dbg-val">\${hex32((regs.vu0I ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">\${rawBitsToFloat((regs.vu0I ?? 0) >>> 0)}</span></div>\`;
    html += '<div class="dbg-divider"></div>';
    html += \`<div class="dbg-row"><span class="dbg-name">Status</span><span class="dbg-val">\${hex32(regs.vu0Status ?? 0)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">MAC   </span><span class="dbg-val">\${((regs.vu0Mac ?? 0) >>> 0).toString(16).toUpperCase().padStart(4,'0')}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Clip  </span><span class="dbg-val">\${hex32(regs.vu0Clip ?? 0)}</span></div>\`;
    el.innerHTML = html;
}

// ── VU0i tab ─────────────────────────────────────────────────────────────────
function renderVu0iPanel(regs) {
    const el = document.getElementById('vu0i-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">VU0 Integer Registers (vi0–vi15)</div>';
    const vi = regs.vu0i ?? [];
    for (let i = 0; i < 16; i++) {
        const val = (vi[i] ?? 0) & 0xFFFF;
        const nz  = val !== 0;
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name">vi\${String(i).padStart(2,'0')} </span><span class="dbg-val">\${val.toString(16).toUpperCase().padStart(4,'0')}</span><span class="dbg-dim" style="margin-left:6px">\${val}</span></div>\`;
    }
    el.innerHTML = html;
}

// ── GS tab ───────────────────────────────────────────────────────────────────
function renderGsPanel(regs) {
    const el = document.getElementById('gs-content');
    if (!el) return;
    if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    let html = '<div class="dbg-section-title">Graphics Synthesizer Registers</div>';
    const priv = regs.gsPriv ?? [];
    for (let i = 0; i < GS_PRIV_NAMES.length; i++) {
        const raw = priv[i] ?? 0;
        // Treat as two 32-bit halves (JSON loses upper bits beyond 53-bit safe int)
        const lo = (raw & 0xFFFFFFFF) >>> 0;
        const hi = Math.floor(raw / 0x100000000) >>> 0;
        const valStr = hi !== 0 ? hex64(lo, hi) : hex32(lo);
        const nz = raw !== 0;
        const name = GS_PRIV_NAMES[i].padEnd(10);
        html += \`<div class="dbg-row\${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">\${name}</span><span class="dbg-val" style="font-size:10px">\${valStr}</span></div>\`;
    }
    el.innerHTML = html;
}

function renderStackPanel(regs, stackBytes, stackBase) {
    if (!regs) { stackContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    const sp = (regs.gpr ?? [])[29] ?? 0;
    let html = \`<div class="dbg-section-title">Stack  SP=\${hex32(sp)}</div>\`;
    if (!stackBytes || stackBytes.length === 0) {
        html += '<div class="dbg-row dbg-dim">Stack data unavailable</div>';
        stackContent.innerHTML = html;
        return;
    }
    // Show 32 slots (128 bytes)
    for (let i = 0; i < Math.min(stackBytes.length, 128); i += 4) {
        const addr = (stackBase + i) >>> 0;
        const word = ((stackBytes[i+3] << 24) | (stackBytes[i+2] << 16) | (stackBytes[i+1] << 8) | stackBytes[i]) >>> 0;
        const addrStr = hex32(addr);
        const valStr  = hex32(word);
        // Highlight potential return addresses (KSEG0/KSEG1 range)
        const isCodeAddr = word >= 0x80000000 && word < 0xC0000000;
        const cls = isCodeAddr ? ' dbg-retaddr' : '';
        const annotation = isCodeAddr ? ' <span class="dbg-dim">← code ptr</span>' : '';
        html += \`<div class="dbg-row\${cls}"><span class="dbg-addr">\${addrStr}</span><span class="dbg-val">\${valStr}</span>\${annotation}</div>\`;
    }
    stackContent.innerHTML = html;
}

// Debug panel tab switching
document.querySelectorAll('.debug-tab').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.debug-tab').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.debug-tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        const tab = btn.dataset.tab;
        document.getElementById(tab + '-content')?.classList.add('active');
    });
});

playPauseBtn.addEventListener('click', () => {
    livePaused = !livePaused;
    if (livePaused) {
        stopLiveRefresh();
        vscode.postMessage({ type: 'pause' });
        setLivePausedUi(true);
    } else {
        vscode.postMessage({ type: 'resume' });
        startLiveRefresh();
        setLivePausedUi(false);
    }
});

window.addEventListener('unload', () => {
    if (liveRefreshTimer !== null) {
        clearInterval(liveRefreshTimer);
        liveRefreshTimer = null;
    }
    if (liveStatusTimer !== null) {
        clearInterval(liveStatusTimer);
        liveStatusTimer = null;
    }
});
`;
}
