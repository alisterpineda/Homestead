import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCardImports } from '@/shared/components/spartan/card';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmCardImports,
  ],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-background px-4">
      <div hlmCard class="w-full max-w-sm">
        <div hlmCardHeader class="text-center">
          <h1 hlmCardTitle>Welcome back</h1>
          <p hlmCardDescription>Sign in to your account</p>
        </div>

        <div hlmCardContent>
          <div class="flex flex-col gap-4">
            @if (error()) {
              <div
                class="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-3 text-sm text-destructive"
                role="alert"
              >
                {{ error() }}
              </div>
            }

            @if (auth.oidcEnabled()) {
              <button hlmBtn type="button" class="w-full" (click)="onOidcLogin()">
                Sign in with {{ auth.oidcDisplayName() }}
              </button>
            }

            @if (auth.oidcEnabled() && auth.localLoginEnabled()) {
              <div class="relative flex items-center justify-center">
                <div class="absolute inset-0 flex items-center" aria-hidden="true">
                  <div class="w-full border-t border-border"></div>
                </div>
                <span class="relative bg-card px-2 text-xs text-muted-foreground">or</span>
              </div>
            }

            @if (auth.localLoginEnabled()) {
              <form [formGroup]="form" (ngSubmit)="onSubmit()" class="flex flex-col gap-4">
                <div class="flex flex-col gap-2">
                  <label hlmLabel for="email">Email</label>
                  <input
                    hlmInput
                    id="email"
                    type="email"
                    formControlName="email"
                    autocomplete="email"
                    placeholder="name@example.com"
                  />
                  @if (submitted() && form.get('email')?.hasError('required')) {
                    <p class="text-xs text-destructive">Email is required.</p>
                  } @else if (submitted() && form.get('email')?.hasError('email')) {
                    <p class="text-xs text-destructive">Enter a valid email address.</p>
                  }
                </div>

                <div class="flex flex-col gap-2">
                  <label hlmLabel for="password">Password</label>
                  <input
                    hlmInput
                    id="password"
                    type="password"
                    formControlName="password"
                    autocomplete="current-password"
                  />
                  @if (submitted() && form.get('password')?.hasError('required')) {
                    <p class="text-xs text-destructive">Password is required.</p>
                  }
                </div>

                <button hlmBtn type="submit" class="w-full" [disabled]="submitting()">
                  {{ submitting() ? 'Signing in...' : 'Sign in' }}
                </button>
              </form>
            }
          </div>
        </div>
      </div>
    </div>
  `,
})
export class LoginComponent implements OnInit {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  protected auth = inject(AuthService);

  protected error = signal('');
  protected submitting = signal(false);
  protected submitted = signal(false);

  protected form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  ngOnInit(): void {
    const errorParam = this.route.snapshot.queryParamMap.get('error');
    if (errorParam === 'oidc-failed') {
      this.error.set('Single sign-on failed. Please try again.');
    }
  }

  onOidcLogin(): void {
    this.auth.loginWithOidc();
  }

  async onSubmit(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) return;

    this.error.set('');
    this.submitting.set(true);

    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      this.router.navigate(['/documents']);
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.error?.error === 'account_locked') {
        this.error.set('Too many failed login attempts. Your account has been temporarily locked. Please try again later.');
      } else {
        this.error.set('Invalid email or password.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
