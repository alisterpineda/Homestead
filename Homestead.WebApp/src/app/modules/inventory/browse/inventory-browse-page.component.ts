import {
  Component,
  ChangeDetectionStrategy,
  computed,
  effect,
  inject,
  signal,
  viewChild,
  TemplateRef,
  OnDestroy,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, tap, startWith } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideSearch } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { PlaceBrowserComponent } from '../overview/place-browser/place-browser.component';
import { placeRefToLink, segmentToRef, placeKey, placeDisplayName, type PlaceRef } from '../overview/place-browser/place-param';
import { InventoryWarningPanelsComponent, type WarningPanelsScope } from './inventory-warning-panels.component';
import { InventoryStatusChipsComponent, type StatusChipsScope } from './inventory-status-chips.component';
import { InventoryRecentlyUpdatedComponent } from './inventory-recently-updated.component';
import { DurableQuickPanelComponent } from '../durables/durable-quick-panel/durable-quick-panel.component';
import { ConsumableQuickPanelComponent } from '../consumables/consumable-quick-panel/consumable-quick-panel.component';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { RecentlyUpdatedItem } from '@/api/generated/models/recently-updated-item';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/**
 * Browse-by-place: the spatial-navigation surface, one route per drilled node under
 * `/inventory/browse`. The current node lives in the URL ({@link placeRef} derived from route
 * data + params); the ancestor trail comes from the API's `PlaceContentsResponse.path`, rendered
 * as a header breadcrumb. At root it shows the area grid (the only place an overview fetch
 * happens); drilled, it renders that node's contents directly — no overview gating — so a cold
 * deep-link paints as soon as the place-contents fetch lands.
 */
@Component({
  selector: 'app-inventory-browse-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmInput,
    HlmSpinner,
    HlmEmptyImports,
    HlmBreadCrumbImports,
    PlaceBrowserComponent,
    InventoryWarningPanelsComponent,
    InventoryStatusChipsComponent,
    InventoryRecentlyUpdatedComponent,
    DurableQuickPanelComponent,
    ConsumableQuickPanelComponent,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideSearch })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerBreadcrumb>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/inventory/browse">Browse</a>
          </li>
          @for (seg of path(); track seg.id; let last = $last) {
            <li hlmBreadcrumbSeparator></li>
            <li hlmBreadcrumbItem class="min-w-0">
              @if (last) {
                <span hlmBreadcrumbPage class="truncate">{{ seg.name }}</span>
              } @else {
                <a hlmBreadcrumbLink class="truncate" [link]="placeRefToLink(segmentToRef(seg))">{{ seg.name }}</a>
              }
            </li>
          } @empty {
            @if (placeContents()) {
              <li hlmBreadcrumbSeparator></li>
              <li hlmBreadcrumbItem class="min-w-0">
                <span hlmBreadcrumbPage class="truncate">{{ placeName() }}</span>
              </li>
            }
          }
        </ol>
      </nav>
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (!placeRef() && loading() && !overview()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (!placeRef() && error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Failed to load places</div>
            <div hlmEmptyDescription>There was an error loading your areas. Please try again.</div>
          </div>
        </div>
      }

      <!-- Search hands the term off to the unified items list — root only (a drilled place already
           scopes the view). The warning region is global at root, place-scoped when drilled. -->
      @if (!placeRef()) {
        <form class="relative mx-auto w-full max-w-xl" (submit)="onSearchSubmit($event)">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            placeholder="Search items..."
            class="h-10 w-full pl-9"
            aria-label="Search items"
            [value]="searchInput()"
            (input)="searchInput.set($any($event.target).value)"
          />
        </form>
      }

      @if (warningScope(); as scope) {
        <app-inventory-warning-panels [scope]="scope" />
      }

      <!-- The recency axis (root only): actionable alerts stay first, then this leads the neutral
           nav rows. Carries full DTOs so a chip click opens the quick panel with no re-fetch. -->
      @if (recentlyUpdated(); as items) {
        <app-inventory-recently-updated
          [items]="items"
          (openDurable)="onOpenDurable($event)"
          (openConsumable)="onOpenConsumable($event)"
        />
      }

      <!-- Sibling of (not nested under) the alert gate above, so the neutral status row shows even
           when there are no alerts. -->
      @if (statusScope(); as scope) {
        <app-inventory-status-chips [scope]="scope" />
      }

      <app-place-browser
        [overview]="overview()"
        [place]="placeRef()"
        [contents]="placeContents()"
        [error]="drillError()"
        (openDurable)="onOpenDurable($event)"
        (openConsumable)="onOpenConsumable($event)"
        (containerDetails)="onContainerDetails($event)"
        (changed)="onChanged()"
      />
    </div>

    <app-durable-quick-panel
      [durable]="quickPanelDurable()"
      [state]="durableQuickPanelState()"
      (changed)="onChanged()"
      (closed)="onDurableQuickPanelClosed()"
    />

    <app-consumable-quick-panel
      [consumable]="quickPanelConsumable()"
      [state]="consumableQuickPanelState()"
      (changed)="onChanged()"
      (closed)="onConsumableQuickPanelClosed()"
    />
  `,
})
export class InventoryBrowsePageComponent implements OnDestroy {
  private overviewApi = inject(InventoryOverviewApiService);
  private placeApi = inject(InventoryPlaceApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private pageHeaderService = inject(PageHeaderService);

  protected readonly placeRefToLink = placeRefToLink;
  protected readonly segmentToRef = segmentToRef;

  loading = signal(true);
  error = signal(false);
  drillLoading = signal(false);
  drillError = signal(false);

  /** Root-only search box; hands the term to the unified items list on submit. */
  searchInput = signal('');

  // Refresh-keyed: the quick panels and the create dialog mutate from this page, so the area grid
  // and place contents must refetch on `changed`.
  refreshTrigger = signal(0);

  private headerBreadcrumbRef = viewChild<TemplateRef<unknown>>('headerBreadcrumb');

  // The drilled node is named by the route: the route's `placeKind` data picks the kind, the path
  // param carries the id. Reading both keeps the same component instance reactive across drills.
  private routeData = toSignal(this.route.data, { initialValue: this.route.snapshot.data });
  private params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });

  placeRef = computed<PlaceRef>(() => {
    const kind = this.routeData()['placeKind'] as string | undefined;
    const params = this.params();
    if (kind === 'area') {
      const id = params.get('areaId');
      return id ? { kind: 'area', id } : null;
    }
    if (kind === 'container') {
      const id = params.get('containerId');
      return id ? { kind: 'container', id } : null;
    }
    if (kind === 'unplaced') return { kind: 'unplaced' };
    return null;
  });

  // The area grid only exists at root, so fetch the overview there and skip it while drilled — a
  // cold deep-link to a drilled node never waits on it.
  private overviewKey = computed(() => ({ root: this.placeRef() === null, tick: this.refreshTrigger() }));

  overview = toSignal<GetInventoryOverviewResponse | null, GetInventoryOverviewResponse | null>(
    toObservable(this.overviewKey).pipe(
      switchMap(({ root }) => {
        if (!root) {
          this.loading.set(false);
          this.error.set(false);
          return of(null);
        }
        this.loading.set(true);
        // Clear a prior root error before retrying, so a successful refetch can't co-render the
        // grid with a stale "Failed to load places" block (the !root branch above resets it too).
        this.error.set(false);
        return this.overviewApi.getOverview().pipe(
          tap(() => this.loading.set(false)),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  // Drilled contents, keyed on the place AND the refresh tick so stock changes from a quick panel
  // re-fetch counts/badges in place — same shape as the list page's drillData.
  private drillKey = computed(() => ({ ref: this.placeRef(), tick: this.refreshTrigger() }));

  // The place the current placeContents value belongs to, to tell a place change (blank → spinner)
  // from a same-place refresh (keep the value, update in place). Null = root.
  private lastPlaceKey: string | null = null;

  placeContents = toSignal<PlaceContentsResponse | null, PlaceContentsResponse | null>(
    toObservable(this.drillKey).pipe(
      switchMap(({ ref }) => {
        const key = placeKey(ref);
        const placeChanged = key !== this.lastPlaceKey;
        this.lastPlaceKey = key;

        if (!ref) {
          this.drillLoading.set(false);
          this.drillError.set(false);
          return of(null);
        }
        this.drillLoading.set(true);
        this.drillError.set(false);
        const params = ref.kind === 'area'
          ? { AreaId: ref.id }
          : ref.kind === 'container'
            ? { ContainerItemId: ref.id }
            : { Unplaced: true };
        const fetch$ = this.placeApi.getPlaceContents(params).pipe(
          tap(() => this.drillLoading.set(false)),
          handleQueryError(this.drillLoading, this.drillError, null),
        );
        // Blank to the spinner only when drilling to a *different* place; a same-place refresh
        // keeps the prior contents on screen and swaps them when the new ones land.
        return placeChanged ? fetch$.pipe(startWith(null)) : fetch$;
      }),
    ),
    { initialValue: null },
  );

  /** Ancestor trail for the header breadcrumb — the API path of the drilled place. */
  protected path = computed(() => this.placeContents()?.path ?? []);

  /** Current-node label for the breadcrumb when the path is empty (Unplaced, or still loading). */
  protected placeName = computed(() => placeDisplayName(this.placeRef(), this.placeContents()));

  // A drilled place's scoped counts + items-list filter param, resolved once so the alert/status
  // scopes below stay thin adapters and the container counting lives in a single spot. Null at root
  // (each scope reads the overview blob directly there — they need different slices of it), on the
  // Unplaced drill, and until the place-contents fetch lands. The explicit return type keeps the
  // `{ area }`/`{ container }` literals from widening out of `Record<string, string>`.
  // Invariant: each count must equal exactly what its filter param shows in the items list.
  private drilledScope = computed<{ lowStockCount: number; lentCount: number; param: Record<string, string> } | null>(() => {
    const ref = this.placeRef();
    if (ref === null || ref.kind === 'unplaced') return null;
    const c = this.placeContents();
    if (!c) return null;
    if (ref.kind === 'area') {
      // Area: backend-resolved counts (the `?area=` filter resolves nested places).
      const param: Record<string, string> = { area: ref.id };
      return { lowStockCount: c.scopedLowStockCount, lentCount: c.scopedLentCount, param };
    }
    // Container: direct counts client-side (the `?container=` drill is direct), so each equals
    // exactly what its filter shows. Trade-off: nested sub-containers aren't counted.
    const param: Record<string, string> = { container: ref.id };
    return {
      lowStockCount: c.consumables.filter(s => s.consumable.isLow).length,
      lentCount: c.items.filter(i => i.status === 'Lent').length
               + c.containers.filter(cc => cc.status === 'Lent').length,
      param,
    };
  });

  // The alert region's scope: global at root (the full overview, for the low-stock previews),
  // place-scoped when drilled. Carries low stock only — lent is a neutral status surfaced by
  // statusScope, and unplaced is a location bucket surfaced as a place card. Null — render nothing —
  // until the backing fetch lands, and for Unplaced.
  protected warningScope = computed<WarningPanelsScope | null>(() => {
    if (this.placeRef() === null) {
      const ov = this.overview();
      return ov ? { kind: 'root', overview: ov } : null;
    }
    const d = this.drilledScope();
    return d ? { kind: 'place', lowStockCount: d.lowStockCount, viewAllParams: d.param } : null;
  });

  // The neutral status row's scope: lent (root + place). Decoupled from warningScope so the row
  // shows even with no alerts. Null until the backing fetch lands, and on the Unplaced drill
  // (nothing to surface).
  protected statusScope = computed<StatusChipsScope | null>(() => {
    if (this.placeRef() === null) {
      const ov = this.overview();
      return ov ? { kind: 'root', lentCount: ov.lentCount } : null;
    }
    const d = this.drilledScope();
    return d ? { kind: 'place', lentCount: d.lentCount, lentParams: { lent: 'true', ...d.param } } : null;
  });

  // The recency strip — root only (a drilled place is already spatially scoped). Null hides it: at a
  // drilled node, before the overview lands, and when nothing has been touched yet.
  protected recentlyUpdated = computed<RecentlyUpdatedItem[] | null>(() => {
    if (this.placeRef() !== null) return null;
    const ov = this.overview();
    return ov && ov.recentlyUpdated.length ? ov.recentlyUpdated : null;
  });

  quickPanelDurable = signal<DurableResponse | null>(null);
  durableQuickPanelState = signal<BrnDialogState>('closed');
  quickPanelConsumable = signal<ConsumableResponse | null>(null);
  consumableQuickPanelState = signal<BrnDialogState>('closed');

  constructor() {
    // Register the header breadcrumb while drilled, clear it at root (the route title "Browse"
    // shows there). Reading placeRef() re-fires this after the shell's NavigationStart auto-clear.
    effect(() => {
      const tpl = this.headerBreadcrumbRef();
      const drilled = this.placeRef() !== null;
      if (tpl && drilled) {
        this.pageHeaderService.setTitle(tpl);
      } else {
        this.pageHeaderService.clearTitle();
      }
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearTitle();
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  /** Hands the term off to the items list — no debounce, it's a one-shot navigation. */
  onSearchSubmit(event: Event): void {
    event.preventDefault();
    const term = this.searchInput().trim();
    void this.router.navigate(['/inventory/items'], { queryParams: { search: term || null } });
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

  /** The drilled container's "Details" button — the deliberate Explore→Detail step into its record. */
  onContainerDetails(durable: DurableResponse): void {
    void this.router.navigate(['/inventory/items', durable.id]);
  }
}
