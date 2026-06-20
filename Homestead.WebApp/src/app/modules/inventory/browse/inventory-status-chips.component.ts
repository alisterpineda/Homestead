import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUserRound, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';

/**
 * The Browse "browse by status" row: a neutral, always-on shortcut into non-empty slices of the
 * items list by lifecycle status. Unlike the "needs attention" alert frame, these are *not*
 * warnings — lending is deliberate — so the row carries no triangle and no alarm styling. It
 * simply renders one chip per status whose count is `> 0`; if none apply, the row doesn't appear
 * (it's navigation to non-empty slices, not a gate).
 *
 * Root-only: a household-wide Lent chip linking into `/inventory/items?lent=true`. A drilled place
 * is already spatially scoped (its lent items show in the contents table), so Browse hides the row
 * there rather than passing a place scope.
 */
export interface StatusChipsScope {
  lentCount: number;
}

@Component({
  selector: 'app-inventory-status-chips',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideUserRound, lucideChevronRight })],
  template: `
    @if (chips().length) {
      <section>
        <h2 class="mb-3 text-sm font-medium text-muted-foreground">Browse by status</h2>
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
      </section>
    }
  `,
})
export class InventoryStatusChipsComponent {
  scope = input.required<StatusChipsScope>();

  /** One chip per nonzero status — currently just Lent — linking into its items-list slice. */
  protected chips = computed<{ icon: string; label: string; count: number; params: Record<string, string> }[]>(() => {
    const s = this.scope();
    const chips: { icon: string; label: string; count: number; params: Record<string, string> }[] = [];
    if (s.lentCount > 0)
      chips.push({ icon: 'lucideUserRound', label: 'Lent out', count: s.lentCount, params: { lent: 'true' } });
    return chips;
  });
}
