import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideTriangleAlert,
  lucideChevronRight,
  lucideShieldAlert,
  lucideUserRound,
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
 * The Browse "needs attention" alert region: a conditional, collapsible block surfacing the things
 * that want the user's eye — **low stock** (a violation of a restock threshold the user set, with a
 * clear resolution), **lent out** (durables currently out on loan), and **warranty expiring** (live
 * durables whose latest cover runs out within 30 days). Lent is folded in here as an alert tile
 * rather than left as a standalone neutral status row, so the panel now renders whenever something
 * is lent even if nothing is low. (Unplaced is deliberately **not** a tile here — it stays
 * a location bucket surfaced as a place card; that diverges from the product-design doc, which lists
 * unplaced as a third needs-attention tile.) The
 * generic frame stays so future alert types (overdue loans, service due) can slot in. The
 * single light card holds stacked, flat alert sections (`InventoryWarningSectionComponent`) laid out
 * in a content-aware `auto-fit` grid — one section fills the row (matching the page's full-width
 * rhythm), two-plus flow into equal side-by-side columns, mobile collapses to one. Two modes:
 *
 * - **root** — the global household-wide low-stock section with a most-urgent-first preview, a
 *   lent-out section listing who has what, and a warranty-expiring section (soonest first), reusing
 *   the overview data the Browse root already fetches; each section deep-links into the unified items
 *   list pre-filtered to that slice (`?lowStock=true` / `?lent=true` / `?warranty=expiring`). A
 *   zero-on-hand consumable is flagged "Out" to distinguish it from merely-low.
 * - **place** — count-only sections scoped to the drilled place ("low consumables stored here" /
 *   "items lent from here"). An **area** drill carries a `{ area: id }` filter, so its low-stock
 *   "View all" links into the pre-filtered items list; lent is always count-only (Browse already
 *   scopes spatially, and the lent items show in the contents table). A **container** drill carries
 *   `null` for low stock too (the list no longer scopes to a container — Browse owns containment), so
 *   that section renders count-only with no link. Warranties are root-scope only: the overview
 *   payload carries no per-place warranty count, and a spatial slice of "expiring soon" is not a
 *   question anyone asks.
 *
 * Renders nothing when the scope has no alerts, so a household with nothing running low, nothing
 * lent, and no cover about to lapse sees a clean Browse. Defaults collapsed in every scope — seeded on scope kind, so drilling
 * area→area keeps the user's toggle while root↔place re-seeds the collapsed default.
 */
export type WarningPanelsScope =
  | { kind: 'root'; overview: GetInventoryOverviewResponse }
  | { kind: 'place'; lowStockCount: number; lentCount: number; viewAllParams: Record<string, string> | null };

@Component({
  selector: 'app-inventory-warning-panels',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmCollapsibleImports,
    InventoryWarningSectionComponent,
    UnitPipe,
    DatePipe,
  ],
  providers: [provideIcons({
    lucideTriangleAlert,
    lucideChevronRight,
    lucideShieldAlert,
    lucideUserRound,
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
                @if (ov.lowStockCount > 0) {
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
                }
                @if (ov.lentCount > 0) {
                  <app-inventory-warning-section
                    icon="lucideUserRound"
                    label="Lent out"
                    [count]="ov.lentCount"
                    [previewCount]="ov.lentDurables.length"
                    [viewAllParams]="{ lent: 'true' }"
                  >
                    @for (durable of ov.lentDurables; track durable.id) {
                      <li class="flex items-center justify-between gap-2 text-sm">
                        <span class="truncate">{{ durable.name }}</span>
                        @if (durable.lentTo) {
                          <span class="shrink-0 truncate text-xs text-muted-foreground">to {{ durable.lentTo }}</span>
                        }
                      </li>
                    }
                  </app-inventory-warning-section>
                }
                @if (ov.expiringWarrantyCount > 0) {
                  <app-inventory-warning-section
                    icon="lucideShieldAlert"
                    label="Warranty expiring"
                    [count]="ov.expiringWarrantyCount"
                    [previewCount]="ov.expiringWarranties.length"
                    [viewAllParams]="{ warranty: 'expiring' }"
                  >
                    @for (item of ov.expiringWarranties; track item.id) {
                      <li class="flex items-center justify-between gap-2 text-sm">
                        <span class="truncate">{{ item.name }}</span>
                        <span class="shrink-0 text-xs text-muted-foreground">until {{ item.expiresOn | date: 'mediumDate' }}</span>
                      </li>
                    }
                  </app-inventory-warning-section>
                }
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
   * The nonzero count-only panels for a drilled place (area or container) — low stock + lent. Kept as
   * a list (rather than a single conditional row) so future alert types slot in alongside it.
   */
  protected placePanels = computed<{ icon: string; label: string; count: number; params: Record<string, string> | null }[]>(() => {
    const s = this.scope();
    if (s.kind !== 'place') return [];
    const panels: { icon: string; label: string; count: number; params: Record<string, string> | null }[] = [];
    if (s.lowStockCount > 0)
      panels.push({
        icon: 'lucideTriangleAlert',
        label: 'Low consumables stored here',
        count: s.lowStockCount,
        // Area drill → linkable (`?area=…&lowStock=true`); container drill (null) → count-only.
        params: s.viewAllParams ? { ...s.viewAllParams, lowStock: 'true' } : null,
      });
    // Lent is count-only here: Browse already scopes spatially, and the lent items show in the
    // contents table below — no items-list deep-link needed.
    if (s.lentCount > 0)
      panels.push({ icon: 'lucideUserRound', label: 'Items lent from here', count: s.lentCount, params: null });
    return panels;
  });

  /** The scope's alert counts, narrowed once from whichever mode is active — the single source for
   *  the hidden-when-empty gate and the collapsed summary line. */
  private counts = computed(() => {
    const s = this.scope();
    return s.kind === 'root'
      ? { low: s.overview.lowStockCount, lent: s.overview.lentCount, warranty: s.overview.expiringWarrantyCount }
      // Warranties are a root-scope alert only — a drilled place carries no warranty count.
      : { low: s.lowStockCount, lent: s.lentCount, warranty: 0 };
  });

  /** Hidden entirely when nothing needs attention — no low stock, nothing lent, no expiring warranty. */
  protected hasWarnings = computed(() => {
    const { low, lent, warranty } = this.counts();
    return low > 0 || lent > 0 || warranty > 0;
  });

  /** One chip per nonzero alert for the collapsed summary line (e.g. "3 low · 1 lent"). */
  protected summaryParts = computed<{ icon: string; label: string; count: number }[]>(() => {
    const { low, lent, warranty } = this.counts();
    const parts: { icon: string; label: string; count: number }[] = [];
    if (low > 0) parts.push({ icon: 'lucideTriangleAlert', label: 'low', count: low });
    if (lent > 0) parts.push({ icon: 'lucideUserRound', label: 'lent', count: lent });
    if (warranty > 0) parts.push({ icon: 'lucideShieldAlert', label: 'expiring', count: warranty });
    return parts;
  });

  // Collapsed by default in every scope so the panels don't fight the page for vertical space; the
  // user opens it on demand. Seeded on the scope KIND, so drilling area→area preserves a user's
  // manual toggle while root↔place re-seeds the collapsed default.
  protected expanded = seededLinkedSignal(
    computed(() => this.scope().kind),
    () => false,
  );
}
