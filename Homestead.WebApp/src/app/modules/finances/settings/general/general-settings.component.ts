import { Component, ChangeDetectionStrategy, inject, signal, computed, effect } from '@angular/core';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { CurrencyPickerComponent } from '../../shared/currency-picker/currency-picker.component';
import { FinanceSettingsService } from '../../shared/finance-settings.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-general-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmLabel, CurrencyPickerComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 max-w-2xl">
      <header>
        <h2 class="text-xl font-semibold">General</h2>
        <p class="text-sm text-muted-foreground">
          Pick the currency Homestead uses to display balances and net worth.
        </p>
      </header>

      <section class="rounded-lg border border-border bg-card p-4 text-card-foreground">
        <div class="space-y-2">
          <label hlmLabel>Display currency</label>
          <p class="text-xs text-muted-foreground">
            All balances and net-worth totals are shown in this currency. Underlying transactions
            keep their native currency — changing this only affects how amounts are presented.
          </p>
          <app-currency-picker
            [value]="selectedId()"
            [selectedCommodity]="selectedCommodity()"
            (valueChange)="selectedId.set($event)"
            (commodityChange)="selectedCommodity.set($event)"
            placeholder="Select display currency"
            ariaLabel="Display currency"
          />
        </div>
        <div class="mt-4 flex items-center gap-2">
          <button hlmBtn (click)="save()" [disabled]="!canSave() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </div>
      </section>
    </div>
  `,
})
export class GeneralSettingsComponent {
  private settingsService = inject(FinanceSettingsService);
  private errorHandler = inject(ErrorHandlerService);

  selectedId = signal<string | null>(null);
  selectedCommodity = signal<CommodityResponse | null>(null);
  saving = signal(false);

  canSave = computed(() => {
    const id = this.selectedId();
    return id !== null && id !== this.settingsService.displayCurrencyId();
  });

  constructor() {
    effect(() => {
      if (this.settingsService.loaded()) {
        const id = this.settingsService.displayCurrencyId();
        if (this.selectedId() === null && id !== null) {
          this.selectedId.set(id);
          const c = this.settingsService.displayCurrency();
          if (c) this.selectedCommodity.set(c);
        }
      }
    });
  }

  save(): void {
    const id = this.selectedId();
    if (!id) return;
    this.saving.set(true);
    this.settingsService.setDisplayCurrencyId(id).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save settings.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Display currency updated.');
      },
    });
  }
}
