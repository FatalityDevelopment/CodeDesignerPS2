import { Component, Input, OnChanges, ViewEncapsulation } from '@angular/core';

/**
 * Displays a hex dump of a byte array.
 *
 * Accepts four `@Input()` bindings:
 * - `data`           – the bytes to display
 * - `offset`         – base address of the first byte (used for address column)
 * - `highlightAddr`  – byte address to highlight (-1 = none)
 * - `highlightLength`– number of bytes to highlight (default 4)
 *
 * The component uses `ViewEncapsulation.None` so the global disasm-editor
 * CSS rules targeting `#hex-header`, `#hex-container`, `.hex-row`, etc. apply
 * directly to the elements it renders.
 *
 * This component runs inside the Angular zone and re-renders via `ngOnChanges`
 * whenever any input changes.  The parent component drives updates by mutating
 * its bound properties inside `NgZone.run()`, which triggers change detection.
 */
@Component({
  selector: 'app-hex-view',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div id="hex-header"></div>
    <div id="hex-container"></div>
  `,
})
export class HexViewComponent implements OnChanges {
  @Input() data: number[] = [];
  @Input() offset = 0;
  @Input() highlightAddr = -1;
  @Input() highlightLength = 4;

  ngOnChanges(): void {
    this._render();
  }

  // ── Rendering ──────────────────────────────────────────────────────────────

  private _render(): void {
    const headerEl    = document.getElementById('hex-header');
    const containerEl = document.getElementById('hex-container');
    if (!headerEl || !containerEl) { return; }

    headerEl.innerHTML = '';
    headerEl.appendChild(this._buildHeader());

    containerEl.innerHTML = '';
    for (let i = 0; i < this.data.length; i += 16) {
      containerEl.appendChild(this._buildRow(i));
    }
  }

  private _buildHeader(): HTMLElement {
    const header = document.createElement('div');
    header.className = 'hex-row hex-header-row';
    let bytes = '';
    for (let i = 0; i < 16; i++) {
      if (i && i % 4 === 0) { bytes += '<span class="hex-sep"></span>'; }
      bytes += `<span class="hex-byte">+${i.toString(16).toUpperCase()}</span>`;
    }
    const rowAddr = (this.offset >>> 0).toString(16).padStart(8, '0').toUpperCase();
    header.innerHTML = `<span class="hex-addr">${rowAddr}</span><span class="hex-bytes">${bytes}</span><span class="hex-ascii">ASCII</span>`;
    return header;
  }

  private _buildRow(rowOffset: number): HTMLElement {
    const row = document.createElement('div');
    row.className = 'hex-row';
    const addr = (this.offset + rowOffset).toString(16).padStart(8, '0').toUpperCase();
    let bytesHtml = '';
    let asciiHtml = '';
    for (let i = 0; i < 16; i++) {
      if (i && i % 4 === 0) { bytesHtml += '<span class="hex-sep"></span>'; }
      let byteStr = '  ';
      let char = ' ';
      let highlight = false;
      const index = rowOffset + i;
      if (index < this.data.length) {
        const byte     = this.data[index];
        const byteAddr = this.offset + index;
        byteStr   = byte.toString(16).padStart(2, '0').toUpperCase();
        char      = byte >= 32 && byte <= 126 ? String.fromCharCode(byte) : '.';
        highlight = this.highlightAddr !== -1
          && byteAddr >= this.highlightAddr
          && byteAddr <  this.highlightAddr + this.highlightLength;
      }
      bytesHtml += `<span class="hex-byte${highlight ? ' highlight' : ''}">${byteStr}</span>`;
      asciiHtml += `<span class="hex-char${highlight ? ' highlight' : ''}">${char}</span>`;
    }
    row.innerHTML = `<span class="hex-addr">${addr}</span><span class="hex-bytes">${bytesHtml}</span><span class="hex-ascii">${asciiHtml}</span>`;
    return row;
  }
}
