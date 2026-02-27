import { Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp, lucideArrowUpDown } from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { type HeaderContext, injectFlexRenderContext } from '@tanstack/angular-table';

@Component({
  selector: 'app-sort-header-button',
  imports: [HlmButton, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideArrowDown, lucideArrowUp, lucideArrowUpDown })],
  template: `
    <button hlmBtn size="sm" variant="ghost" class="-ml-3" (click)="handleClick()">
      {{ label() }}
      <ng-icon hlm size="sm" [name]="sortIcon()" />
    </button>
  `,
})
export class SortHeaderButton<T> {
  protected readonly context = injectFlexRenderContext<HeaderContext<T, unknown>>();
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
