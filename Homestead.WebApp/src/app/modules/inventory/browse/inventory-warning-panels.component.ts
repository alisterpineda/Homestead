import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideTriangleAlert,
  lucideCircleCheck,
  lucideChevronRight,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { InventoryOverviewTileComponent } from './inventory-overview-tile.component';
import { formatQty } from '../supplies/supply-shared';
import { BADGE_BASE } from '../shared/item-status-meta';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

/**
 * The Browse "needs attention" alert region: a conditional, collapsible block surfacing true
 * alerts — currently just **low stock** (a violation of a restock threshold the user set, with a
 * clear resolution). Neutral statuses (lent / unplaced) are *not* alerts and live in the always-on
 * status-chips row instead; the generic frame stays so future alert types (expiring soon, overdue
 * loans, warranty) can slot in. Two modes:
 *
 * - **root** — the global household-wide low-stock tile with a most-urgent-first preview, reusing
 *   the overview data the Browse root already fetches; the tile deep-links into the unified items
 *   list pre-filtered to low stock. A zero-on-hand supply is flagged "Out" to distinguish it from
 *   merely-low.
 * - **place** — a count-only low-stock panel scoped to the drilled place, area or container ("low
 *   supplies stored here"). Its "View all" link carries the place filter (`viewAllParams`) so it
 *   composes with the items-list deep-link reader — `{ area: id }` for an area, `{ container: id }`
 *   for a container.
 *
 * Renders nothing when the scope has no alerts, so a household with nothing running low sees a clean
 * Browse. Defaults open at root, collapsed when drilled — seeded on scope kind, so drilling
 * area→area keeps the user's toggle while root↔place re-seeds the default.
 */
export type WarningPanelsScope =
  | { kind: 'root'; overview: GetInventoryOverviewResponse }
  | { kind: 'place'; lowStockCount: number; viewAllParams: Record<string, string> };

@Component({
  selector: 'app-inventory-warning-panels',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmCollapsibleImports,
    InventoryOverviewTileComponent,
  ],
  providers: [provideIcons({
    lucideTriangleAlert,
    lucideCircleCheck,
    lucideChevronRight,
  })],
  template: `
    @if (hasWarnings()) {
      <div hlmCollapsible [(expanded)]="expanded" class="rounded-lg border border-border bg-card">
        <button
          hlmCollapsibleTrigger
          class="flex w-full items-center gap-2 px-4 py-3 text-left text-sm transition-colors hover:bg-accent rounded-lg data-[state=open]:rounded-b-none"
        >
          <ng-icon hlm name="lucideTriangleAlert" size="sm" class="shrink-0 text-muted-foreground" />
          <span class="font-medium">Needs attention</span>
          <span class="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-muted-foreground tabular-nums">
            @for (part of summaryParts(); track part.label) {
              <span class="flex items-center gap-1">
                <ng-icon hlm [name]="part.icon" size="xs" />
                {{ part.count }} {{ part.label }}
              </span>
            }
          </span>
          <ng-icon
            hlm
            name="lucideChevronRight"
            size="sm"
            class="ml-auto shrink-0 text-muted-foreground transition-transform"
            [class.rotate-90]="expanded()"
          />
        </button>

        <div hlmCollapsibleContent>
          <div class="border-t border-border p-4">
            @if (rootOverview(); as ov) {
              <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <app-inventory-overview-tile
                  icon="lucideTriangleAlert"
                  title="Low stock"
                  [count]="ov.lowStockCount"
                  [previewCount]="ov.lowStockSupplies.length"
                  emptyText="Nothing running low"
                  [viewAllParams]="{ lowStock: 'true' }"
                >
                  @for (supply of ov.lowStockSupplies; track supply.id) {
                    <li class="flex items-center justify-between gap-2 text-sm">
                      <span class="truncate">{{ supply.name }}</span>
                      <span class="flex shrink-0 items-center gap-1.5">
                        @if (supply.totalOnHand === 0) {
                          <span [class]="outBadgeClass" aria-label="Out of stock">Out</span>
                        }
                        <span class="text-xs text-muted-foreground tabular-nums">
                          {{ formatQty(supply.totalOnHand) }} {{ supply.unit }} / {{ formatQty(supply.restockThreshold) }}
                        </span>
                      </span>
                    </li>
                  }
                </app-inventory-overview-tile>
              </div>
            } @else {
              <div class="flex flex-col gap-2">
                @for (panel of placePanels(); track panel.label) {
                  <a
                    class="flex items-center gap-2 rounded-lg border border-border bg-card p-3 text-sm transition-colors hover:bg-accent"
                    routerLink="/inventory/items"
                    [queryParams]="panel.params"
                  >
                    <ng-icon hlm [name]="panel.icon" size="sm" class="shrink-0 text-muted-foreground" />
                    <span class="font-medium">{{ panel.label }}</span>
                    <span class="text-muted-foreground tabular-nums">{{ panel.count }}</span>
                    <ng-icon hlm name="lucideChevronRight" size="sm" class="ml-auto shrink-0 text-muted-foreground" />
                  </a>
                }
              </div>
            }
          </div>
        </div>
      </div>
    }
  `,
})
export class InventoryWarningPanelsComponent {
  scope = input.required<WarningPanelsScope>();

  protected readonly formatQty = formatQty;

  /** The "Out" badge: the shared pill base + a destructive tone, for a zero-on-hand supply. */
  protected readonly outBadgeClass = `${BADGE_BASE} bg-destructive/10 text-destructive dark:bg-destructive/20`;

  /** Narrowed views so the template gets type-safe access to each mode's data. */
  protected rootOverview = computed(() => {
    const s = this.scope();
    return s.kind === 'root' ? s.overview : null;
  });

  /**
   * The nonzero count-only panels for a drilled place (area or container) — low stock only. Kept as
   * a list (rather than a single conditional row) so future alert types slot in alongside it.
   */
  protected placePanels = computed<{ icon: string; label: string; count: number; params: Record<string, string> }[]>(() => {
    const s = this.scope();
    if (s.kind !== 'place') return [];
    const panels: { icon: string; label: string; count: number; params: Record<string, string> }[] = [];
    if (s.lowStockCount > 0)
      panels.push({ icon: 'lucideTriangleAlert', label: 'Low supplies stored here', count: s.lowStockCount, params: { ...s.viewAllParams, lowStock: 'true' } });
    return panels;
  });

  /** Hidden entirely when nothing needs attention — i.e. no low stock. */
  protected hasWarnings = computed(() => {
    const s = this.scope();
    return (s.kind === 'root' ? s.overview.lowStockCount : s.lowStockCount) > 0;
  });

  /** One chip per nonzero alert for the collapsed summary line (low stock today). */
  protected summaryParts = computed<{ icon: string; label: string; count: number }[]>(() => {
    const s = this.scope();
    const low = s.kind === 'root' ? s.overview.lowStockCount : s.lowStockCount;
    const parts: { icon: string; label: string; count: number }[] = [];
    if (low > 0) parts.push({ icon: 'lucideTriangleAlert', label: 'low', count: low });
    return parts;
  });

  // Open by default at the root (the household triage view), collapsed when drilled so the panels
  // don't fight the place browser for vertical space. Seeded on the scope KIND, so drilling
  // area→area preserves a user's manual toggle while root↔place re-seeds the default.
  protected expanded = seededLinkedSignal(
    computed(() => this.scope().kind),
    () => this.scope().kind === 'root',
  );
}
