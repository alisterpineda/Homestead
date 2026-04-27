import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, EMPTY } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRightLeft,
  lucideScale,
  lucideFlag,
  lucideSplit,
  lucideCircleDashed,
  lucideChevronLeft,
  lucideChevronRight,
  lucideCircleAlert,
  lucideListTodo,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { displaySign } from '../shared/sign';
import { formatCurrency } from '../shared/format-currency';
import { toDateOnly } from '../shared/date';
import {
  shapeLabel,
  shapeIconName,
  signedAmountForAccount,
  transferCounterparty,
} from '../transactions/transaction-display';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';

const PAGE_SIZE = 25;

interface ActivityRowViewModel {
  tx: TransactionResponse;
  title: string;
  subtitle: string;
  iconName: string;
  signed: number;
  amountText: string | null;
  dateText: string;
}

@Component({
  selector: 'app-account-activity-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmSpinner, HlmEmptyImports],
  providers: [provideIcons({
    lucideArrowRightLeft, lucideScale, lucideFlag, lucideSplit, lucideCircleDashed,
    lucideChevronLeft, lucideChevronRight, lucideCircleAlert, lucideListTodo,
  })],
  template: `
    @if (loading() && items().length === 0) {
      <div class="flex justify-center py-16">
        <hlm-spinner />
      </div>
    } @else if (error()) {
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucideCircleAlert" />
          </div>
          <div hlmEmptyTitle>Failed to load activity</div>
          <div hlmEmptyDescription>There was an error loading transactions for this account.</div>
        </div>
      </div>
    } @else if (items().length === 0) {
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucideListTodo" />
          </div>
          <div hlmEmptyTitle>No activity yet</div>
          <div hlmEmptyDescription>Record a balance or create a transfer to populate this account.</div>
        </div>
      </div>
    } @else {
      <ul role="list" class="divide-y divide-border">
        @for (item of items(); track item.tx.id) {
          <li>
            <button
              type="button"
              class="flex w-full items-center gap-3 px-3 py-3 text-left transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-md"
              (click)="onClick(item.tx)"
            >
              <span class="flex size-9 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
                <ng-icon hlm [name]="item.iconName" size="sm" />
              </span>
              <div class="min-w-0 flex-1">
                <p class="text-sm font-medium truncate">{{ item.title }}</p>
                <p class="text-xs text-muted-foreground truncate">{{ item.subtitle }}</p>
              </div>
              <div class="text-right tabular-nums shrink-0">
                <p class="text-sm font-medium" [class.text-destructive]="item.signed < 0">
                  {{ item.amountText ?? '—' }}
                </p>
                <p class="text-xs text-muted-foreground">{{ item.dateText }}</p>
              </div>
            </button>
          </li>
        }
      </ul>

      <div class="mt-4 flex items-center justify-between text-sm">
        <p class="text-muted-foreground">
          Page {{ page() }} of {{ totalPages() }} · {{ totalCount() }} item{{ totalCount() === 1 ? '' : 's' }}
        </p>
        <div class="flex items-center gap-2">
          <button hlmBtn variant="outline" size="sm" (click)="prevPage()" [disabled]="page() <= 1 || loading()">
            <ng-icon hlm name="lucideChevronLeft" size="sm" />
            Prev
          </button>
          <button hlmBtn variant="outline" size="sm" (click)="nextPage()" [disabled]="page() >= totalPages() || loading()">
            Next
            <ng-icon hlm name="lucideChevronRight" size="sm" />
          </button>
        </div>
      </div>
    }
  `,
})
export class AccountActivityTabComponent {
  account = input.required<AccountResponse>();
  refreshKey = input(0);

  transactionClicked = output<TransactionResponse>();

  page = signal(1);
  loading = signal(false);
  error = signal(false);
  totalCount = signal(0);
  rawItems = signal<TransactionResponse[]>([]);
  // Cache resolved counterparty / commodity for row rendering.
  accountNames = signal<Record<string, string>>({});
  commodities = signal<Record<string, CommodityResponse>>({});

  private txApi = inject(FinancesTransactionApiService);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private settingsService = inject(FinanceSettingsService);

  totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  items = computed(() => {
    const a = this.account();
    const txs = this.rawItems();
    return txs.map(tx => this.toViewModel(tx, a));
  });

  constructor() {
    // Reset to page 1 whenever the account changes.
    effect(() => {
      this.account();
      this.page.set(1);
    });

    // Pagination + refresh-driven fetch.
    toSignal(
      combineLatest([
        toObservable(this.account),
        toObservable(this.page),
        toObservable(this.refreshKey),
      ]).pipe(
        switchMap(([a, page]) => {
          if (!a) return of({ items: [] as TransactionResponse[], totalCount: 0 });
          this.loading.set(true);
          this.error.set(false);
          return this.txApi.getAccountTransactions({
            accountId: a.id,
            Page: page,
            PageSize: PAGE_SIZE,
          }).pipe(
            map(r => ({ items: r.items ?? [], totalCount: Number(r.totalCount ?? 0) })),
            catchError(() => {
              this.error.set(true);
              return of({ items: [] as TransactionResponse[], totalCount: 0 });
            }),
          );
        }),
        map(r => {
          this.loading.set(false);
          this.rawItems.set(r.items);
          this.totalCount.set(r.totalCount);
          this.resolveLookups(r.items);
          return r;
        }),
      ),
      { initialValue: { items: [] as TransactionResponse[], totalCount: 0 } },
    );
  }

  prevPage(): void {
    if (this.page() > 1) this.page.update(p => p - 1);
  }
  nextPage(): void {
    if (this.page() < this.totalPages()) this.page.update(p => p + 1);
  }

  onClick(tx: TransactionResponse): void {
    this.transactionClicked.emit(tx);
  }

  private resolveLookups(txs: TransactionResponse[]): void {
    const accountIds = new Set<string>();
    const commodityIds = new Set<string>();
    for (const tx of txs) {
      for (const s of tx.splits) {
        accountIds.add(s.accountId);
        commodityIds.add(s.commodityId);
      }
    }
    const knownA = this.accountNames();
    const knownC = this.commodities();
    accountIds.forEach(id => {
      if (knownA[id]) return;
      this.accountApi.getAccount({ id }).pipe(
        catchError(() => EMPTY),
      ).subscribe(a => this.accountNames.update(m => ({ ...m, [id]: a.name })));
    });
    commodityIds.forEach(id => {
      if (knownC[id]) return;
      this.commodityApi.getCommodity({ id }).pipe(
        catchError(() => EMPTY),
      ).subscribe(c => this.commodities.update(m => ({ ...m, [id]: c })));
    });
  }

  private toViewModel(tx: TransactionResponse, account: AccountResponse): ActivityRowViewModel {
    const dc = this.settingsService.displayCurrency();
    const signed = signedAmountForAccount(tx, account.id) * displaySign(account.accountType);

    // The account-side leg's commodity is the right unit for the row amount —
    // crucial for Investment accounts where balance entries / opening balances
    // can be denominated in any held commodity (currency or security).
    const accountSplit = tx.splits.find(s => s.accountId === account.id);
    const commodityForAmount = accountSplit
      ? this.commodities()[accountSplit.commodityId] ?? null
      : this.commodities()[account.reportingCurrencyId] ?? null;
    const amountText = commodityForAmount
      ? formatCurrency(signed, commodityForAmount)
      : (dc ? formatCurrency(signed, dc) : null);

    const dateText = toDateOnly(tx.occurredAt);

    const title = tx.description?.trim() || shapeLabel(tx.shape);
    let subtitle = '';
    switch (tx.shape) {
      case 'Transfer': {
        const cp = transferCounterparty(tx, account.id);
        const cpName = cp ? (this.accountNames()[cp.accountId] ?? '...') : '';
        subtitle = signed < 0 ? `To ${cpName}` : `From ${cpName}`;
        break;
      }
      case 'BalanceEntry':
        subtitle = 'Balance assertion';
        break;
      case 'OpeningBalance':
        subtitle = 'Account opening balance';
        break;
      case 'StockSplit':
        subtitle = 'Stock split';
        break;
      case 'Other':
        subtitle = '';
        break;
    }

    return {
      tx,
      title,
      subtitle,
      iconName: shapeIconName(tx.shape),
      signed,
      amountText,
      dateText,
    };
  }
}
