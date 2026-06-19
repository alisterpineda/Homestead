import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { LOW_STOCK_BADGE_CLASS } from '../shared/item-status-meta';
import { formatQty } from '../consumables/consumable-shared';
import { UnitPipe } from '../consumables/unit.pipe';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import type { RecentlyUpdatedItem } from '@/api/generated/models/recently-updated-item';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';

/**
 * The Browse-root "Recently updated" strip: a quiet, compact row of the most-recently-touched live
 * items (durables + consumables mixed), giving the recency axis back into the inventory alongside
 * the text/status/space axes. Recency is the backend's *effective* last-activity time — the later of
 * an item's `UpdatedAt` and, for a consumable, its latest stock entry's — so a restock/move/consume
 * surfaces a consumable here even though it only touched its stock rows.
 *
 * The list arrives already ordered and filtered to live statuses by the overview query, so this
 * component does no client-side sort. Each chip is a *button* (not a router link): a click opens the
 * existing durable/consumable quick panel in place, so the user stays on the page. The full,
 * location-enriched DTO rides on each entry, so the durable panel renders its location with no
 * re-fetch. Built in the image of {@link InventoryStatusChipsComponent}.
 */
@Component({
  selector: 'app-inventory-recently-updated',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, ItemStatusBadgeComponent, UnitPipe],
  providers: [provideIcons({ lucideTriangleAlert })],
  template: `
    @if (items().length) {
      <section>
        <h2 class="mb-3 text-sm font-medium text-muted-foreground">Recently updated</h2>
        <div class="flex flex-wrap gap-2">
          @for (entry of items(); track entry.durable?.id ?? entry.consumable?.id ?? $index) {
            @if (entry.durable; as d) {
              <button
                type="button"
                class="flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm transition-colors hover:bg-accent"
                (click)="openDurable.emit(d)"
              >
                <span class="max-w-[12rem] truncate font-medium">{{ d.name }}</span>
                <app-item-status-badge [status]="d.status" />
                <span class="text-xs text-muted-foreground tabular-nums">{{ formatRelativeAge(entry.lastActivityAt) }}</span>
              </button>
            } @else if (entry.consumable; as c) {
              <button
                type="button"
                class="flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm transition-colors hover:bg-accent"
                (click)="openConsumable.emit(c)"
              >
                <span class="max-w-[12rem] truncate font-medium">{{ c.name }}</span>
                @if (c.isLow) {
                  <span [class]="lowStockBadgeClass">
                    <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                    Low
                  </span>
                }
                <span class="text-xs text-muted-foreground tabular-nums">{{ formatQty(c.totalOnHand) }} {{ c.unit | unit }}</span>
                <span class="text-xs text-muted-foreground tabular-nums">{{ formatRelativeAge(entry.lastActivityAt) }}</span>
              </button>
            }
          }
        </div>
      </section>
    }
  `,
})
export class InventoryRecentlyUpdatedComponent {
  /** Already ordered (newest first) and filtered to live statuses by the backend — no client sort. */
  items = input.required<RecentlyUpdatedItem[]>();

  openDurable = output<DurableResponse>();
  openConsumable = output<ConsumableResponse>();

  protected readonly formatRelativeAge = formatRelativeAge;
  protected readonly formatQty = formatQty;
  protected readonly lowStockBadgeClass = LOW_STOCK_BADGE_CLASS;
}
