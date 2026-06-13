import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';

/**
 * One drillable place card: root areas, the Unplaced pseudo-place, and child containers in a
 * drilled view. A real anchor (cmd-click, history entries) that merges its `?place=` target onto
 * the current URL. Container cards add a contents preview line and a chevron.
 */
@Component({
  selector: 'app-place-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideTriangleAlert, lucideChevronRight })],
  template: `
    <a
      [routerLink]="[]"
      [queryParams]="queryParams()"
      queryParamsHandling="merge"
      class="flex items-center justify-between gap-2 rounded-lg border border-border bg-card p-4 text-card-foreground transition-colors hover:bg-accent"
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
            class="flex items-center gap-1 rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-medium text-destructive tabular-nums"
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
  name = input.required<string>();
  icon = input.required<string>();
  count = input.required<number>();
  /** Badge hidden at 0. */
  lowStockCount = input(0);
  /** Muted truncated contents-preview line; hidden when empty. */
  previewNames = input<readonly string[]>([]);
  /** Merged onto the current URL — `{ place: 'area:<id>' | 'item:<id>' | 'unplaced' }`. */
  queryParams = input.required<Record<string, string>>();
  /** Container cards show a drill chevron. */
  chevron = input(false);
}
