import { Component, ChangeDetectionStrategy, inject, signal, computed, effect, OnDestroy, DestroyRef, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, ActivatedRoute, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '@/shared/services/auth.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subject, Subscription, debounceTime, distinctUntilChanged, filter, take, takeUntil, takeWhile, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucidePlus, lucideSearch, lucideTable, lucideLayoutGrid, lucideTrash2, lucideX, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown, lucideListChecks, lucideTags, lucideUsers, lucideDownload } from '@ng-icons/lucide';
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
import { DocumentTableComponent, HIDEABLE_COLUMNS, DEFAULT_COLUMN_VISIBILITY } from './document-table/document-table.component';
import { DocumentCardGridComponent } from './document-card-grid/document-card-grid.component';
import type { VisibilityState } from '@tanstack/angular-table';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { UploadManagerService } from '@/shared/services/upload-manager.service';
import { handleFileInputChange, triggerDownload } from '@/shared/utils';
import { DocumentListStateService } from './document-list-state.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { BulkEditTagsPopoverComponent } from './bulk-edit-tags-popover/bulk-edit-tags-popover.component';
import { BulkEditPartiesPopoverComponent } from './bulk-edit-parties-popover/bulk-edit-parties-popover.component';
import { BulkReprocessDialogComponent } from './bulk-reprocess-dialog/bulk-reprocess-dialog.component';
import { BulkReprocessProgressDialogComponent } from './bulk-reprocess-progress-dialog/bulk-reprocess-progress-dialog.component';
import { DownloadProgressDialogComponent } from './download-progress-dialog/download-progress-dialog.component';
import { OpenDocumentsService } from '../detail/open-documents.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { DownloadApiService } from '@/api/services/download-api.service';
import { TagApiService } from '@/api/services/tag-api.service';
import { PartyApiService } from '@/api/services/party-api.service';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';
import type { DocumentSortBy } from '@/api/generated/models/document-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';

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
    NgTemplateOutlet,
    PaginationComponent,
    BulkEditTagsPopoverComponent,
    BulkEditPartiesPopoverComponent,
    BulkReprocessDialogComponent,
    BulkReprocessProgressDialogComponent,
    DownloadProgressDialogComponent,
  ],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  providers: [provideIcons({ lucideFileText, lucidePlus, lucideSearch, lucideTable, lucideLayoutGrid, lucideTrash2, lucideX, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown, lucideListChecks, lucideTags, lucideUsers, lucideDownload })],
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
            <ng-icon hlm name="lucideFileText" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load documents</h3>
            <p hlmEmptyDescription>There was an error loading the document list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
          <section hlmEmpty [class.hidden]="!showInitialEmpty()">
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideFileText" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No documents yet</h3>
              <p hlmEmptyDescription>Upload your first document to get started.</p>
            </div>
            @if (auth.canWrite()) {
              <input #emptyUploadInput type="file" multiple class="hidden" (change)="onEmptyUploadFiles($event)" />
              <button hlmBtn (click)="emptyUploadInput.click()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                Upload Document
              </button>
            }
          </section>
          <div class="relative shrink-0" [class.hidden]="showInitialEmpty()">
            <!-- Normal toolbar: always rendered to hold layout height -->
            <div [class.opacity-0]="isInSelectionState()" [class.pointer-events-none]="isInSelectionState()" [attr.inert]="isInSelectionState() ? '' : null">
              <div class="flex items-center gap-2 mb-3">
                @if (auth.canWrite() && viewMode() === 'grid') {
                  <button hlmBtn variant="outline" size="sm" (click)="enterSelectionMode()" aria-label="Enter selection mode">
                    <ng-icon hlm name="lucideListChecks" size="sm" />
                    <span class="hidden sm:inline">Select</span>
                  </button>
                }
                <div class="relative flex-1 min-w-0">
                  <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
                  <input hlmInput placeholder="Search documents..." class="pl-8 h-8 w-full min-w-0" aria-label="Search documents" [value]="searchInput()" (input)="searchInput.set($any($event.target).value)" />
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
                        <button hlmDropdownMenuRadio [checked]="!effectiveSortBy()" (triggered)="onSortByChange(null)">
                          <hlm-dropdown-menu-radio-indicator />
                          Relevance
                        </button>
                      }
                      @for (opt of sortOptions; track opt.value) {
                        <button hlmDropdownMenuRadio [checked]="effectiveSortBy() === opt.value" (triggered)="onSortByChange(opt.value)">
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
                      <hlm-dropdown-menu-separator />
                      <button hlmDropdownMenuItem (triggered)="onDownloadAll()">
                        <ng-icon hlm name="lucideDownload" size="sm" />
                        Download All
                      </button>
                    </hlm-dropdown-menu>
                  </ng-template>
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
                @if (hasActiveFilters()) {
                  <button hlmBtn variant="ghost" size="sm" (click)="resetFilters()">
                    Reset
                    <ng-icon hlm name="lucideX" size="xs" />
                  </button>
                }
              </div>
            </div>

            <!-- Selection toolbar: absolute overlay -->
            <div class="absolute inset-x-0 top-0 flex flex-wrap items-center gap-x-2 gap-y-1 min-h-8 bg-background" [class.opacity-0]="!isInSelectionState() || !auth.canWrite()" [class.pointer-events-none]="!isInSelectionState() || !auth.canWrite()" [attr.inert]="!isInSelectionState() || !auth.canWrite() ? '' : null">
              <div hlmButtonGroup aria-label="Selection controls">
                <button hlmBtn variant="outline" size="icon-sm" (click)="clearSelection()" aria-label="Exit selection mode">
                  <ng-icon hlm name="lucideX" size="sm" />
                </button>
                @if (!allPageSelected()) {
                  <button hlmBtn variant="outline" size="sm" (click)="selectAllOnPage()">
                    Select all
                  </button>
                }
              </div>
              @if (hasSelection()) {
                <span class="whitespace-nowrap text-sm text-muted-foreground">
                  {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }} selected
                </span>
              } @else {
                <span class="whitespace-nowrap text-sm text-muted-foreground">Select items</span>
              }

              <div class="sm:ml-auto max-sm:basis-full flex items-center gap-2">
                <app-bulk-edit-tags-popover [selectedIds]="selectedDocumentIds()" [documents]="data()?.items ?? []" [disabled]="!hasSelection()" (applied)="onBulkUpdateApplied()" />
                <app-bulk-edit-parties-popover [selectedIds]="selectedDocumentIds()" [documents]="data()?.items ?? []" [disabled]="!hasSelection()" (applied)="onBulkUpdateApplied()" />
                <app-bulk-reprocess-dialog [documentIds]="selectedDocumentIds()" [disabled]="!hasSelection()" (reprocessStarted)="onBulkReprocessStarted($event)" />
                <button hlmBtn variant="outline" size="sm" (click)="onDownloadSelected()" [disabled]="!hasSelection()">
                  <ng-icon hlm name="lucideDownload" size="sm" />
                  Download
                </button>
                <hlm-alert-dialog>
                  <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger [disabled]="!hasSelection()">
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    Delete
                  </button>
                  <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                    <div hlmAlertDialogHeader>
                      <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                      <p hlmAlertDialogDescription>
                        The selected document{{ selectionCount() > 1 ? 's' : '' }}
                        will be moved to trash. You can restore {{ selectionCount() > 1 ? 'them' : 'it' }} later.
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

          <section hlmEmpty [class.hidden]="!showFilteredEmpty()">
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
          <div class="flex min-h-0 flex-1 flex-col sm:overflow-y-auto p-px" [class.hidden]="!hasItems()">
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
                  [selectionMode]="isInSelectionState()"
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
    <app-bulk-reprocess-progress-dialog />
    <app-download-progress-dialog />
  `,
})
export class DocumentListComponent implements OnDestroy {
  auth = inject(AuthService);
  private documentApi = inject(DocumentApiService);
  private tagApi = inject(TagApiService);
  private partyApi = inject(PartyApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private listState = inject(DocumentListStateService);
  private openDocumentsService = inject(OpenDocumentsService);
  private uploadManager = inject(UploadManagerService);
  private errorHandler = inject(ErrorHandlerService);
  private downloadApi = inject(DownloadApiService);
  private reprocessProgressDialog = viewChild(BulkReprocessProgressDialogComponent);
  private downloadDialog = viewChild(DownloadProgressDialogComponent);
  private cancelDownload$ = new Subject<void>();
  private destroyRef = inject(DestroyRef);
  private prefs = inject(UserPreferencesService);
  private hubSub: Subscription;
  private stateSaveSub: Subscription;

  hideableColumns = HIDEABLE_COLUMNS;
  readonly sortOptions: { value: DocumentSortBy; label: string }[] = [
    { value: 'CreatedAt', label: 'Date Added' },
    { value: 'DocumentDate', label: 'Document Date' },
    { value: 'Name', label: 'Name' },
    { value: 'ContentType', label: 'Content Type' },
    { value: 'FileSize', label: 'File Size' },
  ];
  viewMode = this.prefs.documentViewMode;
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);
  downloading = signal(false);

  ready = signal(true);

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

  // Column visibility (TanStack VisibilityState: { [columnId]: boolean })
  columnVisibility = signal<VisibilityState>({ ...DEFAULT_COLUMN_VISIBILITY });

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
    return this.isSearching() ? null : 'CreatedAt' as DocumentSortBy;
  });

  private static readonly DATE_SORT_FIELDS: Set<DocumentSortBy> = new Set(['CreatedAt', 'DocumentDate']);

  effectiveSortDir = computed(() => {
    const explicit = this.sortDir();
    if (explicit) return explicit;
    const sortBy = this.effectiveSortBy();
    if (!sortBy) return 'Desc' as SortDirection;
    return DocumentListComponent.DATE_SORT_FIELDS.has(sortBy) ? 'Desc' as SortDirection : 'Asc' as SortDirection;
  });

  sortLabel = computed(() => {
    const sortBy = this.effectiveSortBy();
    if (!sortBy) return 'Relevance';
    const option = this.sortOptions.find(o => o.value === sortBy);
    return option?.label ?? sortBy;
  });

  hasActiveFilters = computed(() =>
    this.searchTerm().length > 0 ||
    this.selectedPartyIds().length > 0 ||
    this.selectedTagIds().length > 0,
  );

  /** Whether the current data response contains at least one document. */
  hasItems = computed(() => (this.data()?.items?.length ?? 0) > 0);

  /** Show the "No documents yet" empty state (truly empty, no filters, not loading). */
  showInitialEmpty = computed(() => !this.hasItems() && !this.hasActiveFilters() && !this.loading());

  /** Show the "No matching documents" empty state (filters active but no results). */
  showFilteredEmpty = computed(() => !this.hasItems() && this.hasActiveFilters());

  partyOptions = toSignal(
    toObservable(this.partyFilterSearch).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(search => {
        this.partyOptionsLoading.set(true);
        return this.partyApi.getParties({
          Search: search || undefined,
          Page: 1,
          PageSize: 100,
        }).pipe(
          map(r => {
            this.partyOptionsLoading.set(false);
            const items = r?.items ?? [];
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
        return this.tagApi.getTags({
          Search: search || undefined,
          IncludeChildren: false,
          Page: 1,
          PageSize: 50,
        }).pipe(
          map(r => {
            this.tagOptionsLoading.set(false);
            const items = r?.items ?? [];
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
    // Restore saved column visibility (merge defaults underneath so new columns default to hidden)
    const savedVisibility = this.prefs.documentColumnVisibility();
    if (Object.keys(savedVisibility).length > 0) {
      this.columnVisibility.set({ ...DEFAULT_COLUMN_VISIBILITY, ...savedVisibility });
    }

    // Save query params on every change, and restore saved state when navigating
    // to /documents without params (e.g. sidebar link) while saved state exists.
    this.stateSaveSub = this.route.queryParams.subscribe(params => {
      const hasParams = Object.keys(params).length > 0;
      if (!hasParams && this.listState.hasParams) {
        this.ready.set(false);
        this.router.navigate(['/documents'], {
          queryParams: this.listState.getSavedParams(),
          replaceUrl: true,
        });
        this.router.events.pipe(
          filter(e => e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError),
          take(1),
        ).subscribe(() => {
          this.searchInput.set(this.route.snapshot.queryParamMap.get('search') ?? '');
          this.ready.set(true);
        });
      } else if (hasParams) {
        this.listState.saveParams(params);
      }
    });

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
        replaceUrl: true,
      });
    });

    effect(() => {
      const items = this.data()?.items;
      if (items) {
        this.openDocumentsService.setNavigationList(items.map(d => d.id));
      }
    });
  }

  ngOnDestroy(): void {
    this.hubSub.unsubscribe();
    this.searchSub.unsubscribe();
    this.stateSaveSub.unsubscribe();
  }

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? this.prefs.pageSize()))),
    { initialValue: this.prefs.pageSize() },
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
      toObservable(this.ready),
    ]).pipe(
      filter(([, , , , , , , , , ready]) => ready),
      switchMap(([page, pageSize, , partyIds, tagIds, includeChildTags, sortBy, sortDir, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.documentApi.getDocuments({
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
          PartyIds: partyIds.length > 0 ? partyIds : undefined,
          TagIds: tagIds.length > 0 ? tagIds : undefined,
          IncludeChildTags: includeChildTags,
          SortBy: sortBy || undefined,
          SortDir: sortDir || undefined,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r as PaginatedResponseOfGetDocumentsResponse;
          }),
          handleQueryError(this.loading, this.error, null),
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
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.prefs.pageSize.set(newSize);
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  setViewMode(mode: 'table' | 'grid'): void {
    this.viewMode.set(mode);
  }

  onDocumentSelect(id: string): void {
    this.router.navigate(['/documents', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  onEmptyUploadFiles(event: Event): void {
    handleFileInputChange(event, files => this.uploadManager.addFiles(files));
  }

  onPartyFilterChange(ids: string[]): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, partyIds: ids.length > 0 ? ids : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
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
      replaceUrl: true,
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
      replaceUrl: true,
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
      replaceUrl: true,
    });
  }

  onSortByChange(field: DocumentSortBy | null): void {
    if (!field) {
      this.onSortChange(null);
    } else {
      const defaultDir: SortDirection = DocumentListComponent.DATE_SORT_FIELDS.has(field) ? 'Desc' : 'Asc';
      this.onSortChange({ sortBy: field, sortDir: this.sortDir() ?? defaultDir });
    }
  }

  onSortDirChange(dir: SortDirection): void {
    const field = this.effectiveSortBy();
    if (field) {
      this.onSortChange({ sortBy: field, sortDir: dir });
    }
  }

  onToggleColumn(id: string): void {
    this.columnVisibility.update(prev => ({
      ...prev,
      [id]: prev[id] === false,
    }));
    this.prefs.documentColumnVisibility.set(this.columnVisibility());
  }

  resetFilters(): void {
    this.clearSelection();
    this.searchInput.set('');
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, search: null, partyIds: null, tagIds: null, includeChildTags: null },
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

  onBulkUpdateApplied(): void {
    this.clearSelection();
    this.onRefresh();
  }

  onBulkReprocessStarted(jobIds: string[]): void {
    const parentJobId = jobIds[0];
    const documentCount = this.selectedDocumentIds().size;
    this.clearSelection();
    this.processingHub.registerManagedJob(parentJobId);

    this.reprocessProgressDialog()?.start(documentCount);

    const isTerminal = (s: string) => s === 'Completed' || s === 'PartiallyCompleted' || s === 'Failed';

    this.processingHub.jobStatusChanged$.pipe(
      filter(msg => msg.jobId === parentJobId),
      takeWhile(msg => !isTerminal(msg.status), true),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(msg => {
      const dialog = this.reprocessProgressDialog();
      if (!dialog) return;

      dialog.updateProgress(
        msg.completedChildCount ?? 0,
        msg.failedChildCount ?? 0,
        msg.totalChildCount ?? documentCount,
      );

      if (msg.status === 'Completed') {
        dialog.markCompleted();
      } else if (msg.status === 'PartiallyCompleted') {
        dialog.markPartiallyCompleted();
      } else if (msg.status === 'Failed') {
        dialog.markFailed();
      }

      if (isTerminal(msg.status)) {
        this.onRefresh();
      }
    });
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    const result$ = this.documentApi.bulkDeleteDocuments({
      body: { ids },
    }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete documents. Please try again.'),
    );

    toast.promise(
      firstValueFrom(result$).then((response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.deletedCount;
        this.clearSelection();
        this.onRefresh();
        return `${count} document${count > 1 ? 's' : ''} moved to trash.`;
      }),
      {
        loading: 'Moving documents to trash...',
        success: (message) => message,
      },
    );
  }

  onDownloadSelected(): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    if (ids.length === 1) {
      this.downloadSingleDocument(ids[0]);
      return;
    }

    this.startDownload(ids);
  }

  private downloadSingleDocument(id: string): void {
    const doc = this.data()?.items?.find(d => d.id === id);
    if (!doc) return;

    this.downloading.set(true);

    this.documentApi.getDocumentFile(id).pipe(
      handleMutationError(this.errorHandler, this.downloading, 'Failed to download document.'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      triggerDownload(url, `${doc.name}${doc.fileExtension}`);
      setTimeout(() => URL.revokeObjectURL(url));
      this.downloading.set(false);
    });
  }

  onDownloadAll(): void {
    this.startDownload(undefined);
  }

  private startDownload(ids?: string[]): void {
    this.downloading.set(true);
    this.cancelDownload$.next();

    this.downloadApi.downloadDocuments({
      body: { ids: ids ?? null },
    }).pipe(
      handleMutationError(this.errorHandler, this.downloading, 'Failed to start download.'),
    ).subscribe({
      next: (response) => {
        this.processingHub.registerManagedJob(response.jobId);
        const downloadUrl = this.downloadApi.getDownloadUrl(response.jobId);
        this.downloadDialog()?.startDownload(downloadUrl);

        this.processingHub.jobStatusChanged$.pipe(
          filter(msg => msg.jobId === response.jobId),
          filter(msg => msg.status === 'Completed' || msg.status === 'Failed'),
          take(1),
          takeUntil(this.cancelDownload$),
          takeUntilDestroyed(this.destroyRef),
        ).subscribe(msg => {
          this.downloading.set(false);
          if (msg.status === 'Completed') {
            this.downloadDialog()?.markCompleted();
          } else {
            this.downloadDialog()?.markFailed(msg.error);
          }
        });
      },
    });
  }
}
