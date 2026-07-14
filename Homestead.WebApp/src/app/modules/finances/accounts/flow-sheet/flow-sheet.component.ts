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
import { CategoryPickerComponent } from '../../shared/category-picker/category-picker.component';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso, tomorrowDate, toDateOnly } from '../../shared/date';
import { trimToNull } from '../../shared/trim-to-null';
import { commodityStep } from '../../shared/format-currency';
import { commodityResource } from '../../shared/commodity-resource';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type FlowSheetMode = 'create' | 'edit';
export type FlowDirection = 'Expense' | 'Income';

@Component({
  selector: 'app-flow-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, HlmDatePicker, HlmDatePickerInput, CategoryPickerComponent],
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
              <label hlmLabel>Type</label>
              <div role="radiogroup" class="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="selectedDirection() === 'Expense'"
                  [disabled]="mode() === 'edit'"
                  class="rounded-md border px-3 py-2 text-sm text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                  [class]="selectedDirection() === 'Expense' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                  (click)="setDirection('Expense')"
                >
                  <div class="font-medium">Expense</div>
                  <div class="text-xs text-muted-foreground">Money out</div>
                </button>
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="selectedDirection() === 'Income'"
                  [disabled]="mode() === 'edit'"
                  class="rounded-md border px-3 py-2 text-sm text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                  [class]="selectedDirection() === 'Income' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                  (click)="setDirection('Income')"
                >
                  <div class="font-medium">Income</div>
                  <div class="text-xs text-muted-foreground">Money in</div>
                </button>
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Category (optional)</label>
              <app-category-picker
                [value]="categoryId()"
                [direction]="selectedDirection()"
                ariaLabel="Select category"
                (valueChange)="categoryId.set($event)"
              />
              <p class="text-xs text-muted-foreground">Leave as Uncategorised to skip.</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Account</label>
              <p class="text-sm text-muted-foreground">{{ a.name }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="flow-amount">
                Amount
                @if (reportingCcy.value(); as c) {
                  <span class="ml-1 text-xs text-muted-foreground">({{ c.symbol }})</span>
                }
              </label>
              <input
                hlmInput
                id="flow-amount"
                class="w-full"
                type="number"
                [step]="amountStep()"
                min="0"
                placeholder="0.00"
                [value]="amount()"
                (input)="amount.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="flow-date">Date</label>
              <hlm-date-picker
                class="w-full"
                captionLayout="dropdown"
                [date]="datePicker.date()"
                [max]="maxDate"
                (dateChange)="datePicker.set($event)"
              >
                <hlm-date-picker-input inputId="flow-date" placeholder="Select date" />
              </hlm-date-picker>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="flow-description">Description (optional)</label>
              <input
                hlmInput
                id="flow-description"
                class="w-full"
                maxlength="512"
                placeholder="e.g. Groceries"
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
            {{ saving() ? 'Saving...' : ctaLabel() }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class FlowSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  mode = input<FlowSheetMode>('create');
  existing = input<TransactionResponse | null>(null);
  // The create-mode initial direction, chosen from the menu (Expense/Income). In edit mode the
  // direction is read from the stored shape instead, and the toggle is disabled (immutable).
  direction = input<FlowDirection>('Expense');

  saved = output<TransactionResponse>();
  deleted = output<void>();
  closed = output<void>();

  // `direction` is part of the key so a create-mode direction change re-seeds the toggle even
  // if the host re-opens the (otherwise identically-keyed) sheet without toggling state.
  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.mode()}|${this.existing()?.id ?? 'new'}|${this.direction()}|${this.state() === 'open'}`,
  );

  private seed = computed(() => {
    const a = this.account();
    const ex = this.existing();
    if (!a || this.state() !== 'open') {
      return { kind: 'closed' as const };
    }
    const d = ex?.details;
    const details = d?.kind === 'flow' ? d : null;
    if (this.mode() === 'edit' && ex && details) {
      return {
        kind: 'edit' as const,
        direction: (ex.shape === 'Income' ? 'Income' : 'Expense') as FlowDirection,
        amount: String(details.amount),
        date: toDateOnly(ex.occurredAt),
        description: ex.memo ?? '',
        categoryId: details.categoryId ?? null,
      };
    }
    return { kind: 'create' as const };
  });

  selectedDirection = seededLinkedSignal<string, FlowDirection>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.direction : this.direction();
  });
  amount = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.amount : '';
  });
  date = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.date : todayIso();
  });
  description = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.description : '';
  });
  categoryId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const seed = this.seed();
    return seed.kind === 'edit' ? seed.categoryId : null;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  deleting = seededLinkedSignal(this.seedKey, () => false);
  formError = seededLinkedSignal(this.seedKey, () => '');

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  // Single-position kinds hold one commodity, so the posting currency is inferred from the
  // account's reporting currency — no picker. Resolved for the amount-label symbol only.
  reportingCcy = commodityResource(
    this.commodityApi,
    () => this.account()?.reportingCurrencyId,
    () => this.state() === 'open',
  );

  amountStep = computed(() => commodityStep(this.reportingCcy.value()));

  readonly maxDate = tomorrowDate();

  protected readonly datePicker = isoDateBinding(this.date);

  private isExpense = computed(() => this.selectedDirection() === 'Expense');

  headerTitle = computed(() => {
    if (this.mode() === 'edit') return this.isExpense() ? 'Edit expense' : 'Edit income';
    return this.isExpense() ? 'New expense' : 'New income';
  });
  ctaLabel = computed(() => {
    if (this.mode() === 'edit') return 'Save changes';
    return this.isExpense() ? 'Create expense' : 'Create income';
  });
  headerDescription = computed(() => {
    if (this.mode() === 'edit') {
      return this.isExpense()
        ? "Update this expense. The type can't be changed."
        : "Update this income. The type can't be changed.";
    }
    return this.isExpense()
      ? 'Record money out of this account.'
      : 'Record money into this account.';
  });

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.date()) return false;
    const amt = Number(this.amount());
    if (!Number.isFinite(amt) || amt <= 0) return false;
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  // Category is direction-specific — switching Expense↔Income (create mode only) drops the picked
  // category, so a stale wrong-direction id can't be submitted.
  setDirection(direction: FlowDirection): void {
    if (this.selectedDirection() === direction) return;
    this.selectedDirection.set(direction);
    this.categoryId.set(null);
  }

  handleSave(): void {
    const a = this.account();
    if (!a || !this.canSubmit()) return;

    this.saving.set(true);
    this.formError.set('');

    const amount = Number(this.amount());
    const date = this.date();
    const description = trimToNull(this.description());

    const categoryId = this.categoryId();
    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchFlow({
          transactionId: ex.id,
          body: { accountId: a.id, amount, date, description, categoryId },
        })
      : this.txApi.createFlow({
          body: { direction: this.selectedDirection(), accountId: a.id, amount, date, description, categoryId },
        });

    obs.pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save transaction.'),
    ).subscribe({
      next: result => {
        this.saving.set(false);
        toast.success(this.mode() === 'edit' ? 'Transaction updated.' : 'Transaction recorded.');
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
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete transaction.'),
    ).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Transaction deleted.');
        this.deleted.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') this.closed.emit();
  }
}
