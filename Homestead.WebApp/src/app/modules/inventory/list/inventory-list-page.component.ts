import {
  Component,
  ChangeDetectionStrategy,
  DestroyRef,
  inject,
  signal,
  computed,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import {
  switchMap,
  map,
  of,
  catchError,
  forkJoin,
  debounceTime,
  filter,
  type Observable,
} from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucidePackage,
  lucideCircleAlert,
  lucideListFilter,
  lucideArrowUp,
  lucideArrowDown,
  lucideCheck,
  lucideChevronRight,
  lucideSearch,
  lucideX,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { InventoryTableComponent, type InventorySortId, type InventorySortState } from './inventory-table.component';
import { CreateEntryDialogComponent } from './create-entry-dialog/create-entry-dialog.component';
import { EditItemSheetComponent } from '../items/edit-item-sheet/edit-item-sheet.component';
import { ItemQuickPanelComponent } from '../items/item-quick-panel/item-quick-panel.component';
import { EditSupplySheetComponent } from '../supplies/edit-supply-sheet/edit-supply-sheet.component';
import { SupplyQuickPanelComponent } from '../supplies/supply-quick-panel/supply-quick-panel.component';
import { itemStatusLabel, isTerminalStatus } from '../shared/item-status-meta';
import { itemToRow, supplyToRow, supplyToDrillRow, type InventoryRow } from './inventory-row';
import { orderAreaTree, buildDescendantMap, type AreaTreeNode } from '../shared/area-tree';
import { replace } from '@/shared/json-patch';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { LocationSegmentKind } from '@/api/generated/models/location-segment-kind';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface InventorySortFieldOption {
  readonly id: InventorySortId;
  readonly label: string;
  readonly defaultDesc: boolean;
}

/** 'all' | 'unplaced' | an area id. */
type AreaFilter = string;

/** One-tap presets over the row kind: everything, only items, or only supplies. */
type KindFilter = 'all' | 'items' | 'supplies';
const isKindFilter = (v: unknown): v is KindFilter => v === 'all' || v === 'items' || v === 'supplies';
const kindFilterFromParam = (v: string | null): KindFilter => (isKindFilter(v) ? v : 'all');

/**
 * Reads the named deep-link query params once and immediately strips them from the URL
 * (merge-null + replaceUrl, so ?search survives) — later merge navigations (the search debounce)
 * would otherwise keep re-applying stale values. Reading and stripping share the one name list,
 * so a param can't be seeded but left lingering: register a new deep-link param only by adding
 * its name at the call site.
 */
function seedDeepLinkParams(
  route: ActivatedRoute,
  router: Router,
  names: readonly string[],
): Record<string, string | null> {
  const params = route.snapshot.queryParamMap;
  if (names.some(n => params.has(n))) {
    void router.navigate([], {
      relativeTo: route,
      queryParams: Object.fromEntries(names.map(n => [n, null])),
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
  return Object.fromEntries(names.map(n => [n, params.get(n)]));
}

/** Both lists fetched together; null until the first load resolves. */
interface ListData {
  items: ItemResponse[];
  supplies: SupplyResponse[];
  /** The search term this payload was fetched with — lets consumers detect stale data mid-search. */
  term: string;
}

/** Server-fetched payload for a container drill-down: the container itself plus its direct children. */
interface DrillData {
  container: ItemResponse | null;
  children: ItemResponse[];
}

const EMPTY_DRILL: DrillData = { container: null, children: [] };

function compareNullable<T>(av: T | null, bv: T | null, dir: 1 | -1, lt: (a: T, b: T) => number): number {
  if (av === bv) return 0;
  if (av === null) return 1;
  if (bv === null) return -1;
  return lt(av, bv) * dir;
}

@Component({
  selector: 'app-inventory-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmInput,
    HlmToggleGroupImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmPopoverImports,
    HlmSeparatorImports,
    HlmDropdownMenuImports,
    InventoryTableComponent,
    CreateEntryDialogComponent,
    EditItemSheetComponent,
    ItemQuickPanelComponent,
    EditSupplySheetComponent,
    SupplyQuickPanelComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucidePackage,
    lucideCircleAlert,
    lucideListFilter,
    lucideArrowUp,
    lucideArrowDown,
    lucideCheck,
    lucideChevronRight,
    lucideSearch,
    lucideX,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Failed to load items</div>
            <div hlmEmptyDescription>There was an error loading your items. Please try again.</div>
          </div>
        </div>
      }

      @if (data() !== null) {
        @if (allItems().length === 0 && allSupplies().length === 0 && !searchTerm()) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucidePackage" />
              </div>
              <div hlmEmptyTitle>No items yet</div>
              <div hlmEmptyDescription>Add your first item or supply to start tracking what you own, where it lives, and how much is left.</div>
            </div>
            <div hlmEmptyContent>
              <app-create-entry-dialog (created)="onChanged()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New
              </app-create-entry-dialog>
            </div>
          </div>
        } @else {
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="flex flex-wrap items-center gap-2">
              <div class="relative w-full min-w-0 sm:w-56 lg:w-64">
                <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
                <input
                  hlmInput
                  placeholder="Search items..."
                  class="h-8 w-full min-w-0 pl-8"
                  aria-label="Search items and supplies"
                  [value]="searchInput()"
                  (input)="searchInput.set($any($event.target).value)"
                />
              </div>
              <hlm-toggle-group
                type="single"
                variant="outline"
                size="sm"
                aria-label="Kind filter"
                [value]="kindFilter()"
                (valueChange)="onKindFilterChange($event)"
              >
                <button hlmToggleGroupItem value="all">All</button>
                <button hlmToggleGroupItem value="items">Items</button>
                <button hlmToggleGroupItem value="supplies">Supplies</button>
              </hlm-toggle-group>

              <div hlmPopover>
                <button hlmPopoverTrigger hlmBtn variant="outline" size="sm">
                  <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
                  {{ filterLabel() }}
                </button>
                <div hlmPopoverContent *hlmPopoverPortal class="w-[260px] p-0" align="start">
                  <div class="flex items-center justify-between px-3 py-2">
                    <span class="text-sm font-medium">Filters</span>
                    @if (showInactive() || lowStockOnly() || lentOnly() || areaFilter() !== 'all' || containerFilter() || searchTerm()) {
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        class="h-auto px-2 py-0.5 text-xs"
                        (click)="resetFilters()"
                      >Reset</button>
                    }
                  </div>
                  <span hlmSeparator></span>
                  <div class="space-y-1 p-2">
                    <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                      <hlm-checkbox
                        [checked]="showInactive()"
                        (checkedChange)="showInactive.set($event === true)"
                        [aria-label]="'Show inactive'"
                      />
                      <span>Show inactive ({{ inactiveCount() }})</span>
                    </label>
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
                  <span hlmSeparator></span>
                  <div class="max-h-[240px] overflow-y-auto p-1">
                    <span class="block px-2 py-1 text-xs font-medium text-muted-foreground">Area</span>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                      (click)="selectAreaFilter('all')"
                    >
                      <span>All areas</span>
                      @if (areaFilter() === 'all' && !containerFilter()) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                    </button>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                      (click)="selectAreaFilter('unplaced')"
                    >
                      <span class="italic text-muted-foreground">Unplaced</span>
                      @if (areaFilter() === 'unplaced' && !containerFilter()) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                    </button>
                    @for (node of areaTree(); track node.area.id) {
                      <button
                        type="button"
                        class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                        (click)="selectAreaFilter(node.area.id)"
                      >
                        <span class="truncate" [style.marginInlineStart.px]="node.depth * 14">{{ node.area.name }}</span>
                        @if (areaFilter() === node.area.id && !containerFilter()) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                      </button>
                    }
                  </div>
                </div>
              </div>

              <button hlmBtn variant="outline" size="sm" [hlmDropdownMenuTrigger]="sortMenu">
                <ng-icon hlm [name]="sortState().desc ? 'lucideArrowDown' : 'lucideArrowUp'" size="sm" />
                {{ sortFieldLabel() }}
              </button>
              <ng-template #sortMenu>
                <hlm-dropdown-menu class="w-44">
                  <span hlmDropdownMenuLabel>Sort by</span>
                  <hlm-dropdown-menu-group>
                    @for (opt of sortFields; track opt.id) {
                      <button
                        hlmDropdownMenuRadio
                        [checked]="sortState().id === opt.id"
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
                    <button hlmDropdownMenuRadio [checked]="!sortState().desc" (triggered)="onSortDirChange(false)">
                      <hlm-dropdown-menu-radio-indicator />
                      Ascending
                    </button>
                    <button hlmDropdownMenuRadio [checked]="sortState().desc" (triggered)="onSortDirChange(true)">
                      <hlm-dropdown-menu-radio-indicator />
                      Descending
                    </button>
                  </hlm-dropdown-menu-group>
                </hlm-dropdown-menu>
              </ng-template>
            </div>
            <app-create-entry-dialog triggerSize="sm" (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New
            </app-create-entry-dialog>
          </div>

          @if (containerFilter()) {
            <div class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground">
              @for (seg of breadcrumb(); track seg.id; let last = $last) {
                @if (last) {
                  <span class="font-medium text-foreground">{{ seg.name }}</span>
                } @else {
                  <button
                    type="button"
                    class="hover:text-foreground hover:underline"
                    (click)="onBreadcrumbSegment(seg)"
                  >{{ seg.name }}</button>
                  <ng-icon hlm name="lucideChevronRight" size="xs" class="shrink-0" />
                }
              } @empty {
                <span class="italic">Container contents</span>
              }
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                class="ml-1 size-6"
                aria-label="Exit container view"
                (click)="clearContainerFilter()"
              >
                <ng-icon hlm name="lucideX" size="sm" />
              </button>
            </div>
          }

          @if (drillLoading() && sortedVisibleRows().length === 0) {
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
                <div hlmEmptyDescription>No items or supplies match the current filters.</div>
              </div>
            </div>
          } @else {
            <app-inventory-table
              [rows]="sortedVisibleRows()"
              [sortState]="sortState()"
              (sortChange)="sortState.set($event)"
              (open)="onOpen($event)"
              (edit)="onEdit($event)"
              (viewContents)="onViewContents($event)"
              (viewDetails)="onViewDetails($event)"
              (archiveRestore)="onArchiveRestore($event)"
              (delete)="onDeleteRequest($event)"
            />
          }
        }
      }
    </div>

    <app-item-quick-panel
      [item]="quickPanelItem()"
      [state]="itemQuickPanelState()"
      (changed)="onChanged()"
      (closed)="onItemQuickPanelClosed()"
      (viewContents)="onQuickPanelViewContents($event)"
    />

    <app-supply-quick-panel
      [supply]="quickPanelSupply()"
      [state]="supplyQuickPanelState()"
      (changed)="onChanged()"
      (closed)="onSupplyQuickPanelClosed()"
    />

    <app-edit-item-sheet
      [item]="selectedItem()"
      [state]="itemSheetState()"
      (saved)="onItemSheetSaved()"
      (closed)="onItemSheetClosed()"
    />

    <app-edit-supply-sheet
      [supply]="selectedSupply()"
      [state]="supplySheetState()"
      (saved)="onSupplySheetSaved()"
      (closed)="onSupplySheetClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'entry' }}?</h2>
          <p hlmAlertDialogDescription>
            @if (pendingDelete()?.kind === 'supply') {
              This permanently removes the supply. Supplies with stock history cannot be deleted — archive them instead.
            } @else {
              This permanently removes the item. Items with contents cannot be deleted — archive them instead.
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
export class InventoryListPageComponent {
  private itemApi = inject(InventoryItemApiService);
  private supplyApi = inject(InventorySupplyApiService);
  private areaApi = inject(InventoryAreaApiService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  readonly sortFields: readonly InventorySortFieldOption[] = [
    { id: 'name', label: 'Name', defaultDesc: false },
    { id: 'status', label: 'Status', defaultDesc: false },
    { id: 'location', label: 'Location', defaultDesc: false },
    { id: 'onHand', label: 'On hand', defaultDesc: true },
    { id: 'updatedAt', label: 'Updated', defaultDesc: true },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  drillLoading = signal(false);
  // Deep-link params from the overview tiles and place browser, seeded once into the filter
  // signals below and stripped from the URL (read-once, not two-way synced — that's the lenses
  // feature). When both ?area= and ?container= arrive, the container drill wins via scopedRows'
  // existing precedence — no extra handling needed here.
  private deepLink = seedDeepLinkParams(this.route, this.router, ['lowStock', 'lent', 'kind', 'area', 'container']);

  showInactive = signal(false);
  lowStockOnly = signal(this.deepLink['lowStock'] === 'true');
  /** Lent-only lens: rows whose status is Lent. */
  lentOnly = signal(this.deepLink['lent'] === 'true');
  kindFilter = signal<KindFilter>(kindFilterFromParam(this.deepLink['kind']));
  areaFilter = signal<AreaFilter>(this.deepLink['area'] ?? 'all');
  /** When set, the list is scoped to the direct contents of this container (drill-down). */
  containerFilter = signal<string | null>(this.deepLink['container'] ?? null);

  /** What the user is typing (immediate); synced to the URL after a 300ms debounce. */
  searchInput = signal((this.route.snapshot.queryParamMap.get('search') ?? '').trim());
  /** The committed (trimmed) search term from the URL — the source of truth for the server fetch. */
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => (p.get('search') ?? '').trim())),
    { initialValue: this.searchInput() },
  );

  sortState = signal<InventorySortState>({ id: 'name', desc: false });

  selectedItem = signal<ItemResponse | null>(null);
  itemSheetState = signal<BrnDialogState>('closed');
  selectedSupply = signal<SupplyResponse | null>(null);
  supplySheetState = signal<BrnDialogState>('closed');

  quickPanelItem = signal<ItemResponse | null>(null);
  itemQuickPanelState = signal<BrnDialogState>('closed');
  quickPanelSupply = signal<SupplyResponse | null>(null);
  supplyQuickPanelState = signal<BrnDialogState>('closed');

  pendingDelete = signal<InventoryRow | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  deleteError = signal('');
  archiving = signal(false);

  // Server-side search: matches stay correct beyond the 200-row page cap.
  private listKey = computed(() => ({ tick: this.refreshTrigger(), term: this.searchTerm() }));

  data = toSignal<ListData | null, ListData | null>(
    toObservable(this.listKey).pipe(
      switchMap(({ term }) => {
        this.loading.set(true);
        this.error.set(false);
        return forkJoin({
          items: this.itemApi
            .getItems({ Page: 1, PageSize: 200, IncludeInactive: true, Search: term || undefined })
            .pipe(map(r => (r.items ?? []) as ItemResponse[])),
          supplies: this.supplyApi
            .getSupplies({ Page: 1, PageSize: 200, IncludeInactive: true, Search: term || undefined })
            .pipe(map(r => (r.items ?? []) as SupplyResponse[])),
        }).pipe(
          map(d => {
            this.loading.set(false);
            return { ...d, term } as ListData | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  areas = toSignal(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() =>
        this.areaApi.getAreas({ Page: 1, PageSize: 200 }).pipe(
          map(r => r.items ?? []),
          catchError(() => of<AreaResponse[]>([])),
        ),
      ),
    ),
    { initialValue: [] as AreaResponse[] },
  );

  /** Areas indented by depth for the filter dropdown. */
  protected areaTree = computed<AreaTreeNode[]>(() => orderAreaTree(this.areas()));
  /** Each area id → its descendant set (inclusive), so the area filter matches nested areas too. */
  private areaDescendants = computed(() => buildDescendantMap(this.areas()));

  allItems = computed(() => this.data()?.items ?? []);
  allSupplies = computed(() => this.data()?.supplies ?? []);

  // Drill-down is fetched server-side (the container by id + its direct children via the
  // ParentItemId filter), so contents are complete rather than reconstructed from the capped
  // all-items page. Re-runs on refresh and whenever the drilled container changes.
  private drillKey = computed(() => ({ id: this.containerFilter(), tick: this.refreshTrigger() }));

  private drillData = toSignal(
    toObservable(this.drillKey).pipe(
      switchMap(({ id }) => {
        if (!id) {
          this.drillLoading.set(false);
          return of(EMPTY_DRILL);
        }
        this.drillLoading.set(true);
        return forkJoin({
          container: this.itemApi.getItem({ id }).pipe(catchError(() => of<ItemResponse | null>(null))),
          children: this.itemApi
            .getItems({ Page: 1, PageSize: 200, IncludeInactive: true, ParentItemId: id })
            .pipe(map(r => (r.items ?? []) as ItemResponse[]), catchError(() => of<ItemResponse[]>([]))),
        }).pipe(
          map(result => {
            this.drillLoading.set(false);
            return result;
          }),
          catchError(() => {
            this.drillLoading.set(false);
            return of(EMPTY_DRILL);
          }),
        );
      }),
    ),
    { initialValue: EMPTY_DRILL },
  );

  /** The container the list is drilled into (server-fetched), or null when not drilled in. */
  containerItem = computed(() => (this.containerFilter() ? this.drillData().container : null));

  /** Rows in the current scope (drill-down or area filter), before the kind/inactive/low filters. */
  private scopedRows = computed<InventoryRow[]>(() => {
    // Drill-down wins: the container's direct item children (server-fetched, not capped by the
    // all-items page) plus supplies holding stock in it (client-side from the full supplies fetch).
    const drillId = this.containerFilter();
    if (drillId) {
      // Supplies come from the all-list fetch; right after leaving a search, that fetch is still
      // term-filtered, so suppress supplies until the unfiltered payload lands rather than show
      // an incomplete set.
      const suppliesStale = (this.data()?.term ?? '') !== '';
      return [
        ...this.drillData().children.map(itemToRow),
        ...(suppliesStale
          ? []
          : this.allSupplies()
              .map(s => supplyToDrillRow(s, drillId))
              .filter((r): r is InventoryRow => r !== null)),
      ];
    }

    // Area filter uses RESOLVED areas, so a nested item — or supply stock inside a nested
    // container — shows under the area it walks up to. Zero-stock supplies only appear under
    // "All areas".
    let rows = [...this.allItems().map(itemToRow), ...this.allSupplies().map(supplyToRow)];
    // While a freshly committed term's fetch is in flight, the cached rows were fetched with a
    // different term; mirror the server's name filter client-side so the unfiltered set never
    // flashes between commit and response.
    const term = this.searchTerm();
    if (term && (this.data()?.term ?? '') !== term) {
      const needle = term.toLowerCase();
      rows = rows.filter(r => r.name.toLowerCase().includes(needle));
    }
    const af = this.areaFilter();
    if (af === 'all') return rows;
    if (af === 'unplaced') return rows.filter(r => r.resolvedAreaIds.includes(null));
    // Match the selected area AND all its descendants, so the list mirrors the (rolled-up) area
    // card count: a row whose resolved leaf area is the area itself or any sub-area of it shows.
    const descendants = this.areaDescendants().get(af) ?? new Set([af]);
    return rows.filter(r => r.resolvedAreaIds.some(id => id != null && descendants.has(id)));
  });

  private kindRows = computed(() => {
    const kf = this.kindFilter();
    if (kf === 'all') return this.scopedRows();
    const kind = kf === 'items' ? 'item' : 'supply';
    return this.scopedRows().filter(r => r.kind === kind);
  });

  inactiveCount = computed(() => this.kindRows().filter(r => isTerminalStatus(r.status)).length);
  // Counts what the low-stock filter would actually show: archived low supplies count only while
  // "Show inactive" reveals them.
  lowCount = computed(() => this.kindRows()
    .filter(r => r.isLow && (this.showInactive() || !isTerminalStatus(r.status))).length);
  // Lent is a live status, so the inactive toggle can't hide these rows.
  lentCount = computed(() => this.kindRows().filter(r => r.status === 'Lent').length);

  visibleRows = computed(() => {
    let rows = this.kindRows();
    if (!this.showInactive()) rows = rows.filter(r => !isTerminalStatus(r.status));
    // Only supplies carry a low flag, so this doubles as the reorder view.
    if (this.lowStockOnly()) rows = rows.filter(r => r.isLow);
    if (this.lentOnly()) rows = rows.filter(r => r.status === 'Lent');
    return rows;
  });

  /** Breadcrumb to the drilled-in container: its resolved path plus its own (current) name. */
  breadcrumb = computed<{ id: string; name: string; kind: LocationSegmentKind }[]>(() => {
    const container = this.containerItem();
    if (!container) return [];
    const path = (container.path ?? []).map(s => ({ id: s.id, name: s.name, kind: s.kind }));
    return [...path, { id: container.id, name: container.name, kind: 'Container' as LocationSegmentKind }];
  });

  sortedVisibleRows = computed(() => {
    const rows = [...this.visibleRows()];
    const { id, desc } = this.sortState();
    const dir: 1 | -1 = desc ? -1 : 1;
    rows.sort((a, b) => {
      switch (id) {
        case 'name':
          return a.name.localeCompare(b.name) * dir;
        case 'status':
          return itemStatusLabel(a.status).localeCompare(itemStatusLabel(b.status)) * dir;
        case 'location':
          return a.locationText.localeCompare(b.locationText) * dir;
        case 'onHand':
          // Items have no quantity and always sort last, whatever the direction.
          return compareNullable(a.onHand, b.onHand, dir, (x, y) => x - y);
        case 'updatedAt':
          return compareNullable(a.updatedAt ?? null, b.updatedAt ?? null, dir, (x, y) => (x < y ? -1 : 1));
        default:
          return 0;
      }
    });
    return rows;
  });

  sortFieldLabel = computed(() =>
    this.sortFields.find(f => f.id === this.sortState().id)?.label ?? 'Sort');

  filterLabel = computed(() => {
    if (this.containerFilter()) return `In: ${this.containerItem()?.name ?? 'container'}`;
    const af = this.areaFilter();
    if (af === 'unplaced') return 'Filter: Unplaced';
    if (af !== 'all') {
      const name = this.areas().find(a => a.id === af)?.name;
      return name ? `Filter: ${name}` : 'Filter';
    }
    if (this.lowStockOnly()) return 'Filter: Low stock';
    if (this.lentOnly()) return 'Filter: Lent';
    return this.showInactive() ? 'Filter: Inactive' : 'Filter';
  });

  constructor() {
    // Debounced URL sync for the search box: commit the trimmed term once typing pauses. No
    // distinctUntilChanged here — its memory survives programmatic clears (clearSearch) and would
    // swallow a re-typed identical term; comparing against the live URL param is the real dedupe.
    toObservable(this.searchInput).pipe(
      debounceTime(300),
      map(value => value.trim()),
      filter(term => term !== (this.route.snapshot.queryParamMap.get('search') ?? '').trim()),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(term => this.syncSearchParam(term || null));

    // Commit-point enforcement: however a term lands in the URL (typing, history traversal,
    // external navigation), search is a global scope — drop any drill/area scope and keep the
    // input box in step. Living here rather than in the debounce subscriber, it also covers
    // commits that never went through the box (e.g. popstate restoring ?search=).
    toObservable(this.searchTerm).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(term => {
      if (term) {
        this.containerFilter.set(null);
        this.areaFilter.set('all');
      }
      if (term !== this.searchInput().trim()) this.searchInput.set(term);
    });
  }

  /**
   * Drops an active search immediately (no debounce). Clearing the box discards any pending
   * debounced keystrokes; the trailing '' emission is a no-op once the param is gone, because the
   * pipe's filter drops it.
   */
  private clearSearch(): void {
    this.searchInput.set('');
    if (this.searchTerm()) this.syncSearchParam(null);
  }

  /** Single URL-sync point for the search param — keep navigation options identical everywhere. */
  private syncSearchParam(term: string | null): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { search: term },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onKindFilterChange(value: unknown): void {
    if (isKindFilter(value)) this.kindFilter.set(value);
  }

  /** Picking an area drops any container drill-down and search (mutually exclusive scopes). */
  selectAreaFilter(af: AreaFilter): void {
    if (af !== 'all') this.clearSearch();
    this.containerFilter.set(null);
    this.areaFilter.set(af);
  }

  clearContainerFilter(): void {
    // A committed search can't coexist with the drill, but a pending debounced one can — drop it
    // so a keystroke from <300ms ago doesn't override this click.
    this.clearSearch();
    this.containerFilter.set(null);
    this.areaFilter.set('all');
  }

  onBreadcrumbSegment(seg: { id: string; kind: LocationSegmentKind }): void {
    this.clearSearch();
    if (seg.kind === 'Area') {
      this.containerFilter.set(null);
      this.areaFilter.set(seg.id);
    } else {
      this.containerFilter.set(seg.id);
    }
  }

  resetFilters(): void {
    this.clearSearch();
    this.showInactive.set(false);
    this.lowStockOnly.set(false);
    this.lentOnly.set(false);
    this.areaFilter.set('all');
    this.containerFilter.set(null);
  }

  onSortByChange(id: InventorySortId): void {
    const field = this.sortFields.find(f => f.id === id);
    this.sortState.set({ id, desc: field?.defaultDesc ?? false });
  }

  onSortDirChange(desc: boolean): void {
    this.sortState.update(prev => ({ id: prev.id, desc }));
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onOpen(row: InventoryRow): void {
    if (row.kind === 'supply' && row.supply) {
      this.quickPanelSupply.set(row.supply);
      this.supplyQuickPanelState.set('open');
    } else if (row.item) {
      this.quickPanelItem.set(row.item);
      this.itemQuickPanelState.set('open');
    }
  }

  onItemQuickPanelClosed(): void {
    this.itemQuickPanelState.set('closed');
  }

  // Keeps quickPanelSupply set: a close-triggered stepper flush may still be committing, and the
  // panel needs the supply id until it settles. Reopening reseeds the panel via its open-keyed state.
  onSupplyQuickPanelClosed(): void {
    this.supplyQuickPanelState.set('closed');
  }

  onQuickPanelViewContents(item: ItemResponse): void {
    this.onItemQuickPanelClosed();
    this.clearSearch();
    this.containerFilter.set(item.id);
  }

  onEdit(row: InventoryRow): void {
    if (row.kind === 'supply' && row.supply) {
      this.selectedSupply.set(row.supply);
      this.supplySheetState.set('open');
    } else if (row.item) {
      this.selectedItem.set(row.item);
      this.itemSheetState.set('open');
    }
  }

  onViewContents(row: InventoryRow): void {
    this.clearSearch();
    this.containerFilter.set(row.id);
  }

  onViewDetails(row: InventoryRow): void {
    const base = row.kind === 'supply' ? '/inventory/supplies' : '/inventory/items';
    void this.router.navigate([base, row.id]);
  }

  onItemSheetSaved(): void {
    this.itemSheetState.set('closed');
    this.onChanged();
  }

  onItemSheetClosed(): void {
    this.itemSheetState.set('closed');
    this.selectedItem.set(null);
  }

  onSupplySheetSaved(): void {
    this.supplySheetState.set('closed');
    this.onChanged();
  }

  onSupplySheetClosed(): void {
    this.supplySheetState.set('closed');
    this.selectedSupply.set(null);
  }

  onArchiveRestore(row: InventoryRow): void {
    const terminal = isTerminalStatus(row.status);
    const newStatus = terminal ? 'Active' : 'Archived';
    const isSupply = row.kind === 'supply';
    const noun = isSupply ? 'Supply' : 'Item';
    this.archiving.set(true);
    const request$: Observable<unknown> = isSupply
      ? this.supplyApi.patchSupply({ id: row.id, body: [replace('/status', newStatus)] })
      : this.itemApi.patchItem({ id: row.id, body: [replace('/status', newStatus)] });
    request$.pipe(
      handleMutationError(this.errorHandler, this.archiving, `Failed to update ${noun.toLowerCase()}.`),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(terminal ? `${noun} restored.` : `${noun} archived.`);
      this.onChanged();
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
    const isSupply = row.kind === 'supply';
    this.deleting.set(true);
    this.deleteError.set('');
    const request$ = isSupply
      ? this.supplyApi.deleteSupply({ id: row.id })
      : this.itemApi.deleteItem({ id: row.id });
    request$.subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        this.pendingDelete.set(null);
        this.onChanged();
        toast.success(isSupply ? 'Supply deleted.' : 'Item deleted.');
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection
      // reason ("has stock history — archive instead") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(
          this.errorHandler.getErrorMessage(err, `Failed to delete ${isSupply ? 'supply' : 'item'}.`));
      },
    });
  }
}
