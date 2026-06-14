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
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { PlaceBrowserComponent } from '../overview/place-browser/place-browser.component';
import { placeRefToLink, segmentToRef, placeKey, placeDisplayName, type PlaceRef } from '../overview/place-browser/place-param';
import { ItemQuickPanelComponent } from '../items/item-quick-panel/item-quick-panel.component';
import { SupplyQuickPanelComponent } from '../supplies/supply-quick-panel/supply-quick-panel.component';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
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
    HlmBreadCrumbImports,
    PlaceBrowserComponent,
    ItemQuickPanelComponent,
    SupplyQuickPanelComponent,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerBreadcrumb>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/inventory/browse">All places</a>
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

      <app-place-browser
        [overview]="overview()"
        [place]="placeRef()"
        [contents]="placeContents()"
        [error]="drillError()"
        (openItem)="onOpenItem($event)"
        (openSupply)="onOpenSupply($event)"
        (containerDetails)="onOpenItem($event)"
        (changed)="onChanged()"
      />
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
    // Containers are no longer a browse node — they drill into their detail page — so this surface
    // only ever resolves to an area, Unplaced, or the root grid.
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

  quickPanelItem = signal<ItemResponse | null>(null);
  itemQuickPanelState = signal<BrnDialogState>('closed');
  quickPanelSupply = signal<SupplyResponse | null>(null);
  supplyQuickPanelState = signal<BrnDialogState>('closed');

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

  onOpenItem(item: ItemResponse): void {
    this.quickPanelItem.set(item);
    this.itemQuickPanelState.set('open');
  }

  onOpenSupply(supply: SupplyResponse): void {
    this.quickPanelSupply.set(supply);
    this.supplyQuickPanelState.set('open');
  }

  onItemQuickPanelClosed(): void {
    this.itemQuickPanelState.set('closed');
  }

  // Keeps quickPanelSupply set: a close-triggered stepper flush may still be committing, and the
  // panel needs the supply id until it settles. Reopening reseeds the panel via its open-keyed state.
  onSupplyQuickPanelClosed(): void {
    this.supplyQuickPanelState.set('closed');
  }

  /** "View contents" in the item panel opens that container's detail page. */
  onQuickPanelViewContents(item: ItemResponse): void {
    this.onItemQuickPanelClosed();
    void this.router.navigate(['/inventory/items', item.id]);
  }
}
