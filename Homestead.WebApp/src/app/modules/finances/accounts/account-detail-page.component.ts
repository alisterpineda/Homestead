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
import { lucidePencil, lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { KindBadgeComponent } from '../shared/kind-badge.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { AccountInfoTabComponent } from './account-info-tab.component';
import { formatAccountBalance, formatNativeBalance } from '../shared/sign';
import { PageHeaderService } from '@/shared/services/page-header.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
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
    KindBadgeComponent,
    AccountInfoTabComponent,
    EditAccountSheetComponent,
  ],
  providers: [provideIcons({ lucidePencil, lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerActions>
      @if (account(); as a) {
        <button hlmBtn variant="outline" size="sm" (click)="openEdit()">
          <ng-icon hlm name="lucidePencil" size="sm" />
          Edit
        </button>
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

        <hlm-tabs tab="info" class="flex flex-1 flex-col">
          <hlm-tabs-list variant="line">
            <button hlmTabsTrigger="info">Info</button>
          </hlm-tabs-list>
          <div hlmTabsContent="info" class="rounded-lg border border-border bg-card p-4 text-card-foreground">
            <app-account-info-tab [account]="a" [reportingCurrency]="reportingCurrency()" />
          </div>
        </hlm-tabs>
      }
    </div>

    <app-edit-account-sheet
      [account]="account()"
      [state]="sheetState()"
      (saved)="onSheetSaved($event)"
      (closed)="onSheetClosed()"
    />
  `,
})
export class AccountDetailPageComponent implements OnDestroy {
  private route = inject(ActivatedRoute);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  protected settingsService = inject(FinanceSettingsService);
  private pageHeaderService = inject(PageHeaderService);
  private router = inject(Router);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  sheetState = signal<BrnDialogState>('closed');

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

  openEdit(): void {
    this.sheetState.set('open');
  }

  onSheetSaved(_: AccountResponse): void {
    this.sheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
  }
}
