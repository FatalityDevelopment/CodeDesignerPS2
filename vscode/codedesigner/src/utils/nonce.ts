/**
 * Generates a random nonce string for use in Content-Security-Policy headers.
 * The nonce allows specific inline scripts/styles to execute while blocking others.
 */
export function getNonce(): string {
    let text = "";
    const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    for (let i = 0; i < 32; i++) {
        text += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return text;
}

/**
 * Returns a minimal inline script (for use with a nonce) that installs
 * window.onerror and unhandledrejection handlers in a webview.
 *
 * These handlers catch errors that the async bootstrapApplication().catch()
 * cannot detect — e.g. a missing bundle (script 404), a synchronous crash in
 * zone.js or @angular/compiler during module initialisation, or any other
 * error thrown before the Angular catch handler is registered.
 *
 * Error text is HTML-escaped so stack traces with angle-brackets cannot inject
 * markup into the panel.
 */
export function getWebviewErrorHandlerScript(): string {
    return `
(function(){
  function esc(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');}
  function show(msg){document.body.innerHTML='<div style="font-family:monospace;padding:16px;color:#e74c3c;font-size:12px"><b>Panel failed to load</b><br><br><pre style="white-space:pre-wrap">'+esc(msg)+'</pre><br><small>Check the CodeDesigner Output channel for details.</small></div>';}
  window.onerror=function(m,s,l,c,e){show(e?e.stack||m:m);};
  window.addEventListener('unhandledrejection',function(e){show(e.reason?e.reason.stack||e.reason:String(e.reason));});
})();`;
}
