import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  convertToParamMap,
  provideRouter,
} from '@angular/router';
import { loginGuard } from './login.guard';
import { AuthService } from '@/shared/services/auth.service';

function runGuard(returnUrl: string | null = null): boolean | UrlTree {
  const route = {
    queryParamMap: convertToParamMap(returnUrl !== null ? { returnUrl } : {}),
  } as ActivatedRouteSnapshot;
  const state = { url: '/login' } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => loginGuard(route, state)) as boolean | UrlTree;
}

describe('loginGuard', () => {
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    auth.clearUser();
  });

  it('returns true for an unauthenticated user', () => {
    auth.isAuthenticated.set(false);

    expect(runGuard()).toBe(true);
  });

  it('redirects an authenticated active user with no returnUrl to /documents', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(true);

    const result = runGuard() as UrlTree;
    const expected = router.parseUrl('/documents');

    expect(result.toString()).toBe(expected.toString());
  });

  it('redirects an authenticated active user to a safe returnUrl', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(true);

    const result = runGuard('/documents/123') as UrlTree;
    const expected = router.parseUrl('/documents/123');

    expect(result.toString()).toBe(expected.toString());
  });

  it('preserves query strings in a safe returnUrl', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(true);

    const result = runGuard('/documents?search=invoice') as UrlTree;
    const expected = router.parseUrl('/documents?search=invoice');

    expect(result.toString()).toBe(expected.toString());
  });

  it('falls back to /documents when returnUrl is unsafe', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(true);

    const result = runGuard('//evil.com') as UrlTree;
    const expected = router.parseUrl('/documents');

    expect(result.toString()).toBe(expected.toString());
  });

  it('redirects an authenticated inactive user to /pending-activation directly (no flicker through /documents)', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(false);

    const result = runGuard() as UrlTree;
    const expected = router.createUrlTree(['/pending-activation']);

    expect(result.toString()).toBe(expected.toString());
  });

  it('redirects an authenticated inactive user to /pending-activation even when returnUrl is set', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(false);

    const result = runGuard('/documents/123') as UrlTree;
    const expected = router.createUrlTree(['/pending-activation']);

    expect(result.toString()).toBe(expected.toString());
  });
});
