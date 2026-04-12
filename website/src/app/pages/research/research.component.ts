import { Component } from '@angular/core';

@Component({
  selector: 'app-research',
  imports: [],
  template: `
    <div class="research-page">
      <h1 class="page-title">Research</h1>
      <p class="page-subtitle">A collection of everyone's memories</p>

      <div class="research-grid">
        <div class="research-card">
          <div class="card-icon">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#007acc" stroke-width="1.5">
              <path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/>
              <polyline points="9 17 9 5 21 3 21 15"/>
            </svg>
          </div>
          <h3 class="card-title">Memory Maps</h3>
          <p class="card-description">
            Documented memory layouts for PS2 game executables, including function addresses,
            data structures, and runtime state.
          </p>
          <span class="card-status">In Progress</span>
        </div>

        <div class="research-card">
          <div class="card-icon">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#007acc" stroke-width="1.5">
              <rect x="2" y="3" width="20" height="14" rx="2" ry="2"/>
              <line x1="8" y1="21" x2="16" y2="21"/>
              <line x1="12" y1="17" x2="12" y2="21"/>
            </svg>
          </div>
          <h3 class="card-title">Hardware Documentation</h3>
          <p class="card-description">
            Technical documentation for the PS2 Emotion Engine, GS, VU units, DMA controllers,
            and peripheral interfaces.
          </p>
          <span class="card-status">In Progress</span>
        </div>

        <div class="research-card">
          <div class="card-icon">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#007acc" stroke-width="1.5">
              <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
            </svg>
          </div>
          <h3 class="card-title">Reverse Engineering</h3>
          <p class="card-description">
            Reverse engineering notes and findings from analyzing PS2 game binaries, network
            protocols, and file formats.
          </p>
          <span class="card-status">Coming Soon</span>
        </div>

        <div class="research-card">
          <div class="card-icon">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#007acc" stroke-width="1.5">
              <circle cx="12" cy="12" r="10"/>
              <line x1="2" y1="12" x2="22" y2="12"/>
              <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
            </svg>
          </div>
          <h3 class="card-title">Online Infrastructure</h3>
          <p class="card-description">
            Research into PS2 online game network protocols, DNAS authentication, and server
            infrastructure for reviving online multiplayer.
          </p>
          <span class="card-status">Coming Soon</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .research-page {
      padding: 32px 24px;
      max-width: 1200px;
      margin: 0 auto;
    }

    .page-title {
      font-size: 28px;
      color: #d4d4d4;
      margin-bottom: 4px;
    }

    .page-subtitle {
      color: #858585;
      font-size: 14px;
      margin-bottom: 32px;
      font-style: italic;
    }

    .research-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 20px;
    }

    .research-card {
      background-color: #252526;
      border: 1px solid #3e3e42;
      border-radius: 6px;
      padding: 24px;
      transition: border-color 0.15s ease;
    }

    .research-card:hover {
      border-color: #007acc;
    }

    .card-icon {
      margin-bottom: 16px;
    }

    .card-title {
      font-size: 16px;
      color: #d4d4d4;
      margin-bottom: 10px;
    }

    .card-description {
      color: #cccccc;
      font-size: 13px;
      line-height: 1.6;
      margin-bottom: 16px;
    }

    .card-status {
      display: inline-block;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding: 3px 10px;
      border-radius: 12px;
      background-color: rgba(0, 122, 204, 0.15);
      color: #007acc;
    }
  `]
})
export class ResearchComponent {}
