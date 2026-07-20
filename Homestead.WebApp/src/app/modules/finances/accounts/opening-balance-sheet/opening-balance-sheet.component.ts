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
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso, tomorrowDate } from '../../shared/date';
import { currencyInputPrecision } from '../../shared/format-currency';
import { MoneyInputDirective } from '../../shared/money-input.directive';
import { trimToNull } from '../../shared/trim-to-null';
import { commodityResource } from '../../shared/commodity-resource';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type OpeningBalanceSheetMode = 'edit' | 'create-missing';

@Component({
  selector: 'app-opening-balance-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, MoneyInputDirective, HlmDatePicker, HlmDatePickerInput],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ mode() === 'edit' ? 'Edit opening balance' : 'Add opening balance' }}</h3>
          <p hlmSheetDescription>
            The starting balance for this account, booked against <em>Equity:Opening Balances</em>.
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
              <p class="text-sm text-muted-foreground">{{ commodityLabel() ?? '—' }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="opening-balance-quantity">{{ isSecurity() ? 'Quantity' : 'Amount' }}</label>
              <input
                hlmInput
                id="opening-balance-quantity"
                class="w-full"
                [appMoneyInput]="quantityPrecision()"
                placeholder="0.00"
                [value]="quantity()"
                (amountInput)="quantity.set($event)"
              />
              @if (a.accountType === 'Liability') {
                <p class="text-xs text-muted-foreground">Enter the amount initially owed (positive).</p>
              } @else if (isSecurity()) {
                <p class="text-xs text-muted-foreground">Number of units held in {{ commodity.value()!.symbol }}.</p>
              }
            </div>

            @if (isSecurity()) {
              <div class="space-y-2">
                <label hlmLabel for="opening-balance-unit-price">
                  Per-unit price (optional)
                </label>
                <input
                  hlmInput
                  id="opening-balance-unit-price"
                  class="w-full"
                  appMoneyInput
                  placeholder="0.00"
                  [value]="unitPrice()"
                  (amountInput)="unitPrice.set($event)"
                />
                <p class="text-xs text-muted-foreground">
                  Records a price observation in the account's reporting currency on the as-of date.
                  Leave blank to capture the position without a historical price.
                </p>
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel for="opening-balance-date">As-of date</label>
              <hlm-date-picker
                class="w-full"
                captionLayout="dropdown"
                [date]="asOfDatePicker.date()"
                [max]="maxDate"
                (dateChange)="asOfDatePicker.set($event)"
              >
                <hlm-date-picker-input inputId="opening-balance-date" placeholder="Select date" />
              </hlm-date-picker>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="opening-balance-memo">Memo (optional)</label>
              <input
                hlmInput
                id="opening-balance-memo"
                class="w-full"
                maxlength="512"
                placeholder="e.g. Imported from prior tracker"
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
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class OpeningBalanceSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<OpeningBalanceSheetMode>('edit');
  existing = input<OpeningBalanceResponse | null>(null);

  saved = output<OpeningBalanceResponse>();
  deleted = output<void>();
  closed = output<void>();

  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.mode()}|${this.existing()?.commodityId ?? 'new'}|${this.state() === 'open'}`,
  );

  quantity = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return '';
    return this.mode() === 'edit' && ex ? String(ex.quantity) : '';
  });
  unitPrice = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return '';
    return this.mode() === 'edit' && ex && ex.valuePerUnit != null ? String(ex.valuePerUnit) : '';
  });
  asOfDate = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return todayIso();
    return this.mode() === 'edit' && ex ? ex.asOfDate : todayIso();
  });
  memo = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') return '';
    return this.mode() === 'edit' && ex ? ex.memo : '';
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  deleting = seededLinkedSignal(this.seedKey, () => false);
  formError = seededLinkedSignal(this.seedKey, () => '');

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  // Per-position OB on Investment accounts is keyed by the existing entry's
  // commodityId; non-Investment kinds always use the reporting currency.
  private commodityId = computed(() => {
    const a = this.account();
    if (!a || this.state() !== 'open') return null;
    const ex = this.existing();
    return this.mode() === 'edit' && ex ? ex.commodityId : a.reportingCurrencyId;
  });

  commodity = commodityResource(
    this.commodityApi,
    () => this.commodityId(),
    () => this.state() === 'open',
  );

  readonly maxDate = tomorrowDate();

  protected readonly asOfDatePicker = isoDateBinding(this.asOfDate);

  commodityLabel = computed(() => {
    const c = this.commodity.value();
    return c ? `${c.symbol} — ${c.name}` : null;
  });

  isSecurity = computed(() => this.commodity.value()?.kind === 'Security');

  // Resolves to null for a security account (free-precision quantity) and to the minor unit for a
  // cash one, which is exactly the split this field needs — its label flips Quantity/Amount to match.
  quantityPrecision = computed(() => currencyInputPrecision(this.commodity.value()));

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.asOfDate()) return false;
    if (this.quantity() === '') return false;
    const q = Number(this.quantity());
    if (!Number.isFinite(q) || q < 0) return false;
    if (this.isSecurity() && this.unitPrice() !== '') {
      const p = Number(this.unitPrice());
      if (!Number.isFinite(p) || p <= 0) return false;
    }
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const a = this.account();
    if (!a || !this.canSubmit()) return;

    this.saving.set(true);
    this.formError.set('');

    const ex = this.existing();
    const valuePerUnit = this.isSecurity() && this.unitPrice() !== ''
      ? Number(this.unitPrice())
      : null;

    const trimmedMemo = trimToNull(this.memo());

    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchOpeningBalance({
          accountId: a.id,
          commodityId: ex.commodityId,
          body: {
            quantity: Number(this.quantity()),
            valuePerUnit,
            asOfDate: this.asOfDate(),
            memo: trimmedMemo,
          },
        })
      : this.txApi.createOpeningBalance({
          accountId: a.id,
          body: {
            commodityId: a.reportingCurrencyId,
            quantity: Number(this.quantity()),
            asOfDate: this.asOfDate(),
            memo: trimmedMemo,
          },
        });

    obs.pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save opening balance.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Opening balance updated.' : 'Opening balance added.');
        this.saved.emit(result);
      },
    });
  }

  handleDelete(): void {
    const a = this.account();
    const ex = this.existing();
    if (!a || !ex) return;

    this.deleting.set(true);
    this.formError.set('');
    this.txApi.deleteOpeningBalance({ accountId: a.id, commodityId: ex.commodityId }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete opening balance.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Opening balance deleted.');
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
