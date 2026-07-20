import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  DestroyRef,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso, tomorrowDate, toDateOnly } from '../../shared/date';
import { trimToNull } from '../../shared/trim-to-null';
import { currencyInputPrecision } from '../../shared/format-currency';
import { MoneyInputDirective } from '../../shared/money-input.directive';
import { commodityResource } from '../../shared/commodity-resource';
import { AccountPickerComponent } from '../../shared/account-picker/account-picker.component';
import { of, catchError } from 'rxjs';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type TransferSheetMode = 'create' | 'edit';
export type TransferDirection = 'outgoing' | 'incoming';

@Component({
  selector: 'app-transfer-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, MoneyInputDirective, HlmDatePicker, HlmDatePickerInput, AccountPickerComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ mode() === 'edit' ? 'Edit transfer' : 'New transfer' }}</h3>
          <p hlmSheetDescription>Move money between two of your accounts.</p>
        </hlm-sheet-header>

        @if (account(); as a) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Direction</label>
              <div role="radiogroup" class="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="direction() === 'outgoing'"
                  class="rounded-md border px-3 py-2 text-sm text-left transition-colors"
                  [class]="direction() === 'outgoing' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                  (click)="direction.set('outgoing')"
                >
                  <div class="font-medium">Outgoing</div>
                  <div class="text-xs text-muted-foreground">From this account</div>
                </button>
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="direction() === 'incoming'"
                  class="rounded-md border px-3 py-2 text-sm text-left transition-colors"
                  [class]="direction() === 'incoming' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                  (click)="direction.set('incoming')"
                >
                  <div class="font-medium">Incoming</div>
                  <div class="text-xs text-muted-foreground">Into this account</div>
                </button>
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel>{{ direction() === 'outgoing' ? 'Destination account' : 'Source account' }}</label>
              <app-account-picker
                [value]="counterpartyId()"
                (valueChange)="counterpartyId.set($event)"
                (accountChange)="onCounterpartyPicked($event)"
                [excludeAccountId]="a.id"
                [selectedAccount]="counterparty.value()"
                [placeholder]="direction() === 'outgoing' ? 'Select destination' : 'Select source'"
                [ariaLabel]="direction() === 'outgoing' ? 'Destination account' : 'Source account'"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="transfer-source-amount">
                {{ direction() === 'outgoing' ? 'Amount sent' : 'Amount sent by source' }}
                @if (sourceCurrency(); as c) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ c.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="transfer-source-amount"
                class="w-full"
                [appMoneyInput]="sourceAmountPrecision()"
                placeholder="0.00"
                [value]="sourceAmount()"
                (amountInput)="sourceAmount.set($event)"
              />
            </div>

            @if (isCrossCurrency()) {
              <div class="space-y-2">
                <label hlmLabel for="transfer-destination-amount">
                  {{ direction() === 'outgoing' ? 'Amount received' : 'Amount received here' }}
                  @if (destinationCurrency(); as c) {
                    <span class="ml-1 text-xs text-muted-foreground">({{ c.symbol }})</span>
                  }
                </label>
                <input
                  hlmInput
                  id="transfer-destination-amount"
                  class="w-full"
                  [appMoneyInput]="destinationAmountPrecision()"
                  placeholder="0.00"
                  [value]="destinationAmount()"
                  (amountInput)="destinationAmount.set($event)"
                />
                <p class="text-xs text-muted-foreground">A price record will be saved for this transfer.</p>
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel for="transfer-date">Date</label>
              <hlm-date-picker
                class="w-full"
                captionLayout="dropdown"
                [date]="datePicker.date()"
                [max]="maxDate"
                (dateChange)="datePicker.set($event)"
              >
                <hlm-date-picker-input inputId="transfer-date" placeholder="Select date" />
              </hlm-date-picker>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="transfer-memo">Memo (optional)</label>
              <input
                hlmInput
                id="transfer-memo"
                class="w-full"
                maxlength="512"
                placeholder="e.g. April rent transfer"
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
            {{ saving() ? 'Saving...' : (mode() === 'edit' ? 'Save changes' : 'Create transfer') }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class TransferSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<TransferSheetMode>('create');
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
    const rawDetails = ex?.details;
    const details = rawDetails?.kind === 'transfer' ? rawDetails : null;
    if (this.mode() === 'edit' && ex && details) {
      const isOutgoing = details.sourceAccountId === a.id;
      return {
        kind: 'edit' as const,
        details,
        ex,
        isOutgoing,
        counterpartyId: isOutgoing ? details.destinationAccountId : details.sourceAccountId,
      };
    }
    return { kind: 'create' as const };
  });

  direction = seededLinkedSignal<string, TransferDirection>(this.seedKey, () => {
    const seed = this.seed();
    if (seed.kind === 'edit') return seed.isOutgoing ? 'outgoing' : 'incoming';
    return 'outgoing';
  });
  counterpartyId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.counterpartyId : null;
  });
  sourceAmount = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? String(seed.details.sourceAmount) : '';
  });
  destinationAmount = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    if (seed.kind !== 'edit') return '';
    return seed.details.sourceCurrencyId !== seed.details.destinationCurrencyId
      ? String(seed.details.destinationAmount)
      : '';
  });
  date = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? toDateOnly(seed.ex.occurredAt) : todayIso();
  });
  memo = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? (seed.ex.memo ?? '') : '';
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  deleting = seededLinkedSignal(this.seedKey, () => false);
  formError = seededLinkedSignal(this.seedKey, () => '');

  private txApi = inject(FinancesTransactionApiService);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  anchorCurrency = commodityResource(
    this.commodityApi,
    () => this.account()?.reportingCurrencyId,
    () => this.state() === 'open',
  );

  counterparty = rxResource<AccountResponse | null, { id: string | null; open: boolean }>({
    params: () => ({ id: this.counterpartyId(), open: this.state() === 'open' }),
    stream: ({ params }) =>
      params.open && params.id
        ? this.accountApi.getAccount({ id: params.id }).pipe(catchError(() => of(null)))
        : of(null),
    defaultValue: null,
  });

  counterpartyCurrency = commodityResource(
    this.commodityApi,
    () => this.counterparty.value()?.reportingCurrencyId,
    () => this.state() === 'open',
  );

  readonly maxDate = tomorrowDate();

  protected readonly datePicker = isoDateBinding(this.date);

  isCrossCurrency = computed(() => {
    const a = this.anchorCurrency.value();
    const c = this.counterpartyCurrency.value();
    return !!a && !!c && a.id !== c.id;
  });

  // Currency labels by side respect the direction toggle.
  sourceCurrency = computed(() =>
    this.direction() === 'outgoing' ? this.anchorCurrency.value() : this.counterpartyCurrency.value(),
  );
  destinationCurrency = computed(() =>
    this.direction() === 'outgoing' ? this.counterpartyCurrency.value() : this.anchorCurrency.value(),
  );

  // Each side settles at its OWN currency's minor unit — the whole point of a cross-currency transfer
  // is that the two legs differ (e.g. 100.00 USD out, 137.45 CAD in, or a 0-decimal JPY leg).
  sourceAmountPrecision = computed(() => currencyInputPrecision(this.sourceCurrency()));
  destinationAmountPrecision = computed(() => currencyInputPrecision(this.destinationCurrency()));

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.counterpartyId()) return false;
    if (!this.date()) return false;
    if (this.sourceAmount() === '') return false;
    const src = Number(this.sourceAmount());
    if (!Number.isFinite(src) || src <= 0) return false;
    if (this.isCrossCurrency()) {
      if (this.destinationAmount() === '') return false;
      const dst = Number(this.destinationAmount());
      if (!Number.isFinite(dst) || dst <= 0) return false;
    }
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onCounterpartyPicked(acct: AccountResponse): void {
    this.counterpartyId.set(acct.id);
  }

  handleSave(): void {
    const a = this.account();
    if (!a || !this.canSubmit()) return;
    const cpId = this.counterpartyId();
    if (!cpId) return;

    this.saving.set(true);
    this.formError.set('');

    const isOutgoing = this.direction() === 'outgoing';
    const sourceAccountId = isOutgoing ? a.id : cpId;
    const destinationAccountId = isOutgoing ? cpId : a.id;
    const sourceAmount = Number(this.sourceAmount());
    const destinationAmount = this.isCrossCurrency() ? Number(this.destinationAmount()) : null;

    const body = {
      sourceAccountId,
      destinationAccountId,
      sourceAmount,
      destinationAmount,
      date: this.date(),
      memo: trimToNull(this.memo()),
    };

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchTransfer({ transactionId: ex.id, body })
      : this.txApi.createTransfer({ body });

    obs.pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to save transfer.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Transfer updated.' : 'Transfer created.');
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
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete transfer.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Transfer deleted.');
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}

