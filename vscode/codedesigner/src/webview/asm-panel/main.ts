import '@angular/compiler';
import 'zone.js';
import { bootstrapApplication } from '@angular/platform-browser';
import { AsmPanelComponent } from './asm-panel.component';

function showBootstrapError(err: unknown): void {
    const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
    document.body.innerHTML =
        `<div style="font-family:monospace;padding:16px;color:#e74c3c;font-size:12px;">` +
        `<b>Panel failed to initialise</b><br><br>` +
        `<pre style="white-space:pre-wrap">${msg}</pre>` +
        `<br><small>Right-click → Inspect to see full webview console.</small></div>`;
    try {
        // Report to extension host Output channel if acquireVsCodeApi hasn't been
        // called yet (component never ran so the singleton hasn't been created).
        const api = (window as unknown as { acquireVsCodeApi?(): { postMessage(m: unknown): void } })
            .acquireVsCodeApi?.();
        api?.postMessage({ type: 'bootstrapError', error: msg });
    } catch { /* ignore — api already acquired by component */ }
}

bootstrapApplication(AsmPanelComponent).catch(showBootstrapError);
