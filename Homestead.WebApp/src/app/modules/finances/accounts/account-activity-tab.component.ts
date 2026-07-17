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
  linkedSignal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, firstValueFrom, EMPTY } from 'rxjs';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRightLeft,
  lucideScale,
  lucideFlag,
  lucideCircleDashed,
  lucideTrendingUp,
  lucideTrendingDown,
  lucideBanknote,
  lucideBanknoteArrowDown,
  lucideBanknoteArrowUp,
  lucideChevronLeft,
  lucideChevronRight,
  lucideCircleAlert,
  lucideEllipsisVertical,
  lucideListTodo,
  lucidePaperclip,
  lucideTrash2,
  lucideX,
} from '@ng-icons/lucide';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import {
  type CellContext,
  type ColumnDef,
  type RowSelectionState,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
  injectFlexRenderContext,
} from '@tanstack/angular-table';
import { TableHeadSelection, TableRowSelection } from '@/shared/components/table/selection-column';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { displaySign } from '../shared/sign';
import { formatMaskedCurrency } from '../shared/format-currency';
import { toDateOnly } from '../shared/date';
import {  shapeLabel,
  shapeIconName,
  signedAmountForAccount,
  transferCounterparty,
} from '../transactions/transaction-display';
import { LinkedDocumentsDialogComponent } from '../transactions/linked-documents-dialog/linked-documents-dialog.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';

const PAGE_SIZE = 25;

interface ActivityRowViewModel {
  tx: TransactionResponse;
  iconName: string;
  shapeLabel: string;
  typeSubtitle: string;
  signed: number;
  amountText: string | null;
  dateText: string;
  // The flow's payee name, when attached — rendered above the memo. Null for non-flow rows.
  payeeName: string | null;
}

@Component({
  selector: 'app-activity-shape-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports],
  providers: [provideIcons({
    lucideArrowRightLeft, lucideScale, lucideFlag, lucideCircleDashed,
    lucideTrendingUp, lucideTrendingDown, lucideBanknote,
    lucideBanknoteArrowDown, lucideBanknoteArrowUp,
  })],
  template: `
    <div class="flex items-center gap-2">
      <span class="flex size-9 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <ng-icon hlm [name]="context.row.original.iconName" size="sm" />
      </span>
      <div class="flex min-w-0 flex-col">
        <span class="text-sm">{{ context.row.original.shapeLabel }}</span>
        @if (context.row.original.typeSubtitle) {
          <span class="truncate text-xs text-muted-foreground">{{ context.row.original.typeSubtitle }}</span>
        }
      </div>
    </div>
  `,
})
class ActivityShapeCell {
  protected readonly context = injectFlexRenderContext<CellContext<ActivityRowViewModel, unknown>>();
}

@Component({
  selector: 'app-activity-memo-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (context.row.original.payeeName; as payee) {
      <div class="flex min-w-0 flex-col">
        <span class="truncate text-sm">{{ payee }}</span>
        @if (context.row.original.tx.memo) {
          <span class="truncate text-xs text-muted-foreground">{{ context.row.original.tx.memo }}</span>
        }
      </div>
    } @else {
      <span class="block truncate text-sm">{{ context.row.original.tx.memo }}</span>
    }
  `,
})
class ActivityMemoCell {
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

/** Callbacks the actions cell reaches through TanStack's table meta (precedent: AccountActionsCell). */
interface ActivityTableMeta {
  onLinkedDocuments(tx: TransactionResponse): void;
}

@Component({
  selector: 'app-activity-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePaperclip })],
  template: `
    <div class="flex items-center justify-end gap-1">
      @if (tx.documentLinkCount > 0) {
        <span
          class="inline-flex items-center gap-0.5 text-xs text-muted-foreground"
          [attr.aria-label]="tx.documentLinkCount + ' linked document' + (tx.documentLinkCount === 1 ? '' : 's')"
        >
          <ng-icon hlm name="lucidePaperclip" size="sm" />
          {{ tx.documentLinkCount }}
        </span>
      }
      <button
        hlmBtn
        variant="ghost"
        size="icon-sm"
        type="button"
        [hlmDropdownMenuTrigger]="menu"
        aria-label="Transaction actions"
        (click)="onTriggerClick($event)"
      >
        <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
      </button>
      <ng-template #menu>
        <hlm-dropdown-menu class="w-48">
          <hlm-dropdown-menu-group>
            <button hlmDropdownMenuItem (triggered)="onLinkedDocuments()">
              <ng-icon hlm name="lucidePaperclip" size="sm" />
              Linked documents
            </button>
          </hlm-dropdown-menu-group>
        </hlm-dropdown-menu>
      </ng-template>
    </div>
  `,
})
class ActivityActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<ActivityRowViewModel, unknown>>();
  protected get tx(): TransactionResponse { return this.ctx.row.original.tx; }

  protected onLinkedDocuments(): void {
    (this.ctx.table.options.meta as ActivityTableMeta).onLinkedDocuments(this.tx);
  }

  // The row click opens the edit sheet; the trigger must not bubble into it.
  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }
}

@Component({
  selector: 'app-account-activity-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmSpinner, HlmEmptyImports, HlmTableImports, HlmAlertDialogImports, FlexRenderDirective, LinkedDocumentsDialogComponent],
  providers: [provideIcons({
    lucideArrowRightLeft, lucideScale, lucideFlag, lucideCircleDashed,
    lucideTrendingUp, lucideTrendingDown, lucideBanknote,
    lucideBanknoteArrowDown, lucideBanknoteArrowUp,
    lucideChevronLeft, lucideChevronRight, lucideCircleAlert, lucideListTodo,
    lucideTrash2, lucideX,
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
      @if (hasSelection()) {
        <div class="mb-3 flex items-center gap-2">
          <span class="text-sm text-muted-foreground">
            {{ selectionCount() }} transaction{{ selectionCount() === 1 ? '' : 's' }} selected
          </span>
          <hlm-alert-dialog>
            <button
              hlmBtn
              variant="ghost"
              class="text-destructive hover:bg-destructive/10 hover:text-destructive"
              hlmAlertDialogTrigger
            >
              <ng-icon hlm name="lucideTrash2" size="sm" />
              Delete
            </button>
            <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
              <div hlmAlertDialogHeader>
                <h2 hlmAlertDialogTitle>
                  Delete {{ selectionCount() }} transaction{{ selectionCount() === 1 ? '' : 's' }}?
                </h2>
                <p hlmAlertDialogDescription>
                  This permanently deletes the selected transaction{{ selectionCount() === 1 ? '' : 's' }} and can't be undone.
                  @if (selectionIncludesDrip()) {
                    Reinvested dividends are deleted as whole pairs — the paired half is removed even if it isn't selected.
                  }
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
            size="icon"
            class="ml-auto"
            (click)="clearSelection()"
            aria-label="Clear selection"
          >
            <ng-icon hlm name="lucideX" size="sm" />
          </button>
        </div>
      }
      <div class="overflow-clip rounded-md border">
        <div hlmTableContainer>
          <table hlmTable>
          <thead hlmTHead>
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr>
                @for (header of headerGroup.headers; track header.id) {
                  <th hlmTh [class]="getThClass(header.column.id)">
                    @if (!header.isPlaceholder) {
                      <!-- Component headers (select-all) bypass this template; string headers
                           render through it, so keep interpolation for its auto-escaping. -->
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
                [attr.data-state]="row.getIsSelected() ? 'selected' : undefined"
                (click)="onClick(row.original.tx)"
                (keydown.enter)="onClick(row.original.tx)"
              >
                @for (cell of row.getVisibleCells(); track cell.id) {
                  <!-- The select cell self-isolates its checkbox clicks; the td guard widens
                       the dead zone to the whole cell so near-misses don't open the editor. -->
                  <td
                    hlmTd
                    [class]="getTdClass(cell.column.id)"
                    (click)="isDeadZone(cell.column.id) ? $event.stopPropagation() : null"
                    (keydown.enter)="isDeadZone(cell.column.id) ? $event.stopPropagation() : null"
                  >
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

    <app-linked-documents-dialog
      [state]="linkedDocsState()"
      [transactionId]="linkedDocsTxId()"
      (closed)="onLinkedDocsClosed()"
      (linksChanged)="linksChanged.emit()"
    />
  `,
})
export class AccountActivityTabComponent {
  account = input.required<AccountResponse>();
  refreshKey = input(0);

  transactionClicked = output<TransactionResponse>();
  transactionsDeleted = output<void>();
  /** A document link was added/removed — parent bumps refreshTrigger so documentLinkCount re-fetches. */
  linksChanged = output<void>();

  // Linked-documents dialog state, driven from the actions cell via table meta.
  linkedDocsState = signal<BrnDialogState>('closed');
  linkedDocsTxId = signal<string | null>(null);

  // Resets to 1 whenever the account changes; still writable (paging, clamp).
  page = linkedSignal({ source: this.account, computation: () => 1 });
  loading = signal(false);
  error = signal(false);
  totalCount = signal(0);
  rawItems = signal<TransactionResponse[]>([]);
  accountNames = signal<Record<string, string>>({});
  commodities = signal<Record<string, CommodityResponse>>({});
  bulkDeleting = signal(false);
  // Selection is page-scoped and only meaningful for the currently rendered rows:
  // any account/page change or refresh (which can re-paginate the list) discards it.
  private readonly _rowSelection = linkedSignal<unknown, RowSelectionState>({
    source: () => [this.account(), this.page(), this.refreshKey()],
    computation: () => ({}),
  });

  selectedIds = computed(() => {
    const selection = this._rowSelection();
    return Object.keys(selection).filter(k => selection[k]);
  });
  selectionCount = computed(() => this.selectedIds().length);
  hasSelection = computed(() => this.selectionCount() > 0);
  selectionIncludesDrip = computed(() => {
    const selected = new Set(this.selectedIds());
    return this.rawItems().some(tx => selected.has(tx.id) && tx.relatedTransactionId != null);
  });

  private txApi = inject(FinancesTransactionApiService);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private settingsService = inject(FinanceSettingsService);
  private prefs = inject(UserPreferencesService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  items = computed(() => {
    const a = this.account();
    const txs = this.rawItems();
    return txs.map(tx => this.toViewModel(tx, a));
  });

  private readonly _columns: ColumnDef<ActivityRowViewModel>[] = [
    {
      id: 'select',
      header: () => flexRenderComponent(TableHeadSelection),
      cell: () => flexRenderComponent(TableRowSelection),
      enableSorting: false,
    },
    {
      id: 'date',
      accessorFn: r => r.dateText,
      header: 'Date',
      cell: info => info.getValue<string>(),
      enableSorting: false,
    },
    {
      id: 'type',
      header: 'Type',
      cell: () => flexRenderComponent(ActivityShapeCell),
      enableSorting: false,
    },
    {
      id: 'memo',
      header: 'Memo',
      cell: () => flexRenderComponent(ActivityMemoCell),
      enableSorting: false,
    },
    {
      id: 'amount',
      header: 'Amount',
      cell: () => flexRenderComponent(ActivityAmountCell),
      enableSorting: false,
    },
    {
      id: 'actions',
      header: '',
      cell: () => flexRenderComponent(ActivityActionsCell),
      enableSorting: false,
    },
  ];

  protected readonly table = createAngularTable<ActivityRowViewModel>(() => ({
    data: this.items(),
    columns: this._columns,
    getRowId: r => r.tx.id,
    getCoreRowModel: getCoreRowModel(),
    // Opening balances delete through their dedicated editors; 'Other' shapes have no
    // generic delete path. Both stay un-selectable so bulk delete can't target them.
    enableRowSelection: row =>
      row.original.tx.shape !== 'OpeningBalance' && row.original.tx.shape !== 'Other',
    onRowSelectionChange: updater => {
      this._rowSelection.set(updater instanceof Function ? updater(this._rowSelection()) : updater);
    },
    state: {
      rowSelection: this._rowSelection(),
    },
    meta: {
      onLinkedDocuments: (tx: TransactionResponse) => this.openLinkedDocuments(tx),
    } satisfies ActivityTableMeta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    select: 'w-10',
    amount: 'text-right',
    actions: 'w-16 text-right',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    select: 'w-10',
    memo: 'min-w-0',
    amount: 'text-right',
    actions: 'w-16 text-right',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  // Cells whose clicks must never bubble into the row's open-editor handler.
  protected isDeadZone(columnId: string): boolean {
    return columnId === 'select' || columnId === 'actions';
  }

  constructor() {
    // Deleting every row on the last page shrinks totalPages below the current
    // page; clamp back so the user isn't stranded on an empty page. Skipped
    // while errored — the error path resets totalCount to 0, and clamping on
    // that would yank the user to page 1 over a transient fetch failure.
    effect(() => {
      const totalPages = this.totalPages();
      if (this.error()) return;
      if (untracked(this.page) > totalPages) this.page.set(totalPages);
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

  openLinkedDocuments(tx: TransactionResponse): void {
    this.linkedDocsTxId.set(tx.id);
    this.linkedDocsState.set('open');
  }

  onLinkedDocsClosed(): void {
    this.linkedDocsState.set('closed');
    // Clearing the id makes every open an id change, so the dialog fetches exactly once per open
    // (including re-opening the same transaction) without an extra on-open refresh signal.
    this.linkedDocsTxId.set(null);
  }

  clearSelection(): void {
    this._rowSelection.set({});
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = this.selectedIds();
    if (ids.length === 0) return;
    this.bulkDeleting.set(true);
    const result$ = this.txApi.bulkDeleteTransactions({ body: { ids } }).pipe(
      handleMutationError(this.errorHandler, this.bulkDeleting, 'Failed to delete transactions.'),
    );
    toast.promise(
      firstValueFrom(result$).then(response => {
        this.bulkDeleting.set(false);
        ctx.close();
        const count = +response.deletedCount;
        this.clearSelection();
        this.transactionsDeleted.emit();
        return `${count} transaction${count === 1 ? '' : 's'} deleted.`;
      }),
      { loading: 'Deleting transactions...', success: m => m },
    );
  }

  private resolveLookups(txs: TransactionResponse[]): void {
    const accountId = this.account()?.id;
    const accountIds = new Set<string>();
    const commodityIds = new Set<string>();
    for (const tx of txs) {
      for (const s of tx.splits) {
        commodityIds.add(s.commodityId);
      }
      if (tx.shape === 'Transfer' && accountId) {
        const cp = transferCounterparty(tx, accountId);
        if (cp) accountIds.add(cp.accountId);
      }
      if (tx.details?.kind === 'balanceEntry') {
        commodityIds.add(tx.details.assertedCommodityId);
      }
    }
    const knownA = this.accountNames();
    const knownC = this.commodities();
    accountIds.forEach(id => {
      if (knownA[id]) return;
      this.accountApi.getAccount({ id }).pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(a => this.accountNames.update(m => ({ ...m, [id]: a.name })));
    });
    commodityIds.forEach(id => {
      if (knownC[id]) return;
      this.commodityApi.getCommodity({ id }).pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(c => this.commodities.update(m => ({ ...m, [id]: c })));
    });
  }

  private toViewModel(tx: TransactionResponse, account: AccountResponse): ActivityRowViewModel {
    const vm = (tx.shape === 'InvestmentBuy' || tx.shape === 'InvestmentSell')
      ? this.tradeViewModel(tx, account)
      : this.genericViewModel(tx, account);

    // A DRIP renders as two rows (K2): the Dividend half + the Buy half, each carrying a
    // non-null relatedTransactionId. Cue the pairing in the subtitle so it reads legibly in
    // the list — "Reinvested" on the otherwise-bare Dividend row, "… · reinvested" on the Buy row.
    if (tx.relatedTransactionId != null) {
      vm.typeSubtitle = vm.typeSubtitle ? `${vm.typeSubtitle} · reinvested` : 'Reinvested';
    }
    return vm;
  }

  private genericViewModel(tx: TransactionResponse, account: AccountResponse): ActivityRowViewModel {
    const dc = this.settingsService.displayCurrency();
    const masked = this.prefs.financesPrivacyMasked();
    const signed = signedAmountForAccount(tx, account.id) * displaySign(account.accountType);

    // The account-side leg's commodity is the right unit for the row amount —
    // crucial for Investment accounts where balance entries / opening balances
    // can be denominated in any held commodity (currency or security).
    const accountSplit = tx.splits.find(s => s.accountId === account.id);
    const commodityForAmount = accountSplit
      ? this.commodities()[accountSplit.commodityId] ?? null
      : this.commodities()[account.reportingCurrencyId] ?? null;
    const amountText = commodityForAmount
      ? formatMaskedCurrency(signed, commodityForAmount, masked)
      : (dc ? formatMaskedCurrency(signed, dc, masked) : null);

    const dateText = toDateOnly(tx.occurredAt);

    let typeSubtitle = '';
    if (tx.shape === 'Transfer') {
      const cp = transferCounterparty(tx, account.id);
      const cpName = cp ? this.accountNames()[cp.accountId] : null;
      if (cpName) {
        typeSubtitle = signed < 0 ? `To ${cpName}` : `From ${cpName}`;
      }
    } else if (tx.details?.kind === 'balanceEntry') {
      const assertedCommodity = this.commodities()[tx.details.assertedCommodityId] ?? null;
      if (assertedCommodity) {
        typeSubtitle = formatMaskedCurrency(tx.details.assertedAmount, assertedCommodity, masked);
      }
    } else if (tx.details?.kind === 'flow') {
      // Leaf category name; blank when Uncategorised. Full breadcrumb lives on the edit sheet.
      typeSubtitle = tx.details.categoryName ?? '';
    }

    const payeeName = tx.details?.kind === 'flow' ? (tx.details.payeeName ?? null) : null;

    return {
      tx,
      iconName: shapeIconName(tx.shape),
      shapeLabel: shapeLabel(tx.shape),
      typeSubtitle,
      signed,
      amountText,
      dateText,
      payeeName,
    };
  }

  // Trades park BOTH legs on this account, so the generic same-account-leg logic
  // (sum / first-match) misbehaves. Disambiguate by annotation: the security leg
  // is the priced one (`unitPriceRate`), the cash leg is the plain one. The row
  // shows the signed cash movement (Buy −, Sell +) in the funding currency; the
  // subtitle shows the share count @ unit price.
  private tradeViewModel(tx: TransactionResponse, account: AccountResponse): ActivityRowViewModel {
    const masked = this.prefs.financesPrivacyMasked();
    const cash = tx.splits.find(s => s.unitPriceRate == null);
    const sec = tx.splits.find(s => s.unitPriceRate != null);
    const fundingCommodity = cash ? this.commodities()[cash.commodityId] ?? null : null;

    const signed = (cash?.quantity ?? 0) * displaySign(account.accountType);
    const amountText = cash && fundingCommodity
      ? formatMaskedCurrency(signed, fundingCommodity, masked)
      : null;

    let typeSubtitle = '';
    if (sec) {
      const securityCommodity = this.commodities()[sec.commodityId] ?? null;
      const qtyText = securityCommodity
        ? formatMaskedCurrency(Math.abs(sec.quantity), securityCommodity, masked)
        : (masked ? '•' : String(Math.abs(sec.quantity)));
      const priceText = sec.unitPriceRate != null && fundingCommodity
        ? formatMaskedCurrency(sec.unitPriceRate, fundingCommodity, masked)
        : '';
      typeSubtitle = priceText ? `${qtyText} @ ${priceText}` : qtyText;
    }

    return {
      tx,
      iconName: shapeIconName(tx.shape),
      shapeLabel: shapeLabel(tx.shape),
      typeSubtitle,
      signed,
      amountText,
      dateText: toDateOnly(tx.occurredAt),
      payeeName: null,
    };
  }
}
