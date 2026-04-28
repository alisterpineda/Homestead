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
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type CellContext,
  type ColumnDef,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
  injectFlexRenderContext,
} from '@tanstack/angular-table';
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
  shapeLabel: string;
  signed: number;
  amountText: string | null;
  dateText: string;
}

@Component({
  selector: 'app-activity-shape-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports],
  providers: [provideIcons({
    lucideArrowRightLeft, lucideScale, lucideFlag, lucideSplit, lucideCircleDashed,
  })],
  template: `
    <div class="flex items-center gap-2">
      <span class="flex size-9 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <ng-icon hlm [name]="context.row.original.iconName" size="sm" />
      </span>
      <span class="text-sm">{{ context.row.original.shapeLabel }}</span>
    </div>
  `,
})
class ActivityShapeCell {
  protected readonly context = injectFlexRenderContext<CellContext<ActivityRowViewModel, unknown>>();
}

@Component({
  selector: 'app-activity-description-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-w-0 flex-col">
      <span class="truncate text-sm font-medium">{{ context.row.original.title }}</span>
      @if (context.row.original.subtitle) {
        <span class="truncate text-xs text-muted-foreground">{{ context.row.original.subtitle }}</span>
      }
    </div>
  `,
})
class ActivityDescriptionCell {
  protected readonly context = injectFlexRenderContext<CellContext<ActivityRowViewModel, unknown>>();
}

@Component({
  selector: 'app-activity-amount-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="text-sm font-medium tabular-nums"
      [class.text-destructive]="context.row.original.signed < 0"
    >
      {{ context.row.original.amountText ?? '—' }}
    </span>
  `,
})
class ActivityAmountCell {
  protected readonly context = injectFlexRenderContext<CellContext<ActivityRowViewModel, unknown>>();
}

@Component({
  selector: 'app-account-activity-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmSpinner, HlmEmptyImports, HlmTableImports, FlexRenderDirective],
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
      <div class="overflow-clip rounded-md border">
        <div hlmTableContainer>
          <table hlmTable>
          <thead hlmTHead>
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr>
                @for (header of headerGroup.headers; track header.id) {
                  <th hlmTh [class]="getThClass(header.column.id)">
                    @if (!header.isPlaceholder) {
                      <ng-container *flexRender="header.column.columnDef.header; props: header.getContext(); let h">
                        <span>{{ h }}</span>
                      </ng-container>
                    }
                  </th>
                }
              </tr>
            }
          </thead>
          <tbody hlmTBody>
            @for (row of table.getRowModel().rows; track row.id) {
              <tr
                hlmTr
                tabindex="0"
                class="cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                (click)="onClick(row.original.tx)"
                (keydown.enter)="onClick(row.original.tx)"
              >
                @for (cell of row.getVisibleCells(); track cell.id) {
                  <td hlmTd [class]="getTdClass(cell.column.id)">
                    <ng-container *flexRender="cell.column.columnDef.cell; props: cell.getContext(); let c">
                      <div [innerHTML]="c"></div>
                    </ng-container>
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
        </div>
      </div>

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

  private readonly _columns: ColumnDef<ActivityRowViewModel>[] = [
    {
      id: 'date',
      accessorFn: r => r.dateText,
      header: 'Date',
      cell: info => `<span class="tabular-nums text-muted-foreground">${info.getValue<string>()}</span>`,
      enableSorting: false,
    },
    {
      id: 'type',
      header: 'Type',
      cell: () => flexRenderComponent(ActivityShapeCell),
      enableSorting: false,
    },
    {
      id: 'description',
      header: 'Description',
      cell: () => flexRenderComponent(ActivityDescriptionCell),
      enableSorting: false,
    },
    {
      id: 'amount',
      header: 'Amount',
      cell: () => flexRenderComponent(ActivityAmountCell),
      enableSorting: false,
    },
  ];

  protected readonly table = createAngularTable<ActivityRowViewModel>(() => ({
    data: this.items(),
    columns: this._columns,
    getRowId: r => r.tx.id,
    getCoreRowModel: getCoreRowModel(),
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    date: 'hidden sm:table-cell',
    amount: 'text-right',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    date: 'hidden sm:table-cell',
    description: 'min-w-0',
    amount: 'text-right',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

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
      shapeLabel: shapeLabel(tx.shape),
      signed,
      amountText,
      dateText,
    };
  }
}
