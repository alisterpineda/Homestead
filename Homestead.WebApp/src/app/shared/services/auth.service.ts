import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface UserInfo {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  readonly initialized = signal(false);
  readonly isAuthenticated = signal(false);
  readonly userName = signal('');
  readonly userEmail = signal('');
  readonly roles = signal<string[]>([]);
  readonly isAdmin = computed(() => this.roles().includes('Admin'));

  async initialize(): Promise<void> {
    try {
      const user = await firstValueFrom(
        this.http.get<UserInfo>('/api/auth/user-info'),
      );
      this.setUser(user);
    } catch {
      // Not authenticated — that's fine
    } finally {
      this.initialized.set(true);
    }
  }

  async login(email: string, password: string): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/auth/login?useCookies=true', { email, password }),
    );
    const user = await firstValueFrom(
      this.http.get<UserInfo>('/api/auth/user-info'),
    );
    this.setUser(user);
  }

  async register(email: string, displayName: string, password: string): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/auth/custom-register', { email, displayName, password }),
    );
    const user = await firstValueFrom(
      this.http.get<UserInfo>('/api/auth/user-info'),
    );
    this.setUser(user);
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    this.isAuthenticated.set(false);
    this.userName.set('');
    this.userEmail.set('');
    this.roles.set([]);
    this.router.navigate(['/login']);
  }

  private setUser(user: UserInfo): void {
    this.isAuthenticated.set(true);
    this.userName.set(user.displayName || user.email);
    this.userEmail.set(user.email);
    this.roles.set(user.roles ?? []);
  }
}
