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
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest, distinctUntilChanged, forkJoin, of, catchError, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucideTrash2, lucideEllipsisVertical, lucideCircleAlert, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinancesCommodityPriceApiService } from '@/api/services/finances-commodity-price-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { NewPriceDialogComponent } from './new-price-dialog/new-price-dialog.component';
import { EditPriceDialogComponent } from './edit-price-dialog/edit-price-dialog.component';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-commodity-prices-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmTableImports,
    HlmBadgeImports,
    HlmDropdownMenuImports,
    NewPriceDialogComponent,
    EditPriceDialogComponent,
  ],
  providers: [provideIcons({ lucidePencil, lucideTrash2, lucideEllipsisVertical, lucideCircleAlert, lucidePlus })],
  template: `
    <div class="flex flex-col gap-4">
      <div class="flex items-center justify-between gap-2">
        <p class="text-sm text-muted-foreground">
          Manual entries are editable. Prices created by transactions are read-only here — edit the source transaction to change them.
        </p>
        <app-new-price-dialog [commodity]="commodity()" (created)="onChanged()">
          <ng-icon hlm name="lucidePlus" size="sm" />
          New manual price
        </app-new-price-dialog>
      </div>

      @if (loading() && !prices()) {
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
            <div hlmEmptyTitle>No prices yet</div>
            <div hlmEmptyDescription>Add a manual price to start valuing this commodity.</div>
          </div>
        </div>
      } @else {
        <div class="overflow-x-auto rounded-md border border-border">
          <table hlmTable class="w-full">
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>When</th>
                <th hlmTh>Quote</th>
                <th hlmTh class="text-right">Rate</th>
                <th hlmTh>Source</th>
                <th hlmTh class="w-12"></th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (row of rows(); track row.id) {
                <tr hlmTr>
                  <td hlmTd class="tabular-nums">{{ formatPriceAt(row.priceAt) }}</td>
                  <td hlmTd>{{ quoteSymbol(row.quoteCurrencyId) }}</td>
                  <td hlmTd class="text-right tabular-nums">{{ formatRate(row) }}</td>
                  <td hlmTd>
                    @if (row.createdByTransactionId) {
                      <span hlmBadge variant="secondary" title="Edited via the source transaction.">Transaction</span>
                    } @else {
                      <span hlmBadge variant="outline">Manual</span>
                    }
                  </td>
                  <td hlmTd class="w-12">
                    @if (!row.createdByTransactionId) {
                      <button
                        hlmBtn
                        variant="ghost"
                        size="icon-sm"
                        [hlmDropdownMenuTrigger]="menu"
                        aria-label="Price actions"
                      >
                        <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
                      </button>
                      <ng-template #menu>
                        <hlm-dropdown-menu class="w-40">
                          <hlm-dropdown-menu-group>
                            <button hlmDropdownMenuItem (triggered)="onEdit(row)">
                              <ng-icon hlm name="lucidePencil" size="sm" />
                              Edit
                            </button>
                            <hlm-dropdown-menu-separator />
                            <button hlmDropdownMenuItem variant="destructive" (triggered)="onDeleteRequest(row)">
                              <ng-icon hlm name="lucideTrash2" size="sm" />
                              Delete
                            </button>
                          </hlm-dropdown-menu-group>
                        </hlm-dropdown-menu>
                      </ng-template>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
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
export class CommodityPricesTabComponent {
  commodity = input.required<CommodityResponse>();

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  selectedPrice = signal<CommodityPriceResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  pendingDelete = signal<CommodityPriceResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);

  currencyMap = signal<Record<string, CommodityResponse>>({});

  private commodityApi = inject(FinancesCommodityApiService);
  private priceApi = inject(FinancesCommodityPriceApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  prices = toSignal<CommodityPriceResponse[] | null, CommodityPriceResponse[] | null>(
    combineLatest([
      toObservable(this.commodity).pipe(map(c => c.id), distinctUntilChanged()),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([id]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.priceApi.getCommodityPrices({
          CommodityId: id,
          Page: 1,
          PageSize: 200,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return (r.items ?? []) as CommodityPriceResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  rows = computed(() => {
    const list = this.prices() ?? [];
    return [...list].sort((a, b) => b.priceAt.localeCompare(a.priceAt));
  });

  constructor() {
    effect(() => {
      const prices = this.prices() ?? [];
      if (prices.length === 0) return;
      const known = this.currencyMap();
      const missing = Array.from(new Set(prices.map(p => p.quoteCurrencyId)))
        .filter(id => !known[id]);
      if (missing.length === 0) return;
      forkJoin(missing.map(id =>
        this.commodityApi.getCommodity({ id }).pipe(catchError(() => of(null))),
      ))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(results => {
          const update = { ...this.currencyMap() };
          results.forEach(c => { if (c) update[c.id] = c; });
          this.currencyMap.set(update);
        });
    });
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  quoteSymbol(id: string): string {
    return this.currencyMap()[id]?.symbol ?? id;
  }

  formatPriceAt(at: string): string {
    const d = new Date(at);
    return d.toLocaleString();
  }

  formatRate(price: CommodityPriceResponse): string {
    const quote = this.currencyMap()[price.quoteCurrencyId];
    const precision = Math.max(quote?.displayPrecision ?? 4, 4);
    return price.rate.toLocaleString(undefined, {
      minimumFractionDigits: precision,
      maximumFractionDigits: precision,
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
}
