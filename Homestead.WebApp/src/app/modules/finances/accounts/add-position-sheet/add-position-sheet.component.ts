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
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso, tomorrowDate } from '../../shared/date';
import { currencyInputPrecision } from '../../shared/format-currency';
import { MoneyInputDirective } from '../../shared/money-input.directive';
import { CommodityPickerComponent } from '../../shared/commodity-picker/commodity-picker.component';
import { trimToNull } from '../../shared/trim-to-null';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-add-position-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, MoneyInputDirective, HlmDatePicker, HlmDatePickerInput, CommodityPickerComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Add position</h3>
          <p hlmSheetDescription>
            Record a starting position for this Investment account in any currency or security.
            Booked against <em>Equity:Opening Balances</em>.
          </p>
        </hlm-sheet-header>

        @if (account(); as a) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Account</label>
              <p class="text-sm text-muted-foreground">{{ a.name }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel id="add-position-commodity-label">Commodity</label>
              <app-commodity-picker
                ariaLabel="Select commodity"
                placeholder="Pick a currency or security"
                [value]="commodityId()"
                [selectedCommodity]="commodity()"
                [excludeIds]="excludedCommodityIds()"
                (commodityChange)="onCommodityChange($event)"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="add-position-quantity">Quantity</label>
              <input
                hlmInput
                id="add-position-quantity"
                class="w-full"
                [appMoneyInput]="quantityPrecision()"
                placeholder="0"
                [value]="quantity()"
                (amountInput)="quantity.set($event)"
              />
              @if (commodity(); as c) {
                <p class="text-xs text-muted-foreground">In units of {{ c.symbol }}.</p>
              }
            </div>

            @if (showUnitPrice()) {
              <div class="space-y-2">
                <label hlmLabel for="add-position-unit-price">
                  Per-unit price (optional)
                </label>
                <input
                  hlmInput
                  id="add-position-unit-price"
                  class="w-full"
                  appMoneyInput
                  placeholder="0.00"
                  [value]="unitPrice()"
                  (amountInput)="unitPrice.set($event)"
                />
                <p class="text-xs text-muted-foreground">
                  Records a price observation in the account's reporting currency on the as-of date.
                  Leave blank to capture the position without a historical price — display value will
                  resolve via carry-forward when a later price is recorded.
                </p>
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel for="add-position-date">As-of date</label>
              <hlm-date-picker
                class="w-full"
                captionLayout="dropdown"
                [date]="asOfDatePicker.date()"
                [max]="maxDate"
                (dateChange)="asOfDatePicker.set($event)"
              >
                <hlm-date-picker-input inputId="add-position-date" placeholder="Select date" />
              </hlm-date-picker>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="add-position-memo">Memo (optional)</label>
              <input
                hlmInput
                id="add-position-memo"
                class="w-full"
                maxlength="512"
                placeholder="e.g. Carried over from old broker"
                [value]="memo()"
                (input)="memo.set(asValue($event))"
              />
            </div>

            @if (formError()) {
              <p class="text-sm text-destructive">{{ formError() }}</p>
            }
          </div>
        }

        <hlm-sheet-footer class="flex flex-col gap-2 sm:flex-row sm:justify-end">
          <button hlmBtn class="w-full sm:w-auto" (click)="handleSave()" [disabled]="!canSubmit() || saving()">
            {{ saving() ? 'Saving...' : 'Add position' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class AddPositionSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  // Commodities the user already has a position in — passed in by the parent so the
  // picker can hide them. Backend will still 409 if a duplicate sneaks past, surfaced
  // via toast.
  excludedCommodityIds = input<readonly string[]>([]);

  saved = output<OpeningBalanceResponse>();
  closed = output<void>();

  commodity = signal<CommodityResponse | null>(null);
  quantity = signal('');
  unitPrice = signal('');
  asOfDate = signal(todayIso());
  memo = signal('');
  saving = signal(false);
  formError = signal('');

  private txApi = inject(FinancesTransactionApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly maxDate = tomorrowDate();

  protected readonly asOfDatePicker = isoDateBinding(this.asOfDate);

  commodityId = computed(() => this.commodity()?.id ?? null);

  showUnitPrice = computed(() => this.commodity()?.kind === 'Security');

  // The picker is not kind-filtered — an investment account can hold a cash position alongside its
  // securities — so this resolves per pick: 2 for a USD position, null (free-precision) for a share
  // count. The per-unit price beside it stays free-precision unconditionally, and only shows for a
  // security anyway.
  quantityPrecision = computed(() => currencyInputPrecision(this.commodity()));

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.commodityId()) return false;
    if (!this.asOfDate()) return false;
    if (this.quantity() === '') return false;
    const q = Number(this.quantity());
    if (!Number.isFinite(q) || q < 0) return false;
    if (this.showUnitPrice() && this.unitPrice() !== '') {
      const p = Number(this.unitPrice());
      if (!Number.isFinite(p) || p <= 0) return false;
    }
    return true;
  });

  constructor() {
    effect(() => {
      if (this.state() !== 'open') return;
      this.commodity.set(null);
      this.quantity.set('');
      this.unitPrice.set('');
      this.asOfDate.set(todayIso());
      this.memo.set('');
      this.saving.set(false);
      this.formError.set('');
    });
  }

  onCommodityChange(commodity: CommodityResponse): void {
    this.commodity.set(commodity);
    if (commodity.kind !== 'Security') {
      this.unitPrice.set('');
    }
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

    const valuePerUnit = this.showUnitPrice() && this.unitPrice() !== ''
      ? Number(this.unitPrice())
      : null;

    const trimmedMemo = trimToNull(this.memo());

    this.txApi.createOpeningBalance({
      accountId: a.id,
      body: {
        commodityId,
        quantity: Number(this.quantity()),
        valuePerUnit,
        asOfDate: this.asOfDate(),
        memo: trimmedMemo,
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to add position.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success('Position added.');
        this.saved.emit(result);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
