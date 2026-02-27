import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucidePlus, lucideSearch, lucideListFilter, lucideLayoutList, lucideLayoutGrid, lucideTrash2, lucideX, lucideCirclePlus } from '@ng-icons/lucide';
import { BrnAlertDialog } from '@spartan-ng/brain/alert-dialog';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { FilterPopoverComponent } from '@/shared/components/filter-popover/filter-popover.component';
import type { FilterOption } from '@/shared/components/filter-popover/filter-popover.component';
import { DocumentTableComponent } from '../../components/document-table/document-table.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { UploadDialogComponent } from '../../components/upload-dialog/upload-dialog.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { getTags } from '@/api/generated/fn/tags/get-tags';
import { bulkDeleteDocuments } from '@/api/generated/fn/documents/bulk-delete-documents';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';
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
    FilterPopoverComponent,
    DocumentTableComponent,
    PaginationComponent,
    UploadDialogComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucidePlus, lucideSearch, lucideListFilter, lucideLayoutList, lucideLayoutGrid, lucideTrash2, lucideX, lucideCirclePlus })],
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
        @if (result.items.length === 0) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideFileText" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No documents yet</h3>
              <p hlmEmptyDescription>Upload your first document to get started.</p>
            </div>
            <app-upload-dialog (uploaded)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              Upload Document
            </app-upload-dialog>
          </section>
        } @else {
          <div class="flex flex-wrap items-center gap-2 mb-4">
            @if (hasSelection()) {
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
                  <hlm-alert-dialog-content *hlmAlertDialogPortal>
                    <div hlmAlertDialogHeader>
                      <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                      <p hlmAlertDialogDescription>
                        This action cannot be undone. The selected document{{ selectionCount() > 1 ? 's' : '' }}
                        and {{ selectionCount() > 1 ? 'their' : 'its' }} associated files will be permanently deleted.
                      </p>
                    </div>
                    <div hlmAlertDialogFooter>
                      <button hlmAlertDialogCancel>Cancel</button>
                      <button hlmAlertDialogAction variant="destructive" (click)="onBulkDelete()" [disabled]="deleting()">
                        {{ deleting() ? 'Deleting...' : 'Delete' }}
                      </button>
                    </div>
                  </hlm-alert-dialog-content>
                </hlm-alert-dialog>
              </div>
            } @else {
              <!-- Search: shares row with actions on mobile, inline on desktop -->
              <div class="relative order-1 flex-1 sm:flex-none min-w-0">
                <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
                <input hlmInput placeholder="Search documents..." class="pl-8 h-8 w-full sm:w-[200px]" />
              </div>
              <!-- Filters: own row on mobile, inline between search & actions on desktop -->
              <div class="flex flex-wrap items-center gap-2 order-3 sm:order-2 basis-full sm:basis-auto">
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
              <!-- Actions: next to search on mobile, pushed right on desktop -->
              <div class="flex items-center gap-2 order-2 sm:order-3 sm:ml-auto">
                <!-- TODO: Wire up view toggle to switch between table and grid views -->
                <div hlmButtonGroup aria-label="View mode">
                  <button hlmBtn [variant]="viewMode() === 'table' ? 'secondary' : 'outline'" size="icon-sm" (click)="viewMode.set('table')" aria-label="Table view">
                    <ng-icon hlm name="lucideLayoutList" size="sm" />
                  </button>
                  <button hlmBtn [variant]="viewMode() === 'grid' ? 'secondary' : 'outline'" size="icon-sm" (click)="viewMode.set('grid')" aria-label="Grid view">
                    <ng-icon hlm name="lucideLayoutGrid" size="sm" />
                  </button>
                </div>
                <app-upload-dialog triggerSize="sm" (uploaded)="onRefresh()">
                  <ng-icon hlm name="lucidePlus" size="sm" />
                  Upload
                </app-upload-dialog>
              </div>
            }
          </div>

          <app-document-table
            [documents]="result.items"
            [selectedIds]="selectedDocumentIds()"
            [allSelected]="allSelected()"
            [someSelected]="someSelected()"
            (documentSelect)="onDocumentSelect($event)"
            (selectionChange)="onSelectionChange($event)"
            (selectAllChange)="onSelectAll($event)"
          />
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

    </div>
  `,
})
export class DocumentListComponent implements OnDestroy {
  private alertDialog = viewChild(BrnAlertDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private hubSub: Subscription;

  viewMode = signal<'table' | 'grid'>('table');
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);

  selectedDocumentIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedDocumentIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

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

  hasActiveFilters = computed(() => this.selectedPartyIds().length > 0 || this.selectedTagIds().length > 0);

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

  allSelected = computed(() => {
    const items = this.data()?.items;
    const selected = this.selectedDocumentIds();
    return !!items && items.length > 0 && items.every((doc) => selected.has(doc.id));
  });

  someSelected = computed(() => {
    const items = this.data()?.items;
    const selected = this.selectedDocumentIds();
    if (!items || items.length === 0) return false;
    const selectedCount = items.filter((doc) => selected.has(doc.id)).length;
    return selectedCount > 0 && selectedCount < items.length;
  });

  constructor() {
    this.hubSub = this.processingHub.jobCompleted$.subscribe(() => {
      this.refreshTrigger.update((v) => v + 1);
    });
  }

  ngOnDestroy(): void {
    this.hubSub.unsubscribe();
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
    ]).pipe(
      switchMap(([page, pageSize, , partyIds, tagIds, includeChildTags]) => {
        this.loading.set(true);
        this.error.set(false);
        return getDocuments(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
          PartyIds: partyIds.length > 0 ? partyIds : undefined,
          TagIds: tagIds.length > 0 ? tagIds : undefined,
          IncludeChildTags: includeChildTags,
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

  onDocumentSelect(id: string): void {
    this.router.navigate(['/documents', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  onSelectionChange(event: { id: string; checked: boolean }): void {
    this.selectedDocumentIds.update((prev) => {
      const next = new Set(prev);
      if (event.checked) {
        next.add(event.id);
      } else {
        next.delete(event.id);
      }
      return next;
    });
  }

  onSelectAll(checked: boolean): void {
    if (checked) {
      const items = this.data()?.items;
      if (items) {
        this.selectedDocumentIds.set(new Set(items.map((doc) => doc.id)));
      }
    } else {
      this.clearSelection();
    }
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

  resetFilters(): void {
    this.clearSelection();
    this.router.navigate(['/documents'], {
      queryParams: { page: 1, partyIds: null, tagIds: null, includeChildTags: null },
      queryParamsHandling: 'merge',
    });
  }

  clearSelection(): void {
    this.selectedDocumentIds.set(new Set());
  }

  onBulkDelete(): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkDeleteDocuments(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).subscribe({
      next: (response) => {
        this.deleting.set(false);
        this.alertDialog()?.close();
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
