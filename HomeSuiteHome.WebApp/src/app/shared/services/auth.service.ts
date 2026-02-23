import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { OAuthService, AuthConfig } from 'angular-oauth2-oidc';
import { firstValueFrom } from 'rxjs';

interface AuthConfigResponse {
  authority: string;
  clientId: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private oauthService = inject(OAuthService);

  readonly initialized = signal(false);
  readonly isAuthenticated = signal(false);

  readonly userName = computed(() => {
    const claims = this.oauthService.getIdentityClaims();
    return (claims?.['preferred_username'] as string) ??
      (claims?.['name'] as string) ?? '';
  });

  readonly userEmail = computed(() => {
    const claims = this.oauthService.getIdentityClaims();
    return (claims?.['email'] as string) ?? '';
  });

  async initialize(): Promise<void> {
    const config = await firstValueFrom(
      this.http.get<AuthConfigResponse>('/api/v1/auth/config'),
    );

    const authConfig: AuthConfig = {
      issuer: config.authority,
      clientId: config.clientId,
      redirectUri: window.location.origin,
      responseType: 'code',
      scope: 'openid profile email',
      showDebugInformation: false,
    };

    this.oauthService.configure(authConfig);

    await this.oauthService.loadDiscoveryDocumentAndTryLogin();

    if (this.oauthService.hasValidAccessToken()) {
      this.isAuthenticated.set(true);
      this.initialized.set(true);
      this.oauthService.setupAutomaticSilentRefresh();
    } else {
      this.oauthService.initCodeFlow();
      // Return a never-resolving promise so the app doesn't render before redirect
      return new Promise<void>(() => {});
    }
  }

  logout(): void {
    this.oauthService.logOut();
  }
}
