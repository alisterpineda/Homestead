import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router, ActivatedRoute, convertToParamMap } from '@angular/router';
import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { BehaviorSubject, EMPTY, of, Subject } from 'rxjs';
import { DocumentDetailPageComponent } from './document-detail-page.component';
import { AuthService } from '@/shared/services/auth.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { OpenDocumentsService } from './open-documents.service';
import { DocumentEditStateService } from './document-edit-state.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { TextExtractionProvenanceResponse } from '@/api/generated/models/text-extraction-provenance-response';
import type { GetDocumentVersionDto } from '@/api/generated/models/get-document-version-dto';
import type { GetDocumentVersionResponse } from '@/api/generated/models/get-document-version-response';

@Component({ template: '' })
class DummyComponent {}

describe('DocumentDetailPageComponent', () => {
  let fixture: ComponentFixture<DocumentDetailPageComponent>;
  let component: DocumentDetailPageComponent;
  let router: Router;
  let mockOpenDocumentsService: Partial<OpenDocumentsService>;
  let mockEditStateService: Partial<DocumentEditStateService>;
  let paramMapSubject: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let openDocumentsSignal: ReturnType<typeof signal<{ id: string; name: string }[]>>;

  function setDocumentId(id: string | null) {
    const params = id ? { id } : {};
    paramMapSubject.next(convertToParamMap(params));
  }

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    vi.stubGlobal('sessionStorage', {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    paramMapSubject = new BehaviorSubject(convertToParamMap({}));
    openDocumentsSignal = signal<{ id: string; name: string }[]>([]);

    mockOpenDocumentsService = {
      addDocument: vi.fn(),
      removeDocument: vi.fn(),
      getPrevDocumentId: vi.fn(() => null),
      getNextDocumentId: vi.fn(() => null),
      openDocuments: openDocumentsSignal as unknown as OpenDocumentsService['openDocuments'],
    };

    mockEditStateService = {
      saveState: vi.fn(),
      getState: vi.fn(() => null),
      clearState: vi.fn(),
      clearAll: vi.fn(),
      discardChanges: vi.fn(),
      setActiveHasChanges: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([
          { path: 'documents', component: DummyComponent },
          { path: 'documents/:id', component: DummyComponent },
        ]),
        PageHeaderService,
        { provide: AuthService, useValue: { canWrite: signal(true) } },
        { provide: OpenDocumentsService, useValue: mockOpenDocumentsService },
        { provide: DocumentEditStateService, useValue: mockEditStateService },
        {
          provide: ProcessingHubService,
          useValue: { jobCompleted$: new Subject(), waitForJob: vi.fn(() => new Promise(() => {})) },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: paramMapSubject.asObservable(),
            snapshot: { paramMap: convertToParamMap({}) },
          },
        },
      ],
    });

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(DocumentDetailPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('handleClose', () => {
    it('should navigate to /documents', () => {
      const navSpy = vi.spyOn(router, 'navigate');
      component.handleClose();
      expect(navSpy).toHaveBeenCalledWith(['/documents']);
    });

    it('should remove the document from open documents when documentId exists', () => {
      setDocumentId('doc-123');
      fixture.detectChanges();

      component.handleClose();

      expect(mockOpenDocumentsService.removeDocument).toHaveBeenCalledWith('doc-123');
    });

    it('should not call removeDocument when documentId is null', () => {
      component.handleClose();
      expect(mockOpenDocumentsService.removeDocument).not.toHaveBeenCalled();
    });

    it('should discard edit state on close', () => {
      setDocumentId('doc-123');
      fixture.detectChanges();

      component.handleClose();

      expect(mockEditStateService.discardChanges).toHaveBeenCalledWith('doc-123');
    });
  });

  describe('navigatePrev', () => {
    it('should navigate to the previous document when one exists', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getPrevDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-1');
      const navSpy = vi.spyOn(router, 'navigate');

      component.navigatePrev();

      expect(mockOpenDocumentsService.getPrevDocumentId).toHaveBeenCalledWith('doc-2');
      expect(navSpy).toHaveBeenCalledWith(['/documents', 'doc-1']);
    });

    it('should not navigate when there is no previous document', () => {
      setDocumentId('doc-1');
      fixture.detectChanges();

      const navSpy = vi.spyOn(router, 'navigate');

      component.navigatePrev();

      expect(navSpy).not.toHaveBeenCalled();
    });

    it('should not navigate when documentId is null', () => {
      const navSpy = vi.spyOn(router, 'navigate');
      component.navigatePrev();
      expect(navSpy).not.toHaveBeenCalled();
    });

    it('should persist edit state before navigating when hasChanges is true', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getPrevDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-1');
      Object.defineProperty(component, 'hasChanges', {
        value: () => true,
        writable: true,
      });

      component.navigatePrev();

      expect(mockEditStateService.saveState).toHaveBeenCalledWith('doc-2', expect.any(Object));
    });

    it('should not persist state when no changes', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getPrevDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-1');

      component.navigatePrev();

      expect(mockEditStateService.saveState).not.toHaveBeenCalled();
    });
  });

  describe('navigateNext', () => {
    it('should navigate to the next document when one exists', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getNextDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-3');
      const navSpy = vi.spyOn(router, 'navigate');

      component.navigateNext();

      expect(mockOpenDocumentsService.getNextDocumentId).toHaveBeenCalledWith('doc-2');
      expect(navSpy).toHaveBeenCalledWith(['/documents', 'doc-3']);
    });

    it('should not navigate when there is no next document', () => {
      setDocumentId('doc-last');
      fixture.detectChanges();

      const navSpy = vi.spyOn(router, 'navigate');

      component.navigateNext();

      expect(navSpy).not.toHaveBeenCalled();
    });

    it('should not navigate when documentId is null', () => {
      const navSpy = vi.spyOn(router, 'navigate');
      component.navigateNext();
      expect(navSpy).not.toHaveBeenCalled();
    });

    it('should persist edit state before navigating when hasChanges is true', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getNextDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-3');
      Object.defineProperty(component, 'hasChanges', {
        value: () => true,
        writable: true,
      });

      component.navigateNext();

      expect(mockEditStateService.saveState).toHaveBeenCalledWith('doc-2', expect.any(Object));
    });

    it('should not persist state when no changes', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getNextDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-3');

      component.navigateNext();

      expect(mockEditStateService.saveState).not.toHaveBeenCalled();
    });
  });

  describe('canNavigatePrev / canNavigateNext', () => {
    it('should return false when documentId is null', () => {
      expect(component.canNavigatePrev()).toBe(false);
      expect(component.canNavigateNext()).toBe(false);
    });

    it('should reflect OpenDocumentsService navigation availability', () => {
      setDocumentId('doc-2');
      fixture.detectChanges();

      (mockOpenDocumentsService.getPrevDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-1');
      (mockOpenDocumentsService.getNextDocumentId as ReturnType<typeof vi.fn>).mockReturnValue('doc-3');

      expect(component.canNavigatePrev()).toBe(true);
      expect(component.canNavigateNext()).toBe(true);
    });

    it('should return false when no prev/next exists', () => {
      setDocumentId('doc-only');
      fixture.detectChanges();

      expect(component.canNavigatePrev()).toBe(false);
      expect(component.canNavigateNext()).toBe(false);
    });
  });

  describe('ngOnDestroy state persistence', () => {
    it('should save edit state to service when destroyed with changes and document is still open', () => {
      setDocumentId('doc-1');
      fixture.detectChanges();
      openDocumentsSignal.set([{ id: 'doc-1', name: 'Doc 1' }]);

      Object.defineProperty(component, 'hasChanges', {
        value: () => true,
        writable: true,
      });

      component.ngOnDestroy();

      expect(mockEditStateService.saveState).toHaveBeenCalledWith('doc-1', expect.any(Object));
    });

    it('should not save state when destroyed without changes', () => {
      setDocumentId('doc-1');
      fixture.detectChanges();
      openDocumentsSignal.set([{ id: 'doc-1', name: 'Doc 1' }]);

      component.ngOnDestroy();

      expect(mockEditStateService.saveState).not.toHaveBeenCalled();
    });

    it('should not save state when document has been removed from open list (close flow)', () => {
      setDocumentId('doc-1');
      fixture.detectChanges();

      Object.defineProperty(component, 'hasChanges', {
        value: () => true,
        writable: true,
      });

      component.ngOnDestroy();

      expect(mockEditStateService.saveState).not.toHaveBeenCalled();
    });

    it('should clear active changes tracking on destroy', () => {
      setDocumentId('doc-1');
      fixture.detectChanges();

      // Clear any calls from effects
      (mockEditStateService.setActiveHasChanges as ReturnType<typeof vi.fn>).mockClear();

      component.ngOnDestroy();

      expect(mockEditStateService.setActiveHasChanges).toHaveBeenCalledWith('doc-1', false);
    });
  });
});

describe('DocumentDetailPageComponent — edit-state guards on refresh', () => {
    let guardFixture: ComponentFixture<DocumentDetailPageComponent>;
    let guardComponent: DocumentDetailPageComponent;
    let guardParamMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
    let guardEditStateService: Partial<DocumentEditStateService>;
    let guardOpenDocumentsService: Partial<OpenDocumentsService>;
    let guardOpenDocsSignal: ReturnType<typeof signal<{ id: string; name: string }[]>>;
    let guardDocumentApi: {
      getDocument: ReturnType<typeof vi.fn>;
      getDocumentFile: ReturnType<typeof vi.fn>;
      patchDocument: ReturnType<typeof vi.fn>;
      bulkClassifyDocuments: ReturnType<typeof vi.fn>;
      bulkSummarizeDocuments: ReturnType<typeof vi.fn>;
    };
    let guardVersionApi: {
      getDocumentVersion: ReturnType<typeof vi.fn>;
      getDocumentActivity: ReturnType<typeof vi.fn>;
      patchDocumentVersion: ReturnType<typeof vi.fn>;
      createComment: ReturnType<typeof vi.fn>;
    };
    let guardProcessingHub: {
      jobCompleted$: Subject<unknown>;
      waitForJob: ReturnType<typeof vi.fn>;
    };
    let docResponse: GetDocumentResponse | null;
    let versionDetailSubject: Subject<GetDocumentVersionResponse | null>;
    let jobCompletedSubject: Subject<unknown>;

    function makeVersion(overrides: Partial<GetDocumentVersionDto> = {}): GetDocumentVersionDto {
      return {
        id: 'ver-1',
        contentType: 'application/pdf',
        fileExtension: '.pdf',
        fileSize: 100,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        versionNumber: 1,
        label: 'server label',
        ...overrides,
      };
    }

    function makeVersionDetail(
      overrides: Partial<GetDocumentVersionResponse> = {},
    ): GetDocumentVersionResponse {
      return {
        id: 'ver-1',
        contentType: 'application/pdf',
        fileExtension: '.pdf',
        fileSize: 100,
        fileHash: 'hash',
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        versionNumber: 1,
        label: 'server label',
        ...overrides,
      };
    }

    function makeDoc(overrides: Partial<GetDocumentResponse> = {}): GetDocumentResponse {
      return {
        id: 'doc-1',
        name: 'Doc 1',
        contentType: 'application/pdf',
        fileExtension: '.pdf',
        fileHash: 'hash',
        fileSize: 100,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        parties: [],
        tags: [],
        suggestions: [],
        versions: [makeVersion()],
        extractedText: 'server text',
        ...overrides,
      };
    }

    function setGuardDocumentId(id: string | null) {
      const params = id ? { id } : {};
      guardParamMap.next(convertToParamMap(params));
    }

    beforeEach(() => {
      vi.stubGlobal('localStorage', {
        getItem: vi.fn(() => null),
        setItem: vi.fn(),
        removeItem: vi.fn(),
      });
      vi.stubGlobal('sessionStorage', {
        getItem: vi.fn(() => null),
        setItem: vi.fn(),
        removeItem: vi.fn(),
      });

      guardParamMap = new BehaviorSubject(convertToParamMap({}));
      jobCompletedSubject = new Subject<unknown>();
      versionDetailSubject = new Subject<GetDocumentVersionResponse | null>();
      docResponse = null;

      guardOpenDocsSignal = signal<{ id: string; name: string }[]>([]);
      guardOpenDocumentsService = {
        addDocument: vi.fn(),
        removeDocument: vi.fn(),
        getPrevDocumentId: vi.fn(() => null),
        getNextDocumentId: vi.fn(() => null),
        openDocuments: guardOpenDocsSignal as unknown as OpenDocumentsService['openDocuments'],
      };

      guardEditStateService = {
        saveState: vi.fn(),
        getState: vi.fn(() => null),
        clearState: vi.fn(),
        clearAll: vi.fn(),
        discardChanges: vi.fn(),
        setActiveHasChanges: vi.fn(),
      };

      guardDocumentApi = {
        getDocument: vi.fn(() => of(docResponse)),
        getDocumentFile: vi.fn(() => EMPTY),
        patchDocument: vi.fn(() => of(null)),
        bulkClassifyDocuments: vi.fn(() => of({ jobIds: ['job-1'] })),
        bulkSummarizeDocuments: vi.fn(() => of({ jobIds: ['job-1'] })),
      };

      guardVersionApi = {
        getDocumentVersion: vi.fn(() => versionDetailSubject.asObservable()),
        getDocumentActivity: vi.fn(() => of({ items: [] })),
        patchDocumentVersion: vi.fn(() => of(null)),
        createComment: vi.fn(() => of(null)),
      };

      // waitForJob returns a never-resolving promise so the busy flag stays set for the assertion;
      // the enqueue + waitForJob calls are what these specs verify.
      guardProcessingHub = {
        jobCompleted$: jobCompletedSubject,
        waitForJob: vi.fn(() => new Promise<never>(() => {})),
      };

      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideRouter([{ path: 'documents', component: DummyComponent }]),
          PageHeaderService,
          { provide: AuthService, useValue: { canWrite: signal(true) } },
          { provide: OpenDocumentsService, useValue: guardOpenDocumentsService },
          { provide: DocumentEditStateService, useValue: guardEditStateService },
          { provide: DocumentApiService, useValue: guardDocumentApi },
          { provide: DocumentVersionApiService, useValue: guardVersionApi },
          { provide: ProcessingHubService, useValue: guardProcessingHub },
          {
            provide: ActivatedRoute,
            useValue: {
              paramMap: guardParamMap.asObservable(),
              snapshot: { paramMap: convertToParamMap({}) },
            },
          },
        ],
      });

      // Override the template with a minimal placeholder. The component
      // owns lots of heavy child components (NgScrollbar, dialogs, Spartan
      // tabs/resizable) that exercise browser APIs jsdom doesn't support.
      // We only care about exercising the guard-effect logic, not rendering.
      TestBed.overrideComponent(DocumentDetailPageComponent, {
        set: { template: '<div></div>' },
      });

      guardFixture = TestBed.createComponent(DocumentDetailPageComponent);
      guardComponent = guardFixture.componentInstance;
      guardFixture.detectChanges();
    });

    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('refreshTrigger increment does not wipe editVersionLabel or editExtractedText', () => {
      docResponse = makeDoc();
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      guardComponent.editVersionLabel.set('user typed label');
      guardComponent.editExtractedText.set('user typed text');

      guardComponent.refreshTrigger.update((v) => v + 1);
      guardFixture.detectChanges();

      expect(guardComponent.editVersionLabel()).toBe('user typed label');
      expect(guardComponent.editExtractedText()).toBe('user typed text');
    });

    it('SignalR jobCompleted$ does not wipe edits', () => {
      docResponse = makeDoc();
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      guardComponent.editVersionLabel.set('user typed label');
      guardComponent.editExtractedText.set('user typed text');

      jobCompletedSubject.next(null);
      guardFixture.detectChanges();

      expect(guardComponent.editVersionLabel()).toBe('user typed label');
      expect(guardComponent.editExtractedText()).toBe('user typed text');
    });

    // Reprocess rewrites a version's extracted text in place, so the version id stays the same and cannot
    // act as the seed key. These pin the `${versionId}|${extractedAt}` key: a re-extraction re-seeds, a
    // plain re-fetch does not, and a restored draft outranks both.
    function makeExtraction(extractedAt: string): TextExtractionProvenanceResponse {
      return { engine: 'tesseract', engineKey: 'tesseract', extractedAt };
    }

    it('a re-extraction (new extractedAt) re-seeds editExtractedText even though the version id is unchanged', () => {
      docResponse = makeDoc({
        extractedText: 'old pdfpig text',
        textExtraction: makeExtraction('2026-01-01T00:00:00Z'),
      });
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      expect(guardComponent.editExtractedText()).toBe('old pdfpig text');

      // Same document, same version id — only the extraction changed (i.e. an azure-layout reprocess).
      docResponse = makeDoc({
        extractedText: '<table><tr><td>azure</td></tr></table>',
        textExtraction: makeExtraction('2026-01-02T00:00:00Z'),
      });
      jobCompletedSubject.next(null);
      guardFixture.detectChanges();

      expect(guardComponent.editExtractedText()).toBe('<table><tr><td>azure</td></tr></table>');
      // Without this the field is stale and hasChanges() is spuriously true, so a save would PATCH the old
      // text back over the new extraction.
      expect(guardComponent.hasChanges()).toBe(false);
    });

    it('a re-fetch with an unchanged extractedAt does not wipe edits', () => {
      docResponse = makeDoc({
        extractedText: 'server text',
        textExtraction: makeExtraction('2026-01-01T00:00:00Z'),
      });
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      guardComponent.editExtractedText.set('user typed text');

      // Same extraction re-delivered — must not clobber the user.
      docResponse = makeDoc({
        extractedText: 'server text',
        textExtraction: makeExtraction('2026-01-01T00:00:00Z'),
      });
      jobCompletedSubject.next(null);
      guardFixture.detectChanges();

      expect(guardComponent.editExtractedText()).toBe('user typed text');
    });

    it('a restored draft is not clobbered when the extraction changed while it was away', () => {
      (guardEditStateService.getState as ReturnType<typeof vi.fn>).mockReturnValue({
        editName: 'saved name',
        editDocumentDate: null,
        editExpiryDate: null,
        editNotes: 'saved notes',
        editParties: [],
        editTags: [],
        editVersionLabel: 'saved label',
        editExtractedText: 'draft text the user typed',
        versionId: null,
      });

      // The server has since re-extracted — a naive key would seed over the draft here.
      docResponse = makeDoc({
        extractedText: 'newly extracted server text',
        textExtraction: makeExtraction('2026-06-01T00:00:00Z'),
      });
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      expect(guardComponent.editExtractedText()).toBe('draft text the user typed');
    });

    it('saved-state restoration survives the first activeVersion() emit', () => {
      (guardEditStateService.getState as ReturnType<typeof vi.fn>).mockReturnValue({
        editName: 'saved name',
        editDocumentDate: null,
        editExpiryDate: null,
        editNotes: 'saved notes',
        editParties: [],
        editTags: [],
        editVersionLabel: 'saved label',
        editExtractedText: 'saved text',
        versionId: 'ver-1',
      });

      docResponse = makeDoc();
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      expect(guardComponent.editVersionLabel()).toBe('saved label');
      expect(guardComponent.editExtractedText()).toBe('saved text');
    });

    it('switching to a different version reinitializes from server values', () => {
      docResponse = makeDoc({
        versions: [
          makeVersion({ id: 'ver-1', label: 'v1 label', versionNumber: 1 }),
          makeVersion({ id: 'ver-2', label: 'v2 label', versionNumber: 2 }),
        ],
        extractedText: 'v2 text',
      });
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      // Latest is ver-2
      expect(guardComponent.editVersionLabel()).toBe('v2 label');
      expect(guardComponent.editExtractedText()).toBe('v2 text');

      guardComponent.editVersionLabel.set('user typed');
      guardComponent.editExtractedText.set('user typed text');

      guardComponent.onVersionSelected('ver-1');
      guardFixture.detectChanges();

      versionDetailSubject.next(
        makeVersionDetail({ label: 'v1 label', extractedText: 'v1 text' }),
      );
      guardFixture.detectChanges();

      expect(guardComponent.editVersionLabel()).toBe('v1 label');
      expect(guardComponent.editExtractedText()).toBe('v1 text');
    });

    it('after save, re-fetch overwrites edit fields with server values', () => {
      docResponse = makeDoc({
        versions: [makeVersion({ label: 'old label' })],
      });
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      guardComponent.editVersionLabel.set('user typed');

      // Simulate the server returning a different value than the user typed
      // (e.g., a sibling editor updated the label between page load and save).
      docResponse = makeDoc({
        versions: [makeVersion({ label: 'server value' })],
      });

      guardComponent.handleSave();
      guardFixture.detectChanges();

      expect(guardComponent.editVersionLabel()).toBe('server value');
    });

    it('late-resolved versionDetail populates editExtractedText on a non-latest version', () => {
      docResponse = makeDoc({
        versions: [
          makeVersion({ id: 'ver-1', label: 'v1 label', versionNumber: 1 }),
          makeVersion({ id: 'ver-2', label: 'v2 label', versionNumber: 2 }),
        ],
        extractedText: 'v2 text',
      });

      // Pre-select non-latest version before doc loads
      (guardComponent as unknown as { versionId: { set(v: string): void } }).versionId.set('ver-1');
      setGuardDocumentId('doc-1');
      guardFixture.detectChanges();

      // versionDetail hasn't emitted yet; editExtractedText is still empty
      expect(guardComponent.editExtractedText()).toBe('');

      // Late-resolve the version detail
      versionDetailSubject.next(
        makeVersionDetail({ label: 'v1 label', extractedText: 'v1 late text' }),
      );
      guardFixture.detectChanges();

      expect(guardComponent.editExtractedText()).toBe('v1 late text');
    });

    describe('ngOnDestroy does not save when document is closed', () => {
      it('skips save when document has been removed from open list', () => {
        docResponse = makeDoc();
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();

        guardComponent.editName.set('Changed');

        Object.defineProperty(guardComponent, 'hasChanges', {
          value: () => true,
          writable: true,
          configurable: true,
        });

        guardComponent.ngOnDestroy();

        expect(guardEditStateService.saveState).not.toHaveBeenCalled();
      });
    });

    describe('classify and summarize enqueue async jobs', () => {
      it('enqueues a single-document bulk classify with the selected fields and waits on the job', () => {
        docResponse = makeDoc({ name: 'Original' });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();

        guardComponent.handleClassify({ fields: ['Tags'] });

        expect(guardDocumentApi.bulkClassifyDocuments).toHaveBeenCalledWith({
          body: { ids: ['doc-1'], fields: ['Tags'] },
        });
        // The parent job is awaited via the processing hub (SignalR), not a synchronous endpoint.
        expect(guardProcessingHub.waitForJob).toHaveBeenCalledWith('job-1');
        // Busy flag stays set across the background run (waitForJob never resolves in this mock).
        expect(guardComponent.classifyingPreview()).toBe(true);
      });

      it('enqueues a single-document bulk summarize and waits on the job', () => {
        docResponse = makeDoc({ name: 'Original' });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();

        guardComponent.handleSummarize();

        expect(guardDocumentApi.bulkSummarizeDocuments).toHaveBeenCalledWith({
          body: { ids: ['doc-1'] },
        });
        expect(guardProcessingHub.waitForJob).toHaveBeenCalledWith('job-1');
        expect(guardComponent.summarizing()).toBe(true);
      });
    });
});
