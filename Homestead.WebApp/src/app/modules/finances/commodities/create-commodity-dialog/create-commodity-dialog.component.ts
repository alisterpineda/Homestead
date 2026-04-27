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
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-create-commodity-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[480px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New security</h2>
          <p hlmDialogDescription>
            Symbol, exchange, and kind cannot be changed once created.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="grid grid-cols-2 gap-3">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-commodity-symbol">Symbol</label>
              <input
                hlmInput
                id="create-commodity-symbol"
                maxlength="32"
                placeholder="e.g. VEQT"
                autocapitalize="characters"
                spellcheck="false"
                [value]="symbol()"
                (input)="symbol.set(asValue($event))"
              />
            </div>
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-commodity-exchange">Exchange</label>
              <input
                hlmInput
                id="create-commodity-exchange"
                maxlength="16"
                placeholder="e.g. TSX"
                autocapitalize="characters"
                spellcheck="false"
                [value]="exchange()"
                (input)="exchange.set(asValue($event))"
              />
            </div>
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-commodity-name">Name</label>
            <input
              hlmInput
              id="create-commodity-name"
              maxlength="256"
              placeholder="e.g. Vanguard All-Equity ETF"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-commodity-precision">Display precision</label>
            <input
              hlmInput
              id="create-commodity-precision"
              type="number"
              min="0"
              max="10"
              step="1"
              [value]="precision()"
              (input)="precision.set(asValue($event))"
            />
            <p class="text-xs text-muted-foreground">Number of decimal places used when displaying quantities and prices. Default 4.</p>
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create security' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateCommodityDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output<CommodityResponse>();

  symbol = signal('');
  exchange = signal('');
  name = signal('');
  precision = signal('4');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() => {
    if (!this.symbol().trim()) return false;
    if (!this.exchange().trim()) return false;
    if (!this.name().trim()) return false;
    const p = Number(this.precision());
    if (!Number.isInteger(p) || p < 0 || p > 10) return false;
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.symbol.set('');
      this.exchange.set('');
      this.name.set('');
      this.precision.set('4');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;
    this.creating.set(true);
    this.createError.set('');

    this.commodityApi.createCommodity({
      body: {
        symbol: this.symbol().trim(),
        exchange: this.exchange().trim(),
        name: this.name().trim(),
        displayPrecision: Number(this.precision()),
      },
    }).subscribe({
      next: (commodity) => {
        this.creating.set(false);
        toast.success('Security created.');
        this.dialog()?.close();
        this.created.emit(commodity);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create security.'));
      },
    });
  }
}
