import type { AreaResponse } from '@/api/generated/models/area-response';

/**
 * Area-hierarchy helpers shared by the management table, the item-list area filter, the location
 * picker, and the parent-area picker. Areas nest via {@link AreaResponse.parentAreaId}; a null
 * parent is a root. All walks are cycle-guarded so a manually-corrupted chain can't loop or hide
 * rows.
 */

export interface AreaTreeNode {
  readonly area: AreaResponse;
  /** Indentation level: 0 for roots, +1 per ancestor. */
  readonly depth: number;
}

/**
 * Orders a flat area list into a stable pre-order tree: each root (name-sorted) immediately
 * followed by its descendants (recursively name-sorted), annotated with depth for indentation.
 * Any area unreachable from a root (only possible via a manual cycle) is appended at depth 0 so
 * nothing is silently hidden.
 */
export function orderAreaTree(areas: readonly AreaResponse[]): AreaTreeNode[] {
  const childrenByParent = new Map<string | null, AreaResponse[]>();
  for (const a of areas) {
    const key = a.parentAreaId ?? null;
    const list = childrenByParent.get(key) ?? [];
    list.push(a);
    childrenByParent.set(key, list);
  }
  for (const list of childrenByParent.values())
    list.sort((x, y) => x.name.localeCompare(y.name));

  const out: AreaTreeNode[] = [];
  const emitted = new Set<string>();

  const visit = (parentKey: string | null, depth: number): void => {
    for (const area of childrenByParent.get(parentKey) ?? []) {
      if (emitted.has(area.id)) continue;
      emitted.add(area.id);
      out.push({ area, depth });
      visit(area.id, depth + 1);
    }
  };
  visit(null, 0);

  for (const a of areas)
    if (!emitted.has(a.id)) {
      emitted.add(a.id);
      out.push({ area: a, depth: 0 });
    }

  return out;
}

/**
 * Maps each area id to the set of its descendant ids, inclusive of itself — used to make the area
 * filter (and any subtree exclusion) match descendants, not just the exact area.
 */
export function buildDescendantMap(areas: readonly AreaResponse[]): Map<string, Set<string>> {
  const childrenByParent = new Map<string, string[]>();
  for (const a of areas)
    if (a.parentAreaId) {
      const list = childrenByParent.get(a.parentAreaId) ?? [];
      list.push(a.id);
      childrenByParent.set(a.parentAreaId, list);
    }

  const map = new Map<string, Set<string>>();
  for (const a of areas) {
    const set = new Set<string>();
    const stack = [a.id];
    while (stack.length > 0) {
      const current = stack.pop()!;
      if (set.has(current)) continue;
      set.add(current);
      for (const child of childrenByParent.get(current) ?? [])
        stack.push(child);
    }
    map.set(a.id, set);
  }
  return map;
}
