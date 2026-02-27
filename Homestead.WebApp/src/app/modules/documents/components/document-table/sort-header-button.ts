import { Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUpDown } from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { type HeaderContext, injectFlexRenderContext } from '@tanstack/angular-table';

@Component({
  selector: 'app-sort-header-button',
  imports: [HlmButton, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideArrowUpDown })],
  template: `
    <button hlmBtn size="sm" variant="ghost" (click)="handleClick()">
      {{ label() }}
      <ng-icon hlm size="sm" name="lucideArrowUpDown" />
    </button>
  `,
})
export class SortHeaderButton<T> {
  protected readonly context = injectFlexRenderContext<HeaderContext<T, unknown>>();
  public readonly header = input('');
  protected readonly label = computed(() => this.header() || this.context.column.id);

  handleClick(): void {
    this.context.column.toggleSorting(this.context.column.getIsSorted() === 'asc');
  }
}
