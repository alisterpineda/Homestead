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
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { CurrencyPickerComponent } from '@/modules/finances/shared/currency-picker/currency-picker.component';
import { commodityResource } from '@/modules/finances/shared/commodity-resource';
import { LocationPickerComponent, type ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { BrandPickerComponent } from '../../shared/brand-picker/brand-picker.component';
import { TagInputComponent } from '../../tags/tag-input/tag-input.component';
import { ALL_ITEM_STATUSES, itemStatusLabel, isTerminalStatus } from '../../shared/item-status-meta';
import { tomorrowIso } from '../../shared/date';
import { replace } from '@/shared/json-patch';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { BrandPickerValue } from '../../shared/brand-picker/brand-picker.component';
import type { DurableKind } from '@/api/generated/models/durable-kind';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { openGeneration } from '@/shared/utils/open-generation';
import { trimToNull } from '@/modules/finances/shared/trim-to-null';

/** An amount + currency normalised both-or-neither: '' clears the pair; unparseable input → NaN. */
function parseMoneyPair(raw: string, currencyId: string | null): { amount: number | null; currencyId: string | null } {
  const trimmed = raw.trim();
  if (trimmed === '') return { amount: null, currencyId: null };
  const num = Number(trimmed);
  return { amount: Number.isFinite(num) ? num : NaN, currencyId };
}

/** The three checks every money pair shares, worded per field ("Value" / "Purchase price"). */
function moneyPairError(
  pair: { amount: number | null; currencyId: string | null },
  label: string,
  phrase: string,
): string | null {
  if (pair.amount === null) return null;
  if (Number.isNaN(pair.amount)) return `${label} must be a number.`;
  if (pair.amount < 0) return `${label} must be zero or positive.`;
  if (!pair.currencyId) return `Pick a currency for the ${phrase}.`;
  return null;
}

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
    CurrencyPickerComponent,
    LocationPickerComponent,
    BrandPickerComponent,
    TagInputComponent,
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
              <label hlmLabel>Brand</label>
              <app-brand-picker
                [value]="editBrand()"
                (valueChange)="editBrand.set($event)"
                ariaLabel="Brand"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="durable-model">Model</label>
              <input
                hlmInput
                id="durable-model"
                class="w-full"
                maxlength="128"
                placeholder="(none)"
                [value]="editModel()"
                (input)="editModel.set(asValue($event))"
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
              <label hlmLabel>Tags</label>
              <app-tag-input
                [value]="editTags()"
                [initialColorMap]="tagColorMap()"
                (valueChange)="editTags.set($event)"
              />
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
                <label hlmLabel for="durable-compartment">Compartment / slot</label>
                <input
                  hlmInput
                  id="durable-compartment"
                  class="w-full"
                  maxlength="256"
                  placeholder="e.g. Top drawer, CPU"
                  [value]="editCompartment()"
                  (input)="editCompartment.set(asValue($event))"
                />
              </div>
            }

            <div class="space-y-2">
              <label hlmLabel>Type</label>
              <hlm-select [ngModel]="editDurableKind()" (ngModelChange)="editDurableKind.set($event)" [itemToString]="durableKindToString">
                <hlm-select-trigger class="w-full" aria-label="Durable type">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content *hlmSelectPortal>
                  <hlm-select-group>
                    <hlm-select-item value="Plain">Plain item</hlm-select-item>
                    <hlm-select-item value="Container">Container</hlm-select-item>
                    <hlm-select-item value="Assembly">Assembly</hlm-select-item>
                  </hlm-select-group>
                </hlm-select-content>
              </hlm-select>
              <p class="text-xs text-muted-foreground">
                Empty the item (move out its contents) before changing its type.
              </p>
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

            <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">Acquisition</p>

            <div class="space-y-2">
              <label hlmLabel>How acquired</label>
              <hlm-select
                [ngModel]="editAcquisitionKind()"
                (ngModelChange)="editAcquisitionKind.set($event)"
                [itemToString]="acquisitionKindToString"
              >
                <hlm-select-trigger class="w-full" aria-label="How acquired">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content *hlmSelectPortal>
                  <hlm-select-group>
                    @for (k of acquisitionKinds; track k) {
                      <hlm-select-item [value]="k">{{ acquisitionKindToString(k) }}</hlm-select-item>
                    }
                  </hlm-select-group>
                </hlm-select-content>
              </hlm-select>
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
              <div class="min-w-0 space-y-2">
                <label hlmLabel for="durable-acquisition-cost">Purchase price</label>
                <input
                  hlmInput
                  id="durable-acquisition-cost"
                  type="number"
                  step="0.01"
                  min="0"
                  class="w-full"
                  placeholder="0.00"
                  [value]="editAcquisitionCost()"
                  (input)="editAcquisitionCost.set(asNumberValue($event))"
                />
              </div>
              <div class="min-w-0 space-y-2">
                <label hlmLabel>Currency</label>
                <app-currency-picker
                  [value]="editAcquisitionCurrencyId()"
                  (valueChange)="editAcquisitionCurrencyId.set($event)"
                  (commodityChange)="pickedAcquisitionCurrency.set($event)"
                  [selectedCommodity]="selectedAcquisitionCurrency()"
                  [disabled]="!editAcquisitionCost().trim()"
                  placeholder="Select currency"
                  ariaLabel="Purchase price currency"
                />
              </div>
            </div>

            <div class="space-y-2">
              <label hlmLabel for="durable-acquired-from">Acquired from</label>
              <input
                hlmInput
                id="durable-acquired-from"
                class="w-full"
                maxlength="256"
                placeholder="e.g. Home Depot, Mom"
                [value]="editAcquiredFrom()"
                (input)="editAcquiredFrom.set(asValue($event))"
              />
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div class="min-w-0 space-y-2">
                <label hlmLabel for="durable-value">Estimated value</label>
                <input
                  hlmInput
                  id="durable-value"
                  type="number"
                  step="0.01"
                  min="0"
                  class="w-full"
                  placeholder="0.00"
                  [value]="editValue()"
                  (input)="editValue.set(asNumberValue($event))"
                />
              </div>
              <div class="min-w-0 space-y-2">
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
  // '' is the "Not recorded" sentinel — hlm-select items can't carry null.
  protected readonly acquisitionKinds = ['', 'Purchased', 'Gift', 'Inherited', 'Other'];
  protected readonly maxAcquiredDate = tomorrowIso();

  private openGen = openGeneration(this.state);
  private seedKey = computed(() => `${this.durable()?.id ?? ''}|${this.state() === 'open'}|${this.openGen()}`);

  editName = seededLinkedSignal(this.seedKey, () => this.openDurable()?.name ?? '');
  editSerial = seededLinkedSignal(this.seedKey, () => this.openDurable()?.serial ?? '');
  editModel = seededLinkedSignal(this.seedKey, () => this.openDurable()?.model ?? '');
  editBrand = seededLinkedSignal<string, BrandPickerValue | null>(this.seedKey, () => {
    const d = this.openDurable();
    return d?.brandId ? { id: d.brandId, name: d.brandName ?? '' } : null;
  });
  editTags = seededLinkedSignal(this.seedKey, () => this.openDurable()?.tags?.map((t) => t.path) ?? []);
  editAcquiredOn = seededLinkedSignal(this.seedKey, () => this.openDurable()?.acquiredOn ?? '');
  // '' is the "Not recorded" sentinel — hlm-select items can't carry null.
  editAcquisitionKind = seededLinkedSignal<string, string>(
    this.seedKey, () => this.openDurable()?.acquisitionKind ?? '');
  editAcquisitionCost = seededLinkedSignal(this.seedKey, () => {
    const c = this.openDurable()?.acquisitionCost;
    return c != null ? String(c) : '';
  });
  editAcquisitionCurrencyId = seededLinkedSignal<string, string | null>(
    this.seedKey, () => this.openDurable()?.acquisitionCostCurrencyId ?? null);
  editAcquiredFrom = seededLinkedSignal(this.seedKey, () => this.openDurable()?.acquiredFrom ?? '');

  /** path → effective colour, feeding the tag-input so seeded chips render coloured before any search. */
  protected tagColorMap = computed(() => {
    const map: Record<string, string> = {};
    for (const t of this.openDurable()?.tags ?? []) {
      const c = t.effectiveColor ?? t.color;
      if (c) map[t.path] = c;
    }
    return map;
  });
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
  editDurableKind = seededLinkedSignal<string, DurableKind>(
    this.seedKey, () => this.openDurable()?.durableKind ?? 'Plain');
  editStatus = seededLinkedSignal<string, ItemStatus>(
    this.seedKey, () => this.openDurable()?.status ?? 'Active');
  editLentTo = seededLinkedSignal(this.seedKey, () => this.openDurable()?.lentTo ?? '');
  editNotes = seededLinkedSignal(this.seedKey, () => this.openDurable()?.notes ?? '');
  pickedCurrency = seededLinkedSignal<string, CommodityResponse | null>(this.seedKey, () => null);
  pickedAcquisitionCurrency = seededLinkedSignal<string, CommodityResponse | null>(this.seedKey, () => null);
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

  private acquisitionCurrency = commodityResource(
    this.commodityApi,
    () => this.durable()?.acquisitionCostCurrencyId,
    () => this.state() === 'open',
  );

  protected selectedAcquisitionCurrency = computed(
    () => this.pickedAcquisitionCurrency() ?? this.acquisitionCurrency.value());

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
  private normalizedValue = computed(() => parseMoneyPair(this.editValue(), this.editCurrencyId()));

  /** Purchase price/currency normalised as a both-or-neither pair, mirroring the value pair. */
  private normalizedAcquisitionCost = computed(() =>
    parseMoneyPair(this.editAcquisitionCost(), this.editAcquisitionCurrencyId()));

  validationError = computed<string | null>(() => {
    if (!this.editName().trim()) return 'Name is required.';
    const valueError = moneyPairError(this.normalizedValue(), 'Value', 'value');
    if (valueError) return valueError;
    const costError = moneyPairError(this.normalizedAcquisitionCost(), 'Purchase price', 'purchase price');
    if (costError) return costError;
    if (this.editStatus() === 'Lent' && !this.editLentTo().trim()) return 'Lent to is required when the status is Lent.';
    return null;
  });

  hasChanges = computed(() => this.buildOps().length > 0);

  canSave = computed(() => this.validationError() === null && this.hasChanges());

  statusToString = (status: ItemStatus): string => itemStatusLabel(status);

  durableKindToString = (kind: DurableKind): string =>
    kind === 'Plain' ? 'Plain item' : kind;

  acquisitionKindToString = (kind: string): string => (kind === '' ? 'Not recorded' : kind);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  /**
   * For number inputs: the browser reports unparseable typing (badInput) as value '', which is
   * indistinguishable from a deliberately cleared field and would silently wipe a stored
   * amount+currency pair on save. Surface it as 'NaN' so validation blocks the save instead.
   */
  asNumberValue(event: Event): string {
    const el = event.target as HTMLInputElement;
    return el.validity?.badInput ? 'NaN' : el.value;
  }

  private buildOps(): JsonPatchDocument {
    const it = this.durable();
    if (!it) return [];
    const ops: JsonPatchDocument = [];

    const name = this.editName().trim();
    if (name !== it.name) ops.push(replace('/name', name));

    const serial = trimToNull(this.editSerial());
    if (serial !== (it.serial ?? null)) ops.push(replace('/serial', serial));

    const model = trimToNull(this.editModel());
    if (model !== (it.model ?? null)) ops.push(replace('/model', model));

    const brandId = this.editBrand()?.id ?? null;
    if (brandId !== (it.brandId ?? null)) ops.push(replace('/brandId', brandId));

    // /tags carries the whole desired path list (whole-set replace); only send it when the set
    // actually changed vs the seed, comparing order-independently.
    const origTags = (it.tags?.map((t) => t.path) ?? []).slice().sort();
    const currTags = this.editTags().slice().sort();
    if (origTags.length !== currTags.length || origTags.some((t, i) => t !== currTags[i])) {
      ops.push(replace('/tags', this.editTags()));
    }

    const acquired = this.editAcquiredOn() ? this.editAcquiredOn() : null;
    if (acquired !== (it.acquiredOn ?? null)) ops.push(replace('/acquiredOn', acquired));

    const acquisitionKind = this.editAcquisitionKind() || null;
    if (acquisitionKind !== (it.acquisitionKind ?? null)) ops.push(replace('/acquisitionKind', acquisitionKind));

    const acquiredFrom = trimToNull(this.editAcquiredFrom());
    if (acquiredFrom !== (it.acquiredFrom ?? null)) ops.push(replace('/acquiredFrom', acquiredFrom));

    // Purchase price + currency move as a pair so the server's both-or-neither rule never trips.
    const { amount: cost, currencyId: acquisitionCurrencyId } = this.normalizedAcquisitionCost();
    if (cost !== (it.acquisitionCost ?? null) || acquisitionCurrencyId !== (it.acquisitionCostCurrencyId ?? null)) {
      ops.push(replace('/acquisitionCost', cost));
      ops.push(replace('/acquisitionCostCurrencyId', acquisitionCurrencyId));
    }

    const notes = trimToNull(this.editNotes());
    if (notes !== (it.notes ?? null)) ops.push(replace('/notes', notes));

    // Value + currency move as a pair so the server's both-or-neither rule never trips.
    const { amount: value, currencyId } = this.normalizedValue();
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

    // Compare against the same default the field is seeded with, so an item with a null/absent kind
    // doesn't emit a spurious /durableKind op when the user never touched the Type select.
    if (this.editDurableKind() !== (it.durableKind ?? 'Plain')) ops.push(replace('/durableKind', this.editDurableKind()));

    // Compartment only travels while in a container; the server auto-clears it on unplace.
    if (parentItemId !== null) {
      const compartment = trimToNull(this.editCompartment());
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
