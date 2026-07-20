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
import { currencyInputPrecision } from '../../shared/format-currency';
import { MoneyInputDirective } from '../../shared/money-input.directive';
import { composeOccurredAt, decomposeOccurredAt } from '../../shared/occurred-at';
import { commodityResource } from '../../shared/commodity-resource';
import { CommodityPickerComponent } from '../../shared/commodity-picker/commodity-picker.component';
import { OccurredAtFieldComponent } from '../../shared/occurred-at-field/occurred-at-field.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type DividendSheetMode = 'create' | 'edit';

@Component({
  selector: 'app-dividend-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, MoneyInputDirective, CommodityPickerComponent, OccurredAtFieldComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ mode() === 'edit' ? 'Edit dividend' : 'Record dividend' }}</h3>
          <p hlmSheetDescription>
            {{ mode() === 'edit' ? 'Update this cash dividend.' : 'Record a cash dividend paid into this account.' }}
          </p>
        </hlm-sheet-header>

        @if (account(); as a) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Account</label>
              <p class="text-sm text-muted-foreground">{{ a.name }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Paid in</label>
              <app-commodity-picker
                ariaLabel="Payment currency"
                placeholder="Pick a currency"
                [kind]="'Currency'"
                [value]="paymentCcyId()"
                [selectedCommodity]="paymentCcy.value()"
                (commodityChange)="onPaymentChange($event)"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="dividend-amount">
                Amount
                @if (paymentCcy.value(); as c) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ c.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="dividend-amount"
                class="w-full"
                [appMoneyInput]="amountPrecision()"
                placeholder="0.00"
                [value]="amount()"
                (amountInput)="amount.set($event)"
              />
            </div>

            <app-occurred-at-field
              dateId="dividend-date"
              timeId="dividend-time"
              [date]="date()"
              [time]="time()"
              (dateChange)="date.set($event)"
              (timeChange)="time.set($event)"
            />

            <div class="space-y-2">
              <label hlmLabel for="dividend-description">Description (optional)</label>
              <input
                hlmInput
                id="dividend-description"
                class="w-full"
                maxlength="512"
                placeholder="Dividend"
                [value]="description()"
                (input)="description.set(asValue($event))"
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
            {{ saving() ? 'Saving...' : (mode() === 'edit' ? 'Save changes' : 'Record dividend') }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class DividendSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<DividendSheetMode>('create');
  existing = input<TransactionResponse | null>(null);

  saved = output<TransactionResponse>();
  deleted = output<void>();
  closed = output<void>();

  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.mode()}|${this.existing()?.id ?? 'new'}|${this.state() === 'open'}`,
  );

  private seed = computed(() => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') {
      return { kind: 'closed' as const };
    }
    const d = ex?.details;
    const details = d?.kind === 'dividend' ? d : null;
    if (this.mode() === 'edit' && ex && details) {
      const { date, time } = decomposeOccurredAt(ex.occurredAt);
      return {
        kind: 'edit' as const,
        paymentCcyId: details.paymentCurrencyId,
        amount: String(details.amount),
        date,
        time,
        description: ex.memo ?? '',
      };
    }
    return { kind: 'create' as const };
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

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  paymentCcy = commodityResource(
    this.commodityApi,
    () => this.paymentCcyId(),
    () => this.state() === 'open',
  );

  amountPrecision = computed(() => currencyInputPrecision(this.paymentCcy.value()));

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.paymentCcyId()) return false;
    if (!this.date()) return false;
    const amt = Number(this.amount());
    if (!Number.isFinite(amt) || amt <= 0) return false;
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onPaymentChange(commodity: CommodityResponse): void {
    this.paymentCcyId.set(commodity.id);
  }

  handleSave(): void {
    const a = this.account();
    const paymentCurrencyId = this.paymentCcyId();
    if (!a || !paymentCurrencyId || !this.canSubmit()) return;

    this.saving.set(true);
    this.formError.set('');

    const body = {
      accountId: a.id,
      paymentCurrencyId,
      amount: Number(this.amount()),
      occurredAt: composeOccurredAt(this.date(), this.time()),
      description: trimToNull(this.description()),
    };

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchDividend({ transactionId: ex.id, body })
      : this.txApi.createDividend({ body });

    obs.pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save dividend.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Dividend updated.' : 'Dividend recorded.');
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
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete dividend.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Dividend deleted.');
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
