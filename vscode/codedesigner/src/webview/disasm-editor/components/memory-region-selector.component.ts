import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  computed,
} from '@angular/core';

export interface MemoryRegion {
  label: string;
  start: number;
  end: number;
}

const REGIONS: MemoryRegion[] = [
  { label: 'EE RAM',    start: 0x00000000, end: 0x01FFFFFF },
  { label: 'IOP RAM',   start: 0x1C000000, end: 0x1C3FFFFF },
  { label: 'BIOS ROM',  start: 0x1FC00000, end: 0x1FFFFFFF },
  { label: 'Scratchpad',start: 0x70000000, end: 0x70003FFF },
];

@Component({
  selector: 'app-memory-region-selector',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="mrs-bar">
  @for (region of regions; track region.label) {
    <button class="mrs-btn"
            [class.mrs-active]="activeLabel() === region.label"
            [title]="region.label + ': 0x' + region.start.toString(16).toUpperCase().padStart(8,'0')"
            (click)="regionSelected.emit(region)">
      {{ region.label }}
    </button>
  }
</div>
`,
  styles: [`
.mrs-bar {
  display: flex;
  gap: 2px;
  align-items: center;
  padding: 2px 4px;
  flex-wrap: wrap;
}
.mrs-btn {
  background: var(--vscode-button-secondaryBackground, #3a3d41);
  color: var(--vscode-button-secondaryForeground, #ccc);
  border: 1px solid transparent;
  border-radius: 3px;
  padding: 1px 7px;
  font-size: 11px;
  cursor: pointer;
  white-space: nowrap;
  line-height: 18px;
}
.mrs-btn:hover {
  background: var(--vscode-button-secondaryHoverBackground, #45494e);
}
.mrs-btn.mrs-active {
  border-color: var(--vscode-focusBorder, #007fd4);
  background: var(--vscode-button-background, #0e639c);
  color: var(--vscode-button-foreground, #fff);
}
`],
})
export class MemoryRegionSelectorComponent {
  readonly currentAddress = input<number>(0);
  readonly regionSelected = output<MemoryRegion>();

  readonly regions = REGIONS;

  readonly activeLabel = computed(() => {
    const addr = this.currentAddress();
    const match = REGIONS.find(r => addr >= r.start && addr <= r.end);
    return match?.label ?? null;
  });
}
