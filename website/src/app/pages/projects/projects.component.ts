import { Component } from '@angular/core';

@Component({
  selector: 'app-projects',
  imports: [],
  template: `
    <div class="projects-page">
      <h1 class="page-title">Projects</h1>
      <p class="page-subtitle">Open-source tools for the PlayStation 2</p>

      <div class="projects-grid">
        <div class="project-card">
          <div class="project-image-wrapper">
            <img src="assets/logo.png" alt="CodeDesigner" class="project-image" />
          </div>
          <div class="project-info">
            <h2 class="project-name">CodeDesigner</h2>
            <p class="project-description">
              A complete assembler, disassembler, instruction decoder/encoder, and execution engine
              for the Sony PlayStation 2 Emotion Engine (EE) MIPS64 architecture. Verified against
              PCSX2 reference behaviour.
            </p>
            <div class="project-tags">
              <span class="tag">PS2</span>
              <span class="tag">MIPS</span>
              <span class="tag">Assembler</span>
              <span class="tag">C#</span>
              <span class="tag">Emulator</span>
            </div>
            <div class="project-actions">
              <a href="https://github.com/TylerDev888/CodeDesigner" target="_blank" rel="noopener" class="btn btn-primary">
                <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
                  <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
                </svg>
                View on GitHub
              </a>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .projects-page {
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
    }

    .projects-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(500px, 1fr));
      gap: 24px;
    }

    .project-card {
      background-color: #252526;
      border: 1px solid #3e3e42;
      border-radius: 6px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      transition: border-color 0.15s ease;
    }

    .project-card:hover {
      border-color: #007acc;
    }

    .project-image-wrapper {
      background-color: #1e1e1e;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 32px;
      border-bottom: 1px solid #3e3e42;
    }

    .project-image {
      max-width: 200px;
      max-height: 120px;
      object-fit: contain;
    }

    .project-info {
      padding: 24px;
    }

    .project-name {
      font-size: 22px;
      color: #d4d4d4;
      margin-bottom: 12px;
    }

    .project-description {
      color: #cccccc;
      font-size: 14px;
      line-height: 1.6;
      margin-bottom: 16px;
    }

    .project-tags {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-bottom: 20px;
    }

    .tag {
      background-color: #0e639c;
      color: #ffffff;
      padding: 3px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .project-actions {
      display: flex;
      gap: 12px;
    }

    .btn {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      border-radius: 4px;
      font-size: 13px;
      font-weight: 500;
      text-decoration: none;
      transition: background-color 0.15s ease;
    }

    .btn-primary {
      background-color: #0e639c;
      color: #ffffff;
    }

    .btn-primary:hover {
      background-color: #1177bb;
      color: #ffffff;
    }

    @media (max-width: 600px) {
      .projects-grid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class ProjectsComponent {}
