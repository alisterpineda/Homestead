import { Component, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EMPTY, switchMap, tap } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert, lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { FinancesReportsApiService } from '@/api/services/finances-reports-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { localIsoDate } from '../shared/date';
import { RangeSelectorComponent } from './range-selector.component';
import { NetWorthChartComponent } from './net-worth-chart.component';
import { NetWorthEmptyStateComponent } from './net-worth-empty-state.component';
import type { NetWorthEmptyReason } from './net-worth-empty-state.component';
import type { GetNetWorthHistoryResponse } from '@/api/generated/models/get-net-worth-history-response';
import { resolveWindow } from './resolve-window';

@Component({
  selector: 'app-reports-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmButton,
    HlmCheckboxImports,
    HlmLabel,
    RangeSelectorComponent,
    NetWorthChartComponent,
    NetWorthEmptyStateComponent,
  ],
  providers: [provideIcons({ lucideTriangleAlert, lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      <section class="rounded-lg border border-border bg-card p-4 text-card-foreground">
        <p class="text-xs uppercase tracking-wide text-muted-foreground">Net worth</p>
        <p class="mt-1 text-3xl font-semibold tabular-nums">
          {{ headlineText() }}
        </p>
        <p class="text-xs text-muted-foreground">
          {{ subtitleText() }}
        </p>
      </section>

      <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
        <app-net-worth-range-selector
          [value]="range()"
          [responseFrom]="data()?.from ?? null"
          [responseTo]="data()?.to ?? null"
          (valueChange)="range.set($event)"
        />
        <div class="flex items-center gap-2">
          <hlm-checkbox
            id="show-breakdown"
            [checked]="showBreakdown()"
            (checkedChange)="showBreakdown.set($event)"
            aria-label="Show assets and liabilities"
          />
          <label hlmLabel for="show-breakdown" class="cursor-pointer">Show assets &amp; liabilities</label>
        </div>
      </div>

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
            <div hlmEmptyTitle>Failed to load net worth history</div>
            <div hlmEmptyDescription>
              There was an error loading the chart. Try changing the range or reload the page.
            </div>
          </div>
        </div>
      }

      @if (data(); as response) {
        @if (emptyReason(); as reason) {
          <app-net-worth-empty-state [reason]="reason" />
        } @else {
          @if (response.hasUnpriced) {
            <div class="flex items-start gap-3 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
              <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
              <div class="flex-1">
                <p class="font-medium">Some periods have unpriced holdings</p>
                <p class="text-amber-900/80 dark:text-amber-100/80">
                  Add prices for the affected commodities to fill in the gaps.
                </p>
              </div>
              <a hlmBtn variant="outline" size="sm" routerLink="/finances/commodities">Open commodities</a>
            </div>
          }
          <section class="rounded-lg border border-border bg-card p-4 text-card-foreground">
            <app-net-worth-chart
              [points]="response.points"
              [granularity]="response.granularity"
              [displayCurrencySymbol]="response.displayCurrencySymbol ?? null"
              [xAxisMin]="chartXAxisMin()"
              [xAxisMax]="chartXAxisMax()"
              [showBreakdown]="showBreakdown()"
            />
          </section>
        }
      }
    </div>
  `,
})
export class ReportsPageComponent {
  private reportsApi = inject(FinancesReportsApiService);
  protected settingsService = inject(FinanceSettingsService);
  private userPreferences = inject(UserPreferencesService);

  protected readonly range = this.userPreferences.netWorthRange;
  protected readonly showBreakdown = this.userPreferences.netWorthShowBreakdown;
  protected readonly loading = signal(false);
  protected readonly error = signal(false);

  protected readonly resolvedWindow = computed(() => {
    const pref = this.range();
    if (pref.kind === 'All') return null;
    return resolveWindow(pref, new Date());
  });

  protected readonly data = toSignal<GetNetWorthHistoryResponse | null, GetNetWorthHistoryResponse | null>(
    toObservable(this.range).pipe(
      tap(() => {
        this.loading.set(true);
        this.error.set(false);
      }),
      switchMap(pref => {
        const params = pref.kind === 'All'
          ? { Range: 'All' as const }
          : this.customRangeParams();
        if (!params) return EMPTY;
        return this.reportsApi.getNetWorthHistory(params).pipe(
          tap(() => this.loading.set(false)),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  protected readonly chartXAxisMin = computed(
    () => this.resolvedWindow()?.from?.toISOString() ?? this.data()?.from ?? null,
  );

  protected readonly chartXAxisMax = computed(
    () => this.resolvedWindow()?.to?.toISOString() ?? this.data()?.to ?? null,
  );

  private customRangeParams(): { Range: 'Custom'; FromDate: string; ToDate: string } | null {
    const w = this.resolvedWindow();
    if (!w?.from || !w.to) return null;
    return { Range: 'Custom', FromDate: localIsoDate(w.from), ToDate: localIsoDate(w.to) };
  }

  protected readonly emptyReason = computed<NetWorthEmptyReason | null>(() => {
    const r = this.data()?.reason;
    return r === 'no_transactions' || r === 'no_display_currency' ? r : null;
  });

  protected readonly headlineText = computed(() => {
    const response = this.data();
    if (!response || response.points.length === 0) return '—';
    const lastValue = response.points[response.points.length - 1].value;
    if (lastValue == null) return '—';
    const symbol = response.displayCurrencySymbol ?? null;
    return new Intl.NumberFormat(undefined, {
      style: symbol ? 'currency' : 'decimal',
      currency: symbol ?? undefined,
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    }).format(lastValue);
  });

  protected readonly subtitleText = computed(() => {
    const response = this.data();
    if (!response) return 'Loading…';
    if (response.reason === 'no_display_currency') return 'Display currency not set.';
    if (response.reason === 'no_transactions') return 'No activity recorded yet.';
    const symbol = response.displayCurrencySymbol;
    const grain = response.granularity === 'Monthly' ? 'monthly' : 'daily';
    return symbol ? `Most recent value, in ${symbol} (${grain}).` : `Most recent value (${grain}).`;
  });

  constructor() {
    this.settingsService.load();
  }
}
