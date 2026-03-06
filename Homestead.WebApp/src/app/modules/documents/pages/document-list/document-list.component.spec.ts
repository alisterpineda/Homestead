import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { DocumentListComponent } from './document-list.component';
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
    it('should expose the four expected sort fields in order', () => {
      const values = component.sortOptions.map(o => o.value);
      expect(values).toEqual<DocumentSortBy[]>(['Name', 'DocumentDate', 'ContentType', 'FileSize']);
    });

    it('should have a label for each sort option', () => {
      for (const opt of component.sortOptions) {
        expect(opt.label.length).toBeGreaterThan(0);
      }
    });
  });

  describe('onSortByChange', () => {
    it('should call onSortChange(null) when field is null', () => {
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange(null);
      expect(spy).toHaveBeenCalledWith(null);
    });

    it('should default to Asc when no sortDir is active', () => {
      // sortDir() is null/undefined by default (no query params)
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortByChange('Name');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'Name', sortDir: 'Asc' });
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
      // sortBy() is null by default
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
      (component as any).sortBy = signal<DocumentSortBy>('DocumentDate');
      const spy = vi.spyOn(component, 'onSortChange');
      component.onSortDirChange('Asc');
      expect(spy).toHaveBeenCalledWith({ sortBy: 'DocumentDate', sortDir: 'Asc' });
    });
  });
});
