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
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { CurrencyPickerComponent } from '../../shared/currency-picker/currency-picker.component';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { FinancesCommodityPriceApiService } from '@/api/services/finances-commodity-price-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';
import {
  combineDateAndTimeToIso,
  currentTimeHhMm,
  localStartOfToday,
} from '../../shared/date-time';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';

@Component({
  selector: 'app-new-price-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    CurrencyPickerComponent,
    HlmDatePicker,
    HlmDatePickerInput,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" [variant]="triggerVariant()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[480px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New manual price</h2>
          <p hlmDialogDescription>
            Record an observation: 1 {{ commodity().symbol }} was worth
            <em>rate</em> in the chosen quote currency at the chosen instant.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Quote currency</label>
            <app-currency-picker
              [value]="quoteId()"
              (valueChange)="quoteId.set($event)"
              (commodityChange)="onQuotePicked($event)"
              [selectedCommodity]="quoteCommodity()"
              placeholder="Select quote currency"
              ariaLabel="Quote currency"
            />
            @if (selfPriceWarning()) {
              <p class="text-xs text-destructive">A commodity cannot be priced in itself.</p>
            }
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="new-price-date">Date</label>
              <hlm-date-picker
                class="w-full"
                captionLayout="dropdown"
                [date]="dateValue() ?? undefined"
                (dateChange)="dateValue.set($event ?? null)"
              >
                <hlm-date-picker-input inputId="new-price-date" placeholder="Select date" />
              </hlm-date-picker>
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="new-price-time">Time</label>
              <input
                hlmInput
                id="new-price-time"
                type="time"
                step="60"
                class="bg-background appearance-none [&::-webkit-calendar-picker-indicator]:hidden [&::-webkit-calendar-picker-indicator]:appearance-none"
                [value]="timeInput()"
                (input)="timeInput.set(asValue($event))"
              />
            </div>
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="new-price-rate">Rate</label>
            <input
              hlmInput
              id="new-price-rate"
              type="number"
              step="any"
              min="0"
              placeholder="e.g. 35.4200"
              [value]="rateInput()"
              (input)="rateInput.set(asValue($event))"
            />
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Saving...' : 'Save price' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class NewPriceDialogComponent {
  commodity = input.required<CommodityResponse>();
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  triggerVariant = input<'default' | 'outline' | 'secondary' | 'ghost'>('default');
  created = output<CommodityPriceResponse>();

  quoteId = signal<string | null>(null);
  quoteCommodity = signal<CommodityResponse | null>(null);
  dateValue = signal<Date | null>(localStartOfToday());
  timeInput = signal(currentTimeHhMm());
  rateInput = signal('');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private priceApi = inject(FinancesCommodityPriceApiService);
  private errorHandler = inject(ErrorHandlerService);

  selfPriceWarning = computed(() => {
    const id = this.quoteId();
    return id !== null && id === this.commodity().id;
  });

  canSubmit = computed(() => {
    if (!this.quoteId()) return false;
    if (this.selfPriceWarning()) return false;
    if (!this.dateValue()) return false;
    if (!this.timeInput()) return false;
    const rate = Number(this.rateInput());
    if (!Number.isFinite(rate) || rate <= 0) return false;
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onQuotePicked(c: CommodityResponse): void {
    this.quoteCommodity.set(c);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.quoteId.set(null);
      this.quoteCommodity.set(null);
      this.dateValue.set(localStartOfToday());
      this.timeInput.set(currentTimeHhMm());
      this.rateInput.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    const priceAt = combineDateAndTimeToIso(this.dateValue()!, this.timeInput());
    if (!priceAt) return;

    this.creating.set(true);
    this.createError.set('');

    this.priceApi.createCommodityPrice({
      body: {
        commodityId: this.commodity().id,
        quoteCurrencyId: this.quoteId()!,
        priceAt,
        rate: Number(this.rateInput()),
      },
    }).subscribe({
      next: (price) => {
        this.creating.set(false);
        toast.success('Price recorded.');
        this.dialog()?.close();
        this.created.emit(price);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to record price.'));
      },
    });
  }
}
