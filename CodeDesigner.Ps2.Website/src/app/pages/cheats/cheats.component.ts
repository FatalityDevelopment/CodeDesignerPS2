import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-cheats',
  imports: [FormsModule],
  template: `
    <div class="cheats-page">
      <h1 class="page-title">Cheat Database</h1>
      <p class="page-subtitle">Search and browse cheat codes for PlayStation 2 games</p>

      <div class="search-bar">
        <svg class="search-icon" width="16" height="16" viewBox="0 0 16 16" fill="#858585">
          <path d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001c.03.04.062.078.098.115l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85a1.007 1.007 0 0 0-.115-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z"/>
        </svg>
        <input
          type="text"
          class="search-input"
          placeholder="Search games, cheats, codes..."
          [(ngModel)]="searchQuery"
        />
      </div>

      <div class="coming-soon">
        <div class="coming-soon-icon">
          <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="#007acc" stroke-width="1.5">
            <path d="M12 2L2 7l10 5 10-5-10-5z"/>
            <path d="M2 17l10 5 10-5"/>
            <path d="M2 12l10 5 10-5"/>
          </svg>
        </div>
        <h2 class="coming-soon-title">Coming Soon</h2>
        <p class="coming-soon-text">
          The cheat database is currently under development. Soon you'll be able to search,
          browse, and contribute cheat codes for your favorite PS2 games.
        </p>
      </div>

      <div class="cheats-grid">
        <div class="cheat-placeholder">
          <div class="placeholder-line long"></div>
          <div class="placeholder-line medium"></div>
          <div class="placeholder-line short"></div>
        </div>
        <div class="cheat-placeholder">
          <div class="placeholder-line long"></div>
          <div class="placeholder-line medium"></div>
          <div class="placeholder-line short"></div>
        </div>
        <div class="cheat-placeholder">
          <div class="placeholder-line long"></div>
          <div class="placeholder-line medium"></div>
          <div class="placeholder-line short"></div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .cheats-page {
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
      margin-bottom: 24px;
    }

    .search-bar {
      position: relative;
      margin-bottom: 32px;
    }

    .search-icon {
      position: absolute;
      left: 14px;
      top: 50%;
      transform: translateY(-50%);
      pointer-events: none;
    }

    .search-input {
      width: 100%;
      padding: 10px 14px 10px 40px;
      background-color: #3c3c3c;
      border: 1px solid #3e3e42;
      border-radius: 4px;
      color: #d4d4d4;
      font-size: 14px;
      outline: none;
      transition: border-color 0.15s ease;
    }

    .search-input:focus {
      border-color: #007acc;
    }

    .search-input::placeholder {
      color: #858585;
    }

    .coming-soon {
      text-align: center;
      padding: 48px 24px;
      margin-bottom: 32px;
    }

    .coming-soon-icon {
      margin-bottom: 16px;
    }

    .coming-soon-title {
      font-size: 24px;
      color: #007acc;
      margin-bottom: 12px;
    }

    .coming-soon-text {
      color: #858585;
      font-size: 14px;
      max-width: 480px;
      margin: 0 auto;
      line-height: 1.6;
    }

    .cheats-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
      gap: 16px;
    }

    .cheat-placeholder {
      background-color: #252526;
      border: 1px solid #3e3e42;
      border-radius: 6px;
      padding: 20px;
      opacity: 0.4;
    }

    .placeholder-line {
      height: 12px;
      background-color: #3c3c3c;
      border-radius: 3px;
      margin-bottom: 12px;
    }

    .placeholder-line.long { width: 90%; }
    .placeholder-line.medium { width: 65%; }
    .placeholder-line.short { width: 40%; margin-bottom: 0; }
  `]
})
export class CheatsComponent {
  searchQuery = signal('');
}
