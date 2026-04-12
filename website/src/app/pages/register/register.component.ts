import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="register-page">
      <div class="register-card">
        <h1 class="register-title">Create Account</h1>
        <p class="register-subtitle">Register for a CodeDesigner account</p>

        @if (errorMessage()) {
          <div class="error-banner">{{ errorMessage() }}</div>
        }

        <form (ngSubmit)="onSubmit()" class="register-form">
          <div class="form-group">
            <label for="username" class="form-label">Username</label>
            <input
              id="username"
              type="text"
              class="form-input"
              placeholder="Choose a username"
              [(ngModel)]="username"
              name="username"
              required
            />
          </div>

          <div class="form-group">
            <label for="email" class="form-label">Email</label>
            <input
              id="email"
              type="email"
              class="form-input"
              placeholder="Enter your email"
              [(ngModel)]="email"
              name="email"
              required
            />
          </div>

          <div class="form-group">
            <label for="password" class="form-label">Password</label>
            <input
              id="password"
              type="password"
              class="form-input"
              placeholder="Create a password"
              [(ngModel)]="password"
              name="password"
              required
            />
          </div>

          <div class="form-group">
            <label for="confirmPassword" class="form-label">Confirm Password</label>
            <input
              id="confirmPassword"
              type="password"
              class="form-input"
              placeholder="Confirm your password"
              [(ngModel)]="confirmPassword"
              name="confirmPassword"
              required
            />
          </div>

          <button type="submit" class="submit-btn" [disabled]="isLoading()">
            {{ isLoading() ? 'Creating account...' : 'Create Account' }}
          </button>
        </form>

        <p class="register-footer">
          Already have an account? <a routerLink="/login" class="link">Sign In</a>
        </p>
      </div>
    </div>
  `,
  styles: [`
    .register-page {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: calc(100vh - 48px);
      padding: 24px;
    }

    .register-card {
      background-color: #252526;
      border: 1px solid #3e3e42;
      border-radius: 6px;
      padding: 40px;
      width: 100%;
      max-width: 400px;
    }

    .register-title {
      font-size: 24px;
      color: #d4d4d4;
      margin-bottom: 4px;
      text-align: center;
    }

    .register-subtitle {
      color: #858585;
      font-size: 13px;
      text-align: center;
      margin-bottom: 28px;
    }

    .error-banner {
      background-color: rgba(240, 135, 113, 0.1);
      border: 1px solid #f48771;
      color: #f48771;
      padding: 10px 14px;
      border-radius: 4px;
      font-size: 13px;
      margin-bottom: 20px;
    }

    .register-form {
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
      color: #cccccc;
      font-size: 13px;
      font-weight: 500;
    }

    .form-input {
      padding: 9px 12px;
      background-color: #3c3c3c;
      border: 1px solid #3e3e42;
      border-radius: 4px;
      color: #d4d4d4;
      font-size: 14px;
      outline: none;
      transition: border-color 0.15s ease;
    }

    .form-input:focus {
      border-color: #007acc;
    }

    .form-input::placeholder {
      color: #858585;
    }

    .submit-btn {
      margin-top: 8px;
      padding: 10px;
      background-color: #0e639c;
      color: #ffffff;
      border: none;
      border-radius: 4px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: background-color 0.15s ease;
    }

    .submit-btn:hover:not(:disabled) {
      background-color: #1177bb;
    }

    .submit-btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .register-footer {
      text-align: center;
      margin-top: 24px;
      color: #858585;
      font-size: 13px;
    }

    .link {
      color: #007acc;
      text-decoration: none;
    }

    .link:hover {
      color: #1177bb;
    }
  `]
})
export class RegisterComponent {
  username = '';
  email = '';
  password = '';
  confirmPassword = '';
  isLoading = signal(false);
  errorMessage = signal('');

  constructor(private auth: AuthService, private router: Router) {}

  onSubmit(): void {
    if (!this.username || !this.email || !this.password || !this.confirmPassword) {
      this.errorMessage.set('Please fill in all fields.');
      return;
    }

    if (this.password !== this.confirmPassword) {
      this.errorMessage.set('Passwords do not match.');
      return;
    }

    if (this.password.length < 6) {
      this.errorMessage.set('Password must be at least 6 characters.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    this.auth.register({
      username: this.username,
      email: this.email,
      password: this.password
    }).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.router.navigate(['/projects']);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('Registration failed. Please try again.');
      }
    });
  }
}
