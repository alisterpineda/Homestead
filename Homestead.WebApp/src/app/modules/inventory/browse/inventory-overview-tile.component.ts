import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { HlmIconImports } from '@/shared/components/spartan/icon';

/**
 * One "needs attention" tile: icon + title + count, the projected preview rows, a "+N more"
 * overflow line, and a View-all link into the pre-filtered items list. A zero count swaps the
 * body for an all-good line and hides the link. Used by the Browse warning panels at the root,
 * where the panels are global (household-wide). Icons are provided by the host component.
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
