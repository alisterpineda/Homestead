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
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { LocationPickerComponent, type ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { tomorrowIso } from '../../shared/date';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

type EntryKind = 'item' | 'supply';

/**
 * Unified "New" dialog for the merged inventory list: a kind toggle swaps between the item fields
 * (location, container flag, serial, acquired on) and the supply fields (unit, restock threshold).
 */
@Component({
  selector: 'app-create-entry-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmButtonGroup,
    HlmInput,
    HlmLabel,
    HlmCheckboxImports,
    LocationPickerComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[560px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>{{ kind() === 'supply' ? 'New supply' : 'New item' }}</h2>
          <p hlmDialogDescription>
            @if (kind() === 'supply') {
              Define a consumable type. Add stock to it afterwards.
            } @else {
              Add a tracked item. You can set value and currency after creating it.
            }
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div hlmButtonGroup class="w-full" aria-label="What are you adding?">
            <button
              hlmBtn
              type="button"
              [variant]="kind() === 'item' ? 'secondary' : 'outline'"
              size="sm"
              class="flex-1"
              (click)="kind.set('item')"
            >Item — tracked individually</button>
            <button
              hlmBtn
              type="button"
              [variant]="kind() === 'supply' ? 'secondary' : 'outline'"
              size="sm"
              class="flex-1"
              (click)="kind.set('supply')"
            >Supply — counted in bulk</button>
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-entry-name">Name</label>
            <input
              hlmInput
              id="create-entry-name"
              maxlength="256"
              [placeholder]="kind() === 'supply' ? 'e.g. Paper towels' : 'e.g. Cordless drill'"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          @if (kind() === 'item') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel>Location</label>
              <app-location-picker
                [value]="locationValue()"
                (valueChange)="onLocationChange($event)"
                ariaLabel="Location"
              />
            </div>

            @if (parentItemId() !== null) {
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="create-entry-compartment">Compartment (optional)</label>
                <input
                  hlmInput
                  id="create-entry-compartment"
                  maxlength="256"
                  placeholder="e.g. Top drawer"
                  [value]="compartment()"
                  (input)="compartment.set(asValue($event))"
                />
              </div>
            }

            <label class="flex items-center gap-2 text-sm cursor-pointer" hlmLabel>
              <hlm-checkbox
                [checked]="isContainer()"
                (checkedChange)="isContainer.set($event === true)"
                [aria-label]="'This item is a container'"
              />
              <span>This item is a container</span>
            </label>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-entry-serial">Serial (optional)</label>
              <input
                hlmInput
                id="create-entry-serial"
                maxlength="128"
                placeholder="e.g. SN-12345"
                [value]="serial()"
                (input)="serial.set(asValue($event))"
              />
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-entry-acquired">Acquired on (optional)</label>
              <input
                hlmInput
                id="create-entry-acquired"
                type="date"
                [max]="maxAcquiredDate"
                [value]="acquiredOn()"
                (input)="acquiredOn.set(asValue($event))"
              />
            </div>
          } @else {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-entry-unit">Unit</label>
              <input
                hlmInput
                id="create-entry-unit"
                maxlength="32"
                placeholder="e.g. rolls"
                [value]="unit()"
                (input)="unit.set(asValue($event))"
              />
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-entry-threshold">Restock threshold (optional)</label>
              <input
                hlmInput
                id="create-entry-threshold"
                type="number"
                step="0.001"
                min="0"
                placeholder="Flag as low at or below this total"
                [value]="threshold()"
                (input)="threshold.set(asValue($event))"
              />
            </div>
          }

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-entry-notes">Notes (optional)</label>
            <textarea
              hlmInput
              id="create-entry-notes"
              rows="2"
              placeholder="Anything worth remembering"
              [value]="notes()"
              (input)="notes.set(asValue($event))"
            ></textarea>
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : (kind() === 'supply' ? 'Create supply' : 'Create item') }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateEntryDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  kind = signal<EntryKind>('item');
  name = signal('');
  notes = signal('');
  // Item fields
  areaId = signal<string | null>(null);
  parentItemId = signal<string | null>(null);
  isContainer = signal(false);
  compartment = signal('');
  serial = signal('');
  acquiredOn = signal('');
  // Supply fields
  unit = signal('');
  threshold = signal('');

  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private itemApi = inject(InventoryItemApiService);
  private supplyApi = inject(InventorySupplyApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly maxAcquiredDate = tomorrowIso();

  canSubmit = computed(() => {
    if (this.name().trim().length === 0) return false;
    if (this.kind() === 'item') return true;
    if (!this.unit().trim()) return false;
    const t = this.threshold().trim();
    return t === '' || (Number.isFinite(Number(t)) && Number(t) >= 0);
  });

  /** The location pair as a single discriminated value for the picker. */
  locationValue = computed<ItemLocationValue>(() => {
    const parentId = this.parentItemId();
    if (parentId) return { kind: 'container', id: parentId };
    const areaId = this.areaId();
    if (areaId) return { kind: 'area', id: areaId };
    return { kind: 'unplaced' };
  });

  onLocationChange(value: ItemLocationValue): void {
    switch (value.kind) {
      case 'unplaced':
        this.areaId.set(null);
        this.parentItemId.set(null);
        break;
      case 'area':
        this.areaId.set(value.id);
        this.parentItemId.set(null);
        break;
      case 'container':
        this.parentItemId.set(value.id);
        this.areaId.set(null);
        break;
    }
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.kind.set('item');
      this.name.set('');
      this.notes.set('');
      this.areaId.set(null);
      this.parentItemId.set(null);
      this.isContainer.set(false);
      this.compartment.set('');
      this.serial.set('');
      this.acquiredOn.set('');
      this.unit.set('');
      this.threshold.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    if (this.kind() === 'supply') {
      const t = this.threshold().trim();
      this.supplyApi.createSupply({
        body: {
          name: this.name().trim(),
          unit: this.unit().trim(),
          restockThreshold: t === '' ? null : Number(t),
          notes: this.notes().trim() ? this.notes().trim() : null,
        },
      }).subscribe({
        next: () => this.onCreated('Supply created.'),
        error: (err: unknown) => this.onCreateError(err, 'Failed to create supply.'),
      });
      return;
    }

    const parentItemId = this.parentItemId();
    this.itemApi.createItem({
      body: {
        name: this.name().trim(),
        areaId: this.areaId(),
        parentItemId,
        isContainer: this.isContainer(),
        // Compartment is only valid inside a container; the server rejects it otherwise.
        compartment: parentItemId && this.compartment().trim() ? this.compartment().trim() : null,
        serial: this.serial().trim() ? this.serial().trim() : null,
        acquiredOn: this.acquiredOn() ? this.acquiredOn() : null,
        notes: this.notes().trim() ? this.notes().trim() : null,
      },
    }).subscribe({
      next: () => this.onCreated('Item created.'),
      error: (err: unknown) => this.onCreateError(err, 'Failed to create item.'),
    });
  }

  private onCreated(message: string): void {
    this.creating.set(false);
    toast.success(message);
    this.dialog()?.close();
    this.created.emit();
  }

  private onCreateError(err: unknown, fallback: string): void {
    this.creating.set(false);
    this.createError.set(this.errorHandler.getErrorMessage(err, fallback));
  }
}
