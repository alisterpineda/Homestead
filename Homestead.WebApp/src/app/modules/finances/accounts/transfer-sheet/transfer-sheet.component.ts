import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  inject,
  effect,
} from '@angular/core';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso, tomorrowIso, toDateOnly } from '../../shared/date';
import { trimToNull } from '../../shared/trim-to-null';
import { isDetailsOfKind } from '../../transactions/transaction-display';
import { AccountPickerComponent } from '../../shared/account-picker/account-picker.component';
import { of, catchError, switchMap } from 'rxjs';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { TransactionDetailsTransferDetails } from '@/api/generated/models/transaction-details-transfer-details';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type TransferSheetMode = 'create' | 'edit';
export type TransferDirection = 'outgoing' | 'incoming';

@Component({
  selector: 'app-transfer-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, AccountPickerComponent],
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
                [selectedAccount]="counterparty()"
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
                type="number"
                step="0.01"
                min="0"
                placeholder="0.00"
                [value]="sourceAmount()"
                (input)="sourceAmount.set(asValue($event))"
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
                  type="number"
                  step="0.01"
                  min="0"
                  placeholder="0.00"
                  [value]="destinationAmount()"
                  (input)="destinationAmount.set(asValue($event))"
                />
                <p class="text-xs text-muted-foreground">A price record will be saved for this transfer.</p>
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel for="transfer-date">Date</label>
              <input
                hlmInput
                id="transfer-date"
                class="w-full"
                type="date"
                [max]="maxDate"
                [value]="date()"
                (input)="date.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="transfer-description">Description (optional)</label>
              <input
                hlmInput
                id="transfer-description"
                class="w-full"
                maxlength="512"
                placeholder="e.g. April rent transfer"
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

  direction = signal<TransferDirection>('outgoing');
  counterpartyId = signal<string | null>(null);
  counterparty = signal<AccountResponse | null>(null);
  sourceAmount = signal('');
  destinationAmount = signal('');
  date = signal(todayIso());
  description = signal('');
  saving = signal(false);
  deleting = signal(false);
  formError = signal('');
  anchorCurrency = signal<CommodityResponse | null>(null);
  counterpartyCurrency = signal<CommodityResponse | null>(null);

  private txApi = inject(FinancesTransactionApiService);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly maxDate = tomorrowIso();

  isCrossCurrency = computed(() => {
    const a = this.anchorCurrency();
    const c = this.counterpartyCurrency();
    return !!a && !!c && a.id !== c.id;
  });

  // Currency labels by side respect the direction toggle.
  sourceCurrency = computed(() =>
    this.direction() === 'outgoing' ? this.anchorCurrency() : this.counterpartyCurrency(),
  );
  destinationCurrency = computed(() =>
    this.direction() === 'outgoing' ? this.counterpartyCurrency() : this.anchorCurrency(),
  );

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

  constructor() {
    effect(() => {
      const a = this.account();
      if (!a || this.state() !== 'open') return;
      // Resolve anchor's reporting currency for label / cross-currency check.
      this.anchorCurrency.set(null);
      this.commodityApi.getCommodity({ id: a.reportingCurrencyId }).subscribe({
        next: c => this.anchorCurrency.set(c),
        error: () => this.anchorCurrency.set(null),
      });

      this.saving.set(false);
      this.deleting.set(false);
      this.formError.set('');

      const ex = this.existing();
      const rawDetails = ex?.details;
      const details = isDetailsOfKind<TransactionDetailsTransferDetails>(rawDetails, 'transfer')
        ? rawDetails
        : null;
      if (this.mode() === 'edit' && ex && details) {
        const isOutgoing = details.sourceAccountId === a.id;
        this.direction.set(isOutgoing ? 'outgoing' : 'incoming');
        const cpId = isOutgoing ? details.destinationAccountId : details.sourceAccountId;
        this.counterpartyId.set(cpId);
        // Counterparty's reporting currency is only known after the account response,
        // so this dependency cannot be parallelised — flatten with switchMap instead of nesting.
        this.accountApi.getAccount({ id: cpId }).pipe(
          catchError(() => of(null)),
          switchMap(acct => {
            this.counterparty.set(acct);
            if (!acct) return of(null);
            return this.commodityApi.getCommodity({ id: acct.reportingCurrencyId }).pipe(
              catchError(() => of(null)),
            );
          }),
        ).subscribe(c => this.counterpartyCurrency.set(c));
        this.sourceAmount.set(String(details.sourceAmount));
        this.destinationAmount.set(
          details.sourceCurrencyId !== details.destinationCurrencyId
            ? String(details.destinationAmount)
            : '',
        );
        this.date.set(toDateOnly(ex.occurredAt));
        this.description.set(ex.description ?? '');
      } else {
        this.direction.set('outgoing');
        this.counterpartyId.set(null);
        this.counterparty.set(null);
        this.counterpartyCurrency.set(null);
        this.sourceAmount.set('');
        this.destinationAmount.set('');
        this.date.set(todayIso());
        this.description.set('');
      }
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onCounterpartyPicked(acct: AccountResponse): void {
    this.counterparty.set(acct);
    this.counterpartyCurrency.set(null);
    this.commodityApi.getCommodity({ id: acct.reportingCurrencyId }).subscribe({
      next: c => this.counterpartyCurrency.set(c),
      error: () => this.counterpartyCurrency.set(null),
    });
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
      description: trimToNull(this.description()),
    };

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchTransfer({ transactionId: ex.id, body })
      : this.txApi.createTransfer({ body });

    obs.pipe(
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

