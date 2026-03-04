import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '@/shared/services/auth.service';
import { switchMap, map, catchError, of, combineLatest, Subscription, debounceTime, distinctUntilChanged, filter } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucidePlus, lucideSearch, lucideLayoutList, lucideLayoutGrid, lucideTrash2, lucideX, lucideSlidersHorizontal, lucideEllipsisVertical } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { FilterPopoverComponent } from '@/shared/components/filter-popover/filter-popover.component';
import type { FilterOption } from '@/shared/components/filter-popover/filter-popover.component';
import { DocumentTableComponent, HIDEABLE_COLUMNS } from '../../components/document-table/document-table.component';
import { DocumentCardGridComponent } from '../../components/document-card-grid/document-card-grid.component';
import type { VisibilityState } from '@tanstack/angular-table';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { UploadDialogComponent } from '../../components/upload-dialog/upload-dialog.component';
import { GroupSelectComponent } from '../../components/group-select/group-select.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import { bulkDeleteDocuments } from '@/api/generated/fn/documents/bulk-delete-documents';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';
import type { DocumentSortBy } from '@/api/generated/models/document-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-document-list',
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
    FilterPopoverComponent,
    DocumentTableComponent,
    DocumentCardGridComponent,
    PaginationComponent,
    UploadDialogComponent,
    GroupSelectComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucidePlus, lucideSearch, lucideLayoutList, lucideLayoutGrid, lucideTrash2, lucideX, lucideSlidersHorizontal, lucideEllipsisVertical })],
  template: `
    <div>
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideFileText" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load documents</h3>
            <p hlmEmptyDescription>There was an error loading the document list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
        @if (result.items.length === 0 && !hasActiveFilters()) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideFileText" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No documents yet</h3>
              <p hlmEmptyDescription>Upload your first document to get started.</p>
            </div>
            @if (auth.canWrite()) {
              <app-upload-dialog (uploaded)="onRefresh()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                Upload Document
              </app-upload-dialog>
            }
          </section>
        } @else {
          <div class="relative">
            <!-- Normal toolbar: always rendered to hold layout height -->
            <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()">
              <div class="flex items-center gap-2 mb-3">
                <div class="relative flex-1 min-w-0">
                  <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
                  <input hlmInput placeholder="Search documents..." class="pl-8 h-8 w-full min-w-0" aria-label="Search documents" [value]="searchInput()" (input)="searchInput.set($any($event.target).value)" />
                </div>
                <div class="flex items-center gap-2">
                  @if (viewMode() === 'table') {
                  <button hlmBtn variant="outline" size="sm" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="columnsMenu">
                    <ng-icon hlm name="lucideSlidersHorizontal" size="sm" />
                    Columns
                  </button>
                  }
                  <ng-template #columnsMenu>
                    <hlm-dropdown-menu class="w-40">
                      @for (col of hideableColumns; track col.id) {
                        <button hlmDropdownMenuCheckbox [checked]="columnVisibility()[col.id] !== false" (triggered)="onToggleColumn(col.id)">
                          <hlm-dropdown-menu-checkbox-indicator />
                          {{ col.label }}
                        </button>
                      }
                    </hlm-dropdown-menu>
                  </ng-template>
                  <div hlmButtonGroup class="hidden sm:flex" aria-label="View mode">
                    <button hlmBtn [variant]="viewMode() === 'table' ? 'secondary' : 'outline'" size="icon-sm" (click)="setViewMode('table')" aria-label="Table view">
                      <ng-icon hlm name="lucideLayoutList" size="sm" />
                    </button>
                    <button hlmBtn [variant]="viewMode() === 'grid' ? 'secondary' : 'outline'" size="icon-sm" (click)="setViewMode('grid')" aria-label="Grid view">
                      <ng-icon hlm name="lucideLayoutGrid" size="sm" />
                    </button>
                  </div>
                  <!-- Mobile: Overflow menu (below sm) -->
                  <button hlmBtn variant="outline" size="icon-sm" class="sm:hidden"
                          [hlmDropdownMenuTrigger]="overflowMenu" aria-label="More options">
                    <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
                  </button>
                  <ng-template #overflowMenu>
                    <hlm-dropdown-menu class="w-44">
                      <span hlmDropdownMenuLabel>View</span>
                      <hlm-dropdown-menu-group>
                        <button hlmDropdownMenuRadio [checked]="viewMode() === 'table'"
                                (triggered)="setViewMode('table')">
                          <hlm-dropdown-menu-radio-indicator />
                          Table
                        </button>
                        <button hlmDropdownMenuRadio [checked]="viewMode() === 'grid'"
                                (triggered)="setViewMode('grid')">
                          <hlm-dropdown-menu-radio-indicator />
                          Grid
                        </button>
                      </hlm-dropdown-menu-group>
                      @if (viewMode() === 'table') {
                        <hlm-dropdown-menu-separator />
                        <span hlmDropdownMenuLabel>Columns</span>
                        <hlm-dropdown-menu-group>
                          @for (col of hideableColumns; track col.id) {
                            <button hlmDropdownMenuCheckbox
                                    [checked]="columnVisibility()[col.id] !== false"
                                    (triggered)="onToggleColumn(col.id)">
                              <hlm-dropdown-menu-checkbox-indicator />
                              {{ col.label }}
                            </button>
                          }
                        </hlm-dropdown-menu-group>
                      }
                    </hlm-dropdown-menu>
                  </ng-template>
                  @if (auth.canWrite()) {
                    <app-upload-dialog triggerSize="sm" (uploaded)="onRefresh()">
                      <ng-icon hlm name="lucidePlus" size="sm" />
                      Upload
                    </app-upload-dialog>
                  }
                </div>
              </div>
              <div class="flex flex-wrap items-center gap-2 mb-4">
                <app-filter-popover
                  label="Parties"
                  [options]="partyOptions()"
                  [selectedIds]="selectedPartyIds()"
                  searchPlaceholder="Search parties..."
                  [loading]="partyOptionsLoading()"
                  (selectionChange)="onPartyFilterChange($event)"
                  (searchChange)="onPartySearchChange($event)"
                />
                <app-filter-popover
                  label="Tags"
                  [options]="tagOptions()"
                  [selectedIds]="selectedTagIds()"
                  searchPlaceholder="Search tags..."
                  [loading]="tagOptionsLoading()"
                  [showIncludeChildren]="true"
                  [includeChildren]="includeChildTags()"
                  includeChildrenLabel="Include sub-tags"
                  (selectionChange)="onTagFilterChange($event)"
                  (searchChange)="onTagSearchChange($event)"
                  (includeChildrenChange)="onIncludeChildTagsChange($event)"
                />
                <app-group-select [value]="selectedGroupId()" (valueChange)="onGroupFilterChange($event)" />
                @if (hasActiveFilters()) {
                  <button hlmBtn variant="ghost" size="sm" (click)="resetFilters()">
                    Reset
                    <ng-icon hlm name="lucideX" size="xs" />
                  </button>
                }
              </div>
            </div>

            <!-- Selection toolbar: absolute overlay -->
            <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!hasSelection() || !auth.canWrite()" [class.pointer-events-none]="!hasSelection() || !auth.canWrite()">
              <span class="text-sm text-muted-foreground">
                {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }} selected
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
                      <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                      <p hlmAlertDialogDescription>
                        This action cannot be undone. The selected document{{ selectionCount() > 1 ? 's' : '' }}
                        and {{ selectionCount() > 1 ? 'their' : 'its' }} associated files will be permanently deleted.
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

          @if (result.items.length === 0) {
            <section hlmEmpty>
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucideFileText" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No matching documents</h3>
                <p hlmEmptyDescription>Try adjusting your search or filters to find what you're looking for.</p>
              </div>
              <button hlmBtn variant="outline" size="sm" (click)="resetFilters()">
                Reset filters
              </button>
            </section>
          } @else {
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
                (documentSelect)="onDocumentSelect($event)"
                (selectionChange)="selectedDocumentIds.set($event)"
              />
            }
            <app-pagination
              [page]="+result.page"
              [totalPages]="+(result.totalPages ?? 0)"
              [totalCount]="+result.totalCount"
              [pageSize]="+result.pageSize"
              (pageChange)="onPageChange($event)"
              (pageSizeChange)="onPageSizeChange($event)"
            />
          }
        }
      }

    </div>
  `,
})
export class DocumentListComponent implements OnDestroy {
  auth = inject(AuthService);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private hubSub: Subscription;

  private static readonly VIEW_MODE_KEY = 'document-view-mode';

  hideableColumns = HIDEABLE_COLUMNS;
  viewMode = signal<'table' | 'grid'>(this.loadViewMode());
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);

  selectedDocumentIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedDocumentIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Search
  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  // Column visibility (TanStack VisibilityState: { [columnId]: boolean })
  columnVisibility = signal<VisibilityState>({});

  // Party filter
  partyFilterSearch = signal('');
  partyOptionsLoading = signal(false);

  selectedPartyIds = toSignal(
    this.route.queryParamMap.pipe(map(p => p.getAll('partyIds'))),
    { initialValue: [] as string[] },
  );

  // Tag filter
  tagFilterSearch = signal('');
  tagOptionsLoading = signal(false);

  selectedTagIds = toSignal(
    this.route.queryParamMap.pipe(map(p => p.getAll('tagIds'))),
    { initialValue: [] as string[] },
  );

  includeChildTags = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('includeChildTags') === 'true')),
    { initialValue: false },
  );

  // Group filter
  selectedGroupId = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('groupId') ?? '')),
    { initialValue: '' },
  );

  // Sort state from query params
  sortBy = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortBy') as DocumentSortBy | null)),
  );

  sortDir = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortDir') as SortDirection | null)),
  );

  hasActiveFilters = computed(() =>
    this.searchTerm().length > 0 ||
    this.selectedPartyIds().length > 0 ||
    this.selectedTagIds().length > 0 ||
    this.selectedGroupId().length > 0 ||
    this.sortBy() !== null,
  );

  partyOptions = toSignal(
    toObservable(this.partyFilterSearch).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(search => {
        this.partyOptionsLoading.set(true);
        return getParties(this.http, this.apiConfig.rootUrl, {
          Search: search || undefined,
          Page: 1,
          PageSize: 100,
        }).pipe(
          map(r => {
            this.partyOptionsLoading.set(false);
            const items = r.body?.items ?? [];
            return items.map(p => ({ id: p.id, label: p.name } as FilterOption));
          }),
          catchError(() => {
            this.partyOptionsLoading.set(false);
            return of([] as FilterOption[]);
          }),
        );
      }),
    ),
    { initialValue: [] as FilterOption[] },
  );

  tagOptions = toSignal(
    toObservable(this.tagFilterSearch).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(search => {
        this.tagOptionsLoading.set(true);
        return getTags(this.http, this.apiConfig.rootUrl, {
          Search: search || undefined,
          IncludeChildren: false,
        }).pipe(
          map(r => {
            this.tagOptionsLoading.set(false);
            const items = r.body ?? [];
            return items.map(t => ({ id: t.id, label: t.path } as FilterOption));
          }),
          catchError(() => {
            this.tagOptionsLoading.set(false);
            return of([] as FilterOption[]);
          }),
        );
      }),
    ),
    { initialValue: [] as FilterOption[] },
  );


  private searchSub: Subscription;

  constructor() {
    this.hubSub = this.processingHub.jobCompleted$.subscribe(() => {
      this.refreshTrigger.update((v) => v + 1);
    });

    this.searchSub = toObservable(this.searchInput).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
    ).subscribe(value => {
      this.router.navigate(['/documents'], {
        queryParams: { page: 1, search: value || null },
        queryParamsHandling: 'merge',
      });
    });
  }

  ngOnDestroy(): void {
    this.hubSub.unsubscribe();
    this.searchSub.unsubscribe();
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
      toObservable(this.selectedPartyIds),
      toObservable(this.selectedTagIds),
      toObservable(this.includeChildTags),
      toObservable(this.sortBy),
      toObservable(this.sortDir),
      toObservable(this.searchTerm),
      toObservable(this.selectedGroupId),
    ]).pipe(
      switchMap(([page, pageSize, , partyIds, tagIds, includeChildTags, sortBy, sortDir, search, groupId]) => {
        this.loading.set(true);
        this.error.set(false);
        return getDocuments(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
          PartyIds: partyIds.length > 0 ? partyIds : undefined,
          TagIds: tagIds.length > 0 ? tagIds : undefined,
          IncludeChildTags: includeChildTags,
          SortBy: sortBy || undefined,
          SortDir: sortDir || undefined,
          GroupId: groupId || undefined,
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
    this.router.navigate(['/documents'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
    });
  }

  onPageSizeChange(newSize: number): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
    });
  }

  setViewMode(mode: 'table' | 'grid'): void {
    this.viewMode.set(mode);
    localStorage.setItem(DocumentListComponent.VIEW_MODE_KEY, mode);
  }

  private loadViewMode(): 'table' | 'grid' {
    const stored = localStorage.getItem(DocumentListComponent.VIEW_MODE_KEY);
    return stored === 'table' || stored === 'grid' ? stored : 'grid';
  }

  onDocumentSelect(id: string): void {
    this.router.navigate(['/documents', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  onPartyFilterChange(ids: string[]): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, partyIds: ids.length > 0 ? ids : null },
      queryParamsHandling: 'merge',
    });
  }

  onPartySearchChange(search: string): void {
    this.partyFilterSearch.set(search);
  }

  onTagFilterChange(ids: string[]): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, tagIds: ids.length > 0 ? ids : null },
      queryParamsHandling: 'merge',
    });
  }

  onTagSearchChange(search: string): void {
    this.tagFilterSearch.set(search);
  }

  onIncludeChildTagsChange(includeChildren: boolean): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, includeChildTags: includeChildren || null },
      queryParamsHandling: 'merge',
    });
  }

  onGroupFilterChange(groupId: string): void {
    this.router.navigate([], {
      queryParams: { page: 1, groupId: groupId || null },
      queryParamsHandling: 'merge',
    });
  }

  onSortChange(event: { sortBy: DocumentSortBy; sortDir: SortDirection } | null): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: {
        page: 1,
        sortBy: event?.sortBy ?? null,
        sortDir: event?.sortDir ?? null,
      },
      queryParamsHandling: 'merge',
    });
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
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, search: null, partyIds: null, tagIds: null, includeChildTags: null, groupId: null, sortBy: null, sortDir: null },
      queryParamsHandling: 'merge',
    });
  }

  clearSelection(): void {
    this.selectedDocumentIds.set(new Set());
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkDeleteDocuments(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} deleted.`);
        this.clearSelection();
        this.onRefresh();
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete documents. Please try again.');
      },
    });
  }
}
