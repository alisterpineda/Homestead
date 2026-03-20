import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { signal } from '@angular/core';
import { PartyListComponent } from './party-list.component';
import { DEFAULT_COLUMN_VISIBILITY } from '../../components/party-table/party-table.component';
import { PartyListStateService } from '../../services/party-list-state.service';
import type { PartySortBy } from '@/api/generated/models/party-sort-by';

describe('PartyListComponent', () => {
  let fixture: ComponentFixture<PartyListComponent>;
  let component: PartyListComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(PartyListComponent);
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
    it('should expose the three expected sort fields in order', () => {
      const values = component.sortOptions.map(o => o.value);
      expect(values).toEqual<PartySortBy[]>(['Name', 'DocumentCount', 'CreatedAt']);
    });

    it('should have a label for each sort option', () => {
      for (const opt of component.sortOptions) {
        expect(opt.label.length).toBeGreaterThan(0);
      }
    });

    it('should label DocumentCount as "Documents"', () => {
      const opt = component.sortOptions.find(o => o.value === 'DocumentCount');
      expect(opt?.label).toBe('Documents');
    });
  });

  describe('onSortByChange', () => {
    it('should default to Asc for Name', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('Name');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Asc' });
    });

    it('should default to Asc for DocumentCount', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('DocumentCount');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DocumentCount', sortDir: 'Asc' });
    });

    it('should default to Desc for CreatedAt', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('CreatedAt');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'CreatedAt', sortDir: 'Desc' });
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
      (component as any).sortBy = signal<PartySortBy>('DocumentCount');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Asc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DocumentCount', sortDir: 'Asc' });
    });
  });

  describe('effectiveSortBy', () => {
    it('should return Name when no explicit sort is set', () => {
      expect(component.effectiveSortBy()).toBe('Name');
    });

    it('should return the explicit sort value when one is set', () => {
      (component as any).sortBy = signal<PartySortBy>('CreatedAt');
      expect(component.effectiveSortBy()).toBe('CreatedAt');
    });
  });

  describe('effectiveSortDir', () => {
    it('should return Asc when no explicit sort (default for Name)', () => {
      expect(component.effectiveSortDir()).toBe('Asc');
    });

    it('should return Desc when sortBy is CreatedAt with no explicit dir', () => {
      (component as any).sortBy = signal<PartySortBy>('CreatedAt');
      expect(component.effectiveSortDir()).toBe('Desc');
    });

    it('should return the explicit direction when set', () => {
      (component as any).sortDir = signal('Desc');
      (component as any).sortBy = signal<PartySortBy>('Name');
      expect(component.effectiveSortDir()).toBe('Desc');
    });
  });

  describe('sortLabel', () => {
    it('should show Name by default', () => {
      expect(component.sortLabel()).toBe('Name');
    });

    it('should show explicit sort field label', () => {
      (component as any).sortBy = signal<PartySortBy>('DocumentCount');
      expect(component.sortLabel()).toBe('Documents');
    });

    it('should show Date Added for CreatedAt', () => {
      (component as any).sortBy = signal<PartySortBy>('CreatedAt');
      expect(component.sortLabel()).toBe('Date Added');
    });
  });

  describe('hasActiveFilters', () => {
    it('should be false by default', () => {
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('should not consider sort as an active filter', () => {
      (component as any).sortBy = signal<PartySortBy>('Name');
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('should be true when search term is present', () => {
      (component as any).searchTerm = signal('test');
      expect(component.hasActiveFilters()).toBe(true);
    });
  });

  describe('columnVisibility', () => {
    it('should default createdAt to hidden', () => {
      expect(component.columnVisibility()).toEqual(expect.objectContaining({
        createdAt: false,
      }));
    });

    it('should not hide description by default', () => {
      expect(component.columnVisibility()['description']).toBeUndefined();
    });

    it('should merge defaults underneath saved state', () => {
      const listState = TestBed.inject(PartyListStateService);
      listState.saveColumnVisibility({ description: false });

      const newFixture = TestBed.createComponent(PartyListComponent);
      const newComponent = newFixture.componentInstance;
      const vis = newComponent.columnVisibility();

      expect(vis).toEqual(expect.objectContaining({
        description: false,
        createdAt: false,
      }));
    });

    it('should let saved state override defaults for date columns', () => {
      const listState = TestBed.inject(PartyListStateService);
      listState.saveColumnVisibility({ createdAt: true });

      const newFixture = TestBed.createComponent(PartyListComponent);
      const newComponent = newFixture.componentInstance;
      const vis = newComponent.columnVisibility();

      expect(vis['createdAt']).toBe(true);
    });

    it('should toggle a column and save visibility state', () => {
      const listState = TestBed.inject(PartyListStateService);
      const saveSpy = vi.spyOn(listState, 'saveColumnVisibility');

      component.onToggleColumn('createdAt');
      expect(component.columnVisibility()['createdAt']).toBe(true);
      expect(saveSpy).toHaveBeenCalled();
    });

    it('should toggle description column off and on', () => {
      // description is visible by default (not in DEFAULT_COLUMN_VISIBILITY)
      component.onToggleColumn('description');
      expect(component.columnVisibility()['description']).toBe(true);
    });
  });

  describe('resetSearch', () => {
    it('should clear searchInput and navigate', () => {
      const router = TestBed.inject(Router);
      const spy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      component.searchInput.set('some search');

      component.resetSearch();

      expect(component.searchInput()).toBe('');
      expect(spy).toHaveBeenCalledWith(['/documents/parties'], expect.objectContaining({
        queryParams: expect.objectContaining({ search: null }),
      }));
    });
  });

  describe('clearSelection', () => {
    it('should reset selected party IDs', () => {
      component.selectedPartyIds.set(new Set(['id1', 'id2']));
      expect(component.hasSelection()).toBe(true);

      component.clearSelection();

      expect(component.selectedPartyIds().size).toBe(0);
      expect(component.hasSelection()).toBe(false);
    });
  });
});
