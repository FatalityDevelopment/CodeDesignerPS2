import { bootstrapApplication } from '@angular/platform-browser';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { JokerMakerPageComponent } from './joker-maker-page.component';

function showBootstrapError(err: unknown): void {
    const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
    document.body.innerHTML =
        `<div style="font-family:monospace;padding:16px;color:#e74c3c;font-size:12px;">` +
        `<b>Joker Maker failed to initialise</b><br><br>` +
        `<pre style="white-space:pre-wrap">${msg}</pre>` +
        `<br><small>Right-click -> Inspect to see full webview console.</small></div>`;
    try {
        const api = (window as unknown as { acquireVsCodeApi?(): { postMessage(m: unknown): void } })
            .acquireVsCodeApi?.();
        api?.postMessage({ type: 'bootstrapError', error: msg });
    } catch { /* ignore */ }
}

bootstrapApplication(JokerMakerPageComponent, {
    providers: [
        provideExperimentalZonelessChangeDetection(),
    ],
}).catch(showBootstrapError);
