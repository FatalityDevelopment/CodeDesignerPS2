import * as https from 'https';
import * as fs from 'fs';
import type { IncomingMessage } from 'http';

/**
 * Vimm's Lair ROM lookup / download helper.
 *
 * Vimm's Lair has no public API. A PS2 game is located by searching the vault
 * by CRC, the matching vault page is parsed for its media id, and the file is
 * streamed from the download endpoint — which only answers when the request
 * carries a browser User-Agent and a vimm.net Referer.
 */

/** Browser-like User-Agent — Vimm's Lair rejects unknown clients. */
const USER_AGENT =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 ' +
  '(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36';

const MAX_REDIRECTS = 5;

/** Everything needed to fetch a ROM from Vimm's Lair. */
export interface VimmDownload {
  /** Vimm vault page numeric id. */
  vaultId: string;
  /** Full vault page URL (also sent as the download Referer). */
  vaultUrl: string;
  /** Vimm media id submitted to the download endpoint. */
  mediaId: string;
  /** Direct ROM download URL on dl2.vimm.net. */
  downloadUrl: string;
  /** File name advertised by the download endpoint. */
  fileName: string;
  /** Total size in bytes (0 when the endpoint omits Content-Length). */
  sizeBytes: number;
}

export interface DownloadProgress {
  receivedBytes: number;
  totalBytes: number;
}

/** Coerces a possibly-array HTTP header to a single string. */
function headerValue(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** Issues a GET and resolves with the response stream once redirects are followed. */
function httpGet(
  url: string,
  referer: string | undefined,
  redirectsLeft: number,
): Promise<IncomingMessage> {
  return new Promise((resolve, reject) => {
    const headers: Record<string, string> = {
      'User-Agent': USER_AGENT,
      'Accept-Encoding': 'identity',
    };
    if (referer) { headers['Referer'] = referer; }

    const req = https.get(url, { headers }, (res) => {
      const status = res.statusCode ?? 0;
      const location = headerValue(res.headers.location);

      if (status >= 300 && status < 400 && location) {
        res.resume(); // drain the redirect body
        if (redirectsLeft <= 0) {
          reject(new Error(`Too many redirects fetching ${url}`));
          return;
        }
        httpGet(new URL(location, url).toString(), referer, redirectsLeft - 1)
          .then(resolve, reject);
        return;
      }

      if (status !== 200) {
        res.resume();
        reject(new Error(`Vimm's Lair returned HTTP ${status} for ${url}`));
        return;
      }
      resolve(res);
    });
    req.on('error', reject);
  });
}

/** Fetches a URL and buffers the full body as a UTF-8 string. */
async function fetchText(url: string, referer?: string): Promise<string> {
  const res = await httpGet(url, referer, MAX_REDIRECTS);
  const chunks: Buffer[] = [];
  return new Promise<string>((resolve, reject) => {
    res.on('data', (c: Buffer) => chunks.push(c));
    res.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    res.on('error', reject);
  });
}

/** Extracts the file name from a Content-Disposition header. */
function parseFileName(disposition: string | undefined, fallback: string): string {
  if (!disposition) { return fallback; }
  const star = disposition.match(/filename\*=(?:UTF-8'')?([^;]+)/i);
  if (star) {
    try { return decodeURIComponent(star[1].trim().replace(/^"|"$/g, '')); }
    catch { /* fall through to the plain form */ }
  }
  const plain = disposition.match(/filename="?([^";]+)"?/i);
  return plain ? plain[1].trim() : fallback;
}

/**
 * Resolves a PS2 game CRC to a downloadable Vimm's Lair ROM.
 *
 * Searches the vault by CRC, opens the matching game page, extracts the media
 * id, then probes the download endpoint for the real file name and size.
 */
export async function resolveVimmDownload(crc: string): Promise<VimmDownload> {
  const cleanCrc = crc.trim();
  if (!cleanCrc) { throw new Error('No CRC available for this game.'); }

  const listUrl =
    `https://vimm.net/vault/?p=list&system=PS2&q=${encodeURIComponent(cleanCrc)}`;
  const listHtml = await fetchText(listUrl);

  // Game results link to /vault/<digits>"; requiring the closing quote
  // excludes system-nav links such as /vault/3DS and /vault/32X.
  const vaultMatch = listHtml.match(/\/vault\/(\d+)"/);
  if (!vaultMatch) {
    throw new Error(`No PS2 game on Vimm's Lair matches CRC ${cleanCrc}.`);
  }
  const vaultId = vaultMatch[1];
  const vaultUrl = `https://vimm.net/vault/${vaultId}`;

  const vaultHtml = await fetchText(vaultUrl, listUrl);
  const mediaMatch = vaultHtml.match(/name="mediaId"\s+value="(\d+)"/i);
  if (!mediaMatch) {
    throw new Error(`The Vimm's Lair page for this game has no downloadable ROM.`);
  }
  const mediaId = mediaMatch[1];
  const downloadUrl = `https://dl2.vimm.net/?mediaId=${mediaId}`;

  // Probe the download endpoint (it needs the vault page as Referer) to learn
  // the file name and size, then abort before the multi-GB body transfers.
  const probe = await httpGet(downloadUrl, vaultUrl, MAX_REDIRECTS);
  const fileName = parseFileName(
    headerValue(probe.headers['content-disposition']),
    `vimm-${mediaId}.7z`);
  const sizeBytes = Number(headerValue(probe.headers['content-length']) ?? 0) || 0;
  probe.on('error', () => { /* ignore — we are aborting on purpose */ });
  probe.destroy();

  return { vaultId, vaultUrl, mediaId, downloadUrl, fileName, sizeBytes };
}

/**
 * Streams a resolved Vimm ROM to <destPath>, reporting progress.
 * Honours cancellation: aborts the transfer and removes the partial file.
 */
export async function downloadVimmRom(
  dl: VimmDownload,
  destPath: string,
  onProgress: (p: DownloadProgress) => void,
  isCancelled: () => boolean,
): Promise<void> {
  const res = await httpGet(dl.downloadUrl, dl.vaultUrl, MAX_REDIRECTS);
  const total = Number(headerValue(res.headers['content-length']) ?? dl.sizeBytes) || 0;

  await new Promise<void>((resolve, reject) => {
    const file = fs.createWriteStream(destPath);
    let received = 0;
    let settled = false;

    const fail = (err: Error) => {
      if (settled) { return; }
      settled = true;
      res.destroy();
      file.destroy();
      fs.promises.rm(destPath, { force: true }).finally(() => reject(err));
    };

    res.on('data', (chunk: Buffer) => {
      if (settled) { return; }
      if (isCancelled()) { fail(new Error('Download cancelled.')); return; }
      received += chunk.length;
      onProgress({ receivedBytes: received, totalBytes: total });
    });
    res.on('error', (err: Error) => fail(err));
    file.on('error', (err: Error) => fail(err));
    file.on('finish', () => {
      if (settled) { return; }
      settled = true;
      resolve();
    });

    res.pipe(file);
  });
}
