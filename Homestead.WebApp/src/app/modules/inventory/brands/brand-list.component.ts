import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  DestroyRef,
} from '@angular/core';
import { AuthService } from '@/shared/services/auth.service';
import { Router, ActivatedRoute, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest, debounceTime, distinctUntilChanged, filter, take } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideBookmark,
  lucideCircleAlert,
  lucideSearch,
  lucideArrowUp,
  lucideArrowDown,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { BrandListStateService } from './brand-list-state.service';
import { BrandTableComponent, type BrandSortChange } from './brand-table/brand-table.component';
import { CreateBrandDialogComponent } from './create-brand-dialog/create-brand-dialog.component';
import { EditBrandDialogComponent, type BrandMergeRequest } from './edit-brand-dialog/edit-brand-dialog.component';
import { MergeBrandDialogComponent } from './merge-brand-dialog/merge-brand-dialog.component';
import type { BrandResponse } from '@/api/generated/models/brand-response';
import type { PaginatedResponseOfBrandResponse } from '@/api/generated/models/paginated-response-of-brand-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface BrandSortFieldOption {
  readonly value: string;
  readonly label: string;
  readonly defaultDesc: boolean;
}

@Component({
  selector: 'app-brand-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmButton,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    PaginationComponent,
    BrandTableComponent,
    CreateBrandDialogComponent,
    EditBrandDialogComponent,
    MergeBrandDialogComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucideBookmark,
    lucideCircleAlert,
    lucideSearch,
    lucideArrowUp,
    lucideArrowDown,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col p-4 md:p-6">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Failed to load brands</div>
            <div hlmEmptyDescription>There was an error loading the brand list. Please try again.</div>
          </div>
        </div>
      }

      @if (data(); as result) {
        <div hlmEmpty [class.hidden]="!showInitialEmpty()">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideBookmark" />
            </div>
            <div hlmEmptyTitle>No brands yet</div>
            <div hlmEmptyDescription>Brands are created as you attribute items; add one here to get a head start.</div>
          </div>
          @if (auth.canWrite()) {
            <div hlmEmptyContent>
              <app-create-brand-dialog (created)="onRefresh()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New brand
              </app-create-brand-dialog>
            </div>
          }
        </div>

        <div class="mb-3 flex items-center gap-2 shrink-0" [class.hidden]="showInitialEmpty()">
          <div class="relative flex-1 min-w-0">
            <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
            <input hlmInput placeholder="Search brands..." class="pl-8 h-8 w-full min-w-0" aria-label="Search brands" [value]="searchInput()" (input)="searchInput.set($any($event.target).value)" />
          </div>
          <button hlmBtn variant="outline" size="sm" [hlmDropdownMenuTrigger]="sortMenu">
            <ng-icon hlm [name]="effectiveSortDir() === 'asc' ? 'lucideArrowUp' : 'lucideArrowDown'" size="sm" />
            {{ sortLabel() }}
          </button>
          <ng-template #sortMenu>
            <hlm-dropdown-menu class="w-44">
              <span hlmDropdownMenuLabel>Sort by</span>
              <hlm-dropdown-menu-group>
                @for (opt of sortFields; track opt.value) {
                  <button hlmDropdownMenuRadio [checked]="effectiveSortBy() === opt.value" (triggered)="onSortByChange(opt.value)">
                    <hlm-dropdown-menu-radio-indicator />
                    {{ opt.label }}
                  </button>
                }
              </hlm-dropdown-menu-group>
              <hlm-dropdown-menu-separator />
              <span hlmDropdownMenuLabel>Direction</span>
              <hlm-dropdown-menu-group>
                <button hlmDropdownMenuRadio [checked]="effectiveSortDir() === 'asc'" (triggered)="onSortDirChange('asc')">
                  <hlm-dropdown-menu-radio-indicator />
                  Ascending
                </button>
                <button hlmDropdownMenuRadio [checked]="effectiveSortDir() === 'desc'" (triggered)="onSortDirChange('desc')">
                  <hlm-dropdown-menu-radio-indicator />
                  Descending
                </button>
              </hlm-dropdown-menu-group>
            </hlm-dropdown-menu>
          </ng-template>
          @if (auth.canWrite()) {
            <app-create-brand-dialog triggerSize="sm" (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New brand
            </app-create-brand-dialog>
          }
        </div>

        <div hlmEmpty [class.hidden]="!showFilteredEmpty()">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideBookmark" />
            </div>
            <div hlmEmptyTitle>No matching brands</div>
            <div hlmEmptyDescription>Try adjusting your search to find what you're looking for.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" size="sm" (click)="resetSearch()">
              Reset search
            </button>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-y-auto p-px" [class.hidden]="!hasItems()">
          <app-brand-table
            [brands]="result.items"
            [sortBy]="effectiveSortBy()"
            [sortDir]="effectiveSortDir()"
            (sortChange)="onSortChange($event)"
            (edit)="onEdit($event)"
            (merge)="onMergeRequest($event)"
            (delete)="onDeleteRequest($event)"
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

    <app-edit-brand-dialog
      [brand]="selectedBrand()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="onEditClosed()"
      (mergeRequested)="onMergeFromRename($event)"
    />

    <app-merge-brand-dialog
      [source]="mergeSource()"
      [state]="mergeState()"
      [initialTargetName]="mergeTargetName()"
      (merged)="onMerged()"
      (closed)="onMergeClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'brand' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the brand. A brand in use by any item cannot be deleted —
            merge it into another brand first.
          </p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
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
export class BrandListComponent {
  auth = inject(AuthService);
  private brandApi = inject(InventoryBrandApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private listState = inject(BrandListStateService);
  private errorHandler = inject(ErrorHandlerService);
  private prefs = inject(UserPreferencesService);
  private destroyRef = inject(DestroyRef);

  readonly sortFields: readonly BrandSortFieldOption[] = [
    { value: 'name', label: 'Name', defaultDesc: false },
    { value: 'items', label: 'Items', defaultDesc: true },
    { value: 'updated', label: 'Updated', defaultDesc: true },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  ready = signal(true);

  // Edit (rename) dialog
  selectedBrand = signal<BrandResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  // Merge dialog
  mergeSource = signal<BrandResponse | null>(null);
  mergeState = signal<BrnDialogState>('closed');
  mergeTargetName = signal('');

  // Delete confirm
  pendingDelete = signal<BrandResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  deleteError = signal('');

  // Search
  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  // Sort state from query params
  sortBy = toSignal(this.route.queryParamMap.pipe(map(p => p.get('sortBy'))));
  sortDir = toSignal(this.route.queryParamMap.pipe(map(p => p.get('sortDir'))));

  effectiveSortBy = computed(() => this.sortBy() || 'name');

  effectiveSortDir = computed(() => {
    const explicit = this.sortDir();
    if (explicit) return explicit;
    const field = this.sortFields.find(f => f.value === this.effectiveSortBy());
    return field?.defaultDesc ? 'desc' : 'asc';
  });

  sortLabel = computed(() =>
    this.sortFields.find(f => f.value === this.effectiveSortBy())?.label ?? 'Sort');

  hasActiveFilters = computed(() => this.searchTerm().length > 0);
  hasItems = computed(() => (this.data()?.items?.length ?? 0) > 0);
  showInitialEmpty = computed(() => !this.hasItems() && !this.hasActiveFilters() && !this.loading());
  showFilteredEmpty = computed(() => !this.hasItems() && this.hasActiveFilters());

  constructor() {
    // Restore saved list params on a bare navigation (mirrors the Tags list).
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const hasParams = Object.keys(params).length > 0;
      if (!hasParams && this.listState.hasParams) {
        this.ready.set(false);
        this.router.navigate(['/inventory/brands'], {
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
      this.router.navigate(['/inventory/brands'], {
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
      switchMap(([page, pageSize, , , , search]) => {
        this.loading.set(true);
        this.error.set(false);
        // Send the *effective* sort (what the toolbar arrow and table header display), not the raw
        // params. A deep-link like ?sortBy=items with no sortDir would otherwise request the server
        // default (asc) while the UI shows the per-field default (desc) — the arrows would lie.
        return this.brandApi.getBrands({
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
          SortBy: this.effectiveSortBy(),
          SortDir: this.effectiveSortDir(),
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r as PaginatedResponseOfBrandResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  onPageChange(newPage: number): void {
    this.router.navigate(['/inventory/brands'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.prefs.pageSize.set(newSize);
    this.router.navigate(['/inventory/brands'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortChange(event: BrandSortChange | null): void {
    this.router.navigate(['/inventory/brands'], {
      queryParams: {
        page: 1,
        sortBy: event?.sortBy ?? null,
        sortDir: event?.sortDir ?? null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortByChange(field: string): void {
    const defaultDesc = this.sortFields.find(f => f.value === field)?.defaultDesc ?? false;
    this.onSortChange({ sortBy: field, sortDir: this.sortDir() ?? (defaultDesc ? 'desc' : 'asc') });
  }

  onSortDirChange(dir: string): void {
    this.onSortChange({ sortBy: this.effectiveSortBy(), sortDir: dir });
  }

  resetSearch(): void {
    this.searchInput.set('');
    this.router.navigate(['/inventory/brands'], {
      queryParams: { page: 1, search: null },
      queryParamsHandling: 'merge',
    });
  }

  // ----- Rename -----

  onEdit(brand: BrandResponse): void {
    this.selectedBrand.set(brand);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.onRefresh();
  }

  onEditClosed(): void {
    this.editState.set('closed');
    this.selectedBrand.set(null);
  }

  // ----- Merge -----

  onMergeRequest(brand: BrandResponse): void {
    this.mergeTargetName.set('');
    this.mergeSource.set(brand);
    this.mergeState.set('open');
  }

  /** Rename hit a 409: close the edit dialog and open merge pre-seeded to the attempted name. */
  onMergeFromRename(request: BrandMergeRequest): void {
    this.editState.set('closed');
    this.selectedBrand.set(null);
    this.mergeTargetName.set(request.targetName);
    this.mergeSource.set(request.source);
    this.mergeState.set('open');
  }

  onMerged(): void {
    this.mergeState.set('closed');
    this.mergeSource.set(null);
    this.mergeTargetName.set('');
    this.onRefresh();
  }

  onMergeClosed(): void {
    this.mergeState.set('closed');
    this.mergeSource.set(null);
    this.mergeTargetName.set('');
  }

  // ----- Delete -----

  onDeleteRequest(brand: BrandResponse): void {
    this.deleteError.set('');
    this.pendingDelete.set(brand);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const brand = this.pendingDelete();
    if (!brand) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.brandApi.deleteBrand({ id: brand.id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        this.pendingDelete.set(null);
        this.onRefresh();
        toast.success('Brand deleted.');
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection reason
      // ("in use by items — merge it first") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete brand.'));
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
