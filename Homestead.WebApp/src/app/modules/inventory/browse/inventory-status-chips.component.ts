import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMapPinOff, lucideUserRound, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';

/**
 * The Browse "browse by status" row: a neutral, always-on shortcut into non-empty slices of the
 * items list by lifecycle status. Unlike the "needs attention" alert frame, these are *not*
 * warnings — lending is deliberate and plenty of items legitimately have no fixed place — so the
 * row carries no triangle and no alarm styling. It simply renders one chip per status whose count
 * is `> 0`; if none apply, the row doesn't appear (it's navigation to non-empty slices, not a gate).
 *
 * - **root** — household-wide Unplaced + Lent chips.
 * - **place** — Lent only, scoped to the drilled area/container (`lentParams` carries the place
 *   filter); Unplaced is the absence of a place, so the backend reports 0 when drilled.
 */
export type StatusChipsScope =
  | { kind: 'root'; unplacedCount: number; lentCount: number }
  | { kind: 'place'; lentCount: number; lentParams: Record<string, string> };

@Component({
  selector: 'app-inventory-status-chips',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideMapPinOff, lucideUserRound, lucideChevronRight })],
  template: `
    @if (chips().length) {
      <div class="flex flex-col gap-2">
        <span class="text-xs font-medium text-muted-foreground">Browse by status</span>
        <div class="flex flex-wrap gap-2">
          @for (chip of chips(); track chip.label) {
            <a
              class="flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm transition-colors hover:bg-accent"
              routerLink="/inventory/items"
              [queryParams]="chip.params"
            >
              <ng-icon hlm [name]="chip.icon" size="sm" class="shrink-0 text-muted-foreground" />
              <span class="font-medium">{{ chip.label }}</span>
              <span class="text-muted-foreground tabular-nums">{{ chip.count }}</span>
              <ng-icon hlm name="lucideChevronRight" size="sm" class="shrink-0 text-muted-foreground" />
            </a>
          }
        </div>
      </div>
    }
  `,
})
export class InventoryStatusChipsComponent {
  scope = input.required<StatusChipsScope>();

  /** One chip per nonzero status — Unplaced (root only) then Lent — reusing today's deep-links. */
  protected chips = computed<{ icon: string; label: string; count: number; params: Record<string, string> }[]>(() => {
    const s = this.scope();
    const chips: { icon: string; label: string; count: number; params: Record<string, string> }[] = [];
    if (s.kind === 'root' && s.unplacedCount > 0)
      chips.push({ icon: 'lucideMapPinOff', label: 'Unplaced', count: s.unplacedCount, params: { area: 'unplaced', kind: 'items' } });
    const lentParams = s.kind === 'root' ? { lent: 'true' } : s.lentParams;
    if (s.lentCount > 0)
      chips.push({ icon: 'lucideUserRound', label: 'Lent out', count: s.lentCount, params: lentParams });
    return chips;
  });
}
