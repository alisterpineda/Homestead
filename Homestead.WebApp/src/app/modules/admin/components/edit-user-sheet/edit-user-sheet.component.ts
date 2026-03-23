import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  inject,
  effect,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { UserResponse } from '@/api/generated/models/user-response';
import type { AppRole } from '@/api/generated/models/app-role';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';
import { extractApiError } from '@/shared/utils/api-error.utils';
import { ChangePasswordDialogComponent } from '../change-password-dialog/change-password-dialog.component';

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
    BrnSelectImports,
    HlmSelectImports,
    ChangePasswordDialogComponent,
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
              <brn-select [ngModel]="editRole()" (ngModelChange)="editRole.set($event)">
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
          @if (user(); as u) {
            <button hlmBtn variant="outline" class="w-full" (click)="openPasswordDialog()">
              {{ u.hasPassword ? 'Change Password' : 'Set Password' }}
            </button>
          }
          <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>

    @if (user(); as u) {
      <app-change-password-dialog
        [userId]="u.id"
        [hasPassword]="u.hasPassword"
        (passwordChanged)="passwordChanged.emit()"
      />
    }
  `,
})
export class EditUserSheetComponent {
  user = input<UserResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();
  passwordChanged = output<void>();

  editEmail = signal('');
  editDisplayName = signal('');
  editRole = signal<AppRole>('Contributor');
  editIsActive = signal(true);
  saving = signal(false);

  private changePasswordDialog = viewChild(ChangePasswordDialogComponent);

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

  openPasswordDialog(): void {
    this.changePasswordDialog()?.open();
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
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('User updated.');
        this.saved.emit();
      },
      error: (err) => {
        this.saving.set(false);
        toast.error(extractApiError(err, 'Failed to save changes.'));
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
