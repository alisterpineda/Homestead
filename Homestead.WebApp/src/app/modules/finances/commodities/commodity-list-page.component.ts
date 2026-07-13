import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { Router, ActivatedRoute, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, debounceTime, distinctUntilChanged, combineLatest, filter, take, firstValueFrom, forkJoin, of } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideCircleAlert, lucideCoins, lucideListFilter } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { CreateCommodityDialogComponent } from './create-commodity-dialog/create-commodity-dialog.component';
import { EditCommoditySheetComponent } from './edit-commodity-sheet/edit-commodity-sheet.component';
import { CommodityTableComponent, type CommoditySortBy, type CommoditySortDir } from './commodity-table.component';
import { CommodityListStateService } from './commodity-list-state.service';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CommodityKind } from '@/api/generated/models/commodity-kind';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

type KindFilter = CommodityKind | 'all' | 'none';
const isKindFilter = (v: string | null): v is KindFilter =>
  v === 'Security' || v === 'Currency' || v === 'all' || v === 'none';
const isCommoditySortBy = (v: string | null): v is CommoditySortBy =>
  v === 'symbol' || v === 'kind' || v === 'exchange' || v === 'name'
    || v === 'displayPrecision' || v === 'priceCount' || v === 'latestPriceAt';
const isCommoditySortDir = (v: string | null): v is CommoditySortDir => v === 'asc' || v === 'desc';
const COMMODITY_PAGE_SIZE = 100;
const COMMODITY_TOTAL_GUARD = 1000;

@Component({
  selector: 'app-commodity-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    SearchInputComponent,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmPopoverImports,
    HlmSeparatorImports,
    CommodityTableComponent,
    CreateCommodityDialogComponent,
    EditCommoditySheetComponent,
  ],
  providers: [provideIcons({ lucidePlus, lucideCircleAlert, lucideCoins, lucideListFilter })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      <div class="flex items-center justify-between gap-2">
        <div class="flex items-center gap-2 flex-1 min-w-0">
          <app-search-input class="max-w-sm flex-1 min-w-0" [(value)]="searchInput" placeholder="Search by symbol or name" ariaLabel="Search commodities" />
          <div hlmPopover>
            <button hlmPopoverTrigger hlmBtn variant="outline">
              <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
              {{ filterTriggerLabel() }}
            </button>
            <div hlmPopoverContent *hlmPopoverPortal class="w-[260px] p-0" align="start">
              <div class="flex items-center justify-between px-3 py-2">
                <span class="text-sm font-medium">Filters</span>
                @if (kindFilter() !== 'Security') {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    class="h-auto px-2 py-0.5 text-xs"
                    (click)="resetKindFilter()"
                  >Reset</button>
                }
              </div>
              <span hlmSeparator></span>
              <div role="group" aria-labelledby="commodity-kind-filter-label" class="p-3">
                <div id="commodity-kind-filter-label" class="text-xs font-medium text-muted-foreground mb-2">Kind</div>
                <div class="flex flex-col gap-2">
                  <label class="flex items-center gap-2 text-sm cursor-pointer" hlmLabel>
                    <hlm-checkbox
                      [checked]="showSecurities()"
                      (checkedChange)="onKindToggle('Security', $event === true)"
                      [aria-label]="'Show securities'"
                    />
                    <span>Security</span>
                  </label>
                  <label class="flex items-center gap-2 text-sm cursor-pointer" hlmLabel>
                    <hlm-checkbox
                      [checked]="showCurrencies()"
                      (checkedChange)="onKindToggle('Currency', $event === true)"
                      [aria-label]="'Show currencies'"
                    />
                    <span>Currency</span>
                  </label>
                </div>
              </div>
            </div>
          </div>
        </div>
        <app-create-commodity-dialog (created)="onChanged()">
          <ng-icon hlm name="lucidePlus" size="sm" />
          New security
        </app-create-commodity-dialog>
      </div>

      @if (loading() && !data()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Failed to load commodities</div>
            <div hlmEmptyDescription>There was an error loading commodities. Please try again.</div>
          </div>
        </div>
      } @else {
        @if (rows().length === 0) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCoins" /></div>
              <div hlmEmptyTitle>{{ emptyTitle() }}</div>
              <div hlmEmptyDescription>{{ emptyDescription() }}</div>
            </div>
          </div>
        } @else {
          <app-commodity-table
            [commodities]="rows()"
            [sortBy]="sortBy()"
            [sortDir]="sortDir()"
            (rowClick)="openDetail($event)"
            (edit)="onEdit($event)"
            (delete)="onDeleteRequest($event)"
            (sortChange)="onSortChange($event)"
          />
        }
      }
    </div>

    <app-edit-commodity-sheet
      [commodity]="selectedCommodity()"
      [state]="sheetState()"
      (saved)="onSheetSaved($event)"
      (closed)="onSheetClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.symbol ?? 'commodity' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the commodity. The action cannot be undone.
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
export class CommodityListPageComponent {
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private listState = inject(CommodityListStateService);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  ready = signal(false);

  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');

  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => (p.get('search') ?? '').trim())),
    { initialValue: (this.route.snapshot.queryParamMap.get('search') ?? '').trim() },
  );

  kindFilter = toSignal(
    this.route.queryParamMap.pipe(map((p): KindFilter => {
      const raw = p.get('kind');
      return isKindFilter(raw) ? raw : 'Security';
    })),
    { initialValue: 'Security' as KindFilter },
  );

  sortBy = toSignal(
    this.route.queryParamMap.pipe(map((p): CommoditySortBy | null => {
      const raw = p.get('sortBy');
      return isCommoditySortBy(raw) ? raw : null;
    })),
    { initialValue: null as CommoditySortBy | null },
  );

  sortDir = toSignal(
    this.route.queryParamMap.pipe(map((p): CommoditySortDir | null => {
      const raw = p.get('sortDir');
      return isCommoditySortDir(raw) ? raw : null;
    })),
    { initialValue: null as CommoditySortDir | null },
  );

  showSecurities = computed(() => {
    const k = this.kindFilter();
    return k === 'Security' || k === 'all';
  });

  showCurrencies = computed(() => {
    const k = this.kindFilter();
    return k === 'Currency' || k === 'all';
  });

  filterTriggerLabel = computed(() => {
    const k = this.kindFilter();
    if (k === 'Security') return 'Filter: Securities';
    if (k === 'Currency') return 'Filter: Currencies';
    if (k === 'all') return 'Filter: All kinds';
    return 'Filter'; // 'none' — treated as show-all but no commitment recorded
  });

  selectedCommodity = signal<CommodityResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  pendingDelete = signal<CommodityResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);

  data = toSignal<CommodityResponse[] | null, CommodityResponse[] | null>(
    combineLatest([
      toObservable(this.refreshTrigger),
      toObservable(this.searchTerm),
      toObservable(this.ready),
    ]).pipe(
      filter(([, , ready]) => ready),
      switchMap(([, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.commodityApi.getCommodities({
          Page: 1,
          PageSize: COMMODITY_PAGE_SIZE,
          Search: search || undefined,
        }).pipe(
          switchMap(first => {
            const total = first.totalCount;
            if (total > COMMODITY_TOTAL_GUARD) {
              console.warn(
                `[CommodityListPage] Commodity total count (${total}) exceeds guard (${COMMODITY_TOTAL_GUARD}); list is truncated.`,
              );
            }
            const totalPages = Math.min(
              Math.ceil(total / COMMODITY_PAGE_SIZE),
              Math.ceil(COMMODITY_TOTAL_GUARD / COMMODITY_PAGE_SIZE),
            );
            if (totalPages <= 1) return of(first.items ?? []);
            const tail$ = forkJoin(
              Array.from({ length: totalPages - 1 }, (_, i) =>
                this.commodityApi.getCommodities({
                  Page: i + 2,
                  PageSize: COMMODITY_PAGE_SIZE,
                  Search: search || undefined,
                }).pipe(map(r => r.items ?? [])),
              ),
            );
            return tail$.pipe(map(pages => [...(first.items ?? []), ...pages.flat()]));
          }),
          map(items => {
            this.loading.set(false);
            return items as CommodityResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  rows = computed(() => {
    const all = this.data() ?? [];
    const k = this.kindFilter();
    if (k === 'all' || k === 'none') return all;
    return all.filter(c => c.kind === k);
  });

  emptyTitle = computed(() => {
    if (this.searchTerm()) return 'No matches';
    const k = this.kindFilter();
    if (k === 'Security') return 'No securities yet';
    if (k === 'Currency') return 'No currencies';
    return 'No commodities';
  });

  emptyDescription = computed(() => {
    const search = this.searchTerm();
    const k = this.kindFilter();
    if (search) {
      if (k === 'Security') return `No securities matching "${search}". Try a different symbol or name.`;
      if (k === 'Currency') return `No currencies matching "${search}". Try a different symbol or name.`;
      return `No commodities matching "${search}". Try a different symbol or name.`;
    }
    if (k === 'Security') return 'Add a security to start tracking its price.';
    if (k === 'Currency') return 'Currencies are seeded automatically.';
    return 'Adjust the filter to see commodities.';
  });

  constructor() {
    const initialParams = this.route.snapshot.queryParams;
    const hasInitialParams = Object.keys(initialParams).length > 0;
    if (!hasInitialParams && this.listState.hasParams) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: this.listState.getSavedParams(),
        replaceUrl: true,
      });
      this.router.events.pipe(
        filter(e => e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError),
        take(1),
        takeUntilDestroyed(),
      ).subscribe(() => {
        this.searchInput.set(this.route.snapshot.queryParamMap.get('search') ?? '');
        this.ready.set(true);
      });
    } else {
      this.ready.set(true);
    }

    this.route.queryParams.pipe(takeUntilDestroyed()).subscribe(params => {
      if (Object.keys(params).length > 0) {
        this.listState.saveParams(params);
      }
    });

    toObservable(this.searchInput).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      takeUntilDestroyed(),
    ).subscribe(value => {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { search: value || null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onKindToggle(kind: CommodityKind, checked: boolean): void {
    const sec = kind === 'Security' ? checked : this.showSecurities();
    const cur = kind === 'Currency' ? checked : this.showCurrencies();
    let next: KindFilter;
    if (sec && cur) next = 'all';
    else if (sec) next = 'Security';
    else if (cur) next = 'Currency';
    else next = 'none';
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { kind: next },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  resetKindFilter(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { kind: 'Security' },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortChange(event: { sortBy: CommoditySortBy; sortDir: CommoditySortDir }): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sortBy: event.sortBy, sortDir: event.sortDir },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  openDetail(commodity: CommodityResponse): void {
    this.router.navigate(['/finances/commodities', commodity.id]);
  }

  onEdit(commodity: CommodityResponse): void {
    this.selectedCommodity.set(commodity);
    this.sheetState.set('open');
  }

  onSheetSaved(_: CommodityResponse): void {
    this.sheetState.set('closed');
    this.onChanged();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedCommodity.set(null);
  }

  onDeleteRequest(commodity: CommodityResponse): void {
    this.pendingDelete.set(commodity);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const c = this.pendingDelete();
    if (!c) return;
    this.deleting.set(true);
    const result$ = this.commodityApi.deleteCommodity({ id: c.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete commodity.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Commodity deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }
}
