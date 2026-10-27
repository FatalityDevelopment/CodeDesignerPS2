import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, of, tap } from 'rxjs';

export const GITHUB_ORG = 'FatalityDevelopment';
export const ARCHIVE_REPO = 'CheatCodeArchive';

export interface GithubRepo {
  name: string;
  full_name: string;
  html_url: string;
  description: string | null;
  homepage: string | null;
  language: string | null;
  stargazers_count: number;
  forks_count: number;
  open_issues_count: number;
  topics?: string[];
  fork: boolean;
  archived: boolean;
  pushed_at: string;
}

/** One game file in the CheatCodeArchive's Database/ folder. */
export interface ArchiveEntry {
  /** Path inside the repo, e.g. "Database/MEMORY-PS2-CMP/Altered Beast - SLES_530.24.CMP.txt". */
  path: string;
  /** Folder under Database/, e.g. "MEMORY-PS2-CMP". */
  category: string;
  title: string;
  region: string;
  serial: string;
}

/** A parsed cheat file: header fields, then folders of cheats. */
export interface CheatFile {
  name: string;
  gameId: string;
  folders: CheatFolder[];
}

export interface CheatFolder {
  name: string;
  cheats: Cheat[];
}

export interface Cheat {
  name: string;
  /** Text from a trailing "{...}" in the name line, tags stripped. */
  note: string;
  credits: string[];
  codes: string[];
}

interface GithubTree {
  truncated: boolean;
  tree: { path: string; type: string }[];
}

/** Unauthenticated GitHub API calls are limited to 60 an hour per visitor, so cache them. */
const CACHE_TTL_MS = 60 * 60 * 1000;

/**
 * Reads public data straight from GitHub (the API and raw.githubusercontent.com both allow
 * cross-origin requests), so these pages need no backend.
 */
@Injectable({ providedIn: 'root' })
export class GithubService {
  constructor(private http: HttpClient) {}

  /** Public repositories of the organisation, most recently pushed first. */
  orgRepos(): Observable<GithubRepo[]> {
    const url = `https://api.github.com/orgs/${GITHUB_ORG}/repos?type=public&per_page=100&sort=pushed`;
    return this.cached('cd-org-repos', this.http.get<GithubRepo[]>(url).pipe(
      map(repos => repos
        .filter(r => r.name !== '.github')
        .sort((a, b) => b.pushed_at.localeCompare(a.pushed_at)))));
  }

  /** Every game file in the archive, from one recursive tree request. */
  archiveIndex(): Observable<ArchiveEntry[]> {
    const url = `https://api.github.com/repos/${GITHUB_ORG}/${ARCHIVE_REPO}/git/trees/HEAD?recursive=1`;
    return this.cached('cd-archive-index', this.http.get<GithubTree>(url).pipe(
      map(tree => tree.tree
        .filter(t => t.type === 'blob' && /^Database\/[^/]+\/[^/]+\.txt$/i.test(t.path))
        .map(t => parseArchivePath(t.path))
        .sort((a, b) => a.title.localeCompare(b.title, undefined, { sensitivity: 'base' })))));
  }

  /** Downloads and parses one game's cheat file. */
  cheatFile(path: string): Observable<CheatFile> {
    const url = `https://raw.githubusercontent.com/${GITHUB_ORG}/${ARCHIVE_REPO}/HEAD/${path.split('/').map(encodeURIComponent).join('/')}`;
    return this.http.get(url, { responseType: 'text' }).pipe(map(parseCheatFile));
  }

  archiveFileUrl(path: string): string {
    return `https://github.com/${GITHUB_ORG}/${ARCHIVE_REPO}/blob/HEAD/${path.split('/').map(encodeURIComponent).join('/')}`;
  }

  private cached<T>(key: string, source: Observable<T>): Observable<T> {
    try {
      const hit = JSON.parse(sessionStorage.getItem(key) ?? 'null');
      if (hit && Date.now() - hit.at < CACHE_TTL_MS) return of(hit.value as T);
    } catch { /* storage unavailable or corrupt: fetch */ }
    return source.pipe(tap(value => {
      try { sessionStorage.setItem(key, JSON.stringify({ at: Date.now(), value })); } catch { /* quota/private mode */ }
    }));
  }
}

/**
 * File names look like "007 - Agent Under Fire (Ntsc-U) - SLUS_202.65.CMP.txt",
 * "Altered Beast - SLES_530.24.CMP.txt" or "007_ Nightfire.CMP.txt" (no serial).
 */
export function parseArchivePath(path: string): ArchiveEntry {
  const [, category, file] = path.split('/');
  // The archive writes '//', ':' and apostrophes as underscores (".hack__Infection", "007_ Nightfire", "That_s So Raven").
  let base = file.replace(/(\.CMP)?\.txt$/i, '')
    .replace(/__/g, '//')
    .replace(/_ /g, ': ')
    .replace(/(\w)_(s|t|ll|re|ve|d|m)\b/gi, "$1'$2");
  let serial = '';
  const serialMatch = base.match(/^(.*) - ([A-Z]{4}[-_ ]?\d[\d.]*\d(?:_MERGED)?)$/i);
  if (serialMatch) {
    base = serialMatch[1];
    serial = serialMatch[2].replace(/_MERGED$/i, '');
  }
  let region = '';
  const regionMatch = base.match(/^(.*?)\s*\(([^)]*)\)\s*$/);
  if (regionMatch) {
    base = regionMatch[1];
    region = regionMatch[2];
  }
  return { path, category, title: base.trim(), region, serial };
}

/**
 * Archive format: "^n = Key: value" header lines, "!Folder" opens a folder and "!!" closes it,
 * "+Name" starts a cheat, "%Credits: ..." and "$XXXXXXXX YYYYYYYY" lines belong to the last cheat.
 */
export function parseCheatFile(text: string): CheatFile {
  const file: CheatFile = { name: '', gameId: '', folders: [] };
  let folder: CheatFolder | null = null;
  let cheat: Cheat | null = null;

  const currentFolder = (): CheatFolder => {
    if (!folder) {
      folder = { name: '', cheats: [] };
      file.folders.push(folder);
    }
    return folder;
  };

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    const header = line.match(/^\^\d+\s*=\s*([^:]+):\s*(.*)$/);
    if (header) {
      const key = header[1].trim().toLowerCase();
      if (key === 'name') file.name = header[2].trim();
      else if (key === 'gameid') file.gameId = header[2].trim();
    } else if (line === '!!') {
      folder = null;
      cheat = null;
    } else if (line.startsWith('!')) {
      folder = { name: line.slice(1).replace(/:$/, '').trim(), cheats: [] };
      file.folders.push(folder);
      cheat = null;
    } else if (line.startsWith('+')) {
      cheat = { ...splitNote(line.slice(1)), credits: [], codes: [] };
      currentFolder().cheats.push(cheat);
    } else if (line.startsWith('%')) {
      cheat?.credits.push(stripTags(line.slice(1).replace(/^Credits:\s*/i, '')));
    } else if (line.startsWith('$')) {
      cheat?.codes.push(line.slice(1).trim());
    }
  }

  file.folders = file.folders.filter(f => f.cheats.length > 0);
  return file;
}

/** "Name {<I>note</I>}" -> name + note. */
function splitNote(text: string): { name: string; note: string } {
  const m = text.match(/^(.*?)\s*\{([\s\S]*)\}\s*$/);
  return m ? { name: stripTags(m[1]), note: stripTags(m[2]) } : { name: stripTags(text), note: '' };
}

/** Archive text carries HTML-ish markup (<I>, <br>, <li>...). Show it as plain text, never as HTML. */
function stripTags(text: string): string {
  return text
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<li>/gi, '\n• ')
    .replace(/<[^>]*>/g, '')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/[ \t]+/g, ' ')
    .replace(/\n\s*\n+/g, '\n')
    .trim();
}
