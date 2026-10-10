import { Component, computed, input, ChangeDetectionStrategy } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp, lucideArrowUpDown } from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import {
  type HeaderContext,
  injectFlexRenderContext,
  type RowData,
  type rowSortingFeature,
} from '@tanstack/angular-table';

type SortFeatures = { rowSortingFeature: typeof rowSortingFeature };

@Component({
  selector: 'app-sort-header-button',
  imports: [HlmButton, NgIcon],
  providers: [provideIcons({ lucideArrowDown, lucideArrowUp, lucideArrowUpDown })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button hlmBtn size="sm" variant="ghost" class="-ml-3" (click)="handleClick()">
      {{ label() }}
      <ng-icon class="text-[length:--spacing(4)]" [name]="sortIcon()" />
    </button>
  `,
})
export class SortHeaderButton<T extends RowData> {
  protected readonly context = injectFlexRenderContext<HeaderContext<SortFeatures, T, unknown>>();
  public readonly header = input('');
  protected readonly label = computed(() => this.header() || this.context.column.id);
  protected sortIcon(): string {
    const sorted = this.context.column.getIsSorted();
    if (sorted === 'asc') return 'lucideArrowUp';
    if (sorted === 'desc') return 'lucideArrowDown';
    return 'lucideArrowUpDown';
  }

  handleClick(): void {
    this.context.column.toggleSorting(this.context.column.getIsSorted() === 'asc');
  }
}
