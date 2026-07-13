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
  distinctUntilChanged,
  filter,
} from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucidePackage,
  lucideCircleAlert,
  lucideCheck,} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { InventoryTagApiService } from '@/api/services/inventory-tag-api.service';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { CreateEntryDialogComponent } from './create-entry-dialog/create-entry-dialog.component';
import { DurableQuickPanelComponent } from '../durables/durable-quick-panel/durable-quick-panel.component';
import { ConsumableQuickPanelComponent } from '../consumables/consumable-quick-panel/consumable-quick-panel.component';
import { InventoryItemsViewComponent, kindFilterFromParam, type KindFilter } from './inventory-items-view.component';
import { durableToRow, consumableToRow, sortStateFromParams, type InventoryRow, type InventorySortState } from './inventory-row';
import { orderAreaTree, type AreaTreeNode } from '../shared/area-tree';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrandResponse } from '@/api/generated/models/brand-response';
import type { GetInventoryTagsResponse } from '@/api/generated/models/get-inventory-tags-response';
import type { GetInventoryTagsByIdsResponse } from '@/api/generated/models/get-inventory-tags-by-ids-response';
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
  /** Server totals for the current filter — drive the "Showing X of N" cap notice. */
  durablesTotal: number;
  consumablesTotal: number;
  /** The search term this payload was fetched with — lets consumers detect stale data mid-search. */
  term: string;
}

@Component({
  selector: 'app-inventory-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    SearchInputComponent,
    HlmSpinner,
    HlmEmptyImports,
    HlmSeparatorImports,
    HlmCheckboxImports,
    HlmLabel,
    TagBadgeComponent,
    InventoryItemsViewComponent,
    CreateEntryDialogComponent,
    DurableQuickPanelComponent,
    ConsumableQuickPanelComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucidePackage,
    lucideCircleAlert,
    lucideCheck,  })],
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
        @if (allDurables().length === 0 && allConsumables().length === 0 && !searchTerm() && areaFilter() === 'all' && brandFilter() === 'all' && tagFilter().size === 0) {
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
          @if (truncationNotice()) {
            <p class="text-xs text-muted-foreground">{{ truncationNotice() }}</p>
          }
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
            <app-search-input toolbarStart class="w-full min-w-0 sm:w-56 lg:w-64" [(value)]="searchInput" placeholder="Search items..." ariaLabel="Search items" />

            <app-create-entry-dialog toolbarEnd (created)="onChanged()">
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

              @if (brands().length > 0) {
                <span hlmSeparator></span>
                <div class="max-h-[240px] overflow-y-auto p-1">
                  <span class="block px-2 py-1 text-xs font-medium text-muted-foreground">Brand</span>
                  <button
                    type="button"
                    class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                    (click)="selectBrandFilter('all')"
                  >
                    <span>All brands</span>
                    @if (brandFilter() === 'all') { <ng-icon hlm name="lucideCheck" size="sm" /> }
                  </button>
                  @for (brand of brands(); track brand.id) {
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                      (click)="selectBrandFilter(brand.id)"
                    >
                      <span class="truncate">{{ brand.name }}</span>
                      @if (brandFilter() === brand.id) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                    </button>
                  }
                </div>
              }

              <span hlmSeparator></span>
              <div class="p-1">
                <span class="block px-2 py-1 text-xs font-medium text-muted-foreground">Tags</span>
                <label class="flex cursor-pointer items-center gap-2 px-2 py-1.5 text-sm" hlmLabel>
                  <hlm-checkbox
                    [checked]="includeChildTags()"
                    (checkedChange)="includeChildTags.set($event === true)"
                    [aria-label]="'Include sub-tags'"
                  />
                  <span>Include sub-tags</span>
                </label>
                @if (tagChips().length > 0) {
                  <div class="flex flex-wrap gap-1 px-2 py-1.5">
                    @for (chip of tagChips(); track chip.id) {
                      <app-tag-badge [path]="chip.path" [removable]="true" [compact]="true" (removed)="toggleTagFilter(chip.id)" />
                    }
                  </div>
                }
                <div class="px-1 pb-1">
                  <app-search-input [(value)]="tagSearchInput" placeholder="Search tags..." ariaLabel="Search tags" />
                </div>
                <div class="max-h-[180px] overflow-y-auto">
                  @for (tag of tagOptions(); track tag.id) {
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                      (click)="toggleTagFilter(tag.id)"
                    >
                      <span class="truncate">{{ tag.path }}</span>
                      @if (tagFilter().has(tag.id)) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                    </button>
                  } @empty {
                    <span class="block px-2 py-2 text-xs text-muted-foreground">No tags found.</span>
                  }
                </div>
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
  private brandApi = inject(InventoryBrandApiService);
  private tagApi = inject(InventoryTagApiService);
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
  // 'brand' is deliberately NOT in this list: unlike the other params it can also arrive while the
  // component is already mounted (from a quick panel overlaid on this same route), which the one-shot
  // construction seed can't catch — so it is owned instead by the reactive handler in the constructor
  // (seeded from the snapshot below, like `searchInput`). Keeping it out of both lists also avoids two
  // strip-navigations racing on a combined `?brand=&area=` deep link.
  private deepLink = seedDeepLinkParams(this.route, this.router, ['lowStock', 'lent', 'kind', 'area', 'sort', 'dir']);

  // The non-place refinements live in the items-view; pass the deep-link values as its one-shot seeds.
  protected readonly initialKind: KindFilter = kindFilterFromParam(this.deepLink['kind']);
  protected readonly initialLowStockOnly = this.deepLink['lowStock'] === 'true';
  protected readonly initialLentOnly = this.deepLink['lent'] === 'true';
  // The "View all →" recency bridge lands here sorted by Updated-descending; absent/invalid → name-asc.
  protected readonly initialSort: InventorySortState =
    sortStateFromParams(this.deepLink['sort'], this.deepLink['dir']);

  areaFilter = signal<AreaFilter>(this.deepLink['area'] ?? 'all');
  /**
   * 'all' | a brand id. A page-level fetch key, mirroring areaFilter — not a one-shot lens. Seeded
   * synchronously from the URL here (so a cold `?brand=` load fetches once, not all→brand); the
   * constructor handler owns the strip and any mid-mount re-application. See the deepLink note above.
   */
  brandFilter = signal<string>(this.route.snapshot.queryParamMap.get('brand') ?? 'all');

  /**
   * Selected tag-filter ids (multi-select) + the descendant-expansion toggle. Page-level fetch keys
   * like areaFilter/brandFilter, seeded synchronously from the repeated `?tagIds=` and
   * `?includeChildTags=` params; the constructor handler owns the strip and any mid-mount
   * re-application (a badge/CRUD deep-link into `/inventory/items?tagIds=…`). See the deepLink note.
   */
  tagFilter = signal<Set<string>>(new Set(this.route.snapshot.queryParamMap.getAll('tagIds')));
  includeChildTags = signal<boolean>(this.route.snapshot.queryParamMap.get('includeChildTags') === 'true');
  /** Free text driving the refine popover's tag checklist (client search over the tag list). */
  tagSearchInput = signal('');

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
  // Area is part of the fetch key now: the resolved-area filter is applied server-side, so changing
  // it re-queries rather than re-filtering the (200-row-capped) client set.
  private listKey = computed(() => ({
    tick: this.refreshTrigger(),
    term: this.searchTerm(),
    area: this.areaFilter(),
    brand: this.brandFilter(),
    tags: [...this.tagFilter()].sort().join(','),
    // Key on the *resolved* toggle: with no tags selected the sub-tag toggle is inert server-side
    // (fetch collapses it to undefined), so keep it inert in the key too — otherwise flipping the
    // checkbox before picking a tag fires an identical, wasted durables+consumables refetch.
    includeChildTags: this.tagFilter().size > 0 && this.includeChildTags(),
  }));

  data = toSignal<ListData | null, ListData | null>(
    toObservable(this.listKey).pipe(
      switchMap(({ term, area, brand, tags, includeChildTags }) => {
        this.loading.set(true);
        this.error.set(false);
        // Resolved-area filter goes to the server: a specific area sends AreaId (the server rolls in
        // descendants); 'unplaced' sends Unplaced; 'all' sends neither.
        const areaId = area !== 'all' && area !== 'unplaced' ? area : undefined;
        const unplaced = area === 'unplaced' ? true : undefined;
        const brandId = brand !== 'all' ? brand : undefined;
        // Tag filter is applied server-side on both kinds (same expanded id set); the sub-tag toggle
        // rides along and is inert server-side when no tags are selected.
        const tagIds = tags ? tags.split(',') : undefined;
        const childTags = tagIds ? includeChildTags || undefined : undefined;
        const search = term || undefined;
        return forkJoin({
          durables: this.durableApi.getDurables({
            Page: 1, PageSize: 200, IncludeInactive: true, Search: search, AreaId: areaId, Unplaced: unplaced, BrandId: brandId, TagIds: tagIds, IncludeChildTags: childTags,
          }),
          consumables: this.consumableApi.getConsumables({
            Page: 1, PageSize: 200, IncludeInactive: true, Search: search, AreaId: areaId, Unplaced: unplaced, BrandId: brandId, TagIds: tagIds, IncludeChildTags: childTags,
          }),
        }).pipe(
          map(({ durables, consumables }) => {
            this.loading.set(false);
            return {
              durables: (durables.items ?? []) as DurableResponse[],
              consumables: (consumables.items ?? []) as ConsumableResponse[],
              durablesTotal: durables.totalCount ?? 0,
              consumablesTotal: consumables.totalCount ?? 0,
              term,
            } as ListData | null;
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

  // Brand facet source — loaded once for the page (household-scale lists are small). Refetched with
  // the same trigger as areas so a just-created brand shows up after onChanged().
  protected brands = toSignal(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() =>
        this.brandApi.getBrands({ Page: 1, PageSize: 200 }).pipe(
          map(r => r.items ?? []),
          catchError(() => of<BrandResponse[]>([])),
        ),
      ),
    ),
    { initialValue: [] as BrandResponse[] },
  );

  // Resolved labels for the currently-selected tag ids — via /resolve, so a deep-linked tag not in
  // the current tag-list page still renders (and is removable) as a chip in the refine popover.
  protected tagChips = toSignal(
    toObservable(this.tagFilter).pipe(
      switchMap(set => {
        const ids = [...set];
        return ids.length === 0
          ? of<GetInventoryTagsByIdsResponse[]>([])
          : this.tagApi.getTagsByIds({ body: { ids } }).pipe(
              map(r => r as GetInventoryTagsByIdsResponse[]),
              catchError(() => of<GetInventoryTagsByIdsResponse[]>([])),
            );
      }),
    ),
    { initialValue: [] as GetInventoryTagsByIdsResponse[] },
  );

  // Search-driven tag options for the refine popover's multi-select checklist (household-scale, so a
  // 50-row page with a name/path ILIKE is plenty). Debounced so typing doesn't spam the endpoint.
  protected tagOptions = toSignal(
    toObservable(this.tagSearchInput).pipe(
      debounceTime(200),
      switchMap(search =>
        this.tagApi.getTags({ Search: search || undefined, IncludeChildren: true, Page: 1, PageSize: 50 }).pipe(
          map(r => (r.items ?? []) as GetInventoryTagsResponse[]),
          catchError(() => of<GetInventoryTagsResponse[]>([])),
        ),
      ),
    ),
    { initialValue: [] as GetInventoryTagsResponse[] },
  );

  allDurables = computed(() => this.data()?.durables ?? []);
  allConsumables = computed(() => this.data()?.consumables ?? []);

  /** Rows for the current server-filtered payload, before the client kind/inactive/low filters. */
  protected scopedRows = computed<InventoryRow[]>(() => {
    // The area filter is applied server-side now (resolved area + descendants), so there is no
    // client-side area pass here — the payload already scopes to the selected area.
    let rows = [...this.allDurables().map(durableToRow), ...this.allConsumables().map(consumableToRow)];
    // While a freshly committed term's fetch is in flight, the cached rows were fetched with a
    // different term; mirror the server's name filter client-side so the unfiltered set never
    // flashes between commit and response.
    const term = this.searchTerm();
    if (term && (this.data()?.term ?? '') !== term) {
      const needle = term.toLowerCase();
      rows = rows.filter(r => r.name.toLowerCase().includes(needle));
    }
    return rows;
  });

  // The list is still capped at 200 rows per kind; when the server's total exceeds what's shown,
  // tell the user so they refine with search rather than assume they're seeing everything. (Full
  // numbered pagination of the merged two-kind list is a separate table-ux concern — a follow-up.)
  protected truncationNotice = computed<string | null>(() => {
    const d = this.data();
    if (!d) return null;
    const parts: string[] = [];
    if (d.durablesTotal > d.durables.length) parts.push(`${d.durables.length} of ${d.durablesTotal} durables`);
    if (d.consumablesTotal > d.consumables.length) parts.push(`${d.consumables.length} of ${d.consumablesTotal} consumables`);
    return parts.length ? `Showing ${parts.join(' and ')} — refine with search to narrow the list.` : null;
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
    this.areaFilter() !== 'all' || this.brandFilter() !== 'all' || this.tagFilter().size > 0 || this.searchTerm() !== '');

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

    // Sole owner of the `brand` deep-link param (deliberately excluded from seedDeepLinkParams above).
    // brandFilter is seeded synchronously from the snapshot; this handles the strip AND the one case
    // the one-shot seed can't: a brand link inside a quick panel overlaid on THIS list page navigates
    // to /inventory/items?brand=… without reconstructing the component. Watching the param covers that
    // (and cross-route arrivals from the detail-page chip / Browse alike): apply the brand, close any
    // open quick panel so the filtered list is visible, then strip the param (read-once, clean URL).
    this.route.queryParamMap.pipe(
      map(p => p.get('brand')),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(brand => {
      if (!brand) return;
      this.brandFilter.set(brand);
      this.durableQuickPanelState.set('closed');
      this.consumableQuickPanelState.set('closed');
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { brand: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });

    // Sole owner of the `tagIds`/`includeChildTags` deep-link params (excluded from seedDeepLinkParams,
    // same reasoning as `brand`). tagFilter/includeChildTags are seeded synchronously from the snapshot;
    // this handles the strip AND the mid-mount case the one-shot seed can't: a tag badge/CRUD link into
    // `/inventory/items?tagIds=…` (from a quick panel overlaid on THIS page, or cross-route) applies the
    // filter without reconstruction. Serialize the (repeated ids + toggle) pair for distinctUntilChanged.
    this.route.queryParamMap.pipe(
      map(p => JSON.stringify({ ids: p.getAll('tagIds'), inc: p.get('includeChildTags') })),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(serialized => {
      const { ids, inc } = JSON.parse(serialized) as { ids: string[]; inc: string | null };
      // A tagIds-less emission is never a real tag deep-link (the app always pairs them). Bail before
      // touching state — otherwise a stray `?includeChildTags=true` (manual URL / back-nav) would wipe
      // the user's in-memory tag selection while leaving the checkbox checked.
      if (ids.length === 0) return;
      this.tagFilter.set(new Set(ids));
      this.includeChildTags.set(inc === 'true');
      this.durableQuickPanelState.set('closed');
      this.consumableQuickPanelState.set('closed');
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { tagIds: null, includeChildTags: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
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

  /** Picking a brand is orthogonal to search/area — the server ANDs the filters, so leave them be. */
  selectBrandFilter(bf: string): void {
    this.brandFilter.set(bf);
  }

  /** The items-view Reset clears its own refinements; the page clears its scope (search/area/brand/tags). */
  onReset(): void {
    this.clearSearch();
    this.areaFilter.set('all');
    this.brandFilter.set('all');
    // Guard the writes: signals compare Sets/booleans by reference/value, so a blind reset when
    // already empty would perturb listKey and fire a redundant refetch (unlike the string filters,
    // which self-dedupe via `.set('all')`).
    if (this.tagFilter().size > 0) this.tagFilter.set(new Set());
    if (this.includeChildTags()) this.includeChildTags.set(false);
  }

  /** Toggles a tag id in the multi-select filter set (orthogonal to search/area/brand — server ANDs). */
  toggleTagFilter(id: string): void {
    this.tagFilter.update(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
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

  /** A holder's "View contents/components" in the quick panel — Browse owns containment navigation. */
  onQuickPanelViewContents(durable: DurableResponse): void {
    this.onDurableQuickPanelClosed();
    this.viewHolder(durable.id, durable.durableKind);
  }

  /** A holder row's "View contents/components" — drill into it (the list never drills inline). */
  onViewContents(row: InventoryRow): void {
    this.viewHolder(row.id, row.durable?.durableKind);
  }

  /**
   * A Container drills into its Browse place (the list never drills); an Assembly is not a place, so
   * its components live on its detail page's Components section instead.
   */
  private viewHolder(id: string, kind: DurableResponse['durableKind']): void {
    void this.router.navigate(
      kind === 'Assembly' ? ['/inventory/items', id] : ['/inventory/browse/container', id],
    );
  }
}
