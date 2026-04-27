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
import { Router, NavigationEnd } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, debounceTime, distinctUntilChanged, combineLatest, filter, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideCircleAlert, lucideCoins, lucideSearch } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { toast } from '@spartan-ng/brain/sonner';
import { CommodityRowActionsComponent } from './commodity-row-actions.component';
import { CreateCommodityDialogComponent } from './create-commodity-dialog/create-commodity-dialog.component';
import { EditCommoditySheetComponent } from './edit-commodity-sheet/edit-commodity-sheet.component';
import { formatRelativeAge } from './relative-age';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CommodityKind } from '@/api/generated/models/commodity-kind';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

type TabKey = 'securities' | 'currencies';

@Component({
  selector: 'app-commodity-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmInput,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmTabsImports,
    HlmTableImports,
    HlmBadgeImports,
    CommodityRowActionsComponent,
    CreateCommodityDialogComponent,
    EditCommoditySheetComponent,
  ],
  providers: [provideIcons({ lucidePlus, lucideCircleAlert, lucideCoins, lucideSearch })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerActions>
      @if (activeTab() === 'securities') {
        <app-create-commodity-dialog triggerSize="sm" (created)="onChanged()">
          <ng-icon hlm name="lucidePlus" size="sm" />
          New security
        </app-create-commodity-dialog>
      }
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      <hlm-tabs [tab]="activeTab()" (tabActivated)="onTabChange($event)" class="flex flex-1 flex-col gap-4">
        <hlm-tabs-list variant="line">
          <button hlmTabsTrigger="securities">Securities</button>
          <button hlmTabsTrigger="currencies">Currencies</button>
        </hlm-tabs-list>

        <div class="relative max-w-sm">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="pl-9"
            type="search"
            [placeholder]="activeTab() === 'securities' ? 'Search by symbol or name' : 'Search currencies'"
            [value]="searchInput()"
            (input)="searchInput.set(asValue($event))"
          />
        </div>

        <div hlmTabsContent="securities" class="flex-1">
          @if (loading() && !data()) {
            <div class="flex justify-center py-16"><hlm-spinner /></div>
          } @else if (error()) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
                <div hlmEmptyTitle>Failed to load commodities</div>
              </div>
            </div>
          } @else {
            @if (rows().length === 0) {
              <div hlmEmpty>
                <div hlmEmptyHeader>
                  <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCoins" /></div>
                  <div hlmEmptyTitle>{{ searchTerm() ? 'No matches' : 'No securities yet' }}</div>
                  <div hlmEmptyDescription>
                    {{ searchTerm() ? 'Try a different symbol or name.' : 'Add a security to start tracking its price.' }}
                  </div>
                </div>
                @if (!searchTerm()) {
                  <div hlmEmptyContent>
                    <app-create-commodity-dialog (created)="onChanged()">
                      <ng-icon hlm name="lucidePlus" size="sm" />
                      New security
                    </app-create-commodity-dialog>
                  </div>
                }
              </div>
            } @else {
              <div class="overflow-x-auto rounded-md border border-border">
                <table hlmTable class="w-full">
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh>Symbol</th>
                      <th hlmTh>Exchange</th>
                      <th hlmTh>Name</th>
                      <th hlmTh class="text-right">Precision</th>
                      <th hlmTh class="text-right"># prices</th>
                      <th hlmTh>Latest price</th>
                      <th hlmTh class="w-12"></th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (row of rows(); track row.id) {
                      <tr hlmTr class="cursor-pointer" (click)="openDetail(row)">
                        <td hlmTd class="font-medium">{{ row.symbol }}</td>
                        <td hlmTd>
                          @if (row.exchange) {
                            <span hlmBadge variant="outline">{{ row.exchange }}</span>
                          }
                        </td>
                        <td hlmTd class="text-muted-foreground">{{ row.name }}</td>
                        <td hlmTd class="text-right tabular-nums">{{ row.displayPrecision }}</td>
                        <td hlmTd class="text-right tabular-nums">{{ row.priceCount }}</td>
                        <td hlmTd class="text-muted-foreground">{{ formatLatest(row.latestPriceAt) }}</td>
                        <td hlmTd class="w-12">
                          <app-commodity-row-actions
                            [commodity]="row"
                            (edit)="onEdit($event)"
                            (delete)="onDeleteRequest($event)"
                          />
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          }
        </div>

        <div hlmTabsContent="currencies" class="flex-1">
          @if (loading() && !data()) {
            <div class="flex justify-center py-16"><hlm-spinner /></div>
          } @else {
            @if (rows().length === 0) {
              <div hlmEmpty>
                <div hlmEmptyHeader>
                  <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCoins" /></div>
                  <div hlmEmptyTitle>No matches</div>
                  <div hlmEmptyDescription>Try a different symbol or name.</div>
                </div>
              </div>
            } @else {
              <div class="overflow-x-auto rounded-md border border-border">
                <table hlmTable class="w-full">
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh>Symbol</th>
                      <th hlmTh>Name</th>
                      <th hlmTh class="text-right">Precision</th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (row of rows(); track row.id) {
                      <tr hlmTr class="cursor-pointer" (click)="openDetail(row)">
                        <td hlmTd class="font-medium">{{ row.symbol }}</td>
                        <td hlmTd class="text-muted-foreground">{{ row.name }}</td>
                        <td hlmTd class="text-right tabular-nums">{{ row.displayPrecision }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          }
        </div>
      </hlm-tabs>
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
export class CommodityListPageComponent implements OnDestroy {
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);
  private pageHeaderService = inject(PageHeaderService);
  private router = inject(Router);

  activeTab = signal<TabKey>('securities');
  searchInput = signal('');
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  selectedCommodity = signal<CommodityResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  pendingDelete = signal<CommodityResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);

  searchTerm = toSignal(
    toObservable(this.searchInput).pipe(
      debounceTime(300),
      map(s => s.trim()),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  private headerActionsTpl = viewChild.required<TemplateRef<unknown>>('headerActions');

  data = toSignal<CommodityResponse[] | null, CommodityResponse[] | null>(
    combineLatest([
      toObservable(this.activeTab),
      toObservable(this.searchTerm),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([tab, search]) => {
        this.loading.set(true);
        this.error.set(false);
        const kind: CommodityKind = tab === 'securities' ? 'Security' : 'Currency';
        return this.commodityApi.getCommodities({
          Page: 1,
          PageSize: tab === 'currencies' ? 300 : 200,
          Kind: kind,
          Search: search || undefined,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return (r.items ?? []) as CommodityResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  rows = computed(() => this.data() ?? []);

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
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onTabChange(tab: string): void {
    this.activeTab.set(tab as TabKey);
    this.searchInput.set('');
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
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

  formatLatest(at: string | null): string {
    return at ? formatRelativeAge(at) : '—';
  }
}
