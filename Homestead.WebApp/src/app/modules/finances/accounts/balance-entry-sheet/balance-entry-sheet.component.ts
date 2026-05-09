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
import { todayIso, tomorrowIso } from '../../shared/date';
import { commodityStep } from '../../shared/format-currency';
import { trimToNull } from '../../shared/trim-to-null';
import { commodityResource } from '../../shared/commodity-resource';
import { CommodityPickerComponent } from '../../shared/commodity-picker/commodity-picker.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { BalanceEntryResponse } from '@/api/generated/models/balance-entry-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type BalanceEntrySheetMode = 'create' | 'edit';

@Component({
  selector: 'app-balance-entry-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, CommodityPickerComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ mode() === 'edit' ? 'Edit balance entry' : 'Record balance' }}</h3>
          <p hlmSheetDescription>
            Assert the balance of this account on a date. The system books any difference against
            <em>Equity:Adjustments</em>.
          </p>
        </hlm-sheet-header>

        @if (account(); as a) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Account</label>
              <p class="text-sm text-muted-foreground">{{ a.name }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Commodity</label>
              @if (a.kind === 'Investment' && mode() === 'create') {
                <app-commodity-picker
                  ariaLabel="Select commodity"
                  placeholder="Pick a currency or security"
                  [value]="commodityId()"
                  [selectedCommodity]="commodity.value()"
                  (commodityChange)="onCommodityChange($event)"
                />
              } @else {
                <p class="text-sm text-muted-foreground">{{ commodityLabel() ?? '—' }}</p>
              }
            </div>

            <div class="space-y-2">
              <label hlmLabel for="balance-entry-amount">Amount</label>
              <input
                hlmInput
                id="balance-entry-amount"
                class="w-full"
                type="number"
                [step]="amountStep()"
                min="0"
                placeholder="0.00"
                [value]="amount()"
                (input)="amount.set(asValue($event))"
              />
              @if (a.accountType === 'Liability') {
                <p class="text-xs text-muted-foreground">Enter the amount currently owed (positive).</p>
              } @else if (commodity.value()?.kind === 'Security') {
                <p class="text-xs text-muted-foreground">Total units held of {{ commodity.value()!.symbol }}.</p>
              }
            </div>

            <div class="space-y-2">
              <label hlmLabel for="balance-entry-date">As-of date</label>
              <input
                hlmInput
                id="balance-entry-date"
                class="w-full"
                type="date"
                [max]="maxDate"
                [value]="date()"
                (input)="date.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="balance-entry-memo">Memo (optional)</label>
              <input
                hlmInput
                id="balance-entry-memo"
                class="w-full"
                maxlength="512"
                placeholder="e.g. April statement"
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
            {{ saving() ? 'Saving...' : (mode() === 'edit' ? 'Save changes' : 'Record balance') }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class BalanceEntrySheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<BalanceEntrySheetMode>('create');
  existing = input<BalanceEntryResponse | null>(null);

  saved = output<BalanceEntryResponse>();
  deleted = output<void>();
  closed = output<void>();

  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.mode()}|${this.existing()?.transactionId ?? 'new'}|${this.state() === 'open'}`,
  );

  amount = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return '';
    return this.mode() === 'edit' && ex ? String(ex.assertedAmount) : '';
  });
  date = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return todayIso();
    return this.mode() === 'edit' && ex ? ex.date : todayIso();
  });
  memo = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return '';
    return this.mode() === 'edit' && ex ? ex.memo : '';
  });
  commodityId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return null;
    return this.mode() === 'edit' && ex ? ex.commodityId : a.reportingCurrencyId;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  deleting = seededLinkedSignal(this.seedKey, () => false);
  formError = seededLinkedSignal(this.seedKey, () => '');

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  commodity = commodityResource(
    this.commodityApi,
    () => this.commodityId(),
    () => this.state() === 'open',
  );

  readonly maxDate = tomorrowIso();

  commodityLabel = computed(() => {
    const c = this.commodity.value();
    return c ? `${c.symbol} — ${c.name}` : null;
  });

  amountStep = computed(() => commodityStep(this.commodity.value()));

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.commodityId()) return false;
    if (!this.date()) return false;
    if (this.amount() === '') return false;
    const amt = Number(this.amount());
    if (!Number.isFinite(amt) || amt < 0) return false;
    return true;
  });

  onCommodityChange(commodity: CommodityResponse): void {
    this.commodityId.set(commodity.id);
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const a = this.account();
    const commodityId = this.commodityId();
    if (!a || !commodityId || !this.canSubmit()) return;
    this.saving.set(true);
    this.formError.set('');
    const body = {
      commodityId,
      assertedAmount: Number(this.amount()),
      date: this.date(),
      memo: trimToNull(this.memo()),
    };

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchBalanceEntry({ accountId: a.id, transactionId: ex.transactionId, body })
      : this.txApi.createBalanceEntry({ accountId: a.id, body });

    obs.pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save balance entry.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Balance entry updated.' : 'Balance recorded.');
        this.saved.emit(result);
      },
    });
  }

  handleDelete(): void {
    const ex = this.existing();
    if (!ex) return;
    this.deleting.set(true);
    this.formError.set('');
    this.txApi.deleteTransaction({ transactionId: ex.transactionId }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete balance entry.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Balance entry deleted.');
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
