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
  lucideSearch,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
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
import { durableToRow, consumableToRow, sortStateFromParams, type InventoryRow, type InventorySortState } from './inventory-row';
import { orderAreaTree, buildDescendantMap, type AreaTreeNode } from '../shared/area-tree';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
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

@Component({
  selector: 'app-inventory-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
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
    lucideSearch,
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
                New item
              </app-create-entry-dialog>
            </div>
          </div>
        } @else {
          <app-inventory-items-view
            [rows]="scopedRows()"
            [initialKind]="initialKind"
            [initialLowStockOnly]="initialLowStockOnly"
            [initialLentOnly]="initialLentOnly"
            [initialSort]="initialSort"
            [extraFiltersActive]="extraFiltersActive()"
            [scopeLabel]="scopeLabel()"
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
              New item
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
                  @if (areaFilter() === 'all') { <ng-icon hlm name="lucideCheck" size="sm" /> }
                </button>
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  (click)="selectAreaFilter('unplaced')"
                >
                  <span class="italic text-muted-foreground">Unplaced</span>
                  @if (areaFilter() === 'unplaced') { <ng-icon hlm name="lucideCheck" size="sm" /> }
                </button>
                @for (node of areaTree(); track node.area.id) {
                  <button
                    type="button"
                    class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                    (click)="selectAreaFilter(node.area.id)"
                  >
                    <span class="truncate" [style.marginInlineStart.px]="node.depth * 14">{{ node.area.name }}</span>
                    @if (areaFilter() === node.area.id) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                  </button>
                }
              </div>
            </div>
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
  // Deep-link params from the overview tiles and place browser, seeded once into the filter signals
  // below and stripped from the URL (read-once, not two-way synced — that's the lenses feature).
  // Container-level navigation lives on Browse, so the list seeds only the area scope and the
  // non-place lenses; a container's "View contents" routes to /inventory/browse/container/:id.
  private deepLink = seedDeepLinkParams(this.route, this.router, ['lowStock', 'lent', 'kind', 'area', 'sort', 'dir']);

  // The non-place refinements live in the items-view; pass the deep-link values as its one-shot seeds.
  protected readonly initialKind: KindFilter = kindFilterFromParam(this.deepLink['kind']);
  protected readonly initialLowStockOnly = this.deepLink['lowStock'] === 'true';
  protected readonly initialLentOnly = this.deepLink['lent'] === 'true';
  // The "View all →" recency bridge lands here sorted by Updated-descending; absent/invalid → name-asc.
  protected readonly initialSort: InventorySortState =
    sortStateFromParams(this.deepLink['sort'], this.deepLink['dir']);

  areaFilter = signal<AreaFilter>(this.deepLink['area'] ?? 'all');

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

  /** Rows in the current scope (area filter), before the kind/inactive/low filters. */
  protected scopedRows = computed<InventoryRow[]>(() => {
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

  // Higher-precedence trigger label for the refine popover: the selected area (the items-view falls
  // back to its own low/lent/inactive label when this is null).
  protected scopeLabel = computed<string | null>(() => {
    const af = this.areaFilter();
    if (af === 'unplaced') return 'Filter: Unplaced';
    if (af !== 'all') {
      const name = this.areas().find(a => a.id === af)?.name;
      return name ? `Filter: ${name}` : 'Filter';
    }
    return null;
  });

  // Drives the items-view Reset button: the popover's own filters OR any parent scope (area/search)
  // being active.
  protected extraFiltersActive = computed(() =>
    this.areaFilter() !== 'all' || this.searchTerm() !== '');

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
    // external navigation), search is a global scope — drop any area scope and keep the input box
    // in step. Living here rather than in the debounce subscriber, it also covers commits that
    // never went through the box (e.g. popstate restoring ?search=).
    toObservable(this.searchTerm).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(term => {
      if (term) this.areaFilter.set('all');
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

  /** Picking an area drops any active search (mutually exclusive scopes). */
  selectAreaFilter(af: AreaFilter): void {
    if (af !== 'all') this.clearSearch();
    this.areaFilter.set(af);
  }

  /** The items-view Reset clears its own refinements; the page clears its scope (search/area). */
  onReset(): void {
    this.clearSearch();
    this.areaFilter.set('all');
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

  /** A container's "View contents" in the quick panel — Browse owns containment navigation. */
  onQuickPanelViewContents(durable: DurableResponse): void {
    this.onDurableQuickPanelClosed();
    this.viewContainerInBrowse(durable.id);
  }

  /** A container row's "View contents" — drill into it on Browse, not in the list. */
  onViewContents(row: InventoryRow): void {
    this.viewContainerInBrowse(row.id);
  }

  /** Both "View contents" entry points route to the container's Browse drill (the list never drills). */
  private viewContainerInBrowse(id: string): void {
    void this.router.navigate(['/inventory/browse/container', id]);
  }
}
