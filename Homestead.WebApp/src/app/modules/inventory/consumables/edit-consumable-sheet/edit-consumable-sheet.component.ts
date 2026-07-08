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
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { BrandPickerComponent } from '../../shared/brand-picker/brand-picker.component';
import { TagInputComponent } from '../../tags/tag-input/tag-input.component';
import { itemStatusLabel } from '../../shared/item-status-meta';
import { CONSUMABLE_STATUSES } from '../consumable-shared';
import { replace } from '@/shared/json-patch';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { BrandResponse } from '@/api/generated/models/brand-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { openGeneration } from '@/shared/utils/open-generation';

@Component({
  selector: 'app-edit-consumable-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, HlmSheetImports, HlmButton, HlmLabel, HlmInput, HlmSelectImports, BrandPickerComponent, TagInputComponent],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit consumable</h3>
          <p hlmSheetDescription>Update name, unit, restock threshold, and status.</p>
        </hlm-sheet-header>

        @if (consumable(); as s) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="consumable-name">Name</label>
              <input
                hlmInput
                id="consumable-name"
                class="w-full"
                maxlength="256"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="consumable-unit">Unit</label>
              <input
                hlmInput
                id="consumable-unit"
                class="w-full"
                maxlength="32"
                placeholder="e.g. rolls (optional)"
                [value]="editUnit()"
                (input)="editUnit.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="consumable-threshold">Restock threshold</label>
              <input
                hlmInput
                id="consumable-threshold"
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
              <label hlmLabel>Brand</label>
              <app-brand-picker
                [value]="editBrand()"
                (valueChange)="editBrand.set($event)"
                ariaLabel="Brand"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel>Tags</label>
              <app-tag-input
                [value]="editTags()"
                [initialColorMap]="tagColorMap()"
                (valueChange)="editTags.set($event)"
              />
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
              <label hlmLabel for="consumable-notes">Notes</label>
              <textarea
                hlmInput
                id="consumable-notes"
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
export class EditConsumableSheetComponent {
  consumable = input<ConsumableResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<ConsumableResponse>();
  closed = output<void>();

  protected readonly statuses = CONSUMABLE_STATUSES;

  private openGen = openGeneration(this.state);
  private seedKey = computed(() => `${this.consumable()?.id ?? ''}|${this.state() === 'open'}|${this.openGen()}`);

  editName = seededLinkedSignal(this.seedKey, () => this.openConsumable()?.name ?? '');
  editUnit = seededLinkedSignal(this.seedKey, () => this.openConsumable()?.unit ?? '');
  editThreshold = seededLinkedSignal(this.seedKey, () => {
    const t = this.openConsumable()?.restockThreshold;
    return t != null ? String(t) : '';
  });
  editStatus = seededLinkedSignal<string, ItemStatus>(this.seedKey, () => this.openConsumable()?.status ?? 'Active');
  editBrand = seededLinkedSignal<string, BrandResponse | null>(this.seedKey, () => {
    const c = this.openConsumable();
    return c?.brandId ? { id: c.brandId, name: c.brandName ?? '' } : null;
  });
  editTags = seededLinkedSignal(this.seedKey, () => this.openConsumable()?.tags?.map((t) => t.path) ?? []);
  editNotes = seededLinkedSignal(this.seedKey, () => this.openConsumable()?.notes ?? '');
  saving = seededLinkedSignal(this.seedKey, () => false);

  /** path → effective colour, feeding the tag-input so seeded chips render coloured before any search. */
  protected tagColorMap = computed(() => {
    const map: Record<string, string> = {};
    for (const t of this.openConsumable()?.tags ?? []) {
      const c = t.effectiveColor ?? t.color;
      if (c) map[t.path] = c;
    }
    return map;
  });

  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  private openConsumable(): ConsumableResponse | null {
    return this.state() === 'open' ? this.consumable() : null;
  }

  private normalizedThreshold = computed<number | null | typeof NaN>(() => {
    const raw = this.editThreshold().trim();
    if (raw === '') return null;
    const num = Number(raw);
    return Number.isFinite(num) ? num : NaN;
  });

  validationError = computed<string | null>(() => {
    if (!this.editName().trim()) return 'Name is required.';
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
    const s = this.consumable();
    if (!s) return [];
    const ops: JsonPatchDocument = [];

    const name = this.editName().trim();
    if (name !== s.name) ops.push(replace('/name', name));

    // A cleared unit sends "" — the backend normalizes it to NULL. Compare against the stored value
    // as null-or-string so a blank field over a null unit doesn't emit a spurious no-op patch.
    const unit = this.editUnit().trim();
    if ((unit || null) !== (s.unit ?? null)) ops.push(replace('/unit', unit));

    const threshold = this.normalizedThreshold();
    if (!Number.isNaN(threshold) && threshold !== (s.restockThreshold ?? null)) {
      ops.push(replace('/restockThreshold', threshold));
    }

    const status = this.editStatus();
    if (status !== s.status) ops.push(replace('/status', status));

    const brandId = this.editBrand()?.id ?? null;
    if (brandId !== (s.brandId ?? null)) ops.push(replace('/brandId', brandId));

    // /tags carries the whole desired path list (whole-set replace); only send it when the set
    // actually changed vs the seed, comparing order-independently.
    const origTags = (s.tags?.map((t) => t.path) ?? []).slice().sort();
    const currTags = this.editTags().slice().sort();
    if (origTags.length !== currTags.length || origTags.some((t, i) => t !== currTags[i])) {
      ops.push(replace('/tags', this.editTags()));
    }

    const notes = this.editNotes().trim() ? this.editNotes().trim() : null;
    if (notes !== (s.notes ?? null)) ops.push(replace('/notes', notes));

    return ops;
  }

  handleSave(): void {
    const s = this.consumable();
    if (!s || !this.canSave()) return;
    const ops = this.buildOps();
    if (ops.length === 0) return;

    this.saving.set(true);
    this.consumableApi.patchConsumable({ id: s.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Consumable updated.');
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
