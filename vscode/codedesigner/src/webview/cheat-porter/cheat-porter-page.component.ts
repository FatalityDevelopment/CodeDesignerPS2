import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  viewChild,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { PageHeaderComponent } from '../shared/page-header.component';
import { PorterPageComponent, PortRequest, PortResultPayload } from './porter-page.component';
import '../shared/styles/shared.css';
import '../disasm-editor/disasm-editor.css';

/**
 * Standalone Cheat Porter page. Hosts PorterPageComponent and relays its
 * browse/port requests to CheatPorterProvider in the extension host.
 */
@Component({
  selector: 'app-cheat-porter-page',
  standalone: true,
  imports: [PorterPageComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  styles: [`
html, body { height: 100%; overflow: hidden; }
.cp-outer {
  display: flex;
  flex-direction: column;
  height: 100vh;
  overflow: hidden;
  background: var(--vscode-editor-background, #1e1e1e);
  color: var(--vscode-foreground, #d4d4d4);
}
.cp-body { display: flex; flex-direction: column; flex: 1; min-height: 0; }
`],
  template: `
<div class="cp-outer">
  <app-page-header icon="repeat" title="Cheat Porter" subtitle="Port cheat codes between two EE RAM dumps" />
  <div class="cp-body">
    <app-porter-page #porterComp
      (browseSource)="post('browseSource')"
      (browseTarget)="post('browseTarget')"
      (port$)="onPortRequest($event)" />
  </div>
</div>
`,
})
export class CheatPorterPageComponent implements AfterViewInit {
  private readonly vscode    = getVsCodeApi();
  private readonly zone      = inject(NgZone);
  private readonly porterRef = viewChild<PorterPageComponent>('porterComp');

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => { this._handleMessage(data); });
    });
    this.vscode.postMessage({ type: 'ready' });
  }

  post(type: string): void {
    this.vscode.postMessage({ type });
  }

  onPortRequest(req: PortRequest): void {
    this.vscode.postMessage({ type: 'portCheats', codes: req.codes, sourcePath: req.sourcePath, targetPath: req.targetPath });
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'browseSourceResult':
        this.porterRef()?.setSourcePath(data['filePath'] as string ?? '');
        break;
      case 'browseTargetResult':
        this.porterRef()?.setTargetPath(data['filePath'] as string ?? '');
        break;
      case 'portCheatsResult':
        this.porterRef()?.setResult(data as unknown as PortResultPayload);
        break;
    }
  }
}
