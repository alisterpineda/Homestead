import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { CategoryPickerComponent } from './category-picker.component';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import type { CategoryResponse } from '@/api/generated/models/category-response';

const cat = (id: string, name: string, over: Partial<CategoryResponse> = {}): CategoryResponse => ({
  id, name, type: 'Expense', parentId: null, depth: 0, path: name, isSystem: false, readOnly: false, ...over,
});

describe('CategoryPickerComponent', () => {
  let getCategories: ReturnType<typeof vi.fn>;
  let createCategory: ReturnType<typeof vi.fn>;

  function setupFixture(): ComponentFixture<CategoryPickerComponent> {
    const fixture = TestBed.createComponent(CategoryPickerComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    getCategories = vi.fn().mockReturnValue(of([
      cat('c1', 'Groceries'),
      cat('c2', 'Rent'),
      cat('sys', 'Expense', { isSystem: true, readOnly: true }),
    ]));
    createCategory = vi.fn().mockReturnValue(of(cat('c3', 'Fuel')));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesCategoryApiService, useValue: { getCategories, createCategory } },
      ],
    });
  });

  it('loads categories for the direction and excludes system buckets from the list', () => {
    const c = setupFixture().componentInstance;
    expect(getCategories).toHaveBeenCalledWith({ Type: 'Expense' });
    expect(c.results().map(x => x.id)).toEqual(['c1', 'c2']); // 'sys' filtered out
  });

  it('filters results by search term against name and path', () => {
    const c = setupFixture().componentInstance;
    c.searchTerm.set('rent');
    expect(c.results().map(x => x.id)).toEqual(['c2']);
  });

  it('offers a create row only for a non-matching search (case-insensitive)', () => {
    const c = setupFixture().componentInstance;
    expect(c.showCreateRow()).toBe(false);        // empty search
    c.searchTerm.set('groceries');
    expect(c.showCreateRow()).toBe(false);         // matches an existing category
    c.searchTerm.set('New Thing');
    expect(c.showCreateRow()).toBe(true);
  });

  it('suppresses the create row when allowCreate is false', () => {
    const fixture = setupFixture();
    fixture.componentRef.setInput('allowCreate', false);
    fixture.componentInstance.searchTerm.set('New Thing');
    fixture.detectChanges();
    expect(fixture.componentInstance.showCreateRow()).toBe(false);
  });

  it('find-or-create: creates a top-level category of the direction and selects it', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    const emitted: (string | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.searchTerm.set('  Fuel  ');
    c.onCreate();

    expect(createCategory).toHaveBeenCalledWith({ body: { name: 'Fuel', type: 'Expense', parentId: null } });
    expect(emitted).toEqual(['c3']);
    expect(c.searchTerm()).toBe('');

    // Simulate the parent binding value <- the emitted id. The trigger resolves 'c3' from the locally-
    // retained create (the getCategories mock never returned it), proving inline creates show at once.
    fixture.componentRef.setInput('value', 'c3');
    fixture.detectChanges();
    expect(c.selected()?.id).toBe('c3');
  });

  it('onSelect emits the id and closes; onClear emits null', () => {
    const c = setupFixture().componentInstance;
    const emitted: (string | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.onSelect(cat('c1', 'Groceries'));
    expect(emitted).toEqual(['c1']);
    expect(c.popoverState()).toBe('closed');

    c.onClear();
    expect(emitted).toEqual(['c1', null]);
  });

  it('excludeIds removes categories from the selectable list', () => {
    const fixture = setupFixture();
    fixture.componentRef.setInput('excludeIds', ['c1']);
    fixture.detectChanges();
    expect(fixture.componentInstance.results().map(x => x.id)).toEqual(['c2']);
  });

  it('resolves the trigger value from the loaded categories', () => {
    const fixture = setupFixture();
    fixture.componentRef.setInput('value', 'c2');
    fixture.detectChanges();
    expect(fixture.componentInstance.selected()?.name).toBe('Rent');
  });
});
