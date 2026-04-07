import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  viewChild,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import {
  LabelManagerOverlayComponent,
  LabelManagerAction,
  LabelSetPayload,
} from '../disasm-editor/components/label-manager-overlay.component';
import '../disasm-editor/disasm-editor.css';
import './label-manager-page.css';

/**
 * Full-page host for the Label Manager standalone webview tab.
 *
 * This component wraps LabelManagerOverlayComponent and:
 *  - Keeps it always visible (no modal backdrop).
 *  - Routes action$ outputs to the extension via vscode.postMessage().
 *  - Receives labelManager* messages from the extension host and forwards
 *    them to the overlay's public API.
 *  - On init, signals 'ready' so the provider can send the initial data.
 */
@Component({
  selector: 'app-label-manager-page',
  standalone: true,
  imports: [LabelManagerOverlayComponent],
  encapsulation: ViewEncapsulation.None,
  template: `<app-label-manager-overlay #lmComp (action$)="onAction($event)" (close$)="onClose()" />`,
})
export class LabelManagerPageComponent implements AfterViewInit {
  private readonly lmRef = viewChild.required<LabelManagerOverlayComponent>('lmComp');
  private readonly vscode = getVsCodeApi();
  private readonly zone = inject(NgZone);

  ngAfterViewInit(): void {
    const lm = this.lmRef();
    // Force the overlay into full-page visible mode immediately.
    lm.visible.set(true);
    // Start live-value polling via the public API.
    lm.startPolling();

    // Listen for messages from the extension host.
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => {
        switch (data['type']) {
          case 'labelManagerSets':
            lm.setSets((data['sets'] as string[]) ?? []);
            break;
          case 'labelManagerSet':
            lm.setActiveSet(data['set'] as LabelSetPayload);
            break;
          case 'labelManagerLiveValues':
            lm.setLiveValues((data['values'] as Record<string, string>) ?? {});
            break;
          case 'labelManagerStatus':
            lm.showStatus((data['message'] as string) ?? '');
            break;
          case 'labelManagerError':
            lm.showStatus((data['message'] as string) ?? 'Error', true);
            break;
          case 'labelManagerExport':
            this._handleExport(data['json'] as string, data['setName'] as string);
            break;
          case 'prefillAddress':
            lm.openWithAddress(data['address'] as string);
            break;
        }
      });
    });

    // Tell the extension host the webview is ready to receive data.
    this.vscode.postMessage({ type: 'ready' });
  }

  /** Route label manager actions to the extension host. */
  onAction(action: LabelManagerAction): void {
    this.vscode.postMessage({ type: 'labelManagerAction', action });
  }

  /** In page (tab) mode we never close; if the overlay somehow calls close(), reopen it. */
  onClose(): void {
    this.lmRef().visible.set(true);
  }

  private _handleExport(json: string, setName: string): void {
    if (!json) { return; }
    const blob = new Blob([json], { type: 'application/json' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = (setName || 'labels') + '.json';
    a.click();
    URL.revokeObjectURL(url);
  }
}
