import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '@/shared/services/auth.service';
import { Router, ActivatedRoute, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription, debounceTime, distinctUntilChanged, filter, take } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTags, lucidePlus, lucideTrash2, lucideSearch, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { TagTableComponent, HIDEABLE_COLUMNS, DEFAULT_COLUMN_VISIBILITY } from '../../components/tag-table/tag-table.component';
import { CreateTagDialogComponent } from '../../components/create-tag-dialog/create-tag-dialog.component';
import { EditTagSheetComponent } from '../../components/edit-tag-sheet/edit-tag-sheet.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { TagListStateService } from '../../services/tag-list-state.service';
import type { VisibilityState } from '@tanstack/angular-table';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import { bulkDeleteTags } from '@/api/generated/fn/documents/bulk-delete-tags';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { PaginatedResponseOfGetTagsResponse } from '@/api/generated/models/paginated-response-of-get-tags-response';
import type { TagSortBy } from '@/api/generated/models/tag-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-tag-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    NgTemplateOutlet,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmButton,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    TagTableComponent,
    CreateTagDialogComponent,
    EditTagSheetComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideTags, lucidePlus, lucideTrash2, lucideSearch, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
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
            <ng-icon hlm name="lucideTags" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load tags</h3>
            <p hlmEmptyDescription>There was an error loading the tag list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
        <section hlmEmpty [class.hidden]="!showInitialEmpty()">
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideTags" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>No tags yet</h3>
            <p hlmEmptyDescription>Create your first tag to get started.</p>
          </div>
          @if (auth.canWrite()) {
            <app-create-tag-dialog (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New Tag
            </app-create-tag-dialog>
          }
        </section>
        <div class="relative shrink-0" [class.hidden]="showInitialEmpty()">
          <!-- Normal toolbar -->
          <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()" [attr.inert]="hasSelection() ? '' : null">
            <div class="flex items-center gap-2 mb-3">
              <div class="relative flex-1 min-w-0">
                <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
                <input hlmInput placeholder="Search tags..." class="pl-8 h-8 w-full min-w-0" aria-label="Search tags" [value]="searchInput()" (input)="searchInput.set($any($event.target).value)" />
              </div>
              <div class="flex items-center gap-2">
                <button hlmBtn variant="outline" size="sm" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="sortMenu">
                  <ng-icon hlm [name]="effectiveSortDir() === 'Asc' ? 'lucideArrowUp' : 'lucideArrowDown'" size="sm" />
                  {{ sortLabel() }}
                </button>
                <ng-template #sortGroup>
                  <span hlmDropdownMenuLabel>Sort by</span>
                  <hlm-dropdown-menu-group>
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
                <button hlmBtn variant="outline" size="sm" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="columnsMenu">
                  <ng-icon hlm name="lucideSlidersHorizontal" size="sm" />
                  Columns
                </button>
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
                <!-- Mobile: Overflow menu (below sm) -->
                <button hlmBtn variant="outline" size="icon-sm" class="sm:hidden"
                        [hlmDropdownMenuTrigger]="overflowMenu" aria-label="More options">
                  <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
                </button>
                <ng-template #overflowMenu>
                  <hlm-dropdown-menu class="w-44">
                    <button hlmDropdownMenuItem [hlmDropdownMenuTrigger]="sortSubMenu">
                      Sort
                      <hlm-dropdown-menu-item-sub-indicator />
                    </button>
                    <ng-template #sortSubMenu>
                      <hlm-dropdown-menu-sub>
                        <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
                      </hlm-dropdown-menu-sub>
                    </ng-template>
                    <button hlmDropdownMenuItem [hlmDropdownMenuTrigger]="columnsSubMenu">
                      Columns
                      <hlm-dropdown-menu-item-sub-indicator />
                    </button>
                    <ng-template #columnsSubMenu>
                      <hlm-dropdown-menu-sub>
                        <ng-container [ngTemplateOutlet]="columnsContent" [ngTemplateOutletInjector]="'outlet'" />
                      </hlm-dropdown-menu-sub>
                    </ng-template>
                  </hlm-dropdown-menu>
                </ng-template>
                @if (auth.canWrite()) {
                  <app-create-tag-dialog triggerSize="sm" (created)="onRefresh()">
                    <ng-icon hlm name="lucidePlus" size="sm" />
                    New Tag
                  </app-create-tag-dialog>
                }
              </div>
            </div>
          </div>

          <!-- Selection toolbar -->
          <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!hasSelection() || !auth.canWrite()" [class.pointer-events-none]="!hasSelection() || !auth.canWrite()" [attr.inert]="!hasSelection() || !auth.canWrite() ? '' : null">
            <span class="text-sm text-muted-foreground">
              {{ selectionCount() }} tag{{ selectionCount() > 1 ? 's' : '' }} selected
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
                    <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} tag{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                    <p hlmAlertDialogDescription>
                      This action cannot be undone. Any child tags will be re-parented to the deleted tag's parent.
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
            <ng-icon hlm name="lucideTags" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>No matching tags</h3>
            <p hlmEmptyDescription>Try adjusting your search to find what you're looking for.</p>
          </div>
          <button hlmBtn variant="outline" size="sm" (click)="resetSearch()">
            Reset search
          </button>
        </section>
        <div class="min-h-0 flex-1 overflow-y-auto p-px" [class.hidden]="!hasItems()">
          <app-tag-table
            [tags]="result.items"
            [selectedIds]="selectedTagIds()"
            [sortBy]="sortBy()"
            [sortDir]="sortDir()"
            [columnVisibility]="columnVisibility()"
            (tagSelect)="onTagSelect($event)"
            (selectionChange)="selectedTagIds.set($event)"
            (sortChange)="onSortChange($event)"
          />
        </div>
        <div class="shrink-0" [class.hidden]="!hasItems()">
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            [totalCount]="+result.totalCount"
            [pageSize]="+result.pageSize"
            (pageChange)="onPageChange($event)"
            (pageSizeChange)="onPageSizeChange($event)"
          />
        </div>
      }
    </div>

    <app-edit-tag-sheet
      [tag]="selectedTag()"
      [allTags]="allTags()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />
  `,
})
export class TagListComponent implements OnDestroy {
  auth = inject(AuthService);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private listState = inject(TagListStateService);

  hideableColumns = HIDEABLE_COLUMNS;
  readonly sortOptions: { value: TagSortBy; label: string }[] = [
    { value: 'Name', label: 'Name' },
    { value: 'Path', label: 'Path' },
    { value: 'DocumentCount', label: 'Documents' },
    { value: 'CreatedAt', label: 'Date Added' },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);
  ready = signal(true);

  // Selection state
  selectedTagIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedTagIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Sheet state
  selectedTag = signal<GetTagsResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  // Search
  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  // Column visibility
  columnVisibility = signal<VisibilityState>({ ...DEFAULT_COLUMN_VISIBILITY });

  // Sort state from query params
  sortBy = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortBy') as TagSortBy | null)),
  );

  sortDir = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortDir') as SortDirection | null)),
  );

  effectiveSortBy = computed(() => {
    const explicit = this.sortBy();
    return explicit || 'Name' as TagSortBy;
  });

  private static readonly DATE_SORT_FIELDS: Set<TagSortBy> = new Set(['CreatedAt']);

  effectiveSortDir = computed(() => {
    const explicit = this.sortDir();
    if (explicit) return explicit;
    const sortBy = this.effectiveSortBy();
    return TagListComponent.DATE_SORT_FIELDS.has(sortBy) ? 'Desc' as SortDirection : 'Asc' as SortDirection;
  });

  sortLabel = computed(() => {
    const sortBy = this.effectiveSortBy();
    const option = this.sortOptions.find(o => o.value === sortBy);
    return option?.label ?? sortBy;
  });

  hasActiveFilters = computed(() => this.searchTerm().length > 0);
  hasItems = computed(() => (this.data()?.items?.length ?? 0) > 0);
  showInitialEmpty = computed(() => !this.hasItems() && !this.hasActiveFilters() && !this.loading());
  showFilteredEmpty = computed(() => !this.hasItems() && this.hasActiveFilters());

  private searchSub: Subscription;
  private stateSaveSub: Subscription;

  constructor() {
    const savedVisibility = this.listState.getSavedColumnVisibility();
    if (savedVisibility) {
      this.columnVisibility.set({ ...DEFAULT_COLUMN_VISIBILITY, ...savedVisibility });
    }

    this.stateSaveSub = this.route.queryParams.subscribe(params => {
      const hasParams = Object.keys(params).length > 0;
      if (!hasParams && this.listState.hasParams) {
        this.ready.set(false);
        this.router.navigate(['/documents/tags'], {
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

    this.searchSub = toObservable(this.searchInput).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
    ).subscribe(value => {
      this.router.navigate(['/documents/tags'], {
        queryParams: { page: 1, search: value || null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });
  }

  ngOnDestroy(): void {
    this.searchSub.unsubscribe();
    this.stateSaveSub.unsubscribe();
  }

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? 50))),
    { initialValue: 50 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.refreshTrigger),
      toObservable(this.sortBy),
      toObservable(this.sortDir),
      toObservable(this.searchTerm),
      toObservable(this.ready),
    ]).pipe(
      filter(([, , , , , , ready]) => ready),
      switchMap(([page, pageSize, , sortBy, sortDir, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return getTags(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
          IncludeChildren: true,
          Search: search || undefined,
          SortBy: sortBy || undefined,
          SortDir: sortDir || undefined,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGetTagsResponse;
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

  // All tags (unpaginated) for the edit sheet's parent selector
  allTags = toSignal(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() =>
        getTags(this.http, this.apiConfig.rootUrl, {
          IncludeChildren: true,
          Page: 1,
          PageSize: 100,
        }).pipe(
          map(r => r.body.items as GetTagsResponse[]),
          catchError(() => of([] as GetTagsResponse[])),
        ),
      ),
    ),
    { initialValue: [] as GetTagsResponse[] },
  );

  onPageChange(newPage: number): void {
    this.clearSelection();
    this.router.navigate(['/documents/tags'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.clearSelection();
    this.router.navigate(['/documents/tags'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onTagSelect(id: string): void {
    const tags = this.data()?.items;
    const tag = tags?.find((t) => t.id === id) ?? null;
    if (tag) {
      this.selectedTag.set(tag);
      this.sheetState.set('open');
    }
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedTag.set(null);
  }

  clearSelection(): void {
    this.selectedTagIds.set(new Set());
  }

  onSortChange(event: { sortBy: TagSortBy; sortDir: SortDirection } | null): void {
    this.clearSelection();
    this.router.navigate(['/documents/tags'], {
      queryParams: {
        page: 1,
        sortBy: event?.sortBy ?? null,
        sortDir: event?.sortDir ?? null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortByChange(field: TagSortBy): void {
    const defaultDir: SortDirection = TagListComponent.DATE_SORT_FIELDS.has(field) ? 'Desc' : 'Asc';
    this.onSortChange({ sortBy: field, sortDir: this.sortDir() ?? defaultDir });
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
    this.listState.saveColumnVisibility(this.columnVisibility());
  }

  resetSearch(): void {
    this.clearSelection();
    this.searchInput.set('');
    this.router.navigate(['/documents/tags'], {
      queryParams: { page: 1, search: null },
      queryParamsHandling: 'merge',
    });
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedTagIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkDeleteTags(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} tag${count > 1 ? 's' : ''} deleted.`);
        this.clearSelection();
        this.onRefresh();
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete tags. Please try again.');
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
