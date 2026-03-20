import { TestBed } from '@angular/core/testing';
import { TagListStateService } from './tag-list-state.service';

describe('TagListStateService', () => {
  let service: TagListStateService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(TagListStateService);
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
      service.saveParams({ search: 'second', sortBy: 'Name' });
      expect(service.getSavedParams()).toEqual({ search: 'second', sortBy: 'Name' });
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
      service.saveColumnVisibility({ description: false, createdAt: true });
      expect(service.getSavedColumnVisibility()).toEqual({ description: false, createdAt: true });
    });

    it('should overwrite previously saved column visibility', () => {
      service.saveColumnVisibility({ description: false });
      service.saveColumnVisibility({ description: true, createdAt: false });
      expect(service.getSavedColumnVisibility()).toEqual({ description: true, createdAt: false });
    });

    it('should store a shallow copy so external mutations do not affect saved state', () => {
      const visibility = { description: false };
      service.saveColumnVisibility(visibility);
      visibility.description = true;
      expect(service.getSavedColumnVisibility()!['description']).toBe(false);
    });
  });
});
