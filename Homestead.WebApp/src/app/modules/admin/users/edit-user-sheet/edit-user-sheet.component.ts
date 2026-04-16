import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  inject,
  effect,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';

import { HlmSelectImports } from '@/shared/components/spartan/select';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { UserResponse } from '@/api/generated/models/user-response';
import type { AppRole } from '@/api/generated/models/app-role';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-edit-user-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmCheckboxImports,
        HlmSelectImports,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit User</h3>
          <p hlmSheetDescription>Update user details and role.</p>
        </hlm-sheet-header>

        @if (user(); as u) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="user-email">Email</label>
              <input
                hlmInput
                id="user-email"
                type="email"
                class="w-full"
                [value]="editEmail()"
                (input)="editEmail.set(asInputValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="user-display-name">Display Name</label>
              <input
                hlmInput
                id="user-display-name"
                class="w-full"
                [value]="editDisplayName()"
                (input)="editDisplayName.set(asInputValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel>Role</label>
              <hlm-select [ngModel]="editRole()" (ngModelChange)="editRole.set($event)">
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

            <div class="flex items-center gap-2">
              <hlm-checkbox
                id="user-is-active"
                [checked]="editIsActive()"
                (checkedChange)="editIsActive.set($event)"
              />
              <label hlmLabel for="user-is-active">Active</label>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Created</label>
              <p class="text-sm text-muted-foreground">
                {{ formatDate(u.createdAt) }}
              </p>
            </div>
          </div>
        }

        <hlm-sheet-footer class="flex flex-col gap-2">
          <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditUserSheetComponent {
  user = input<UserResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  editEmail = signal('');
  editDisplayName = signal('');
  editRole = signal<AppRole>('Contributor');
  editIsActive = signal(true);
  saving = signal(false);

  hasChanges = computed(() => {
    const u = this.user();
    if (!u) return false;
    if (this.editEmail() !== (u.email ?? '')) return true;
    if (this.editDisplayName() !== u.displayName) return true;
    if (this.editRole() !== u.role) return true;
    if (this.editIsActive() !== u.isActive) return true;
    return false;
  });

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);

  constructor() {
    effect(() => {
      const u = this.user();
      if (u && this.state() === 'open') {
        this.editEmail.set(u.email ?? '');
        this.editDisplayName.set(u.displayName);
        this.editRole.set(u.role);
        this.editIsActive.set(u.isActive);
      }
    });
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const u = this.user();
    if (!u) return;
    this.saving.set(true);
    this.adminApi.updateUser({
      id: u.id,
      body: {
        email: this.editEmail(),
        displayName: this.editDisplayName(),
        role: this.editRole(),
        isActive: this.editIsActive(),
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('User updated.');
        this.saved.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
