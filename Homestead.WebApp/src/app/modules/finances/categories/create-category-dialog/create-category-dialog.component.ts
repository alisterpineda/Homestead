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
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { CategoryPickerComponent, type CategoryDirection } from '../../shared/category-picker/category-picker.component';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';
import type { CategoryResponse } from '@/api/generated/models/category-response';

@Component({
  selector: 'app-create-category-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel, CategoryPickerComponent],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[480px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New category</h2>
          <p hlmDialogDescription>
            Categories group your spending and income. Nest one under a parent to build a hierarchy.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Type</label>
            <div role="radiogroup" class="grid grid-cols-2 gap-2">
              <button
                type="button"
                role="radio"
                [attr.aria-checked]="type() === 'Expense'"
                class="rounded-md border px-3 py-2 text-sm text-left transition-colors"
                [class]="type() === 'Expense' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                (click)="setType('Expense')"
              >
                <div class="font-medium">Expense</div>
                <div class="text-xs text-muted-foreground">Money out</div>
              </button>
              <button
                type="button"
                role="radio"
                [attr.aria-checked]="type() === 'Income'"
                class="rounded-md border px-3 py-2 text-sm text-left transition-colors"
                [class]="type() === 'Income' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                (click)="setType('Income')"
              >
                <div class="font-medium">Income</div>
                <div class="text-xs text-muted-foreground">Money in</div>
              </button>
            </div>
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-category-name">Name</label>
            <input
              hlmInput
              id="create-category-name"
              maxlength="256"
              placeholder="e.g. Groceries"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Parent (optional)</label>
            <app-category-picker
              [value]="parentId()"
              [direction]="type()"
              [allowCreate]="false"
              placeholder="No parent (top level)"
              clearLabel="No parent (top level)"
              ariaLabel="Select parent category"
              (valueChange)="parentId.set($event)"
            />
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create category' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateCategoryDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output<CategoryResponse>();

  name = signal('');
  type = signal<CategoryDirection>('Expense');
  parentId = signal<string | null>(null);
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private categoryApi = inject(FinancesCategoryApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() => this.name().trim().length > 0);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  setType(type: CategoryDirection): void {
    if (this.type() === type) return;
    this.type.set(type);
    // A parent must share the child's type, so switching type drops a now-invalid parent choice.
    this.parentId.set(null);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.type.set('Expense');
      this.parentId.set(null);
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;
    this.creating.set(true);
    this.createError.set('');

    this.categoryApi.createCategory({
      body: {
        name: this.name().trim(),
        type: this.type(),
        parentId: this.parentId(),
      },
    }).subscribe({
      next: (category) => {
        this.creating.set(false);
        toast.success('Category created.');
        this.dialog()?.close();
        this.created.emit(category);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create category.'));
      },
    });
  }
}
