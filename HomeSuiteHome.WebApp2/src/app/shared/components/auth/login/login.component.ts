import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';
import { HlmButton } from '@/components/ui/button';
import { HlmInput } from '@/components/ui/input';
import { HlmLabel } from '@/components/ui/label';
import { HlmCardImports } from '@/components/ui/card';
import { HlmSeparator } from '@/components/ui/separator';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmCardImports,
    HlmSeparator,
  ],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-background px-4">
      <div hlmCard class="w-full max-w-sm">
        <div hlmCardHeader class="text-center">
          <h1 hlmCardTitle>Welcome back</h1>
          <p hlmCardDescription>Sign in to your account</p>
        </div>

        <div hlmCardContent>
          <form [formGroup]="form" (ngSubmit)="onSubmit()" class="flex flex-col gap-4">
            @if (error()) {
              <div
                class="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-3 text-sm text-destructive"
                role="alert"
              >
                {{ error() }}
              </div>
            }

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
        </div>

        <div hlmCardContent class="pt-0">
          <hlm-separator />
          <p class="mt-4 text-center text-sm text-muted-foreground">
            Don't have an account?
            <a routerLink="/register" class="font-medium text-primary underline-offset-4 hover:underline">
              Create one
            </a>
          </p>
        </div>
      </div>
    </div>
  `,
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private auth = inject(AuthService);

  protected error = signal('');
  protected submitting = signal(false);
  protected submitted = signal(false);

  protected form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  async onSubmit(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) return;

    this.error.set('');
    this.submitting.set(true);

    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      this.router.navigate(['/documents']);
    } catch {
      this.error.set('Invalid email or password.');
    } finally {
      this.submitting.set(false);
    }
  }
}
