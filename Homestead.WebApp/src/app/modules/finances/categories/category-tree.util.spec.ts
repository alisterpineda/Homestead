import { buildCategoryTree, categoryDisplayLabel, descendantIds } from './category-tree.util';
import type { CategoryResponse } from '@/api/generated/models/category-response';

const cat = (id: string, name: string, over: Partial<CategoryResponse> = {}): CategoryResponse => ({
  id, name, type: 'Expense', parentId: null, depth: 0, path: name, isSystem: false, readOnly: false, ...over,
});

describe('category-tree.util', () => {
  describe('categoryDisplayLabel', () => {
    it('labels system buckets with a direction suffix, user categories by name', () => {
      expect(categoryDisplayLabel(cat('s', 'Expense', { isSystem: true }))).toBe('Uncategorised Expense');
      expect(categoryDisplayLabel(cat('s', 'Income', { isSystem: true, type: 'Income' }))).toBe('Uncategorised Income');
      expect(categoryDisplayLabel(cat('u', 'Groceries'))).toBe('Groceries');
    });
  });

  describe('buildCategoryTree', () => {
    it('nests children under parents and pins system buckets last among roots', () => {
      const items = [
        cat('food', 'Food'),
        cat('groc', 'Groceries', { parentId: 'food', depth: 1 }),
        cat('rent', 'Rent'),
        cat('sys', 'Expense', { isSystem: true, readOnly: true }),
      ];

      const roots = buildCategoryTree(items);

      // User roots alphabetically first (Food, Rent), the system bucket pinned last.
      expect(roots.map(r => r.category.id)).toEqual(['food', 'rent', 'sys']);
      expect(roots[roots.length - 1].category.isSystem).toBe(true);
      // Food has Groceries nested.
      const food = roots.find(r => r.category.id === 'food')!;
      expect(food.children.map(c => c.category.id)).toEqual(['groc']);
    });
  });

  describe('descendantIds', () => {
    it('returns the whole subtree excluding the root itself', () => {
      const items = [
        cat('food', 'Food'),
        cat('groc', 'Groceries', { parentId: 'food' }),
        cat('prod', 'Produce', { parentId: 'groc' }),
        cat('rent', 'Rent'),
      ];

      const ids = descendantIds(items, 'food');

      expect(ids.sort()).toEqual(['groc', 'prod']);
      expect(ids).not.toContain('food');
      expect(ids).not.toContain('rent');
    });
  });
});
