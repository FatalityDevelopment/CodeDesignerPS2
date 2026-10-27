import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <nav class="navbar">
      <div class="nav-brand">
        <a routerLink="/" class="brand-link">
          <img src="assets/fatality-logo.png" alt="" class="brand-icon" />
          <span class="brand-text">Fatality Development</span>
        </a>
      </div>
      <div class="nav-links">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" class="nav-link">Home</a>
        <a routerLink="/projects" routerLinkActive="active" class="nav-link">Projects</a>
        <a routerLink="/cheats" routerLinkActive="active" class="nav-link">Cheats</a>
        <a routerLink="/research" routerLinkActive="active" class="nav-link">Research</a>
        <a routerLink="/servers" routerLinkActive="active" class="nav-link">Servers</a>
      </div>
      <div class="nav-auth">
        @if (auth.isLoggedIn()) {
          <span class="username">{{ auth.username() }}</span>
          <button class="logout-btn" (click)="auth.logout()">Logout</button>
        } @else {
          <a routerLink="/login" routerLinkActive="active" class="nav-link">Login</a>
          <a routerLink="/register" routerLinkActive="active" class="nav-link register-link">Register</a>
        }
      </div>
    </nav>
    <main class="main-content">
      <router-outlet />
    </main>
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .navbar {
      display: flex;
      align-items: center;
      background-color: var(--bg-header);
      border-bottom: 1px solid var(--border);
      padding: 0 20px;
      height: 48px;
      flex-shrink: 0;
    }

    .nav-brand {
      margin-right: 32px;
    }

    .brand-link {
      display: flex;
      align-items: center;
      gap: 8px;
      color: var(--fg);
      text-decoration: none;
    }

    .brand-link:hover {
      color: #ffffff;
      text-decoration: none;
    }

    .brand-icon {
      width: 28px;
      height: 28px;
    }

    .brand-text {
      font-size: 16px;
      font-weight: 600;
      letter-spacing: 0.5px;
    }

    .nav-links {
      display: flex;
      gap: 4px;
      flex: 1;
    }

    .nav-link {
      color: var(--fg-secondary);
      text-decoration: none;
      padding: 12px 16px;
      font-size: 13px;
      border-bottom: 2px solid transparent;
      transition: color 0.15s ease, border-color 0.15s ease;
    }

    .nav-link:hover {
      color: #ffffff;
      text-decoration: none;
      background-color: rgba(255, 255, 255, 0.05);
    }

    .nav-link.active {
      color: #ffffff;
      border-bottom-color: var(--accent);
    }

    .nav-auth {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .username {
      color: var(--fg-secondary);
      font-size: 13px;
    }

    .logout-btn {
      background-color: transparent;
      color: var(--fg-secondary);
      border: 1px solid var(--border);
      padding: 4px 12px;
      font-size: 12px;
      border-radius: 3px;
      cursor: pointer;
    }

    .logout-btn:hover {
      background-color: rgba(255, 255, 255, 0.05);
      color: #ffffff;
    }

    .register-link {
      background-color: var(--btn);
      color: #ffffff !important;
      border-radius: 3px;
      padding: 6px 14px !important;
      border-bottom: none !important;
    }

    .register-link:hover {
      background-color: var(--btn-hover) !important;
    }

    .main-content {
      flex: 1;
      overflow-y: auto;
      background-color: transparent;
    }

    @media (max-width: 768px) {
      .navbar {
        flex-wrap: wrap;
        height: auto;
        padding: 8px 12px;
        gap: 8px;
      }

      .nav-brand {
        margin-right: auto;
      }

      .nav-links {
        order: 3;
        flex: 0 0 100%;
        overflow-x: auto;
      }

      .nav-link {
        padding: 8px 12px;
        white-space: nowrap;
      }
    }
  `]
})
export class LayoutComponent {
  constructor(public auth: AuthService) {}
}
