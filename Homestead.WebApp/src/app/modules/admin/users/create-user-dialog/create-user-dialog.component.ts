import { Component, ChangeDetectionStrategy, inject, input, output, signal, computed, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';

import { HlmSelectImports } from '@/shared/components/spartan/select';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AppRole } from '@/api/generated/models/app-role';
import { toast } from '@spartan-ng/brain/sonner';
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
          <p hlmDialogDescription>Add a new user account. The user will sign in via SSO.</p>
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
            <label hlmLabel>Role</label>
            <hlm-select [ngModel]="role()" (ngModelChange)="role.set($event)">
              <hlm-select-trigger class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
<hlm-select-group>
                <hlm-select-item value="Admin">Admin</hlm-select-item>
                <hlm-select-item value="Contributor">Contributor</hlm-select-item>
                <hlm-select-item value="Viewer">Viewer</hlm-select-item>
                <hlm-select-item value="Guest">Guest</hlm-select-item>
              </hlm-select-group>
</hlm-select-content>
            </hlm-select>
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
  role = signal<AppRole>('Contributor');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() =>
    this.email().trim().length > 0 &&
    this.displayName().trim().length > 0,
  );

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.email.set('');
      this.displayName.set('');
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
