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
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { CurrencyPickerComponent } from '@/modules/finances/shared/currency-picker/currency-picker.component';
import { commodityResource } from '@/modules/finances/shared/commodity-resource';
import { AreaPickerComponent } from '../../shared/area-picker/area-picker.component';
import { ALL_ITEM_STATUSES, itemStatusLabel } from '../../shared/item-status-meta';
import { tomorrowIso } from '../../shared/date';
import { replace } from '@/shared/json-patch';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';

@Component({
  selector: 'app-edit-item-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmSelectImports,
    CurrencyPickerComponent,
    AreaPickerComponent,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit item</h3>
          <p hlmSheetDescription>Update details, value, location, and status.</p>
        </hlm-sheet-header>

        @if (item(); as it) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="item-name">Name</label>
              <input
                hlmInput
                id="item-name"
                class="w-full"
                maxlength="256"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="item-serial">Serial</label>
              <input
                hlmInput
                id="item-serial"
                class="w-full"
                maxlength="128"
                placeholder="(none)"
                [value]="editSerial()"
                (input)="editSerial.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="item-acquired">Acquired on</label>
              <input
                hlmInput
                id="item-acquired"
                type="date"
                class="w-full"
                [max]="maxAcquiredDate"
                [value]="editAcquiredOn()"
                (input)="editAcquiredOn.set(asValue($event))"
              />
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div class="space-y-2">
                <label hlmLabel for="item-value">Value</label>
                <input
                  hlmInput
                  id="item-value"
                  type="number"
                  step="0.01"
                  min="0"
                  class="w-full"
                  placeholder="0.00"
                  [value]="editValue()"
                  (input)="editValue.set(asValue($event))"
                />
              </div>
              <div class="space-y-2">
                <label hlmLabel>Currency</label>
                <app-currency-picker
                  [value]="editCurrencyId()"
                  (valueChange)="editCurrencyId.set($event)"
                  (commodityChange)="pickedCurrency.set($event)"
                  [selectedCommodity]="selectedCurrency()"
                  [disabled]="!editValue().trim()"
                  placeholder="Select currency"
                  ariaLabel="Value currency"
                />
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Location</label>
              <app-area-picker
                [value]="editAreaId()"
                (valueChange)="editAreaId.set($event)"
                [selectedAreaName]="it.areaName ?? null"
                ariaLabel="Area"
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
                    @for (s of statuses; track s) {
                      <hlm-select-item [value]="s">{{ statusToString(s) }}</hlm-select-item>
                    }
                  </hlm-select-group>
                </hlm-select-content>
              </hlm-select>
            </div>

            @if (editStatus() === 'Lent') {
              <div class="space-y-2">
                <label hlmLabel for="item-lent-to">Lent to</label>
                <input
                  hlmInput
                  id="item-lent-to"
                  class="w-full"
                  maxlength="256"
                  placeholder="e.g. Dave next door"
                  [value]="editLentTo()"
                  (input)="editLentTo.set(asValue($event))"
                />
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel for="item-notes">Notes</label>
              <textarea
                hlmInput
                id="item-notes"
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
export class EditItemSheetComponent {
  item = input<ItemResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<ItemResponse>();
  closed = output<void>();

  protected readonly statuses = ALL_ITEM_STATUSES;
  protected readonly maxAcquiredDate = tomorrowIso();

  private seedKey = computed(() => `${this.item()?.id ?? ''}|${this.state() === 'open'}`);

  editName = seededLinkedSignal(this.seedKey, () => this.openItem()?.name ?? '');
  editSerial = seededLinkedSignal(this.seedKey, () => this.openItem()?.serial ?? '');
  editAcquiredOn = seededLinkedSignal(this.seedKey, () => this.openItem()?.acquiredOn ?? '');
  editValue = seededLinkedSignal(this.seedKey, () => {
    const v = this.openItem()?.value;
    return v != null ? String(v) : '';
  });
  editCurrencyId = seededLinkedSignal<string, string | null>(
    this.seedKey, () => this.openItem()?.valueCurrencyId ?? null);
  editAreaId = seededLinkedSignal<string, string | null>(
    this.seedKey, () => this.openItem()?.areaId ?? null);
  editStatus = seededLinkedSignal<string, ItemStatus>(
    this.seedKey, () => this.openItem()?.status ?? 'Active');
  editLentTo = seededLinkedSignal(this.seedKey, () => this.openItem()?.lentTo ?? '');
  editNotes = seededLinkedSignal(this.seedKey, () => this.openItem()?.notes ?? '');
  pickedCurrency = seededLinkedSignal<string, CommodityResponse | null>(this.seedKey, () => null);
  saving = seededLinkedSignal(this.seedKey, () => false);

  private itemApi = inject(InventoryItemApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  private valueCurrency = commodityResource(
    this.commodityApi,
    () => this.item()?.valueCurrencyId,
    () => this.state() === 'open',
  );

  protected selectedCurrency = computed(() => this.pickedCurrency() ?? this.valueCurrency.value());

  private openItem(): ItemResponse | null {
    return this.state() === 'open' ? this.item() : null;
  }

  /** Value/currency normalised as a both-or-neither pair. */
  private normalizedValue = computed<{ value: number | null; currencyId: string | null }>(() => {
    const raw = this.editValue().trim();
    if (raw === '') return { value: null, currencyId: null };
    const num = Number(raw);
    return { value: Number.isFinite(num) ? num : NaN, currencyId: this.editCurrencyId() };
  });

  validationError = computed<string | null>(() => {
    if (!this.editName().trim()) return 'Name is required.';
    const nv = this.normalizedValue();
    if (nv.value !== null && Number.isNaN(nv.value)) return 'Value must be a number.';
    if (nv.value !== null && nv.value < 0) return 'Value must be zero or positive.';
    if (nv.value !== null && !nv.currencyId) return 'Pick a currency for the value.';
    if (this.editStatus() === 'Lent' && !this.editLentTo().trim()) return 'Lent to is required when the status is Lent.';
    return null;
  });

  hasChanges = computed(() => this.buildOps().length > 0);

  canSave = computed(() => this.validationError() === null && this.hasChanges());

  statusToString = (status: ItemStatus): string => itemStatusLabel(status);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  private buildOps(): JsonPatchDocument {
    const it = this.item();
    if (!it) return [];
    const ops: JsonPatchDocument = [];

    const name = this.editName().trim();
    if (name !== it.name) ops.push(replace('/name', name));

    const serial = this.editSerial().trim() ? this.editSerial().trim() : null;
    if (serial !== (it.serial ?? null)) ops.push(replace('/serial', serial));

    const acquired = this.editAcquiredOn() ? this.editAcquiredOn() : null;
    if (acquired !== (it.acquiredOn ?? null)) ops.push(replace('/acquiredOn', acquired));

    const notes = this.editNotes().trim() ? this.editNotes().trim() : null;
    if (notes !== (it.notes ?? null)) ops.push(replace('/notes', notes));

    // Value + currency move as a pair so the server's both-or-neither rule never trips.
    const { value, currencyId } = this.normalizedValue();
    const v0 = it.value ?? null;
    const c0 = it.valueCurrencyId ?? null;
    if (value !== v0 || currencyId !== c0) {
      ops.push(replace('/value', value));
      ops.push(replace('/valueCurrencyId', currencyId));
    }

    const status = this.editStatus();
    if (status !== it.status) ops.push(replace('/status', status));

    // LentTo only travels with a Lent status; the server clears it otherwise.
    if (status === 'Lent') {
      const lentTo = this.editLentTo().trim();
      if (lentTo !== (it.lentTo ?? '')) ops.push(replace('/lentTo', lentTo));
    }

    const areaId = this.editAreaId();
    if (areaId !== (it.areaId ?? null)) ops.push(replace('/areaId', areaId));

    return ops;
  }

  handleSave(): void {
    const it = this.item();
    if (!it || !this.canSave()) return;
    const ops = this.buildOps();
    if (ops.length === 0) return;

    this.saving.set(true);
    this.itemApi.patchItem({ id: it.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Item updated.');
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
