import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '@/shared/services/auth.service';

interface ConfigureOptions {
  errorParam?: string | null;
  returnUrl?: string | null;
}

function configure(options: ConfigureOptions = {}): ComponentFixture<LoginComponent> {
  const errorParam = options.errorParam ?? null;
  const returnUrl = options.returnUrl ?? null;
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            queryParamMap: {
              get: (k: string) => {
                if (k === 'error') return errorParam;
                if (k === 'returnUrl') return returnUrl;
                return null;
              },
            },
          },
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(LoginComponent);
  fixture.detectChanges();
  return fixture;
}

describe('LoginComponent', () => {
  it('renders the SSO button with the configured display name', () => {
    const fixture = configure();
    const auth = TestBed.inject(AuthService);
    auth.oidcDisplayName.set('Keycloak');
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button');
    expect(button).toBeTruthy();
    expect(button.textContent).toContain('Sign in with Keycloak');
  });

  it('delegates SSO click to AuthService.loginWithOidc', () => {
    const fixture = configure();
    const auth = TestBed.inject(AuthService);
    const spy = vi.spyOn(auth, 'loginWithOidc').mockImplementation(() => undefined);

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();

    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith(undefined);
  });

  it('shows the OIDC-failed error message when ?error=oidc-failed', () => {
    const fixture = configure({ errorParam: 'oidc-failed' });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Single sign-on failed');
  });

  it('shows the account-inactive message when ?error=account-inactive', () => {
    const fixture = configure({ errorParam: 'account-inactive' });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('account is inactive');
  });

  it('forwards a safe ?returnUrl to AuthService.loginWithOidc on click', () => {
    const fixture = configure({ returnUrl: '/documents/123' });
    const auth = TestBed.inject(AuthService);
    const spy = vi.spyOn(auth, 'loginWithOidc').mockImplementation(() => undefined);

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();

    expect(spy).toHaveBeenCalledWith('/documents/123');
  });

  it('drops an unsafe ?returnUrl so the AuthService default path is used', () => {
    const fixture = configure({ returnUrl: '//evil.com' });
    const auth = TestBed.inject(AuthService);
    const spy = vi.spyOn(auth, 'loginWithOidc').mockImplementation(() => undefined);

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();

    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith(undefined);
  });
});
