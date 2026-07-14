import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
} from '@angular/core';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { CategoryPickerComponent, type CategoryDirection } from '../../shared/category-picker/category-picker.component';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { descendantIds } from '../category-tree.util';
import { toast } from '@spartan-ng/brain/sonner';
import type { CategoryResponse } from '@/api/generated/models/category-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-edit-category-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, CategoryPickerComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit category</h3>
          <p hlmSheetDescription>Rename or move this category. The type can't be changed.</p>
        </hlm-sheet-header>

        @if (category(); as c) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Type</label>
              <p class="text-sm text-muted-foreground">{{ c.type }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="edit-category-name">Name</label>
              <input
                hlmInput
                id="edit-category-name"
                class="w-full"
                maxlength="256"
                [value]="name()"
                (input)="name.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel>Parent</label>
              <app-category-picker
                [value]="parentIdField()"
                [direction]="direction()"
                [allowCreate]="false"
                [excludeIds]="excludeIds()"
                placeholder="No parent (top level)"
                clearLabel="No parent (top level)"
                ariaLabel="Select parent category"
                (valueChange)="parentIdField.set($event)"
              />
            </div>
          </div>
        }

        <hlm-sheet-footer class="flex flex-col gap-2">
          <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!canSave() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditCategorySheetComponent {
  category = input<CategoryResponse | null>(null);
  state = input<BrnDialogState>('closed');
  allCategories = input<readonly CategoryResponse[]>([]);

  saved = output<CategoryResponse>();
  closed = output<void>();

  private categoryApi = inject(FinancesCategoryApiService);
  private errorHandler = inject(ErrorHandlerService);

  private seedKey = computed(() => `${this.category()?.id ?? ''}|${this.state() === 'open'}`);

  name = seededLinkedSignal(this.seedKey, () => this.category()?.name ?? '');
  parentIdField = seededLinkedSignal<string, string | null>(this.seedKey, () => this.category()?.parentId ?? null);
  saving = seededLinkedSignal(this.seedKey, () => false);

  // A category is always Income/Expense; narrow the AccountType for the picker's direction input.
  direction = computed<CategoryDirection>(() => (this.category()?.type === 'Income' ? 'Income' : 'Expense'));

  // Exclude the category itself and its descendants — none can be its own parent (the server re-checks).
  excludeIds = computed<string[]>(() => {
    const c = this.category();
    if (!c) return [];
    return [c.id, ...descendantIds(this.allCategories(), c.id)];
  });

  canSave = computed(() => {
    const c = this.category();
    if (!c) return false;
    const name = this.name().trim();
    if (!name) return false;
    return name !== c.name || (this.parentIdField() ?? null) !== (c.parentId ?? null);
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const c = this.category();
    if (!c || !this.canSave()) return;
    this.saving.set(true);

    this.categoryApi.patchCategory({
      id: c.id,
      body: { name: this.name().trim(), parentId: this.parentIdField() },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Category updated.');
        this.saved.emit(updated);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
