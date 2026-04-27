import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { FinancesCommodityPriceApiService } from '@/api/services/finances-commodity-price-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { replace } from '@/shared/json-patch';
import { toast } from '@spartan-ng/brain/sonner';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-edit-price-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Edit price</h2>
          <p hlmDialogDescription>Only the rate can be changed. Other fields require a new price.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="edit-price-rate">Rate</label>
            <input
              hlmInput
              id="edit-price-rate"
              type="number"
              step="any"
              min="0"
              [value]="rateInput()"
              (input)="rateInput.set(asValue($event))"
            />
          </div>

          @if (saveError()) {
            <p class="text-sm text-destructive">{{ saveError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || saving()">
              {{ saving() ? 'Saving...' : 'Save' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class EditPriceDialogComponent {
  price = input<CommodityPriceResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<CommodityPriceResponse>();
  closed = output<void>();

  rateInput = signal('');
  saving = signal(false);
  saveError = signal('');

  private priceApi = inject(FinancesCommodityPriceApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() => {
    const p = this.price();
    if (!p) return false;
    const rate = Number(this.rateInput());
    if (!Number.isFinite(rate) || rate <= 0) return false;
    return rate !== p.rate;
  });

  constructor() {
    effect(() => {
      const p = this.price();
      if (p && this.state() === 'open') {
        this.rateInput.set(String(p.rate));
        this.saving.set(false);
        this.saveError.set('');
      }
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const p = this.price();
    if (!p || !this.canSubmit()) return;

    const ops: JsonPatchDocument = [replace('/rate', Number(this.rateInput()))];
    this.saving.set(true);
    this.saveError.set('');

    this.priceApi.patchCommodityPrice({ id: p.id, body: ops }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save price.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Price updated.');
        this.saved.emit(updated);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
