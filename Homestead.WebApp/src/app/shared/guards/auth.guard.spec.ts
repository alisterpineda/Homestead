import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '@/shared/services/auth.service';

function runGuard(url: string): boolean | UrlTree {
  const route = {} as ActivatedRouteSnapshot;
  const state = { url } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => authGuard(route, state)) as boolean | UrlTree;
}

describe('authGuard', () => {
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

  it('returns true for an authenticated active user', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(true);

    const result = runGuard('/documents/123');

    expect(result).toBe(true);
  });

  it('redirects an authenticated inactive user to /pending-activation', () => {
    auth.isAuthenticated.set(true);
    auth.isActive.set(false);

    const result = runGuard('/documents/123') as UrlTree;
    const expected = router.createUrlTree(['/pending-activation']);

    expect(result.toString()).toBe(expected.toString());
  });

  it('redirects an unauthenticated user to /login with returnUrl for a deep link', () => {
    auth.isAuthenticated.set(false);

    const result = runGuard('/documents/123') as UrlTree;
    const expected = router.createUrlTree(['/login'], {
      queryParams: { returnUrl: '/documents/123' },
    });

    expect(result.toString()).toBe(expected.toString());
  });

  it('preserves query strings in the captured returnUrl', () => {
    auth.isAuthenticated.set(false);

    const result = runGuard('/documents?search=invoice') as UrlTree;
    const expected = router.createUrlTree(['/login'], {
      queryParams: { returnUrl: '/documents?search=invoice' },
    });

    expect(result.toString()).toBe(expected.toString());
  });

  it('redirects an unauthenticated user hitting / to /login without a returnUrl', () => {
    auth.isAuthenticated.set(false);

    const result = runGuard('/') as UrlTree;
    const expected = router.createUrlTree(['/login']);

    expect(result.toString()).toBe(expected.toString());
  });
});
