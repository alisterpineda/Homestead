import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'documents', component: LoginComponent }]),
      ],
    });
    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should display lockout error when API returns account_locked', async () => {
    const comp = component as any;
    comp.form.setValue({ email: 'test@test.com', password: 'password' });

    const submitPromise = comp.onSubmit();

    const req = httpTesting.expectOne('/api/auth/login?useCookies=true');
    req.flush({ error: 'account_locked' }, { status: 401, statusText: 'Unauthorized' });

    await submitPromise;

    expect(comp.error()).toBe(
      'Too many failed login attempts. Your account has been temporarily locked. Please try again later.',
    );
  });

  it('should display generic error for other 401 responses', async () => {
    const comp = component as any;
    comp.form.setValue({ email: 'test@test.com', password: 'wrong' });

    const submitPromise = comp.onSubmit();

    const req = httpTesting.expectOne('/api/auth/login?useCookies=true');
    req.flush(null, { status: 401, statusText: 'Unauthorized' });

    await submitPromise;

    expect(comp.error()).toBe('Invalid email or password.');
  });

  it('should navigate to documents on successful login', async () => {
    const navigateSpy = vi.spyOn(router, 'navigate');
    const comp = component as any;
    comp.form.setValue({ email: 'test@test.com', password: 'password' });

    const submitPromise = comp.onSubmit();

    const loginReq = httpTesting.expectOne('/api/auth/login?useCookies=true');
    loginReq.flush(null, { status: 200, statusText: 'OK' });

    // Allow the microtask from the login POST to resolve before the next HTTP call
    await Promise.resolve();

    const userInfoReq = httpTesting.expectOne('/api/auth/user-info');
    userInfoReq.flush({
      id: '00000000-0000-0000-0000-000000000001',
      email: 'test@test.com',
      displayName: 'Test User',
      role: 'Admin',
      isActive: true,
    });

    await submitPromise;

    expect(navigateSpy).toHaveBeenCalledWith(['/documents']);
  });
});
