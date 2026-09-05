import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { LOW_STOCK_BADGE_CLASS } from '../shared/item-status-meta';
import { formatQty } from '../consumables/consumable-shared';
import { UnitPipe } from '../consumables/unit.pipe';
import { formatPath, consumableLocationText, type InventorySortId } from '../list/inventory-row';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import type { RecentlyUpdatedItem } from '@/api/generated/models/recently-updated-item';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';

/**
 * The Browse-root "Recently updated" shortcut: a quiet, scannable row list of the most-recently-touched
 * live items (durables + consumables mixed), giving the recency axis back alongside the
 * text/status/space axes. Recency is the backend's *effective* last-activity time — the later of an
 * item's `UpdatedAt` and, for a consumable, its latest stock entry's — so a restock/move/consume
 * surfaces a consumable here even though it only touched its stock rows.
 *
 * Each row carries its resolved **location** (on-theme for Browse), and the section header is a
 * "View all →" bridge into the unified items list sorted by Updated-descending — the full, sortable
 * recency view lives there, so this stays a lightweight shortcut, not a second table.
 *
 * The list arrives already ordered and filtered to live statuses by the overview query, so this
 * component does no client-side sort. Each row is a *button* (not a router link): a click opens the
 * existing durable/consumable quick panel in place, so the user stays on the page. The full,
 * location-enriched DTO rides on each entry, so a panel renders its location with no re-fetch.
 */
@Component({
  selector: 'app-inventory-recently-updated',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, ItemStatusBadgeComponent, UnitPipe],
  providers: [provideIcons({ lucideTriangleAlert, lucideChevronRight })],
  template: `
    @if (items().length) {
      <section class="flex flex-col gap-2">
        <a
          class="-mx-2 flex items-center gap-2 rounded-md px-2 py-1 transition-colors hover:bg-accent"
          routerLink="/inventory/items"
          [queryParams]="viewAllParams"
        >
          <h2 class="text-sm font-medium text-muted-foreground">Recently updated</h2>
          <span class="ml-auto flex shrink-0 items-center gap-1 text-xs font-medium text-muted-foreground">
            View all <ng-icon hlm name="lucideChevronRight" size="xs" />
          </span>
        </a>
        <ul class="space-y-1">
          @for (entry of items(); track entry.durable?.id ?? entry.consumable?.id ?? $index) {
            <li>
              @if (entry.durable; as d) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors hover:bg-accent"
                  (click)="openDurable.emit(d)"
                >
                  <span class="flex min-w-0 items-center gap-2">
                    <span class="truncate font-medium">{{ d.name }}</span>
                    <app-item-status-badge [status]="d.status" [note]="d.statusNote" />
                  </span>
                  <span class="flex shrink-0 items-center gap-1.5 text-xs text-muted-foreground tabular-nums">
                    <span class="hidden sm:inline">{{ formatPath(d) }}</span>
                    <span>{{ formatRelativeAge(entry.lastActivityAt) }}</span>
                  </span>
                </button>
              } @else if (entry.consumable; as c) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors hover:bg-accent"
                  (click)="openConsumable.emit(c)"
                >
                  <span class="flex min-w-0 items-center gap-2">
                    <span class="truncate font-medium">{{ c.name }}</span>
                    @if (c.isLow) {
                      <span [class]="lowStockBadgeClass">
                        <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                        Low
                      </span>
                    }
                  </span>
                  <span class="flex shrink-0 items-center gap-1.5 text-xs text-muted-foreground tabular-nums">
                    <span class="hidden sm:inline">{{ consumableLocationText(c.stockLocations ?? []) }}</span>
                    <span>{{ formatQty(c.totalOnHand) }} {{ c.unit | unit }}</span>
                    <span>{{ formatRelativeAge(entry.lastActivityAt) }}</span>
                  </span>
                </button>
              }
            </li>
          }
        </ul>
      </section>
    }
  `,
})
export class InventoryRecentlyUpdatedComponent {
  /** Already ordered (newest first) and filtered to live statuses by the backend — no client sort. */
  items = input.required<RecentlyUpdatedItem[]>();

  openDurable = output<DurableResponse>();
  openConsumable = output<ConsumableResponse>();

  /** "View all →" deep-link: the unified items list sorted by Updated-descending (the full recency view). */
  readonly viewAllParams: { sort: InventorySortId; dir: 'asc' | 'desc' } = { sort: 'updatedAt', dir: 'desc' };

  protected readonly formatPath = formatPath;
  protected readonly consumableLocationText = consumableLocationText;
  protected readonly formatRelativeAge = formatRelativeAge;
  protected readonly formatQty = formatQty;
  protected readonly lowStockBadgeClass = LOW_STOCK_BADGE_CLASS;
}
