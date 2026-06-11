import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  DestroyRef,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { itemStatusLabel } from '../../shared/item-status-meta';
import { SUPPLY_STATUSES } from '../supply-shared';
import { replace } from '@/shared/json-patch';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';

@Component({
  selector: 'app-edit-supply-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, HlmSheetImports, HlmButton, HlmLabel, HlmInput, HlmSelectImports],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit supply</h3>
          <p hlmSheetDescription>Update name, unit, restock threshold, and status.</p>
        </hlm-sheet-header>

        @if (supply(); as s) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="supply-name">Name</label>
              <input
                hlmInput
                id="supply-name"
                class="w-full"
                maxlength="256"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="supply-unit">Unit</label>
              <input
                hlmInput
                id="supply-unit"
                class="w-full"
                maxlength="32"
                [value]="editUnit()"
                (input)="editUnit.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="supply-threshold">Restock threshold</label>
              <input
                hlmInput
                id="supply-threshold"
                type="number"
                step="0.001"
                min="0"
                class="w-full"
                placeholder="(none)"
                [value]="editThreshold()"
                (input)="editThreshold.set(asValue($event))"
              />
              <p class="text-xs text-muted-foreground">Flagged low when the total on hand is at or below this.</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Status</label>
              <hlm-select [ngModel]="editStatus()" (ngModelChange)="editStatus.set($event)" [itemToString]="statusToString">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content *hlmSelectPortal>
                  <hlm-select-group>
                    @for (st of statuses; track st) {
                      <hlm-select-item [value]="st">{{ statusToString(st) }}</hlm-select-item>
                    }
                  </hlm-select-group>
                </hlm-select-content>
              </hlm-select>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="supply-notes">Notes</label>
              <textarea
                hlmInput
                id="supply-notes"
                class="w-full"
                rows="3"
                placeholder="(none)"
                [value]="editNotes()"
                (input)="editNotes.set(asValue($event))"
              ></textarea>
            </div>

            @if (validationError(); as err) {
              <p class="text-sm text-destructive">{{ err }}</p>
            }
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
export class EditSupplySheetComponent {
  supply = input<SupplyResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<SupplyResponse>();
  closed = output<void>();

  protected readonly statuses = SUPPLY_STATUSES;

  private seedKey = computed(() => `${this.supply()?.id ?? ''}|${this.state() === 'open'}`);

  editName = seededLinkedSignal(this.seedKey, () => this.openSupply()?.name ?? '');
  editUnit = seededLinkedSignal(this.seedKey, () => this.openSupply()?.unit ?? '');
  editThreshold = seededLinkedSignal(this.seedKey, () => {
    const t = this.openSupply()?.restockThreshold;
    return t != null ? String(t) : '';
  });
  editStatus = seededLinkedSignal<string, ItemStatus>(this.seedKey, () => this.openSupply()?.status ?? 'Active');
  editNotes = seededLinkedSignal(this.seedKey, () => this.openSupply()?.notes ?? '');
  saving = seededLinkedSignal(this.seedKey, () => false);

  private supplyApi = inject(InventorySupplyApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  private openSupply(): SupplyResponse | null {
    return this.state() === 'open' ? this.supply() : null;
  }

  private normalizedThreshold = computed<number | null | typeof NaN>(() => {
    const raw = this.editThreshold().trim();
    if (raw === '') return null;
    const num = Number(raw);
    return Number.isFinite(num) ? num : NaN;
  });

  validationError = computed<string | null>(() => {
    if (!this.editName().trim()) return 'Name is required.';
    if (!this.editUnit().trim()) return 'Unit is required.';
    const t = this.normalizedThreshold();
    if (t !== null && Number.isNaN(t)) return 'Restock threshold must be a number.';
    if (t !== null && (t as number) < 0) return 'Restock threshold must be zero or positive.';
    return null;
  });

  hasChanges = computed(() => this.buildOps().length > 0);
  canSave = computed(() => this.validationError() === null && this.hasChanges());

  statusToString = (status: ItemStatus): string => itemStatusLabel(status);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  private buildOps(): JsonPatchDocument {
    const s = this.supply();
    if (!s) return [];
    const ops: JsonPatchDocument = [];

    const name = this.editName().trim();
    if (name !== s.name) ops.push(replace('/name', name));

    const unit = this.editUnit().trim();
    if (unit !== s.unit) ops.push(replace('/unit', unit));

    const threshold = this.normalizedThreshold();
    if (!Number.isNaN(threshold) && threshold !== (s.restockThreshold ?? null)) {
      ops.push(replace('/restockThreshold', threshold));
    }

    const status = this.editStatus();
    if (status !== s.status) ops.push(replace('/status', status));

    const notes = this.editNotes().trim() ? this.editNotes().trim() : null;
    if (notes !== (s.notes ?? null)) ops.push(replace('/notes', notes));

    return ops;
  }

  handleSave(): void {
    const s = this.supply();
    if (!s || !this.canSave()) return;
    const ops = this.buildOps();
    if (ops.length === 0) return;

    this.saving.set(true);
    this.supplyApi.patchSupply({ id: s.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Supply updated.');
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
