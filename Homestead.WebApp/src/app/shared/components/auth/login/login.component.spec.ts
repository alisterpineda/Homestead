import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '@/shared/services/auth.service';

function configure(errorParam: string | null): ComponentFixture<LoginComponent> {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: { queryParamMap: { get: (k: string) => (k === 'error' ? errorParam : null) } },
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
    const fixture = configure(null);
    const auth = TestBed.inject(AuthService);
    auth.oidcDisplayName.set('Keycloak');
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button');
    expect(button).toBeTruthy();
    expect(button.textContent).toContain('Sign in with Keycloak');
  });

  it('delegates SSO click to AuthService.loginWithOidc', () => {
    const fixture = configure(null);
    const auth = TestBed.inject(AuthService);
    const spy = vi.spyOn(auth, 'loginWithOidc').mockImplementation(() => undefined);

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();

    expect(spy).toHaveBeenCalledTimes(1);
  });

  it('shows the OIDC-failed error message when ?error=oidc-failed', () => {
    const fixture = configure('oidc-failed');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Single sign-on failed');
  });

  it('shows the account-inactive message when ?error=account-inactive', () => {
    const fixture = configure('account-inactive');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('account is inactive');
  });
});
