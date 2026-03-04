import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  viewChild,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { changeUserPassword } from '@/api/generated/fn/admin/change-user-password';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-change-password-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" class="hidden" hlmDialogTrigger #trigger>trigger</button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>{{ hasPassword() ? 'Change Password' : 'Set Password' }}</h2>
          <p hlmDialogDescription>
            @if (hasPassword()) {
              Enter a new password for this user.
            } @else {
              This user signed in via an external provider and has no local password. Set one to allow local login.
            }
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="change-pw-new">New Password</label>
            <input
              hlmInput
              id="change-pw-new"
              type="password"
              placeholder="Minimum 8 characters"
              [value]="password()"
              (input)="password.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="change-pw-confirm">Confirm Password</label>
            <input
              hlmInput
              id="change-pw-confirm"
              type="password"
              placeholder="Confirm password"
              [value]="confirmPassword()"
              (input)="confirmPassword.set(asInputValue($event))"
            />
          </div>
          @if (errorMessage()) {
            <p class="text-sm text-destructive">{{ errorMessage() }}</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || submitting()">
              {{ submitting() ? 'Saving...' : (hasPassword() ? 'Change Password' : 'Set Password') }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ChangePasswordDialogComponent {
  userId = input.required<string>();
  hasPassword = input(true);
  passwordChanged = output<void>();

  password = signal('');
  confirmPassword = signal('');
  submitting = signal(false);
  errorMessage = signal('');

  private dialog = viewChild(BrnDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  canSubmit = computed(() =>
    this.password().length >= 8 &&
    this.password() === this.confirmPassword(),
  );

  open(): void {
    this.dialog()?.open();
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.password.set('');
      this.confirmPassword.set('');
      this.submitting.set(false);
      this.errorMessage.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.submitting.set(true);
    this.errorMessage.set('');

    changeUserPassword(this.http, this.apiConfig.rootUrl, {
      id: this.userId(),
      body: { password: this.password() },
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        toast.success('Password changed.');
        this.dialog()?.close();
        this.passwordChanged.emit();
      },
      error: (err) => {
        this.submitting.set(false);
        const errors = err?.error?.errors;
        if (errors) {
          const firstError = Object.values<string[]>(errors)[0]?.[0];
          this.errorMessage.set(firstError ?? 'Failed to change password.');
        } else {
          this.errorMessage.set('Failed to change password. Please try again.');
        }
      },
    });
  }
}
