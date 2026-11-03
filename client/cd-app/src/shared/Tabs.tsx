export interface TabItem<T extends string> { id: T; label: string }

// A row of tabs in the chrome's property-panel style.
export function Tabs<T extends string>({ tabs, active, onChange }: { tabs: Array<TabItem<T>>; active: T; onChange: (id: T) => void }) {
  return (
    <div className="me-tabs" role="tablist">
      {tabs.map(t => (
        <button key={t.id} role="tab" aria-selected={t.id === active} className={`me-tab${t.id === active ? ' active' : ''}`} onClick={() => onChange(t.id)}>
          {t.label}
        </button>
      ))}
    </div>
  );
}
