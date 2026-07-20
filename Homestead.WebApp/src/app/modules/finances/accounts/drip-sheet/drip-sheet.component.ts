import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { of, catchError } from 'rxjs';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso } from '../../shared/date';
import { trimToNull } from '../../shared/trim-to-null';
import { currencyInputPrecision, formatCurrency } from '../../shared/format-currency';
import { MoneyInputDirective } from '../../shared/money-input.directive';
import { composeOccurredAt, decomposeOccurredAt } from '../../shared/occurred-at';
import { commodityResource } from '../../shared/commodity-resource';
import { CommodityPickerComponent } from '../../shared/commodity-picker/commodity-picker.component';
import { OccurredAtFieldComponent } from '../../shared/occurred-at-field/occurred-at-field.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type DripSheetMode = 'create' | 'edit';

@Component({
  selector: 'app-drip-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    MoneyInputDirective,
    HlmSpinner,
    HlmAlertDialogImports,
    CommodityPickerComponent,
    OccurredAtFieldComponent,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ headerTitle() }}</h3>
          <p hlmSheetDescription>{{ headerDescription() }}</p>
        </hlm-sheet-header>

        @if (account(); as a) {
          @if (partnerLoading()) {
            <div class="flex flex-1 items-center justify-center py-16">
              <hlm-spinner />
            </div>
          } @else if (partnerLoadError()) {
            <div class="flex-1 px-4 py-4">
              <p class="text-sm text-destructive">
                Couldn't load this reinvestment's details. Close this panel and try again.
              </p>
            </div>
          } @else {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
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
                [disabled]="mode() === 'edit'"
                [value]="securityId()"
                [selectedCommodity]="security.value()"
                (commodityChange)="onSecurityChange($event)"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel>Paid in / reinvested in</label>
              <app-commodity-picker
                ariaLabel="Payment currency"
                placeholder="Pick a currency"
                [kind]="'Currency'"
                [disabled]="mode() === 'edit'"
                [value]="paymentCcyId()"
                [selectedCommodity]="paymentCcy.value()"
                (commodityChange)="onPaymentChange($event)"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="drip-amount">
                Dividend amount
                @if (paymentCcy.value(); as c) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ c.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="drip-amount"
                class="w-full"
                [appMoneyInput]="amountPrecision()"
                placeholder="0.00"
                [value]="amount()"
                (amountInput)="amount.set($event)"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="drip-units">
                Units received
                @if (security.value(); as s) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ s.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="drip-units"
                class="w-full"
                appMoneyInput
                placeholder="0"
                [value]="units()"
                (amountInput)="units.set($event)"
              />
            </div>

            <div class="space-y-1">
              <label hlmLabel>Reinvestment price</label>
              <p class="text-sm text-muted-foreground tabular-nums">
                {{ pricePerUnitText() ? ('≈ ' + pricePerUnitText() + ' / unit') : '—' }}
              </p>
              <p class="text-xs text-muted-foreground">Derived from the dividend amount ÷ units — never typed.</p>
            </div>

            <app-occurred-at-field
              dateId="drip-date"
              timeId="drip-time"
              [date]="date()"
              [time]="time()"
              (dateChange)="date.set($event)"
              (timeChange)="time.set($event)"
            />

            <div class="space-y-2">
              <label hlmLabel for="drip-description">Description (optional)</label>
              <input
                hlmInput
                id="drip-description"
                class="w-full"
                maxlength="512"
                placeholder="Reinvest dividend"
                [value]="description()"
                (input)="description.set(asValue($event))"
              />
            </div>

            @if (formError()) {
              <p class="text-sm text-destructive">{{ formError() }}</p>
            }
          </div>
          }
        }

        @if (account() && !partnerLoading() && !partnerLoadError()) {
          <hlm-sheet-footer class="flex flex-col gap-2 sm:flex-row sm:justify-between">
            @if (mode() === 'edit' && existing()) {
              <button
                hlmBtn
                variant="ghost"
                class="text-destructive hover:text-destructive sm:order-first"
                (click)="promptDelete()"
                [disabled]="saving() || deleting()"
              >
                {{ deleting() ? 'Deleting...' : 'Delete' }}
              </button>
            }
            <button hlmBtn class="w-full sm:w-auto" (click)="handleSave()" [disabled]="!canSubmit() || saving() || deleting()">
              {{ saving() ? 'Saving...' : (mode() === 'edit' ? 'Save changes' : 'Reinvest dividend') }}
            </button>
          </hlm-sheet-footer>
        }
      </hlm-sheet-content>
    </hlm-sheet>

    <hlm-alert-dialog [state]="dripDeleteState()" (stateChanged)="dripDeleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete this reinvested dividend?</h2>
          <p hlmAlertDialogDescription>
            This is one half of a dividend reinvestment. Delete both halves, or keep the
            {{ survivorKind() }} as a standalone entry.
          </p>
        </div>
        <div hlmAlertDialogFooter class="flex flex-col gap-2 sm:flex-row sm:justify-end">
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="outline" (click)="confirmDeleteOne(ctx)" [disabled]="deleting()">
            Delete this one only
          </button>
          <button hlmAlertDialogAction variant="destructive" (click)="confirmDeleteBoth(ctx)" [disabled]="deleting()">
            {{ deleting() ? 'Deleting...' : 'Delete both' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class DripSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<DripSheetMode>('create');
  // The clicked activity row — either the Dividend half or the Buy half (K1/K6).
  existing = input<TransactionResponse | null>(null);

  saved = output<TransactionResponse>();
  deleted = output<void>();
  closed = output<void>();

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  // Edit seeds entirely from the Buy half (K6): it carries account/security/currency/units/price,
  // and amount = units × unitPrice exactly. Clicking the Buy row needs no fetch; clicking the
  // Dividend row fetches its partner (the Buy half) via relatedTransactionId. The only network
  // round-trip is this one GetTransaction, and only when the Dividend row was clicked.
  private buyHalf = rxResource<TransactionResponse | null, { open: boolean; partnerId: string | null }>({
    params: () => {
      const ex = this.existing();
      const partnerId =
        this.mode() === 'edit' && ex?.shape === 'Dividend' ? (ex.relatedTransactionId ?? null) : null;
      return { open: this.state() === 'open', partnerId };
    },
    stream: ({ params }) =>
      params.open && params.partnerId
        ? this.txApi.getTransaction({ transactionId: params.partnerId }).pipe(catchError(() => of(null)))
        : of(null),
    defaultValue: null,
  });

  private resolvedBuyHalf = computed<TransactionResponse | null>(() => {
    const ex = this.existing();
    if (this.mode() !== 'edit' || !ex) return null;
    if (ex.shape === 'InvestmentBuy') return ex; // self-sufficient — no fetch
    if (ex.shape === 'Dividend') return this.buyHalf.value(); // fetched partner
    return null;
  });

  // Editing a clicked Dividend row needs its partner Buy half (the only network round-trip).
  // While that's in flight — or if it fails (the stream catches to null) — the form data isn't
  // ready, so surface explicit loading / error states instead of a blank, locked, unsubmittable
  // sheet (the pickers are disabled in edit, so the user couldn't fix a silently-empty form).
  private partnerFetchExpected = computed(() =>
    this.state() === 'open'
    && this.mode() === 'edit'
    && this.existing()?.shape === 'Dividend'
    && this.existing()?.relatedTransactionId != null,
  );
  partnerLoading = computed(() => this.partnerFetchExpected() && this.buyHalf.status() !== 'resolved');
  partnerLoadError = computed(() =>
    this.partnerFetchExpected() && this.buyHalf.status() === 'resolved' && this.buyHalf.value() == null,
  );

  // The resolved Buy half's id is part of the key so the form re-seeds when the partner
  // fetch lands (Dividend row clicked); for a Buy row it's available synchronously.
  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.mode()}|${this.existing()?.id ?? 'new'}|${this.resolvedBuyHalf()?.id ?? 'none'}|${this.state() === 'open'}`,
  );

  private seed = computed(() => {
    const a = this.account();
    if (!a || this.state() !== 'open') {
      return { kind: 'closed' as const };
    }
    if (this.mode() === 'edit') {
      const buy = this.resolvedBuyHalf();
      const d = buy?.details;
      const details = d?.kind === 'trade' ? d : null;
      if (buy && details) {
        const { date, time } = decomposeOccurredAt(buy.occurredAt);
        // amount = units × unitPrice (the cash legs net to zero by construction). toPrecision(12)
        // strips any IEEE-754 tail so the seeded input reads cleanly.
        const amount = Number((details.quantity * details.unitPrice).toPrecision(12));
        return {
          kind: 'edit' as const,
          securityId: details.securityId,
          paymentCcyId: details.fundingCurrencyId,
          units: String(details.quantity),
          amount: String(amount),
          date,
          time,
          description: buy.memo ?? '',
        };
      }
      // Edit mode, but the Buy half hasn't resolved yet (Dividend row clicked, fetch in flight).
      return { kind: 'pending' as const };
    }
    return { kind: 'create' as const };
  });

  securityId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.securityId : null;
  });
  paymentCcyId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    if (seed.kind === 'edit') return seed.paymentCcyId;
    return this.account()?.reportingCurrencyId ?? null;
  });
  amount = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.amount : '';
  });
  units = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.units : '';
  });
  date = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.date : todayIso();
  });
  time = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.time : null;
  });
  description = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.description : '';
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  deleting = seededLinkedSignal(this.seedKey, () => false);
  formError = seededLinkedSignal(this.seedKey, () => '');
  dripDeleteState = seededLinkedSignal<string, BrnDialogState>(this.seedKey, () => 'closed');

  security = commodityResource(
    this.commodityApi,
    () => this.securityId(),
    () => this.state() === 'open',
  );
  paymentCcy = commodityResource(
    this.commodityApi,
    () => this.paymentCcyId(),
    () => this.state() === 'open',
  );

  // Units are a share count, not money — left free-precision (no blur rounding); see currencyInputPrecision.
  amountPrecision = computed(() => currencyInputPrecision(this.paymentCcy.value()));

  // Inverse of the trade sheet's total: the per-unit price is derived from amount ÷ units.
  pricePerUnit = computed(() => {
    const u = this.num(this.units());
    return u > 0 ? this.num(this.amount()) / u : 0;
  });
  pricePerUnitText = computed(() => {
    const c = this.paymentCcy.value();
    if (!c || this.pricePerUnit() <= 0) return '';
    return formatCurrency(this.pricePerUnit(), c);
  });

  headerTitle = computed(() => (this.mode() === 'edit' ? 'Edit reinvested dividend' : 'Reinvest dividend'));
  headerDescription = computed(() =>
    this.mode() === 'edit'
      ? 'Update this reinvested dividend. The account, security, and currency are locked.'
      : 'Record a cash dividend reinvested into more units of this security.',
  );

  // When deleting just the clicked half, the survivor is the other half.
  survivorKind = computed(() => (this.existing()?.shape === 'Dividend' ? 'buy' : 'dividend'));

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.securityId()) return false;
    if (!this.paymentCcyId()) return false;
    if (!this.date()) return false;
    if (this.num(this.amount()) <= 0) return false;
    if (this.num(this.units()) <= 0) return false;
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

  onPaymentChange(commodity: CommodityResponse): void {
    this.paymentCcyId.set(commodity.id);
  }

  handleSave(): void {
    const a = this.account();
    const securityId = this.securityId();
    const paymentCurrencyId = this.paymentCcyId();
    if (!a || !securityId || !paymentCurrencyId || !this.canSubmit()) return;

    this.saving.set(true);
    this.formError.set('');

    const body = {
      accountId: a.id,
      securityId,
      paymentCurrencyId,
      amount: this.num(this.amount()),
      units: this.num(this.units()),
      occurredAt: composeOccurredAt(this.date(), this.time()),
      description: trimToNull(this.description()),
    };

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchDrip({ transactionId: ex.id, body })
      : this.txApi.createDrip({ body });

    obs.pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save reinvested dividend.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Reinvested dividend updated.' : 'Dividend reinvested.');
        this.saved.emit(result);
      },
    });
  }

  promptDelete(): void {
    this.dripDeleteState.set('open');
  }

  // "Delete both" — the dedicated atomic endpoint removes the pair and the Buy half's price row.
  confirmDeleteBoth(ctx: { close: () => void }): void {
    const ex = this.existing();
    if (!ex) return;
    this.deleting.set(true);
    this.formError.set('');
    this.txApi.deleteDrip({ transactionId: ex.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete reinvested dividend.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        toast.success('Reinvested dividend deleted.');
        this.deleted.emit();
      },
    });
  }

  // "Delete this one only" — the generic delete; the DB nulls the survivor's link server-side,
  // leaving it as a standalone Dividend / Buy.
  confirmDeleteOne(ctx: { close: () => void }): void {
    const ex = this.existing();
    if (!ex) return;
    this.deleting.set(true);
    this.formError.set('');
    this.txApi.deleteTransaction({ transactionId: ex.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete this half.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        toast.success(`Kept the ${this.survivorKind()} as a standalone entry.`);
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
