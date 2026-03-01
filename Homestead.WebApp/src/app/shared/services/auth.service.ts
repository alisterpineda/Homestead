import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { GetStatusResponse } from '@/api/generated/models/get-status-response';
import { GetUserInfoResponse } from '@/api/generated/models/get-user-info-response';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  readonly initialized = signal(false);
  readonly needsSetup = signal(false);
  readonly isAuthenticated = signal(false);
  readonly userName = signal('');
  readonly userEmail = signal('');
  readonly roles = signal<string[]>([]);
  readonly isAdmin = computed(() => this.roles().includes('Admin'));

  async initialize(): Promise<void> {
    try {
      const [statusResult, userResult] = await Promise.allSettled([
        firstValueFrom(this.http.get<GetStatusResponse>('/api/status')),
        firstValueFrom(this.http.get<GetUserInfoResponse>('/api/auth/user-info')),
      ]);

      if (statusResult.status === 'fulfilled') {
        this.needsSetup.set(statusResult.value.needsSetup);
      }

      if (!this.needsSetup() && userResult.status === 'fulfilled') {
        this.setUser(userResult.value);
      }
    } catch {
      // Initialization failed — assume defaults
    } finally {
      this.initialized.set(true);
    }
  }

  async login(email: string, password: string): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/auth/login?useCookies=true', { email, password }),
    );
    await this.fetchAndSetUser();
  }

  async setup(email: string, displayName: string, password: string): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/auth/register', { email, displayName, password }),
    );
    await this.fetchAndSetUser();
    this.needsSetup.set(false);
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    this.isAuthenticated.set(false);
    this.userName.set('');
    this.userEmail.set('');
    this.roles.set([]);
    this.router.navigate(['/login']);
  }

  private async fetchAndSetUser(): Promise<void> {
    const user = await firstValueFrom(
      this.http.get<GetUserInfoResponse>('/api/auth/user-info'),
    );
    this.setUser(user);
  }

  private setUser(user: GetUserInfoResponse): void {
    this.isAuthenticated.set(true);
    this.userName.set(user.displayName || user.email);
    this.userEmail.set(user.email);
    this.roles.set(user.roles ?? []);
  }
}
