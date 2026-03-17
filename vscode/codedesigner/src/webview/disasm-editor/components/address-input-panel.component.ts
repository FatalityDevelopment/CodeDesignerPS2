import {
  Component,
  ViewEncapsulation,
  input,
  output,
  signal,
  effect,
  ElementRef,
  viewChild,
  computed,
} from '@angular/core';

/**
 * AddressInputPanel - Reusable address input panel.
 *
 * Used for "Go to Address" and "Set PC" functionality.
 * Validates hex addresses and emits events for navigation.
 */
@Component({
  selector: 'app-address-input-panel',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    @if (visible()) {
      <div [id]="panelId()" class="visible">
        <div class="search-row">
          <div class="search-input-wrapper">
            <input
              #addressInput
              type="text"
              class="search-input"
              [class.error]="hasError()"
              [placeholder]="placeholder()"
              [value]="inputValue()"
              (input)="onInput($event)"
              (keydown)="onKeydown($event)"
            />
            <button class="search-clear" (click)="clearInput()">&#xD7;</button>
          </div>
          <div class="search-nav">
            <button class="nav-btn" [title]="buttonTitle()" (click)="submit()">
              {{ buttonLabel() }}
            </button>
          </div>
          <span class="search-info">{{ statusMessage() }}</span>
          <button class="close-search" title="Close (Escape)" (click)="close.emit()">&#xD7;</button>
        </div>
      </div>
    }
  `,
})
export class AddressInputPanelComponent {
  /** Input signals */
  readonly visible = input.required<boolean>();
  readonly panelId = input.required<string>();
  readonly placeholder = input<string>('Address (hex)');
  readonly buttonLabel = input<string>('Go');
  readonly buttonTitle = input<string>('Go to address (Enter)');
  readonly externalStatus = input<string>('');
  readonly externalValue = input<string>('');

  /** Output events */
  readonly submit$ = output<number>();
  readonly close = output<void>();
  readonly valueChange = output<string>();

  /** Reference to input element for focus management */
  private readonly inputRef = viewChild<ElementRef<HTMLInputElement>>('addressInput');

  /** Local state */
  protected readonly inputValue = signal('');
  protected readonly hasError = signal(false);
  protected readonly localStatus = signal('');

  /** Computed status - prefer external status if provided */
  protected readonly statusMessage = computed(() =>
    this.externalStatus() || this.localStatus()
  );

  constructor() {
    // Focus input when panel becomes visible
    effect(() => {
      if (this.visible()) {
        setTimeout(() => {
          const input = this.inputRef()?.nativeElement;
          if (input) {
            input.focus();
            input.select();
          }
        }, 0);
      }
    });

    // Sync external value to input
    effect(() => {
      const ext = this.externalValue();
      this.inputValue.set(ext);
    });
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.inputValue.set(value);
    this.hasError.set(false);
    this.localStatus.set('');
    this.valueChange.emit(value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.submit();
    } else if (event.key === 'Escape') {
      this.close.emit();
    }
  }

  protected clearInput(): void {
    this.inputValue.set('');
    this.hasError.set(false);
    this.localStatus.set('');
    this.inputRef()?.nativeElement?.focus();
  }

  protected submit(): void {
    const addr = this.parseAddress(this.inputValue());
    if (addr === null) {
      this.hasError.set(true);
      this.localStatus.set('Invalid hex address');
      return;
    }

    this.hasError.set(false);
    this.submit$.emit(addr);
  }

  /** Parse hex address with optional 0x prefix */
  private parseAddress(value: string): number | null {
    const cleaned = value.trim().replace(/^0x/i, '');
    if (!cleaned || !/^[0-9a-f]+$/i.test(cleaned)) {
      return null;
    }
    const parsed = parseInt(cleaned, 16);
    return isNaN(parsed) ? null : parsed;
  }

  /** Public method to set status from parent */
  setStatus(message: string, isError = false): void {
    this.localStatus.set(message);
    this.hasError.set(isError);
  }

  /** Public method to set value from parent */
  setValue(value: string): void {
    this.inputValue.set(value);
    this.hasError.set(false);
  }
}
