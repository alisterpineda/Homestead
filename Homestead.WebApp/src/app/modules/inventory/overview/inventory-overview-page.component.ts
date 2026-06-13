import { Component, ChangeDetectionStrategy, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, tap, startWith } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideTriangleAlert,
  lucideUserRound,
  lucideMapPin,
  lucideMapPinOff,
  lucideSearch,
  lucideCircleCheck,
  lucideCircleAlert,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { formatQty } from '../supplies/supply-shared';
import { PlaceBrowserComponent } from './place-browser/place-browser.component';
import { parsePlaceParam, formatPlaceParam, type PlaceRef } from './place-browser/place-param';
import { ItemQuickPanelComponent } from '../items/item-quick-panel/item-quick-panel.component';
import { SupplyQuickPanelComponent } from '../supplies/supply-quick-panel/supply-quick-panel.component';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/**
 * One "needs attention" tile: icon + title + count, the projected preview rows, a "+N more"
 * overflow line, and a View-all link into the pre-filtered items list. A zero count swaps the
 * body for an all-good line and hides the link.
 */
@Component({
  selector: 'app-inventory-overview-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmButton],
  template: `
    <section class="flex h-full flex-col rounded-lg border border-border bg-card p-4 text-card-foreground">
      <div class="flex items-center gap-2">
        <ng-icon hlm [name]="icon()" size="sm" class="text-muted-foreground" />
        <h2 class="text-sm font-medium">{{ title() }}</h2>
      </div>
      <p class="mt-1 text-2xl font-semibold tabular-nums">{{ count() }}</p>
      @if (count() === 0) {
        <p class="mt-2 flex items-center gap-1.5 text-sm text-muted-foreground">
          <ng-icon hlm name="lucideCircleCheck" size="sm" />
          {{ emptyText() }}
        </p>
      } @else {
        <ul class="mt-2 flex-1 space-y-1">
          <ng-content />
        </ul>
        @if (count() > previewCount()) {
          <p class="mt-1 text-xs text-muted-foreground">+{{ count() - previewCount() }} more</p>
        }
        <a
          hlmBtn
          variant="outline"
          size="sm"
          class="mt-3 self-start"
          routerLink="/inventory/items"
          [queryParams]="viewAllParams()"
        >View all</a>
      }
    </section>
  `,
})
export class InventoryOverviewTileComponent {
  icon = input.required<string>();
  title = input.required<string>();
  count = input.required<number>();
  /** Length of the projected preview list — drives the "+N more" overflow line. */
  previewCount = input.required<number>();
  emptyText = input.required<string>();
  viewAllParams = input.required<Record<string, string>>();
}

@Component({
  selector: 'app-inventory-overview-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmInput,
    HlmSpinner,
    HlmEmptyImports,
    InventoryOverviewTileComponent,
    PlaceBrowserComponent,
    ItemQuickPanelComponent,
    SupplyQuickPanelComponent,
  ],
  providers: [provideIcons({
    lucideTriangleAlert,
    lucideUserRound,
    lucideMapPin,
    lucideMapPinOff,
    lucideSearch,
    lucideCircleCheck,
    lucideCircleAlert,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      <form class="relative mx-auto w-full max-w-xl" (submit)="onSearchSubmit($event)">
        <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
        <input
          hlmInput
          placeholder="Search items and supplies..."
          class="h-10 w-full pl-9"
          aria-label="Search items and supplies"
          [value]="searchInput()"
          (input)="searchInput.set($any($event.target).value)"
        />
      </form>

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
            <div hlmEmptyTitle>Failed to load overview</div>
            <div hlmEmptyDescription>There was an error loading the inventory overview. Please try again.</div>
          </div>
        </div>
      }

      @if (data(); as overview) {
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <app-inventory-overview-tile
            icon="lucideTriangleAlert"
            title="Low stock"
            [count]="overview.lowStockCount"
            [previewCount]="overview.lowStockSupplies.length"
            emptyText="Nothing running low"
            [viewAllParams]="{ lowStock: 'true' }"
          >
            @for (supply of overview.lowStockSupplies; track supply.id) {
              <li class="flex items-center justify-between gap-2 text-sm">
                <span class="truncate">{{ supply.name }}</span>
                <span class="shrink-0 text-xs text-muted-foreground tabular-nums">
                  {{ formatQty(supply.totalOnHand) }} {{ supply.unit }} / {{ formatQty(supply.restockThreshold) }}
                </span>
              </li>
            }
          </app-inventory-overview-tile>

          <app-inventory-overview-tile
            icon="lucideUserRound"
            title="Lent out"
            [count]="overview.lentCount"
            [previewCount]="overview.lentItems.length"
            emptyText="Nothing lent out"
            [viewAllParams]="{ lent: 'true' }"
          >
            @for (item of overview.lentItems; track item.id) {
              <li class="flex items-center justify-between gap-2 text-sm">
                <span class="truncate">{{ item.name }}</span>
                @if (item.lentTo) {
                  <span class="shrink-0 truncate text-xs text-muted-foreground">{{ item.lentTo }}</span>
                }
              </li>
            }
          </app-inventory-overview-tile>

          <app-inventory-overview-tile
            icon="lucideMapPinOff"
            title="Unplaced"
            [count]="overview.unplacedCount"
            [previewCount]="overview.unplacedItems.length"
            emptyText="Everything has a place"
            [viewAllParams]="{ area: 'unplaced', kind: 'items' }"
          >
            @for (item of overview.unplacedItems; track item.id) {
              <li class="truncate text-sm">{{ item.name }}</li>
            }
          </app-inventory-overview-tile>
        </div>

        <app-place-browser
          [overview]="overview"
          [place]="placeRef()"
          [contents]="placeContents()"
          [error]="drillError()"
          (openItem)="onOpenItem($event)"
          (openSupply)="onOpenSupply($event)"
          (containerDetails)="onOpenItem($event)"
          (changed)="onChanged()"
        />
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
  `,
})
export class InventoryOverviewPageComponent {
  private overviewApi = inject(InventoryOverviewApiService);
  private placeApi = inject(InventoryPlaceApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  protected readonly formatQty = formatQty;

  loading = signal(true);
  error = signal(false);
  drillLoading = signal(false);
  drillError = signal(false);

  searchInput = signal('');

  // Refresh-keyed: the quick panels and the create dialog mutate from this page, so the tiles
  // and place cards must refetch on `changed` (the old one-shot fetch predates in-place actions).
  refreshTrigger = signal(0);

  data = toSignal<GetInventoryOverviewResponse | null, GetInventoryOverviewResponse | null>(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        return this.overviewApi.getOverview().pipe(
          tap(() => this.loading.set(false)),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  /** The raw `?place=` param — the source of truth for the drill-down (two-way, no replaceUrl). */
  private placeParam = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('place'))),
    { initialValue: this.route.snapshot.queryParamMap.get('place') },
  );

  placeRef = computed<PlaceRef>(() => parsePlaceParam(this.placeParam() ?? null));

  // Drilled contents, keyed on the place AND the refresh tick so stock changes from a quick panel
  // re-fetch counts/badges in place — same shape as the list page's drillData.
  private drillKey = computed(() => ({ ref: this.placeRef(), tick: this.refreshTrigger() }));

  // The place the current placeContents value belongs to, to tell a place change (blank → spinner)
  // from a same-place refresh (keep the value, update in place). Null = root.
  private lastPlaceKey: string | null = null;

  placeContents = toSignal<PlaceContentsResponse | null, PlaceContentsResponse | null>(
    toObservable(this.drillKey).pipe(
      switchMap(({ ref }) => {
        const key = formatPlaceParam(ref);
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

  quickPanelItem = signal<ItemResponse | null>(null);
  itemQuickPanelState = signal<BrnDialogState>('closed');
  quickPanelSupply = signal<SupplyResponse | null>(null);
  supplyQuickPanelState = signal<BrnDialogState>('closed');

  /** Hands the term off to the items list — no debounce, it's a one-shot navigation. */
  onSearchSubmit(event: Event): void {
    event.preventDefault();
    const term = this.searchInput().trim();
    void this.router.navigate(['/inventory/items'], { queryParams: { search: term || null } });
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

  /** "View contents" in the item panel drills the browser into that container. */
  onQuickPanelViewContents(item: ItemResponse): void {
    this.onItemQuickPanelClosed();
    this.navigatePlace({ kind: 'container', id: item.id });
  }

  /** Single URL-sync point for `?place=` — real navigations (no replaceUrl) so back/forward work. */
  private navigatePlace(ref: PlaceRef): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { place: formatPlaceParam(ref) },
      queryParamsHandling: 'merge',
    });
  }
}
