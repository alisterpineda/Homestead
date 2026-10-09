import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  linkedSignal,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideCircleAlert, lucideFolderTree } from '@ng-icons/lucide';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { CreateCategoryDialogComponent } from './create-category-dialog/create-category-dialog.component';
import { EditCategorySheetComponent } from './edit-category-sheet/edit-category-sheet.component';
import { CategoryTreeComponent } from './category-tree.component';
import { CategoryListStateService, isCategoryTypeFilter } from './category-list-state.service';
import { buildCategoryTree } from './category-tree.util';
import type { CategoryResponse } from '@/api/generated/models/category-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-category-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmToggleGroupImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    CategoryTreeComponent,
    CreateCategoryDialogComponent,
    EditCategorySheetComponent,
  ],
  providers: [provideIcons({ lucidePlus, lucideCircleAlert, lucideFolderTree })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      <div class="flex items-center justify-between gap-2">
        <hlm-toggle-group
          type="single"
          nullable="false"
          variant="outline"
          aria-label="Type filter"
          [value]="typeFilter()"
          (valueChange)="onTypeFilterChange($event)"
        >
          <button hlmToggleGroupItem value="all">All</button>
          <button hlmToggleGroupItem value="Expense">Expense</button>
          <button hlmToggleGroupItem value="Income">Income</button>
        </hlm-toggle-group>
        <app-create-category-dialog (created)="onChanged()">
          <ng-icon name="lucidePlus" class="text-[length:--spacing(4)]" />
          New category
        </app-create-category-dialog>
      </div>

      @if (loading() && !data()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Failed to load categories</div>
            <div hlmEmptyDescription>There was an error loading categories. Please try again.</div>
          </div>
        </div>
      } @else {
        @if (!hasUserCategories()) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon"><ng-icon name="lucideFolderTree" /></div>
              <div hlmEmptyTitle>No categories yet</div>
              <div hlmEmptyDescription>
                Add a category to start grouping your spending and income. Flows stay Uncategorised until you assign one.
              </div>
            </div>
          </div>
        }
        <div class="overflow-clip rounded-md border">
          <app-category-tree
            [nodes]="tree()"
            [expanded]="expandedSet()"
            [toggle]="toggle"
            [edit]="onEditNode"
            [delete]="onDeleteNode"
          />
        </div>
      }
    </div>

    <app-edit-category-sheet
      [category]="selectedCategory()"
      [allCategories]="data() ?? []"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'category' }}?</h2>
          <p hlmAlertDialogDescription>
            Its transactions move to Uncategorised and any sub-categories move up to its parent.
            This cannot be undone.
          </p>
        </div>
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button
            hlmAlertDialogAction
            variant="destructive"
            (click)="confirmDelete(ctx)"
            [disabled]="deleting()"
          >
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class CategoryListPageComponent {
  private categoryApi = inject(FinancesCategoryApiService);
  private errorHandler = inject(ErrorHandlerService);
  private listState = inject(CategoryListStateService);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  readonly typeFilter = this.listState.typeFilter;

  data = toSignal<CategoryResponse[] | null, CategoryResponse[] | null>(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return this.categoryApi.getCategories().pipe(
          map(items => {
            this.loading.set(false);
            return items as CategoryResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  // Expand every parent on first real load; preserve the user's toggles across later refetches.
  private expanded = linkedSignal<CategoryResponse[] | null, Set<string> | null>({
    source: this.data,
    computation: (items, prev) => {
      if (prev?.value) return prev.value;
      if (items == null) return null;
      return new Set(items.map(c => c.id));
    },
  });

  expandedSet = computed<ReadonlySet<string>>(() => this.expanded() ?? new Set<string>());

  tree = computed(() => {
    const all = this.data() ?? [];
    const filter = this.typeFilter();
    const filtered = filter === 'all' ? all : all.filter(c => c.type === filter);
    return buildCategoryTree(filtered);
  });

  hasUserCategories = computed(() => (this.data() ?? []).some(c => !c.isSystem));

  selectedCategory = signal<CategoryResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  pendingDelete = signal<CategoryResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);

  toggle = (id: string): void => {
    this.expanded.update(set => {
      const next = new Set(set ?? []);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  onEditNode = (category: CategoryResponse): void => {
    this.selectedCategory.set(category);
    this.sheetState.set('open');
  };

  onDeleteNode = (category: CategoryResponse): void => {
    this.pendingDelete.set(category);
    this.deleteState.set('open');
  };

  onTypeFilterChange(value: unknown): void {
    if (isCategoryTypeFilter(value)) this.typeFilter.set(value);
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onChanged();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedCategory.set(null);
  }

  confirmDelete(ctx: { close: () => void }): void {
    const category = this.pendingDelete();
    if (!category) return;
    this.deleting.set(true);
    const result$ = this.categoryApi.deleteCategory({ id: category.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete category.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Category deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }
}
