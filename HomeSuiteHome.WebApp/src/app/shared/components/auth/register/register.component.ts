import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';

@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-background px-4">
      <div class="w-full max-w-sm space-y-6">
        <div class="space-y-2 text-center">
          <h1 class="text-2xl font-semibold tracking-tight text-foreground">Create an account</h1>
          <p class="text-sm text-muted-foreground">Enter your details to get started</p>
        </div>

        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="space-y-4">
          @if (error()) {
            <div
              class="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-3 text-sm text-destructive"
              role="alert"
            >
              {{ error() }}
            </div>
          }

          <div class="space-y-2">
            <label for="email" class="text-sm font-medium text-foreground">Email</label>
            <input
              id="email"
              type="email"
              formControlName="email"
              autocomplete="email"
              class="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              placeholder="name@example.com"
            />
            @if (submitted() && form.get('email')?.hasError('required')) {
              <p class="text-xs text-destructive">Email is required.</p>
            } @else if (submitted() && form.get('email')?.hasError('email')) {
              <p class="text-xs text-destructive">Enter a valid email address.</p>
            }
          </div>

          <div class="space-y-2">
            <label for="displayName" class="text-sm font-medium text-foreground">Display name</label>
            <input
              id="displayName"
              type="text"
              formControlName="displayName"
              autocomplete="name"
              class="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              placeholder="Your name"
            />
            @if (submitted() && form.get('displayName')?.hasError('required')) {
              <p class="text-xs text-destructive">Display name is required.</p>
            }
          </div>

          <div class="space-y-2">
            <label for="password" class="text-sm font-medium text-foreground">Password</label>
            <input
              id="password"
              type="password"
              formControlName="password"
              autocomplete="new-password"
              class="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
            @if (submitted() && form.get('password')?.hasError('required')) {
              <p class="text-xs text-destructive">Password is required.</p>
            } @else if (submitted() && form.get('password')?.hasError('minlength')) {
              <p class="text-xs text-destructive">Password must be at least 8 characters.</p>
            }
          </div>

          <div class="space-y-2">
            <label for="confirmPassword" class="text-sm font-medium text-foreground">
              Confirm password
            </label>
            <input
              id="confirmPassword"
              type="password"
              formControlName="confirmPassword"
              autocomplete="new-password"
              class="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
            @if (submitted() && form.get('confirmPassword')?.hasError('required')) {
              <p class="text-xs text-destructive">Confirm your password.</p>
            } @else if (submitted() && form.hasError('passwordMismatch')) {
              <p class="text-xs text-destructive">Passwords do not match.</p>
            }
          </div>

          <button
            type="submit"
            [disabled]="submitting()"
            class="inline-flex h-10 w-full items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50"
          >
            {{ submitting() ? 'Creating account...' : 'Create account' }}
          </button>
        </form>

        <p class="text-center text-sm text-muted-foreground">
          Already have an account?
          <a routerLink="/login" class="font-medium text-primary underline-offset-4 hover:underline">
            Sign in
          </a>
        </p>
      </div>
    </div>
  `,
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private auth = inject(AuthService);

  protected error = signal('');
  protected submitting = signal(false);
  protected submitted = signal(false);

  protected form = this.fb.nonNullable.group(
    {
      email: ['', [Validators.required, Validators.email]],
      displayName: ['', Validators.required],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: [passwordMatchValidator] },
  );

  async onSubmit(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) return;

    this.error.set('');
    this.submitting.set(true);

    try {
      const { email, displayName, password } = this.form.getRawValue();
      await this.auth.register(email, displayName, password);
      this.router.navigate(['/documents']);
    } catch {
      this.error.set('Registration failed. The email may already be in use.');
    } finally {
      this.submitting.set(false);
    }
  }
}

function passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirm = control.get('confirmPassword')?.value;
  return password === confirm ? null : { passwordMismatch: true };
}
