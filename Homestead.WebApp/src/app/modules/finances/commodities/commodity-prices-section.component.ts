import {
  Component,
  ChangeDetectionStrategy,
  DestroyRef,
  inject,
  input,
  signal,
  computed,
  effect,
} from '@angular/core';
import { ActivatedRoute, Router, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest, forkJoin, of, catchError, firstValueFrom, filter, take } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucidePlus,
  lucideListFilter,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinancesCommodityPriceApiService } from '@/api/services/finances-commodity-price-api.service';
import { FinancesPriceSourcesApiService } from '@/api/services/finances-price-sources-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { NewPriceDialogComponent } from './new-price-dialog/new-price-dialog.component';
import { EditPriceDialogComponent } from './edit-price-dialog/edit-price-dialog.component';
import { CommodityPricesTableComponent } from './commodity-prices-table.component';
import { CurrencyPickerComponent } from '../shared/currency-picker/currency-picker.component';
import { DatePickerComponent } from '@/shared/components/date-picker/date-picker.component';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { CommodityPricesStateService } from './commodity-prices-state.service';
import { startOfDayLocal, endOfDayLocal } from '../shared/date-time';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';
import type { CommodityPriceSource } from '@/api/generated/models/commodity-price-source';
import type { CommodityPriceSortBy } from '@/api/generated/models/commodity-price-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import type { PaginatedResponseOfCommodityPriceResponse } from '@/api/generated/models/paginated-response-of-commodity-price-response';
import type { PriceSourceSummary } from '@/api/generated/models/price-source-summary';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

const isCommodityPriceSource = (v: string): v is CommodityPriceSource =>
  v === 'Manual' || v === 'Transaction' || v === 'BankOfCanada' || v === 'AlphaVantage';

const isCommodityPriceSortBy = (v: string | null): v is CommodityPriceSortBy =>
  v === 'PriceAt' || v === 'Rate' || v === 'Source' || v === 'Quote';

const isSortDirection = (v: string | null): v is SortDirection =>
  v === 'Asc' || v === 'Desc';

type DatePreset = 'last30' | 'thisMonth' | 'ytd' | 'lastYear';

@Component({
  selector: 'app-commodity-prices-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmPopoverImports,
    HlmSeparatorImports,
    NewPriceDialogComponent,
    EditPriceDialogComponent,
    CommodityPricesTableComponent,
    CurrencyPickerComponent,
    DatePickerComponent,
    PaginationComponent,
  ],
  providers: [
    CommodityPricesStateService,
    provideIcons({ lucideCircleAlert, lucidePlus, lucideListFilter, lucideTrash2 }),
  ],
  template: `
    <div class="flex flex-col gap-4">
      <div class="relative">
        <div
          class="flex items-center justify-between gap-2"
          [class.opacity-0]="hasSelection()"
          [class.pointer-events-none]="hasSelection()"
          [attr.inert]="hasSelection() ? '' : null"
        >
        <div hlmPopover>
          <button hlmPopoverTrigger hlmBtn variant="outline" size="sm">
            <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
            {{ filterTriggerLabel() }}
          </button>
          <div hlmPopoverContent *hlmPopoverPortal class="w-[300px] p-0" align="start">
            <div class="flex items-center justify-between px-3 py-2">
              <span class="text-sm font-medium">Filters</span>
              @if (hasActiveFilters()) {
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  class="h-auto px-2 py-0.5 text-xs"
                  (click)="resetFilters()"
                >Reset</button>
              }
            </div>
            <span hlmSeparator></span>
            <div role="group" aria-labelledby="prices-source-filter-label" class="p-3">
              <div id="prices-source-filter-label" class="text-xs font-medium text-muted-foreground mb-2">Source</div>
              <div class="flex flex-col gap-2">
                @for (src of allSources(); track src) {
                  <label class="flex items-center gap-2 text-sm cursor-pointer" hlmLabel>
                    <hlm-checkbox
                      [checked]="isSourceChecked(src)"
                      (checkedChange)="onSourceToggle(src, $event === true)"
                      [aria-label]="'Toggle ' + sourceLabel(src) + ' filter'"
                    />
                    <span>
                      {{ sourceLabel(src) }}
                      @if (disabledSources().has(src)) {
                        <span class="text-muted-foreground">(disabled)</span>
                      }
                    </span>
                  </label>
                }
              </div>
            </div>
            <span hlmSeparator></span>
            <div class="p-3">
              <div class="text-xs font-medium text-muted-foreground mb-2">Quote currency</div>
              <app-currency-picker
                [value]="quoteCurrencyId()"
                [selectedCommodity]="selectedQuoteCommodity()"
                (valueChange)="onQuoteCurrencyChange($event)"
                placeholder="Any currency"
                ariaLabel="Filter by quote currency"
              />
              @if (quoteCurrencyId()) {
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  class="h-auto px-2 py-0.5 text-xs mt-1"
                  (click)="onQuoteCurrencyChange(null)"
                >Clear</button>
              }
            </div>
            <span hlmSeparator></span>
            <div class="p-3">
              <div class="text-xs font-medium text-muted-foreground mb-2">Date range</div>
              <div class="flex flex-wrap gap-1.5 mb-2">
                <button hlmBtn variant="outline" size="sm" class="h-7 text-xs" (click)="applyPreset('last30')">Last 30 days</button>
                <button hlmBtn variant="outline" size="sm" class="h-7 text-xs" (click)="applyPreset('thisMonth')">This month</button>
                <button hlmBtn variant="outline" size="sm" class="h-7 text-xs" (click)="applyPreset('ytd')">YTD</button>
                <button hlmBtn variant="outline" size="sm" class="h-7 text-xs" (click)="applyPreset('lastYear')">Last year</button>
                @if (fromAtDate() || toAtDate()) {
                  <button hlmBtn variant="ghost" size="sm" class="h-7 text-xs" (click)="clearDateRange()">All</button>
                }
              </div>
              <div class="grid grid-cols-2 gap-2">
                <div class="flex flex-col gap-1">
                  <span class="text-[11px] uppercase text-muted-foreground">From</span>
                  <app-date-picker
                    [date]="fromAtDate()"
                    (dateChange)="onFromAtChange($event)"
                    ariaLabel="From date"
                  />
                </div>
                <div class="flex flex-col gap-1">
                  <span class="text-[11px] uppercase text-muted-foreground">To</span>
                  <app-date-picker
                    [date]="toAtDate()"
                    (dateChange)="onToAtChange($event)"
                    ariaLabel="To date"
                  />
                </div>
              </div>
            </div>
          </div>
        </div>
        <app-new-price-dialog [commodity]="commodity()" (created)="onChanged()">
          <ng-icon hlm name="lucidePlus" size="sm" />
          New manual price
        </app-new-price-dialog>
        </div>

        <div
          class="absolute inset-0 flex items-center gap-2 bg-background"
          [class.opacity-0]="!hasSelection()"
          [class.pointer-events-none]="!hasSelection()"
          [attr.inert]="!hasSelection() ? '' : null"
        >
          <span class="text-sm text-muted-foreground">
            {{ selectionCount() }} price{{ selectionCount() === 1 ? '' : 's' }} selected
          </span>
          <hlm-alert-dialog>
            <button
              hlmBtn
              variant="ghost"
              size="sm"
              class="text-destructive hover:bg-destructive/10 hover:text-destructive"
              hlmAlertDialogTrigger
            >
              <ng-icon hlm name="lucideTrash2" size="sm" />
              Delete
            </button>
            <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
              <div hlmAlertDialogHeader>
                <h2 hlmAlertDialogTitle>
                  Delete {{ selectionCount() }} price{{ selectionCount() === 1 ? '' : 's' }}?
                </h2>
                <p hlmAlertDialogDescription>
                  This permanently removes the selected price observations.
                </p>
              </div>
              <div hlmAlertDialogFooter>
                <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                <button
                  hlmAlertDialogAction
                  variant="destructive"
                  (click)="onBulkDelete(ctx)"
                  [disabled]="bulkDeleting()"
                >
                  {{ bulkDeleting() ? 'Deleting...' : 'Delete' }}
                </button>
              </div>
            </hlm-alert-dialog-content>
          </hlm-alert-dialog>
          <button
            hlmBtn
            variant="outline"
            size="sm"
            class="ml-auto"
            (click)="clearSelection()"
          >
            Clear
          </button>
        </div>
      </div>

      @if (loading() && !response()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Failed to load prices</div>
          </div>
        </div>
      } @else if (rows().length === 0) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyTitle>{{ emptyTitle() }}</div>
            <div hlmEmptyDescription>{{ emptyDescription() }}</div>
          </div>
        </div>
      } @else {
        <app-commodity-prices-table
          [prices]="rows()"
          [sortBy]="sortBy()"
          [sortDir]="sortDir()"
          [selectedIds]="selectedPriceIds()"
          [disabledSources]="disabledSources()"
          [formatPriceAt]="formatPriceAt"
          [quoteSymbol]="quoteSymbol"
          [formatRate]="formatRate"
          (edit)="onEdit($event)"
          (delete)="onDeleteRequest($event)"
          (sortChange)="onSortChange($event)"
          (selectionChange)="selectedPriceIds.set($event)"
        />

        @if (response(); as r) {
          <app-pagination
            [page]="+r.page"
            [totalPages]="+(r.totalPages ?? 0)"
            [totalCount]="+r.totalCount"
            [pageSize]="+r.pageSize"
            (pageChange)="onPageChange($event)"
            (pageSizeChange)="onPageSizeChange($event)"
          />
        }
      }
    </div>

    <app-edit-price-dialog
      [price]="selectedPrice()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="onEditClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete this price?</h2>
          <p hlmAlertDialogDescription>This permanently removes the price observation.</p>
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
export class CommodityPricesSectionComponent {
  commodity = input.required<CommodityResponse>();

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  ready = signal(false);

  selectedPrice = signal<CommodityPriceResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  pendingDelete = signal<CommodityPriceResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);

  selectedPriceIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedPriceIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);
  bulkDeleting = signal(false);

  currencyMap = signal<Record<string, CommodityResponse>>({});

  selectedQuoteCommodity = computed<CommodityResponse | null>(() => {
    const id = this.quoteCurrencyId();
    return id ? (this.currencyMap()[id] ?? null) : null;
  });

  private commodityApi = inject(FinancesCommodityApiService);
  private priceApi = inject(FinancesCommodityPriceApiService);
  private priceSourcesApi = inject(FinancesPriceSourcesApiService);
  private errorHandler = inject(ErrorHandlerService);
  private prefs = inject(UserPreferencesService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private pricesState = inject(CommodityPricesStateService);
  private destroyRef = inject(DestroyRef);

  priceSources = toSignal(
    this.priceSourcesApi.list().pipe(
      map(r => r.items),
      catchError(() => of([] as PriceSourceSummary[])),
    ),
    { initialValue: [] as PriceSourceSummary[] },
  );

  allSources = computed<CommodityPriceSource[]>(() =>
    this.priceSources().map(s => s.source)
  );

  disabledSources = computed<Set<CommodityPriceSource>>(() => {
    const set = new Set<CommodityPriceSource>();
    for (const s of this.priceSources()) {
      if (!s.isEnabled) set.add(s.source);
    }
    return set;
  });

  private sourceLabelMap = computed<Map<CommodityPriceSource, string>>(() => {
    const map = new Map<CommodityPriceSource, string>();
    for (const s of this.priceSources()) {
      map.set(s.source, s.displayName);
    }
    return map;
  });

  page = toSignal(
    this.route.queryParamMap.pipe(map(p => Number(p.get('page') ?? 1) || 1)),
    { initialValue: Number(this.route.snapshot.queryParamMap.get('page') ?? 1) || 1 },
  );

  pageSize = toSignal(
    this.route.queryParamMap.pipe(map(p => Number(p.get('pageSize') ?? this.prefs.pageSize()) || this.prefs.pageSize())),
    { initialValue: Number(this.route.snapshot.queryParamMap.get('pageSize') ?? this.prefs.pageSize()) || this.prefs.pageSize() },
  );

  sortBy = toSignal(
    this.route.queryParamMap.pipe(map((p): CommodityPriceSortBy | null => {
      const raw = p.get('sortBy');
      return isCommodityPriceSortBy(raw) ? raw : null;
    })),
    { initialValue: null as CommodityPriceSortBy | null },
  );

  sortDir = toSignal(
    this.route.queryParamMap.pipe(map((p): SortDirection | null => {
      const raw = p.get('sortDir');
      return isSortDirection(raw) ? raw : null;
    })),
    { initialValue: null as SortDirection | null },
  );

  private sourcesParam = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('sources') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('sources') ?? '' },
  );

  sources = computed((): CommodityPriceSource[] => {
    const raw = this.sourcesParam();
    if (!raw) return [];
    return raw.split(',').filter(isCommodityPriceSource);
  });

  quoteCurrencyId = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('quoteCurrencyId'))),
    { initialValue: this.route.snapshot.queryParamMap.get('quoteCurrencyId') },
  );

  fromAt = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('fromAt'))),
    { initialValue: this.route.snapshot.queryParamMap.get('fromAt') },
  );

  toAt = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('toAt'))),
    { initialValue: this.route.snapshot.queryParamMap.get('toAt') },
  );

  fromAtDate = computed(() => {
    const v = this.fromAt();
    return v ? new Date(v) : null;
  });

  toAtDate = computed(() => {
    const v = this.toAt();
    return v ? new Date(v) : null;
  });

  hasActiveFilters = computed(() =>
    this.sources().length > 0 || this.quoteCurrencyId() !== null || this.fromAt() !== null || this.toAt() !== null
  );

  filterTriggerLabel = computed(() => {
    const parts: string[] = [];
    if (this.sources().length > 0) parts.push('Source');
    if (this.quoteCurrencyId()) parts.push('Quote');
    if (this.fromAt() || this.toAt()) parts.push('Dates');
    return parts.length > 0 ? `Filter: ${parts.join(', ')}` : 'Filter';
  });

  response = toSignal<PaginatedResponseOfCommodityPriceResponse | null>(
    combineLatest([
      toObservable(this.commodity).pipe(map(c => c.id)),
      toObservable(this.refreshTrigger),
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.sortBy),
      toObservable(this.sortDir),
      toObservable(this.sources),
      toObservable(this.quoteCurrencyId),
      toObservable(this.fromAt),
      toObservable(this.toAt),
      toObservable(this.ready),
    ]).pipe(
      filter(([, , , , , , , , , , ready]) => ready),
      switchMap(([commodityId, , page, pageSize, sortBy, sortDir, sources, quoteId, fromAt, toAt]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.priceApi.getCommodityPrices({
          CommodityId: commodityId,
          Page: page,
          PageSize: pageSize,
          SortBy: sortBy ?? undefined,
          SortDir: sortDir ?? undefined,
          Sources: sources.length > 0 ? sources : undefined,
          QuoteCurrencyId: quoteId ?? undefined,
          FromAt: fromAt ?? undefined,
          ToAt: toAt ?? undefined,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r as PaginatedResponseOfCommodityPriceResponse | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  rows = computed(() => this.response()?.items ?? []);

  emptyTitle = computed(() => this.hasActiveFilters() ? 'No matches' : 'No prices yet');
  emptyDescription = computed(() => this.hasActiveFilters()
    ? 'No prices match the current filters. Adjust or reset them to see more.'
    : 'Add a manual price to start valuing this commodity.');

  formatPriceAt = (at: string): string => {
    const d = new Date(at);
    return d.toLocaleString();
  };

  quoteSymbol = (id: string): string => {
    return this.currencyMap()[id]?.symbol ?? id;
  };

  formatRate = (price: CommodityPriceResponse): string => {
    const quote = this.currencyMap()[price.quoteCurrencyId];
    const precision = Math.max(quote?.displayPrecision ?? 4, 4);
    return price.rate.toLocaleString(undefined, {
      minimumFractionDigits: precision,
      maximumFractionDigits: precision,
    });
  };

  constructor() {
    const initialParams = this.route.snapshot.queryParams;
    const hasInitialParams = Object.keys(initialParams).length > 0;
    if (!hasInitialParams && this.pricesState.hasParams) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: this.pricesState.getSavedParams(),
        replaceUrl: true,
      });
      this.router.events.pipe(
        filter(e => e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError),
        take(1),
        takeUntilDestroyed(),
      ).subscribe(() => this.ready.set(true));
    } else {
      this.ready.set(true);
    }

    this.route.queryParams.pipe(takeUntilDestroyed()).subscribe(params => {
      if (Object.keys(params).length > 0) {
        this.pricesState.saveParams(params);
      }
    });

    effect(() => {
      const known = this.currencyMap();
      const ids = new Set<string>();
      this.rows().forEach(p => ids.add(p.quoteCurrencyId));
      const filterId = this.quoteCurrencyId();
      if (filterId) ids.add(filterId);
      const missing = Array.from(ids).filter(id => !known[id]);
      if (missing.length === 0) return;
      forkJoin(missing.map(id =>
        this.commodityApi.getCommodity({ id }).pipe(catchError(() => of(null))),
      ))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(results => {
          const found = results.filter((c): c is CommodityResponse => c !== null);
          if (found.length === 0) return;
          this.currencyMap.update(m => {
            const next = { ...m };
            found.forEach(c => { next[c.id] = c; });
            return next;
          });
        });
    });
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  isSourceChecked(src: CommodityPriceSource): boolean {
    return this.sources().includes(src);
  }

  sourceLabel(src: CommodityPriceSource): string {
    return this.sourceLabelMap().get(src) ?? src;
  }

  onSourceToggle(src: CommodityPriceSource, checked: boolean): void {
    const current = new Set(this.sources());
    if (checked) current.add(src); else current.delete(src);
    const next = this.allSources().filter(s => current.has(s));
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        sources: next.length > 0 ? next.join(',') : null,
        page: 1,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onQuoteCurrencyChange(id: string | null): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { quoteCurrencyId: id ?? null, page: 1 },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onFromAtChange(date: Date | null): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { fromAt: date ? startOfDayLocal(date).toISOString() : null, page: 1 },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onToAtChange(date: Date | null): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { toAt: date ? endOfDayLocal(date).toISOString() : null, page: 1 },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  applyPreset(preset: DatePreset): void {
    const now = new Date();
    let from: Date;
    let to: Date | null = null;
    switch (preset) {
      case 'last30': {
        from = new Date(now);
        from.setDate(from.getDate() - 30);
        to = now;
        break;
      }
      case 'thisMonth': {
        from = new Date(now.getFullYear(), now.getMonth(), 1);
        to = now;
        break;
      }
      case 'ytd': {
        from = new Date(now.getFullYear(), 0, 1);
        to = now;
        break;
      }
      case 'lastYear': {
        from = new Date(now.getFullYear() - 1, 0, 1);
        to = new Date(now.getFullYear() - 1, 11, 31);
        break;
      }
    }
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        fromAt: startOfDayLocal(from).toISOString(),
        toAt: to ? endOfDayLocal(to).toISOString() : null,
        page: 1,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  clearDateRange(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { fromAt: null, toAt: null, page: 1 },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  resetFilters(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sources: null, quoteCurrencyId: null, fromAt: null, toAt: null, page: 1 },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortChange(event: { sortBy: CommodityPriceSortBy; sortDir: SortDirection }): void {
    this.clearSelection();
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sortBy: event.sortBy, sortDir: event.sortDir, page: 1 },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageChange(newPage: number): void {
    this.clearSelection();
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.prefs.pageSize.set(newSize);
    this.clearSelection();
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onEdit(price: CommodityPriceResponse): void {
    this.selectedPrice.set(price);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.onChanged();
  }

  onEditClosed(): void {
    this.editState.set('closed');
    this.selectedPrice.set(null);
  }

  onDeleteRequest(price: CommodityPriceResponse): void {
    this.pendingDelete.set(price);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const p = this.pendingDelete();
    if (!p) return;
    this.deleting.set(true);
    const result$ = this.priceApi.deleteCommodityPrice({ id: p.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete price.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Price deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }

  clearSelection(): void {
    this.selectedPriceIds.set(new Set());
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedPriceIds());
    if (ids.length === 0) return;
    this.bulkDeleting.set(true);
    const result$ = this.priceApi.bulkDeleteCommodityPrices({ body: { ids } }).pipe(
      handleMutationError(this.errorHandler, this.bulkDeleting, 'Failed to delete prices.'),
    );
    toast.promise(
      firstValueFrom(result$).then(response => {
        this.bulkDeleting.set(false);
        ctx.close();
        const count = +response.deletedCount;
        this.clearSelection();
        this.onChanged();
        return `${count} price${count === 1 ? '' : 's'} deleted.`;
      }),
      { loading: 'Deleting prices...', success: m => m },
    );
  }
}
