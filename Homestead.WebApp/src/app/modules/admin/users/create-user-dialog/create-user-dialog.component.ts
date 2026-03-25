import { Component, ChangeDetectionStrategy, inject, input, output, signal, computed, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AppRole } from '@/api/generated/models/app-role';
import { toast } from 'ngx-sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-user-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    BrnSelectImports,
    HlmSelectImports,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create User</h2>
          <p hlmDialogDescription>Add a new user account.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-user-email">Email</label>
            <input
              hlmInput
              id="create-user-email"
              type="email"
              placeholder="user@example.com"
              [value]="email()"
              (input)="email.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-user-display-name">Display Name</label>
            <input
              hlmInput
              id="create-user-display-name"
              placeholder="Enter display name"
              [value]="displayName()"
              (input)="displayName.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-user-password">Password</label>
            <input
              hlmInput
              id="create-user-password"
              type="password"
              placeholder="Minimum 8 characters"
              [value]="password()"
              (input)="password.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-user-confirm-password">Confirm Password</label>
            <input
              hlmInput
              id="create-user-confirm-password"
              type="password"
              placeholder="Confirm password"
              [value]="confirmPassword()"
              (input)="confirmPassword.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Role</label>
            <brn-select [ngModel]="role()" (ngModelChange)="role.set($event)">
              <hlm-select-trigger class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content>
                <hlm-option value="Admin">Admin</hlm-option>
                <hlm-option value="Contributor">Contributor</hlm-option>
                <hlm-option value="Viewer">Viewer</hlm-option>
                <hlm-option value="Guest">Guest</hlm-option>
              </hlm-select-content>
            </brn-select>
          </div>
          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateUserDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  email = signal('');
  displayName = signal('');
  password = signal('');
  confirmPassword = signal('');
  role = signal<AppRole>('Contributor');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() =>
    this.email().trim().length > 0 &&
    this.displayName().trim().length > 0 &&
    this.password().length >= 8 &&
    this.password() === this.confirmPassword(),
  );

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.email.set('');
      this.displayName.set('');
      this.password.set('');
      this.confirmPassword.set('');
      this.role.set('Contributor');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    this.adminApi.createUser({
      body: {
        email: this.email().trim(),
        displayName: this.displayName().trim(),
        password: this.password(),
        role: this.role(),
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('User created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create user.'));
      },
    });
  }
}
