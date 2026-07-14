import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { CategoryRowActionsComponent } from './category-row-actions.component';
import { categoryDisplayLabel, type CategoryNode } from './category-tree.util';
import type { CategoryResponse } from '@/api/generated/models/category-response';

/**
 * One node in the category tree, recursing into its children when expanded. System Uncategorised nodes
 * render read-only (no chevron-less-vs-leaf distinction aside, no row actions). Toggle/edit/delete are
 * passed down as callback inputs — simpler than re-emitting outputs through every level of recursion.
 */
@Component({
  selector: 'app-category-tree-node',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmBadge, CategoryRowActionsComponent],
  providers: [provideIcons({ lucideChevronRight })],
  template: `
    <div class="flex items-center gap-1 rounded-md pr-1 hover:bg-accent/50" [style.paddingLeft.px]="node().category.depth * 20">
      @if (hasChildren()) {
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          class="shrink-0"
          [attr.aria-label]="isExpanded() ? 'Collapse' : 'Expand'"
          (click)="toggle()(node().category.id)"
        >
          <ng-icon hlm name="lucideChevronRight" size="sm" class="transition-transform" [class.rotate-90]="isExpanded()" />
        </button>
      } @else {
        <span class="inline-block size-7 shrink-0"></span>
      }
      <span class="min-w-0 flex-1 truncate py-1.5 text-sm" [class.text-muted-foreground]="node().category.isSystem">
        {{ label() }}
      </span>
      <span hlmBadge variant="outline" class="shrink-0">{{ node().category.type }}</span>
      @if (!node().category.readOnly) {
        <app-category-row-actions
          [category]="node().category"
          (edit)="edit()($event)"
          (delete)="delete()($event)"
        />
      } @else {
        <span class="inline-block size-7 shrink-0"></span>
      }
    </div>

    @if (isExpanded()) {
      @for (child of node().children; track child.category.id) {
        <app-category-tree-node
          [node]="child"
          [expanded]="expanded()"
          [toggle]="toggle()"
          [edit]="edit()"
          [delete]="delete()"
        />
      }
    }
  `,
})
export class CategoryTreeNodeComponent {
  node = input.required<CategoryNode>();
  expanded = input.required<ReadonlySet<string>>();
  toggle = input.required<(id: string) => void>();
  edit = input.required<(category: CategoryResponse) => void>();
  delete = input.required<(category: CategoryResponse) => void>();

  hasChildren = computed(() => this.node().children.length > 0);
  isExpanded = computed(() => this.expanded().has(this.node().category.id));
  label = computed(() => categoryDisplayLabel(this.node().category));
}
