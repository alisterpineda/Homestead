import { Component, computed } from '@angular/core';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import {
  type CellContext,
  type HeaderContext,
  injectFlexRenderContext,
  type RowData,
  type rowSelectionFeature,
} from '@tanstack/angular-table';

/** The features these cells call into; every table that mounts them registers at least these. */
type SelectionFeatures = { rowSelectionFeature: typeof rowSelectionFeature };

// Both cells swallow click/enter at their host so interacting with the checkbox
// never triggers the enclosing row's click handler — host templates don't need
// their own per-cell stopPropagation guards.
@Component({
  selector: 'app-table-head-selection',
  imports: [HlmCheckboxImports],
  host: {
    class: 'flex',
    '(click)': '$event.stopPropagation()',
    '(keydown.enter)': '$event.stopPropagation()',
  },
  template: `
    <hlm-checkbox
      [checked]="context.table.getIsAllRowsSelected()"
      [indeterminate]="context.table.getIsSomeRowsSelected() && !context.table.getIsAllRowsSelected()"
      (checkedChange)="context.table.toggleAllRowsSelected()"
      aria-label="Select all"
    />
  `,
})
export class TableHeadSelection<T extends RowData> {
  protected readonly context = injectFlexRenderContext<HeaderContext<SelectionFeatures, T, unknown>>();
}

@Component({
  selector: 'app-table-row-selection',
  imports: [HlmCheckboxImports],
  host: {
    class: 'flex',
    '(click)': '$event.stopPropagation()',
    '(keydown.enter)': '$event.stopPropagation()',
  },
  template: `
    <hlm-checkbox
      [checked]="context.row.getIsSelected()"
      [disabled]="!context.row.getCanSelect()"
      (checkedChange)="context.row.toggleSelected($event)"
      [aria-label]="selectLabel()"
    />
  `,
})
export class TableRowSelection<T extends RowData> {
  protected readonly context = injectFlexRenderContext<CellContext<SelectionFeatures, T, unknown>>();
  protected selectLabel = computed(() => {
    const original = this.context.row.original;
    if (original && typeof original === 'object' && 'name' in original) {
      return `Select ${(original as { name: string }).name}`;
    }
    return 'Select row';
  });
}
