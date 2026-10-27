import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="login-page">
      <div class="login-card">
        <h1 class="login-title">Sign In</h1>
        <p class="login-subtitle">Sign in to your CodeDesigner account</p>

        @if (errorMessage()) {
          <div class="error-banner">{{ errorMessage() }}</div>
        }

        <form (ngSubmit)="onSubmit()" class="login-form">
          <div class="form-group">
            <label for="username" class="form-label">Username</label>
            <input
              id="username"
              type="text"
              class="form-input"
              placeholder="Enter your username"
              [(ngModel)]="username"
              name="username"
              required
            />
          </div>

          <div class="form-group">
            <label for="password" class="form-label">Password</label>
            <input
              id="password"
              type="password"
              class="form-input"
              placeholder="Enter your password"
              [(ngModel)]="password"
              name="password"
              required
            />
          </div>

          <button type="submit" class="submit-btn" [disabled]="isLoading()">
            {{ isLoading() ? 'Signing in...' : 'Sign In' }}
          </button>
        </form>

        <p class="login-footer">
          Don't have an account? <a routerLink="/register" class="link">Register</a>
        </p>
      </div>
    </div>
  `,
  styles: [`
    .login-page {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: calc(100vh - 48px);
      padding: 24px;
    }

    .login-card {
      background-color: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: 6px;
      padding: 40px;
      width: 100%;
      max-width: 400px;
    }

    .login-title {
      font-size: 24px;
      color: var(--fg);
      margin-bottom: 4px;
      text-align: center;
    }

    .login-subtitle {
      color: var(--fg-muted);
      font-size: 13px;
      text-align: center;
      margin-bottom: 28px;
    }

    .error-banner {
      background-color: rgba(240, 135, 113, 0.1);
      border: 1px solid var(--error);
      color: var(--error);
      padding: 10px 14px;
      border-radius: 4px;
      font-size: 13px;
      margin-bottom: 20px;
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-label {
      color: var(--fg-secondary);
      font-size: 13px;
      font-weight: 500;
    }

    .form-input {
      padding: 9px 12px;
      background-color: var(--bg-input);
      border: 1px solid var(--border);
      border-radius: 4px;
      color: var(--fg);
      font-size: 14px;
      outline: none;
      transition: border-color 0.15s ease;
    }

    .form-input:focus {
      border-color: var(--accent);
    }

    .form-input::placeholder {
      color: var(--fg-muted);
    }

    .submit-btn {
      margin-top: 8px;
      padding: 10px;
      background-color: var(--btn);
      color: #ffffff;
      border: none;
      border-radius: 4px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: background-color 0.15s ease;
    }

    .submit-btn:hover:not(:disabled) {
      background-color: var(--btn-hover);
    }

    .submit-btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .login-footer {
      text-align: center;
      margin-top: 24px;
      color: var(--fg-muted);
      font-size: 13px;
    }

    .link {
      color: var(--accent);
      text-decoration: none;
    }

    .link:hover {
      color: var(--btn-hover);
    }
  `]
})
export class LoginComponent {
  username = '';
  password = '';
  isLoading = signal(false);
  errorMessage = signal('');

  constructor(private auth: AuthService, private router: Router) {}

  onSubmit(): void {
    if (!this.username || !this.password) {
      this.errorMessage.set('Please fill in all fields.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    this.auth.login({ username: this.username, password: this.password }).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.router.navigate(['/projects']);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('Invalid username or password.');
      }
    });
  }
}
