import { Injectable, signal } from '@angular/core';

export type CategoryTypeFilter = 'all' | 'Expense' | 'Income';

/**
 * Minimal in-memory list state for the categories page — the type filter survives navigation within a
 * session (the expand/collapse set is intentionally ephemeral, reset on each page mount).
 */
@Injectable({ providedIn: 'root' })
export class CategoryListStateService {
  readonly typeFilter = signal<CategoryTypeFilter>('all');
}
