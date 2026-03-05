import { TestBed } from '@angular/core/testing';
import { DocumentListStateService } from './document-list-state.service';

describe('DocumentListStateService', () => {
  let service: DocumentListStateService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DocumentListStateService);
  });

  describe('initial state', () => {
    it('should have no saved params', () => {
      expect(service.getSavedParams()).toBeNull();
    });

    it('should have no saved column visibility', () => {
      expect(service.getSavedColumnVisibility()).toBeNull();
    });

    it('should report hasParams as false', () => {
      expect(service.hasParams).toBe(false);
    });
  });

  describe('saveParams / getSavedParams', () => {
    it('should store and retrieve params', () => {
      service.saveParams({ search: 'test', page: '2' });
      expect(service.getSavedParams()).toEqual({ search: 'test', page: '2' });
    });

    it('should overwrite previously saved params', () => {
      service.saveParams({ search: 'first' });
      service.saveParams({ search: 'second', sortBy: 'name' });
      expect(service.getSavedParams()).toEqual({ search: 'second', sortBy: 'name' });
    });

    it('should store a shallow copy so external mutations do not affect saved state', () => {
      const params = { search: 'test' };
      service.saveParams(params);
      params.search = 'mutated';
      expect(service.getSavedParams()!['search']).toBe('test');
    });
  });

  describe('hasParams', () => {
    it('should return true after saving non-empty params', () => {
      service.saveParams({ search: 'test' });
      expect(service.hasParams).toBe(true);
    });

    it('should return false after saving empty params', () => {
      service.saveParams({});
      expect(service.hasParams).toBe(false);
    });

    it('should return false when params were never saved', () => {
      expect(service.hasParams).toBe(false);
    });

    it('should reflect the latest saved params', () => {
      service.saveParams({ search: 'test' });
      expect(service.hasParams).toBe(true);
      service.saveParams({});
      expect(service.hasParams).toBe(false);
    });
  });

  describe('saveColumnVisibility / getSavedColumnVisibility', () => {
    it('should store and retrieve column visibility', () => {
      service.saveColumnVisibility({ parties: false, tags: true });
      expect(service.getSavedColumnVisibility()).toEqual({ parties: false, tags: true });
    });

    it('should overwrite previously saved column visibility', () => {
      service.saveColumnVisibility({ parties: false });
      service.saveColumnVisibility({ parties: true, tags: false });
      expect(service.getSavedColumnVisibility()).toEqual({ parties: true, tags: false });
    });

    it('should store a shallow copy so external mutations do not affect saved state', () => {
      const visibility = { parties: false };
      service.saveColumnVisibility(visibility);
      visibility.parties = true;
      expect(service.getSavedColumnVisibility()!['parties']).toBe(false);
    });
  });
});
