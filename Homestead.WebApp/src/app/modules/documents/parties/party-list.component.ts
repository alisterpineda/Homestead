import { Component, ChangeDetectionStrategy, inject, signal, computed, DestroyRef } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { AuthService } from '@/shared/services/auth.service';
import { Router, ActivatedRoute, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest, debounceTime, distinctUntilChanged, filter, take, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucidePlus, lucideTrash2, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown } from '@ng-icons/lucide';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { PartyTableComponent, HIDEABLE_COLUMNS, DEFAULT_COLUMN_VISIBILITY } from './party-table/party-table.component';
import { CreatePartyDialogComponent } from './create-party-dialog/create-party-dialog.component';
import { EditPartySheetComponent } from './edit-party-sheet/edit-party-sheet.component';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { PartyListStateService } from './party-list-state.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { ColumnVisibilityState } from '@tanstack/angular-table';
import { PartyApiService } from '@/api/services/party-api.service';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import type { PaginatedResponseOfGetPartiesResponse } from '@/api/generated/models/paginated-response-of-get-parties-response';
import type { PartySortBy } from '@/api/generated/models/party-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-party-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    NgTemplateOutlet,
    HlmSpinner,
    HlmEmptyImports,
    SearchInputComponent,
    HlmButton,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    PartyTableComponent,
    CreatePartyDialogComponent,
    EditPartySheetComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucidePlus, lucideTrash2, lucideSlidersHorizontal, lucideEllipsisVertical, lucideArrowUp, lucideArrowDown })],
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
              <ng-icon name="lucideUsers" />
            </div>
            <div hlmEmptyTitle>Failed to load parties</div>
            <div hlmEmptyDescription>There was an error loading the party list. Please try again.</div>
          </div>
        </div>
      }

      @if (data(); as result) {
        <div hlmEmpty [class.hidden]="!showInitialEmpty()">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideUsers" />
            </div>
            <div hlmEmptyTitle>No parties yet</div>
            <div hlmEmptyDescription>Create your first party to get started.</div>
          </div>
          @if (auth.canWrite()) {
            <div hlmEmptyContent>
              <app-create-party-dialog (created)="onRefresh()">
                <ng-icon name="lucidePlus" class="text-[length:--spacing(4)]" />
                New Party
              </app-create-party-dialog>
            </div>
          }
        </div>
        <div class="relative shrink-0" [class.hidden]="showInitialEmpty()">
          <!-- Normal toolbar -->
          <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()" [attr.inert]="hasSelection() ? '' : null">
            <div class="flex items-center gap-2 mb-3">
              <app-search-input class="flex-1 min-w-0" [(value)]="searchInput" placeholder="Search parties..." ariaLabel="Search parties" />
              <div class="flex items-center gap-2">
                <button hlmBtn variant="outline" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="sortMenu">
                  <ng-icon [name]="effectiveSortDir() === 'Asc' ? 'lucideArrowUp' : 'lucideArrowDown'" class="text-[length:--spacing(4)]" />
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
                <button hlmBtn variant="outline" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="columnsMenu">
                  <ng-icon name="lucideSlidersHorizontal" class="text-[length:--spacing(4)]" />
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
                <button hlmBtn variant="outline" size="icon" class="sm:hidden"
                        [hlmDropdownMenuTrigger]="overflowMenu" aria-label="More options">
                  <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
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
                  <app-create-party-dialog (created)="onRefresh()">
                    <ng-icon name="lucidePlus" class="text-[length:--spacing(4)]" />
                    New Party
                  </app-create-party-dialog>
                }
              </div>
            </div>
          </div>

          <!-- Selection toolbar -->
          <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!hasSelection() || !auth.canWrite()" [class.pointer-events-none]="!hasSelection() || !auth.canWrite()" [attr.inert]="!hasSelection() || !auth.canWrite() ? '' : null">
            <span class="text-sm text-muted-foreground">
              {{ selectionCount() }} {{ selectionCount() > 1 ? 'parties' : 'party' }} selected
            </span>
            <div class="ml-auto flex items-center gap-2">
              <button hlmBtn variant="outline" (click)="clearSelection()">
                Clear selection
              </button>
              <hlm-alert-dialog>
                <button hlmBtn variant="destructive" hlmAlertDialogTrigger>
                  <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
                  Delete
                </button>
                <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                  <div hlmAlertDialogHeader>
                    <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} {{ selectionCount() > 1 ? 'parties' : 'party' }}?</h2>
                    <p hlmAlertDialogDescription>
                      This action cannot be undone. The selected {{ selectionCount() > 1 ? 'parties' : 'party' }} will be permanently deleted
                      and removed from all associated documents.
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

        <div hlmEmpty [class.hidden]="!showFilteredEmpty()">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideUsers" />
            </div>
            <div hlmEmptyTitle>No matching parties</div>
            <div hlmEmptyDescription>Try adjusting your search to find what you're looking for.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" size="sm" (click)="resetSearch()">
              Reset search
            </button>
          </div>
        </div>
        <div class="min-h-0 flex-1 overflow-y-auto p-px" [class.hidden]="!hasItems()">
          <app-party-table
            [parties]="result.items"
            [selectedIds]="selectedPartyIds()"
            [sortBy]="sortBy()"
            [sortDir]="sortDir()"
            [columnVisibility]="columnVisibility()"
            (partySelect)="onPartySelect($event)"
            (selectionChange)="selectedPartyIds.set($event)"
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

    <app-edit-party-sheet
      [party]="selectedParty()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />
  `,
})
export class PartyListComponent {
  auth = inject(AuthService);
  private partyApi = inject(PartyApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private listState = inject(PartyListStateService);
  private errorHandler = inject(ErrorHandlerService);
  private prefs = inject(UserPreferencesService);
  private destroyRef = inject(DestroyRef);

  hideableColumns = HIDEABLE_COLUMNS;
  readonly sortOptions: { value: PartySortBy; label: string }[] = [
    { value: 'Name', label: 'Name' },
    { value: 'DocumentCount', label: 'Documents' },
    { value: 'CreatedAt', label: 'Date Added' },
    { value: 'UpdatedAt', label: 'Modified' },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);
  ready = signal(true);

  // Selection state
  selectedPartyIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedPartyIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Sheet state
  selectedParty = signal<GetPartiesResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  // Search
  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  // Column visibility
  columnVisibility = signal<ColumnVisibilityState>({ ...DEFAULT_COLUMN_VISIBILITY });

  // Sort state from query params
  sortBy = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortBy') as PartySortBy | null)),
  );

  sortDir = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sortDir') as SortDirection | null)),
  );

  effectiveSortBy = computed(() => {
    const explicit = this.sortBy();
    return explicit || 'Name' as PartySortBy;
  });

  private static readonly DATE_SORT_FIELDS: Set<PartySortBy> = new Set(['CreatedAt', 'UpdatedAt']);

  effectiveSortDir = computed(() => {
    const explicit = this.sortDir();
    if (explicit) return explicit;
    const sortBy = this.effectiveSortBy();
    return PartyListComponent.DATE_SORT_FIELDS.has(sortBy) ? 'Desc' as SortDirection : 'Asc' as SortDirection;
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

  constructor() {
    const savedVisibility = this.prefs.partyColumnVisibility();
    if (Object.keys(savedVisibility).length > 0) {
      this.columnVisibility.set({ ...DEFAULT_COLUMN_VISIBILITY, ...savedVisibility });
    }

    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const hasParams = Object.keys(params).length > 0;
      if (!hasParams && this.listState.hasParams) {
        this.ready.set(false);
        this.router.navigate(['/documents/parties'], {
          queryParams: this.listState.getSavedParams(),
          replaceUrl: true,
        });
        this.router.events.pipe(
          filter(e => e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError),
          take(1),
          takeUntilDestroyed(this.destroyRef),
        ).subscribe(() => {
          this.searchInput.set(this.route.snapshot.queryParamMap.get('search') ?? '');
          this.ready.set(true);
        });
      } else if (hasParams) {
        this.listState.saveParams(params);
      }
    });

    toObservable(this.searchInput).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(value => {
      this.router.navigate(['/documents/parties'], {
        queryParams: { page: 1, search: value || null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });
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
      toObservable(this.sortBy),
      toObservable(this.sortDir),
      toObservable(this.searchTerm),
      toObservable(this.ready),
    ]).pipe(
      filter(([, , , , , , ready]) => ready),
      switchMap(([page, pageSize, , sortBy, sortDir, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.partyApi.getParties({
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
          SortBy: sortBy || undefined,
          SortDir: sortDir || undefined,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r as PaginatedResponseOfGetPartiesResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  onPageChange(newPage: number): void {
    this.clearSelection();
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.prefs.pageSize.set(newSize);
    this.clearSelection();
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPartySelect(id: string): void {
    const parties = this.data()?.items;
    const party = parties?.find((p) => p.id === id) ?? null;
    if (party) {
      this.selectedParty.set(party);
      this.sheetState.set('open');
    }
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedParty.set(null);
  }

  clearSelection(): void {
    this.selectedPartyIds.set(new Set());
  }

  onSortChange(event: { sortBy: PartySortBy; sortDir: SortDirection } | null): void {
    this.clearSelection();
    this.router.navigate(['/documents/parties'], {
      queryParams: {
        page: 1,
        sortBy: event?.sortBy ?? null,
        sortDir: event?.sortDir ?? null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortByChange(field: PartySortBy): void {
    const defaultDir: SortDirection = PartyListComponent.DATE_SORT_FIELDS.has(field) ? 'Desc' : 'Asc';
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
    this.prefs.partyColumnVisibility.set(this.columnVisibility());
  }

  resetSearch(): void {
    this.clearSelection();
    this.searchInput.set('');
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: 1, search: null },
      queryParamsHandling: 'merge',
    });
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedPartyIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    const result$ = this.partyApi.bulkDeleteParties({
      body: { ids },
    }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete parties. Please try again.'),
    );

    toast.promise(
      firstValueFrom(result$).then((response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.deletedCount;
        this.clearSelection();
        this.onRefresh();
        return `${count} ${count > 1 ? 'parties' : 'party'} deleted.`;
      }),
      {
        loading: 'Deleting parties...',
        success: (message) => message,
      },
    );
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
