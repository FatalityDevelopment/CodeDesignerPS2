/**
 * Minimal Markdown → safe HTML converter. Handles only the subset needed for
 * the Welcome page's What's New card: ATX headings, paragraphs, bulleted and
 * numbered lists, bold, italic, inline code, fenced code blocks, and links.
 *
 * Output is sanitised by HTML-escaping every literal segment before any
 * tag is emitted, so user-authored markdown cannot inject markup.
 */
export function renderMarkdown(src: string): string {
    const lines = src.replace(/\r\n?/g, '\n').split('\n');
    const out: string[] = [];

    let i = 0;
    let inFence = false;
    let fenceBuf: string[] = [];

    let listType: 'ul' | 'ol' | null = null;
    let paraBuf: string[] = [];

    const closeList = () => {
        if (listType) { out.push(`</${listType}>`); listType = null; }
    };
    const flushPara = () => {
        if (paraBuf.length === 0) { return; }
        out.push(`<p>${inlineFormat(paraBuf.join(' '))}</p>`);
        paraBuf = [];
    };

    while (i < lines.length) {
        const line = lines[i];

        // Fenced code block (``` … ```).
        if (/^```/.test(line)) {
            if (inFence) {
                out.push(`<pre><code>${escapeHtml(fenceBuf.join('\n'))}</code></pre>`);
                fenceBuf = [];
                inFence = false;
            } else {
                flushPara();
                closeList();
                inFence = true;
            }
            i++;
            continue;
        }
        if (inFence) { fenceBuf.push(line); i++; continue; }

        // Blank line → paragraph break.
        if (line.trim() === '') {
            flushPara();
            closeList();
            i++;
            continue;
        }

        // ATX heading.
        const h = line.match(/^(#{1,6})\s+(.*)$/);
        if (h) {
            flushPara();
            closeList();
            const level = h[1].length;
            out.push(`<h${level}>${inlineFormat(h[2])}</h${level}>`);
            i++;
            continue;
        }

        // Unordered list item.
        const ul = line.match(/^\s*[-*+]\s+(.*)$/);
        if (ul) {
            flushPara();
            if (listType !== 'ul') { closeList(); out.push('<ul>'); listType = 'ul'; }
            out.push(`<li>${inlineFormat(ul[1])}</li>`);
            i++;
            continue;
        }

        // Ordered list item.
        const ol = line.match(/^\s*\d+[.)]\s+(.*)$/);
        if (ol) {
            flushPara();
            if (listType !== 'ol') { closeList(); out.push('<ol>'); listType = 'ol'; }
            out.push(`<li>${inlineFormat(ol[1])}</li>`);
            i++;
            continue;
        }

        // Paragraph text — accumulate until a blank line / block boundary.
        closeList();
        paraBuf.push(line);
        i++;
    }

    if (inFence) {
        // Unterminated fence — render whatever we collected.
        out.push(`<pre><code>${escapeHtml(fenceBuf.join('\n'))}</code></pre>`);
    }
    flushPara();
    closeList();

    return out.join('\n');
}

const TOKEN_SEP = '';

function inlineFormat(text: string): string {
    // Tokenise inline code first so its contents are not re-processed.
    // ASCII unit-separator wrappers guarantee the placeholder cannot collide
    // with any literal text the user might write.
    const tokens: string[] = [];
    let work = text.replace(/`([^`]+)`/g, (_m, code) => {
        const tok = `<code>${escapeHtml(String(code))}</code>`;
        tokens.push(tok);
        return `${TOKEN_SEP}${tokens.length - 1}${TOKEN_SEP}`;
    });

    work = escapeHtml(work);

    // Links: [text](url) — both halves are already escaped above.
    work = work.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_m, label, href) => {
        const safeHref = href.replace(/"/g, '');
        return `<a href="${safeHref}">${label}</a>`;
    });

    // Bold (**…** or __…__).
    work = work.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    work = work.replace(/__([^_]+)__/g, '<strong>$1</strong>');

    // Italic (*…* or _…_).
    work = work.replace(/(^|[^*])\*([^*\n]+)\*(?!\*)/g, '$1<em>$2</em>');
    work = work.replace(/(^|[^_])_([^_\n]+)_(?!_)/g, '$1<em>$2</em>');

    // Re-insert inline code tokens.
    const tokenRe = new RegExp(`${TOKEN_SEP}(\\d+)${TOKEN_SEP}`, 'g');
    work = work.replace(tokenRe, (_m, idx) => tokens[Number(idx)] ?? '');

    return work;
}

function escapeHtml(s: string): string {
    return s
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}
