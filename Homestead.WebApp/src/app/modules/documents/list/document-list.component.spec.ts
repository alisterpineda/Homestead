import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { signal } from '@angular/core';
import { DocumentListComponent } from './document-list.component';
import { DEFAULT_COLUMN_VISIBILITY } from './document-table/document-table.component';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { DocumentSortBy } from '@/api/generated/models/document-sort-by';

describe('DocumentListComponent', () => {
  let fixture: ComponentFixture<DocumentListComponent>;
  let component: DocumentListComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(DocumentListComponent);
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

  describe('sortOptions', () => {
    it('should expose the five expected sort fields in order', () => {
      const values = component.sortOptions.map(o => o.value);
      expect(values).toEqual<DocumentSortBy[]>(['CreatedAt', 'DocumentDate', 'Name', 'ContentType', 'FileSize']);
    });

    it('should have a label for each sort option', () => {
      for (const opt of component.sortOptions) {
        expect(opt.label.length).toBeGreaterThan(0);
      }
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

    it('should default to Desc for CreatedAt when no sortDir is active', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('CreatedAt');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'CreatedAt', sortDir: 'Desc' });
    });

    it('should default to Desc for DocumentDate when no sortDir is active', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('DocumentDate');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DocumentDate', sortDir: 'Desc' });
    });

    it('should preserve existing Desc direction when changing field', () => {
      (component as any).sortDir = signal('Desc');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('FileSize');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'FileSize', sortDir: 'Desc' });
    });
  });

  describe('onSortDirChange', () => {
    it('should do nothing when searching with no explicit sort (relevance)', () => {
      (component as any).searchTerm = signal('test query');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Desc');
      expect(spy).not.toHaveBeenCalled();
    });

    it('should use default CreatedAt when not searching and no explicit sortBy is set', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Asc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'CreatedAt', sortDir: 'Asc' });
    });

    it('should call onSortChange with active field and Desc direction', () => {
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Desc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Desc' });
    });

    it('should call onSortChange with active field and Asc direction', () => {
      (component as any).sortBy = signal<DocumentSortBy>('DocumentDate');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Asc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DocumentDate', sortDir: 'Asc' });
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

  describe('effectiveSortBy', () => {
    it('should return CreatedAt when no explicit sort and not searching', () => {
      expect(component.effectiveSortBy()).toBe('CreatedAt');
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
    it('should return Desc when no explicit sort (default for CreatedAt)', () => {
      expect(component.effectiveSortDir()).toBe('Desc');
    });

    it('should return the explicit direction when set', () => {
      (component as any).sortDir = signal('Asc');
      (component as any).sortBy = signal<DocumentSortBy>('Name');
      expect(component.effectiveSortDir()).toBe('Asc');
    });
  });

  describe('sortLabel', () => {
    it('should show Date Added by default', () => {
      expect(component.sortLabel()).toBe('Date Added');
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

  describe('onDocumentSelect', () => {
    it('should navigate to the document detail page', () => {
      const router = TestBed.inject(Router);
      const spy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      component.onDocumentSelect('doc-123');
      expect(spy).toHaveBeenCalledWith(['/documents', 'doc-123']);
    });
  });

  describe('columnVisibility', () => {
    it('should default createdAt and documentDate to hidden', () => {
      expect(component.columnVisibility()).toEqual(expect.objectContaining({
        createdAt: false,
        documentDate: false,
      }));
    });

    it('should merge defaults underneath saved state', () => {
      const prefs = TestBed.inject(UserPreferencesService);
      prefs.documentColumnVisibility.set({ parties: false });

      // Re-create the component so the constructor runs with saved state
      const newFixture = TestBed.createComponent(DocumentListComponent);
      const newComponent = newFixture.componentInstance;
      const vis = newComponent.columnVisibility();

      expect(vis).toEqual(expect.objectContaining({
        parties: false,
        createdAt: false,
        documentDate: false,
      }));
    });

    it('should let saved state override defaults for date columns', () => {
      const prefs = TestBed.inject(UserPreferencesService);
      prefs.documentColumnVisibility.set({ createdAt: true, documentDate: true });

      const newFixture = TestBed.createComponent(DocumentListComponent);
      const newComponent = newFixture.componentInstance;
      const vis = newComponent.columnVisibility();

      expect(vis['createdAt']).toBe(true);
      expect(vis['documentDate']).toBe(true);
    });

    it('should toggle a column and persist to preferences', () => {
      const prefs = TestBed.inject(UserPreferencesService);

      component.onToggleColumn('createdAt');
      expect(component.columnVisibility()['createdAt']).toBe(true);
      expect(prefs.documentColumnVisibility()).toEqual(expect.objectContaining({ createdAt: true }));
    });
  });
});
