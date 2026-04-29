import { promises as fs } from 'fs';
import { join, relative, sep } from 'path';
import type { DocFile } from '@shared/types';

const MD_EXT = /\.(md|markdown)$/i;

/** Recursively list every markdown file beneath `root`. */
export async function listMarkdownFiles(root: string): Promise<DocFile[]> {
  const out: DocFile[] = [];
  await walk(root, root, out);
  out.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
  return out;
}

async function walk(root: string, dir: string, out: DocFile[]): Promise<void> {
  let entries;
  try {
    entries = await fs.readdir(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const e of entries) {
    if (e.name.startsWith('.') || e.name === 'node_modules') continue;
    const full = join(dir, e.name);
    if (e.isDirectory()) {
      await walk(root, full, out);
    } else if (e.isFile() && MD_EXT.test(e.name)) {
      const rel = relative(root, full).split(sep).join('/');
      out.push({
        relativePath: rel,
        title: e.name.replace(MD_EXT, '')
      });
    }
  }
}

export async function readMarkdownFile(root: string, relativePath: string): Promise<string> {
  // Guard against directory traversal (`..`).
  const safe = relativePath.replace(/\\/g, '/');
  if (safe.includes('..')) throw new Error('Invalid path');
  const full = join(root, safe);
  return fs.readFile(full, 'utf8');
}
