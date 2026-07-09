import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { TagListComponent } from './tag-list.component';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { InventoryTagSortBy } from '@/api/generated/models/inventory-tag-sort-by';

describe('TagListComponent', () => {
  let fixture: ComponentFixture<TagListComponent>;
  let component: TagListComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(TagListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialise with loading false and no error', () => {
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe(false);
  });

  it('should have no selection initially', () => {
    expect(component.hasSelection()).toBe(false);
    expect(component.selectionCount()).toBe(0);
  });

  describe('sortOptions', () => {
    it('should expose the five expected sort fields in order', () => {
      const values = component.sortOptions.map(o => o.value);
      expect(values).toEqual<InventoryTagSortBy[]>(['Name', 'Path', 'ItemCount', 'CreatedAt', 'UpdatedAt']);
    });

    it('should have a label for each sort option', () => {
      for (const opt of component.sortOptions) {
        expect(opt.label.length).toBeGreaterThan(0);
      }
    });

    it('should label ItemCount as "Items"', () => {
      const opt = component.sortOptions.find(o => o.value === 'ItemCount');
      expect(opt?.label).toBe('Items');
    });

    it('should label Path as "Path"', () => {
      const opt = component.sortOptions.find(o => o.value === 'Path');
      expect(opt?.label).toBe('Path');
    });
  });

  describe('onSortByChange', () => {
    it('should default to Asc for Name', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('Name');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Asc' });
    });

    it('should default to Asc for Path', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('Path');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Path', sortDir: 'Asc' });
    });

    it('should default to Asc for ItemCount', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('ItemCount');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'ItemCount', sortDir: 'Asc' });
    });

    it('should default to Desc for CreatedAt', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('CreatedAt');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'CreatedAt', sortDir: 'Desc' });
    });

    it('should default to Desc for UpdatedAt', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('UpdatedAt');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'UpdatedAt', sortDir: 'Desc' });
    });

    it('should preserve existing sort direction when changing field', () => {
      (component as any).sortDir = signal('Desc');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('Name');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Desc' });
    });
  });

  describe('onSortDirChange', () => {
    it('should use default Name when no explicit sortBy is set', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Desc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Desc' });
    });

    it('should call onSortChange with active field and Asc direction', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('ItemCount');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Asc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'ItemCount', sortDir: 'Asc' });
    });
  });

  describe('effectiveSortBy', () => {
    it('should return Name when no explicit sort is set', () => {
      expect(component.effectiveSortBy()).toBe('Name');
    });

    it('should return the explicit sort value when one is set', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('CreatedAt');
      expect(component.effectiveSortBy()).toBe('CreatedAt');
    });
  });

  describe('effectiveSortDir', () => {
    it('should return Asc when no explicit sort (default for Name)', () => {
      expect(component.effectiveSortDir()).toBe('Asc');
    });

    it('should return Desc when sortBy is CreatedAt with no explicit dir', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('CreatedAt');
      expect(component.effectiveSortDir()).toBe('Desc');
    });

    it('should return the explicit direction when set', () => {
      (component as any).sortDir = signal('Desc');
      (component as any).sortBy = signal<InventoryTagSortBy>('Name');
      expect(component.effectiveSortDir()).toBe('Desc');
    });
  });

  describe('sortLabel', () => {
    it('should show Name by default', () => {
      expect(component.sortLabel()).toBe('Name');
    });

    it('should show explicit sort field label', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('ItemCount');
      expect(component.sortLabel()).toBe('Items');
    });

    it('should show Date Added for CreatedAt', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('CreatedAt');
      expect(component.sortLabel()).toBe('Date Added');
    });

    it('should show Modified for UpdatedAt', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('UpdatedAt');
      expect(component.sortLabel()).toBe('Modified');
    });

    it('should show Path for Path', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('Path');
      expect(component.sortLabel()).toBe('Path');
    });
  });

  describe('hasActiveFilters', () => {
    it('should be false by default', () => {
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('should not consider sort as an active filter', () => {
      (component as any).sortBy = signal<InventoryTagSortBy>('Name');
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('should be true when search term is present', () => {
      (component as any).searchTerm = signal('test');
      expect(component.hasActiveFilters()).toBe(true);
    });
  });

  describe('columnVisibility', () => {
    it('should default path and createdAt to hidden', () => {
      expect(component.columnVisibility()).toEqual(expect.objectContaining({
        path: false,
        createdAt: false,
      }));
    });

    it('should hide path by default', () => {
      expect(component.columnVisibility()['path']).toBe(false);
    });

    it('should not hide description by default', () => {
      expect(component.columnVisibility()['description']).toBeUndefined();
    });

    it('should merge defaults underneath saved state', () => {
      const prefs = TestBed.inject(UserPreferencesService);
      prefs.inventoryTagColumnVisibility.set({ description: false });

      const newFixture = TestBed.createComponent(TagListComponent);
      const newComponent = newFixture.componentInstance;
      const vis = newComponent.columnVisibility();

      expect(vis).toEqual(expect.objectContaining({
        description: false,
        createdAt: false,
      }));
    });

    it('should let saved state override defaults for date columns', () => {
      const prefs = TestBed.inject(UserPreferencesService);
      prefs.inventoryTagColumnVisibility.set({ createdAt: true });

      const newFixture = TestBed.createComponent(TagListComponent);
      const newComponent = newFixture.componentInstance;
      const vis = newComponent.columnVisibility();

      expect(vis['createdAt']).toBe(true);
    });

    it('should toggle a column and persist to preferences', () => {
      const prefs = TestBed.inject(UserPreferencesService);

      component.onToggleColumn('createdAt');
      expect(component.columnVisibility()['createdAt']).toBe(true);
      expect(prefs.inventoryTagColumnVisibility()).toEqual(expect.objectContaining({ createdAt: true }));
    });

    it('should toggle description column off when it starts visible', () => {
      // description is visible by default (not in DEFAULT_COLUMN_VISIBILITY)
      component.onToggleColumn('description');
      expect(component.columnVisibility()['description']).toBe(false);
    });
  });

  describe('resetSearch', () => {
    it('should clear searchInput and navigate', () => {
      const router = TestBed.inject(Router);
      const spy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      component.searchInput.set('some search');

      component.resetSearch();

      expect(component.searchInput()).toBe('');
      expect(spy).toHaveBeenCalledWith(['/inventory/tags'], expect.objectContaining({
        queryParams: expect.objectContaining({ search: null }),
      }));
    });
  });

  describe('clearSelection', () => {
    it('should reset selected tag IDs', () => {
      component.selectedTagIds.set(new Set(['id1', 'id2']));
      expect(component.hasSelection()).toBe(true);

      component.clearSelection();

      expect(component.selectedTagIds().size).toBe(0);
      expect(component.hasSelection()).toBe(false);
    });
  });

  describe('sheet lifecycle', () => {
    it('onSheetClosed closes the sheet and clears the selected tag', () => {
      component.sheetState.set('open');
      component.selectedTag.set({ id: 't1', name: 'A', path: 'A', itemCount: 0, createdAt: '', updatedAt: '' });

      component.onSheetClosed();

      expect(component.sheetState()).toBe('closed');
      expect(component.selectedTag()).toBeNull();
    });

    it('onSheetSaved closes the sheet', () => {
      component.sheetState.set('open');

      component.onSheetSaved();

      expect(component.sheetState()).toBe('closed');
    });

    it('onTagSelect is a no-op while the list data has not loaded', () => {
      // data() is null here (no HTTP backend flush), so there is no tag to select.
      expect(component.data()).toBeNull();

      component.onTagSelect('nope');

      expect(component.sheetState()).toBe('closed');
    });
  });
});
