import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  signal,
  DestroyRef,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBox, lucideArrowRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmItemImports } from '@/shared/components/spartan/item';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import {
  LocationPickerComponent,
  type ItemLocationValue,
} from '../../shared/location-picker/location-picker.component';
import { ItemStatusBadgeComponent } from '../../shared/item-status-badge.component';
import { ALL_ITEM_STATUSES, itemStatusLabel } from '../../shared/item-status-meta';
import { injectSheetSide } from '../../shared/sheet-side';
import { formatPath } from '../../list/inventory-row';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { replace } from '@/shared/json-patch';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

type InlineForm = 'move' | 'lent' | null;

/**
 * Quick panel over the items list: location, key details, and fast status/location changes
 * without leaving the list. All sub-interactions are inline; editing stays on the row actions.
 * Terminal-status items remain fully actionable — location is a documented escape hatch and bad
 * status transitions are rejected server-side with clear messages.
 */
@Component({
  selector: 'app-item-quick-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmItemImports,
    HlmDropdownMenuImports,
    LocationPickerComponent,
    ItemStatusBadgeComponent,
  ],
  providers: [provideIcons({ lucideBox, lucideArrowRight })],
  template: `
    <hlm-sheet [side]="sheetSide()" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal class="md:max-w-md max-md:max-h-[85dvh] max-md:rounded-t-xl">
        <hlm-sheet-header>
          <h3 hlmSheetTitle class="flex items-center gap-2">
            @if (view()?.isContainer) {
              <ng-icon hlm name="lucideBox" size="sm" class="shrink-0 text-muted-foreground" aria-label="Container" />
            }
            <span class="truncate">{{ view()?.name }}</span>
          </h3>
          @if (view()?.id; as id) {
            <a
              [routerLink]="['/inventory/items', id]"
              class="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
            >
              Open full view
              <ng-icon hlm name="lucideArrowRight" size="sm" />
            </a>
          }
        </hlm-sheet-header>

        @if (view(); as it) {
          <div class="flex-1 space-y-4 overflow-y-auto px-4 py-1">
            <!-- Location -->
            <div hlmItem variant="outline">
              <div hlmItemContent class="min-w-0">
                <span class="text-sm text-muted-foreground">Location</span>
                <p class="truncate text-sm font-medium" [title]="locationText()">{{ locationText() }}</p>
                @if (it.compartment) {
                  <p class="text-xs text-muted-foreground">Compartment: {{ it.compartment }}</p>
                }
              </div>
              @if (inlineForm() !== 'move') {
                <div hlmItemActions>
                  <button hlmBtn variant="ghost" size="sm" class="max-md:h-11" (click)="openMove()">Move</button>
                </div>
              }

              @if (inlineForm() === 'move') {
                <div class="w-full basis-full space-y-3 rounded-md border border-dashed p-3">
                  <div class="space-y-2">
                    <label hlmLabel>To</label>
                    <app-location-picker
                      [value]="moveDest()"
                      (valueChange)="moveDest.set($event)"
                      [selectedLabel]="moveSelectedLabel()"
                      [excludeItemId]="it.id"
                      [allowUnplaced]="true"
                      ariaLabel="Move to"
                    />
                  </div>
                  <div class="flex justify-end gap-2">
                    <button hlmBtn variant="ghost" size="sm" (click)="closeForm()">Cancel</button>
                    <button hlmBtn size="sm" (click)="confirmMove()" [disabled]="!canConfirmMove() || saving()">
                      {{ saving() ? 'Saving...' : 'Move' }}
                    </button>
                  </div>
                </div>
              }
            </div>

            <!-- Details -->
            <div hlmItem variant="outline">
              <div hlmItemContent class="min-w-0">
                <dl class="grid grid-cols-[auto_1fr] items-baseline gap-x-4 gap-y-2 text-sm">
                  <dt class="text-muted-foreground">Status</dt>
                  <dd><app-item-status-badge [status]="it.status" /></dd>
                  @if (it.serial) {
                    <dt class="text-muted-foreground">Serial</dt>
                    <dd class="truncate">{{ it.serial }}</dd>
                  }
                  @if (it.value != null) {
                    <dt class="text-muted-foreground">Value</dt>
                    <dd class="tabular-nums">{{ it.value }}{{ it.valueCurrencySymbol ? ' ' + it.valueCurrencySymbol : '' }}</dd>
                  }
                  @if (it.acquiredOn) {
                    <dt class="text-muted-foreground">Acquired on</dt>
                    <dd>{{ it.acquiredOn | date: 'mediumDate' }}</dd>
                  }
                  @if (it.status === 'Lent' && it.lentTo) {
                    <dt class="text-muted-foreground">Lent to</dt>
                    <dd class="truncate">{{ it.lentTo }}</dd>
                  }
                </dl>
                @if (it.notes) {
                  <p class="whitespace-pre-wrap text-sm text-muted-foreground">{{ it.notes }}</p>
                }
                <p class="text-xs text-muted-foreground">Updated {{ updatedRelative() }}</p>
              </div>
            </div>

            <!-- Inline lent form (opened by picking Lent in the status menu) -->
            @if (inlineForm() === 'lent') {
              <div class="space-y-3 rounded-md border border-dashed p-3">
                <div class="space-y-2">
                  <label hlmLabel for="panel-lent-to">Lent to</label>
                  <input
                    hlmInput
                    id="panel-lent-to"
                    class="w-full"
                    maxlength="256"
                    placeholder="e.g. Dave next door"
                    [value]="lentToDraft()"
                    (input)="lentToDraft.set(asValue($event))"
                  />
                </div>
                <div class="flex justify-end gap-2">
                  <button hlmBtn variant="ghost" size="sm" (click)="closeForm()">Cancel</button>
                  <button hlmBtn size="sm" (click)="confirmLent()" [disabled]="!lentToDraft().trim() || saving()">
                    {{ saving() ? 'Saving...' : 'Mark as lent' }}
                  </button>
                </div>
              </div>
            }

            <!-- Contents (containers only) -->
            @if (it.isContainer) {
              <div hlmItem variant="outline" size="sm">
                <div hlmItemContent>
                  <span class="text-sm">
                    {{ contentsCount() === null ? '…' : contentsCount() }}
                    {{ contentsCount() === 1 ? 'item' : 'items' }}
                  </span>
                </div>
                <div hlmItemActions>
                  <button hlmBtn variant="ghost" size="sm" class="max-md:h-11" (click)="onViewContents()">
                    View contents
                    <ng-icon hlm name="lucideArrowRight" size="sm" />
                  </button>
                </div>
              </div>
            }
          </div>

          <hlm-sheet-footer>
            <button hlmBtn variant="outline" class="w-full" [hlmDropdownMenuTrigger]="statusMenu">
              Change status
            </button>
            <ng-template #statusMenu>
              <hlm-dropdown-menu class="w-44">
                <hlm-dropdown-menu-group>
                  @for (s of statuses; track s) {
                    <button
                      hlmDropdownMenuRadio
                      [checked]="it.status === s"
                      (triggered)="onStatusPick(s)"
                    >
                      <hlm-dropdown-menu-radio-indicator />
                      {{ statusLabel(s) }}
                    </button>
                  }
                </hlm-dropdown-menu-group>
              </hlm-dropdown-menu>
            </ng-template>
          </hlm-sheet-footer>
        }
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class ItemQuickPanelComponent {
  item = input<ItemResponse | null>(null);
  state = input<BrnDialogState>('closed');

  changed = output<void>();
  closed = output<void>();
  viewContents = output<ItemResponse>();

  private itemApi = inject(InventoryItemApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  protected sheetSide = injectSheetSide();

  protected readonly statuses = ALL_ITEM_STATUSES;
  protected readonly statusLabel = itemStatusLabel;

  private seedKey = computed(() => `${this.item()?.id ?? ''}|${this.state() === 'open'}`);

  /** Row data as the seed; every patch response overwrites it as the new truth. */
  current = seededLinkedSignal<string, ItemResponse | null>(this.seedKey, () => this.item());

  view = computed(() => this.current() ?? this.item());

  protected locationText = computed(() => {
    const it = this.view();
    return it ? formatPath(it) : '';
  });

  protected updatedRelative = computed(() => {
    const updatedAt = this.view()?.updatedAt;
    return updatedAt ? formatRelativeAge(updatedAt) : '';
  });

  // Lazy contents count, containers only — PageSize 1 just to read totalCount.
  private contentsKey = computed(() => {
    const it = this.view();
    const open = this.state() === 'open';
    return { id: open && it?.isContainer ? it.id : null };
  });

  contentsCount = toSignal<number | null, number | null>(
    toObservable(this.contentsKey).pipe(
      switchMap(({ id }) => {
        if (!id) return of(null);
        return this.itemApi.getItems({ ParentItemId: id, IncludeInactive: true, Page: 1, PageSize: 1 }).pipe(
          map((r) => r.totalCount ?? 0),
          catchError(() => of(null)),
        );
      }),
    ),
    { initialValue: null },
  );

  // --- Inline forms (one open at a time) ---

  protected inlineForm = seededLinkedSignal<string, InlineForm>(this.seedKey, () => null);

  // Bumped on every form open so each opening gets a fresh seed key. Without it, close→reopen of
  // the same form round-trips formSeedKey to an identical string — and since nothing reads the key
  // while the form is closed, the linked signals never observe the intermediate value and keep the
  // previously typed draft instead of reseeding. (Same mechanism as in supply-quick-panel.)
  private formOpenCount = signal(0);

  private formSeedKey = computed(() => `${this.seedKey()}|${this.formOpenCount()}|${this.inlineForm() ?? ''}`);

  moveDest = seededLinkedSignal<string, ItemLocationValue>(this.formSeedKey, () => {
    const it = this.view();
    if (it?.parentItemId) return { kind: 'container', id: it.parentItemId };
    if (it?.areaId) return { kind: 'area', id: it.areaId };
    return { kind: 'unplaced' };
  });
  lentToDraft = seededLinkedSignal(this.formSeedKey, () => this.view()?.lentTo ?? '');
  // Panel-scoped (NOT formSeedKey): switching forms must not reset the in-flight guard, or a
  // second concurrent PATCH could be issued from stale state.
  saving = seededLinkedSignal(this.seedKey, () => false);

  /** Known label of the current location, shown before the picker's lists load. */
  protected moveSelectedLabel = computed<string | null>(() => {
    const it = this.view();
    if (!it) return null;
    const dest = this.moveDest();
    if (dest.kind === 'container' && dest.id === (it.parentItemId ?? null)) {
      return it.path?.[it.path.length - 1]?.name ?? null;
    }
    if (dest.kind === 'area' && dest.id === (it.areaId ?? null)) {
      return it.areaName ?? null;
    }
    return null;
  });

  openMove(): void {
    this.formOpenCount.update(n => n + 1);
    this.inlineForm.set('move');
  }

  closeForm(): void {
    this.inlineForm.set(null);
  }

  /**
   * Area/parent ops for changes only. Moving into a DIFFERENT container also clears the
   * compartment explicitly — the server only auto-clears it when the target parent is null.
   */
  buildMoveOps(dest: ItemLocationValue): JsonPatchDocument {
    const it = this.view();
    if (!it) return [];
    const target = dest.kind === 'area'
      ? { areaId: dest.id, parentItemId: null }
      : dest.kind === 'container'
        ? { areaId: null, parentItemId: dest.id }
        : { areaId: null, parentItemId: null };

    const ops: JsonPatchDocument = [];
    if (target.areaId !== (it.areaId ?? null)) ops.push(replace('/areaId', target.areaId));
    if (target.parentItemId !== (it.parentItemId ?? null)) ops.push(replace('/parentItemId', target.parentItemId));
    if (
      target.parentItemId !== null &&
      target.parentItemId !== (it.parentItemId ?? null) &&
      (it.compartment ?? null) !== null
    ) {
      ops.push(replace('/compartment', null));
    }
    return ops;
  }

  canConfirmMove = computed(() => this.buildMoveOps(this.moveDest()).length > 0);

  confirmMove(): void {
    const ops = this.buildMoveOps(this.moveDest());
    if (ops.length === 0) return;
    this.patch(ops, 'Item moved.');
  }

  onStatusPick(status: ItemStatus): void {
    const it = this.view();
    if (!it || status === it.status) return;
    if (status === 'Lent') {
      // Lent requires a non-empty /lentTo in the same patch — collect it inline first.
      this.formOpenCount.update(n => n + 1);
      this.inlineForm.set('lent');
      return;
    }
    // Any other status auto-clears lentTo server-side; send /status only.
    this.patch([replace('/status', status)], 'Status updated.');
  }

  confirmLent(): void {
    const lentTo = this.lentToDraft().trim();
    if (!lentTo) return;
    this.patch([replace('/status', 'Lent'), replace('/lentTo', lentTo)], 'Status updated.');
  }

  onViewContents(): void {
    const it = this.view();
    if (it) this.viewContents.emit(it);
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private patch(ops: JsonPatchDocument, successMessage: string): void {
    const it = this.view();
    if (!it || this.saving()) return;
    this.saving.set(true);
    this.itemApi.patchItem({ id: it.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to update item.'),
    ).subscribe((updated) => {
      this.saving.set(false);
      // Apply only if the panel still shows this item — a late response must not overwrite
      // another item's panel (there is no server fetch here to self-correct afterwards).
      if (updated.id === this.item()?.id) {
        this.current.set(updated);
        this.inlineForm.set(null);
      }
      this.changed.emit();
      toast.success(successMessage);
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
