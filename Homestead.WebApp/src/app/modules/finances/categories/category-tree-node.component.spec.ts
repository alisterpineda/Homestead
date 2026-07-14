import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { CategoryTreeNodeComponent } from './category-tree-node.component';
import type { CategoryNode } from './category-tree.util';
import type { CategoryResponse } from '@/api/generated/models/category-response';

const cat = (id: string, name: string, over: Partial<CategoryResponse> = {}): CategoryResponse => ({
  id, name, type: 'Expense', parentId: null, depth: 0, path: name, isSystem: false, readOnly: false, ...over,
});
const node = (category: CategoryResponse, children: CategoryNode[] = []): CategoryNode => ({ category, children });

function render(n: CategoryNode, expanded: Set<string>): ComponentFixture<CategoryTreeNodeComponent> {
  const fixture = TestBed.createComponent(CategoryTreeNodeComponent);
  fixture.componentRef.setInput('node', n);
  fixture.componentRef.setInput('expanded', expanded);
  fixture.componentRef.setInput('toggle', () => {});
  fixture.componentRef.setInput('edit', () => {});
  fixture.componentRef.setInput('delete', () => {});
  fixture.detectChanges();
  return fixture;
}

describe('CategoryTreeNodeComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  it('renders row actions for a user category', () => {
    const fixture = render(node(cat('u', 'Groceries')), new Set());
    expect(fixture.nativeElement.querySelector('app-category-row-actions')).not.toBeNull();
  });

  it('hides row actions for a read-only system bucket', () => {
    const fixture = render(node(cat('s', 'Expense', { isSystem: true, readOnly: true })), new Set());
    expect(fixture.nativeElement.querySelector('app-category-row-actions')).toBeNull();
  });

  it('renders children only when the node is expanded', () => {
    const tree = node(cat('food', 'Food'), [node(cat('groc', 'Groceries', { parentId: 'food', depth: 1 }))]);

    const collapsed = render(tree, new Set());
    expect(collapsed.nativeElement.textContent).not.toContain('Groceries');

    const expanded = render(tree, new Set(['food']));
    expect(expanded.nativeElement.textContent).toContain('Groceries');
  });
});
