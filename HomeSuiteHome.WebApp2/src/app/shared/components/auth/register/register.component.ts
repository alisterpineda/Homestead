import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';
import { HlmButton } from '@/components/ui/button';
import { HlmInput } from '@/components/ui/input';
import { HlmLabel } from '@/components/ui/label';
import { HlmCardImports } from '@/components/ui/card';
import { HlmSeparator } from '@/components/ui/separator';

@Component({
  selector: 'app-register',
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
          <h1 hlmCardTitle>Create an account</h1>
          <p hlmCardDescription>Enter your details to get started</p>
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
              <label hlmLabel for="displayName">Display name</label>
              <input
                hlmInput
                id="displayName"
                type="text"
                formControlName="displayName"
                autocomplete="name"
                placeholder="Your name"
              />
              @if (submitted() && form.get('displayName')?.hasError('required')) {
                <p class="text-xs text-destructive">Display name is required.</p>
              }
            </div>

            <div class="flex flex-col gap-2">
              <label hlmLabel for="password">Password</label>
              <input
                hlmInput
                id="password"
                type="password"
                formControlName="password"
                autocomplete="new-password"
              />
              @if (submitted() && form.get('password')?.hasError('required')) {
                <p class="text-xs text-destructive">Password is required.</p>
              } @else if (submitted() && form.get('password')?.hasError('minlength')) {
                <p class="text-xs text-destructive">Password must be at least 8 characters.</p>
              }
            </div>

            <div class="flex flex-col gap-2">
              <label hlmLabel for="confirmPassword">Confirm password</label>
              <input
                hlmInput
                id="confirmPassword"
                type="password"
                formControlName="confirmPassword"
                autocomplete="new-password"
              />
              @if (submitted() && form.get('confirmPassword')?.hasError('required')) {
                <p class="text-xs text-destructive">Confirm your password.</p>
              } @else if (submitted() && form.hasError('passwordMismatch')) {
                <p class="text-xs text-destructive">Passwords do not match.</p>
              }
            </div>

            <button hlmBtn type="submit" class="w-full" [disabled]="submitting()">
              {{ submitting() ? 'Creating account...' : 'Create account' }}
            </button>
          </form>
        </div>

        <div hlmCardContent class="pt-0">
          <hlm-separator />
          <p class="mt-4 text-center text-sm text-muted-foreground">
            Already have an account?
            <a routerLink="/login" class="font-medium text-primary underline-offset-4 hover:underline">
              Sign in
            </a>
          </p>
        </div>
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
