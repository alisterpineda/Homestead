import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  inject,
  signal,
  computed,
  input,
  output,
} from '@angular/core';
import { Router } from '@angular/router';
import { NgTemplateOutlet } from '@angular/common';
import { type Observable } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePackage,
  lucideListFilter,
  lucideArrowUp,
  lucideArrowDown,
  lucideLayoutGrid,
  lucideTable,
  lucideEllipsisVertical,
  lucideCheck,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroupImports } from '@/shared/components/spartan/button-group';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { InventoryTableComponent } from './inventory-table.component';
import { InventoryCardGridComponent } from './inventory-card-grid.component';
import { EditDurableSheetComponent } from '../durables/edit-durable-sheet/edit-durable-sheet.component';
import { EditConsumableSheetComponent } from '../consumables/edit-consumable-sheet/edit-consumable-sheet.component';
import { isTerminalStatus } from '../shared/item-status-meta';
import { WARRANTY_FACETS, type WarrantyFacet } from '../shared/warranties/warranty-status-meta';
import { injectIsNarrowViewport } from '@/shared/utils/viewport';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { sortRows, type InventoryRow, type InventorySortId, type InventorySortState } from './inventory-row';
import { replace } from '@/shared/json-patch';
import { trimToNull } from '@/shared/utils/trim-to-null';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { VisibilityState } from '@tanstack/angular-table';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface InventorySortFieldOption {
  readonly id: InventorySortId;
  readonly label: string;
  readonly defaultDesc: boolean;
}

/** One-tap presets over the row kind: everything, only durables, or only consumables. */
export type KindFilter = 'all' | 'durables' | 'consumables';
export const isKindFilter = (v: unknown): v is KindFilter =>
  v === 'all' || v === 'durables' || v === 'consumables';
export const kindFilterFromParam = (v: string | null): KindFilter => (isKindFilter(v) ? v : 'all');

/**
 * The interactive items surface shared by the standalone Items list and the Browse drill-down: the
 * refine toolbar (kind toggle + refine popover + sort dropdown + view toggle), the rows themselves
 * rendered as either a table or a card grid, and the row-level edit sheets / delete-confirm dialog
 * plus their archive/restore/delete mutations.
 *
 * The renderer follows a persisted three-state preference (`table` / `card` / unset); unset resolves
 * to cards below 768px and the table above, so phones get the card grid without anyone opting in.
 * Below `sm` the sort dropdown and the view toggle collapse into a `⋮` overflow menu (the kind
 * toggle and Filter stay inline at every width) — the shared toolbar pattern from
 * `docs/product-design/shared/table-ux.md`.
 *
 * The parent supplies `rows` already place-scoped (area filter, container drill, place contents);
 * this component owns only the non-place refinements — kind, show-inactive, low-stock, lent, sort —
 * and runs `rows → kindRows → visibleRows → sortedVisibleRows`. Quick panels stay page-hosted: a row
 * click emits `openDurable`/`openConsumable` rather than opening a panel here, since Browse also opens
 * those panels from its "Recently updated" strip and a second internal pair would duplicate them.
 * `viewContents` bubbles up so the parent picks drill-vs-navigate; `viewDetails` is identical
 * everywhere and handled internally. Mutations emit `changed` so the parent refetches.
 */
@Component({
  selector: 'app-inventory-items-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmButtonGroupImports,
    HlmToggleGroupImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmInput,
    HlmPopoverImports,
    HlmSeparatorImports,
    HlmDropdownMenuImports,
    InventoryTableComponent,
    InventoryCardGridComponent,
    EditDurableSheetComponent,
    EditConsumableSheetComponent,
  ],
  providers: [provideIcons({
    lucidePackage,
    lucideListFilter,
    lucideArrowUp,
    lucideArrowDown,
    lucideLayoutGrid,
    lucideTable,
    lucideEllipsisVertical,
    lucideCheck,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col gap-4' },
  template: `
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex flex-wrap items-center gap-2">
        <ng-content select="[toolbarStart]" />
        <hlm-toggle-group
          type="single"
          nullable="false"
          variant="outline"
          aria-label="Kind filter"
          [value]="kind()"
          (valueChange)="onKindFilterChange($event)"
        >
          <button hlmToggleGroupItem value="all">All</button>
          <button hlmToggleGroupItem value="durables">Durables</button>
          <button hlmToggleGroupItem value="consumables">Consumables</button>
        </hlm-toggle-group>

        <div hlmPopover>
          <button hlmPopoverTrigger hlmBtn variant="outline">
            <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
            {{ filterLabel() }}
          </button>
          <div hlmPopoverContent *hlmPopoverPortal class="max-h-[min(70vh,32rem)] w-[260px] overflow-y-auto p-0" align="start">
            <div class="flex items-center justify-between px-3 py-2">
              <span class="text-sm font-medium">Filters</span>
              @if (showInactive() || lowStockOnly() || lentOnly() || warrantyFilter() !== 'any' || extraFiltersActive()) {
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  (click)="onReset()"
                >Reset</button>
              }
            </div>
            <span hlmSeparator></span>
            <div class="space-y-1 p-2">
              @if (showInactiveFilter()) {
                <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                  <hlm-checkbox
                    [checked]="showInactive()"
                    (checkedChange)="showInactive.set($event === true)"
                    [aria-label]="'Show inactive'"
                  />
                  <span>Show inactive ({{ inactiveCount() }})</span>
                </label>
              }
              <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                <hlm-checkbox
                  [checked]="lowStockOnly()"
                  (checkedChange)="lowStockOnly.set($event === true)"
                  [aria-label]="'Low stock only'"
                />
                <span>Low stock only ({{ lowCount() }})</span>
              </label>
              <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                <hlm-checkbox
                  [checked]="lentOnly()"
                  (checkedChange)="lentOnly.set($event === true)"
                  [aria-label]="'Lent only'"
                />
                <span>Lent only ({{ lentCount() }})</span>
              </label>
            </div>
            <!-- Warranty is single-choice (the states are mutually exclusive), so it reads as a
                 radio-style group of rows rather than four more checkboxes. -->
            <span hlmSeparator></span>
            <div class="p-1">
              <span class="block px-2 py-1 text-xs font-medium text-muted-foreground">Warranty</span>
              @for (facet of warrantyFacets; track facet.value) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  (click)="warrantyFilter.set(facet.value)"
                >
                  <span>{{ facet.label }} ({{ warrantyCounts()[facet.value] }})</span>
                  @if (warrantyFilter() === facet.value) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                </button>
              }
            </div>
            <ng-content select="[refineExtra]" />
          </div>
        </div>

        <button hlmBtn variant="outline" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="sortMenu">
          <ng-icon hlm [name]="sort().desc ? 'lucideArrowDown' : 'lucideArrowUp'" size="sm" />
          {{ sortFieldLabel() }}
        </button>
        <ng-template #sortGroup>
          <span hlmDropdownMenuLabel>Sort by</span>
          <hlm-dropdown-menu-group>
            @for (opt of sortFields(); track opt.id) {
              <button
                hlmDropdownMenuRadio
                [checked]="sort().id === opt.id"
                (triggered)="onSortByChange(opt.id)"
              >
                <hlm-dropdown-menu-radio-indicator />
                {{ opt.label }}
              </button>
            }
          </hlm-dropdown-menu-group>
          <hlm-dropdown-menu-separator />
          <span hlmDropdownMenuLabel>Direction</span>
          <hlm-dropdown-menu-group>
            <button hlmDropdownMenuRadio [checked]="!sort().desc" (triggered)="onSortDirChange(false)">
              <hlm-dropdown-menu-radio-indicator />
              Ascending
            </button>
            <button hlmDropdownMenuRadio [checked]="sort().desc" (triggered)="onSortDirChange(true)">
              <hlm-dropdown-menu-radio-indicator />
              Descending
            </button>
          </hlm-dropdown-menu-group>
        </ng-template>
        <ng-template #sortMenu>
          <hlm-dropdown-menu class="w-44">
            <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
          </hlm-dropdown-menu>
        </ng-template>

        @if (showViewToggle()) {
          <div hlmButtonGroup class="hidden sm:flex" aria-label="View mode">
            <button
              hlmBtn
              [variant]="viewMode() === 'card' ? 'secondary' : 'outline'"
              size="icon"
              (click)="setViewMode('card')"
              aria-label="Card view"
            >
              <ng-icon hlm name="lucideLayoutGrid" size="sm" />
            </button>
            <button
              hlmBtn
              [variant]="viewMode() === 'table' ? 'secondary' : 'outline'"
              size="icon"
              (click)="setViewMode('table')"
              aria-label="Table view"
            >
              <ng-icon hlm name="lucideTable" size="sm" />
            </button>
          </div>
        }

        <button hlmBtn variant="outline" size="icon" class="sm:hidden"
                [hlmDropdownMenuTrigger]="overflowMenu" aria-label="More options">
          <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
        </button>
        <ng-template #overflowMenu>
          <hlm-dropdown-menu class="w-44">
            @if (showViewToggle()) {
              <span hlmDropdownMenuLabel>View</span>
              <hlm-dropdown-menu-group>
                <button hlmDropdownMenuRadio [checked]="viewMode() === 'card'"
                        (triggered)="setViewMode('card')">
                  <hlm-dropdown-menu-radio-indicator />
                  Card
                </button>
                <button hlmDropdownMenuRadio [checked]="viewMode() === 'table'"
                        (triggered)="setViewMode('table')">
                  <hlm-dropdown-menu-radio-indicator />
                  Table
                </button>
              </hlm-dropdown-menu-group>
              <hlm-dropdown-menu-separator />
            }
            <button hlmDropdownMenuItem [hlmDropdownMenuTrigger]="sortSubMenu">
              Sort
              <hlm-dropdown-menu-item-sub-indicator />
            </button>
            <ng-template #sortSubMenu>
              <hlm-dropdown-menu-sub>
                <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
              </hlm-dropdown-menu-sub>
            </ng-template>
          </hlm-dropdown-menu>
        </ng-template>
      </div>
      <ng-content select="[toolbarEnd]" />
    </div>

    @if (loading() && sortedVisibleRows().length === 0) {
      <div class="flex justify-center py-16">
        <hlm-spinner />
      </div>
    } @else if (sortedVisibleRows().length === 0) {
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucidePackage" />
          </div>
          <div hlmEmptyTitle>Nothing matches</div>
          <div hlmEmptyDescription>No items match the current filters.</div>
        </div>
      </div>
    } @else if (viewMode() === 'table') {
      <app-inventory-table
        [rows]="sortedVisibleRows()"
        [sortState]="sort()"
        [columnVisibility]="columnVisibility()"
        (sortChange)="sort.set($event)"
        (open)="onOpen($event)"
        (edit)="onEdit($event)"
        (viewContents)="viewContents.emit($event)"
        (viewDetails)="onViewDetails($event)"
        (archiveRestore)="onArchiveRestore($event)"
        (delete)="onDeleteRequest($event)"
      />
    } @else {
      <app-inventory-card-grid
        [rows]="sortedVisibleRows()"
        [columnVisibility]="columnVisibility()"
        (open)="onOpen($event)"
        (edit)="onEdit($event)"
        (viewContents)="viewContents.emit($event)"
        (viewDetails)="onViewDetails($event)"
        (archiveRestore)="onArchiveRestore($event)"
        (delete)="onDeleteRequest($event)"
      />
    }

    <app-edit-durable-sheet
      [durable]="selectedDurable()"
      [state]="durableSheetState()"
      (saved)="onDurableSheetSaved()"
      (closed)="onDurableSheetClosed()"
    />

    <app-edit-consumable-sheet
      [consumable]="selectedConsumable()"
      [state]="consumableSheetState()"
      (saved)="onConsumableSheetSaved()"
      (closed)="onConsumableSheetClosed()"
    />

    <hlm-alert-dialog [state]="archiveState()" (stateChanged)="archiveState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Archive {{ pendingArchive()?.name ?? 'entry' }}?</h2>
          <p hlmAlertDialogDescription>
            @if (pendingArchive()?.kind === 'consumable') {
              The consumable and its stock history are kept, but it leaves the default list. You can restore it later.
            } @else {
              The durable is kept in records, but it leaves the default list. Containers with live contents cannot be archived. You can restore it later.
            }
          </p>
        </div>
        <div class="grid gap-2">
          <label hlmLabel for="archive-note">Note (optional)</label>
          <input
            hlmInput
            id="archive-note"
            class="w-full"
            maxlength="256"
            placeholder="e.g. Sold to Sam"
            [value]="archiveNote()"
            (input)="archiveNote.set($any($event.target).value)"
          />
        </div>
        @if (archiveError()) {
          <p class="text-sm text-destructive">{{ archiveError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction (click)="confirmArchive(ctx)" [disabled]="archiving()">
            {{ archiving() ? 'Archiving...' : 'Archive' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'entry' }}?</h2>
          <p hlmAlertDialogDescription>
            @if (pendingDelete()?.kind === 'consumable') {
              This permanently removes the consumable. Consumables with stock history or notes cannot be deleted — archive them instead.
            } @else {
              This permanently removes the durable. Durables with contents, stock, or notes cannot be deleted — archive them instead.
            }
          </p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button
            hlmAlertDialogAction
            variant="destructive"
            (click)="confirmDelete(ctx)"
            [disabled]="deleting()"
          >
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class InventoryItemsViewComponent implements OnInit {
  private durableApi = inject(InventoryDurableApiService);
  private consumableApi = inject(InventoryConsumableApiService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);
  private prefs = inject(UserPreferencesService);
  private readonly narrow = injectIsNarrowViewport();

  private readonly allSortFields: readonly InventorySortFieldOption[] = [
    { id: 'name', label: 'Name', defaultDesc: false },
    { id: 'location', label: 'Location', defaultDesc: false },
    { id: 'onHand', label: 'On hand', defaultDesc: true },
    { id: 'updatedAt', label: 'Updated', defaultDesc: true },
  ];
  // Don't offer a hidden column as a sort option (Browse hides Location, where it'd sort nothing
  // visible). Sort id == column id for every sortable column, so columnVisibility filters directly;
  // on the Items page (no hidden columns) this is the full list, unchanged.
  sortFields = computed(() =>
    this.allSortFields.filter(f => this.columnVisibility()[f.id] !== false));

  /** Place-scoped rows from the parent (area filter / drill / place contents), before the refinements. */
  rows = input.required<InventoryRow[]>();
  /** One-shot deep-link seeds — applied once in ngOnInit, never re-applied (no linkedSignal clobber). */
  initialKind = input<KindFilter>('all');
  initialShowInactive = input(false);
  initialLowStockOnly = input(false);
  initialLentOnly = input(false);
  initialWarranty = input<WarrantyFacet>('any');
  initialSort = input<InventorySortState>({ id: 'name', desc: false });
  /** Browse passes false: place contents carry only live statuses, so the toggle would be dead. */
  showInactiveFilter = input(true);
  /** Parent-owned scope filters (area/container/search) so Reset shows whenever any filter is active. */
  extraFiltersActive = input(false);
  /** Higher-precedence trigger label for the parent's scope (e.g. "In: Tool chest"); null → own label. */
  scopeLabel = input<string | null>(null);
  /** Parent is mid-fetch (e.g. a container drill): show the spinner instead of "Nothing matches". */
  loading = input(false);
  /** Per-column visibility passed straight to the table/card grid — Browse hides Location. */
  columnVisibility = input<VisibilityState>({});
  /** Browse passes false — the drilled list inherits the pref but shows no second toggle. */
  showViewToggle = input(true);

  changed = output<void>();
  openDurable = output<DurableResponse>();
  openConsumable = output<ConsumableResponse>();
  /** A container row's "View contents" — the parent decides drill (list) vs navigate (Browse). */
  viewContents = output<InventoryRow>();
  /** Reset pressed — the parent clears its own scope (search/area/container). */
  reset = output<void>();

  kind = signal<KindFilter>('all');
  showInactive = signal(false);
  lowStockOnly = signal(false);
  /** Lent-only lens: rows whose status is Lent. */
  lentOnly = signal(false);
  /** Single-choice warranty lens over the durable rows' server-derived status; 'any' = off. */
  warrantyFilter = signal<WarrantyFacet>('any');
  protected readonly warrantyFacets = WARRANTY_FACETS;
  sort = signal<InventorySortState>({ id: 'name', desc: false });

  selectedDurable = signal<DurableResponse | null>(null);
  durableSheetState = signal<BrnDialogState>('closed');
  selectedConsumable = signal<ConsumableResponse | null>(null);
  consumableSheetState = signal<BrnDialogState>('closed');

  pendingDelete = signal<InventoryRow | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  deleteError = signal('');

  pendingArchive = signal<InventoryRow | null>(null);
  archiveState = signal<BrnDialogState>('closed');
  archiving = signal(false);
  archiveError = signal('');
  /** Optional reason typed into the archive confirm dialog; reset each time it opens. */
  archiveNote = signal('');

  // Seed the filter signals from the initial* inputs once. ngOnInit (not the constructor) because
  // signal inputs aren't bound until the first change detection; running here is synchronous and
  // pre-render, so a deep-link lands without a flash, and being one-shot it never clobbers a later
  // user toggle (the documented linkedSignal re-seed hazard).
  ngOnInit(): void {
    this.kind.set(this.initialKind());
    this.showInactive.set(this.initialShowInactive());
    this.lowStockOnly.set(this.initialLowStockOnly());
    this.lentOnly.set(this.initialLentOnly());
    this.warrantyFilter.set(this.initialWarranty());
    this.sort.set(this.initialSort());
  }

  private kindRows = computed(() => {
    const kf = this.kind();
    if (kf === 'all') return this.rows();
    const kind = kf === 'durables' ? 'durable' : 'consumable';
    return this.rows().filter(r => r.kind === kind);
  });

  inactiveCount = computed(() => this.kindRows().filter(r => isTerminalStatus(r.status)).length);
  /**
   * The base every in-popover count is taken over: the kind-filtered rows minus the ones the
   * inactive toggle is currently hiding. A count that ignored that narrowing would promise rows the
   * list cannot show — "None (44)" beside a list that yields 35 — so every filter's badge is
   * measured against the same visible-if-you-pick-this set.
   */
  private countableRows = computed(() => {
    const rows = this.kindRows();
    return this.showInactive() ? rows : rows.filter(r => !isTerminalStatus(r.status));
  });

  // Counts what the low-stock filter would actually show: archived low consumables count only while
  // "Show inactive" reveals them.
  lowCount = computed(() => this.countableRows().filter(r => r.isLow).length);
  // Lent is a live status, so the inactive narrowing above can never remove one of these rows.
  lentCount = computed(() => this.countableRows().filter(r => r.status === 'Lent').length);
  /**
   * How many rows each warranty facet would show. Durables only — a consumable carries no warranty
   * status, so it can never satisfy any facet but "Any" (which counts the whole countable set, so
   * the badge beside it matches the list you get by clearing the facet).
   */
  warrantyCounts = computed<Record<WarrantyFacet, number>>(() => {
    const rows = this.countableRows();
    const counts = { any: rows.length, active: 0, expiring: 0, expired: 0, none: 0 } as Record<WarrantyFacet, number>;
    for (const facet of WARRANTY_FACETS) {
      if (!facet.status) continue;
      counts[facet.value] = rows.filter(r => r.warrantyStatus === facet.status).length;
    }
    return counts;
  });

  visibleRows = computed(() => {
    let rows = this.kindRows();
    if (!this.showInactive()) rows = rows.filter(r => !isTerminalStatus(r.status));
    // Only consumables carry a low flag, so this doubles as the reorder view.
    if (this.lowStockOnly()) rows = rows.filter(r => r.isLow);
    if (this.lentOnly()) rows = rows.filter(r => r.status === 'Lent');
    // Warranties are durables-only, so any facet but 'any' drops every consumable by construction.
    const warranty = WARRANTY_FACETS.find(f => f.value === this.warrantyFilter())?.status ?? null;
    if (warranty) rows = rows.filter(r => r.warrantyStatus === warranty);
    return rows;
  });

  sortedVisibleRows = computed(() => sortRows(this.visibleRows(), this.sort()));

  // Three-state preference: an explicit table/card choice wins everywhere; unset falls back to the
  // viewport, so phones get cards and desktops the table without anyone having to pick.
  viewMode = computed<'table' | 'card'>(() => {
    const stored = this.prefs.inventoryItemViewMode();
    if (stored === 'table' || stored === 'card') return stored;
    return this.narrow() ? 'card' : 'table';
  });

  setViewMode(mode: 'table' | 'card'): void {
    this.prefs.inventoryItemViewMode.set(mode);
  }

  sortFieldLabel = computed(() =>
    this.allSortFields.find(f => f.id === this.sort().id)?.label ?? 'Sort');

  filterLabel = computed(() => {
    const scope = this.scopeLabel();
    if (scope) return scope;
    if (this.lowStockOnly()) return 'Filter: Low stock';
    if (this.lentOnly()) return 'Filter: Lent';
    // The facet *value* ('expiring') reads better in the trigger than its menu label ('Expiring soon').
    const warranty = this.warrantyFilter();
    if (warranty !== 'any') return `Filter: Warranty ${warranty}`;
    return this.showInactive() ? 'Filter: Inactive' : 'Filter';
  });

  onKindFilterChange(value: unknown): void {
    if (isKindFilter(value)) this.kind.set(value);
  }

  onSortByChange(id: InventorySortId): void {
    const field = this.allSortFields.find(f => f.id === id);
    this.sort.set({ id, desc: field?.defaultDesc ?? false });
  }

  onSortDirChange(desc: boolean): void {
    this.sort.update(prev => ({ id: prev.id, desc }));
  }

  // Resets only the refinements this component owns; the parent clears its scope (search/area/
  // container) in its `reset` handler. Mirrors the old resetFilters, which left the kind toggle alone.
  onReset(): void {
    this.showInactive.set(false);
    this.lowStockOnly.set(false);
    this.lentOnly.set(false);
    this.warrantyFilter.set('any');
    this.reset.emit();
  }

  onOpen(row: InventoryRow): void {
    if (row.kind === 'consumable' && row.consumable) {
      this.openConsumable.emit(row.consumable);
    } else if (row.durable) {
      this.openDurable.emit(row.durable);
    }
  }

  onEdit(row: InventoryRow): void {
    if (row.kind === 'consumable' && row.consumable) {
      this.selectedConsumable.set(row.consumable);
      this.consumableSheetState.set('open');
    } else if (row.durable) {
      this.selectedDurable.set(row.durable);
      this.durableSheetState.set('open');
    }
  }

  onViewDetails(row: InventoryRow): void {
    void this.router.navigate(['/inventory/items', row.id]);
  }

  onDurableSheetSaved(): void {
    this.durableSheetState.set('closed');
    this.changed.emit();
  }

  onDurableSheetClosed(): void {
    this.durableSheetState.set('closed');
    this.selectedDurable.set(null);
  }

  onConsumableSheetSaved(): void {
    this.consumableSheetState.set('closed');
    this.changed.emit();
  }

  onConsumableSheetClosed(): void {
    this.consumableSheetState.set('closed');
    this.selectedConsumable.set(null);
  }

  /** Restore is immediate; archive asks first (it hides the item and can be blocked by live contents). */
  onArchiveRestore(row: InventoryRow): void {
    if (isTerminalStatus(row.status)) {
      this.setStatus(row, 'Active');
      return;
    }
    this.pendingArchive.set(row);
    this.archiveNote.set('');
    this.archiveError.set('');
    this.archiveState.set('open');
  }

  confirmArchive(ctx: { close: () => void }): void {
    const row = this.pendingArchive();
    if (!row) return;
    this.setStatus(row, 'Archived', ctx);
  }

  private setStatus(row: InventoryRow, newStatus: 'Active' | 'Archived', ctx?: { close: () => void }): void {
    const isConsumable = row.kind === 'consumable';
    const noun = isConsumable ? 'Consumable' : 'Durable';
    this.archiving.set(true);
    this.archiveError.set('');
    // Archiving may carry an optional reason; restoring clears the note server-side, so it sends
    // only the status op.
    const body = newStatus === 'Archived'
      ? [replace('/status', newStatus), replace('/statusNote', trimToNull(this.archiveNote()))]
      : [replace('/status', newStatus)];
    const request$: Observable<unknown> = isConsumable
      ? this.consumableApi.patchConsumable({ id: row.id, body })
      : this.durableApi.patchDurable({ id: row.id, body });
    request$.subscribe({
      next: () => {
        this.archiving.set(false);
        ctx?.close();
        this.pendingArchive.set(null);
        toast.success(newStatus === 'Active' ? `${noun} restored.` : `${noun} archived.`);
        this.changed.emit();
      },
      // With the confirm dialog open, the rejection reason ("container has live contents") is shown
      // inline where the user is looking; a restore has no dialog, so it toasts like other mutations.
      error: (err: unknown) => {
        this.archiving.set(false);
        const message = this.errorHandler.getErrorMessage(err, `Failed to update ${noun.toLowerCase()}.`);
        if (ctx) this.archiveError.set(message);
        else if (!this.errorHandler.isServerOrNetworkError(err)) toast.error(message);
      },
    });
  }

  onDeleteRequest(row: InventoryRow): void {
    this.pendingDelete.set(row);
    this.deleteError.set('');
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const row = this.pendingDelete();
    if (!row) return;
    const isConsumable = row.kind === 'consumable';
    this.deleting.set(true);
    this.deleteError.set('');
    const request$ = isConsumable
      ? this.consumableApi.deleteConsumable({ id: row.id })
      : this.durableApi.deleteDurable({ id: row.id });
    request$.subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        this.pendingDelete.set(null);
        this.changed.emit();
        toast.success(isConsumable ? 'Consumable deleted.' : 'Durable deleted.');
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection
      // reason ("has stock history — archive instead") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(
          this.errorHandler.getErrorMessage(err, `Failed to delete ${isConsumable ? 'consumable' : 'durable'}.`));
      },
    });
  }
}
