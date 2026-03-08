import { TestBed } from '@angular/core/testing';
import { OpenDocumentsService } from './open-documents.service';

describe('OpenDocumentsService', () => {
  let service: OpenDocumentsService;
  let mockStorage: Record<string, string>;

  beforeEach(() => {
    mockStorage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => mockStorage[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });

    TestBed.configureTestingModule({});
    service = TestBed.inject(OpenDocumentsService);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('initial state', () => {
    it('should start with empty open documents', () => {
      expect(service.openDocuments()).toEqual([]);
    });

    it('should start with empty navigation list', () => {
      expect(service.navigationList()).toEqual([]);
    });
  });

  describe('addDocument', () => {
    it('should add a new document', () => {
      service.addDocument('1', 'Doc A');
      expect(service.openDocuments()).toEqual([{ id: '1', name: 'Doc A' }]);
    });

    it('should append new documents to the end', () => {
      service.addDocument('1', 'Doc A');
      service.addDocument('2', 'Doc B');
      service.addDocument('3', 'Doc C');
      expect(service.openDocuments().map(d => d.id)).toEqual(['1', '2', '3']);
    });

    it('should not reorder when re-adding an existing document', () => {
      service.addDocument('1', 'Doc A');
      service.addDocument('2', 'Doc B');
      service.addDocument('3', 'Doc C');
      service.addDocument('1', 'Doc A');
      expect(service.openDocuments().map(d => d.id)).toEqual(['1', '2', '3']);
    });

    it('should update name in place when re-adding an existing document', () => {
      service.addDocument('1', 'Old Name');
      service.addDocument('2', 'Doc B');
      service.addDocument('1', 'New Name');
      expect(service.openDocuments()).toEqual([
        { id: '1', name: 'New Name' },
        { id: '2', name: 'Doc B' },
      ]);
    });

    it('should not create a new array reference when name is unchanged', () => {
      service.addDocument('1', 'Doc A');
      const before = service.openDocuments();
      service.addDocument('1', 'Doc A');
      expect(service.openDocuments()).toBe(before);
    });

    it('should not write to localStorage when name is unchanged', () => {
      service.addDocument('1', 'Doc A');
      (localStorage.setItem as ReturnType<typeof vi.fn>).mockClear();
      service.addDocument('1', 'Doc A');
      expect(localStorage.setItem).not.toHaveBeenCalled();
    });

    it('should cap at 20 documents', () => {
      for (let i = 0; i < 25; i++) {
        service.addDocument(`id-${i}`, `Doc ${i}`);
      }
      expect(service.openDocuments().length).toBe(20);
    });

    it('should keep the most recent 20 when exceeding cap', () => {
      for (let i = 0; i < 25; i++) {
        service.addDocument(`id-${i}`, `Doc ${i}`);
      }
      expect(service.openDocuments()[0].id).toBe('id-5');
      expect(service.openDocuments()[19].id).toBe('id-24');
    });

    it('should persist to localStorage', () => {
      service.addDocument('1', 'Doc A');
      expect(localStorage.setItem).toHaveBeenCalledWith(
        'homestead-open-documents',
        JSON.stringify([{ id: '1', name: 'Doc A' }]),
      );
    });
  });

  describe('removeDocument', () => {
    it('should remove a document by id', () => {
      service.addDocument('1', 'Doc A');
      service.addDocument('2', 'Doc B');
      service.removeDocument('1');
      expect(service.openDocuments()).toEqual([{ id: '2', name: 'Doc B' }]);
    });

    it('should do nothing when id does not exist', () => {
      service.addDocument('1', 'Doc A');
      (localStorage.setItem as ReturnType<typeof vi.fn>).mockClear();
      service.removeDocument('nonexistent');
      expect(service.openDocuments()).toEqual([{ id: '1', name: 'Doc A' }]);
      expect(localStorage.setItem).not.toHaveBeenCalled();
    });

    it('should persist to localStorage', () => {
      service.addDocument('1', 'Doc A');
      (localStorage.setItem as ReturnType<typeof vi.fn>).mockClear();
      service.removeDocument('1');
      expect(localStorage.setItem).toHaveBeenCalledWith(
        'homestead-open-documents',
        '[]',
      );
    });
  });

  describe('clearAll', () => {
    it('should remove all documents', () => {
      service.addDocument('1', 'Doc A');
      service.addDocument('2', 'Doc B');
      service.clearAll();
      expect(service.openDocuments()).toEqual([]);
    });

    it('should persist empty array to localStorage', () => {
      service.addDocument('1', 'Doc A');
      (localStorage.setItem as ReturnType<typeof vi.fn>).mockClear();
      service.clearAll();
      expect(localStorage.setItem).toHaveBeenCalledWith(
        'homestead-open-documents',
        '[]',
      );
    });
  });

  describe('localStorage persistence', () => {
    it('should load documents from localStorage on construction', () => {
      const stored = [{ id: '1', name: 'Saved Doc' }];
      mockStorage['homestead-open-documents'] = JSON.stringify(stored);

      const freshService = new OpenDocumentsService();
      expect(freshService.openDocuments()).toEqual(stored);
    });

    it('should handle invalid JSON in localStorage gracefully', () => {
      mockStorage['homestead-open-documents'] = 'not valid json';

      const freshService = new OpenDocumentsService();
      expect(freshService.openDocuments()).toEqual([]);
    });

    it('should handle non-array JSON in localStorage gracefully', () => {
      mockStorage['homestead-open-documents'] = '{"foo":"bar"}';

      const freshService = new OpenDocumentsService();
      expect(freshService.openDocuments()).toEqual([]);
    });

    it('should filter out invalid entries from localStorage', () => {
      const stored = [
        { id: '1', name: 'Valid' },
        { id: 2, name: 'Bad id type' },
        { name: 'Missing id' },
        { id: '3' },
        null,
        { id: '4', name: 'Also Valid' },
      ];
      mockStorage['homestead-open-documents'] = JSON.stringify(stored);

      const freshService = new OpenDocumentsService();
      expect(freshService.openDocuments()).toEqual([
        { id: '1', name: 'Valid' },
        { id: '4', name: 'Also Valid' },
      ]);
    });
  });

  describe('navigationList', () => {
    it('should set navigation list', () => {
      service.setNavigationList(['a', 'b', 'c']);
      expect(service.navigationList()).toEqual(['a', 'b', 'c']);
    });

    it('should overwrite previous navigation list', () => {
      service.setNavigationList(['a', 'b']);
      service.setNavigationList(['x', 'y', 'z']);
      expect(service.navigationList()).toEqual(['x', 'y', 'z']);
    });
  });

  describe('getPrevDocumentId', () => {
    beforeEach(() => {
      service.setNavigationList(['a', 'b', 'c', 'd']);
    });

    it('should return null for the first document', () => {
      expect(service.getPrevDocumentId('a')).toBeNull();
    });

    it('should return the previous document id', () => {
      expect(service.getPrevDocumentId('b')).toBe('a');
      expect(service.getPrevDocumentId('c')).toBe('b');
      expect(service.getPrevDocumentId('d')).toBe('c');
    });

    it('should return null when id is not in the list', () => {
      expect(service.getPrevDocumentId('unknown')).toBeNull();
    });

    it('should return null when navigation list is empty', () => {
      service.setNavigationList([]);
      expect(service.getPrevDocumentId('a')).toBeNull();
    });
  });

  describe('getNextDocumentId', () => {
    beforeEach(() => {
      service.setNavigationList(['a', 'b', 'c', 'd']);
    });

    it('should return null for the last document', () => {
      expect(service.getNextDocumentId('d')).toBeNull();
    });

    it('should return the next document id', () => {
      expect(service.getNextDocumentId('a')).toBe('b');
      expect(service.getNextDocumentId('b')).toBe('c');
      expect(service.getNextDocumentId('c')).toBe('d');
    });

    it('should return null when id is not in the list', () => {
      expect(service.getNextDocumentId('unknown')).toBeNull();
    });

    it('should return null when navigation list is empty', () => {
      service.setNavigationList([]);
      expect(service.getNextDocumentId('a')).toBeNull();
    });
  });
});
