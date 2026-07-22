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
import { lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadcrumbImports } from '@/shared/components/spartan/breadcrumb';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { PlaceBrowserComponent } from '../overview/place-browser/place-browser.component';
import { placeRefToLink, segmentToRef, placeKey, placeDisplayName, type PlaceRef } from '../overview/place-browser/place-param';
import { InventoryWarningPanelsComponent, type WarningPanelsScope } from './inventory-warning-panels.component';
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
    HlmSpinner,
    HlmEmptyImports,
    HlmBreadcrumbImports,
    PlaceBrowserComponent,
    InventoryWarningPanelsComponent,
    InventoryRecentlyUpdatedComponent,
    DurableQuickPanelComponent,
    ConsumableQuickPanelComponent,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
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

      <!-- The warning region is global at root, place-scoped when drilled. (Item search lives in the
           shell-wide command palette / ⌘K, so there is no per-page search box here.) -->
      @if (warningScope(); as scope) {
        <app-inventory-warning-panels [scope]="scope" />
      }

      <app-place-browser
        [overview]="overview()"
        [place]="placeRef()"
        [contents]="placeContents()"
        [error]="drillError()"
        (openDurable)="onOpenDurable($event)"
        (openConsumable)="onOpenConsumable($event)"
        (containerDetails)="onContainerDetails($event)"
        (viewContainer)="onViewContainer($event)"
        (changed)="onChanged()"
      />

      <!-- The recency axis (root only): demoted below the place grid so Browse leads with its point —
           the places. Carries full DTOs so a chip click opens the quick panel with no re-fetch. -->
      @if (recentlyUpdated(); as items) {
        <app-inventory-recently-updated
          [items]="items"
          (openDurable)="onOpenDurable($event)"
          (openConsumable)="onOpenConsumable($event)"
        />
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

  // A drilled place's scoped low-stock + lent counts and the items-list filter param, resolved once
  // so the alert scope below stays a thin adapter and the container counting lives in a single spot.
  // Null at root (warningScope reads the overview blob directly there), on the Unplaced drill, and
  // until the place-contents fetch lands. The explicit return type keeps the `{ area }` literal from
  // widening out of `Record<string, string>`.
  // Invariant: the low-stock count must equal exactly what its filter param shows in the items list.
  private drilledScope = computed<{ lowStockCount: number; lentCount: number; param: Record<string, string> | null } | null>(() => {
    const ref = this.placeRef();
    if (ref === null || ref.kind === 'unplaced') return null;
    const c = this.placeContents();
    if (!c) return null;
    if (ref.kind === 'area') {
      // Area: backend-resolved (rolled subtree) counts, low stock linkable into the items list via the
      // `?area=` filter (which resolves nested places) — so the count still equals exactly what its
      // "View all" opens. Lent is the same backend-resolved scoped count, shown count-only (no link).
      const param: Record<string, string> = { area: ref.id };
      return { lowStockCount: c.scopedLowStockCount, lentCount: c.scopedLentCount, param };
    }
    // Container: both counts are the backend's DIRECT scoped counts — for a container scope
    // `scopedLowStockCount`/`scopedLentCount` are the container's own direct contents (not the rolled
    // subtree), computed server-side so the one place that knows a container's direct contents also
    // counts them (including a child container that is itself Lent). The items list can't scope to a
    // container (Browse owns containment), so the panel is count-only (param null) — the matching rows
    // already render in the contents table below.
    return { lowStockCount: c.scopedLowStockCount, lentCount: c.scopedLentCount, param: null };
  });

  // The alert region's scope: global at root (the full overview, for the low-stock + lent previews),
  // place-scoped when drilled. Carries both low stock and lent — lent is folded into needs-attention
  // as a tile. Unplaced is a location bucket surfaced as a place card, not an alert. Null — render
  // nothing — until the backing fetch lands, and for Unplaced.
  protected warningScope = computed<WarningPanelsScope | null>(() => {
    if (this.placeRef() === null) {
      const ov = this.overview();
      return ov ? { kind: 'root', overview: ov } : null;
    }
    const d = this.drilledScope();
    return d ? { kind: 'place', lowStockCount: d.lowStockCount, lentCount: d.lentCount, viewAllParams: d.param } : null;
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

    // A non-container (an assembly) is not a Browse place. The container drill route still resolves one
    // (a deep link, or a breadcrumb segment for a non-container an item is nested under — segments carry
    // no container flag), so when the drilled scope turns out to be a non-container, bounce to its
    // detail page where its components live.
    effect(() => {
      const ref = this.placeRef();
      if (ref?.kind === 'container' && this.placeContents()?.containerItem?.isContainer === false) {
        void this.router.navigate(['/inventory/items', ref.id]);
      }
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearTitle();
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

  /**
   * A holder's "View contents/components" from the quick panel. A non-container (an assembly) is not a
   * Browse place, so it routes out to its detail page (where its components live); a container keeps
   * drilling in Browse.
   */
  onQuickPanelViewContents(durable: DurableResponse): void {
    this.onDurableQuickPanelClosed();
    void this.router.navigate(
      !durable.isContainer
        ? ['/inventory/items', durable.id]
        : ['/inventory/browse/container', durable.id],
    );
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

  /** A loose container row's "View contents" — keep drilling within Browse (Explore), not out to detail. */
  onViewContainer(containerId: string): void {
    void this.router.navigate(['/inventory/browse/container', containerId]);
  }
}
