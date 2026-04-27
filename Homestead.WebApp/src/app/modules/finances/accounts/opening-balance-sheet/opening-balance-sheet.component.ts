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
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import { todayIso, tomorrowIso } from '../../shared/date';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

export type OpeningBalanceSheetMode = 'edit' | 'create-missing';

@Component({
  selector: 'app-opening-balance-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput],
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
              <label hlmLabel for="opening-balance-quantity">Amount</label>
              <input
                hlmInput
                id="opening-balance-quantity"
                class="w-full"
                type="number"
                step="0.01"
                min="0"
                placeholder="0.00"
                [value]="quantity()"
                (input)="quantity.set(asValue($event))"
              />
              @if (a.accountType === 'Liability') {
                <p class="text-xs text-muted-foreground">Enter the amount initially owed (positive).</p>
              }
            </div>

            <div class="space-y-2">
              <label hlmLabel for="opening-balance-date">As-of date</label>
              <input
                hlmInput
                id="opening-balance-date"
                class="w-full"
                type="date"
                [max]="maxDate"
                [value]="asOfDate()"
                (input)="asOfDate.set(asValue($event))"
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

  quantity = signal('');
  asOfDate = signal(todayIso());
  saving = signal(false);
  deleting = signal(false);
  formError = signal('');
  commodity = signal<CommodityResponse | null>(null);

  private txApi = inject(FinancesTransactionApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly maxDate = tomorrowIso();

  commodityLabel = computed(() => {
    const c = this.commodity();
    return c ? `${c.symbol} — ${c.name}` : null;
  });

  canSubmit = computed(() => {
    if (!this.account()) return false;
    if (!this.asOfDate()) return false;
    if (this.quantity() === '') return false;
    const q = Number(this.quantity());
    if (!Number.isFinite(q) || q < 0) return false;
    return true;
  });

  constructor() {
    effect(() => {
      const a = this.account();
      if (!a || this.state() !== 'open') return;
      this.commodity.set(null);
      this.commodityApi.getCommodity({ id: a.reportingCurrencyId }).subscribe({
        next: c => this.commodity.set(c),
        error: () => this.commodity.set(null),
      });

      this.saving.set(false);
      this.deleting.set(false);
      this.formError.set('');

      const ex = this.existing();
      if (this.mode() === 'edit' && ex) {
        this.quantity.set(String(ex.quantity));
        this.asOfDate.set(ex.asOfDate);
      } else {
        this.quantity.set('');
        this.asOfDate.set(todayIso());
      }
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const a = this.account();
    if (!a || !this.canSubmit()) return;

    this.saving.set(true);
    this.formError.set('');

    const ex = this.existing();
    const obs = this.mode() === 'edit' && ex
      ? this.txApi.patchOpeningBalance({
          accountId: a.id,
          commodityId: ex.commodityId,
          body: {
            quantity: Number(this.quantity()),
            asOfDate: this.asOfDate(),
          },
        })
      : this.txApi.createOpeningBalance({
          accountId: a.id,
          body: {
            commodityId: a.reportingCurrencyId,
            quantity: Number(this.quantity()),
            asOfDate: this.asOfDate(),
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
