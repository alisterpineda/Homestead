import { TestBed } from '@angular/core/testing';
import { DocumentEditStateService, type DocumentEditState } from './document-edit-state.service';

function makeState(overrides?: Partial<DocumentEditState>): DocumentEditState {
  return {
    editName: 'Test Doc',
    editDocumentDate: '2024-01-15',
    editExpiryDate: null,
    editNotes: '',
    editParties: [{ id: 'p1', name: 'Party 1' }],
    editTags: ['tag/a', 'tag/b'],
    editVersionLabel: 'v1 label',
    editExtractedText: 'some text',
    versionId: 'ver-1',
    ...overrides,
  };
}

describe('DocumentEditStateService', () => {
  let service: DocumentEditStateService;
  let mockStorage: Record<string, string>;

  beforeEach(() => {
    mockStorage = {};
    vi.stubGlobal('sessionStorage', {
      getItem: vi.fn((key: string) => mockStorage[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });

    TestBed.configureTestingModule({});
    service = TestBed.inject(DocumentEditStateService);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('saveState / getState', () => {
    it('should save and retrieve state for a document', () => {
      const state = makeState();
      service.saveState('doc-1', state);
      expect(service.getState('doc-1')).toEqual(state);
    });

    it('should return null for unknown document', () => {
      expect(service.getState('unknown')).toBeNull();
    });

    it('should persist to sessionStorage', () => {
      TestBed.flushEffects(); // initial skip
      const state = makeState();
      service.saveState('doc-1', state);
      TestBed.flushEffects();
      expect(sessionStorage.setItem).toHaveBeenCalled();
      const stored = JSON.parse(mockStorage['homestead-document-edit-state']);
      expect(stored['doc-1']).toEqual(state);
    });

    it('should overwrite existing state for the same document', () => {
      service.saveState('doc-1', makeState({ editName: 'Old' }));
      service.saveState('doc-1', makeState({ editName: 'New' }));
      expect(service.getState('doc-1')?.editName).toBe('New');
    });

    it('should store multiple documents independently', () => {
      service.saveState('doc-1', makeState({ editName: 'Doc 1' }));
      service.saveState('doc-2', makeState({ editName: 'Doc 2' }));
      expect(service.getState('doc-1')?.editName).toBe('Doc 1');
      expect(service.getState('doc-2')?.editName).toBe('Doc 2');
    });
  });

  describe('clearState', () => {
    it('should clear state for a specific document', () => {
      service.saveState('doc-1', makeState());
      service.saveState('doc-2', makeState());
      service.clearState('doc-1');
      expect(service.getState('doc-1')).toBeNull();
      expect(service.getState('doc-2')).not.toBeNull();
    });

    it('should do nothing when clearing non-existent document', () => {
      TestBed.flushEffects(); // initial skip
      (sessionStorage.setItem as ReturnType<typeof vi.fn>).mockClear();
      service.clearState('unknown');
      TestBed.flushEffects();
      expect(sessionStorage.setItem).not.toHaveBeenCalled();
    });
  });

  describe('clearAll', () => {
    it('should clear all stored states', () => {
      service.saveState('doc-1', makeState());
      service.saveState('doc-2', makeState());
      service.clearAll();
      expect(service.getState('doc-1')).toBeNull();
      expect(service.getState('doc-2')).toBeNull();
    });

    it('should clear active changes tracking', () => {
      service.setActiveHasChanges('doc-1', true);
      service.clearAll();
      expect(service.unsavedDocIds().has('doc-1')).toBe(false);
    });
  });

  describe('hasState', () => {
    it('should return true when state exists', () => {
      service.saveState('doc-1', makeState());
      expect(service.hasState('doc-1')).toBe(true);
    });

    it('should return false when state does not exist', () => {
      expect(service.hasState('doc-1')).toBe(false);
    });

    it('should return false after clearing', () => {
      service.saveState('doc-1', makeState());
      service.clearState('doc-1');
      expect(service.hasState('doc-1')).toBe(false);
    });
  });

  describe('discardChanges', () => {
    it('should clear both stored state and active tracking', () => {
      service.saveState('doc-1', makeState());
      service.setActiveHasChanges('doc-1', true);

      service.discardChanges('doc-1');

      expect(service.getState('doc-1')).toBeNull();
      expect(service.unsavedDocIds().has('doc-1')).toBe(false);
    });

    it('should not affect other documents', () => {
      service.saveState('doc-1', makeState());
      service.saveState('doc-2', makeState());
      service.setActiveHasChanges('doc-2', true);

      service.discardChanges('doc-1');

      expect(service.getState('doc-2')).not.toBeNull();
      expect(service.unsavedDocIds().has('doc-2')).toBe(true);
    });
  });

  describe('setActiveHasChanges', () => {
    it('should track active changes in unsavedDocIds', () => {
      service.setActiveHasChanges('doc-1', true);
      expect(service.unsavedDocIds().has('doc-1')).toBe(true);
    });

    it('should remove from unsavedDocIds when set to false', () => {
      service.setActiveHasChanges('doc-1', true);
      service.setActiveHasChanges('doc-1', false);
      expect(service.unsavedDocIds().has('doc-1')).toBe(false);
    });

    it('should still show in unsavedDocIds when stored state exists even if active is false', () => {
      service.saveState('doc-1', makeState());
      service.setActiveHasChanges('doc-1', false);
      expect(service.unsavedDocIds().has('doc-1')).toBe(true);
    });
  });

  describe('unsavedDocIds', () => {
    it('should return empty set when no unsaved changes', () => {
      expect(service.unsavedDocIds().size).toBe(0);
    });

    it('should include documents with active changes', () => {
      service.setActiveHasChanges('doc-1', true);
      service.setActiveHasChanges('doc-2', false);
      expect(service.unsavedDocIds().has('doc-1')).toBe(true);
      expect(service.unsavedDocIds().has('doc-2')).toBe(false);
    });

    it('should include documents with stored state', () => {
      service.saveState('doc-1', makeState());
      expect(service.unsavedDocIds().has('doc-1')).toBe(true);
    });

    it('should combine active and stored sources', () => {
      service.setActiveHasChanges('doc-1', true);
      service.saveState('doc-2', makeState());
      const ids = service.unsavedDocIds();
      expect(ids.has('doc-1')).toBe(true);
      expect(ids.has('doc-2')).toBe(true);
    });
  });

  describe('sessionStorage persistence', () => {
    it('should load stored doc IDs from sessionStorage on construction', () => {
      mockStorage['homestead-document-edit-state'] = JSON.stringify({
        'doc-1': makeState(),
      });
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({});
      const freshService = TestBed.inject(DocumentEditStateService);
      expect(freshService.hasState('doc-1')).toBe(true);
    });

    it('should handle invalid JSON gracefully', () => {
      mockStorage['homestead-document-edit-state'] = 'not valid json';
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({});
      const freshService = TestBed.inject(DocumentEditStateService);
      expect(freshService.getState('doc-1')).toBeNull();
    });

    it('should handle non-object JSON gracefully', () => {
      mockStorage['homestead-document-edit-state'] = '[1,2,3]';
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({});
      const freshService = TestBed.inject(DocumentEditStateService);
      expect(freshService.getState('doc-1')).toBeNull();
    });
  });
});
