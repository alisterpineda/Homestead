import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from '@/shared/services/auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let router: Router;
  let auth: AuthService;
  let navigateSpy: ReturnType<typeof vi.spyOn>;
  let clearUserSpy: ReturnType<typeof vi.spyOn>;

  function setRouterUrl(url: string): void {
    vi.spyOn(router, 'url', 'get').mockReturnValue(url);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    auth = TestBed.inject(AuthService);
    navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    clearUserSpy = vi.spyOn(auth, 'clearUser');
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should clearUser before navigating to /login on 401 for non-auth URLs', () => {
    setRouterUrl('/');
    const callOrder: string[] = [];
    clearUserSpy.mockImplementation(() => {
      callOrder.push('clearUser');
    });
    navigateSpy.mockImplementation((commands: unknown[]) => {
      callOrder.push(`navigate:${commands.join(',')}`);
      return Promise.resolve(true);
    });

    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(clearUserSpy).toHaveBeenCalledTimes(1);
    expect(navigateSpy).toHaveBeenCalledWith(['/login'], undefined);
    expect(callOrder).toEqual(['clearUser', 'navigate:/login']);
  });

  it('should NOT clearUser or navigate on 401 for /api/auth/* URLs', () => {
    http.get('/api/auth/user-info').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/auth/user-info').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(clearUserSpy).not.toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('should navigate to /pending-activation on 403 with account_inactive (no clearUser)', () => {
    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { errorCode: 'account_inactive', detail: 'Account inactive' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(navigateSpy).toHaveBeenCalledWith(['/pending-activation']);
    expect(clearUserSpy).not.toHaveBeenCalled();
  });

  it('should NOT navigate or clearUser on 403 without account_inactive code', () => {
    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Forbidden' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(clearUserSpy).not.toHaveBeenCalled();
  });

  it('should NOT navigate or clearUser on 500 errors', () => {
    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Server Error' },
      { status: 500, statusText: 'Internal Server Error' },
    );

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(clearUserSpy).not.toHaveBeenCalled();
  });

  it('should include returnUrl when navigating to /login on 401 from a deep link', () => {
    setRouterUrl('/documents/123');

    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(navigateSpy).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/documents/123' },
    });
  });

  it('should preserve query string in returnUrl', () => {
    setRouterUrl('/documents?search=invoice');

    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(navigateSpy).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/documents?search=invoice' },
    });
  });

  it('should NOT add returnUrl when on /login at the time of 401', () => {
    setRouterUrl('/login');

    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(navigateSpy).toHaveBeenCalledWith(['/login'], undefined);
  });

  it('should NOT add returnUrl when on /pending-activation at the time of 401', () => {
    setRouterUrl('/pending-activation');

    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(navigateSpy).toHaveBeenCalledWith(['/login'], undefined);
  });

  it('should NOT add returnUrl when on / at the time of 401', () => {
    setRouterUrl('/');

    http.get('/api/documents').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/documents').flush(
      { detail: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(navigateSpy).toHaveBeenCalledWith(['/login'], undefined);
  });
});
