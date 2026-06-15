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
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { CurrencyPickerComponent } from '@/modules/finances/shared/currency-picker/currency-picker.component';
import { commodityResource } from '@/modules/finances/shared/commodity-resource';
import { LocationPickerComponent, type ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { ALL_ITEM_STATUSES, itemStatusLabel, isTerminalStatus } from '../../shared/item-status-meta';
import { tomorrowIso } from '../../shared/date';
import { replace } from '@/shared/json-patch';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';

@Component({
  selector: 'app-edit-durable-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmSelectImports,
    HlmCheckboxImports,
    CurrencyPickerComponent,
    LocationPickerComponent,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit durable</h3>
          <p hlmSheetDescription>Update details, value, location, and status.</p>
        </hlm-sheet-header>

        @if (durable(); as it) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="durable-name">Name</label>
              <input
                hlmInput
                id="durable-name"
                class="w-full"
                maxlength="256"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="durable-serial">Serial</label>
              <input
                hlmInput
                id="durable-serial"
                class="w-full"
                maxlength="128"
                placeholder="(none)"
                [value]="editSerial()"
                (input)="editSerial.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="durable-acquired">Acquired on</label>
              <input
                hlmInput
                id="durable-acquired"
                type="date"
                class="w-full"
                [max]="maxAcquiredDate"
                [value]="editAcquiredOn()"
                (input)="editAcquiredOn.set(asValue($event))"
              />
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div class="space-y-2">
                <label hlmLabel for="durable-value">Value</label>
                <input
                  hlmInput
                  id="durable-value"
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
              <app-location-picker
                [value]="locationValue()"
                (valueChange)="onLocationChange($event)"
                [selectedLabel]="selectedLocationLabel()"
                [excludeItemId]="it.id"
                ariaLabel="Location"
              />
              @if (terminalLocationHint()) {
                <p class="text-xs text-muted-foreground">
                  Location changes are ignored when marking a durable Sold/Discarded/Archived.
                </p>
              }
            </div>

            @if (editParentItemId() !== null) {
              <div class="space-y-2">
                <label hlmLabel for="durable-compartment">Compartment</label>
                <input
                  hlmInput
                  id="durable-compartment"
                  class="w-full"
                  maxlength="256"
                  placeholder="e.g. Top drawer"
                  [value]="editCompartment()"
                  (input)="editCompartment.set(asValue($event))"
                />
              </div>
            }

            <label class="flex items-center gap-2 text-sm cursor-pointer" hlmLabel>
              <hlm-checkbox
                [checked]="editIsContainer()"
                (checkedChange)="editIsContainer.set($event === true)"
                [aria-label]="'This durable is a container'"
              />
              <span>This durable is a container</span>
            </label>

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
                <label hlmLabel for="durable-lent-to">Lent to</label>
                <input
                  hlmInput
                  id="durable-lent-to"
                  class="w-full"
                  maxlength="256"
                  placeholder="e.g. Dave next door"
                  [value]="editLentTo()"
                  (input)="editLentTo.set(asValue($event))"
                />
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel for="durable-notes">Notes</label>
              <textarea
                hlmInput
                id="durable-notes"
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
export class EditDurableSheetComponent {
  durable = input<DurableResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<DurableResponse>();
  closed = output<void>();

  protected readonly statuses = ALL_ITEM_STATUSES;
  protected readonly maxAcquiredDate = tomorrowIso();

  private seedKey = computed(() => `${this.durable()?.id ?? ''}|${this.state() === 'open'}`);

  editName = seededLinkedSignal(this.seedKey, () => this.openDurable()?.name ?? '');
  editSerial = seededLinkedSignal(this.seedKey, () => this.openDurable()?.serial ?? '');
  editAcquiredOn = seededLinkedSignal(this.seedKey, () => this.openDurable()?.acquiredOn ?? '');
  editValue = seededLinkedSignal(this.seedKey, () => {
    const v = this.openDurable()?.value;
    return v != null ? String(v) : '';
  });
  editCurrencyId = seededLinkedSignal<string, string | null>(
    this.seedKey, () => this.openDurable()?.valueCurrencyId ?? null);
  editAreaId = seededLinkedSignal<string, string | null>(
    this.seedKey, () => this.openDurable()?.areaId ?? null);
  editParentItemId = seededLinkedSignal<string, string | null>(
    this.seedKey, () => this.openDurable()?.parentItemId ?? null);
  editCompartment = seededLinkedSignal(this.seedKey, () => this.openDurable()?.compartment ?? '');
  editIsContainer = seededLinkedSignal(this.seedKey, () => this.openDurable()?.isContainer ?? false);
  editStatus = seededLinkedSignal<string, ItemStatus>(
    this.seedKey, () => this.openDurable()?.status ?? 'Active');
  editLentTo = seededLinkedSignal(this.seedKey, () => this.openDurable()?.lentTo ?? '');
  editNotes = seededLinkedSignal(this.seedKey, () => this.openDurable()?.notes ?? '');
  pickedCurrency = seededLinkedSignal<string, CommodityResponse | null>(this.seedKey, () => null);
  saving = seededLinkedSignal(this.seedKey, () => false);

  private durableApi = inject(InventoryDurableApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  private valueCurrency = commodityResource(
    this.commodityApi,
    () => this.durable()?.valueCurrencyId,
    () => this.state() === 'open',
  );

  protected selectedCurrency = computed(() => this.pickedCurrency() ?? this.valueCurrency.value());

  private openDurable(): DurableResponse | null {
    return this.state() === 'open' ? this.durable() : null;
  }

  /** The location pair as a single discriminated value for the picker. */
  protected locationValue = computed<ItemLocationValue>(() => {
    const parentId = this.editParentItemId();
    if (parentId) return { kind: 'container', id: parentId };
    const areaId = this.editAreaId();
    if (areaId) return { kind: 'area', id: areaId };
    return { kind: 'unplaced' };
  });

  /** Known label of the current location, shown before the picker's lists load. */
  protected selectedLocationLabel = computed<string | null>(() => {
    const it = this.durable();
    if (!it) return null;
    if (this.editParentItemId() && this.editParentItemId() === (it.parentItemId ?? null)) {
      return it.path?.[it.path.length - 1]?.name ?? null;
    }
    if (this.editAreaId() && this.editAreaId() === (it.areaId ?? null)) {
      return it.areaName ?? null;
    }
    return null;
  });

  // A non-blocking warning: the server keeps the location on a live→terminal transition, so any
  // pending move/compartment edit in the same save is silently dropped.
  protected terminalLocationHint = computed<boolean>(() => {
    const it = this.durable();
    if (!it || isTerminalStatus(it.status) || !isTerminalStatus(this.editStatus())) return false;
    const areaMoved = this.editAreaId() !== (it.areaId ?? null);
    const parentMoved = this.editParentItemId() !== (it.parentItemId ?? null);
    const compartmentChanged =
      this.editParentItemId() !== null &&
      (this.editCompartment().trim() || null) !== (it.compartment ?? null);
    return areaMoved || parentMoved || compartmentChanged;
  });

  /** Apply a picked location to the area/parent pair atomically (they are mutually exclusive). */
  protected onLocationChange(value: ItemLocationValue): void {
    switch (value.kind) {
      case 'unplaced':
        this.editAreaId.set(null);
        this.editParentItemId.set(null);
        break;
      case 'area':
        this.editAreaId.set(value.id);
        this.editParentItemId.set(null);
        break;
      case 'container':
        this.editParentItemId.set(value.id);
        this.editAreaId.set(null);
        break;
    }
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
    const it = this.durable();
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

    // Area + parent are a mutually-exclusive pair; a cross-kind move naturally emits both ops, so
    // the server's at-most-one rule never trips.
    const areaId = this.editAreaId();
    if (areaId !== (it.areaId ?? null)) ops.push(replace('/areaId', areaId));

    const parentItemId = this.editParentItemId();
    if (parentItemId !== (it.parentItemId ?? null)) ops.push(replace('/parentItemId', parentItemId));

    if (this.editIsContainer() !== it.isContainer) ops.push(replace('/isContainer', this.editIsContainer()));

    // Compartment only travels while in a container; the server auto-clears it on unplace.
    if (parentItemId !== null) {
      const compartment = this.editCompartment().trim() ? this.editCompartment().trim() : null;
      if (compartment !== (it.compartment ?? null)) ops.push(replace('/compartment', compartment));
    }

    return ops;
  }

  handleSave(): void {
    const it = this.durable();
    if (!it || !this.canSave()) return;
    const ops = this.buildOps();
    if (ops.length === 0) return;

    this.saving.set(true);
    this.durableApi.patchDurable({ id: it.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Durable updated.');
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
