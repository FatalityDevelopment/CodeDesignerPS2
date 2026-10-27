import { Component } from '@angular/core';

const REPO = 'https://github.com/FatalityDevelopment/CodeDesignerPS2';

interface Feature { title: string; text: string; }
interface Shot { file: string; caption: string; }

@Component({
  selector: 'app-home',
  imports: [],
  template: `
    <div class="container home">
      <!-- ── Hero (mirrors the extension's welcome page) ─────────── -->
      <section class="hero">
        <div class="hero-left">
          <div class="hero-head">
            <img src="assets/codedesigner-logo-512.png" alt="" class="hero-logo" />
            <div>
              <h1>CodeDesigner</h1>
              <p class="cd-subtitle">PlayStation 2 debugger &amp; toolkit for VS Code</p>
            </div>
          </div>
          <p class="hero-blurb">
            Assemble and disassemble the Emotion Engine (MIPS R5900), debug games live in CodeDesigner's
            PCSX2 fork or the built-in emulator, build and port cheats, edit game assets and run online
            servers. Its MCP server gives AI assistants the same tools, so you and an assistant work in
            one reverse-engineering workflow.
          </p>
          <div class="hero-actions">
            <a class="cd-btn cd-btn-primary" [href]="installerUrl">
              <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                <path d="M8 11.5 3.5 7l1.06-1.06 2.69 2.69V1h1.5v7.63l2.69-2.69L12.5 7 8 11.5zM2 13.5h12V15H2v-1.5z"/>
              </svg>
              Download installer
            </a>
            <a class="cd-btn" [href]="repo" target="_blank" rel="noopener">
              <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
              </svg>
              GitHub
            </a>
            <a class="cd-btn" [href]="repo + '/blob/master/docs/README.md'" target="_blank" rel="noopener">Documentation</a>
          </div>
        </div>

        <div class="cd-card get-started">
          <div class="cd-card-header">Get started</div>
          <ol class="cd-card-body steps">
            <li>Run <code>CodeDesignerInstaller.exe</code> and choose <strong>Install CodeDesigner</strong>, or <strong>Developer setup</strong> to build from source.</li>
            <li>Open VS Code and the <strong>CodeDesigner</strong> sidebar.</li>
            <li>Pick PCSX2 or the built-in emulator, your BIOS and a game, then click <strong>Run</strong>.</li>
          </ol>
          <p class="cd-card-body cd-muted small">Windows 10/11, 64-bit. The installer adds anything missing (.NET, VS Code, SQL Server LocalDB).</p>
        </div>
      </section>

      <!-- ── What it does ─────────────────────────────────────────── -->
      <section class="cd-card">
        <div class="cd-card-header">What it does</div>
        <div class="cd-card-body features">
          @for (f of features; track f.title) {
            <div class="feature">
              <h4>{{ f.title }}</h4>
              <p class="cd-muted">{{ f.text }}</p>
            </div>
          }
        </div>
      </section>

      <!-- ── Screenshots ──────────────────────────────────────────── -->
      <section class="cd-card">
        <div class="cd-card-header">Screenshots</div>
        <div class="cd-card-body gallery">
          @for (s of shots; track s.file) {
            <a class="shot" [href]="'assets/screenshots/' + s.file" target="_blank" rel="noopener">
              <img [src]="'assets/screenshots/' + s.file" [alt]="s.caption" loading="lazy" />
              <span>{{ s.caption }}</span>
            </a>
          }
        </div>
      </section>
    </div>
  `,
  styles: [`
    .home {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .hero {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 340px;
      gap: 20px;
      align-items: stretch;
      padding: 12px 0 8px;
    }

    @media (max-width: 880px) {
      .hero { grid-template-columns: 1fr; }
    }

    .hero-left {
      display: flex;
      flex-direction: column;
      gap: 14px;
      min-width: 0;
    }

    .hero-head {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .hero-logo {
      width: 72px;
      height: 72px;
      flex-shrink: 0;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.022);
      border: 1px solid var(--border);
      padding: 4px;
      object-fit: contain;
    }

    .hero-head h1 { font-size: 28px; line-height: 1.1; }
    .hero-head .cd-subtitle { margin-top: 4px; }

    .hero-blurb {
      max-width: 640px;
      color: var(--fg-muted);
      font-size: 13px;
    }

    .hero-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .steps {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding-left: 30px;
      font-size: 12.5px;
    }

    .steps code {
      color: #9cdcfe;
      font-size: 12px;
    }

    .small { font-size: 11.5px; }

    .features {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 10px;
    }

    @media (max-width: 880px) {
      .features { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }

    @media (max-width: 560px) {
      .features { grid-template-columns: 1fr; }
    }

    .feature {
      border: 1px solid var(--border);
      border-radius: 4px;
      padding: 10px 12px;
      background: var(--bg-card);
    }

    .feature h4 { margin-bottom: 3px; }
    .feature p { font-size: 12px; }

    .gallery {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 12px;
    }

    .shot {
      display: flex;
      flex-direction: column;
      gap: 6px;
      color: var(--fg-muted);
      font-size: 12px;
      border: 1px solid var(--border);
      border-radius: 4px;
      padding: 6px;
      background: var(--bg-card);
      transition: border-color 0.1s, background 0.1s;
    }

    .shot:hover {
      border-color: var(--border-hover);
      background: var(--bg-card-hover);
      color: var(--fg);
      text-decoration: none;
    }

    .shot img {
      width: 100%;
      aspect-ratio: 16 / 10;
      object-fit: cover;
      object-position: top;
      border-radius: 3px;
      background: #111;
    }
  `]
})
export class HomeComponent {
  readonly repo = REPO;
  readonly installerUrl = `${REPO}/releases/latest/download/CodeDesignerInstaller.exe`;

  readonly features: Feature[] = [
    { title: 'Disassembler', text: 'Live views of every memory region with function and table detection, cross-references, breakpoints, watches and registers. Full R5900 ISA including MMI, FPU and VU.' },
    { title: 'Emulators', text: 'Debug commercial games in the PCSX2 fork over PINE (breakpoints, stepping, symbols, save states, pad, picture, sound) or in the built-in emulator.' },
    { title: 'CDS language', text: 'A patch scripting language that compiles to MIPS, GameShark codes or a frame-loop cheat table.' },
    { title: 'Cheats', text: 'Cheat lists and the CheatCodeArchive, format conversion, a porter between game versions and a joker code builder.' },
    { title: 'Memory tools', text: 'Memory Diff, save states, scanners, labels, pointer structs and structure diagrams.' },
    { title: 'Assets', text: 'Viewer plus model, map and animation editors for SOCOM II, with SOCOM: Combined Assault read-only.' },
    { title: 'Network', text: 'DEV9 packet capture with every Medius, DME and SOCOM II message labelled, plus packet building and injection.' },
    { title: 'Online', text: 'SOCOM II online servers (DNAS, Medius, DME, NAT, patch download), verified on real hardware.' },
    { title: 'AI assistants', text: 'An MCP server exposes the toolset to AI clients, with profiles that pick which tools a client sees.' },
  ];

  readonly shots: Shot[] = [
    { file: 'workspace.png', caption: 'The integrated reverse-engineering workspace' },
    { file: 'debugger.png', caption: 'Disassembly and debugging tools inspecting game memory' },
    { file: 'live-labels.png', caption: 'Live memory labels and address annotations' },
    { file: 'label-lists.png', caption: 'Label lists and mapped memory addresses' },
    { file: 'cheat-development.png', caption: 'Cheat development: code and source workflows' },
    { file: 'cheat-list-manager.png', caption: 'Cheat List Manager for the cheat archive' },
    { file: 'cheat-scanner.png', caption: 'Memory scanner for values in a running game' },
    { file: 'cheat-converter.png', caption: 'Cheat converter between code formats' },
    { file: 'cheat-porter.png', caption: 'Cheat Porter between game versions' },
    { file: 'memory-diff.png', caption: 'Memory Diff comparing snapshots' },
    { file: 'memory-diff-results.png', caption: 'Memory Diff results for save states' },
    { file: 'memory-compare.png', caption: 'Memory comparison view' },
    { file: 'packet-debugger.png', caption: 'Packet debugger: capture, dissect and inject' },
    { file: 'socom2-servers.png', caption: 'SOCOM II online server tools' },
    { file: 'socom2-diagnostics.png', caption: 'SOCOM II multiplayer diagnostics' },
    { file: 'memory-card.png', caption: 'Memory card tools' },
    { file: 'hdd.png', caption: 'HDD image tools' },
    { file: 'game-library.png', caption: 'Game library and compatibility' },
    { file: 'ai-control-panel.png', caption: 'AI control panel: memory, skills, profiles' },
    { file: 'ai-workflow.png', caption: 'AI-assisted workflow in the workspace' },
  ];
}
