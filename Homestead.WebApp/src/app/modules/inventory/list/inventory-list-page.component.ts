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
} from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucidePackage,
  lucideCircleAlert,
  lucideCheck,
  lucideChevronRight,
  lucideSearch,
  lucideX,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { CreateEntryDialogComponent } from './create-entry-dialog/create-entry-dialog.component';
import { DurableQuickPanelComponent } from '../durables/durable-quick-panel/durable-quick-panel.component';
import { ConsumableQuickPanelComponent } from '../consumables/consumable-quick-panel/consumable-quick-panel.component';
import { InventoryItemsViewComponent, kindFilterFromParam, type KindFilter } from './inventory-items-view.component';
import { durableToRow, consumableToRow, consumableToDrillRow, type InventoryRow } from './inventory-row';
import { orderAreaTree, buildDescendantMap, type AreaTreeNode } from '../shared/area-tree';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { LocationSegmentKind } from '@/api/generated/models/location-segment-kind';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/** 'all' | 'unplaced' | an area id. */
type AreaFilter = string;

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
  durables: DurableResponse[];
  consumables: ConsumableResponse[];
  /** The search term this payload was fetched with — lets consumers detect stale data mid-search. */
  term: string;
}

/** Server-fetched payload for a container drill-down: the container itself plus its direct children. */
interface DrillData {
  container: DurableResponse | null;
  children: DurableResponse[];
}

const EMPTY_DRILL: DrillData = { container: null, children: [] };

@Component({
  selector: 'app-inventory-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmInput,
    HlmSpinner,
    HlmEmptyImports,
    HlmSeparatorImports,
    InventoryItemsViewComponent,
    CreateEntryDialogComponent,
    DurableQuickPanelComponent,
    ConsumableQuickPanelComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucidePackage,
    lucideCircleAlert,
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
        @if (allDurables().length === 0 && allConsumables().length === 0 && !searchTerm()) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucidePackage" />
              </div>
              <div hlmEmptyTitle>No items yet</div>
              <div hlmEmptyDescription>Add your first durable or consumable to start tracking what you own, where it lives, and how much is left.</div>
            </div>
            <div hlmEmptyContent>
              <app-create-entry-dialog (created)="onChanged()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New
              </app-create-entry-dialog>
            </div>
          </div>
        } @else {
          <app-inventory-items-view
            [rows]="scopedRows()"
            [initialKind]="initialKind"
            [initialLowStockOnly]="initialLowStockOnly"
            [initialLentOnly]="initialLentOnly"
            [extraFiltersActive]="extraFiltersActive()"
            [scopeLabel]="scopeLabel()"
            [loading]="drillLoading()"
            (changed)="onChanged()"
            (openDurable)="onOpenDurable($event)"
            (openConsumable)="onOpenConsumable($event)"
            (viewContents)="onViewContents($event)"
            (reset)="onReset()"
          >
            <div toolbarStart class="relative w-full min-w-0 sm:w-56 lg:w-64">
              <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
              <input
                hlmInput
                placeholder="Search items..."
                class="h-8 w-full min-w-0 pl-8"
                aria-label="Search items"
                [value]="searchInput()"
                (input)="searchInput.set($any($event.target).value)"
              />
            </div>

            <app-create-entry-dialog toolbarEnd triggerSize="sm" (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New
            </app-create-entry-dialog>

            <div refineExtra>
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

            @if (containerFilter()) {
              <div beforeTable class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground">
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
          </app-inventory-items-view>
        }
      }
    </div>

    <app-durable-quick-panel
      [durable]="quickPanelDurable()"
      [state]="durableQuickPanelState()"
      (changed)="onChanged()"
      (closed)="onDurableQuickPanelClosed()"
      (viewContents)="onQuickPanelViewContents($event)"
    />

    <app-consumable-quick-panel
      [consumable]="quickPanelConsumable()"
      [state]="consumableQuickPanelState()"
      (changed)="onChanged()"
      (closed)="onConsumableQuickPanelClosed()"
    />
  `,
})
export class InventoryListPageComponent {
  private durableApi = inject(InventoryDurableApiService);
  private consumableApi = inject(InventoryConsumableApiService);
  private areaApi = inject(InventoryAreaApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  drillLoading = signal(false);
  // Deep-link params from the overview tiles and place browser, seeded once into the filter
  // signals below and stripped from the URL (read-once, not two-way synced — that's the lenses
  // feature). When both ?area= and ?container= arrive, the container drill wins via scopedRows'
  // existing precedence — no extra handling needed here.
  private deepLink = seedDeepLinkParams(this.route, this.router, ['lowStock', 'lent', 'kind', 'area', 'container']);

  // The non-place refinements live in the items-view; pass the deep-link values as its one-shot seeds.
  protected readonly initialKind: KindFilter = kindFilterFromParam(this.deepLink['kind']);
  protected readonly initialLowStockOnly = this.deepLink['lowStock'] === 'true';
  protected readonly initialLentOnly = this.deepLink['lent'] === 'true';

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

  quickPanelDurable = signal<DurableResponse | null>(null);
  durableQuickPanelState = signal<BrnDialogState>('closed');
  quickPanelConsumable = signal<ConsumableResponse | null>(null);
  consumableQuickPanelState = signal<BrnDialogState>('closed');

  // Server-side search: matches stay correct beyond the 200-row page cap.
  private listKey = computed(() => ({ tick: this.refreshTrigger(), term: this.searchTerm() }));

  data = toSignal<ListData | null, ListData | null>(
    toObservable(this.listKey).pipe(
      switchMap(({ term }) => {
        this.loading.set(true);
        this.error.set(false);
        return forkJoin({
          durables: this.durableApi
            .getDurables({ Page: 1, PageSize: 200, IncludeInactive: true, Search: term || undefined })
            .pipe(map(r => (r.items ?? []) as DurableResponse[])),
          consumables: this.consumableApi
            .getConsumables({ Page: 1, PageSize: 200, IncludeInactive: true, Search: term || undefined })
            .pipe(map(r => (r.items ?? []) as ConsumableResponse[])),
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

  allDurables = computed(() => this.data()?.durables ?? []);
  allConsumables = computed(() => this.data()?.consumables ?? []);

  // Drill-down is fetched server-side (the container by id + its direct children via the
  // ParentItemId filter), so contents are complete rather than reconstructed from the capped
  // all-durables page. Re-runs on refresh and whenever the drilled container changes.
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
          container: this.durableApi.getDurable({ id }).pipe(catchError(() => of<DurableResponse | null>(null))),
          children: this.durableApi
            .getDurables({ Page: 1, PageSize: 200, IncludeInactive: true, ParentItemId: id })
            .pipe(map(r => (r.items ?? []) as DurableResponse[]), catchError(() => of<DurableResponse[]>([]))),
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
  protected scopedRows = computed<InventoryRow[]>(() => {
    // Drill-down wins: the container's direct durable children (server-fetched, not capped by the
    // all-durables page) plus consumables holding stock in it (client-side from the full consumables fetch).
    const drillId = this.containerFilter();
    if (drillId) {
      // Consumables come from the all-list fetch; right after leaving a search, that fetch is still
      // term-filtered, so suppress consumables until the unfiltered payload lands rather than show
      // an incomplete set.
      const consumablesStale = (this.data()?.term ?? '') !== '';
      return [
        ...this.drillData().children.map(durableToRow),
        ...(consumablesStale
          ? []
          : this.allConsumables()
              .map(c => consumableToDrillRow(c, drillId))
              .filter((r): r is InventoryRow => r !== null)),
      ];
    }

    // Area filter uses RESOLVED areas, so a nested durable — or consumable stock inside a nested
    // container — shows under the area it walks up to. Zero-stock consumables only appear under
    // "All areas".
    let rows = [...this.allDurables().map(durableToRow), ...this.allConsumables().map(consumableToRow)];
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

  /** Breadcrumb to the drilled-in container: its resolved path plus its own (current) name. */
  breadcrumb = computed<{ id: string; name: string; kind: LocationSegmentKind }[]>(() => {
    const container = this.containerItem();
    if (!container) return [];
    const path = (container.path ?? []).map(s => ({ id: s.id, name: s.name, kind: s.kind }));
    return [...path, { id: container.id, name: container.name, kind: 'Container' as LocationSegmentKind }];
  });

  // Higher-precedence trigger label for the refine popover: the drilled container or selected area
  // (the items-view falls back to its own low/lent/inactive label when this is null). Mirrors the
  // old filterLabel's container > area precedence exactly.
  protected scopeLabel = computed<string | null>(() => {
    if (this.containerFilter()) return `In: ${this.containerItem()?.name ?? 'container'}`;
    const af = this.areaFilter();
    if (af === 'unplaced') return 'Filter: Unplaced';
    if (af !== 'all') {
      const name = this.areas().find(a => a.id === af)?.name;
      return name ? `Filter: ${name}` : 'Filter';
    }
    return null;
  });

  // Drives the items-view Reset button: the popover's own filters OR any parent scope (area/
  // container/search) being active.
  protected extraFiltersActive = computed(() =>
    this.areaFilter() !== 'all' || this.containerFilter() !== null || this.searchTerm() !== '');

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

  /** The items-view Reset clears its own refinements; the page clears its scope (search/area/container). */
  onReset(): void {
    this.clearSearch();
    this.areaFilter.set('all');
    this.containerFilter.set(null);
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onOpenDurable(durable: DurableResponse): void {
    this.quickPanelDurable.set(durable);
    this.durableQuickPanelState.set('open');
  }

  onOpenConsumable(consumable: ConsumableResponse): void {
    this.quickPanelConsumable.set(consumable);
    this.consumableQuickPanelState.set('open');
  }

  onDurableQuickPanelClosed(): void {
    this.durableQuickPanelState.set('closed');
  }

  // Keeps quickPanelConsumable set: a close-triggered stepper flush may still be committing, and the
  // panel needs the consumable id until it settles. Reopening reseeds the panel via its open-keyed state.
  onConsumableQuickPanelClosed(): void {
    this.consumableQuickPanelState.set('closed');
  }

  onQuickPanelViewContents(durable: DurableResponse): void {
    this.onDurableQuickPanelClosed();
    this.clearSearch();
    this.containerFilter.set(durable.id);
  }

  onViewContents(row: InventoryRow): void {
    this.clearSearch();
    this.containerFilter.set(row.id);
  }
}
