import {
  Component,
  ChangeDetectionStrategy,
  DestroyRef,
  inject,
  signal,
  computed,
  viewChild,
  TemplateRef,
  afterNextRender,
  OnDestroy,
} from '@angular/core';
import { ActivatedRoute, RouterLink, Router, NavigationEnd } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, catchError, map, filter, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePencil,
  lucideCircleAlert,
  lucidePlus,
  lucideScale,
  lucideArrowRightLeft,
  lucideChevronDown,
  lucidePackagePlus,
  lucideTrendingUp,
  lucideTrendingDown,
  lucideBanknote,
  lucideBanknoteArrowDown,
  lucideBanknoteArrowUp,
  lucideRepeat,
  lucideFileUp,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadcrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { toast } from '@spartan-ng/brain/sonner';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { KindBadgeComponent } from '../shared/kind-badge.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { AccountActivityTabComponent } from './account-activity-tab.component';
import { AccountPositionsTabComponent } from './account-positions-tab.component';
import { BalanceEntrySheetComponent, type BalanceEntrySheetMode } from './balance-entry-sheet/balance-entry-sheet.component';
import { TransferSheetComponent, type TransferSheetMode } from './transfer-sheet/transfer-sheet.component';
import { TradeSheetComponent, type TradeSheetMode } from './trade-sheet/trade-sheet.component';
import { DividendSheetComponent, type DividendSheetMode } from './dividend-sheet/dividend-sheet.component';
import { FlowSheetComponent, type FlowSheetMode, type FlowDirection } from './flow-sheet/flow-sheet.component';
import { DripSheetComponent, type DripSheetMode } from './drip-sheet/drip-sheet.component';
import { OpeningBalanceSheetComponent, type OpeningBalanceSheetMode } from './opening-balance-sheet/opening-balance-sheet.component';
import { AddPositionSheetComponent } from './add-position-sheet/add-position-sheet.component';
import { StatementImportDialogComponent } from './statement-import-dialog/statement-import-dialog.component';
import { formatAccountBalance, formatNativeBalance } from '../shared/sign';
import { toDateOnly } from '../shared/date';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { handleDetailLoadError } from '@/shared/utils/rxjs-error.utils';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { BalanceEntryResponse } from '@/api/generated/models/balance-entry-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';
import type { TradeSide } from '@/api/generated/models/trade-side';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-account-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmBreadcrumbImports,
    HlmDropdownMenuImports,
    KindBadgeComponent,
    AccountActivityTabComponent,
    AccountPositionsTabComponent,
    EditAccountSheetComponent,
    BalanceEntrySheetComponent,
    TransferSheetComponent,
    TradeSheetComponent,
    DividendSheetComponent,
    FlowSheetComponent,
    DripSheetComponent,
    OpeningBalanceSheetComponent,
    AddPositionSheetComponent,
    StatementImportDialogComponent,
  ],
  providers: [provideIcons({
    lucidePencil, lucideCircleAlert, lucidePlus, lucideScale, lucideArrowRightLeft,
    lucideChevronDown, lucidePackagePlus,
    lucideTrendingUp, lucideTrendingDown, lucideBanknote, lucideRepeat,
    lucideBanknoteArrowDown, lucideBanknoteArrowUp, lucideFileUp,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/finances/accounts">Accounts</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem class="min-w-0">
            <span hlmBreadcrumbPage
                  [title]="account()?.name ?? 'Account'"
                  class="truncate max-w-[150px] sm:max-w-[250px] md:max-w-[350px]">
              {{ account()?.name ?? 'Account' }}
            </span>
          </li>
        </ol>
      </nav>
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (account(); as a) {
        @if (loadError()) {
          <div class="flex items-center justify-between gap-3 rounded-md border border-dashed border-destructive/50 p-3 text-sm text-destructive">
            <span>Couldn't refresh. Showing the last loaded version.</span>
            <button type="button" hlmBtn variant="outline" (click)="retry()">Retry</button>
          </div>
        }
        <header class="flex flex-col gap-4 rounded-lg border border-border bg-card p-4 text-card-foreground">
          <div class="flex flex-row items-start justify-between gap-3">
            <div class="flex flex-col gap-2 min-w-0">
              <div class="flex items-center gap-2 flex-wrap">
                <h1 class="text-2xl font-semibold truncate">{{ a.name }}</h1>
                <app-kind-badge [kind]="a.kind" />
                @if (a.isArchived) {
                  <span class="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">Archived</span>
                }
              </div>
              @if (a.institution) {
                <p class="text-sm text-muted-foreground">{{ a.institution }}</p>
              }
            </div>
            <button hlmBtn variant="outline" class="shrink-0" (click)="openEdit()">
              <ng-icon hlm name="lucidePencil" size="sm" />
              Edit
            </button>
          </div>
          <div class="flex flex-row justify-end tabular-nums">
            <div class="text-right">
              <p class="text-xs uppercase tracking-wide text-muted-foreground">Balance</p>
              <p class="text-3xl font-semibold">{{ displayText() ?? '—' }}</p>
              @if (showNative() && nativeText(); as nt) {
                <p class="text-sm text-muted-foreground">{{ nt }}</p>
              }
            </div>
          </div>
        </header>

        @if (!a.isArchived) {
          <div class="flex flex-wrap items-center justify-end gap-2">
            @if (a.kind === 'Investment') {
              <button hlmBtn (click)="openAddPosition()">
                <ng-icon hlm name="lucidePackagePlus" size="sm" />
                Add position
              </button>
            } @else if (openingBalanceExists() === false) {
              <button hlmBtn variant="outline" (click)="openAddOpeningBalance()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                Add opening balance
              </button>
            }

            <button hlmBtn [hlmDropdownMenuTrigger]="newTxMenu">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New transaction
              <ng-icon hlm name="lucideChevronDown" size="sm" />
            </button>

            @if (a.kind !== 'Investment') {
              <app-statement-import-dialog [accountId]="a.id">
                <ng-icon hlm name="lucideFileUp" size="sm" />
                Import statement
              </app-statement-import-dialog>
            }
            <ng-template #newTxMenu>
              <hlm-dropdown-menu class="w-52">
                @if (a.kind !== 'Investment') {
                  <hlm-dropdown-menu-group>
                    <button hlmDropdownMenuItem (triggered)="openNewFlow('Expense')">
                      <ng-icon hlm name="lucideBanknoteArrowDown" size="sm" />
                      Expense
                    </button>
                    <button hlmDropdownMenuItem (triggered)="openNewFlow('Income')">
                      <ng-icon hlm name="lucideBanknoteArrowUp" size="sm" />
                      Income
                    </button>
                  </hlm-dropdown-menu-group>
                  <hlm-dropdown-menu-separator />
                }
                <hlm-dropdown-menu-group>
                  <button hlmDropdownMenuItem (triggered)="openNewTransfer()">
                    <ng-icon hlm name="lucideArrowRightLeft" size="sm" />
                    Transfer
                  </button>
                  <button hlmDropdownMenuItem (triggered)="openRecordBalance()">
                    <ng-icon hlm name="lucideScale" size="sm" />
                    Record balance
                  </button>
                </hlm-dropdown-menu-group>
                @if (a.kind === 'Investment') {
                  <hlm-dropdown-menu-separator />
                  <hlm-dropdown-menu-label>Trade</hlm-dropdown-menu-label>
                  <hlm-dropdown-menu-group>
                    <button hlmDropdownMenuItem (triggered)="openTrade('Buy')">
                      <ng-icon hlm name="lucideTrendingUp" size="sm" />
                      Buy
                    </button>
                    <button hlmDropdownMenuItem (triggered)="openTrade('Sell')">
                      <ng-icon hlm name="lucideTrendingDown" size="sm" />
                      Sell
                    </button>
                    <button hlmDropdownMenuItem (triggered)="openNewDividend()">
                      <ng-icon hlm name="lucideBanknote" size="sm" />
                      Dividend
                    </button>
                    <button hlmDropdownMenuItem (triggered)="openNewDrip()">
                      <ng-icon hlm name="lucideRepeat" size="sm" />
                      Reinvest dividend
                    </button>
                  </hlm-dropdown-menu-group>
                }
              </hlm-dropdown-menu>
            </ng-template>
          </div>
        }

        @if (a.kind === 'Investment') {
          <div class="rounded-lg border border-border bg-card p-4 text-card-foreground">
            <app-account-positions-tab
              [account]="a"
              [refreshKey]="refreshTrigger()"
              (editOpeningBalance)="onPositionEditRequested($event.commodityId)"
              (heldCommodityIdsChange)="heldCommodityIds.set($event)"
              (heldCurrencyIdsChange)="heldCurrencyIds.set($event)"
            />
          </div>
        }

        <app-account-activity-tab
          [account]="a"
          [refreshKey]="refreshTrigger()"
          (transactionClicked)="onTransactionClicked($event)"
          (transactionsDeleted)="onTransactionsDeleted()"
        />
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Account not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
          <div hlmEmptyContent>
            <a hlmBtn variant="outline" routerLink="/finances/accounts">Back to accounts</a>
          </div>
        </div>
      } @else if (loadError()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Couldn't load account</div>
            <div hlmEmptyDescription>Something went wrong. Check your connection and try again.</div>
          </div>
          <div hlmEmptyContent class="flex gap-2">
            <button type="button" hlmBtn variant="outline" (click)="retry()">Retry</button>
            <a hlmBtn variant="ghost" routerLink="/finances/accounts">Back to accounts</a>
          </div>
        </div>
      } @else if (loading()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }
    </div>

    <app-edit-account-sheet
      [account]="account()"
      [state]="editSheetState()"
      (saved)="onAccountSaved($event)"
      (closed)="onAccountSheetClosed()"
    />

    <app-balance-entry-sheet
      [account]="account()"
      [state]="balanceSheetState()"
      [mode]="balanceSheetMode()"
      [existing]="balanceSheetExisting()"
      (saved)="onBalanceEntrySaved()"
      (deleted)="onBalanceEntryDeleted()"
      (closed)="onBalanceSheetClosed()"
    />

    <app-transfer-sheet
      [account]="account()"
      [state]="transferSheetState()"
      [mode]="transferSheetMode()"
      [existing]="transferSheetExisting()"
      (saved)="onTransferSaved()"
      (deleted)="onTransferDeleted()"
      (closed)="onTransferSheetClosed()"
    />

    <app-trade-sheet
      [account]="account()"
      [state]="tradeSheetState()"
      [mode]="tradeSheetMode()"
      [side]="tradeSheetSide()"
      [existing]="tradeSheetExisting()"
      [showFundingCcy]="tradeShowFundingCcy()"
      (saved)="onTradeSaved()"
      (deleted)="onTradeDeleted()"
      (closed)="onTradeSheetClosed()"
    />

    <app-dividend-sheet
      [account]="account()"
      [state]="dividendSheetState()"
      [mode]="dividendSheetMode()"
      [existing]="dividendSheetExisting()"
      (saved)="onDividendSaved()"
      (deleted)="onDividendDeleted()"
      (closed)="onDividendSheetClosed()"
    />

    <app-flow-sheet
      [account]="account()"
      [state]="flowSheetState()"
      [mode]="flowSheetMode()"
      [existing]="flowSheetExisting()"
      [direction]="flowSheetDirection()"
      (saved)="onFlowSaved()"
      (deleted)="onFlowDeleted()"
      (closed)="onFlowSheetClosed()"
    />

    <app-drip-sheet
      [account]="account()"
      [state]="dripSheetState()"
      [mode]="dripSheetMode()"
      [existing]="dripSheetExisting()"
      (saved)="onDripSaved()"
      (deleted)="onDripDeleted()"
      (closed)="onDripSheetClosed()"
    />

    <app-opening-balance-sheet
      [account]="account()"
      [state]="openingBalanceSheetState()"
      [mode]="openingBalanceSheetMode()"
      [existing]="openingBalanceSheetExisting()"
      (saved)="onOpeningBalanceSaved()"
      (deleted)="onOpeningBalanceDeleted()"
      (closed)="onOpeningBalanceSheetClosed()"
    />

    <app-add-position-sheet
      [account]="account()"
      [state]="addPositionSheetState()"
      [excludedCommodityIds]="heldCommodityIds()"
      (saved)="onAddPositionSaved()"
      (closed)="onAddPositionSheetClosed()"
    />
  `,
})
export class AccountDetailPageComponent implements OnDestroy {
  private route = inject(ActivatedRoute);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private txApi = inject(FinancesTransactionApiService);
  protected settingsService = inject(FinanceSettingsService);
  private prefs = inject(UserPreferencesService);
  private pageHeaderService = inject(PageHeaderService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  refreshTrigger = signal(0);
  loading = signal(false);
  // Split the old generic `error` into a terminal 404 state and a retryable load error, so a failed
  // refetch (after ~15 mutation handlers bump refreshTrigger) never blanks an already-rendered page.
  notFound = signal(false);
  loadError = signal(false);

  editSheetState = signal<BrnDialogState>('closed');

  balanceSheetState = signal<BrnDialogState>('closed');
  balanceSheetMode = signal<BalanceEntrySheetMode>('create');
  balanceSheetExisting = signal<BalanceEntryResponse | null>(null);

  transferSheetState = signal<BrnDialogState>('closed');
  transferSheetMode = signal<TransferSheetMode>('create');
  transferSheetExisting = signal<TransactionResponse | null>(null);

  tradeSheetState = signal<BrnDialogState>('closed');
  tradeSheetMode = signal<TradeSheetMode>('create');
  tradeSheetSide = signal<TradeSide>('Buy');
  tradeSheetExisting = signal<TransactionResponse | null>(null);

  dividendSheetState = signal<BrnDialogState>('closed');
  dividendSheetMode = signal<DividendSheetMode>('create');
  dividendSheetExisting = signal<TransactionResponse | null>(null);

  flowSheetState = signal<BrnDialogState>('closed');
  flowSheetMode = signal<FlowSheetMode>('create');
  flowSheetExisting = signal<TransactionResponse | null>(null);
  flowSheetDirection = signal<FlowDirection>('Expense');

  dripSheetState = signal<BrnDialogState>('closed');
  dripSheetMode = signal<DripSheetMode>('create');
  dripSheetExisting = signal<TransactionResponse | null>(null);

  // PC1: held currency IDs (from the positions tab). The trade funding-currency
  // picker is revealed only when the account holds cash in a non-reporting currency.
  heldCurrencyIds = signal<string[]>([]);
  tradeShowFundingCcy = computed(() => {
    const reporting = this.account()?.reportingCurrencyId;
    return this.heldCurrencyIds().some(id => id !== reporting);
  });

  openingBalanceSheetState = signal<BrnDialogState>('closed');
  openingBalanceSheetMode = signal<OpeningBalanceSheetMode>('edit');
  openingBalanceSheetExisting = signal<OpeningBalanceResponse | null>(null);

  addPositionSheetState = signal<BrnDialogState>('closed');
  heldCommodityIds = signal<string[]>([]);

  reportingCurrency = signal<CommodityResponse | null>(null);

  private headerTitleTpl = viewChild.required<TemplateRef<unknown>>('headerTitle');

  private accountId = toSignal(
    this.route.paramMap.pipe(map(p => p.get('id'))),
    { initialValue: null },
  );

  account = toSignal(
    combineLatest([toObservable(this.accountId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        this.loadError.set(false);
        return this.accountApi.getAccount({ id }).pipe(
          map(a => {
            this.loading.set(false);
            return a;
          }),
          catchError((err) => handleDetailLoadError(err, { notFound: this.notFound, loadError: this.loadError }, this.loading)),
        );
      }),
    ),
    { initialValue: null as AccountResponse | null },
  );

  // For non-Investment accounts, probe whether an opening balance already exists at
  // (account, reporting currency). Drives the "Add opening balance" action, which is
  // only offered when none exists (Investment accounts use "Add position" per commodity).
  // null = unknown / not applicable (Investment, archived, or in-flight); false = none; true = exists.
  openingBalanceExists = toSignal<boolean | null, boolean | null>(
    toObservable(this.account).pipe(
      switchMap(a => {
        if (!a || a.kind === 'Investment' || a.isArchived) return of<boolean | null>(null);
        return this.txApi.getOpeningBalance({ accountId: a.id, commodityId: a.reportingCurrencyId }).pipe(
          map(() => true),
          catchError((err: unknown) =>
            of<boolean | null>((err as { status?: number })?.status === 404 ? false : null)),
        );
      }),
    ),
    { initialValue: null },
  );

  displayText = computed(() => {
    const a = this.account();
    return a
      ? formatAccountBalance(a, this.settingsService.displayCurrency(), this.prefs.financesPrivacyMasked())
      : null;
  });

  showNative = computed(() => {
    const a = this.account();
    if (!a || a.kind === 'Investment') return false;
    const dc = this.settingsService.displayCurrency();
    if (!dc) return false;
    if (dc.id === a.reportingCurrencyId) return false;
    return a.nativeBalance != null;
  });

  nativeText(): string | null {
    const a = this.account();
    if (!a) return null;
    return formatNativeBalance(a, this.reportingCurrency(), this.prefs.financesPrivacyMasked());
  }

  constructor() {
    afterNextRender(() => {
      this.pageHeaderService.setTitle(this.headerTitleTpl());
    });
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
      this.pageHeaderService.setTitle(this.headerTitleTpl());
    });

    // Resolve reporting currency commodity when the account loads.
    toObservable(this.account).pipe(
      filter((a): a is AccountResponse => a !== null),
      switchMap(a => this.commodityApi.getCommodity({ id: a.reportingCurrencyId }).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(),
    ).subscribe(c => this.reportingCurrency.set(c));
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearTitle();
  }

  // ─── Action handlers ─────────────────────────────────────────────────────

  /** Retry a failed load/refetch — same mechanism the mutation handlers use to refresh. */
  retry(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  openEdit(): void {
    this.editSheetState.set('open');
  }

  openRecordBalance(): void {
    this.balanceSheetMode.set('create');
    this.balanceSheetExisting.set(null);
    this.balanceSheetState.set('open');
  }

  openNewTransfer(): void {
    this.transferSheetMode.set('create');
    this.transferSheetExisting.set(null);
    this.transferSheetState.set('open');
  }

  openTrade(side: TradeSide): void {
    this.tradeSheetSide.set(side);
    this.tradeSheetMode.set('create');
    this.tradeSheetExisting.set(null);
    this.tradeSheetState.set('open');
  }

  openNewDividend(): void {
    this.dividendSheetMode.set('create');
    this.dividendSheetExisting.set(null);
    this.dividendSheetState.set('open');
  }

  openNewFlow(direction: FlowDirection): void {
    this.flowSheetDirection.set(direction);
    this.flowSheetMode.set('create');
    this.flowSheetExisting.set(null);
    this.flowSheetState.set('open');
  }

  openNewDrip(): void {
    this.dripSheetMode.set('create');
    this.dripSheetExisting.set(null);
    this.dripSheetState.set('open');
  }

  openAddPosition(): void {
    this.addPositionSheetState.set('open');
  }

  openAddOpeningBalance(): void {
    this.openingBalanceSheetMode.set('create-missing');
    this.openingBalanceSheetExisting.set(null);
    this.openingBalanceSheetState.set('open');
  }

  // ─── Activity-row clicks ─────────────────────────────────────────────────

  onTransactionClicked(tx: TransactionResponse): void {
    const a = this.account();
    if (!a) return;
    // A DRIP is two linked halves with no dedicated shape (K1): route either clicked half
    // to the DRIP editor before the shape switch, so standalone Dividend/Buy rows still
    // reach their own editors.
    if (tx.relatedTransactionId != null) {
      this.openDripEditor(tx);
      return;
    }
    switch (tx.shape) {
      case 'BalanceEntry':
        this.openBalanceEntryEditor(tx);
        break;
      case 'Transfer':
        this.openTransferEditor(tx);
        break;
      case 'OpeningBalance':
        this.openOpeningBalanceEditor(tx);
        break;
      case 'InvestmentBuy':
      case 'InvestmentSell':
        this.openTradeEditor(tx);
        break;
      case 'Dividend':
        this.openDividendEditor(tx);
        break;
      case 'Expense':
      case 'Income':
        this.openFlowEditor(tx);
        break;
      // 'Other' is read-only on single-position account surfaces.
    }
  }

  private openBalanceEntryEditor(tx: TransactionResponse): void {
    const details = tx.details;
    if (details?.kind !== 'balanceEntry') return;
    const existing: BalanceEntryResponse = {
      transactionId: tx.id,
      accountId: details.accountId,
      commodityId: details.assertedCommodityId,
      assertedAmount: details.assertedAmount,
      date: toDateOnly(tx.occurredAt),
      memo: tx.memo,
    };
    this.balanceSheetMode.set('edit');
    this.balanceSheetExisting.set(existing);
    this.balanceSheetState.set('open');
  }

  private openTransferEditor(tx: TransactionResponse): void {
    this.transferSheetMode.set('edit');
    this.transferSheetExisting.set(tx);
    this.transferSheetState.set('open');
  }

  private openTradeEditor(tx: TransactionResponse): void {
    // In edit mode the sheet derives the (disabled) side from the stored shape, so the
    // `side` input is unused here — only `existing` matters.
    this.tradeSheetMode.set('edit');
    this.tradeSheetExisting.set(tx);
    this.tradeSheetState.set('open');
  }

  private openDividendEditor(tx: TransactionResponse): void {
    this.dividendSheetMode.set('edit');
    this.dividendSheetExisting.set(tx);
    this.dividendSheetState.set('open');
  }

  private openFlowEditor(tx: TransactionResponse): void {
    // The sheet derives the (disabled) direction from the stored shape, so `direction`
    // isn't set here — only `existing` matters.
    this.flowSheetMode.set('edit');
    this.flowSheetExisting.set(tx);
    this.flowSheetState.set('open');
  }

  private openDripEditor(tx: TransactionResponse): void {
    // The sheet seeds from the Buy half: clicking the Buy row needs no fetch, the Dividend
    // row fetches its partner. Either clicked half is passed through as `existing`.
    this.dripSheetMode.set('edit');
    this.dripSheetExisting.set(tx);
    this.dripSheetState.set('open');
  }

  private openOpeningBalanceEditor(tx: TransactionResponse): void {
    const a = this.account();
    if (!a) return;
    // The account-side split's commodity identifies which (account, commodity)
    // pair the opening balance lives at — works for both single-position kinds
    // (always reporting currency) and per-position Investment OBs.
    const accountSplit = tx.splits.find(s => s.accountId === a.id);
    const commodityId = accountSplit?.commodityId ?? a.reportingCurrencyId;
    this.openOpeningBalanceForCommodity(commodityId);
  }

  onPositionEditRequested(commodityId: string): void {
    this.openOpeningBalanceForCommodity(commodityId);
  }

  private openOpeningBalanceForCommodity(commodityId: string): void {
    const a = this.account();
    if (!a) return;
    this.txApi.getOpeningBalance({ accountId: a.id, commodityId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ob => {
          this.openingBalanceSheetMode.set('edit');
          this.openingBalanceSheetExisting.set(ob);
          this.openingBalanceSheetState.set('open');
        },
        error: () => {
          toast.error('Couldn\'t load opening balance.');
        },
      });
  }

  // ─── Sheet event handlers ────────────────────────────────────────────────

  onAccountSaved(_: AccountResponse): void {
    this.editSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onAccountSheetClosed(): void {
    this.editSheetState.set('closed');
  }

  onBalanceEntrySaved(): void {
    this.balanceSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onBalanceEntryDeleted(): void {
    this.balanceSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onBalanceSheetClosed(): void {
    this.balanceSheetState.set('closed');
  }

  onTransferSaved(): void {
    this.transferSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onTransferDeleted(): void {
    this.transferSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onTransactionsDeleted(): void {
    this.refreshTrigger.update(v => v + 1);
  }
  onTransferSheetClosed(): void {
    this.transferSheetState.set('closed');
  }

  onTradeSaved(): void {
    this.tradeSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onTradeDeleted(): void {
    this.tradeSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onTradeSheetClosed(): void {
    this.tradeSheetState.set('closed');
  }

  onDividendSaved(): void {
    this.dividendSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onDividendDeleted(): void {
    this.dividendSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onDividendSheetClosed(): void {
    this.dividendSheetState.set('closed');
  }

  onFlowSaved(): void {
    this.flowSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onFlowDeleted(): void {
    this.flowSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onFlowSheetClosed(): void {
    this.flowSheetState.set('closed');
  }

  onDripSaved(): void {
    this.dripSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onDripDeleted(): void {
    this.dripSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onDripSheetClosed(): void {
    this.dripSheetState.set('closed');
  }

  onOpeningBalanceSaved(): void {
    this.openingBalanceSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onOpeningBalanceDeleted(): void {
    this.openingBalanceSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onOpeningBalanceSheetClosed(): void {
    this.openingBalanceSheetState.set('closed');
  }

  onAddPositionSaved(): void {
    this.addPositionSheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }
  onAddPositionSheetClosed(): void {
    this.addPositionSheetState.set('closed');
  }
}
