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
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { replace } from '@/shared/json-patch';
import { toast } from '@spartan-ng/brain/sonner';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-edit-commodity-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSheetImports, HlmButton, HlmLabel, HlmInput],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit commodity</h3>
          <p hlmSheetDescription>Symbol, exchange, and kind cannot be changed.</p>
        </hlm-sheet-header>

        @if (commodity(); as c) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="grid grid-cols-2 gap-3">
              <div class="space-y-2">
                <label hlmLabel>Symbol</label>
                <input hlmInput class="w-full" [value]="c.symbol" disabled />
              </div>
              <div class="space-y-2">
                <label hlmLabel>Exchange</label>
                <input hlmInput class="w-full" [value]="c.exchange ?? ''" disabled />
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="commodity-name">Name</label>
              <input
                hlmInput
                id="commodity-name"
                class="w-full"
                maxlength="256"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="commodity-precision">Display precision</label>
              <input
                hlmInput
                id="commodity-precision"
                class="w-full"
                type="number"
                min="0"
                max="10"
                step="1"
                [value]="editPrecision()"
                (input)="editPrecision.set(asValue($event))"
              />
              <p class="text-xs text-muted-foreground">Decimal places used when displaying quantities and prices.</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Kind</label>
              <p class="text-sm text-muted-foreground">{{ c.kind }}</p>
            </div>
          </div>
        }

        <hlm-sheet-footer class="flex flex-col gap-2">
          <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!canSave() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditCommoditySheetComponent {
  commodity = input<CommodityResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<CommodityResponse>();
  closed = output<void>();

  editName = signal('');
  editPrecision = signal('');
  saving = signal(false);

  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  hasChanges = computed(() => {
    const c = this.commodity();
    if (!c) return false;
    if (this.editName().trim() !== c.name) return true;
    if (Number(this.editPrecision()) !== c.displayPrecision) return true;
    return false;
  });

  canSave = computed(() => {
    if (!this.commodity()) return false;
    if (!this.editName().trim()) return false;
    const p = Number(this.editPrecision());
    if (!Number.isInteger(p) || p < 0 || p > 10) return false;
    return this.hasChanges();
  });

  constructor() {
    effect(() => {
      const c = this.commodity();
      if (c && this.state() === 'open') {
        this.editName.set(c.name);
        this.editPrecision.set(String(c.displayPrecision));
        this.saving.set(false);
      }
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const c = this.commodity();
    if (!c || !this.canSave()) return;
    this.saving.set(true);

    const ops: JsonPatchDocument = [];
    if (this.editName().trim() !== c.name) ops.push(replace('/name', this.editName().trim()));
    const newPrecision = Number(this.editPrecision());
    if (newPrecision !== c.displayPrecision) ops.push(replace('/displayPrecision', newPrecision));

    this.commodityApi.patchCommodity({ id: c.id, body: ops }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Commodity updated.');
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
