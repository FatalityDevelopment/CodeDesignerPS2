import { useEffect, useLayoutEffect, useRef, useState } from 'react';

export interface MenuAction {
  label?: string;
  run?: () => void;
  disabled?: boolean;
  danger?: boolean;
  separator?: boolean;
}

// A context menu at the pointer, in the VS Code menu colours; any click or Escape closes it.
export function ContextMenu({ x, y, actions, onClose }: { x: number; y: number; actions: MenuAction[]; onClose: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState({ x, y });
  useLayoutEffect(() => {
    const r = ref.current?.getBoundingClientRect();
    if (r) { setPos({ x: Math.min(x, window.innerWidth - r.width - 4), y: Math.min(y, window.innerHeight - r.height - 4) }); }
  }, [x, y]);
  useEffect(() => {
    const close = (e: Event) => { if (!(e.target instanceof Node && ref.current?.contains(e.target))) { onClose(); } };
    const key = (e: KeyboardEvent) => { if (e.key === 'Escape') { onClose(); } };
    window.addEventListener('pointerdown', close, true);
    window.addEventListener('keydown', key);
    window.addEventListener('blur', onClose);
    return () => { window.removeEventListener('pointerdown', close, true); window.removeEventListener('keydown', key); window.removeEventListener('blur', onClose); };
  }, [onClose]);
  return (
    <div ref={ref} className="cd-ctx" style={{ left: pos.x, top: pos.y }} onContextMenu={e => e.preventDefault()}>
      {actions.map((a, i) => a.separator ? <div key={i} className="cd-ctx-sep" /> : (
        <button key={i} type="button" className={`cd-ctx-item${a.danger ? ' danger' : ''}`} disabled={a.disabled}
          onClick={() => { onClose(); a.run?.(); }}>{a.label}</button>
      ))}
    </div>
  );
}
