import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { LOW_STOCK_BADGE_CLASS } from '../../shared/item-status-meta';

/**
 * One drillable place card: root areas, the Unplaced pseudo-place, and child containers in a
 * drilled view. A real anchor (cmd-click, history entries) routing to the place's own
 * `/inventory/browse/...` node. Container cards add a contents preview line and a chevron.
 */
@Component({
  selector: 'app-place-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideTriangleAlert, lucideChevronRight })],
  // Flex host so the anchor fills the grid cell's stretched height — cards in a row stay equal
  // height even when only some carry a contents-preview line.
  host: { class: 'flex' },
  template: `
    <a
      [routerLink]="link()"
      class="flex min-w-0 flex-1 items-center justify-between gap-2 rounded-lg border border-border bg-card p-4 text-card-foreground transition-colors hover:bg-accent"
    >
      <span class="flex min-w-0 flex-1 flex-col gap-0.5">
        <span class="flex min-w-0 items-center gap-2">
          <ng-icon hlm [name]="icon()" size="sm" class="shrink-0 text-muted-foreground" />
          <span class="truncate text-sm font-medium">{{ name() }}</span>
        </span>
        @if (previewNames().length > 0) {
          <span class="truncate text-xs text-muted-foreground">{{ previewNames().join(', ') }}</span>
        }
      </span>
      <span class="flex shrink-0 items-center gap-2">
        @if (lowStockCount() > 0) {
          <span
            [class]="lowStockBadgeClass"
            [attr.aria-label]="lowStockCount() + ' low stock'"
          >
            <ng-icon hlm name="lucideTriangleAlert" size="xs" />
            {{ lowStockCount() }}
          </span>
        }
        <span class="text-sm text-muted-foreground tabular-nums">{{ count() }}</span>
        @if (chevron()) {
          <ng-icon hlm name="lucideChevronRight" size="sm" class="text-muted-foreground" />
        }
      </span>
    </a>
  `,
})
export class PlaceCardComponent {
  /** Count badge needs tabular figures so multi-digit counts don't jitter. */
  protected readonly lowStockBadgeClass = `${LOW_STOCK_BADGE_CLASS} tabular-nums`;
  name = input.required<string>();
  icon = input.required<string>();
  count = input.required<number>();
  /** Badge hidden at 0. */
  lowStockCount = input(0);
  /** Muted truncated contents-preview line; hidden when empty. */
  previewNames = input<readonly string[]>([]);
  /** The place's own browse route — e.g. `['/inventory/browse/area', id]` (see {@link placeRefToLink}). */
  link = input.required<unknown[]>();
  /** Container cards show a drill chevron. */
  chevron = input(false);
}
