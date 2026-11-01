import { Icon } from './Icon';
import { host, toasts, useSignals } from './index';

// Notifications in the bottom-right corner, the stand-in for VS Code's show*Message.
export function Toasts() {
  useSignals();
  return (
    <div className="cd-toasts" aria-live="polite">
      {toasts().map(t => (
        <div key={t.id} className={`cd-toast ${t.level}`}>
          <span className="cd-toast-text">{t.text}</span>
          <button className="mb-fb-icon-btn" title="Dismiss" onClick={() => host.dismissToast(t.id)}><Icon name="x" size={13} /></button>
        </div>
      ))}
    </div>
  );
}
