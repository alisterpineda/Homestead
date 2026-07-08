import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  signal,
  inject,
  DestroyRef,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { switchMap, map, of, defer, type Observable } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMinus, lucidePlus, lucideTriangleAlert, lucideArrowRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmItemImports } from '@/shared/components/spartan/item';
import { HlmButtonGroupImports } from '@/shared/components/spartan/button-group';
import { HlmProgressImports } from '@/shared/components/spartan/progress';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import {
  LocationPickerComponent,
  type ItemLocationValue,
} from '../../shared/location-picker/location-picker.component';
import { ItemStatusBadgeComponent } from '../../shared/item-status-badge.component';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { isTerminalStatus } from '../../shared/item-status-meta';
import { injectSheetSide } from '../../shared/sheet-side';
import { formatQty } from '../consumable-shared';
import { UnitPipe } from '../unit.pipe';
import { StockStepperEngine, stockLocationKey, stepperKey } from './stock-stepper.engine';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

/** Snapshot of the location row an inline form acts on (quantity includes optimistic deltas). */
interface PanelOpSource {
  areaId: string | null;
  containerItemId: string | null;
  label: string;
  quantity: number;
}

type InlineForm =
  | { kind: 'assign' }
  | { kind: 'move'; key: string; src: PanelOpSource }
  | { kind: 'correct'; key: string; src: PanelOpSource };

/** Captured at tap time so a queued commit acts on the consumable/location the user actually tapped. */
interface StepperPayload {
  consumableId: string;
  areaId: string | null;
  containerItemId: string | null;
}

/**
 * Quick panel over the consumables list: on-hand summary plus stepper-based stock changes per
 * location, with inline assign/move/correct forms. No sheet ever opens from inside the panel —
 * deeper work links out to the consumable detail page.
 */
@Component({
  selector: 'app-consumable-quick-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmItemImports,
    HlmButtonGroupImports,
    HlmProgressImports,
    HlmSpinner,
    LocationPickerComponent,
    ItemStatusBadgeComponent,
    TagBadgeComponent,
    UnitPipe,
  ],
  providers: [provideIcons({ lucideMinus, lucidePlus, lucideTriangleAlert, lucideArrowRight })],
  template: `
    <hlm-sheet [side]="sheetSide()" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal class="md:max-w-md max-md:max-h-[85dvh] max-md:rounded-t-xl">
        <hlm-sheet-header>
          <div class="flex items-center gap-2">
            <h3 hlmSheetTitle class="min-w-0 truncate">{{ consumableView()?.name }}</h3>
            @if (archived() && consumableView(); as s) {
              <app-item-status-badge [status]="s.status" />
            }
          </div>
        </hlm-sheet-header>

        @if (consumableView(); as s) {
          <div class="flex-1 space-y-4 overflow-y-auto px-4 py-1">
            @if (s.tags?.length) {
              <div class="flex flex-wrap gap-1">
                @for (t of s.tags ?? []; track t.id) {
                  <a [routerLink]="['/inventory/items']" [queryParams]="{ tagIds: t.id }">
                    <app-tag-badge [path]="t.path" [color]="t.effectiveColor" [compact]="true" />
                  </a>
                }
              </div>
            }

            <!-- On-hand summary -->
            <div hlmItem variant="outline">
              <div hlmItemContent>
                <span class="text-sm text-muted-foreground">Total on hand</span>
                <p class="text-2xl font-bold tabular-nums">
                  {{ formatQty(displayedTotal()) }}
                  <span class="text-sm font-normal text-muted-foreground">{{ s.unit | unit }}</span>
                </p>
                @if (entries().length > 0) {
                  <p class="text-xs text-muted-foreground">
                    across {{ entries().length }} {{ entries().length === 1 ? 'location' : 'locations' }}
                  </p>
                }
                @if (restockPercent() !== null) {
                  <hlm-progress class="mt-1" [value]="restockPercent()">
                    <hlm-progress-indicator />
                  </hlm-progress>
                }
                @if (isLowNow()) {
                  <p class="flex items-center gap-1 text-xs font-medium text-amber-700 dark:text-amber-300">
                    <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                    Low — restock at or below {{ formatQty(s.restockThreshold!) }} {{ s.unit | unit }}
                  </p>
                }
              </div>
            </div>

            <!-- Brand -->
            @if (s.brandId) {
              <div hlmItem variant="outline">
                <div hlmItemContent class="min-w-0">
                  <span class="text-sm text-muted-foreground">Brand</span>
                  <p>
                    <a
                      routerLink="/inventory/items"
                      [queryParams]="{ brand: s.brandId }"
                      class="hover:underline"
                    >{{ s.brandName }}</a>
                  </p>
                </div>
              </div>
            }

            <!-- Per-location stock -->
            <div class="flex items-center justify-between gap-2">
              <h4 class="text-sm font-medium text-muted-foreground">Stored at</h4>
              @if (!archived() && inlineForm()?.kind !== 'assign') {
                <button hlmBtn variant="ghost" size="sm" (click)="openAssign()">
                  <ng-icon hlm name="lucidePlus" size="sm" />
                  Assign to a place
                </button>
              }
            </div>

            @if (inlineForm()?.kind === 'assign') {
              <div class="space-y-3 rounded-md border border-dashed p-3">
                <div class="space-y-2">
                  <label hlmLabel>Into</label>
                  <app-location-picker
                    [value]="assignDest()"
                    (valueChange)="assignDest.set($event)"
                    [allowUnplaced]="false"
                    placeholder="Select a location"
                    ariaLabel="Receive into"
                  />
                </div>
                <div class="space-y-2">
                  <label hlmLabel for="panel-assign-qty">Quantity</label>
                  <div class="flex items-center gap-2">
                    <input
                      hlmInput
                      id="panel-assign-qty"
                      type="number"
                      inputmode="decimal"
                      step="0.001"
                      min="0.001"
                      class="w-full"
                      [value]="assignQty()"
                      (input)="assignQty.set(asValue($event))"
                    />
                    <span class="shrink-0 text-sm text-muted-foreground">{{ s.unit | unit }}</span>
                  </div>
                </div>
                @if (formError(); as err) {
                  <p class="text-sm text-destructive">{{ err }}</p>
                }
                <div class="flex justify-end gap-2">
                  <button hlmBtn variant="ghost" size="sm" (click)="closeForm()">Cancel</button>
                  <button hlmBtn size="sm" (click)="submitForm()" [disabled]="!canSubmitForm() || saving()">
                    {{ saving() ? 'Saving...' : 'Receive' }}
                  </button>
                </div>
              </div>
            }

            @if (loading() && entries().length === 0 && !detail()) {
              <div class="flex justify-center py-8"><hlm-spinner class="text-xl" /></div>
            } @else {
              @for (entry of entries(); track stockLocationKey(entry)) {
                <div hlmItem variant="outline" size="sm">
                  <div hlmItemContent class="min-w-0 max-md:basis-full">
                    <div hlmItemTitle class="w-full min-w-0">
                      <span class="truncate">{{ entry.locationLabel ?? '(unknown)' }}</span>
                    </div>
                    @if (archived()) {
                      <p class="text-sm tabular-nums text-muted-foreground">
                        {{ formatQty(entry.quantity) }} {{ s.unit | unit }}
                      </p>
                    }
                  </div>
                  @if (!archived()) {
                    <div hlmItemActions class="max-md:basis-full max-md:justify-between">
                      <div hlmButtonGroup>
                        <button
                          hlmBtn
                          variant="outline"
                          size="icon-sm"
                          class="max-md:size-11"
                          [disabled]="displayedQty(entry) < 1"
                          [attr.aria-label]="'Use one ' + (s.unit | unit)"
                          (click)="onTap(entry, -1)"
                        >
                          <ng-icon hlm name="lucideMinus" size="sm" />
                        </button>
                        <span hlmButtonGroupText class="min-w-16 justify-center px-2 text-sm tabular-nums max-md:h-11">
                          {{ formatQty(displayedQty(entry)) }} {{ s.unit | unit }}
                        </span>
                        <button
                          hlmBtn
                          variant="outline"
                          size="icon-sm"
                          class="max-md:size-11"
                          [attr.aria-label]="'Receive one ' + (s.unit | unit)"
                          (click)="onTap(entry, 1)"
                        >
                          <ng-icon hlm name="lucidePlus" size="sm" />
                        </button>
                      </div>
                      <div class="flex items-center gap-2">
                        <button hlmBtn variant="ghost" size="sm" class="max-md:h-11" (click)="openMove(entry)">Move</button>
                        <button hlmBtn variant="ghost" size="sm" class="max-md:h-11" (click)="openCorrect(entry)">Correct</button>
                      </div>
                    </div>
                  }

                  @if (entryForm(entry); as f) {
                    <div class="w-full basis-full space-y-3 rounded-md border border-dashed p-3">
                      @if (f.kind === 'move') {
                        <div class="space-y-2">
                          <label hlmLabel>To</label>
                          <app-location-picker
                            [value]="moveDest()"
                            (valueChange)="moveDest.set($event)"
                            [allowUnplaced]="false"
                            placeholder="Select a destination"
                            ariaLabel="Move to"
                          />
                        </div>
                        <div class="space-y-2">
                          <label hlmLabel for="panel-move-qty">Quantity</label>
                          <div class="flex items-center gap-2">
                            <input
                              hlmInput
                              id="panel-move-qty"
                              type="number"
                              inputmode="decimal"
                              step="0.001"
                              min="0.001"
                              class="w-full"
                              [value]="moveQty()"
                              (input)="moveQty.set(asValue($event))"
                            />
                            <span class="shrink-0 text-sm text-muted-foreground">{{ s.unit | unit }}</span>
                          </div>
                        </div>
                      } @else {
                        <div class="space-y-2">
                          <label hlmLabel for="panel-correct-qty">Corrected quantity</label>
                          <div class="flex items-center gap-2">
                            <input
                              hlmInput
                              id="panel-correct-qty"
                              type="number"
                              inputmode="decimal"
                              step="0.001"
                              min="0"
                              class="w-full"
                              [value]="correctQty()"
                              (input)="correctQty.set(asValue($event))"
                            />
                            <span class="shrink-0 text-sm text-muted-foreground">{{ s.unit | unit }}</span>
                          </div>
                        </div>
                      }
                      @if (formError(); as err) {
                        <p class="text-sm text-destructive">{{ err }}</p>
                      }
                      <div class="flex justify-end gap-2">
                        <button hlmBtn variant="ghost" size="sm" (click)="closeForm()">Cancel</button>
                        <button hlmBtn size="sm" (click)="submitForm()" [disabled]="!canSubmitForm() || saving()">
                          {{ saving() ? 'Saving...' : (f.kind === 'move' ? 'Move' : 'Save count') }}
                        </button>
                      </div>
                    </div>
                  }
                </div>
              } @empty {
                @if (!loading()) {
                  <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
                    No stock on hand — assign it to a place to receive some.
                  </div>
                }
              }
            }

            @if (fetchError()) {
              <p class="text-sm text-destructive">Failed to load stock locations.</p>
            }
          </div>

          <hlm-sheet-footer class="flex-row items-center justify-between gap-2">
            <span class="text-xs text-muted-foreground">Updated {{ updatedRelative() }}</span>
            <!-- Styled to match the durable panel's plain link exactly; stays a <button> only so it can
                 flush pending optimistic stepper taps before navigating (durable patches are synchronous). -->
            <button
              type="button"
              (click)="viewFullHistory()"
              [disabled]="navigating()"
              class="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground disabled:pointer-events-none disabled:opacity-60"
            >
              {{ navigating() ? 'Saving changes...' : 'View details' }}
              <ng-icon hlm name="lucideArrowRight" size="sm" />
            </button>
          </hlm-sheet-footer>
        }
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class ConsumableQuickPanelComponent {
  /** Row data — renders the header/summary instantly while the detail fetch is in flight. */
  consumable = input<ConsumableResponse | null>(null);
  state = input<BrnDialogState>('closed');

  changed = output<void>();
  closed = output<void>();

  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);

  protected sheetSide = injectSheetSide();

  // Engine commits deliberately outlive the component (close-flush must land), but emitting on a
  // destroyed OutputRef warns (NG0953) — late results still apply to signals, just don't emit.
  private destroyed = false;
  private destroyRef = inject(DestroyRef);

  protected readonly formatQty = formatQty;
  protected readonly stockLocationKey = stockLocationKey;

  loading = signal(false);
  fetchError = signal(false);

  private seedKey = computed(() => `${this.consumable()?.id ?? ''}|${this.state() === 'open'}`);

  private fetchKey = computed(() =>
    this.state() === 'open' ? (this.consumable()?.id ?? null) : null);

  /** The panel's truth: seeded by the open fetch, overwritten by every mutation response. */
  detail = signal<ConsumableDetailResponse | null>(null);

  // Bumped by every mutation response — an open fetch issued before it is stale and discarded,
  // so a slow GET can never overwrite a newer commit response.
  private detailVersion = 0;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));

    // The open fetch. The display writes are component-bound (a destroyed panel has nothing to
    // show), unlike the engine queue, which must outlive teardown to land close-flushed commits.
    toObservable(this.fetchKey).pipe(
      switchMap((id) => {
        if (!id) {
          this.detail.set(null);
          return of(null);
        }
        this.loading.set(true);
        this.fetchError.set(false);
        const version = this.detailVersion;
        return this.consumableApi.getConsumable({ id }).pipe(
          map((d) => {
            this.loading.set(false);
            if (version === this.detailVersion) this.detail.set(d);
            return null;
          }),
          handleQueryError(this.loading, this.fetchError, null),
        );
      }),
      takeUntilDestroyed(),
    ).subscribe();
  }

  /** Mutation responses are the new truth — but only for the consumable the panel still shows. */
  private applyDetail(d: ConsumableDetailResponse): void {
    if (d.consumable.id !== this.consumable()?.id) return;
    this.detailVersion++;
    this.detail.set(d);
  }

  consumableView = computed<ConsumableResponse | null>(() => this.detail()?.consumable ?? this.consumable());
  entries = computed<StockEntryResponse[]>(() => this.detail()?.locations ?? []);
  archived = computed(() => {
    const s = this.consumableView();
    return s != null && isTerminalStatus(s.status);
  });

  // All panel mutations are serialized through the engine so each ConsumableDetailResponse can
  // overwrite `detail` without clobbering newer state. Commit targets travel in the tap-time
  // payload — never read from component state at run time, which may show another consumable by then.
  private engine = new StockStepperEngine<StepperPayload>({
    commit: (p, delta) => {
      const loc = { areaId: p.areaId, containerItemId: p.containerItemId };
      if (delta < 0) {
        return this.consumableApi.consumeStock({ id: p.consumableId, body: { ...loc, quantity: -delta, note: null } });
      }
      return this.consumableApi.receiveStock({
        id: p.consumableId,
        body: { ...loc, quantity: delta, unitCost: null, unitCostCurrencyId: null, note: null },
      });
    },
    onCommitted: (d) => {
      this.applyDetail(d);
      if (!this.destroyed) this.changed.emit();
    },
    onError: (p, err) => {
      if (!this.errorHandler.isServerOrNetworkError(err)) {
        this.errorHandler.handleError(err, 'Failed to update stock.');
      }
      // Local truth for that consumable is suspect — resync THROUGH the queue so the refetch can
      // never race a queued commit's response. Skipped if the panel moved to another consumable.
      this.engine.enqueue(
        () => defer(() =>
          this.consumable()?.id === p.consumableId
            ? this.consumableApi.getConsumable({ id: p.consumableId })
            : of(null)),
        (d) => {
          if (d) this.applyDetail(d);
        },
        () => {
          // A failed resync keeps the last known state; the next mutation resyncs again.
        },
      );
    },
  });

  /** Engine key for an entry, scoped to the current consumable so deltas never bleed across them. */
  private keyFor(entry: { areaId?: string | null; containerItemId?: string | null }): string {
    return stepperKey(this.consumableView()?.id ?? '', entry);
  }

  displayedQty(entry: StockEntryResponse): number {
    return entry.quantity + this.engine.deltaFor(this.keyFor(entry));
  }

  displayedTotal = computed(() => {
    const id = this.consumableView()?.id;
    return (this.consumableView()?.totalOnHand ?? 0) + (id ? this.engine.totalDelta(id) : 0);
  });

  /** Low tracks optimistic taps, not just the last server response. */
  isLowNow = computed(() => {
    const threshold = this.consumableView()?.restockThreshold;
    return threshold != null && this.displayedTotal() <= threshold;
  });

  // hlm-progress throws when value > max and its indicator ignores max — clamp to 0..100 here.
  restockPercent = computed<number | null>(() => {
    const threshold = this.consumableView()?.restockThreshold;
    if (threshold == null || threshold <= 0) return null;
    return Math.min(100, Math.max(0, Math.round((this.displayedTotal() / threshold) * 100)));
  });

  updatedRelative = computed(() => {
    const updatedAt = this.consumableView()?.updatedAt;
    return updatedAt ? formatRelativeAge(updatedAt) : '';
  });

  // --- Inline forms (one open at a time) ---

  protected inlineForm = seededLinkedSignal<string, InlineForm | null>(this.seedKey, () => null);

  // Bumped on every open*() so each opening gets a fresh seed key. Without it, close→reopen of the
  // same form round-trips formSeedKey to an identical string — and since nothing reads the key
  // while the form is closed, the linked signals never observe the intermediate value and keep the
  // previously typed draft instead of reseeding.
  private formOpenCount = signal(0);

  private formSeedKey = computed(() => {
    const f = this.inlineForm();
    const key = f && f.kind !== 'assign' ? f.key : '';
    return `${this.seedKey()}|${this.formOpenCount()}|${f?.kind ?? ''}|${key}`;
  });

  assignDest = seededLinkedSignal<string, ItemLocationValue>(this.formSeedKey, () => ({ kind: 'unplaced' }));
  assignQty = seededLinkedSignal(this.formSeedKey, () => '');
  moveDest = seededLinkedSignal<string, ItemLocationValue>(this.formSeedKey, () => ({ kind: 'unplaced' }));
  moveQty = seededLinkedSignal(this.formSeedKey, () => {
    const f = this.inlineForm();
    return f?.kind === 'move' ? formatQty(f.src.quantity) : '';
  });
  correctQty = seededLinkedSignal(this.formSeedKey, () => {
    const f = this.inlineForm();
    return f?.kind === 'correct' ? formatQty(f.src.quantity) : '';
  });
  // Panel-scoped (NOT formSeedKey): switching forms must not reset the in-flight guard, or a
  // queued op could be double-submitted.
  saving = seededLinkedSignal(this.seedKey, () => false);
  /** True while "View full history" waits for outstanding commits to land before navigating. */
  navigating = seededLinkedSignal(this.seedKey, () => false);

  protected entryForm(entry: StockEntryResponse): InlineForm | null {
    const f = this.inlineForm();
    return f && f.kind !== 'assign' && f.key === this.keyFor(entry) ? f : null;
  }

  onTap(entry: StockEntryResponse, delta: 1 | -1): void {
    const consumableId = this.consumableView()?.id;
    if (!consumableId) return;
    this.engine.tap(stepperKey(consumableId, entry), delta, {
      consumableId,
      areaId: entry.areaId ?? null,
      containerItemId: entry.containerItemId ?? null,
    });
  }

  openAssign(): void {
    this.engine.flushAll();
    this.formOpenCount.update(n => n + 1);
    this.inlineForm.set({ kind: 'assign' });
  }

  openMove(entry: StockEntryResponse): void {
    const src = this.toOpSource(entry);
    this.engine.flushAll();
    this.formOpenCount.update(n => n + 1);
    this.inlineForm.set({ kind: 'move', key: this.keyFor(entry), src });
  }

  openCorrect(entry: StockEntryResponse): void {
    const src = this.toOpSource(entry);
    this.engine.flushAll();
    this.formOpenCount.update(n => n + 1);
    this.inlineForm.set({ kind: 'correct', key: this.keyFor(entry), src });
  }

  closeForm(): void {
    this.inlineForm.set(null);
  }

  /** Snapshot taken at click time — the displayed (optimistic) quantity, not the stale base. */
  private toOpSource(entry: StockEntryResponse): PanelOpSource {
    return {
      areaId: entry.areaId ?? null,
      containerItemId: entry.containerItemId ?? null,
      label: entry.locationLabel ?? '(unknown)',
      quantity: this.displayedQty(entry),
    };
  }

  private parsedFormQty = computed(() => {
    const f = this.inlineForm();
    if (!f) return NaN;
    const raw = (f.kind === 'assign' ? this.assignQty() : f.kind === 'move' ? this.moveQty() : this.correctQty()).trim();
    return raw === '' ? NaN : Number(raw);
  });

  formError = computed<string | null>(() => {
    const f = this.inlineForm();
    if (!f) return null;
    const q = this.parsedFormQty();
    if (Number.isNaN(q)) return 'Enter a quantity.';

    if (f.kind === 'correct') {
      if (q < 0) return 'Quantity must be zero or positive.';
      // Submitting the current on-hand unchanged is a no-op the server rejects, so block it here.
      // Compare against the LIVE entry quantity (kept fresh by resyncs), not the click-time
      // snapshot — the snapshot goes stale if the open-triggered flush fails.
      const live = this.entries().find((e) => this.keyFor(e) === f.key)?.quantity ?? 0;
      if (q === live) return 'Enter a different quantity to correct.';
      return null;
    }

    if (q <= 0) return 'Quantity must be greater than zero.';

    // Advisory over-draw guard for a move — the source snapshot is captured optimistically at open.
    if (f.kind === 'move' && q > f.src.quantity) return `Only ${formatQty(f.src.quantity)} on hand here.`;

    const dest = f.kind === 'assign' ? this.assignDest() : this.moveDest();
    if (dest.kind === 'unplaced') return f.kind === 'move' ? 'Pick a destination.' : 'Pick a location.';
    if (f.kind === 'move') {
      const d = this.destFields(dest);
      if (d.areaId === f.src.areaId && d.containerItemId === f.src.containerItemId) {
        return 'Destination must differ from the source.';
      }
    }
    return null;
  });

  canSubmitForm = computed(() => this.formError() === null);

  private destFields(v: ItemLocationValue): { areaId: string | null; containerItemId: string | null } {
    if (v.kind === 'area') return { areaId: v.id, containerItemId: null };
    if (v.kind === 'container') return { areaId: null, containerItemId: v.id };
    return { areaId: null, containerItemId: null };
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  submitForm(): void {
    const f = this.inlineForm();
    const id = this.consumableView()?.id;
    if (!f || !id || !this.canSubmitForm() || this.saving()) return;
    const qty = this.parsedFormQty();
    this.saving.set(true);

    // The full request is captured NOW — the queued job must not read form signals at run time,
    // because they reseed (e.g. to 'unplaced') when the form closes or switches while queued.
    const work = ((): (() => Observable<ConsumableDetailResponse>) => {
      switch (f.kind) {
        case 'assign': {
          const dest = this.destFields(this.assignDest());
          const body = {
            areaId: dest.areaId,
            containerItemId: dest.containerItemId,
            quantity: qty,
            unitCost: null,
            unitCostCurrencyId: null,
            note: null,
          };
          return () => this.consumableApi.receiveStock({ id, body });
        }
        case 'move': {
          const dest = this.destFields(this.moveDest());
          const body = {
            fromAreaId: f.src.areaId,
            fromContainerItemId: f.src.containerItemId,
            toAreaId: dest.areaId,
            toContainerItemId: dest.containerItemId,
            quantity: qty,
            note: null,
          };
          return () => this.consumableApi.transferStock({ id, body });
        }
        case 'correct': {
          const body = { areaId: f.src.areaId, containerItemId: f.src.containerItemId, newQuantity: qty, note: null };
          return () => this.consumableApi.adjustStock({ id, body });
        }
      }
    })();

    this.engine.enqueue(
      work,
      (d) => {
        this.saving.set(false);
        this.applyDetail(d);
        if (!this.destroyed) this.changed.emit();
        // A late success must not close a form the user opened for a different consumable since.
        if (d.consumable.id === this.consumable()?.id) this.closeForm();
        toast.success('Stock updated.');
      },
      (err) => {
        this.saving.set(false);
        if (!this.errorHandler.isServerOrNetworkError(err)) {
          this.errorHandler.handleError(err, 'Failed to update stock.');
        }
      },
    );
  }

  // The component instance survives close (only the portal content is destroyed), so a flush
  // triggered here still completes and `changed` still reaches the list.
  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.engine.flushAll();
      this.closed.emit();
    }
  }

  async viewFullHistory(): Promise<void> {
    if (this.navigating()) return;
    const id = this.consumableView()?.id;
    this.navigating.set(true);
    this.engine.flushAll();
    await this.engine.whenIdle();
    this.navigating.set(false);
    if (this.destroyed) return;
    this.closed.emit();
    if (id) this.router.navigate(['/inventory/items', id]);
  }
}
