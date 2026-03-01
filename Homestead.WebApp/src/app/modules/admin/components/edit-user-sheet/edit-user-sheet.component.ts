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
import { HttpClient } from '@angular/common/http';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { updateUser } from '@/api/generated/fn/admin/update-user';
import type { GetUsersResponse } from '@/api/generated/models/get-users-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-edit-user-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmCheckboxImports,
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
              <label hlmLabel for="user-password">New Password</label>
              <input
                hlmInput
                id="user-password"
                type="password"
                class="w-full"
                placeholder="Leave empty to keep current"
                [value]="editPassword()"
                (input)="editPassword.set(asInputValue($event))"
              />
            </div>

            <div class="flex items-center gap-2">
              <hlm-checkbox
                id="user-is-admin"
                [checked]="editIsAdmin()"
                (checkedChange)="editIsAdmin.set($event)"
              />
              <label hlmLabel for="user-is-admin">Administrator</label>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Created</label>
              <p class="text-sm text-muted-foreground">
                {{ formatDate(u.createdAt) }}
              </p>
            </div>
          </div>
        }

        <hlm-sheet-footer>
          <button hlmBtn (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditUserSheetComponent {
  user = input<GetUsersResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  editEmail = signal('');
  editDisplayName = signal('');
  editPassword = signal('');
  editIsAdmin = signal(false);
  saving = signal(false);

  hasChanges = computed(() => {
    const u = this.user();
    if (!u) return false;
    if (this.editEmail() !== (u.email ?? '')) return true;
    if (this.editDisplayName() !== u.displayName) return true;
    if (this.editPassword().length > 0) return true;
    if (this.editIsAdmin() !== u.isAdmin) return true;
    return false;
  });

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  constructor() {
    effect(() => {
      const u = this.user();
      if (u && this.state() === 'open') {
        this.editEmail.set(u.email ?? '');
        this.editDisplayName.set(u.displayName);
        this.editPassword.set('');
        this.editIsAdmin.set(u.isAdmin);
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
    updateUser(this.http, this.apiConfig.rootUrl, {
      id: u.id,
      body: {
        email: this.editEmail(),
        displayName: this.editDisplayName(),
        password: this.editPassword() || null,
        isAdmin: this.editIsAdmin(),
      },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('User updated.');
        this.saved.emit();
      },
      error: (err) => {
        this.saving.set(false);
        const errors = err?.error?.errors;
        if (errors) {
          const firstError = Object.values<string[]>(errors)[0]?.[0];
          toast.error(firstError ?? 'Failed to save changes.');
        } else {
          toast.error('Failed to save changes.');
        }
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
