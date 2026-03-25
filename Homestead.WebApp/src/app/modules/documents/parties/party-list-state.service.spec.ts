import { TestBed } from '@angular/core/testing';
import { PartyListStateService } from './party-list-state.service';

describe('PartyListStateService', () => {
  let service: PartyListStateService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(PartyListStateService);
  });

  describe('initial state', () => {
    it('should have no saved params', () => {
      expect(service.getSavedParams()).toBeNull();
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

});
