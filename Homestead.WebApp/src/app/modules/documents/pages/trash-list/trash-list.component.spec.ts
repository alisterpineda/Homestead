import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { TrashListComponent } from './trash-list.component';
import type { DocumentSortBy } from '@/api/generated/models/document-sort-by';

describe('TrashListComponent', () => {
  let fixture: ComponentFixture<TrashListComponent>;
  let component: TrashListComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(TrashListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
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

  it('should initialise selectionMode as false', () => {
    expect(component.selectionMode()).toBe(false);
  });

  describe('sortOptions', () => {
    it('should include DeletedAt plus standard sort fields', () => {
      const values = component.sortOptions.map(o => o.value);
      expect(values).toEqual<DocumentSortBy[]>(['DeletedAt', 'CreatedAt', 'Name', 'ContentType', 'FileSize']);
    });

    it('should have a label for each sort option', () => {
      for (const opt of component.sortOptions) {
        expect(opt.label.length).toBeGreaterThan(0);
      }
    });
  });

  describe('effectiveSortBy', () => {
    it('should default to DeletedAt when no explicit sort and not searching', () => {
      expect(component.effectiveSortBy()).toBe('DeletedAt');
    });

    it('should return null when no explicit sort and searching (relevance)', () => {
      (component as any).searchTerm = signal('test');
      expect(component.effectiveSortBy()).toBeNull();
    });

    it('should return the explicit sort value when one is set', () => {
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      expect(component.effectiveSortBy()).toBe('Name');
    });
  });

  describe('effectiveSortDir', () => {
    it('should return Desc when no explicit sort (default for DeletedAt)', () => {
      expect(component.effectiveSortDir()).toBe('Desc');
    });

    it('should return the explicit direction when set', () => {
      (component as any).sortDir = signal('Asc');
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      expect(component.effectiveSortDir()).toBe('Asc');
    });
  });

  describe('sortLabel', () => {
    it('should show Date Deleted by default', () => {
      expect(component.sortLabel()).toBe('Date Deleted');
    });

    it('should show Relevance when searching with no explicit sort', () => {
      (component as any).searchTerm = signal('test');
      expect(component.sortLabel()).toBe('Relevance');
    });

    it('should show explicit sort field name', () => {
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      expect(component.sortLabel()).toBe('Name');
    });
  });

  describe('onSortByChange', () => {
    it('should call onSortChange(null) when field is null (Relevance)', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange(null);
      expect(spy).toHaveBeenCalledWith(null);
    });

    it('should default to Asc for non-date fields when no sortDir is active', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('Name');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Asc' });
    });

    it('should default to Desc for DeletedAt when no sortDir is active', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('DeletedAt');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DeletedAt', sortDir: 'Desc' });
    });

    it('should default to Desc for CreatedAt when no sortDir is active', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('CreatedAt');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'CreatedAt', sortDir: 'Desc' });
    });

    it('should preserve existing Desc direction when changing field', () => {
      (component as any).sortDir = signal('Desc');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('FileSize');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'FileSize', sortDir: 'Desc' });
    });
  });

  describe('onSortDirChange', () => {
    it('should do nothing when no sort field is active', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Desc');
      expect(spy).not.toHaveBeenCalled();
    });

    it('should call onSortChange with active field and Desc direction', () => {
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Desc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Desc' });
    });

    it('should call onSortChange with active field and Asc direction', () => {
      (component as any).sortBy = signal<DocumentSortBy>('DeletedAt');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Asc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DeletedAt', sortDir: 'Asc' });
    });
  });

  describe('isSearching', () => {
    it('should be false by default', () => {
      expect(component.isSearching()).toBe(false);
    });

    it('should be true when searchTerm has content', () => {
      (component as any).searchTerm = signal('test query');
      expect(component.isSearching()).toBe(true);
    });
  });

  describe('hasActiveFilters', () => {
    it('should not consider sort as an active filter', () => {
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('should be true when search term is present', () => {
      (component as any).searchTerm = signal('test');
      expect(component.hasActiveFilters()).toBe(true);
    });
  });
});
