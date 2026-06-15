import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
} from '@angular/core';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso } from '../../shared/date';
import { trimToNull } from '../../shared/trim-to-null';
import { commodityStep, formatCurrency } from '../../shared/format-currency';
import { composeOccurredAt, decomposeOccurredAt } from '../../shared/occurred-at';
import { commodityResource } from '../../shared/commodity-resource';
import { CommodityPickerComponent } from '../../shared/commodity-picker/commodity-picker.component';
import { OccurredAtFieldComponent } from '../../shared/occurred-at-field/occurred-at-field.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { TradeSide } from '@/api/generated/models/trade-side';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type TradeSheetMode = 'create' | 'edit';

@Component({
  selector: 'app-trade-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, CommodityPickerComponent, OccurredAtFieldComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ headerTitle() }}</h3>
          <p hlmSheetDescription>{{ headerDescription() }}</p>
        </hlm-sheet-header>

        @if (account(); as a) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Side</label>
              <div role="radiogroup" class="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="selectedSide() === 'Buy'"
                  [disabled]="mode() === 'edit'"
                  class="rounded-md border px-3 py-2 text-sm text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                  [class]="selectedSide() === 'Buy' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                  (click)="selectedSide.set('Buy')"
                >
                  <div class="font-medium">Buy</div>
                  <div class="text-xs text-muted-foreground">Acquire shares</div>
                </button>
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="selectedSide() === 'Sell'"
                  [disabled]="mode() === 'edit'"
                  class="rounded-md border px-3 py-2 text-sm text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                  [class]="selectedSide() === 'Sell' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                  (click)="selectedSide.set('Sell')"
                >
                  <div class="font-medium">Sell</div>
                  <div class="text-xs text-muted-foreground">Dispose of shares</div>
                </button>
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Account</label>
              <p class="text-sm text-muted-foreground">{{ a.name }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Security</label>
              <app-commodity-picker
                ariaLabel="Select security"
                placeholder="Pick a security"
                [kind]="'Security'"
                [value]="securityId()"
                [selectedCommodity]="security.value()"
                (commodityChange)="onSecurityChange($event)"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="trade-quantity">
                Quantity
                @if (security.value(); as s) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ s.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="trade-quantity"
                class="w-full"
                type="number"
                [step]="qtyStep()"
                min="0"
                placeholder="0"
                [value]="quantity()"
                (input)="quantity.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="trade-price">
                Price per unit
                @if (fundingCcy.value(); as c) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ c.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="trade-price"
                class="w-full"
                type="number"
                [step]="priceStep()"
                min="0"
                placeholder="0.00"
                [value]="unitPrice()"
                (input)="unitPrice.set(asValue($event))"
              />
            </div>

            <div class="space-y-1">
              <label hlmLabel>Total</label>
              <p class="text-sm text-muted-foreground tabular-nums">{{ totalText() || '—' }}</p>
              <p class="text-xs text-muted-foreground">
                {{ selectedSide() === 'Buy' ? 'Funded from cash in this account.' : 'Proceeds added as cash in this account.' }}
              </p>
            </div>

            @if (showFunding()) {
              <div class="space-y-2">
                <label hlmLabel>Funding currency</label>
                <app-commodity-picker
                  ariaLabel="Funding currency"
                  placeholder="Pick a currency"
                  [kind]="'Currency'"
                  [value]="fundingCcyId()"
                  [selectedCommodity]="fundingCcy.value()"
                  (commodityChange)="onFundingChange($event)"
                />
              </div>
            }

            <app-occurred-at-field
              dateId="trade-date"
              timeId="trade-time"
              [date]="date()"
              [time]="time()"
              (dateChange)="date.set($event)"
              (timeChange)="time.set($event)"
            />

            <div class="space-y-2">
              <label hlmLabel for="trade-memo">Memo (optional)</label>
              <input
                hlmInput
                id="trade-memo"
                class="w-full"
                maxlength="512"
                placeholder="e.g. Monthly contribution"
                [value]="memo()"
                (input)="memo.set(asValue($event))"
              />
            </div>

            @if (formError()) {
              <p class="text-sm text-destructive">{{ formError() }}</p>
            }
          </div>
        }

        <hlm-sheet-footer class="flex flex-col gap-2 sm:flex-row sm:justify-between">
          @if (mode() === 'edit' && existing()) {
            <button
              hlmBtn
              variant="ghost"
              class="text-destructive hover:text-destructive sm:order-first"
              (click)="handleDelete()"
              [disabled]="saving() || deleting()"
            >
              {{ deleting() ? 'Deleting...' : 'Delete' }}
            </button>
          }
          <button hlmBtn class="w-full sm:w-auto" (click)="handleSave()" [disabled]="!canSubmit() || saving() || deleting()">
            {{ saving() ? 'Saving...' : (mode() === 'edit' ? 'Save changes' : (selectedSide() === 'Buy' ? 'Buy security' : 'Sell security')) }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class TradeSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<TradeSheetMode>('create');
  existing = input<TransactionResponse | null>(null);
  // The create-mode initial side, chosen from the menu (Buy/Sell). In edit mode the
  // side is read from the stored shape instead, and the toggle is disabled (immutable).
  side = input<TradeSide>('Buy');
  // PC1 (host-driven): reveal the funding-currency picker only when the account
  // holds cash in a non-reporting currency; otherwise it defaults to the reporting
  // currency and stays hidden.
  showFundingCcy = input(false);

  saved = output<TransactionResponse>();
  deleted = output<void>();
  closed = output<void>();

  // `side` is part of the key so a create-mode side change re-seeds the toggle even
  // if the host re-opens the (otherwise identically-keyed) sheet without toggling state.
  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.mode()}|${this.existing()?.id ?? 'new'}|${this.side()}|${this.state() === 'open'}`,
  );

  private seed = computed(() => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') {
      return { kind: 'closed' as const };
    }
    const d = ex?.details;
    const details = d?.kind === 'trade' ? d : null;
    if (this.mode() === 'edit' && ex && details) {
      const { date, time } = decomposeOccurredAt(ex.occurredAt);
      return {
        kind: 'edit' as const,
        side: (ex.shape === 'InvestmentSell' ? 'Sell' : 'Buy') as TradeSide,
        securityId: details.securityId,
        quantity: String(details.quantity),
        unitPrice: String(details.unitPrice),
        fundingCcyId: details.fundingCurrencyId,
        date,
        time,
        memo: ex.memo ?? '',
      };
    }
    return { kind: 'create' as const };
  });

  selectedSide = seededLinkedSignal<string, TradeSide>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.side : this.side();
  });
  securityId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.securityId : null;
  });
  quantity = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.quantity : '';
  });
  unitPrice = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.unitPrice : '';
  });
  fundingCcyId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    if (seed.kind === 'edit') return seed.fundingCcyId;
    return this.account()?.reportingCurrencyId ?? null;
  });
  date = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.date : todayIso();
  });
  time = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.time : null;
  });
  memo = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.memo : '';
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  deleting = seededLinkedSignal(this.seedKey, () => false);
  formError = seededLinkedSignal(this.seedKey, () => '');

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  security = commodityResource(
    this.commodityApi,
    () => this.securityId(),
    () => this.state() === 'open',
  );
  fundingCcy = commodityResource(
    this.commodityApi,
    () => this.fundingCcyId(),
    () => this.state() === 'open',
  );

  qtyStep = computed(() => commodityStep(this.security.value()));
  priceStep = computed(() => commodityStep(this.fundingCcy.value()));

  total = computed(() => this.num(this.quantity()) * this.num(this.unitPrice()));
  totalText = computed(() => {
    const c = this.fundingCcy.value();
    if (!c) return '';
    return formatCurrency(this.total(), c);
  });

  // PC1 reveal (host-driven) is for the create common case. Always reveal in edit so a
  // trade funded in a currency the account no longer holds stays viewable/editable, and
  // reveal whenever the selected funding currency isn't the reporting currency.
  showFunding = computed(() =>
    this.showFundingCcy()
    || this.mode() === 'edit'
    || (!!this.fundingCcyId() && this.fundingCcyId() !== this.account()?.reportingCurrencyId),
  );

  headerTitle = computed(() => {
    if (this.mode() === 'edit') return 'Edit trade';
    return this.selectedSide() === 'Buy' ? 'Buy security' : 'Sell security';
  });
  headerDescription = computed(() => {
    if (this.mode() === 'edit') return "Update this trade. The Buy/Sell side can't be changed.";
    return this.selectedSide() === 'Buy'
      ? 'Record a purchase funded from cash in this account.'
      : 'Record a sale, adding the proceeds as cash in this account.';
  });

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.securityId()) return false;
    if (!this.fundingCcyId()) return false;
    if (!this.date()) return false;
    if (this.num(this.quantity()) <= 0) return false;
    if (this.num(this.unitPrice()) <= 0) return false;
    return true;
  });

  private num(value: string): number {
    const n = Number(value);
    return Number.isFinite(n) ? n : 0;
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onSecurityChange(commodity: CommodityResponse): void {
    this.securityId.set(commodity.id);
  }

  onFundingChange(commodity: CommodityResponse): void {
    this.fundingCcyId.set(commodity.id);
  }

  handleSave(): void {
    const a = this.account();
    const securityId = this.securityId();
    const fundingCurrencyId = this.fundingCcyId();
    if (!a || !securityId || !fundingCurrencyId || !this.canSubmit()) return;

    this.saving.set(true);
    this.formError.set('');

    const body = {
      accountId: a.id,
      side: this.selectedSide(),
      securityId,
      fundingCurrencyId,
      quantity: this.num(this.quantity()),
      unitPrice: this.num(this.unitPrice()),
      occurredAt: composeOccurredAt(this.date(), this.time()),
      memo: trimToNull(this.memo()),
    };

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchTrade({ transactionId: ex.id, body })
      : this.txApi.createTrade({ body });

    obs.pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save trade.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Trade updated.' : 'Trade recorded.');
        this.saved.emit(result);
      },
    });
  }

  handleDelete(): void {
    const ex = this.existing();
    if (!ex) return;
    this.deleting.set(true);
    this.formError.set('');
    this.txApi.deleteTransaction({ transactionId: ex.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete trade.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Trade deleted.');
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
