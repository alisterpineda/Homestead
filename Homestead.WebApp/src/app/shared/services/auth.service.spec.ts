import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import type { GetUserInfoResponse } from '@/api/generated/models/get-user-info-response';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
  });

  describe('isAdmin', () => {
    it('should return true only for Admin role', () => {
      setUser({ role: 'Admin' });
      expect(service.isAdmin()).toBe(true);

      setUser({ role: 'Contributor' });
      expect(service.isAdmin()).toBe(false);

      setUser({ role: 'Viewer' });
      expect(service.isAdmin()).toBe(false);

      setUser({ role: 'Guest' });
      expect(service.isAdmin()).toBe(false);
    });
  });

  describe('canWrite', () => {
    it('should return true for Admin and Contributor', () => {
      setUser({ role: 'Admin' });
      expect(service.canWrite()).toBe(true);

      setUser({ role: 'Contributor' });
      expect(service.canWrite()).toBe(true);
    });

    it('should return false for Viewer and Guest', () => {
      setUser({ role: 'Viewer' });
      expect(service.canWrite()).toBe(false);

      setUser({ role: 'Guest' });
      expect(service.canWrite()).toBe(false);
    });
  });

  describe('canRead', () => {
    it('should return true for Admin, Contributor, and Viewer', () => {
      setUser({ role: 'Admin' });
      expect(service.canRead()).toBe(true);

      setUser({ role: 'Contributor' });
      expect(service.canRead()).toBe(true);

      setUser({ role: 'Viewer' });
      expect(service.canRead()).toBe(true);
    });

    it('should return false for Guest', () => {
      setUser({ role: 'Guest' });
      expect(service.canRead()).toBe(false);
    });
  });

  describe('setUser', () => {
    it('should set role from API response', () => {
      setUser({ role: 'Viewer' });

      expect(service.role()).toBe('Viewer');
      expect(service.isAuthenticated()).toBe(true);
    });

    it('should handle Guest role with no access', () => {
      setUser({ role: 'Guest' });

      expect(service.role()).toBe('Guest');
      expect(service.canWrite()).toBe(false);
      expect(service.canRead()).toBe(false);
    });
  });

  describe('logout', () => {
    it('should reset role to empty string', async () => {
      setUser({ role: 'Admin' });
      expect(service.role()).toBe('Admin');

      // logout() makes a full-page redirect; use clearUser() to reset state in tests.
      service.clearUser();

      expect(service.role()).toBe('');
      expect(service.isAdmin()).toBe(false);
      expect(service.canWrite()).toBe(false);
      expect(service.canRead()).toBe(false);
    });
  });

  describe('clearUser', () => {
    it('should reset all session signals to defaults', () => {
      setUser({
        role: 'Admin',
        email: 'admin@example.com',
        displayName: 'Admin User',
        isActive: false,
      });
      expect(service.isAuthenticated()).toBe(true);
      expect(service.role()).toBe('Admin');

      service.clearUser();

      expect(service.isAuthenticated()).toBe(false);
      expect(service.isActive()).toBe(true);
      expect(service.userName()).toBe('');
      expect(service.userEmail()).toBe('');
      expect(service.role()).toBe('');
      expect(service.isAdmin()).toBe(false);
      expect(service.canWrite()).toBe(false);
      expect(service.canRead()).toBe(false);
    });

    it('should not clear oidcDisplayName', () => {
      service.oidcDisplayName.set('Keycloak');
      setUser({ role: 'Contributor' });

      service.clearUser();

      expect(service.oidcDisplayName()).toBe('Keycloak');
    });
  });

  /** Helper to simulate setUser via the public signals (since setUser is private) */
  function setUser(overrides: Partial<GetUserInfoResponse> = {}) {
    const user: GetUserInfoResponse = {
      id: '00000000-0000-0000-0000-000000000001',
      email: 'test@example.com',
      displayName: 'Test User',
      role: 'Contributor',
      isActive: true,
      ...overrides,
    };
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (service as any)['setUser'](user);
  }
});
