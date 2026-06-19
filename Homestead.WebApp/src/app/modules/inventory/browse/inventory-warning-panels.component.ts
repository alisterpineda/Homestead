import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideTriangleAlert,
  lucideChevronRight,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { InventoryWarningSectionComponent } from './inventory-warning-section.component';
import { formatQty } from '../consumables/consumable-shared';
import { UnitPipe } from '../consumables/unit.pipe';
import { OUT_OF_STOCK_BADGE_CLASS } from '../shared/item-status-meta';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

/**
 * The Browse "needs attention" alert region: a conditional, collapsible block surfacing true
 * alerts — currently just **low stock** (a violation of a restock threshold the user set, with a
 * clear resolution). Neutral statuses are *not* alerts: lending lives in the always-on status-chips
 * row, while unplaced is a location bucket surfaced as a place card; the generic frame stays so
 * future alert types (expiring soon, overdue loans, warranty) can slot in. The single light card
 * holds stacked, flat alert sections (`InventoryWarningSectionComponent`) laid out in a content-aware
 * `auto-fit` grid — one section fills the row (matching the page's full-width rhythm), two-plus flow
 * into equal side-by-side columns, mobile collapses to one. Two modes:
 *
 * - **root** — the global household-wide low-stock section with a most-urgent-first preview, reusing
 *   the overview data the Browse root already fetches; the section deep-links into the unified items
 *   list pre-filtered to low stock. A zero-on-hand consumable is flagged "Out" to distinguish it from
 *   merely-low.
 * - **place** — a count-only low-stock section scoped to the drilled place, area or container ("low
 *   consumables stored here"). Its "View all" link carries the place filter (`viewAllParams`) so it
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
    NgIcon,
    HlmIconImports,
    HlmCollapsibleImports,
    InventoryWarningSectionComponent,
    UnitPipe,
  ],
  providers: [provideIcons({
    lucideTriangleAlert,
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
          <span
            class="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-muted-foreground tabular-nums"
            [class.hidden]="expanded()"
          >
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
            <div
              class="grid gap-x-8 gap-y-6 [grid-template-columns:repeat(auto-fit,minmax(min(100%,20rem),1fr))]"
            >
              @if (rootOverview(); as ov) {
                <app-inventory-warning-section
                  icon="lucideTriangleAlert"
                  label="Low stock"
                  [count]="ov.lowStockCount"
                  [previewCount]="ov.lowStockConsumables.length"
                  [viewAllParams]="{ lowStock: 'true' }"
                >
                  @for (consumable of ov.lowStockConsumables; track consumable.id) {
                    <li class="flex items-center justify-between gap-2 text-sm">
                      <span class="truncate">{{ consumable.name }}</span>
                      <span class="flex shrink-0 items-center gap-1.5">
                        @if (consumable.totalOnHand === 0) {
                          <span [class]="outBadgeClass" aria-label="Out of stock">Out</span>
                        }
                        <span class="text-xs text-muted-foreground tabular-nums">
                          {{ formatQty(consumable.totalOnHand) }} {{ consumable.unit | unit }} / {{ formatQty(consumable.restockThreshold) }}
                        </span>
                      </span>
                    </li>
                  }
                </app-inventory-warning-section>
              } @else {
                @for (panel of placePanels(); track panel.label) {
                  <app-inventory-warning-section
                    [icon]="panel.icon"
                    [label]="panel.label"
                    [count]="panel.count"
                    [viewAllParams]="panel.params"
                  />
                }
              }
            </div>
          </div>
        </div>
      </div>
    }
  `,
})
export class InventoryWarningPanelsComponent {
  scope = input.required<WarningPanelsScope>();

  protected readonly formatQty = formatQty;

  /** The "Out" badge for a zero-on-hand consumable (destructive tone). */
  protected readonly outBadgeClass = OUT_OF_STOCK_BADGE_CLASS;

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
      panels.push({ icon: 'lucideTriangleAlert', label: 'Low consumables stored here', count: s.lowStockCount, params: { ...s.viewAllParams, lowStock: 'true' } });
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
