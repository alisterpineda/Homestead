import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinanceSettingsService } from '../../shared/finance-settings.service';
import { CurrencyPickerComponent } from '../../shared/currency-picker/currency-picker.component';
import { currencyInputPrecision } from '../../shared/format-currency';
import { MoneyInputDirective } from '../../shared/money-input.directive';
import { KindPickerComponent } from '../../shared/kind-picker.component';
import type { UserAccountKind } from '../../shared/account-kind-meta';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { todayIso, tomorrowDate } from '../../shared/date';

@Component({
  selector: 'app-create-account-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmInput,
    MoneyInputDirective,
    HlmLabel,
    HlmDatePicker,
    HlmDatePickerInput,
    CurrencyPickerComponent,
    KindPickerComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[560px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New account</h2>
          <p hlmDialogDescription>Pick a kind and fill in the basics. You can edit these later.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label hlmLabel>Kind</label>
            <app-kind-picker [(value)]="kind" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-account-name">Name</label>
            <input
              hlmInput
              id="create-account-name"
              maxlength="256"
              placeholder="e.g. Chequing"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Reporting currency</label>
            <app-currency-picker
              [value]="currencyId()"
              (valueChange)="currencyId.set($event)"
              (commodityChange)="onCurrencyPicked($event)"
              [selectedCommodity]="pickedCurrency()"
              placeholder="Select reporting currency"
              ariaLabel="Reporting currency"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-account-institution">Institution (optional)</label>
            <input
              hlmInput
              id="create-account-institution"
              maxlength="128"
              placeholder="e.g. RBC"
              [value]="institution()"
              (input)="institution.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-account-number">Account number (optional)</label>
            <input
              hlmInput
              id="create-account-number"
              maxlength="64"
              placeholder="e.g. 1234567890"
              [value]="accountNumber()"
              (input)="accountNumber.set(asValue($event))"
            />
          </div>

          @if (showInitialBalance()) {
            <fieldset class="flex flex-col gap-3 rounded-md border border-input p-3">
              <legend class="px-1 text-sm font-medium">Initial balance</legend>
              <p class="text-xs text-muted-foreground">
                Enter the current balance and the date it applies to. Liabilities use a positive amount owed.
              </p>
              <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div class="flex flex-col gap-1.5">
                  <label hlmLabel for="create-account-amount">Amount</label>
                  <input
                    hlmInput
                    id="create-account-amount"
                    [appMoneyInput]="initialAmountPrecision()"
                    placeholder="0.00"
                    [value]="initialAmount()"
                    (amountInput)="initialAmount.set($event)"
                  />
                </div>
                <div class="flex flex-col gap-1.5">
                  <label hlmLabel for="create-account-as-of">As-of date</label>
                  <hlm-date-picker
                    class="w-full"
                    captionLayout="dropdown"
                    [date]="initialAsOfDatePicker.date()"
                    [max]="maxAsOfDate"
                    (dateChange)="initialAsOfDatePicker.set($event)"
                  >
                    <hlm-date-picker-input inputId="create-account-as-of" placeholder="Select date" />
                  </hlm-date-picker>
                </div>
              </div>
            </fieldset>
          }

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create account' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateAccountDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  kind = signal<UserAccountKind | null>(null);
  name = signal('');
  currencyId = signal<string | null>(null);
  pickedCurrency = signal<CommodityResponse | null>(null);

  // Tracks the reporting-currency picker above it: switch USD → JPY and the initial balance settles
  // at 0 decimals instead of 2. Null until a currency is picked, so nothing is rounded prematurely.
  initialAmountPrecision = computed(() => currencyInputPrecision(this.pickedCurrency()));
  institution = signal('');
  accountNumber = signal('');
  initialAmount = signal('');
  initialAsOfDate = signal(todayIso());
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private accountApi = inject(FinancesAccountApiService);
  private settingsService = inject(FinanceSettingsService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);

  readonly maxAsOfDate = tomorrowDate();

  protected readonly initialAsOfDatePicker = isoDateBinding(this.initialAsOfDate);

  showInitialBalance = computed(() => {
    const k = this.kind();
    return k !== null && k !== 'Investment';
  });

  canSubmit = computed(() => {
    if (!this.kind()) return false;
    if (!this.name().trim()) return false;
    if (!this.currencyId()) return false;
    if (this.showInitialBalance()) {
      if (!this.initialAsOfDate()) return false;
      const amt = Number(this.initialAmount());
      if (this.initialAmount() === '' || !Number.isFinite(amt) || amt < 0) return false;
    }
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onCurrencyPicked(commodity: CommodityResponse): void {
    this.pickedCurrency.set(commodity);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.kind.set(null);
      this.name.set('');
      // Default reporting currency to the household display currency when set.
      const display = this.settingsService.displayCurrency();
      this.currencyId.set(display?.id ?? null);
      this.pickedCurrency.set(display ?? null);
      this.institution.set('');
      this.accountNumber.set('');
      this.initialAmount.set('');
      this.initialAsOfDate.set(todayIso());
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;
    const k = this.kind();
    const currencyId = this.currencyId();
    if (!k || !currencyId) return;

    this.creating.set(true);
    this.createError.set('');

    const initialBalance = this.showInitialBalance()
      ? { amount: Number(this.initialAmount()), asOfDate: this.initialAsOfDate() }
      : null;

    this.accountApi.createAccount({
      body: {
        kind: k,
        name: this.name().trim(),
        reportingCurrencyId: currencyId,
        institution: this.institution().trim() ? this.institution().trim() : null,
        accountNumber: this.accountNumber().trim() ? this.accountNumber().trim() : null,
        initialBalance,
      },
    }).subscribe({
      next: (account) => {
        this.creating.set(false);
        toast.success('Account created.');
        this.dialog()?.close();
        this.created.emit();
        this.router.navigate(['/finances/accounts', account.id]);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create account.'));
      },
    });
  }
}
