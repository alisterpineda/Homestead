import { Injectable, inject, signal, computed } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IdentityApiService } from '@/api/services/identity-api.service';
import { GetUserInfoResponse } from '@/api/generated/models/get-user-info-response';
import type { AppRole } from '@/api/generated/models/app-role';
import { isSafeReturnPath } from '../utils/safe-return-path';

/** Role hierarchy levels — higher number = more access. */
const ROLE_LEVEL: Record<string, number> = { Admin: 3, Contributor: 2, Viewer: 1, Guest: 0 };

@Injectable({ providedIn: 'root' })
export class AuthService {
  private identityApi = inject(IdentityApiService);

  readonly initialized = signal(false);
  readonly isAuthenticated = signal(false);
  readonly isActive = signal(true);
  readonly userName = signal('');
  readonly userEmail = signal('');
  readonly role = signal<AppRole | ''>('');
  readonly oidcDisplayName = signal('SSO');
  readonly isAdmin = computed(() => this.role() === 'Admin');
  readonly canWrite = computed(() => this.roleLevel() >= ROLE_LEVEL['Contributor']);
  readonly canRead = computed(() => this.roleLevel() >= ROLE_LEVEL['Viewer']);

  private roleLevel = computed(() => ROLE_LEVEL[this.role()] ?? -1);

  /** Whether the user's role meets or exceeds the required role. */
  hasRole(required: string): boolean {
    return (ROLE_LEVEL[this.role()] ?? -1) >= (ROLE_LEVEL[required] ?? Infinity);
  }

  async initialize(): Promise<void> {
    try {
      const [statusResult, userResult] = await Promise.allSettled([
        firstValueFrom(this.identityApi.getStatus()),
        firstValueFrom(this.identityApi.getUserInfo()),
      ]);

      if (statusResult.status === 'fulfilled') {
        this.oidcDisplayName.set(statusResult.value.displayName);
      }

      if (userResult.status === 'fulfilled') {
        this.setUser(userResult.value);
      }
    } catch {
      // Initialization failed — assume defaults
    } finally {
      this.initialized.set(true);
    }
  }

  loginWithOidc(returnUrl = '/documents'): void {
    this.redirectToOidc('login', returnUrl);
  }

  logout(): void {
    this.redirectToOidc('logout', '/login');
  }

  private redirectToOidc(action: 'login' | 'logout', returnPath: string): void {
    const safePath = isSafeReturnPath(returnPath) ? returnPath : '/';
    const fullReturnUrl = window.location.origin + safePath;
    window.location.href = `/api/auth/oidc/${action}?returnUrl=${encodeURIComponent(fullReturnUrl)}`;
  }

  private setUser(user: GetUserInfoResponse): void {
    this.isAuthenticated.set(true);
    this.isActive.set(user.isActive);
    this.userName.set(user.displayName || user.email);
    this.userEmail.set(user.email);
    this.role.set(user.role);
  }

  clearUser(): void {
    this.isAuthenticated.set(false);
    this.isActive.set(true);
    this.userName.set('');
    this.userEmail.set('');
    this.role.set('');
  }
}
