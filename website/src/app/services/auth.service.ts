import { Injectable, signal, computed } from '@angular/core';
import { ApiService, LoginRequest, RegisterRequest } from './api.service';
import { Observable, tap } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenKey = 'auth_token';
  private readonly usernameKey = 'auth_username';

  username = signal<string | null>(this.getStoredUsername());
  isLoggedIn = computed(() => this.username() !== null);

  constructor(private api: ApiService) {}

  private getStoredUsername(): string | null {
    const token = localStorage.getItem(this.tokenKey);
    if (!token) return null;
    return localStorage.getItem(this.usernameKey);
  }

  login(request: LoginRequest): Observable<unknown> {
    return this.api.login(request).pipe(
      tap(response => {
        if (response.success && response.data) {
          localStorage.setItem(this.tokenKey, response.data.token);
          localStorage.setItem(this.usernameKey, response.data.username);
          this.username.set(response.data.username);
        }
      })
    );
  }

  register(request: RegisterRequest): Observable<unknown> {
    return this.api.register(request).pipe(
      tap(response => {
        if (response.success && response.data) {
          localStorage.setItem(this.tokenKey, response.data.token);
          localStorage.setItem(this.usernameKey, response.data.username);
          this.username.set(response.data.username);
        }
      })
    );
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.usernameKey);
    this.username.set(null);
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }
}
