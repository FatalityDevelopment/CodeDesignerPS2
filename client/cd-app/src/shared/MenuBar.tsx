import { useEffect, useRef, useState, type ReactNode } from 'react';

export interface MenuItem {
  label?: string;
  shortcut?: string;
  action?: () => void;
  disabled?: () => boolean;
  checked?: () => boolean;
  separator?: boolean;
}

export interface Menu { id: string; label: string; items: MenuItem[] }

// The editor menu bar: drop-down menus on the left, whatever the page passes as children on the right.
export function MenuBar({ brand, menus, children }: { brand?: string; menus: Menu[]; children?: ReactNode }) {
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!openMenu) { return; }
    const close = (e: Event) => { if (!(e.target instanceof Node && ref.current?.contains(e.target))) { setOpenMenu(null); } };
    window.addEventListener('pointerdown', close, true);
    return () => window.removeEventListener('pointerdown', close, true);
  }, [openMenu]);

  return (
    <div className="me-menubar" ref={ref}>
      {brand && (
        <span className="me-brand">
          <img src="/codedesigner-icon.png" alt="" width={14} height={14} /> {brand}
        </span>
      )}
      {menus.map(menu => (
        <div key={menu.id} className={`me-menu${openMenu === menu.id ? ' open' : ''}`}>
          <button
            className="me-menu-btn"
            onClick={() => setOpenMenu(openMenu === menu.id ? null : menu.id)}
            onMouseEnter={() => { if (openMenu) { setOpenMenu(menu.id); } }}
          >
            {menu.label}
          </button>
          {openMenu === menu.id && (
            <div className="me-menu-pop">
              {menu.items.map((item, i) => item.separator ? (
                <div key={i} className="me-menu-sep" />
              ) : (
                <button
                  key={i}
                  className="me-menu-item"
                  disabled={item.disabled?.()}
                  onClick={() => { setOpenMenu(null); item.action?.(); }}
                >
                  <span className="me-menu-check">{item.checked?.() ? '✓' : ''}</span>
                  <span className="me-menu-label">{item.label}</span>
                  <span className="me-menu-key">{item.shortcut ?? ''}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      ))}
      <span className="me-spacer" />
      {children}
    </div>
  );
}
