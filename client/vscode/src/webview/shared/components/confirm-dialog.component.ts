import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
} from '@angular/core';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
@if (visible()) {
  <div class="cd-confirm-backdrop" (click)="cancelled.emit()">
    <div class="cd-confirm-dialog" (click)="$event.stopPropagation()">
      <p class="cd-confirm-title">&#x26A0; {{ title() }}</p>
      @if (body()) {
        <p class="cd-confirm-body">{{ body() }}</p>
      }
      @if (note()) {
        <p class="cd-confirm-note lm-muted">{{ note() }}</p>
      }
      <div class="cd-confirm-actions">
        <button class="lm-tool-btn lm-danger-btn" (click)="confirmed.emit()">{{ confirmLabel() }}</button>
        <button class="lm-tool-btn" (click)="cancelled.emit()">Cancel</button>
      </div>
    </div>
  </div>
}
`,
  styles: [`
.cd-confirm-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0,0,0,.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}
.cd-confirm-dialog {
  background: var(--vscode-editor-background, #1e1e1e);
  border: 1px solid var(--vscode-panel-border, #444);
  border-radius: 6px;
  padding: 20px 24px;
  min-width: 300px;
  max-width: 480px;
}
.cd-confirm-title {
  font-weight: 600;
  margin: 0 0 8px;
  color: var(--vscode-editorWarning-foreground, #cca700);
}
.cd-confirm-body {
  margin: 0 0 6px;
  word-break: break-all;
}
.cd-confirm-note {
  margin: 0 0 14px;
  font-size: 11px;
}
.cd-confirm-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
`],
})
export class ConfirmDialogComponent {
  readonly visible      = input<boolean>(false);
  readonly title        = input<string>('Are you sure?');
  readonly body         = input<string>('');
  readonly note         = input<string>('This action cannot be undone.');
  readonly confirmLabel = input<string>('Delete');

  readonly confirmed  = output<void>();
  readonly cancelled  = output<void>();
}
