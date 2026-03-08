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
${getGotoPanel()}
${getSearchPanel()}
${getLiveToolbar()}
${getHexContainer()}
</div>
<div id="content-wrapper">
${getGrid()}
${getDebugPanel()}
</div>

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

function getLiveToolbar(): string {
    return `<div id="live-toolbar">
    <span class="live-indicator" id="live-indicator">● LIVE</span>
    <button id="play-pause-btn" title="Pause live feed" aria-label="Pause live feed">⏸ Pause</button>
</div>`;
}

function getHexContainer(): string {
    return `<div id="hex-container"></div>`;
}

function getGrid(): string {
    return `<div id="grid" tabindex="0"></div>`;
}

function getDebugPanel(): string {
    return `<div id="debug-panel">
    <div id="debug-tabs">
        <button class="debug-tab active" data-tab="registers">Registers</button>
        <button class="debug-tab" data-tab="stack">Stack</button>
    </div>
    <div id="reg-content" class="debug-tab-content active"></div>
    <div id="stack-content" class="debug-tab-content"></div>
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
const liveToolbar = document.getElementById("live-toolbar");
const liveIndicator = document.getElementById("live-indicator");
const playPauseBtn = document.getElementById("play-pause-btn");
const debugPanel = document.getElementById("debug-panel");
const regContent = document.getElementById("reg-content");
const stackContent = document.getElementById("stack-content");

const state = {
    cursor: 0,
    disasmRows: [],
    totalInstructions: 0,
    pageSize: 1000,
    baseAddress: 0,
    hexData: [],
    hexOffset: 0,
    loadedPages: new Map(),
    searchMode: 'text',
    searchQuery: '',
    searchMatches: [],
    currentMatch: -1,
    searchActive: false,
    gotoActive: false,
    isLive: false,
    breakpoints: new Set()
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
            if (msg.isLive) {
                state.isLive = true;
                liveToolbar.classList.add('visible');
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
        row.innerHTML = buildDisasmRowHtml(rowData, globalIndex);
        row.onclick = () => {
            state.cursor = globalIndex;
            updateCursor();
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
        liveToolbar.classList.add('paused');
        liveIndicator.classList.add('paused');
        liveIndicator.textContent = '⏸ PAUSED';
        playPauseBtn.textContent = '▶ Resume';
        playPauseBtn.title = 'Resume live feed';
        playPauseBtn.setAttribute('aria-label', 'Resume live feed');
    } else {
        liveToolbar.classList.remove('paused');
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
    const startIndex = getPageStart(state.cursor);
    const zeroRows = generateZeroPage(startIndex);
    state.loadedPages.set(startIndex, zeroRows);
    state.disasmRows = zeroRows;
    renderDisassembly();
    hideDebugPanel();
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

// ── Debug Panel (Registers + Stack) ──────────────────────────────────────────

const MIPS_REG_NAMES = [
    '$zero','$at','$v0','$v1','$a0','$a1','$a2','$a3',
    '$t0','$t1','$t2','$t3','$t4','$t5','$t6','$t7',
    '$s0','$s1','$s2','$s3','$s4','$s5','$s6','$s7',
    '$t8','$t9','$k0','$k1','$gp','$sp','$fp','$ra'
];

function hex32(n) {
    return (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

function showDebugPanel(regs, stackBytes, stackBase) {
    debugPanel.classList.add('visible');
    renderRegPanel(regs);
    renderStackPanel(regs, stackBytes, stackBase);
}

function hideDebugPanel() {
    debugPanel.classList.remove('visible');
}

function renderRegPanel(regs) {
    let html = '<div class="dbg-section-title">CPU Registers</div>';
    // PC
    html += \`<div class="dbg-row"><span class="dbg-name">PC  </span><span class="dbg-val">\${hex32(regs.pc)}</span></div>\`;
    // HI/LO
    html += \`<div class="dbg-row"><span class="dbg-name">HI  </span><span class="dbg-val">\${hex32(regs.hi)}</span><span class="dbg-name" style="margin-left:8px">LO  </span><span class="dbg-val">\${hex32(regs.lo)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">HI1 </span><span class="dbg-val">\${hex32(regs.hi1)}</span><span class="dbg-name" style="margin-left:8px">LO1 </span><span class="dbg-val">\${hex32(regs.lo1)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">SA  </span><span class="dbg-val">\${hex32(regs.sa)}</span></div>\`;
    html += '<div class="dbg-divider"></div>';
    // GPRs
    for (let i = 0; i < 32; i++) {
        const name = MIPS_REG_NAMES[i].padEnd(5);
        const val = hex32(regs.gpr[i]);
        const changed = regs.gpr[i] !== 0;
        html += \`<div class="dbg-row\${changed ? ' dbg-nonzero' : ''}"><span class="dbg-name">\${name}</span><span class="dbg-val">\${val}</span></div>\`;
    }
    html += '<div class="dbg-divider"></div>';
    html += '<div class="dbg-section-title">COP0</div>';
    html += \`<div class="dbg-row"><span class="dbg-name">Status</span><span class="dbg-val">\${hex32(regs.cop0Status)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Cause </span><span class="dbg-val">\${hex32(regs.cop0Cause)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">EPC   </span><span class="dbg-val">\${hex32(regs.cop0Epc)}</span></div>\`;
    html += \`<div class="dbg-row"><span class="dbg-name">Count </span><span class="dbg-val">\${hex32(regs.cop0Count)}</span></div>\`;
    regContent.innerHTML = html;
}

function renderStackPanel(regs, stackBytes, stackBase) {
    if (!regs) { stackContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
    const sp = regs.gpr[29];
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
