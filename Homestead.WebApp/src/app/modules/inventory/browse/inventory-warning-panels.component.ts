import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideTriangleAlert,
  lucideUserRound,
  lucideMapPinOff,
  lucideCircleCheck,
  lucideChevronRight,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { InventoryOverviewTileComponent } from './inventory-overview-tile.component';
import { formatQty } from '../supplies/supply-shared';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

/**
 * The Browse "needs attention" warning region: a conditional, collapsible block surfacing
 * low-stock / lent / unplaced triage. Two modes:
 *
 * - **root** — the global household-wide tiles (low-stock, lent, unplaced) with most-urgent-first
 *   previews, reusing the overview data the Browse root already fetches; each tile deep-links into
 *   the unified items list pre-filtered to that lens.
 * - **place** — count-only panels scoped to the drilled place, area or container ("low supplies
 *   stored here" + "items lent from here"; no unplaced panel — that's the absence of a place). Each
 *   "View all" link carries the place filter (`viewAllParams`) so it composes with the items-list
 *   deep-link reader — `{ area: id }` for an area, `{ container: id }` for a container.
 *
 * Renders nothing when the scope has zero warnings, so a healthy household sees a clean Browse.
 * Defaults open at root, collapsed when drilled — seeded on scope kind, so drilling area→area keeps
 * the user's toggle while root↔place re-seeds the default.
 */
export type WarningPanelsScope =
  | { kind: 'root'; overview: GetInventoryOverviewResponse }
  | { kind: 'place'; lowStockCount: number; lentCount: number; viewAllParams: Record<string, string> };

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
    lucideUserRound,
    lucideMapPinOff,
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
                      <span class="shrink-0 text-xs text-muted-foreground tabular-nums">
                        {{ formatQty(supply.totalOnHand) }} {{ supply.unit }} / {{ formatQty(supply.restockThreshold) }}
                      </span>
                    </li>
                  }
                </app-inventory-overview-tile>

                <app-inventory-overview-tile
                  icon="lucideUserRound"
                  title="Lent out"
                  [count]="ov.lentCount"
                  [previewCount]="ov.lentItems.length"
                  emptyText="Nothing lent out"
                  [viewAllParams]="{ lent: 'true' }"
                >
                  @for (item of ov.lentItems; track item.id) {
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
                  [count]="ov.unplacedCount"
                  [previewCount]="ov.unplacedItems.length"
                  emptyText="Everything has a place"
                  [viewAllParams]="{ area: 'unplaced', kind: 'items' }"
                >
                  @for (item of ov.unplacedItems; track item.id) {
                    <li class="truncate text-sm">{{ item.name }}</li>
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

  /** Narrowed views so the template gets type-safe access to each mode's data. */
  protected rootOverview = computed(() => {
    const s = this.scope();
    return s.kind === 'root' ? s.overview : null;
  });

  /** The nonzero count-only panels for a drilled place (area or container) — low-stock + lent. */
  protected placePanels = computed<{ icon: string; label: string; count: number; params: Record<string, string> }[]>(() => {
    const s = this.scope();
    if (s.kind !== 'place') return [];
    const panels: { icon: string; label: string; count: number; params: Record<string, string> }[] = [];
    if (s.lowStockCount > 0)
      panels.push({ icon: 'lucideTriangleAlert', label: 'Low supplies stored here', count: s.lowStockCount, params: { ...s.viewAllParams, lowStock: 'true' } });
    if (s.lentCount > 0)
      panels.push({ icon: 'lucideUserRound', label: 'Items lent from here', count: s.lentCount, params: { ...s.viewAllParams, lent: 'true' } });
    return panels;
  });

  /** Hidden entirely when nothing needs attention (root: low+lent+unplaced; place: low+lent). */
  protected hasWarnings = computed(() => {
    const s = this.scope();
    return s.kind === 'root'
      ? s.overview.lowStockCount + s.overview.lentCount + s.overview.unplacedCount > 0
      : s.lowStockCount + s.lentCount > 0;
  });

  /** One chip per nonzero warning for the collapsed summary line. */
  protected summaryParts = computed<{ icon: string; label: string; count: number }[]>(() => {
    const s = this.scope();
    const low = s.kind === 'root' ? s.overview.lowStockCount : s.lowStockCount;
    const lent = s.kind === 'root' ? s.overview.lentCount : s.lentCount;
    const unplaced = s.kind === 'root' ? s.overview.unplacedCount : 0;
    const parts: { icon: string; label: string; count: number }[] = [];
    if (low > 0) parts.push({ icon: 'lucideTriangleAlert', label: 'low', count: low });
    if (lent > 0) parts.push({ icon: 'lucideUserRound', label: 'lent', count: lent });
    if (unplaced > 0) parts.push({ icon: 'lucideMapPinOff', label: 'unplaced', count: unplaced });
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
