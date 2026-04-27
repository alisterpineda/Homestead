import {
  Component,
  ChangeDetectionStrategy,
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
  lucideEllipsisVertical,
  lucideArchive,
  lucideArchiveRestore,
  lucideChevronDown,
  lucidePackagePlus,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { replace } from '@/shared/json-patch';
import { toast } from '@spartan-ng/brain/sonner';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { KindBadgeComponent } from '../shared/kind-badge.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { AccountInfoTabComponent } from './account-info-tab.component';
import { AccountActivityTabComponent } from './account-activity-tab.component';
import { AccountPositionsTabComponent } from './account-positions-tab.component';
import { BalanceEntrySheetComponent, type BalanceEntrySheetMode } from './balance-entry-sheet/balance-entry-sheet.component';
import { TransferSheetComponent, type TransferSheetMode } from './transfer-sheet/transfer-sheet.component';
import { OpeningBalanceSheetComponent, type OpeningBalanceSheetMode } from './opening-balance-sheet/opening-balance-sheet.component';
import { AddPositionSheetComponent } from './add-position-sheet/add-position-sheet.component';
import { formatAccountBalance, formatNativeBalance } from '../shared/sign';
import { toDateOnly } from '../shared/date';
import { isDetailsOfKind } from '../transactions/transaction-display';
import { PageHeaderService } from '@/shared/services/page-header.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { TransactionDetailsBalanceEntryDetails } from '@/api/generated/models/transaction-details-balance-entry-details';
import type { BalanceEntryResponse } from '@/api/generated/models/balance-entry-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';
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
    HlmBreadCrumbImports,
    HlmTabsImports,
    HlmDropdownMenuImports,
    KindBadgeComponent,
    AccountInfoTabComponent,
    AccountActivityTabComponent,
    AccountPositionsTabComponent,
    EditAccountSheetComponent,
    BalanceEntrySheetComponent,
    TransferSheetComponent,
    OpeningBalanceSheetComponent,
    AddPositionSheetComponent,
  ],
  providers: [provideIcons({
    lucidePencil, lucideCircleAlert, lucidePlus, lucideScale, lucideArrowRightLeft,
    lucideEllipsisVertical, lucideArchive, lucideArchiveRestore, lucideChevronDown,
    lucidePackagePlus,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerActions>
      @if (account(); as a) {
        @if (!a.isArchived) {
          @if (a.kind === 'Investment') {
            <button hlmBtn size="sm" (click)="openAddPosition()">
              <ng-icon hlm name="lucidePackagePlus" size="sm" />
              Add position
            </button>
            <button hlmBtn variant="outline" size="sm" (click)="openRecordBalance()">
              <ng-icon hlm name="lucideScale" size="sm" />
              Record balance
            </button>
          } @else {
            <button hlmBtn size="sm" (click)="openRecordBalance()">
              <ng-icon hlm name="lucideScale" size="sm" />
              Record balance
            </button>
          }

          <button hlmBtn variant="outline" size="sm" [hlmDropdownMenuTrigger]="newTxMenu">
            <ng-icon hlm name="lucidePlus" size="sm" />
            New transaction
            <ng-icon hlm name="lucideChevronDown" size="sm" />
          </button>
          <ng-template #newTxMenu>
            <hlm-dropdown-menu class="w-48">
              <hlm-dropdown-menu-group>
                <button hlmDropdownMenuItem (triggered)="openNewTransfer()">
                  <ng-icon hlm name="lucideArrowRightLeft" size="sm" />
                  Transfer
                </button>
              </hlm-dropdown-menu-group>
            </hlm-dropdown-menu>
          </ng-template>
        }

        <button hlmBtn variant="ghost" size="icon-sm" [hlmDropdownMenuTrigger]="accountMenu" aria-label="Account actions">
          <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
        </button>
        <ng-template #accountMenu>
          <hlm-dropdown-menu class="w-44">
            <hlm-dropdown-menu-group>
              <button hlmDropdownMenuItem (triggered)="openEdit()">
                <ng-icon hlm name="lucidePencil" size="sm" />
                Edit
              </button>
              <button hlmDropdownMenuItem (triggered)="toggleArchive()">
                <ng-icon hlm [name]="a.isArchived ? 'lucideArchiveRestore' : 'lucideArchive'" size="sm" />
                {{ a.isArchived ? 'Unarchive' : 'Archive' }}
              </button>
            </hlm-dropdown-menu-group>
          </hlm-dropdown-menu>
        </ng-template>
      }
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (loading()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Account not found</div>
            <div hlmEmptyDescription>The account couldn't be loaded.</div>
          </div>
          <div hlmEmptyContent>
            <a hlmBtn variant="outline" routerLink="/finances/accounts">Back to accounts</a>
          </div>
        </div>
      } @else if (account(); as a) {
        <nav>
          <ol hlmBreadcrumbList>
            <li hlmBreadcrumbItem>
              <a hlmBreadcrumbLink link="/finances/accounts">Accounts</a>
            </li>
            <li hlmBreadcrumbSeparator></li>
            <li hlmBreadcrumbItem>
              <span hlmBreadcrumbPage>{{ a.name }}</span>
            </li>
          </ol>
        </nav>

        <header class="flex flex-col gap-3 rounded-lg border border-border bg-card p-4 text-card-foreground sm:flex-row sm:items-start sm:justify-between">
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
          <div class="text-right shrink-0 tabular-nums">
            <p class="text-xs uppercase tracking-wide text-muted-foreground">Balance</p>
            <p class="text-3xl font-semibold">{{ displayText() ?? '—' }}</p>
            @if (showNative() && nativeText(); as nt) {
              <p class="text-sm text-muted-foreground">{{ nt }}</p>
            }
          </div>
        </header>

        <hlm-tabs tab="activity" class="flex flex-1 flex-col">
          <hlm-tabs-list variant="line">
            <button hlmTabsTrigger="activity">Activity</button>
            @if (a.kind === 'Investment') {
              <button hlmTabsTrigger="positions">Positions</button>
            }
            <button hlmTabsTrigger="info">Info</button>
          </hlm-tabs-list>
          <div hlmTabsContent="activity" class="rounded-lg border border-border bg-card p-4 text-card-foreground">
            <app-account-activity-tab
              [account]="a"
              [refreshKey]="refreshTrigger()"
              (transactionClicked)="onTransactionClicked($event)"
            />
          </div>
          @if (a.kind === 'Investment') {
            <div hlmTabsContent="positions" class="rounded-lg border border-border bg-card p-4 text-card-foreground">
              <app-account-positions-tab
                [account]="a"
                [refreshKey]="refreshTrigger()"
                (editOpeningBalance)="onPositionEditRequested($event.commodityId)"
              />
            </div>
          }
          <div hlmTabsContent="info" class="rounded-lg border border-border bg-card p-4 text-card-foreground">
            <app-account-info-tab [account]="a" [reportingCurrency]="reportingCurrency()" />
          </div>
        </hlm-tabs>
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
  private errorHandler = inject(ErrorHandlerService);
  protected settingsService = inject(FinanceSettingsService);
  private pageHeaderService = inject(PageHeaderService);
  private router = inject(Router);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  editSheetState = signal<BrnDialogState>('closed');

  balanceSheetState = signal<BrnDialogState>('closed');
  balanceSheetMode = signal<BalanceEntrySheetMode>('create');
  balanceSheetExisting = signal<BalanceEntryResponse | null>(null);

  transferSheetState = signal<BrnDialogState>('closed');
  transferSheetMode = signal<TransferSheetMode>('create');
  transferSheetExisting = signal<TransactionResponse | null>(null);

  openingBalanceSheetState = signal<BrnDialogState>('closed');
  openingBalanceSheetMode = signal<OpeningBalanceSheetMode>('edit');
  openingBalanceSheetExisting = signal<OpeningBalanceResponse | null>(null);

  addPositionSheetState = signal<BrnDialogState>('closed');

  archiving = signal(false);

  reportingCurrency = signal<CommodityResponse | null>(null);

  private headerActionsTpl = viewChild.required<TemplateRef<unknown>>('headerActions');

  private accountId = toSignal(
    this.route.paramMap.pipe(map(p => p.get('id'))),
    { initialValue: null },
  );

  account = toSignal(
    combineLatest([toObservable(this.accountId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        this.error.set(false);
        return this.accountApi.getAccount({ id }).pipe(
          map(a => {
            this.loading.set(false);
            return a;
          }),
          catchError(() => {
            this.loading.set(false);
            this.error.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null as AccountResponse | null },
  );

  displayText = computed(() => {
    const a = this.account();
    return a ? formatAccountBalance(a, this.settingsService.displayCurrency()) : null;
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
    return formatNativeBalance(a, this.reportingCurrency());
  }

  constructor() {
    afterNextRender(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });

    // Resolve reporting currency commodity when the account loads.
    toObservable(this.account).pipe(
      filter((a): a is AccountResponse => a !== null),
      switchMap(a => this.commodityApi.getCommodity({ id: a.reportingCurrencyId }).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(),
    ).subscribe(c => this.reportingCurrency.set(c));
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
  }

  // ─── Header actions ──────────────────────────────────────────────────────

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

  openAddPosition(): void {
    this.addPositionSheetState.set('open');
  }

  toggleArchive(): void {
    const a = this.account();
    if (!a) return;
    this.archiving.set(true);
    this.accountApi.patchAccount({
      id: a.id,
      body: [replace('/isArchived', !a.isArchived)],
    }).pipe(
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update account.'),
    ).subscribe({
      next: () => {
        this.archiving.set(false);
        toast.success(a.isArchived ? 'Account unarchived.' : 'Account archived.');
        this.refreshTrigger.update(v => v + 1);
      },
    });
  }

  // ─── Activity-row clicks ─────────────────────────────────────────────────

  onTransactionClicked(tx: TransactionResponse): void {
    const a = this.account();
    if (!a) return;
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
      // StockSplit and 'Other' are read-only on single-position account surfaces.
    }
  }

  private openBalanceEntryEditor(tx: TransactionResponse): void {
    const details = tx.details;
    if (!isDetailsOfKind<TransactionDetailsBalanceEntryDetails>(details, 'balanceEntry')) return;
    const existing: BalanceEntryResponse = {
      transactionId: tx.id,
      accountId: details.accountId,
      commodityId: details.assertedCommodityId,
      assertedAmount: details.assertedAmount,
      date: toDateOnly(tx.occurredAt),
      note: details.note ?? null,
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
    this.txApi.getOpeningBalance({ accountId: a.id, commodityId }).subscribe({
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
  onTransferSheetClosed(): void {
    this.transferSheetState.set('closed');
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
