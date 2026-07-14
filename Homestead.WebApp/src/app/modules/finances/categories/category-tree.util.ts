import type { CategoryResponse } from '@/api/generated/models/category-response';

export interface CategoryNode {
  category: CategoryResponse;
  children: CategoryNode[];
}

// System Uncategorised buckets display with a disambiguating direction suffix in the tree; user
// categories show their own name. (The flow picker shortens the empty state to just "Uncategorised".)
export function categoryDisplayLabel(c: CategoryResponse): string {
  return c.isSystem ? `Uncategorised ${c.type}` : c.name;
}

// Build a parent→children forest from the flat list. Roots (parentId == null) list user categories
// first then the system buckets; children sort alphabetically. Depth comes from the server.
export function buildCategoryTree(items: readonly CategoryResponse[]): CategoryNode[] {
  const byId = new Map<string, CategoryNode>();
  for (const c of items) byId.set(c.id, { category: c, children: [] });

  const roots: CategoryNode[] = [];
  for (const node of byId.values()) {
    const parent = node.category.parentId ? byId.get(node.category.parentId) : undefined;
    if (parent) parent.children.push(node);
    else roots.push(node);
  }

  const sort = (nodes: CategoryNode[]): void => {
    nodes.sort((a, b) => {
      if (a.category.isSystem !== b.category.isSystem) return a.category.isSystem ? 1 : -1;
      return a.category.name.localeCompare(b.category.name);
    });
    for (const n of nodes) sort(n.children);
  };
  sort(roots);

  return roots;
}

// Ids in the subtree beneath categoryId (excluding itself) — the edit sheet's parent picker excludes
// these so a category can't be moved under one of its own descendants.
export function descendantIds(items: readonly CategoryResponse[], categoryId: string): string[] {
  const childrenByParent = new Map<string, CategoryResponse[]>();
  for (const c of items) {
    if (!c.parentId) continue;
    const arr = childrenByParent.get(c.parentId) ?? [];
    arr.push(c);
    childrenByParent.set(c.parentId, arr);
  }

  const out: string[] = [];
  const stack = [categoryId];
  while (stack.length) {
    const current = stack.pop()!;
    for (const child of childrenByParent.get(current) ?? []) {
      out.push(child.id);
      stack.push(child.id);
    }
  }
  return out;
}
