import { bootstrapApplication } from '@angular/platform-browser';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { XrefsViewComponent } from './xrefs-view.component';

function showBootstrapError(err: unknown): void {
    const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
    document.body.innerHTML =
        `<div style="font-family:monospace;padding:16px;color:#e74c3c;font-size:12px;">` +
        `<b>Cross-references view failed to initialise</b><br><br>` +
        `<pre style="white-space:pre-wrap">${msg}</pre></div>`;
}

bootstrapApplication(XrefsViewComponent, {
    providers: [
        provideExperimentalZonelessChangeDetection()
    ]
}).catch(showBootstrapError);
