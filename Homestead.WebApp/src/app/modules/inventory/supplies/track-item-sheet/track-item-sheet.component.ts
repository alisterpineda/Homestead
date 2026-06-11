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
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { formatQty } from '../supply-shared';
import type { StockOpLocation } from '../stock-op-sheet/stock-op-sheet.component';
import type { TrackItemResponse } from '@/api/generated/models/track-item-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';

/**
 * Carves one unit out of fungible stock at a fixed location into a new individually-tracked item.
 * The source location is not editable here — it is the row the user acted on. Compartment is only
 * offered when that source is a container.
 */
@Component({
  selector: 'app-track-item-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Track one individually</h3>
          <p hlmSheetDescription>
            Remove one {{ unit() }} from stock and start tracking it as its own item — with its own
            serial, value, and history.
          </p>
        </hlm-sheet-header>

        <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
          <!-- Fixed source location -->
          <div class="space-y-1">
            <span class="text-sm text-muted-foreground">From</span>
            <p class="text-sm font-medium">{{ fromLabel() }}</p>
            <p class="text-xs text-muted-foreground">{{ onHandLabel() }} on hand</p>
          </div>

          <!-- Name -->
          <div class="space-y-2">
            <label hlmLabel for="track-name">Name</label>
            <input
              hlmInput
              id="track-name"
              maxlength="256"
              class="w-full"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <!-- Compartment (container source only) -->
          @if (isContainerSource()) {
            <div class="space-y-2">
              <label hlmLabel for="track-compartment">Compartment (optional)</label>
              <input
                hlmInput
                id="track-compartment"
                maxlength="256"
                class="w-full"
                placeholder="e.g. Top drawer"
                [value]="compartment()"
                (input)="compartment.set(asValue($event))"
              />
            </div>
          }

          <!-- Note -->
          <div class="space-y-2">
            <label hlmLabel for="track-note">Note (optional)</label>
            <textarea
              hlmInput
              id="track-note"
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
            {{ saving() ? 'Saving...' : 'Track individually' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class TrackItemSheetComponent {
  supplyId = input.required<string>();
  supplyName = input.required<string>();
  unit = input.required<string>();
  /** The fixed location the unit is carved from. */
  fromLocation = input<StockOpLocation | null>(null);
  state = input<BrnDialogState>('closed');

  created = output<TrackItemResponse>();
  closed = output<void>();

  private supplyApi = inject(InventorySupplyApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  // Reset all fields whenever the sheet (re)opens for a given supply/location.
  private seedKey = computed(() =>
    `${this.supplyId()}|${this.fromLocation()?.label ?? ''}|${this.state() === 'open'}`);

  // Name is prefilled with the supply name as a sensible starting point.
  name = seededLinkedSignal(this.seedKey, () => this.supplyName());
  compartment = seededLinkedSignal(this.seedKey, () => '');
  note = seededLinkedSignal(this.seedKey, () => '');
  saving = seededLinkedSignal(this.seedKey, () => false);

  protected isContainerSource = computed(() => this.fromLocation()?.containerItemId != null);
  protected fromLabel = computed(() => this.fromLocation()?.label ?? '');
  protected onHandLabel = computed(() => formatQty(this.fromLocation()?.quantity ?? 0));

  validationError = computed<string | null>(() => {
    if (!this.name().trim()) return 'Enter a name.';
    return null;
  });

  canSubmit = computed(() => this.validationError() === null);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  handleSubmit(): void {
    if (!this.canSubmit() || this.saving()) return;
    const src = this.fromLocation();
    if (!src) return;
    const compartment = this.isContainerSource() && this.compartment().trim()
      ? this.compartment().trim()
      : null;
    this.saving.set(true);

    this.supplyApi.trackItem({
      id: this.supplyId(),
      body: {
        areaId: src.areaId,
        containerItemId: src.containerItemId,
        name: this.name().trim(),
        compartment,
        note: this.note().trim() ? this.note().trim() : null,
      },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to track item.'),
    ).subscribe({
      next: (result) => {
        this.saving.set(false);
        toast.success(`Now tracking "${result.item.name}" individually.`);
        this.created.emit(result);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
