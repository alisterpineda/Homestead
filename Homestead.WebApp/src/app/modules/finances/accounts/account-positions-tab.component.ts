import {
  Component,
  ChangeDetectionStrategy,
  DestroyRef,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, tap, catchError, combineLatest, EMPTY } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePackage,
  lucideCircleAlert,
  lucideEllipsisVertical,
  lucidePencil,
} from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { formatMaskedCurrency } from '../shared/format-currency';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { PositionResponse } from '@/api/generated/models/position-response';

interface PositionRow {
  position: PositionResponse;
  commodity: CommodityResponse | null;
  quantityText: string;
  displayValueText: string | null;
  isUnpriced: boolean;
  shareText: string | null;
}

@Component({
  selector: 'app-account-positions-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmTableImports,
    HlmDropdownMenuImports,
  ],
  providers: [provideIcons({
    lucidePackage, lucideCircleAlert, lucideEllipsisVertical, lucidePencil,
  })],
  template: `
    @if (loading() && rows().length === 0) {
      <div class="flex justify-center py-16">
        <hlm-spinner />
      </div>
    } @else if (error()) {
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucideCircleAlert" />
          </div>
          <div hlmEmptyTitle>Failed to load positions</div>
          <div hlmEmptyDescription>There was an error loading the positions for this account.</div>
        </div>
      </div>
    } @else if (rows().length === 0) {
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucidePackage" />
          </div>
          <div hlmEmptyTitle>No positions yet</div>
          <div hlmEmptyDescription>Use <em>Add position</em> to record a starting balance for a currency or security.</div>
        </div>
      </div>
    } @else {
      @if (unpricedCount() > 0) {
        <p class="mb-3 text-xs text-muted-foreground">
          {{ unpricedCount() }} of {{ rows().length }} position{{ rows().length === 1 ? '' : 's' }} unpriced — display value can't be computed without a price.
        </p>
      }
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh>Commodity</th>
              <th hlmTh class="text-right">Quantity</th>
              <th hlmTh class="text-right">Value</th>
              <th hlmTh class="w-24 text-right">Share</th>
              <th hlmTh class="w-12"></th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (row of rows(); track row.position.commodityId) {
              <tr hlmTr>
                <td hlmTd>
                  @if (row.commodity; as c) {
                    <div class="flex flex-col">
                      <span class="font-medium">{{ c.symbol }}@if (c.exchange) {<span class="text-muted-foreground"> · {{ c.exchange }}</span>}</span>
                      <span class="text-xs text-muted-foreground">{{ c.name }}</span>
                    </div>
                  } @else {
                    <span class="text-muted-foreground">…</span>
                  }
                </td>
                <td hlmTd class="text-right tabular-nums">{{ row.quantityText }}</td>
                <td hlmTd class="text-right tabular-nums">
                  @if (row.displayValueText !== null) {
                    {{ row.displayValueText }}
                  } @else if (row.isUnpriced) {
                    <div class="flex flex-col items-end">
                      <span>—</span>
                      <a
                        [routerLink]="['/finances/commodities', row.position.commodityId]"
                        class="text-xs font-normal text-primary hover:underline"
                      >Add price</a>
                    </div>
                  } @else {
                    —
                  }
                </td>
                <td hlmTd class="text-right tabular-nums text-muted-foreground">{{ row.shareText ?? '—' }}</td>
                <td hlmTd class="text-right">
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    [hlmDropdownMenuTrigger]="rowMenu"
                    [attr.aria-label]="'Position actions for ' + (row.commodity?.symbol ?? '')"
                  >
                    <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
                  </button>
                  <ng-template #rowMenu>
                    <hlm-dropdown-menu class="w-52">
                      <hlm-dropdown-menu-group>
                        <button hlmDropdownMenuItem (triggered)="onEditOpeningBalance(row.position.commodityId)">
                          <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
                          Edit opening balance
                        </button>
                      </hlm-dropdown-menu-group>
                    </hlm-dropdown-menu>
                  </ng-template>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class AccountPositionsTabComponent {
  account = input.required<AccountResponse>();
  refreshKey = input(0);

  editOpeningBalance = output<{ commodityId: string }>();
  // Held commodity IDs, surfaced so the parent can hide them from the Add-position picker.
  heldCommodityIdsChange = output<string[]>();
  // Held *currency* IDs (a subset, resolved once each commodity's kind is known),
  // surfaced so the parent can decide whether to reveal the trade funding-currency
  // picker (PC1: only when cash is held in a non-reporting currency).
  heldCurrencyIdsChange = output<string[]>();

  loading = signal(false);
  error = signal(false);
  raw = signal<PositionResponse[]>([]);
  commodities = signal<Record<string, CommodityResponse>>({});

  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private settingsService = inject(FinanceSettingsService);
  private prefs = inject(UserPreferencesService);
  private destroyRef = inject(DestroyRef);

  rows = computed<PositionRow[]>(() => {
    const dc = this.settingsService.displayCurrency();
    const masked = this.prefs.financesPrivacyMasked();
    const map = this.commodities();
    return this.raw().map(p => {
      const c = map[p.commodityId] ?? null;
      const quantityText = c
        ? formatMaskedCurrency(p.quantity, c, masked)
        : (masked ? '•' : p.quantity.toString());
      const displayValueText = (p.displayValue != null && dc)
        ? formatMaskedCurrency(p.displayValue, dc, masked)
        : null;
      const shareText = p.shareOfAccount != null
        ? `${(p.shareOfAccount * 100).toFixed(1)}%`
        : null;
      // Genuinely unpriced (no resolvable price) vs. merely missing a display currency:
      // only the former warrants the "Add price" prompt.
      const isUnpriced = p.displayValue == null;
      return { position: p, commodity: c, quantityText, displayValueText, isUnpriced, shareText };
    });
  });

  unpricedCount = computed(() => this.raw().filter(p => p.displayValue == null).length);

  // Held positions whose commodity has resolved to a currency. Recomputes as the
  // per-commodity GETs land, so the value is eventually consistent. The array-content
  // `equal` comparator keeps it from churning the parent on every unrelated commodity
  // resolution — it only fires the emit effect when the currency-id set actually changes.
  private heldCurrencyIds = computed(
    () => {
      const map = this.commodities();
      return this.raw()
        .map(p => p.commodityId)
        .filter(id => map[id]?.kind === 'Currency');
    },
    { equal: (a, b) => a.length === b.length && a.every(id => b.includes(id)) },
  );

  constructor() {
    effect(() => this.heldCurrencyIdsChange.emit(this.heldCurrencyIds()));

    toSignal(
      combineLatest([
        toObservable(this.account),
        toObservable(this.refreshKey),
      ]).pipe(
        switchMap(([a]) => {
          if (!a) return of([] as PositionResponse[]);
          this.loading.set(true);
          this.error.set(false);
          return this.accountApi.getAccountPositions({ accountId: a.id }).pipe(
            catchError(() => {
              this.error.set(true);
              return of([] as PositionResponse[]);
            }),
          );
        }),
        tap(items => {
          this.loading.set(false);
          this.raw.set(items);
          this.resolveCommodities(items);
          this.heldCommodityIdsChange.emit(items.map(p => p.commodityId));
        }),
      ),
      { initialValue: [] as PositionResponse[] },
    );
  }

  onEditOpeningBalance(commodityId: string): void {
    this.editOpeningBalance.emit({ commodityId });
  }

  private resolveCommodities(positions: PositionResponse[]): void {
    const known = this.commodities();
    for (const p of positions) {
      if (known[p.commodityId]) continue;
      this.commodityApi.getCommodity({ id: p.commodityId }).pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(c => this.commodities.update(m => ({ ...m, [p.commodityId]: c })));
    }
  }
}
