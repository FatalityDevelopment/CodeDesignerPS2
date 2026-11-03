import { bootstrapApplication } from '@angular/platform-browser';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { ZipperWorkbenchPageComponent } from './zipper-workbench-page.component';

function showBootstrapError(err: unknown): void {
  const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
  document.body.innerHTML =
    `<div style="font-family:monospace;padding:16px;color:#f48771;font-size:12px;">` +
    `<b>Zipper FileFormats failed to initialize</b><br><br>` +
    `<pre style="white-space:pre-wrap">${escapeHtml(msg)}</pre>` +
    `</div>`;
  try {
    const api = (window as unknown as { acquireVsCodeApi?(): { postMessage(m: unknown): void } })
      .acquireVsCodeApi?.();
    api?.postMessage({ type: 'bootstrapError', error: msg });
  } catch {
    // ignore
  }
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

bootstrapApplication(ZipperWorkbenchPageComponent, {
  providers: [
    provideExperimentalZonelessChangeDetection(),
  ],
}).catch(showBootstrapError);
