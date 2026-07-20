import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import {
  LineCategoryComboboxComponent,
  type LineCategorySelection,
} from './line-category-combobox.component';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import type { CategoryResponse } from '@/api/generated/models/category-response';
import type { AccountType } from '@/api/generated/models/account-type';
import type { LineCategory } from '../../accounts/statement-import-review-cells';

const cat = (
  id: string, name: string, type: AccountType = 'Expense', over: Partial<CategoryResponse> = {},
): CategoryResponse => ({
  id, name, type, parentId: null, depth: 0, path: name, isSystem: false, readOnly: false, ...over,
});

// Two Expense + one Income category, plus the two system Uncategorised buckets (never selectable).
const CATS: CategoryResponse[] = [
  cat('e1', 'Groceries', 'Expense'),
  cat('e2', 'Rent', 'Expense'),
  cat('i1', 'Salary', 'Income'),
  cat('sysE', 'Expense', 'Expense', { isSystem: true, readOnly: true }),
  cat('sysI', 'Income', 'Income', { isSystem: true, readOnly: true }),
];

// The component is exercised through its public API (inputs + derived signals + onPick/onCreate), never the
// rendered overlay — jsdom won't lay out the popup portal, and detectChanges isn't needed for pull-based
// signals. This mirrors the documents-filter-panel combobox spec.
describe('LineCategoryComboboxComponent', () => {
  let createCategory: ReturnType<typeof vi.fn>;

  function make(inputs: Record<string, unknown> = {}): ComponentFixture<LineCategoryComboboxComponent> {
    const fixture = TestBed.createComponent(LineCategoryComboboxComponent);
    fixture.componentRef.setInput('categories', CATS);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    return fixture;
  }

  beforeEach(() => {
    createCategory = vi.fn().mockReturnValue(of(cat('e3', 'Fuel', 'Expense')));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesCategoryApiService, useValue: { getCategories: () => of(CATS), createCategory } },
      ],
    });
  });

  it('splits categories into direction groups, excluding the system Uncategorised buckets', () => {
    const c = make().componentInstance;
    expect(c.expenseCats().map((x) => x.id)).toEqual(['e1', 'e2']);
    expect(c.incomeCats().map((x) => x.id)).toEqual(['i1']);
  });

  it('shows the Suggested shortcut only when the payee default matches the current direction', () => {
    const expDefault: LineCategory = { id: 'e1', name: 'Groceries', type: 'Expense' };
    expect(make({ direction: 'Expense', payeeDefaultCategory: expDefault }).componentInstance.suggested()?.id)
      .toBe('e1');
    // A direction flip filters the Expense default out of Suggested.
    expect(make({ direction: 'Income', payeeDefaultCategory: expDefault }).componentInstance.suggested())
      .toBeNull();
  });

  it('value-collision: the resting token is canonical (cat:), so exactly one item checks', () => {
    const applied: LineCategory = { id: 'e1', name: 'Groceries', type: 'Expense' };
    // Same category is both the applied value AND the payee default (so it appears in Suggested + its group).
    const c = make({ direction: 'Expense', category: applied, payeeDefaultCategory: applied }).componentInstance;
    expect(c.value()).toBe('cat:e1');
    // isSelected(itemValue) = eq(itemValue, value()): the group item matches, the Suggested shortcut does not.
    expect(c.eq('cat:e1', c.value())).toBe(true);
    expect(c.eq('sug:e1', c.value())).toBe(false);
    expect(c.suggested()?.id).toBe('e1'); // Suggested still renders — just unchecked.

    // Selecting EITHER the group item or the Suggested shortcut emits the identical selection.
    const emitted: LineCategorySelection[] = [];
    c.categoryChange.subscribe((s) => emitted.push(s));
    c.onPick('cat:e1');
    c.onPick('sug:e1');
    const expected = { direction: 'Expense', categoryId: 'e1', category: { id: 'e1', name: 'Groceries', type: 'Expense' } };
    expect(emitted).toEqual([expected, expected]);
  });

  it('emits {Expense,id,cat} for a category pick and {Income,null,null} for Uncategorised · Income', () => {
    const c = make().componentInstance;
    const emitted: LineCategorySelection[] = [];
    c.categoryChange.subscribe((s) => emitted.push(s));
    c.onPick('cat:e2');
    c.onPick('unc:Income');
    expect(emitted[0]).toEqual({
      direction: 'Expense', categoryId: 'e2', category: { id: 'e2', name: 'Rent', type: 'Expense' },
    });
    expect(emitted[1]).toEqual({ direction: 'Income', categoryId: null, category: null });
  });

  it('ignores a null/undefined pick (no deselect in single-select)', () => {
    const c = make().componentInstance;
    const emitted: LineCategorySelection[] = [];
    c.categoryChange.subscribe((s) => emitted.push(s));
    c.onPick(null);
    c.onPick(undefined);
    expect(emitted).toEqual([]);
  });

  it('offers a create row on a non-matching search and creates in the current direction', () => {
    const c = make({ direction: 'Income' }).componentInstance;
    expect(c.showCreateRow()).toBe(false);   // empty search
    c.search.set('salary');
    expect(c.showCreateRow()).toBe(false);   // matches an existing Income category (case-insensitive)
    c.search.set('Bonus');
    expect(c.showCreateRow()).toBe(true);

    const created = cat('i2', 'Bonus', 'Income');
    createCategory.mockReturnValue(of(created));
    const emitted: LineCategorySelection[] = [];
    const createdOut: CategoryResponse[] = [];
    c.categoryChange.subscribe((s) => emitted.push(s));
    c.categoryCreated.subscribe((r) => createdOut.push(r));
    c.onCreate();

    expect(createCategory).toHaveBeenCalledWith({ body: { name: 'Bonus', type: 'Income', parentId: null } });
    expect(emitted).toEqual([{
      direction: 'Income', categoryId: 'i2', category: { id: 'i2', name: 'Bonus', type: 'Income' },
    }]);
    // The full created category is surfaced so the host can share it with the other rows.
    expect(createdOut).toEqual([created]);
    expect(c.search()).toBe('');
  });

  it('suppresses the create row when allowCreate is false', () => {
    const c = make({ allowCreate: false }).componentInstance;
    c.search.set('Brand New');
    expect(c.showCreateRow()).toBe(false);
  });

  it('bulk (resetAfterSelect) mode rests on no value so nothing checks, yet still emits each pick', () => {
    const c = make({ resetAfterSelect: true, placeholder: 'Set category…' }).componentInstance;
    expect(c.value()).toBeUndefined();              // no resting token
    expect(c.eq('cat:e1', c.value())).toBe(false);  // → no item is checked
    expect(c.triggerLabel()).toBe('Set category…'); // always the placeholder

    const emitted: LineCategorySelection[] = [];
    c.categoryChange.subscribe((s) => emitted.push(s));
    c.onPick('cat:e1');
    c.onPick('unc:Expense');
    expect(emitted.length).toBe(2);
  });
});
