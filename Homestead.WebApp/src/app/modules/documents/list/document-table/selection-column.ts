import { Component, computed } from '@angular/core';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { type CellContext, type HeaderContext, injectFlexRenderContext } from '@tanstack/angular-table';

@Component({
  selector: 'app-table-head-selection',
  imports: [HlmCheckboxImports],
  host: { class: 'flex' },
  template: `
    <hlm-checkbox
      [checked]="context.table.getIsAllRowsSelected()"
      [indeterminate]="context.table.getIsSomeRowsSelected()"
      (checkedChange)="context.table.toggleAllRowsSelected()"
      aria-label="Select all"
    />
  `,
})
export class TableHeadSelection<T> {
  protected readonly context = injectFlexRenderContext<HeaderContext<T, unknown>>();
}

@Component({
  selector: 'app-table-row-selection',
  imports: [HlmCheckboxImports],
  host: { class: 'flex' },
  template: `
    <hlm-checkbox
      [checked]="context.row.getIsSelected()"
      (checkedChange)="context.row.getToggleSelectedHandler()($event)"
      [aria-label]="selectLabel()"
    />
  `,
})
export class TableRowSelection<T> {
  protected readonly context = injectFlexRenderContext<CellContext<T, unknown>>();
  protected selectLabel = computed(() => {
    const original = this.context.row.original;
    if (original && typeof original === 'object' && 'name' in original) {
      return `Select ${(original as { name: string }).name}`;
    }
    return 'Select row';
  });
}
