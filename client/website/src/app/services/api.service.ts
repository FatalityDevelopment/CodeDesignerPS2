import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  message: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  username: string;
}

export interface ServerInfo {
  name: string;
  status: string;
  playerCount: number;
  maxPlayers: number;
  region: string;
  game: string;
}

export interface CheatEntry {
  id: number;
  title: string;
  game: string;
  codes: string;
  author: string;
}

export interface ResearchEntry {
  id: number;
  title: string;
  description: string;
  author: string;
  date: string;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl = 'http://localhost:5100/api';

  constructor(private http: HttpClient) {}

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('auth_token');
    let headers = new HttpHeaders({ 'Content-Type': 'application/json' });
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }
    return headers;
  }

  login(request: LoginRequest): Observable<ApiResponse<AuthResponse>> {
    return this.http.post<ApiResponse<AuthResponse>>(
      `${this.baseUrl}/auth/login`, request, { headers: this.getHeaders() }
    );
  }

  register(request: RegisterRequest): Observable<ApiResponse<AuthResponse>> {
    return this.http.post<ApiResponse<AuthResponse>>(
      `${this.baseUrl}/auth/register`, request, { headers: this.getHeaders() }
    );
  }

  getServers(): Observable<ApiResponse<ServerInfo[]>> {
    return this.http.get<ApiResponse<ServerInfo[]>>(
      `${this.baseUrl}/servers`, { headers: this.getHeaders() }
    );
  }

  getCheats(): Observable<ApiResponse<CheatEntry[]>> {
    return this.http.get<ApiResponse<CheatEntry[]>>(
      `${this.baseUrl}/cheats`, { headers: this.getHeaders() }
    );
  }

  searchCheats(query: string): Observable<ApiResponse<CheatEntry[]>> {
    return this.http.get<ApiResponse<CheatEntry[]>>(
      `${this.baseUrl}/cheats/search?q=${encodeURIComponent(query)}`, { headers: this.getHeaders() }
    );
  }

  getResearch(): Observable<ApiResponse<ResearchEntry[]>> {
    return this.http.get<ApiResponse<ResearchEntry[]>>(
      `${this.baseUrl}/research`, { headers: this.getHeaders() }
    );
  }
}
