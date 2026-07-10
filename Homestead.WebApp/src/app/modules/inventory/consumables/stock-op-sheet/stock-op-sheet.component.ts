import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { CurrencyPickerComponent } from '@/modules/finances/shared/currency-picker/currency-picker.component';
import {
  LocationPickerComponent,
  type ItemLocationValue,
} from '../../shared/location-picker/location-picker.component';
import { formatQty } from '../consumable-shared';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { openGeneration } from '@/shared/utils/open-generation';
import { trimToNull } from '@/modules/finances/shared/trim-to-null';

export type StockOpMode = 'receive' | 'consume' | 'transfer' | 'adjust';

/** A fixed location for an op (the row the user acted on), or null when the user picks one. */
export interface StockOpLocation {
  areaId: string | null;
  containerItemId: string | null;
  label: string;
  /** Current on-hand at this location — pre-fills the adjust target. */
  quantity: number;
}

const META: Record<StockOpMode, { title: string; description: string; action: string }> = {
  receive: { title: 'Receive stock', description: 'Add stock into a location.', action: 'Receive' },
  consume: { title: 'Use stock', description: 'Record using some from this location.', action: 'Use' },
  transfer: { title: 'Transfer stock', description: 'Move stock to another location.', action: 'Transfer' },
  adjust: { title: 'Correct count', description: 'Set the on-hand here to its true value.', action: 'Save count' },
};

@Component({
  selector: 'app-stock-op-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput, CurrencyPickerComponent, LocationPickerComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ meta().title }}</h3>
          <p hlmSheetDescription>{{ meta().description }}</p>
        </hlm-sheet-header>

        <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
          <!-- Source / fixed location -->
          @if (mode() === 'receive') {
            <div class="space-y-2">
              <label hlmLabel>Into</label>
              <app-location-picker
                [value]="destValue()"
                (valueChange)="destValue.set($event)"
                [allowUnplaced]="false"
                placeholder="Select a location"
                ariaLabel="Receive into"
              />
            </div>
          } @else {
            <div class="space-y-1">
              <span class="text-sm text-muted-foreground">{{ mode() === 'transfer' ? 'From' : 'Location' }}</span>
              <p class="text-sm font-medium">{{ fromLabel() }}</p>
              @if (mode() !== 'adjust') {
                <p class="text-xs text-muted-foreground">{{ onHandLabel() }} on hand</p>
              }
            </div>
          }

          @if (mode() === 'transfer') {
            <div class="space-y-2">
              <label hlmLabel>To</label>
              <app-location-picker
                [value]="destValue()"
                (valueChange)="destValue.set($event)"
                [allowUnplaced]="false"
                placeholder="Select a destination"
                ariaLabel="Transfer to"
              />
            </div>
          }

          <!-- Quantity -->
          <div class="space-y-2">
            <label hlmLabel for="stock-qty">{{ mode() === 'adjust' ? 'Corrected quantity' : 'Quantity' }}</label>
            <div class="flex items-center gap-2">
              <input
                hlmInput
                id="stock-qty"
                type="number"
                step="0.001"
                [min]="mode() === 'adjust' ? 0 : 0.001"
                class="w-full"
                [value]="quantity()"
                (input)="quantity.set(asValue($event))"
              />
              <span class="shrink-0 text-sm text-muted-foreground">{{ unit() }}</span>
            </div>
          </div>

          <!-- Optional unit cost on receive -->
          @if (mode() === 'receive') {
            <div class="grid grid-cols-2 gap-3">
              <div class="space-y-2">
                <label hlmLabel for="stock-cost">Unit cost (optional)</label>
                <input
                  hlmInput
                  id="stock-cost"
                  type="number"
                  step="0.01"
                  min="0"
                  class="w-full"
                  placeholder="0.00"
                  [value]="unitCost()"
                  (input)="unitCost.set(asValue($event))"
                />
              </div>
              <div class="space-y-2">
                <label hlmLabel>Currency</label>
                <app-currency-picker
                  [value]="currencyId()"
                  (valueChange)="currencyId.set($event)"
                  (commodityChange)="pickedCurrency.set($event)"
                  [selectedCommodity]="pickedCurrency()"
                  [disabled]="!unitCost().trim()"
                  placeholder="Select currency"
                  ariaLabel="Unit cost currency"
                />
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="stock-source">Source (optional)</label>
              <input
                hlmInput
                id="stock-source"
                class="w-full"
                maxlength="256"
                placeholder="e.g. Costco"
                [value]="source()"
                (input)="source.set(asValue($event))"
              />
            </div>
          }

          <!-- Note -->
          <div class="space-y-2">
            <label hlmLabel for="stock-note">Note (optional)</label>
            <textarea
              hlmInput
              id="stock-note"
              class="w-full"
              rows="2"
              placeholder="(none)"
              [value]="note()"
              (input)="note.set(asValue($event))"
            ></textarea>
          </div>

          @if (validationError(); as err) {
            <p class="text-sm text-destructive">{{ err }}</p>
          }
        </div>

        <hlm-sheet-footer class="flex flex-col gap-2">
          <button hlmBtn class="w-full" (click)="handleSubmit()" [disabled]="!canSubmit() || saving()">
            {{ saving() ? 'Saving...' : meta().action }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class StockOpSheetComponent {
  consumableId = input.required<string>();
  unit = input.required<string>();
  mode = input.required<StockOpMode>();
  /** Fixed location for consume/transfer-source/adjust; null for receive. */
  fromLocation = input<StockOpLocation | null>(null);
  state = input<BrnDialogState>('closed');

  updated = output<ConsumableDetailResponse>();
  closed = output<void>();

  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  protected meta = computed(() => META[this.mode()]);

  // Reset all fields whenever the sheet (re)opens for a given consumable/mode/location.
  private openGen = openGeneration(this.state);
  private seedKey = computed(() =>
    `${this.consumableId()}|${this.mode()}|${this.fromLocation()?.label ?? ''}|${this.state() === 'open'}|${this.openGen()}`);

  quantity = seededLinkedSignal(this.seedKey, () => {
    // Adjust pre-fills the current on-hand so the user edits a number, not enters one blind.
    if (this.mode() === 'adjust' && this.state() === 'open') {
      const q = this.fromLocation()?.quantity ?? 0;
      return formatQty(q);
    }
    return '';
  });
  unitCost = seededLinkedSignal(this.seedKey, () => '');
  currencyId = seededLinkedSignal<string, string | null>(this.seedKey, () => null);
  pickedCurrency = seededLinkedSignal<string, CommodityResponse | null>(this.seedKey, () => null);
  note = seededLinkedSignal(this.seedKey, () => '');
  source = seededLinkedSignal(this.seedKey, () => '');
  destValue = seededLinkedSignal<string, ItemLocationValue>(this.seedKey, () => ({ kind: 'unplaced' }));
  saving = seededLinkedSignal(this.seedKey, () => false);

  protected fromLabel = computed(() => this.fromLocation()?.label ?? '');
  protected onHandLabel = computed(() => formatQty(this.fromLocation()?.quantity ?? 0));

  private parsedQuantity = computed(() => {
    const raw = this.quantity().trim();
    if (raw === '') return NaN;
    return Number(raw);
  });

  validationError = computed<string | null>(() => {
    const q = this.parsedQuantity();
    if (Number.isNaN(q)) return 'Enter a quantity.';
    if (this.mode() === 'adjust') {
      if (q < 0) return 'Quantity must be zero or positive.';
      // The field is pre-filled with the current on-hand; submitting it unchanged is a no-op the
      // server rejects, so block it here rather than surfacing that as an error toast.
      if (q === (this.fromLocation()?.quantity ?? 0)) return 'Enter a different quantity to correct.';
    } else if (q <= 0) {
      return 'Quantity must be greater than zero.';
    } else if (this.mode() === 'consume' || this.mode() === 'transfer') {
      // Advisory snapshot check — the server is authoritative, but blocking an obvious over-draw
      // here saves a round-trip and matches the steppers, which gate on optimistic on-hand.
      const onHand = this.fromLocation()?.quantity ?? 0;
      if (q > onHand) return `Only ${formatQty(onHand)} on hand here.`;
    }

    if ((this.mode() === 'receive' || this.mode() === 'transfer') && this.destValue().kind === 'unplaced') {
      return this.mode() === 'transfer' ? 'Pick a destination.' : 'Pick a location.';
    }
    if (this.mode() === 'transfer' && this.sameAsSource()) {
      return 'Destination must differ from the source.';
    }
    if (this.mode() === 'receive') {
      const cost = this.unitCost().trim();
      if (cost !== '') {
        if (!Number.isFinite(Number(cost)) || Number(cost) < 0) return 'Unit cost must be zero or positive.';
        if (!this.currencyId()) return 'Pick a currency for the unit cost.';
      }
    }
    return null;
  });

  canSubmit = computed(() => this.validationError() === null);

  private destLocationFields(): { areaId: string | null; containerItemId: string | null } {
    const v = this.destValue();
    if (v.kind === 'area') return { areaId: v.id, containerItemId: null };
    if (v.kind === 'container') return { areaId: null, containerItemId: v.id };
    return { areaId: null, containerItemId: null };
  }

  private sameAsSource(): boolean {
    const dest = this.destLocationFields();
    const src = this.fromLocation();
    if (!src) return false;
    return dest.areaId === src.areaId && dest.containerItemId === src.containerItemId;
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  handleSubmit(): void {
    if (!this.canSubmit()) return;
    const id = this.consumableId();
    const qty = this.parsedQuantity();
    const note = trimToNull(this.note());
    this.saving.set(true);

    const op$ = (() => {
      switch (this.mode()) {
        case 'receive': {
          const dest = this.destLocationFields();
          const cost = this.unitCost().trim();
          return this.consumableApi.receiveStock({
            id,
            body: {
              areaId: dest.areaId,
              containerItemId: dest.containerItemId,
              quantity: qty,
              unitCost: cost === '' ? null : Number(cost),
              unitCostCurrencyId: cost === '' ? null : this.currencyId(),
              note,
              source: trimToNull(this.source()),
            },
          });
        }
        case 'consume': {
          const src = this.fromLocation()!;
          return this.consumableApi.consumeStock({
            id,
            body: { areaId: src.areaId, containerItemId: src.containerItemId, quantity: qty, note },
          });
        }
        case 'transfer': {
          const src = this.fromLocation()!;
          const dest = this.destLocationFields();
          return this.consumableApi.transferStock({
            id,
            body: {
              fromAreaId: src.areaId,
              fromContainerItemId: src.containerItemId,
              toAreaId: dest.areaId,
              toContainerItemId: dest.containerItemId,
              quantity: qty,
              note,
            },
          });
        }
        case 'adjust': {
          const src = this.fromLocation()!;
          return this.consumableApi.adjustStock({
            id,
            body: { areaId: src.areaId, containerItemId: src.containerItemId, newQuantity: qty, note },
          });
        }
      }
    })();

    op$.pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to update stock.'),
    ).subscribe({
      next: (detail) => {
        this.saving.set(false);
        toast.success('Stock updated.');
        this.updated.emit(detail);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
