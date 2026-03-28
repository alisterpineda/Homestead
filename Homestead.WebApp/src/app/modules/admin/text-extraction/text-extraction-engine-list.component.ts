import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest, Subscription, firstValueFrom } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs/operators';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideScanText, lucideSearch, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { CreateTextExtractionEngineDialogComponent } from './create-text-extraction-engine-dialog/create-text-extraction-engine-dialog.component';
import { EditTextExtractionEngineSheetComponent } from './edit-text-extraction-engine-sheet/edit-text-extraction-engine-sheet.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { PaginatedResponseOfTextExtractionEngineResponse } from '@/api/generated/models/paginated-response-of-text-extraction-engine-response';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';
import type { BulkDeleteTextExtractionEnginesResponse } from '@/api/generated/models/bulk-delete-text-extraction-engines-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { ENGINE_TYPE_LABELS } from './engine-type-labels';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-text-extraction-engine-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, NgIcon, HlmIconImports, HlmSpinner, HlmEmptyImports, HlmInput,
    HlmButton, HlmTableImports, HlmBadge, HlmCheckboxImports, HlmAlertDialogImports,
    PaginationComponent, CreateTextExtractionEngineDialogComponent, EditTextExtractionEngineSheetComponent,
  ],
  providers: [provideIcons({ lucideScanText, lucideSearch, lucidePlus, lucideTrash2 })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideScanText" />
            </div>
            <div hlmEmptyTitle>Failed to load text extraction engines</div>
            <div hlmEmptyDescription>There was an error loading the engine list. Please try again.</div>
          </div>
        </div>
      }

      @if (!loading() || data()) {
        @if (data(); as d) {
          @if (d.items.length === 0 && !searchTerm()) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucideScanText" />
                </div>
                <div hlmEmptyTitle>No text extraction engines found</div>
                <div hlmEmptyDescription>Add an engine configuration to get started.</div>
              </div>
              <div hlmEmptyContent>
                <app-create-text-extraction-engine-dialog (created)="onRefresh()">
                  <ng-icon hlm name="lucidePlus" size="sm" class="mr-1" />
                  Add Engine
                </app-create-text-extraction-engine-dialog>
              </div>
            </div>
          } @else {
            <div class="relative shrink-0">
              <!-- Normal toolbar -->
              <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()">
                <div class="flex items-center gap-2 mb-4">
                  <div class="relative flex-1">
                    <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
                    <input hlmInput class="pl-9 w-full" placeholder="Search engines..."
                      [value]="searchInput()" (input)="searchInput.set(asInputValue($event))" />
                  </div>
                  <app-create-text-extraction-engine-dialog triggerSize="sm" (created)="onRefresh()">
                    <ng-icon hlm name="lucidePlus" size="sm" class="mr-1" />
                    Add Engine
                  </app-create-text-extraction-engine-dialog>
                </div>
              </div>

              <!-- Selection toolbar -->
              <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background"
                [class.opacity-0]="!hasSelection()" [class.pointer-events-none]="!hasSelection()">
                <span class="text-sm text-muted-foreground">
                  {{ selectionCount() }} {{ selectionCount() > 1 ? 'engines' : 'engine' }} selected
                </span>
                <div class="ml-auto flex items-center gap-2">
                  <button hlmBtn variant="outline" size="sm" (click)="clearSelection()">
                    Clear selection
                  </button>
                  <hlm-alert-dialog>
                    <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                      <ng-icon hlm name="lucideTrash2" size="sm" />
                      Delete
                    </button>
                    <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                      <div hlmAlertDialogHeader>
                        <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} {{ selectionCount() > 1 ? 'engines' : 'engine' }}?</h2>
                        <p hlmAlertDialogDescription>
                          This action cannot be undone. The selected {{ selectionCount() > 1 ? 'engines' : 'engine' }} will be permanently deleted.
                        </p>
                      </div>
                      <div hlmAlertDialogFooter>
                        <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                        <button hlmAlertDialogAction variant="destructive" (click)="onBulkDelete(ctx)" [disabled]="deleting()">
                          {{ deleting() ? 'Deleting...' : 'Delete' }}
                        </button>
                      </div>
                    </hlm-alert-dialog-content>
                  </hlm-alert-dialog>
                </div>
              </div>
            </div>

            @if (d.items.length === 0) {
              <div hlmEmpty>
                <div hlmEmptyHeader>
                  <div hlmEmptyMedia variant="icon">
                    <ng-icon name="lucideSearch" />
                  </div>
                  <div hlmEmptyTitle>No results found</div>
                  <div hlmEmptyDescription>No engines match your search. Try a different term.</div>
                </div>
              </div>
            } @else {
              <div class="min-h-0 flex-1 overflow-y-auto p-px">
                <div class="overflow-clip rounded-md border">
                  <div hlmTableContainer>
                    <table hlmTable>
                      <thead hlmTHead class="sticky top-0 z-10 bg-background">
                        <tr hlmTr>
                          <th hlmTh class="w-10">
                            <hlm-checkbox [checked]="allSelected()" [indeterminate]="indeterminate()"
                              (checkedChange)="toggleAll($event)" aria-label="Select all engines"
                              (click)="$event.stopPropagation()" />
                          </th>
                          <th hlmTh>Name</th>
                          <th hlmTh class="w-36">Engine Type</th>
                          <th hlmTh class="hidden md:table-cell w-40">Created</th>
                        </tr>
                      </thead>
                      <tbody hlmTBody>
                        @for (engine of d.items; track engine.id) {
                          <tr hlmTr tabindex="0" (click)="onEngineClick(engine)"
                            (keydown.enter)="onEngineClick(engine)" class="cursor-pointer">
                            <td hlmTd class="w-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
                              @if (!engine.isBuiltIn) {
                                <hlm-checkbox [checked]="isSelected(engine.id)"
                                  (checkedChange)="toggleSelection(engine.id, $event)"
                                  [attr.aria-label]="'Select ' + engine.name" />
                              }
                            </td>
                            <td hlmTd class="font-medium">
                              {{ engine.name }}
                              @if (engine.isBuiltIn) {
                                <span hlmBadge variant="outline" class="ml-2">Built-in</span>
                              }
                            </td>
                            <td hlmTd>
                              <span hlmBadge variant="secondary">{{ getEngineTypeLabel(engine.engineType) }}</span>
                            </td>
                            <td hlmTd class="hidden md:table-cell text-muted-foreground">
                              {{ engine.isBuiltIn ? '—' : (engine.createdAt | date: 'mediumDate') }}
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
              <div class="shrink-0">
                <app-pagination [page]="+d.page" [pageSize]="+d.pageSize" [totalCount]="+d.totalCount"
                  [totalPages]="+(d.totalPages ?? 1)" (pageChange)="onPageChange($event)" />
              </div>
            }
          }
        }
      }
    </div>

    <app-edit-text-extraction-engine-sheet
      [engine]="selectedEngine()"
      [state]="sheetState()"
      (saved)="onEngineSaved()"
      (closed)="onSheetClosed()" />
  `,
})
export class TextExtractionEngineListComponent implements OnDestroy {
  private adminApi = inject(AdminApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private errorHandler = inject(ErrorHandlerService);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);

  selectedEngine = signal<TextExtractionEngineResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  // Multi-selection
  selectedIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Checkbox state for select-all
  private selectableItems = computed(() => {
    const d = this.data();
    return d?.items.filter((e: TextExtractionEngineResponse) => !e.isBuiltIn) ?? [];
  });

  allSelected = computed(() => {
    const selectable = this.selectableItems();
    return selectable.length > 0 && selectable.every((e: TextExtractionEngineResponse) => this.selectedIds().has(e.id));
  });

  indeterminate = computed(() => {
    const selectable = this.selectableItems();
    const selected = this.selectedIds();
    const count = selectable.filter((e: TextExtractionEngineResponse) => selected.has(e.id)).length;
    return count > 0 && count < selectable.length;
  });

  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  private page = toSignal(
    this.route.queryParamMap.pipe(map(params => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map(params => +(params.get('pageSize') ?? 50))),
    { initialValue: 50 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.searchTerm),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([page, pageSize, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.adminApi.getTextExtractionEngines({
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r as PaginatedResponseOfTextExtractionEngineResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  private searchSub: Subscription;

  constructor() {
    this.searchSub = toObservable(this.searchInput)
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      )
      .subscribe(value => {
        this.router.navigate(['/admin/text-extraction'], {
          queryParams: { page: 1, search: value || null },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      });
  }

  ngOnDestroy(): void {
    this.searchSub.unsubscribe();
  }

  getEngineTypeLabel(engineType: string): string {
    return ENGINE_TYPE_LABELS[engineType] ?? engineType;
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  toggleSelection(id: string, checked: boolean): void {
    const next = new Set(this.selectedIds());
    if (checked) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.selectedIds.set(next);
  }

  toggleAll(checked: boolean): void {
    if (checked) {
      const next = new Set(this.selectedIds());
      for (const e of this.selectableItems()) {
        next.add(e.id);
      }
      this.selectedIds.set(next);
    } else {
      this.selectedIds.set(new Set());
    }
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;
    this.deleting.set(true);

    const result$ = this.adminApi.bulkDeleteTextExtractionEngines({
      body: { ids },
    }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete engines. Please try again.'),
    );

    toast.promise(
      firstValueFrom(result$).then((response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +(response as BulkDeleteTextExtractionEnginesResponse).deletedCount;
        this.clearSelection();
        this.onRefresh();
        return `${count} ${count > 1 ? 'engines' : 'engine'} deleted.`;
      }),
      {
        loading: 'Deleting engines...',
        success: (message) => message,
      },
    );
  }

  onPageChange(newPage: number): void {
    this.router.navigate(['/admin/text-extraction'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onEngineClick(engine: TextExtractionEngineResponse): void {
    this.selectedEngine.set(engine);
    this.sheetState.set('open');
  }

  onEngineSaved(): void {
    this.sheetState.set('closed');
    this.selectedEngine.set(null);
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedEngine.set(null);
  }

  onRefresh(): void {
    this.refreshTrigger.update(v => v + 1);
  }
}
