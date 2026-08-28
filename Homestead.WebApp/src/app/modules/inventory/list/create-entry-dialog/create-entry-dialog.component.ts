import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  inject,
  input,
  output,
  signal,
  computed,
  viewChild,
} from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import type { BrandPickerValue } from '../../shared/brand-picker/brand-picker.component';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { LocationPickerComponent, type ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { BrandPickerComponent } from '../../shared/brand-picker/brand-picker.component';
import { TagInputComponent } from '../../tags/tag-input/tag-input.component';
import { formatQty, resolveUnit } from '../../consumables/consumable-shared';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

type EntryKind = 'durable' | 'consumable';

/**
 * Unified "New" dialog for the merged inventory list. The kind toggle is the only kind selector —
 * title, description, and submit stay kind-neutral — and the form is ordered as a fast path:
 * **Name → Location → Starting stock** (consumables only), with everything else (brand, tags, model /
 * restock threshold, notes) folded into a collapsed "More details" section. Nothing is cut; the
 * secondary fields are one click away.
 *
 * Location now applies to **both** kinds, so a consumable can be seeded with opening stock straight
 * from create. Quantity and location are both-or-neither (the backend validator requires exactly
 * that), and the dialog blocks the mismatch symmetrically via {@link stockHint} — a quantity with no
 * place, or a place with no quantity, shows an inline hint and disables submit. Unplaced + blank
 * quantity is the bare "define the type, stock it later" create.
 *
 * "Create & add another" keeps the dialog open for both kinds, retaining the kind and location while
 * clearing the item-specific fields — the natural rhythm when unpacking a box into one place.
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
    HlmIconImports,
    HlmCollapsibleImports,
    LocationPickerComponent,
    BrandPickerComponent,
    TagInputComponent,
  ],
  providers: [provideIcons({ lucideChevronRight })],
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
          <p hlmDialogDescription>
            Name it and say where it lives. Everything else can be set later.
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
              #nameInput
              id="create-entry-name"
              maxlength="256"
              [placeholder]="kind() === 'consumable' ? 'e.g. Paper towels' : 'e.g. Cordless drill'"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <!-- Location applies to both kinds: it places a durable, and holds a consumable's opening stock. -->
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Location</label>
            <app-location-picker
              [value]="locationValue()"
              (valueChange)="onLocationChange($event)"
              [selectedLabel]="initialLocationLabel()"
              ariaLabel="Location"
            />
          </div>

          @if (kind() === 'consumable') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-entry-initial-quantity">Starting stock</label>
              <div class="grid grid-cols-[1fr_7rem] gap-2">
                <input
                  hlmInput
                  id="create-entry-initial-quantity"
                  type="number"
                  inputmode="decimal"
                  step="0.001"
                  min="0"
                  placeholder="0"
                  [value]="initialQuantity()"
                  (input)="initialQuantity.set(asValue($event))"
                />
                <input
                  hlmInput
                  id="create-entry-unit"
                  aria-label="Unit"
                  maxlength="32"
                  placeholder="unit, e.g. rolls"
                  [value]="unit()"
                  (input)="unit.set(asValue($event))"
                />
              </div>
              @if (stockHint(); as hint) {
                <p class="text-sm text-destructive">{{ hint }}</p>
              } @else if (!hasPlace()) {
                <p class="text-sm text-muted-foreground">Leave blank to add stock later.</p>
              }
            </div>
          }

          <div hlmCollapsible [(expanded)]="moreOpen">
            <button
              hlmCollapsibleTrigger
              type="button"
              class="flex items-center gap-1 text-sm text-muted-foreground"
            >
              <ng-icon
                hlm
                name="lucideChevronRight"
                size="sm"
                class="shrink-0 transition-transform"
                [class.rotate-90]="moreOpen()"
              />
              More details
            </button>
            <div hlmCollapsibleContent>
              <div class="flex flex-col gap-4 pt-3">
                <div class="flex flex-col gap-1.5">
                  <label hlmLabel>Brand</label>
                  <app-brand-picker
                    [value]="brand()"
                    (valueChange)="brand.set($event)"
                    ariaLabel="Brand"
                  />
                </div>

                <div class="flex flex-col gap-1.5">
                  <label hlmLabel>Tags</label>
                  <app-tag-input [value]="tags()" (valueChange)="tags.set($event)" />
                </div>

                @if (kind() === 'durable') {
                  <div class="flex flex-col gap-1.5">
                    <label hlmLabel for="create-entry-model">Model</label>
                    <input
                      hlmInput
                      id="create-entry-model"
                      maxlength="128"
                      placeholder="e.g. MRW23"
                      [value]="model()"
                      (input)="model.set(asValue($event))"
                    />
                  </div>
                } @else {
                  <div class="flex flex-col gap-1.5">
                    <label hlmLabel for="create-entry-threshold">Restock threshold</label>
                    <input
                      hlmInput
                      id="create-entry-threshold"
                      type="number"
                      inputmode="decimal"
                      step="0.001"
                      min="0"
                      placeholder="Flag as low at or below this total"
                      [value]="threshold()"
                      (input)="threshold.set(asValue($event))"
                    />
                  </div>
                }

                <div class="flex flex-col gap-1.5">
                  <label hlmLabel for="create-entry-notes">Notes</label>
                  <textarea
                    hlmInput
                    id="create-entry-notes"
                    rows="2"
                    placeholder="Anything worth remembering"
                    [value]="notes()"
                    (input)="notes.set(asValue($event))"
                  ></textarea>
                </div>
              </div>
            </div>
          </div>

          @if (thresholdHint(); as hint) {
            <p class="text-sm text-destructive">{{ hint }}</p>
          }

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" class="w-full sm:w-auto" hlmDialogClose>Cancel</button>
            <button
              hlmBtn
              variant="outline"
              type="button"
              class="w-full sm:w-auto"
              [disabled]="!canSubmit() || creating()"
              (click)="submit(true)"
            >
              Create &amp; add another
            </button>
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
  /** Pre-selected location on open ("Add here" from a drilled place); null = unplaced. */
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
  // Location applies to both kinds.
  areaId = signal<string | null>(null);
  parentItemId = signal<string | null>(null);
  // Durable fields
  model = signal('');
  // Consumable fields
  unit = signal('');
  threshold = signal('');
  /** Opening stock to seed at the picked location; blank means "define the type, stock it later". */
  initialQuantity = signal('');
  /** Whether the collapsed "More details" section is open. */
  moreOpen = signal(false);

  creating = signal(false);
  createError = signal('');
  /** A "Create & add another" batch has created something; the host hears about it once, on close. */
  private pendingCreated = false;

  private dialog = viewChild(BrnDialog);
  private nameInput = viewChild<ElementRef<HTMLInputElement>>('nameInput');
  private durableApi = inject(InventoryDurableApiService);
  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);

  /** Whether the picked location is a real place (area or container) rather than Unplaced. */
  hasPlace = computed(() => this.locationValue().kind !== 'unplaced');

  /**
   * The typed starting quantity as a number, or `null` for "no opening stock" — blank and `0` both
   * mean none yet. Unparseable / negative input yields `NaN`, which {@link stockHint} reports.
   */
  quantityValue = computed<number | null>(() => {
    const raw = this.initialQuantity().trim();
    if (raw === '') return null;
    const n = Number(raw);
    if (!Number.isFinite(n) || n < 0) return Number.NaN;
    return n === 0 ? null : n;
  });

  /**
   * The symmetric both-or-neither block on quantity ↔ location: the backend requires a quantity and
   * a place together or neither, so say which half is missing instead of failing on submit. Also
   * names an invalid (negative / unparseable) quantity, so submit is never disabled silently.
   */
  stockHint = computed<string | null>(() => {
    if (this.kind() !== 'consumable') return null;
    const qty = this.quantityValue();
    if (qty !== null && Number.isNaN(qty)) {
      return 'Enter a starting quantity of 0 or more.';
    }
    if (qty !== null && qty > 0 && !this.hasPlace()) {
      return 'Pick a location to hold this stock, or leave the quantity blank.';
    }
    if (this.hasPlace() && qty === null) {
      return 'Enter a starting quantity, or set the location to Unplaced.';
    }
    return null;
  });

  /**
   * Inline hint for an invalid (negative / unparseable) restock threshold. Rendered outside the
   * collapsed "More details" section so the reason submit is disabled is never hidden with the field.
   */
  thresholdHint = computed<string | null>(() => {
    if (this.kind() !== 'consumable') return null;
    const t = this.threshold().trim();
    if (t === '') return null;
    const n = Number(t);
    return Number.isFinite(n) && n >= 0 ? null : 'Restock threshold must be 0 or more.';
  });

  canSubmit = computed(() => {
    if (this.name().trim().length === 0) return false;
    if (this.kind() === 'durable') return true;
    return this.thresholdHint() === null && this.stockHint() === null;
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
      this.resetAll();
      return;
    }
    if (this.pendingCreated) {
      this.pendingCreated = false;
      this.created.emit();
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    this.submit(false);
  }

  /** Creates the entry; `keepOpen` is the "Create & add another" path. */
  submit(keepOpen: boolean): void {
    if (!this.canSubmit() || this.creating()) return;

    this.creating.set(true);
    this.createError.set('');

    if (this.kind() === 'consumable') {
      const t = this.threshold().trim();
      const qty = this.quantityValue();
      // Quantity and place are both-or-neither — the backend validator rejects one without the other.
      const seedStock = qty !== null && this.hasPlace();
      const successMessage = seedStock
        ? `Consumable created with ${formatQty(qty)} ${resolveUnit(this.unit())}.`
        : 'Consumable created.';
      this.consumableApi.createConsumable({
        body: {
          name: this.name().trim(),
          unit: this.unit().trim() || undefined,
          restockThreshold: t === '' ? null : Number(t),
          brandId: this.brand()?.id ?? null,
          tags: this.tags(),
          notes: this.notes().trim() ? this.notes().trim() : null,
          initialQuantity: seedStock ? qty : null,
          initialAreaId: seedStock ? this.areaId() : null,
          initialContainerItemId: seedStock ? this.parentItemId() : null,
        },
      }).subscribe({
        next: () => this.onCreated(successMessage, keepOpen),
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
      next: () => this.onCreated('Durable created.', keepOpen),
      error: (err: unknown) => this.onCreateError(err, 'Failed to create durable.'),
    });
  }

  private onCreated(message: string, keepOpen: boolean): void {
    this.creating.set(false);
    toast.success(message);
    if (keepOpen) {
      // Hold the host's `created` until the dialog closes: hosts mount this dialog inside data-gated
      // `@if` blocks (empty states, "Add here" buttons) that a refetch flips, which would destroy and
      // force-close the dialog mid-batch — and a batch warrants one refresh, not one per item.
      this.pendingCreated = true;
      this.resetForAnother();
    } else {
      this.pendingCreated = false;
      this.created.emit();
      this.dialog()?.close();
    }
  }

  private onCreateError(err: unknown, fallback: string): void {
    this.creating.set(false);
    this.createError.set(this.errorHandler.getErrorMessage(err, fallback));
  }

  /** Full reset on open: back to a durable at the pre-filled location with every field blank. */
  private resetAll(): void {
    this.kind.set('durable');
    this.onLocationChange(this.initialLocation() ?? { kind: 'unplaced' });
    this.clearItemFields();
    this.moreOpen.set(false);
    this.creating.set(false);
    this.pendingCreated = false;
  }

  /**
   * Reset for "Create & add another": keeps the kind, the location, and the More-details disclosure
   * (you're unpacking a box into one place), clears everything item-specific, and returns focus to Name.
   */
  private resetForAnother(): void {
    this.clearItemFields();
    this.nameInput()?.nativeElement.focus();
  }

  /** Clears every item-specific field — the one list both resets share, so a new field lands in both. */
  private clearItemFields(): void {
    this.name.set('');
    this.notes.set('');
    this.brand.set(null);
    this.tags.set([]);
    this.model.set('');
    this.unit.set('');
    this.threshold.set('');
    this.initialQuantity.set('');
    this.createError.set('');
  }
}
