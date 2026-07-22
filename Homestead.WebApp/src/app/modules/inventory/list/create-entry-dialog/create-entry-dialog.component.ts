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
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import type { BrandPickerValue } from '../../shared/brand-picker/brand-picker.component';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { LocationPickerComponent, type ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { BrandPickerComponent } from '../../shared/brand-picker/brand-picker.component';
import { TagInputComponent } from '../../tags/tag-input/tag-input.component';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

type EntryKind = 'durable' | 'consumable';

/**
 * Unified "New" dialog for the merged inventory list: a kind toggle swaps between the durable fields
 * (model, location) and the consumable fields (unit, restock threshold). The two field sets and
 * the two descriptions are grid-stacked into a shared cell, so the dialog reserves max(durable, consumable)
 * height and never reflows on toggle; the inactive variant is `invisible` + `inert`. Title and submit stay
 * kind-neutral — the toggle group is the kind selector. The rest is set later from the Edit sheet.
 */
@Component({
  selector: 'app-create-entry-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmToggleGroupImports,
    HlmInput,
    HlmLabel,
    LocationPickerComponent,
    BrandPickerComponent,
    TagInputComponent,
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
          <h2 hlmDialogTitle>New item</h2>
          <p hlmDialogDescription class="grid grid-cols-1">
            <span class="col-start-1 row-start-1" [class.invisible]="kind() !== 'durable'">
              Add a tracked durable — one specific thing. Set serial, value, and more after creating.
            </span>
            <span class="col-start-1 row-start-1" [class.invisible]="kind() !== 'consumable'">
              @if (placedInitialLocation()) {
                Define a consumable type, counted in bulk. Optionally give it a starting quantity, or
                add stock later.
              } @else {
                Define a consumable type, counted in bulk. Add stock to it afterwards.
              }
            </span>
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <hlm-toggle-group
            type="single"
            nullable="false"
            variant="outline"
            [value]="kind()"
            (valueChange)="onKindChange($event)"
            class="w-full"
            aria-label="What are you adding?"
          >
            <button hlmToggleGroupItem value="durable" class="flex-1">Durable</button>
            <button hlmToggleGroupItem value="consumable" class="flex-1">Consumable</button>
          </hlm-toggle-group>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-entry-name">Name</label>
            <input
              hlmInput
              id="create-entry-name"
              maxlength="256"
              [placeholder]="kind() === 'consumable' ? 'e.g. Paper towels' : 'e.g. Cordless drill'"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <!-- Brand applies to both kinds, so it lives outside the kind-specific grid. -->
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Brand (optional)</label>
            <app-brand-picker
              [value]="brand()"
              (valueChange)="brand.set($event)"
              ariaLabel="Brand"
            />
          </div>

          <!-- Tags apply to both kinds, so they live outside the kind-specific grid. -->
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Tags (optional)</label>
            <app-tag-input [value]="tags()" (valueChange)="tags.set($event)" />
          </div>

          <!-- Both field sets share one grid cell so the dialog reserves max height and never reflows
               on toggle; the inactive variant is hidden (still occupies space) and made inert. -->
          <div class="grid grid-cols-1">
            <div
              class="col-start-1 row-start-1 flex flex-col gap-4"
              [class.invisible]="kind() !== 'durable'"
              [attr.inert]="kind() === 'durable' ? null : ''"
            >
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="create-entry-model">Model (optional)</label>
                <input
                  hlmInput
                  id="create-entry-model"
                  maxlength="128"
                  placeholder="e.g. MRW23"
                  [value]="model()"
                  (input)="model.set(asValue($event))"
                />
              </div>

              <div class="flex flex-col gap-1.5">
                <label hlmLabel>Location</label>
                <app-location-picker
                  [value]="locationValue()"
                  (valueChange)="onLocationChange($event)"
                  [selectedLabel]="initialLocationLabel()"
                  ariaLabel="Location"
                />
              </div>
            </div>

            <div
              class="col-start-1 row-start-1 flex flex-col gap-4"
              [class.invisible]="kind() !== 'consumable'"
              [attr.inert]="kind() === 'consumable' ? null : ''"
            >
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="create-entry-unit">Unit</label>
                <input
                  hlmInput
                  id="create-entry-unit"
                  maxlength="32"
                  placeholder="e.g. rolls (optional)"
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

              @if (placedInitialLocation()) {
                <div class="flex flex-col gap-1.5">
                  <label hlmLabel for="create-entry-initial-quantity">Starting quantity (optional)</label>
                  <input
                    hlmInput
                    id="create-entry-initial-quantity"
                    type="number"
                    step="0.001"
                    min="0"
                    placeholder="0"
                    [value]="initialQuantity()"
                    (input)="initialQuantity.set(asValue($event))"
                  />
                  <p class="text-sm text-muted-foreground">
                    at {{ initialLocationLabel() ?? 'this location' }}. Leave blank to add stock later.
                  </p>
                </div>
              }
            </div>
          </div>

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
            <button hlmBtn variant="secondary" type="button" class="w-full sm:w-auto" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" class="w-full sm:w-auto" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateEntryDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  /** Pre-selected durable location on open ("Add here" from a drilled place); null = unplaced. */
  initialLocation = input<ItemLocationValue | null>(null);
  /** Display name for the pre-selected location, shown before the picker's lazy lists load. */
  initialLocationLabel = input<string | null>(null);
  created = output();

  kind = signal<EntryKind>('durable');
  name = signal('');
  notes = signal('');
  // Brand applies to both kinds.
  brand = signal<BrandPickerValue | null>(null);
  // Tags apply to both kinds (the desired path list; the backend upserts ancestors).
  tags = signal<string[]>([]);
  // Durable fields
  areaId = signal<string | null>(null);
  parentItemId = signal<string | null>(null);
  model = signal('');
  // Consumable fields
  unit = signal('');
  threshold = signal('');
  /** Optional starting quantity to seed at the drilled place; only shown for a real pre-filled place. */
  initialQuantity = signal('');

  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private durableApi = inject(InventoryDurableApiService);
  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);

  /**
   * The drilled location only when it's a real place (area or container). A bare consumable can't be
   * given starting stock from the Items page / Browse root / Unplaced, so the quantity field hides there.
   */
  placedInitialLocation = computed(() => {
    const loc = this.initialLocation();
    return loc && loc.kind !== 'unplaced' ? loc : null;
  });

  canSubmit = computed(() => {
    if (this.name().trim().length === 0) return false;
    if (this.kind() === 'durable') return true;
    const t = this.threshold().trim();
    if (t !== '' && !(Number.isFinite(Number(t)) && Number(t) >= 0)) return false;
    const q = this.initialQuantity().trim();
    return q === '' || (Number.isFinite(Number(q)) && Number(q) >= 0);
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

  /** The toggle-group emits `unknown` — narrow it. */
  onKindChange(value: unknown): void {
    if (value === 'durable' || value === 'consumable') this.kind.set(value);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.kind.set('durable');
      this.name.set('');
      this.notes.set('');
      this.brand.set(null);
      this.tags.set([]);
      this.onLocationChange(this.initialLocation() ?? { kind: 'unplaced' });
      this.model.set('');
      this.unit.set('');
      this.threshold.set('');
      this.initialQuantity.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    if (this.kind() === 'consumable') {
      const t = this.threshold().trim();
      // Only seed stock when opened from a real place AND a positive quantity was entered; otherwise
      // create a bare consumable (empty/0 quantity is a deliberate "stock it later" choice).
      const loc = this.placedInitialLocation();
      const q = this.initialQuantity().trim();
      const qty = q === '' ? null : Number(q);
      const placeStock = loc !== null && qty !== null && qty > 0;
      const successMessage = placeStock
        ? `Consumable created with ${qty} at ${this.initialLocationLabel() ?? 'this location'}.`
        : 'Consumable created.';
      this.consumableApi.createConsumable({
        body: {
          name: this.name().trim(),
          unit: this.unit().trim() || undefined,
          restockThreshold: t === '' ? null : Number(t),
          brandId: this.brand()?.id ?? null,
          tags: this.tags(),
          notes: this.notes().trim() ? this.notes().trim() : null,
          initialQuantity: placeStock ? qty : null,
          initialAreaId: placeStock && loc!.kind === 'area' ? loc!.id : null,
          initialContainerItemId: placeStock && loc!.kind === 'container' ? loc!.id : null,
        },
      }).subscribe({
        next: () => this.onCreated(successMessage),
        error: (err: unknown) => this.onCreateError(err, 'Failed to create consumable.'),
      });
      return;
    }

    this.durableApi.createDurable({
      body: {
        name: this.name().trim(),
        areaId: this.areaId(),
        parentItemId: this.parentItemId(),
        brandId: this.brand()?.id ?? null,
        model: this.model().trim() ? this.model().trim() : null,
        tags: this.tags(),
        notes: this.notes().trim() ? this.notes().trim() : null,
      },
    }).subscribe({
      next: () => this.onCreated('Durable created.'),
      error: (err: unknown) => this.onCreateError(err, 'Failed to create durable.'),
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
