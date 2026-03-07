import { Component, ChangeDetectionStrategy, inject, signal, computed, DestroyRef } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@/shared/services/auth.service';
import { switchMap, map, catchError, of, combineLatest, debounceTime, distinctUntilChanged, filter } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTrash2, lucideSearch, lucideTable, lucideLayoutGrid, lucideX, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown, lucideListChecks, lucideRotateCcw } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { DocumentTableComponent, HIDEABLE_COLUMNS } from '../../components/document-table/document-table.component';
import { DocumentCardGridComponent } from '../../components/document-card-grid/document-card-grid.component';
import type { VisibilityState } from '@tanstack/angular-table';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import { bulkRestoreDocuments } from '@/api/generated/fn/documents/bulk-restore-documents';
import { bulkPermanentlyDeleteDocuments } from '@/api/generated/fn/documents/bulk-permanently-delete-documents';
import { emptyTrash } from '@/api/generated/fn/documents/empty-trash';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';
import type { DocumentSortBy } from '@/api/generated/models/document-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-trash-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmButton,
    HlmButtonGroup,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    DocumentTableComponent,
    DocumentCardGridComponent,
    NgTemplateOutlet,
    PaginationComponent,
  ],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  providers: [provideIcons({ lucideTrash2, lucideSearch, lucideTable, lucideLayoutGrid, lucideX, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown, lucideListChecks, lucideRotateCcw })],
  template: `
    <div class="flex min-h-0 flex-1 flex-col">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideTrash2" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load trash</h3>
            <p hlmEmptyDescription>There was an error loading the trash. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
          <section hlmEmpty [class.hidden]="!showInitialEmpty()">
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideTrash2" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>Trash is empty</h3>
              <p hlmEmptyDescription>Items you delete will appear here.</p>
            </div>
          </section>
          <div class="relative shrink-0" [class.hidden]="showInitialEmpty()">
            <!-- Normal toolbar -->
            <div [class.opacity-0]="isInSelectionState()" [class.pointer-events-none]="isInSelectionState()" [attr.inert]="isInSelectionState() ? '' : null">
              <div class="flex items-center gap-2 mb-3">
                <div class="relative flex-1 min-w-0">
                  <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
                  <input hlmInput placeholder="Search trash..." class="pl-8 h-8 w-full min-w-0" aria-label="Search trash" [value]="searchInput()" (input)="searchInput.set($any($event.target).value)" />
                </div>
                <div class="flex items-center gap-2">
                  <button hlmBtn variant="outline" size="sm" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="sortMenu">
                    <ng-icon hlm [name]="effectiveSortDir() === 'Asc' ? 'lucideArrowUp' : 'lucideArrowDown'" size="sm" />
                    {{ sortLabel() }}
                  </button>
                  <ng-template #sortGroup>
                    <span hlmDropdownMenuLabel>Sort by</span>
                    <hlm-dropdown-menu-group>
                      @if (isSearching()) {
                        <button hlmDropdownMenuRadio [checked]="!sortBy()" (triggered)="onSortByChange(null)">
                          <hlm-dropdown-menu-radio-indicator />
                          Relevance
                        </button>
                      }
                      @for (opt of sortOptions; track opt.value) {
                        <button hlmDropdownMenuRadio [checked]="sortBy() === opt.value" (triggered)="onSortByChange(opt.value)">
                          <hlm-dropdown-menu-radio-indicator />
                          {{ opt.label }}
                        </button>
                      }
                    </hlm-dropdown-menu-group>
                    <hlm-dropdown-menu-separator />
                    <span hlmDropdownMenuLabel>Direction</span>
                    <hlm-dropdown-menu-group>
                      <button hlmDropdownMenuRadio [checked]="effectiveSortDir() === 'Asc'" (triggered)="onSortDirChange('Asc')">
                        <hlm-dropdown-menu-radio-indicator />
                        Ascending
                      </button>
                      <button hlmDropdownMenuRadio [checked]="effectiveSortDir() === 'Desc'" (triggered)="onSortDirChange('Desc')">
                        <hlm-dropdown-menu-radio-indicator />
                        Descending
                      </button>
                    </hlm-dropdown-menu-group>
                  </ng-template>
                  <ng-template #sortMenu>
                    <hlm-dropdown-menu class="w-44">
                      <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
                    </hlm-dropdown-menu>
                  </ng-template>
                  @if (viewMode() === 'table') {
                  <button hlmBtn variant="outline" size="sm" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="columnsMenu">
                    <ng-icon hlm name="lucideSlidersHorizontal" size="sm" />
                    Columns
                  </button>
                  }
                  <ng-template #columnsContent>
                    @for (col of hideableColumns; track col.id) {
                      <button hlmDropdownMenuCheckbox [checked]="columnVisibility()[col.id] !== false" (triggered)="onToggleColumn(col.id)">
                        <hlm-dropdown-menu-checkbox-indicator />
                        {{ col.label }}
                      </button>
                    }
                  </ng-template>
                  <ng-template #columnsMenu>
                    <hlm-dropdown-menu class="w-40">
                      <ng-container [ngTemplateOutlet]="columnsContent" [ngTemplateOutletInjector]="'outlet'" />
                    </hlm-dropdown-menu>
                  </ng-template>
                  <div hlmButtonGroup class="hidden sm:flex" aria-label="View mode">
                    <button hlmBtn [variant]="viewMode() === 'grid' ? 'secondary' : 'outline'" size="icon-sm" (click)="setViewMode('grid')" aria-label="Grid view">
                      <ng-icon hlm name="lucideLayoutGrid" size="sm" />
                    </button>
                    <button hlmBtn [variant]="viewMode() === 'table' ? 'secondary' : 'outline'" size="icon-sm" (click)="setViewMode('table')" aria-label="Table view">
                      <ng-icon hlm name="lucideTable" size="sm" />
                    </button>
                  </div>
                  @if (auth.canWrite() && viewMode() === 'grid') {
                    <button hlmBtn variant="outline" size="sm" class="hidden sm:inline-flex" (click)="enterSelectionMode()" aria-label="Enter selection mode">
                      <ng-icon hlm name="lucideListChecks" size="sm" />
                      Select
                    </button>
                  }
                  @if (auth.canWrite()) {
                    <hlm-alert-dialog>
                      <button hlmBtn variant="destructive" size="sm" class="hidden sm:inline-flex" hlmAlertDialogTrigger>
                        <ng-icon hlm name="lucideTrash2" size="sm" />
                        Empty Trash
                      </button>
                      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                        <div hlmAlertDialogHeader>
                          <h2 hlmAlertDialogTitle>Empty trash?</h2>
                          <p hlmAlertDialogDescription>
                            This will permanently delete all items in the trash. This action cannot be undone.
                          </p>
                        </div>
                        <div hlmAlertDialogFooter>
                          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                          <button hlmAlertDialogAction variant="destructive" (click)="onEmptyTrash(ctx)" [disabled]="emptyingTrash()">
                            {{ emptyingTrash() ? 'Emptying...' : 'Empty Trash' }}
                          </button>
                        </div>
                      </hlm-alert-dialog-content>
                    </hlm-alert-dialog>
                  }
                  <!-- Mobile: Overflow menu (below sm) -->
                  <button hlmBtn variant="outline" size="icon-sm" class="sm:hidden"
                          [hlmDropdownMenuTrigger]="overflowMenu" aria-label="More options">
                    <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
                  </button>
                  <ng-template #overflowMenu>
                    <hlm-dropdown-menu class="w-44">
                      <span hlmDropdownMenuLabel>View</span>
                      <hlm-dropdown-menu-group>
                        <button hlmDropdownMenuRadio [checked]="viewMode() === 'grid'"
                                (triggered)="setViewMode('grid')">
                          <hlm-dropdown-menu-radio-indicator />
                          Grid
                        </button>
                        <button hlmDropdownMenuRadio [checked]="viewMode() === 'table'"
                                (triggered)="setViewMode('table')">
                          <hlm-dropdown-menu-radio-indicator />
                          Table
                        </button>
                      </hlm-dropdown-menu-group>
                      <hlm-dropdown-menu-separator />
                      <button hlmDropdownMenuItem [hlmDropdownMenuTrigger]="sortSubMenu">
                        Sort
                        <hlm-dropdown-menu-item-sub-indicator />
                      </button>
                      <ng-template #sortSubMenu>
                        <hlm-dropdown-menu-sub>
                          <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
                        </hlm-dropdown-menu-sub>
                      </ng-template>
                      @if (viewMode() === 'table') {
                        <button hlmDropdownMenuItem [hlmDropdownMenuTrigger]="columnsSubMenu">
                          Columns
                          <hlm-dropdown-menu-item-sub-indicator />
                        </button>
                        <ng-template #columnsSubMenu>
                          <hlm-dropdown-menu-sub>
                            <ng-container [ngTemplateOutlet]="columnsContent" [ngTemplateOutletInjector]="'outlet'" />
                          </hlm-dropdown-menu-sub>
                        </ng-template>
                      }
                      @if (auth.canWrite() && viewMode() === 'grid') {
                        <hlm-dropdown-menu-separator />
                        <button hlmDropdownMenuItem (triggered)="enterSelectionMode()">
                          Select
                        </button>
                      }
                    </hlm-dropdown-menu>
                  </ng-template>
                </div>
              </div>
              @if (hasActiveFilters()) {
                <div class="flex flex-wrap items-center gap-2 mb-4">
                  <button hlmBtn variant="ghost" size="sm" (click)="resetFilters()">
                    Reset
                    <ng-icon hlm name="lucideX" size="xs" />
                  </button>
                </div>
              }
            </div>

            <!-- Selection toolbar: absolute overlay -->
            <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!isInSelectionState() || !auth.canWrite()" [class.pointer-events-none]="!isInSelectionState() || !auth.canWrite()" [attr.inert]="!isInSelectionState() || !auth.canWrite() ? '' : null">
              <button hlmBtn variant="ghost" size="icon-sm" (click)="clearSelection()" aria-label="Exit selection mode">
                <ng-icon hlm name="lucideX" size="sm" />
              </button>
              @if (hasSelection()) {
                <span class="text-sm text-muted-foreground">
                  {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }} selected
                </span>
                @if (!allPageSelected()) {
                  <button hlmBtn variant="link" size="sm" (click)="selectAllOnPage()">
                    Select all
                  </button>
                }
                <div class="ml-auto flex items-center gap-2">
                  <button hlmBtn variant="outline" size="sm" (click)="onBulkRestore()" [disabled]="restoring()">
                    <ng-icon hlm name="lucideRotateCcw" size="sm" />
                    {{ restoring() ? 'Restoring...' : 'Restore' }}
                  </button>
                  <hlm-alert-dialog>
                    <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                      <ng-icon hlm name="lucideTrash2" size="sm" />
                      Delete Permanently
                    </button>
                    <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                      <div hlmAlertDialogHeader>
                        <h2 hlmAlertDialogTitle>Permanently delete {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                        <p hlmAlertDialogDescription>
                          This action cannot be undone. The selected document{{ selectionCount() > 1 ? 's' : '' }}
                          and {{ selectionCount() > 1 ? 'their' : 'its' }} associated files will be permanently deleted.
                        </p>
                      </div>
                      <div hlmAlertDialogFooter>
                        <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                        <button hlmAlertDialogAction variant="destructive" (click)="onBulkPermanentlyDelete(ctx)" [disabled]="deleting()">
                          {{ deleting() ? 'Deleting...' : 'Delete Permanently' }}
                        </button>
                      </div>
                    </hlm-alert-dialog-content>
                  </hlm-alert-dialog>
                </div>
              } @else {
                <span class="text-sm text-muted-foreground">Select items</span>
              }
            </div>
          </div>

          <section hlmEmpty [class.hidden]="!showFilteredEmpty()">
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucideTrash2" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No matching documents</h3>
                <p hlmEmptyDescription>Try adjusting your search to find what you're looking for.</p>
              </div>
              <button hlmBtn variant="outline" size="sm" (click)="resetFilters()">
                Reset filters
              </button>
          </section>
          <div class="flex min-h-0 flex-1 flex-col overflow-y-auto p-px" [class.hidden]="!hasItems()">
              @if (viewMode() === 'table') {
                <app-document-table
                  [documents]="result.items"
                  [selectedIds]="selectedDocumentIds()"
                  [sortBy]="sortBy()"
                  [sortDir]="sortDir()"
                  [columnVisibility]="columnVisibility()"
                  (documentSelect)="onDocumentSelect($event)"
                  (selectionChange)="selectedDocumentIds.set($event)"
                  (sortChange)="onSortChange($event)"
                />
              } @else {
                <app-document-card-grid
                  [documents]="result.items"
                  [selectedIds]="selectedDocumentIds()"
                  [selectionMode]="true"
                  (documentSelect)="onDocumentSelect($event)"
                  (selectionChange)="selectedDocumentIds.set($event)"
                />
              }
              <ng-template #paginationTpl>
                <app-pagination
                  [page]="+result.page"
                  [totalPages]="+(result.totalPages ?? 0)"
                  [totalCount]="+result.totalCount"
                  [pageSize]="+result.pageSize"
                  (pageChange)="onPageChange($event)"
                  (pageSizeChange)="onPageSizeChange($event)"
                />
              </ng-template>
              <div class="mt-auto sm:hidden">
                <ng-container [ngTemplateOutlet]="paginationTpl" />
              </div>
          </div>
          <div class="shrink-0 max-sm:hidden" [class.hidden]="!hasItems()">
              <ng-container [ngTemplateOutlet]="paginationTpl" />
          </div>
      }

    </div>
  `,
})
export class TrashListComponent {
  auth = inject(AuthService);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  private static readonly VIEW_MODE_KEY = 'trash-view-mode';

  hideableColumns = HIDEABLE_COLUMNS;
  readonly sortOptions: { value: DocumentSortBy; label: string }[] = [
    { value: 'DeletedAt', label: 'Date Deleted' },
    { value: 'CreatedAt', label: 'Date Added' },
    { value: 'Name', label: 'Name' },
    { value: 'ContentType', label: 'Content Type' },
    { value: 'FileSize', label: 'File Size' },
  ];
  viewMode = signal<'table' | 'grid'>(this.loadViewMode());
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);
  restoring = signal(false);
  emptyingTrash = signal(false);

  selectedDocumentIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedDocumentIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);
  selectionMode = signal(false);
  isInSelectionState = computed(() => this.selectionMode() || this.selectionCount() > 0);
  allPageSelected = computed(() => {
    const items = this.data()?.items;
    if (!items || items.length === 0) return true;
    const selected = this.selectedDocumentIds();
    return items.every(d => selected.has(d.id));
  });

  // Search
  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  // Column visibility
  columnVisibility = signal<VisibilityState>({});

  // Sort state from query params
  sortBy = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortBy') as DocumentSortBy | null)),
  );

  sortDir = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortDir') as SortDirection | null)),
  );

  isSearching = computed(() => this.searchTerm().length > 0);

  effectiveSortBy = computed(() => {
    const explicit = this.sortBy();
    if (explicit) return explicit;
    return this.isSearching() ? null : 'DeletedAt' as DocumentSortBy;
  });

  private static readonly DATE_SORT_FIELDS: Set<DocumentSortBy> = new Set(['CreatedAt', 'DeletedAt']);

  effectiveSortDir = computed(() => {
    const explicit = this.sortDir();
    if (explicit) return explicit;
    const sortBy = this.effectiveSortBy();
    if (!sortBy) return 'Desc' as SortDirection;
    return TrashListComponent.DATE_SORT_FIELDS.has(sortBy) ? 'Desc' as SortDirection : 'Asc' as SortDirection;
  });

  sortLabel = computed(() => {
    const sortBy = this.effectiveSortBy();
    if (!sortBy) return 'Relevance';
    const option = this.sortOptions.find(o => o.value === sortBy);
    return option?.label ?? sortBy;
  });

  hasActiveFilters = computed(() => this.searchTerm().length > 0);

  hasItems = computed(() => (this.data()?.items?.length ?? 0) > 0);
  showInitialEmpty = computed(() => !this.hasItems() && !this.hasActiveFilters() && !this.loading());
  showFilteredEmpty = computed(() => !this.hasItems() && this.hasActiveFilters());

  constructor() {
    toObservable(this.searchInput).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      takeUntilDestroyed(),
    ).subscribe(value => {
      this.router.navigate(['/documents/trash'], {
        queryParams: { page: 1, search: value || null },
        queryParamsHandling: 'merge',
      });
    });
  }

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? 10))),
    { initialValue: 10 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.refreshTrigger),
      toObservable(this.sortBy),
      toObservable(this.sortDir),
      toObservable(this.searchTerm),
    ]).pipe(
      switchMap(([page, pageSize, , sortBy, sortDir, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return getDocuments(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
          Deleted: true,
          IncludeChildTags: false,
          SortBy: sortBy || undefined,
          SortDir: sortDir || undefined,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGetDocumentsResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            this.error.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  onPageChange(newPage: number): void {
    this.clearSelection();
    this.router.navigate(['/documents/trash'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
    });
  }

  onPageSizeChange(newSize: number): void {
    this.clearSelection();
    this.router.navigate(['/documents/trash'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
    });
  }

  setViewMode(mode: 'table' | 'grid'): void {
    this.viewMode.set(mode);
    localStorage.setItem(TrashListComponent.VIEW_MODE_KEY, mode);
  }

  private loadViewMode(): 'table' | 'grid' {
    const stored = localStorage.getItem(TrashListComponent.VIEW_MODE_KEY);
    return stored === 'table' || stored === 'grid' ? stored : 'grid';
  }

  onDocumentSelect(id: string): void {
    this.selectedDocumentIds.update(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  onSortChange(event: { sortBy: DocumentSortBy; sortDir: SortDirection } | null): void {
    this.clearSelection();
    this.router.navigate(['/documents/trash'], {
      queryParams: {
        page: 1,
        sortBy: event?.sortBy ?? null,
        sortDir: event?.sortDir ?? null,
      },
      queryParamsHandling: 'merge',
    });
  }

  onSortByChange(field: DocumentSortBy | null): void {
    if (!field) {
      this.onSortChange(null);
    } else {
      const defaultDir: SortDirection = TrashListComponent.DATE_SORT_FIELDS.has(field) ? 'Desc' : 'Asc';
      this.onSortChange({ sortBy: field, sortDir: this.sortDir() ?? defaultDir });
    }
  }

  onSortDirChange(dir: SortDirection): void {
    const field = this.sortBy();
    if (field) {
      this.onSortChange({ sortBy: field, sortDir: dir });
    }
  }

  onToggleColumn(id: string): void {
    this.columnVisibility.update(prev => ({
      ...prev,
      [id]: prev[id] === false,
    }));
  }

  resetFilters(): void {
    this.clearSelection();
    this.searchInput.set('');
    this.router.navigate(['/documents/trash'], {
      queryParams: { page: 1, search: null },
      queryParamsHandling: 'merge',
    });
  }

  enterSelectionMode(): void {
    this.selectionMode.set(true);
  }

  clearSelection(): void {
    this.selectedDocumentIds.set(new Set());
    this.selectionMode.set(false);
  }

  selectAllOnPage(): void {
    const items = this.data()?.items;
    if (!items) return;
    this.selectedDocumentIds.set(new Set(items.map(d => d.id)));
  }

  onBulkRestore(): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.restoring.set(true);

    bulkRestoreDocuments(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.restoring.set(false);
        const count = +response.body.restoredCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} restored.`);
        this.clearSelection();
        this.refreshTrigger.update(v => v + 1);
      },
      error: () => {
        this.restoring.set(false);
        toast.error('Failed to restore documents. Please try again.');
      },
    });
  }

  onBulkPermanentlyDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkPermanentlyDeleteDocuments(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} permanently deleted.`);
        this.clearSelection();
        this.refreshTrigger.update(v => v + 1);
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete documents. Please try again.');
      },
    });
  }

  onEmptyTrash(ctx: { close: () => void }): void {
    this.emptyingTrash.set(true);

    emptyTrash(this.http, this.apiConfig.rootUrl).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.emptyingTrash.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} permanently deleted.`);
        this.clearSelection();
        this.refreshTrigger.update(v => v + 1);
      },
      error: () => {
        this.emptyingTrash.set(false);
        toast.error('Failed to empty trash. Please try again.');
      },
    });
  }
}
