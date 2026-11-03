import { useEffect, useRef, useState } from 'react';
import { Icon } from './Icon';
import { output, outputChannels } from './output';
import { useSignals } from './signals';

// The bottom panel under every page: one tab per output channel, in the chrome's bottom-panel style.
export function OutputPanel() {
  useSignals();
  const [collapsed, setCollapsed] = useState(true);
  const channels = Object.keys(outputChannels());
  const [channel, setChannel] = useState('CodeDesigner');
  const lines = outputChannels()[channel] ?? [];
  const logRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!collapsed) { logRef.current?.scrollTo({ top: logRef.current.scrollHeight }); }
  }, [lines, collapsed]);

  return (
    <div className={`me-bottom cd-output${collapsed ? ' collapsed' : ''}`}>
      <div className="me-bottom-tabs">
        {channels.map(c => (
          <button key={c} className={c === channel && !collapsed ? 'on' : ''} onClick={() => { setChannel(c); setCollapsed(false); }}>{c}</button>
        ))}
        <span className="me-spacer" />
        <button className="lm-row-btn" title="Clear" onClick={() => output.clear(channel)}><Icon name="trash2" size={13} /></button>
        <button className="lm-row-btn" title={collapsed ? 'Show output' : 'Hide output'} onClick={() => setCollapsed(c => !c)}>
          <Icon name={collapsed ? 'chevronup' : 'chevrondown'} size={13} />
        </button>
      </div>
      {!collapsed && (
        <div className="me-bottom-body">
          <div className="me-log" ref={logRef}>
            {lines.map((l, i) => <div key={i} className={l.error ? 'err' : ''}><span className="lm-muted">{l.time}</span> {l.text}</div>)}
          </div>
        </div>
      )}
    </div>
  );
}
