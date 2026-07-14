import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { CategoryTreeNodeComponent } from './category-tree-node.component';
import type { CategoryNode } from './category-tree.util';
import type { CategoryResponse } from '@/api/generated/models/category-response';

@Component({
  selector: 'app-category-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CategoryTreeNodeComponent],
  template: `
    @for (root of nodes(); track root.category.id) {
      <app-category-tree-node
        [node]="root"
        [expanded]="expanded()"
        [toggle]="toggle()"
        [edit]="edit()"
        [delete]="delete()"
      />
    }
  `,
})
export class CategoryTreeComponent {
  nodes = input.required<CategoryNode[]>();
  expanded = input.required<ReadonlySet<string>>();
  toggle = input.required<(id: string) => void>();
  edit = input.required<(category: CategoryResponse) => void>();
  delete = input.required<(category: CategoryResponse) => void>();
}
