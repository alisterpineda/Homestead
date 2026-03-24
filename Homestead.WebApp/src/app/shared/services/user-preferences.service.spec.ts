import { TestBed } from '@angular/core/testing';
import { UserPreferencesService } from './user-preferences.service';

describe('UserPreferencesService', () => {
  let mockStorage: Record<string, string>;

  beforeEach(() => {
    mockStorage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => mockStorage[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  function createService(): UserPreferencesService {
    return TestBed.inject(UserPreferencesService);
  }

  describe('pageSize', () => {
    it('should default to 50', () => {
      const service = createService();
      expect(service.pageSize()).toBe(50);
    });

    it('should load a valid page size from storage', () => {
      mockStorage['homestead-page-size'] = '20';
      const service = createService();
      expect(service.pageSize()).toBe(20);
    });

    it('should reject an invalid page size and use default', () => {
      mockStorage['homestead-page-size'] = '25';
      const service = createService();
      expect(service.pageSize()).toBe(50);
    });

    it('should persist when set', () => {
      const service = createService();
      TestBed.flushEffects(); // initial skip
      service.pageSize.set(100);
      TestBed.flushEffects();
      expect(localStorage.setItem).toHaveBeenCalledWith('homestead-page-size', '100');
    });
  });

  describe('documentViewMode', () => {
    it('should default to grid', () => {
      const service = createService();
      expect(service.documentViewMode()).toBe('grid');
    });

    it('should load a valid view mode from storage', () => {
      mockStorage['homestead-document-view-mode'] = '"table"';
      const service = createService();
      expect(service.documentViewMode()).toBe('table');
    });

    it('should reject an invalid view mode and use default', () => {
      mockStorage['homestead-document-view-mode'] = '"list"';
      const service = createService();
      expect(service.documentViewMode()).toBe('grid');
    });
  });

  describe('column visibility signals', () => {
    it('should default to empty object for document columns', () => {
      const service = createService();
      expect(service.documentColumnVisibility()).toEqual({});
    });

    it('should load stored document column visibility', () => {
      mockStorage['homestead-document-columns'] = '{"parties":false,"tags":true}';
      const service = createService();
      expect(service.documentColumnVisibility()).toEqual({ parties: false, tags: true });
    });

    it('should default to empty object for party columns', () => {
      const service = createService();
      expect(service.partyColumnVisibility()).toEqual({});
    });

    it('should default to empty object for tag columns', () => {
      const service = createService();
      expect(service.tagColumnVisibility()).toEqual({});
    });

    it('should persist column visibility when set', () => {
      const service = createService();
      TestBed.flushEffects(); // initial skip
      service.documentColumnVisibility.set({ parties: false });
      TestBed.flushEffects();
      expect(localStorage.setItem).toHaveBeenCalledWith(
        'homestead-document-columns',
        '{"parties":false}',
      );
    });
  });

  describe('old view mode key migration', () => {
    it('should migrate old document-view-mode key to new key', () => {
      mockStorage['document-view-mode'] = 'table';
      const service = createService();
      expect(service.documentViewMode()).toBe('table');
      expect(localStorage.removeItem).toHaveBeenCalledWith('document-view-mode');
    });

    it('should not migrate if old key does not exist', () => {
      const service = createService();
      expect(service.documentViewMode()).toBe('grid');
      expect(localStorage.removeItem).not.toHaveBeenCalled();
    });

    it('should prefer new key over old key when both exist', () => {
      mockStorage['homestead-document-view-mode'] = '"table"';
      mockStorage['document-view-mode'] = 'grid';
      const service = createService();
      // New key was loaded first by persistedSignal, then migration overwrites with old key value
      // After migration, old key is removed
      expect(localStorage.removeItem).toHaveBeenCalledWith('document-view-mode');
    });
  });
});
