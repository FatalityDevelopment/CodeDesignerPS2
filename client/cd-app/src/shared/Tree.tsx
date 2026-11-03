import { useState, type ReactNode } from 'react';
import { Icon } from './Icon';

export interface TreeNode {
  id: string;
  label: ReactNode;
  meta?: ReactNode;
  children?: TreeNode[];
}

interface TreeProps {
  nodes: TreeNode[];
  selected?: string | null;
  onSelect?: (node: TreeNode) => void;
  onContextMenu?: (node: TreeNode, x: number, y: number) => void;
}

// A collapsible tree in the chrome's outliner style. Branches start collapsed.
export function Tree(props: TreeProps) {
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());
  const toggle = (id: string) => setOpen(s => {
    const next = new Set(s);
    if (!next.delete(id)) { next.add(id); }
    return next;
  });
  const row = (node: TreeNode, depth: number): ReactNode => {
    const branch = !!node.children?.length;
    const expanded = open.has(node.id);
    return (
      <div key={node.id}>
        <div
          className={`me-tree-row${props.selected === node.id ? ' sel' : ''}`}
          style={{ paddingLeft: 4 + depth * 12 }}
          onClick={() => { if (branch) { toggle(node.id); } props.onSelect?.(node); }}
          onContextMenu={e => { if (props.onContextMenu) { e.preventDefault(); props.onContextMenu(node, e.clientX, e.clientY); } }}
        >
          <span className="cd-tree-chevron">{branch && <Icon name={expanded ? 'chevrondown' : 'chevronright'} size={12} />}</span>
          <span>{node.label}</span>
          {node.meta !== undefined && <span className="lm-muted">{node.meta}</span>}
        </div>
        {branch && expanded && node.children!.map(child => row(child, depth + 1))}
      </div>
    );
  };
  return <div className="me-tree">{props.nodes.map(n => row(n, 0))}</div>;
}
