import { Component, ChangeDetectionStrategy, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { tap } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideTriangleAlert,
  lucideUserRound,
  lucideMapPinOff,
  lucideSearch,
  lucideCircleCheck,
  lucideCircleAlert,
  lucideChevronRight,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { formatQty } from '../supplies/supply-shared';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

/**
 * One "needs attention" tile: icon + title + count, the projected preview rows, a "+N more"
 * overflow line, and a View-all link into the pre-filtered items list. A zero count swaps the
 * body for an all-good line and hides the link.
 */
@Component({
  selector: 'app-inventory-overview-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports],
  template: `
    @if (count() === 0) {
      <section class="flex h-full items-center gap-2 rounded-lg border border-border bg-card p-4 text-card-foreground">
        <ng-icon hlm name="lucideCircleCheck" size="sm" class="shrink-0 text-muted-foreground" />
        <h2 class="text-sm font-medium">{{ title() }}</h2>
        <span class="truncate text-sm text-muted-foreground">{{ emptyText() }}</span>
      </section>
    } @else {
      <a
        class="flex h-full flex-col rounded-lg border border-border bg-card p-4 text-card-foreground transition-colors hover:bg-accent"
        routerLink="/inventory/items"
        [queryParams]="viewAllParams()"
      >
        <div class="flex items-center gap-2">
          <ng-icon hlm [name]="icon()" size="sm" class="text-muted-foreground" />
          <h2 class="text-sm font-medium">{{ title() }}</h2>
          <ng-icon hlm name="lucideChevronRight" size="sm" class="ml-auto text-muted-foreground" />
        </div>
        <p class="mt-1 text-2xl font-semibold tabular-nums">{{ count() }}</p>
        <ul class="mt-2 flex-1 space-y-1">
          <ng-content />
        </ul>
        @if (count() > previewCount()) {
          <p class="mt-1 text-xs text-muted-foreground">+{{ count() - previewCount() }} more</p>
        }
      </a>
    }
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

/**
 * The Inventory triage dashboard at `/inventory`: a search box that hands the term off to the
 * unified items list, and three read-only "needs attention" tiles (low-stock, lent, unplaced).
 * Spatial navigation lives on its own surface — see InventoryBrowsePageComponent (`/inventory/browse`).
 */
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
  ],
  providers: [provideIcons({
    lucideTriangleAlert,
    lucideUserRound,
    lucideMapPinOff,
    lucideSearch,
    lucideCircleCheck,
    lucideCircleAlert,
    lucideChevronRight,
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
      }
    </div>
  `,
})
export class InventoryOverviewPageComponent {
  private overviewApi = inject(InventoryOverviewApiService);
  private router = inject(Router);

  protected readonly formatQty = formatQty;

  loading = signal(true);
  error = signal(false);

  searchInput = signal('');

  data = toSignal<GetInventoryOverviewResponse | null, GetInventoryOverviewResponse | null>(
    this.overviewApi.getOverview().pipe(
      tap(() => this.loading.set(false)),
      handleQueryError(this.loading, this.error, null),
    ),
    { initialValue: null },
  );

  /** Hands the term off to the items list — no debounce, it's a one-shot navigation. */
  onSearchSubmit(event: Event): void {
    event.preventDefault();
    const term = this.searchInput().trim();
    void this.router.navigate(['/inventory/items'], { queryParams: { search: term || null } });
  }
}
