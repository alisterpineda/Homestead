import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  DestroyRef,
} from '@angular/core';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, debounceTime } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX, lucideSearch } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import {
  LocationPickerComponent,
  type ItemLocationValue,
} from '../../shared/location-picker/location-picker.component';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

/**
 * Collapses individually-tracked durables back into fungible stock. Multi-pick (searched, Active
 * non-fungible durables) + a target location, gated behind a destructive confirm — the operation
 * archives the picked durables and is lossy. The server's sealing/eligibility gates stay authoritative;
 * their 400s surface inline here, so we don't pre-filter containers-with-contents client-side.
 */
@Component({
  selector: 'app-collapse-durables-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmDialogImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmBadgeImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmSpinner,
    LocationPickerComponent,
  ],
  providers: [provideIcons({ lucideX, lucideSearch })],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content
        *hlmDialogPortal
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[520px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Collapse durables into {{ consumableName() }}</h2>
          <p hlmDialogDescription>
            Fold individually-tracked durables back into this consumable's count. Each picked durable is
            archived and adds one {{ unit() }} to the chosen location.
          </p>
        </div>

        <div class="flex flex-col gap-4 py-2">
          <!-- Selected badges -->
          @if (selectedList().length > 0) {
            <div class="flex flex-wrap gap-1.5">
              @for (item of selectedList(); track item.id) {
                <span hlmBadge variant="secondary" class="gap-1">
                  {{ item.name }}
                  <button type="button" class="ml-0.5 rounded-full hover:text-foreground" (click)="deselect(item.id)" [attr.aria-label]="'Remove ' + item.name">
                    <ng-icon hlm name="lucideX" size="xs" />
                  </button>
                </span>
              }
            </div>
          }

          <!-- Search -->
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Durables to collapse</label>
            <div class="relative">
              <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
              <input
                hlmInput
                class="w-full pl-9"
                placeholder="Search individually-tracked durables..."
                [value]="searchTerm()"
                (input)="searchTerm.set(asValue($event))"
                aria-label="Search durables to collapse"
              />
            </div>
            <div class="max-h-[240px] overflow-y-auto rounded-md border">
              @if (searching()) {
                <div class="flex justify-center py-6"><hlm-spinner class="size-4" /></div>
              } @else if (candidates().length === 0) {
                <p class="px-3 py-6 text-center text-sm text-muted-foreground">
                  No individually-tracked durables found.
                </p>
              } @else {
                @for (item of candidates(); track item.id) {
                  <label class="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-accent hover:text-accent-foreground">
                    <hlm-checkbox [checked]="isSelected(item.id)" (checkedChange)="toggle(item)" [aria-label]="checkboxLabel(item)" />
                    <!-- aria-hidden: the checkbox's aria-label already names the row (name + location);
                         leaving this visible text in the a11y tree would double the name via the wrapping label. -->
                    <span class="min-w-0 flex-1" aria-hidden="true">
                      <span class="block truncate text-sm">{{ item.name }}</span>
                      <span class="block truncate text-xs text-muted-foreground">{{ locationLabel(item) }}</span>
                    </span>
                  </label>
                }
              }
            </div>
          </div>

          <!-- Target location -->
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Add the count to</label>
            <app-location-picker
              [value]="targetLocation()"
              (valueChange)="targetLocation.set($event)"
              [allowUnplaced]="false"
              placeholder="Select a location"
              ariaLabel="Collapse into location"
            />
          </div>

          @if (validationError(); as err) {
            <p class="text-sm text-destructive">{{ err }}</p>
          }
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
          <button hlmBtn variant="destructive" type="button" [disabled]="!canCollapse()" (click)="confirmState.set('open')">
            Collapse {{ selectedCount() }} {{ selectedCount() === 1 ? 'durable' : 'durables' }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>

    <!-- Lossy confirm -->
    <hlm-alert-dialog [state]="confirmState()" (stateChanged)="confirmState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Collapse {{ selectedCount() }} {{ selectedCount() === 1 ? 'durable' : 'durables' }}?</h2>
          <p hlmAlertDialogDescription>
            Collapsing archives {{ selectedCount() === 1 ? 'this durable' : 'these ' + selectedCount() + ' durables' }}
            and adds {{ selectedCount() }} {{ unit() }} to {{ consumableName() }}. Their serials, values, and
            individual histories stop being tracked.
          </p>
        </div>
        <!-- Name every durable being archived, surfacing any recorded value/serial — so a mis-picked
             high-value durable is obvious at the point of no return. -->
        <ul class="my-1 max-h-48 space-y-1 overflow-y-auto rounded-md border p-2 text-sm">
          @for (item of selectedList(); track item.id) {
            <li class="flex items-baseline justify-between gap-3">
              <span class="min-w-0 truncate">{{ item.name }}</span>
              @if (itemDetailLabel(item); as detail) {
                <span class="shrink-0 text-xs font-medium text-foreground">{{ detail }}</span>
              }
            </li>
          }
        </ul>
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" (click)="handleCollapse(ctx)" [disabled]="collapsing()">
            {{ collapsing() ? 'Collapsing...' : 'Collapse' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class CollapseDurablesDialogComponent {
  consumableId = input.required<string>();
  consumableName = input.required<string>();
  unit = input.required<string>();
  state = input<BrnDialogState>('closed');

  collapsed = output<ConsumableDetailResponse>();
  closed = output<void>();

  private durableApi = inject(InventoryDurableApiService);
  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  protected searchTerm = signal('');
  protected searching = signal(false);
  protected collapsing = signal(false);
  protected confirmState = signal<BrnDialogState>('closed');

  // Selections live in a Map so they survive search changes (a deselected search no longer lists
  // them, but they stay picked).
  private selected = signal<Map<string, DurableResponse>>(new Map());
  protected targetLocation = signal<ItemLocationValue>({ kind: 'unplaced' });

  private searchKey = computed(() => ({ term: this.searchTerm().trim(), open: this.state() === 'open' }));

  private searchResults = toSignal(
    toObservable(this.searchKey).pipe(
      debounceTime(250),
      switchMap(({ term, open }) => {
        if (!open) return of<DurableResponse[]>([]);
        this.searching.set(true);
        return this.durableApi.getDurables({ Search: term || undefined, Page: 1, PageSize: 50 }).pipe(
          map((r) => (r.items ?? []) as DurableResponse[]),
          catchError(() => of<DurableResponse[]>([])),
        );
      }),
      map((items) => {
        this.searching.set(false);
        // Only Active, individually-tracked durables are eligible to collapse.
        return items.filter((i) => i.status === 'Active' && i.fungibility === 'NonFungible');
      }),
    ),
    { initialValue: [] },
  );

  protected candidates = computed(() => this.searchResults());
  protected selectedList = computed(() => Array.from(this.selected().values()));
  protected selectedCount = computed(() => this.selected().size);

  private targetIsSelectedContainer = computed(() => {
    const t = this.targetLocation();
    return t.kind === 'container' && this.selected().has(t.id);
  });

  protected validationError = computed<string | null>(() => {
    if (this.targetIsSelectedContainer()) {
      return 'The destination container is one of the selected durables. Pick a different location.';
    }
    return null;
  });

  protected canCollapse = computed(() =>
    this.selected().size > 0 &&
    this.targetLocation().kind !== 'unplaced' &&
    this.validationError() === null);

  protected isSelected(id: string): boolean {
    return this.selected().has(id);
  }

  protected toggle(item: DurableResponse): void {
    const next = new Map(this.selected());
    if (next.has(item.id)) next.delete(item.id);
    else next.set(item.id, item);
    this.selected.set(next);
  }

  protected deselect(id: string): void {
    const next = new Map(this.selected());
    next.delete(id);
    this.selected.set(next);
  }

  protected locationLabel(item: DurableResponse): string {
    const names = (item.path ?? []).map((s) => s.name);
    return names.length > 0 ? names.join(' › ') : 'Unplaced';
  }

  // The checkbox's sole accessible name — name plus location, so screen-reader users can tell apart
  // two same-named durables at different places.
  protected checkboxLabel(item: DurableResponse): string {
    return `${item.name} — ${this.locationLabel(item)}`;
  }

  // Surfaces the identity a collapse would discard from the active picture — value and/or serial —
  // shown in the destructive confirm so an accidentally-picked valuable durable stands out.
  protected itemDetailLabel(item: DurableResponse): string | null {
    const parts: string[] = [];
    if (item.value != null) parts.push(`${item.valueCurrencySymbol ?? ''}${item.value}`);
    if (item.serial) parts.push(`SN ${item.serial}`);
    return parts.length > 0 ? parts.join(' · ') : null;
  }

  protected asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private targetFields(): { areaId: string | null; containerItemId: string | null } {
    const t = this.targetLocation();
    if (t.kind === 'area') return { areaId: t.id, containerItemId: null };
    if (t.kind === 'container') return { areaId: null, containerItemId: t.id };
    return { areaId: null, containerItemId: null };
  }

  handleCollapse(ctx: { close: () => void }): void {
    if (!this.canCollapse() || this.collapsing()) return;
    const target = this.targetFields();
    const itemIds = Array.from(this.selected().keys());
    const count = itemIds.length;
    this.collapsing.set(true);

    this.consumableApi.collapseDurables({
      id: this.consumableId(),
      body: { itemIds, areaId: target.areaId, containerItemId: target.containerItemId },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.collapsing, 'Failed to collapse durables.'),
    ).subscribe({
      next: (detail) => {
        this.collapsing.set(false);
        ctx.close();
        toast.success(`Collapsed ${count} ${count === 1 ? 'durable' : 'durables'} into ${this.consumableName()}.`);
        this.collapsed.emit(detail);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'open') {
      // Fresh pick each time the dialog opens.
      this.searchTerm.set('');
      this.selected.set(new Map());
      this.targetLocation.set({ kind: 'unplaced' });
      this.collapsing.set(false);
    } else if (state === 'closed') {
      this.closed.emit();
    }
  }
}
