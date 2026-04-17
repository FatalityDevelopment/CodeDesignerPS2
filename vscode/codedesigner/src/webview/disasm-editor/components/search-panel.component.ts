import {
  Component,
  ViewEncapsulation,
  output,
  inject,
  signal,
  effect,
  ElementRef,
  viewChild,
  computed,
} from '@angular/core';
import { EditorState } from '../state/editor.state';
import { SearchMode } from '../state/types';

/**
 * SearchPanel - Self-contained search UI component.
 *
 * Uses Angular signals for reactive state. Emits events to parent for
 * actions that require access to the disassembly grid (navigation, rendering).
 */
@Component({
  selector: 'app-search-panel',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    @if (visible()) {
      <div id="search-panel" class="visible">
        <div class="search-row">
          <div class="search-input-wrapper">
            <input
              #searchInput
              type="text"
              class="search-input"
              [class.error]="hasError()"
              [placeholder]="placeholder()"
              [value]="query()"
              (input)="onInput($event)"
              (keydown)="onKeydown($event)"
            />
            <button class="search-clear" (click)="clearSearch()">&#xD7;</button>
          </div>
          <div class="search-mode">
            @for (mode of modes; track mode.id) {
              <button
                class="mode-btn"
                [class.active]="currentMode() === mode.id"
                [attr.data-mode]="mode.id"
                [title]="mode.title"
                (click)="setMode(mode.id)"
              >{{ mode.label }}</button>
            }
          </div>
          <div class="search-nav">
            <button
              class="nav-btn"
              [disabled]="!hasMatches()"
              title="Previous Match (Shift+Enter)"
              (click)="prevMatch.emit()"
            >&#8593;</button>
            <button
              class="nav-btn"
              [disabled]="!hasMatches()"
              title="Next Match (Enter)"
              (click)="nextMatch.emit()"
            >&#8595;</button>
          </div>
          <span class="search-info">{{ statusText() }}</span>
          <button class="close-search" title="Close (Escape)" (click)="close.emit()">&#xD7;</button>
        </div>
      </div>
    }
  `,
  styles: [`
    /* Search panel styles are inherited from disasm-editor.css */
  `],
})
export class SearchPanelComponent {
  protected readonly state = inject(EditorState);

  /** Reference to input element for focus management */
  private readonly inputRef = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  /** Local UI state */
  protected readonly query = signal('');
  protected readonly hasError = signal(false);

  /** Computed from EditorState */
  protected readonly visible = this.state.panels.search;
  protected readonly currentMode = this.state.searchMode;
  protected readonly hasMatches = this.state.hasSearchMatches;
  protected readonly placeholder = computed(() => {
    switch (this.currentMode()) {
      case 'bytes':
        return 'Byte pattern: 0x12 34 AB CD';
      case 'string':
        return 'String in binary (case-sensitive)';
      case 'regex':
        return 'Regular expression';
      default:
        return 'Search text';
    }
  });

  /** Search mode options */
  protected readonly modes: ReadonlyArray<{ id: SearchMode; label: string; title: string }> = [
    { id: 'text', label: 'Aa', title: 'Text Search' },
    { id: 'bytes', label: '0x', title: 'Byte Pattern Search' },
    { id: 'string', label: '"ab"', title: 'String Search (ASCII/UTF-8)' },
    { id: 'regex', label: '.*', title: 'Regular Expression' },
  ];

  /** Output events */
  readonly search = output<{ query: string; mode: SearchMode }>();
  readonly clear = output<void>();
  readonly close = output<void>();
  readonly prevMatch = output<void>();
  readonly nextMatch = output<void>();

  /** Computed status text */
  protected readonly statusText = () => {
    const matches = this.state.searchMatches();
    const currentIdx = this.state.currentMatchIndex();
    if (matches.length === 0) {
      return this.state.searchQuery() ? 'No results' : '';
    }
    return `${currentIdx + 1} of ${matches.length}`;
  };

  constructor() {
    // Focus input when panel becomes visible
    effect(() => {
      if (this.visible()) {
        // Use setTimeout to ensure DOM is ready
        setTimeout(() => {
          const input = this.inputRef()?.nativeElement;
          if (input) {
            input.focus();
            input.select();
          }
        }, 0);
      }
    });

    effect(() => {
      this.query.set(this.state.searchQuery());
      if (!this.state.searchQuery()) {
        this.hasError.set(false);
      }
    });
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.query.set(value);
    this.hasError.set(false);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      if (event.shiftKey) {
        this.prevMatch.emit();
      } else if (this.query().trim() && (!this.hasMatches() || this.query().trim() !== this.state.searchQuery())) {
        this.performSearch();
      } else {
        this.nextMatch.emit();
      }
    } else if (event.key === 'Escape') {
      this.close.emit();
    }
  }

  protected setMode(mode: SearchMode): void {
    this.state.searchMode.set(mode);
    this.hasError.set(false);
    // Re-search if there's a query
    if (this.query()) {
      this.performSearch();
    }
  }

  protected clearSearch(): void {
    this.query.set('');
    this.hasError.set(false);
    this.clear.emit();
  }

  private performSearch(): void {
    const query = this.query().trim();
    const mode = this.currentMode();

    if (!query) {
      this.clear.emit();
      return;
    }

    // Validate byte pattern
    if (mode === 'bytes') {
      const pattern = this.parseBytePattern(query);
      if (!pattern) {
        this.hasError.set(true);
        return;
      }
    }

    this.hasError.set(false);
    this.state.searchQuery.set(query);
    this.search.emit({ query, mode });
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

    return result.length > 0 ? result : null;
  }
}
