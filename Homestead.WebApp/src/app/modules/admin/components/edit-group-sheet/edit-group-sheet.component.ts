import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output, effect } from '@angular/core';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { GroupDetailResponse } from '@/api/generated/models/group-detail-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-edit-group-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmInput, HlmLabel],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit Group</h3>
          <p hlmSheetDescription>Update group details.</p>
        </hlm-sheet-header>

        @if (group(); as g) {
          <div class="flex-1 space-y-4 overflow-y-auto px-4 py-4">
            <div class="flex flex-col gap-2">
              <label hlmLabel for="edit-group-name">Name</label>
              <input hlmInput id="edit-group-name" [value]="editName()"
                (input)="editName.set(asInputValue($event))" />
            </div>

            <div class="flex flex-col gap-2">
              <label hlmLabel for="edit-group-desc">Description</label>
              <input hlmInput id="edit-group-desc" [value]="editDescription()"
                (input)="editDescription.set(asInputValue($event))" />
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
export class EditGroupSheetComponent {
  group = input<GroupDetailResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);

  editName = signal('');
  editDescription = signal('');
  saving = signal(false);

  hasChanges = computed(() => {
    const g = this.group();
    if (!g) return false;
    if (this.editName() !== g.name) return true;
    if (this.editDescription() !== (g.description ?? '')) return true;
    return false;
  });

  constructor() {
    effect(() => {
      const g = this.group();
      if (g && this.state() === 'open') {
        this.editName.set(g.name);
        this.editDescription.set(g.description ?? '');
      }
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const g = this.group();
    if (!g) return;
    this.saving.set(true);

    this.adminApi.updateGroup({
      id: g.id,
      body: {
        name: this.editName().trim(),
        description: this.editDescription().trim() || null,
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Group updated.');
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
