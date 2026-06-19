import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { HlmIconImports } from '@/shared/components/spartan/icon';

/**
 * One "needs attention" alert section: a flat header row (icon + label + count + a View-all
 * deep-link into the pre-filtered items list), the projected preview rows, and a "+N more"
 * overflow line. No card surface of its own — the host warning panel supplies the single card
 * chrome, and these sections stack flat inside it. The header row is the sole deep-link.
 *
 * Reusable per alert type: today only low stock exists, but expiring / overdue-loan / warranty
 * sections slot in as additional siblings. A count-only section (no projected rows, `previewCount`
 * left at 0) renders just the header. Icons are provided by the host component.
 */
@Component({
  selector: 'app-inventory-warning-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports],
  template: `
    <section class="flex flex-col gap-2">
      <a
        class="-mx-2 flex items-center gap-2 rounded-md px-2 py-1 transition-colors hover:bg-accent"
        routerLink="/inventory/items"
        [queryParams]="viewAllParams()"
      >
        <ng-icon hlm [name]="icon()" size="sm" class="shrink-0 text-muted-foreground" />
        <h3 class="text-sm font-medium">{{ label() }}</h3>
        <span class="text-xs text-muted-foreground tabular-nums">{{ count() }}</span>
        <span class="ml-auto flex shrink-0 items-center gap-1 text-xs font-medium text-muted-foreground">
          View all <ng-icon hlm name="lucideChevronRight" size="xs" />
        </span>
      </a>
      @if (previewCount() > 0) {
        <ul class="space-y-1"><ng-content /></ul>
        @if (count() > previewCount()) {
          <p class="text-xs text-muted-foreground">+{{ count() - previewCount() }} more</p>
        }
      }
    </section>
  `,
})
export class InventoryWarningSectionComponent {
  icon = input.required<string>();
  label = input.required<string>();
  count = input.required<number>();
  /** Length of the projected preview list — drives the "+N more" overflow line; 0 = count-only. */
  previewCount = input(0);
  viewAllParams = input.required<Record<string, string>>();
}
