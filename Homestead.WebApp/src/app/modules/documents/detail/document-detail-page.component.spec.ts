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
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { DocumentSuggestionDto } from '@/api/generated/models/document-suggestion-dto';
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
        // isAdmin is not optional here: the real template gates the Backup section on it, so a mock
        // without it throws the moment that branch is evaluated.
        { provide: AuthService, useValue: { canWrite: signal(true), isAdmin: signal(false) } },
        { provide: AdminBackupApiService, useValue: { getDocumentBackupState: vi.fn(() => EMPTY) } },
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
      getDocumentDownloadUrl: ReturnType<typeof vi.fn>;
      patchDocument: ReturnType<typeof vi.fn>;
      bulkClassifyDocuments: ReturnType<typeof vi.fn>;
      bulkSummarizeDocuments: ReturnType<typeof vi.fn>;
      approveSuggestion: ReturnType<typeof vi.fn>;
      declineSuggestion: ReturnType<typeof vi.fn>;
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
        getDocumentDownloadUrl: vi.fn((id: string, versionId: string | null) => `/download/${id}/${versionId}`),
        patchDocument: vi.fn(() => of(null)),
        bulkClassifyDocuments: vi.fn(() => of({ jobIds: ['job-1'] })),
        bulkSummarizeDocuments: vi.fn(() => of({ jobIds: ['job-1'] })),
        approveSuggestion: vi.fn(() => of(null)),
        declineSuggestion: vi.fn(() => of(null)),
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
          // isAdmin is not optional here: the real template gates the Backup section on it, so a mock
          // without it throws the moment that branch is evaluated.
          { provide: AuthService, useValue: { canWrite: signal(true), isAdmin: signal(false) } },
          {
            provide: AdminBackupApiService,
            useValue: { getDocumentBackupState: vi.fn(() => EMPTY) },
          },
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

    // getDocumentFile never emits here, so these run with the preview still loading.
    describe('handleDownload', () => {
      let clicked: HTMLAnchorElement[];

      beforeEach(() => {
        clicked = [];
        vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
          clicked.push(this);
        });
      });

      afterEach(() => {
        vi.restoreAllMocks();
      });

      it('downloads from the server without waiting for the preview', () => {
        docResponse = makeDoc();
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();

        guardComponent.handleDownload();

        expect(guardDocumentApi.getDocumentDownloadUrl).toHaveBeenCalledWith('doc-1', null);
        expect(clicked).toHaveLength(1);
        expect(clicked[0].getAttribute('href')).toBe('/download/doc-1/null');
        expect(clicked[0].download).toBe('Doc 1.pdf');
      });

      it('downloads the version being viewed', () => {
        docResponse = makeDoc({ versions: [makeVersion({ id: 'ver-2', versionNumber: 2 }), makeVersion()] });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();
        guardComponent.onVersionSelected('ver-1');
        guardFixture.detectChanges();

        guardComponent.handleDownload();

        expect(guardDocumentApi.getDocumentDownloadUrl).toHaveBeenCalledWith('doc-1', 'ver-1');
      });
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

    // The textarea is read-only until explicitly unlocked. Unlocking warns only when the text currently
    // carries engine provenance (that is the record a hand edit replaces); the lock snaps back whenever the
    // seed effect reseeds, and a restored draft that already differs is editable without any click.
    describe('extracted-text edit gate', () => {
      function loadDoc(overrides: Partial<GetDocumentResponse> = {}) {
        docResponse = makeDoc({
          extractedText: 'server text',
          textExtraction: makeExtraction('2026-01-01T00:00:00Z'),
          ...overrides,
        });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();
      }

      it('is locked once the document seeds', () => {
        loadDoc();

        expect(guardComponent.textEditable()).toBe(false);
        expect(guardComponent.editTextDialogState()).toBe('closed');
      });

      it('asks before unlocking engine-extracted text, and unlocks on confirm', () => {
        loadDoc();

        guardComponent.startTextEdit();

        expect(guardComponent.editTextDialogState()).toBe('open');
        expect(guardComponent.textEditable()).toBe(false);

        guardComponent.confirmTextEdit();

        expect(guardComponent.editTextDialogState()).toBe('closed');
        expect(guardComponent.textEditable()).toBe(true);
      });

      it('unlocks without asking when the text has no provenance', () => {
        loadDoc({ textExtraction: null });

        guardComponent.startTextEdit();

        expect(guardComponent.editTextDialogState()).toBe('closed');
        expect(guardComponent.textEditable()).toBe(true);
      });

      // A second correction has nothing left to disconnect: the text is already credited to a person.
      it('unlocks without asking when the text was already hand-edited', () => {
        loadDoc({
          textExtraction: {
            engine: 'manual',
            engineKey: null,
            extractedAt: '2026-02-01T00:00:00Z',
            editedByDisplayName: 'Pat',
            basisEngine: 'tesseract',
            basisEngineKey: 'tesseract',
            basisExtractedAt: '2026-01-01T00:00:00Z',
          },
        });

        guardComponent.startTextEdit();

        expect(guardComponent.editTextDialogState()).toBe('closed');
        expect(guardComponent.textEditable()).toBe(true);
      });

      it('cancelling an edit restores the stored text and relocks', () => {
        loadDoc();
        guardComponent.confirmTextEdit();
        guardComponent.editExtractedText.set('half-typed correction');
        expect(guardComponent.hasChanges()).toBe(true);

        guardComponent.cancelTextEdit();

        expect(guardComponent.editExtractedText()).toBe('server text');
        expect(guardComponent.textEditable()).toBe(false);
        expect(guardComponent.hasChanges()).toBe(false);
      });

      it('switching versions relocks', () => {
        loadDoc({
          versions: [
            makeVersion({ id: 'ver-1', versionNumber: 1 }),
            makeVersion({ id: 'ver-2', versionNumber: 2 }),
          ],
        });
        guardComponent.confirmTextEdit();
        expect(guardComponent.textEditable()).toBe(true);

        guardComponent.onVersionSelected('ver-1');
        guardFixture.detectChanges();
        versionDetailSubject.next(makeVersionDetail({ extractedText: 'v1 text' }));
        guardFixture.detectChanges();

        expect(guardComponent.textEditable()).toBe(false);
      });

      it('a re-extraction landing while unlocked relocks', () => {
        loadDoc();
        guardComponent.confirmTextEdit();

        docResponse = makeDoc({
          extractedText: 'freshly extracted',
          textExtraction: makeExtraction('2026-03-01T00:00:00Z'),
        });
        jobCompletedSubject.next(null);
        guardFixture.detectChanges();

        expect(guardComponent.editExtractedText()).toBe('freshly extracted');
        expect(guardComponent.textEditable()).toBe(false);
      });

      it('saving sends the edited text and relocks on the re-fetch', () => {
        loadDoc();
        guardComponent.confirmTextEdit();
        guardComponent.editExtractedText.set('corrected text');

        // What the server answers after the PATCH: the corrected text, now credited to the editor.
        docResponse = makeDoc({
          extractedText: 'corrected text',
          textExtraction: {
            engine: 'manual',
            engineKey: null,
            extractedAt: '2026-04-01T00:00:00Z',
            editedByDisplayName: 'Pat',
            basisEngine: 'tesseract',
            basisEngineKey: 'tesseract',
            basisExtractedAt: '2026-01-01T00:00:00Z',
          },
        });
        guardComponent.handleSave();
        guardFixture.detectChanges();

        expect(guardVersionApi.patchDocumentVersion).toHaveBeenCalledWith({
          documentId: 'doc-1',
          id: 'ver-1',
          body: [{ op: 'replace', path: '/extractedText', value: 'corrected text' }],
        });
        expect(guardComponent.editExtractedText()).toBe('corrected text');
        expect(guardComponent.textEditable()).toBe(false);
      });

      // The sidebar reuses this component across documents. A draft restored for the next document takes
      // the seed effect's adopt path (no reseed, no relock), so the unlock has to be dropped on the document
      // switch itself — otherwise B's engine text opens for editing with no confirmation.
      it('an unlock does not carry over to the next document when that document restores a draft', () => {
        loadDoc();
        guardComponent.confirmTextEdit();
        expect(guardComponent.textEditable()).toBe(true);

        (guardEditStateService.getState as ReturnType<typeof vi.fn>).mockReturnValue({
          editName: 'renamed but unsaved',
          editDocumentDate: null,
          editExpiryDate: null,
          editNotes: '',
          editParties: [],
          editTags: [],
          editVersionLabel: 'server label',
          editExtractedText: 'doc 2 engine text',
          versionId: null,
        });
        docResponse = makeDoc({
          id: 'doc-2',
          name: 'Doc 2',
          extractedText: 'doc 2 engine text',
          textExtraction: makeExtraction('2026-02-01T00:00:00Z'),
        });
        setGuardDocumentId('doc-2');
        guardFixture.detectChanges();

        expect(guardComponent.editName()).toBe('renamed but unsaved');
        expect(guardComponent.textEditable()).toBe(false);
      });

      // Nothing is persisted about the lock: a draft that differs from the stored text is, by definition,
      // an edit in progress.
      it('a restored draft that differs from the stored text is editable without a click', () => {
        (guardEditStateService.getState as ReturnType<typeof vi.fn>).mockReturnValue({
          editName: 'Doc 1',
          editDocumentDate: null,
          editExpiryDate: null,
          editNotes: '',
          editParties: [],
          editTags: [],
          editVersionLabel: 'server label',
          editExtractedText: 'draft the user typed before leaving',
          versionId: null,
        });

        loadDoc();

        expect(guardComponent.textEditable()).toBe(true);
      });
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

    describe('AI date suggestions', () => {
      function makeSuggestion(overrides: Partial<DocumentSuggestionDto>): DocumentSuggestionDto {
        return {
          id: 'sug-1',
          field: 'DocumentDate',
          status: 'Pending',
          suggestedValue: '2025-03-15',
          originalValue: null,
          createdAt: '2026-01-01T00:00:00Z',
          ...overrides,
        };
      }

      it('exposes the pending date suggestions per field, ignoring resolved ones', () => {
        docResponse = makeDoc({
          suggestions: [
            makeSuggestion({ id: 'sug-doc', field: 'DocumentDate' }),
            makeSuggestion({ id: 'sug-exp-old', field: 'ExpiryDate', status: 'Declined' }),
            makeSuggestion({ id: 'sug-name', field: 'Name', suggestedValue: 'AI Name' }),
          ],
        });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();

        expect(guardComponent.pendingDocumentDateSuggestion()?.id).toBe('sug-doc');
        expect(guardComponent.pendingExpiryDateSuggestion()).toBeUndefined();
        expect(guardComponent.pendingNameSuggestion()?.id).toBe('sug-name');
      });

      it('formats a suggestion original for the tooltip, (none) when it was empty', () => {
        expect(guardComponent.formatSuggestionOriginal(null)).toBe('(none)');
        expect(guardComponent.formatSuggestionOriginal(undefined)).toBe('(none)');
        expect(guardComponent.formatSuggestionOriginal('2025-01-01')).toBe('Jan 1, 2025');
      });

      it('labels the tooltip only while a suggestion is pending', () => {
        expect(guardComponent.suggestionOriginalLabel(undefined)).toBeNull();
        expect(guardComponent.suggestionOriginalLabel(makeSuggestion({ originalValue: '2025-01-01' })))
          .toBe('Original: Jan 1, 2025');
        expect(guardComponent.suggestionOriginalLabel(makeSuggestion({ originalValue: null })))
          .toBe('Original: (none)');
      });

      it('approving a date suggestion calls the API with its id and refetches', () => {
        docResponse = makeDoc({ suggestions: [makeSuggestion({ id: 'sug-doc' })] });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();
        guardDocumentApi.getDocument.mockClear();

        guardComponent.approveSuggestion(guardComponent.pendingDocumentDateSuggestion()!.id);
        guardFixture.detectChanges();

        expect(guardDocumentApi.approveSuggestion).toHaveBeenCalledWith({ documentId: 'doc-1', suggestionId: 'sug-doc' });
        expect(guardDocumentApi.getDocument).toHaveBeenCalled();
      });

      it('declining a date suggestion calls the API with its id and re-seeds the date from the server', () => {
        docResponse = makeDoc({
          documentDate: '2025-03-15',
          suggestions: [makeSuggestion({ id: 'sug-doc', originalValue: '2025-01-01' })],
        });
        setGuardDocumentId('doc-1');
        guardFixture.detectChanges();
        guardDocumentApi.getDocument.mockClear();

        // The server restores the original on decline.
        docResponse = makeDoc({ documentDate: '2025-01-01', suggestions: [] });
        guardComponent.declineSuggestion('sug-doc');
        guardFixture.detectChanges();

        expect(guardDocumentApi.declineSuggestion).toHaveBeenCalledWith({ documentId: 'doc-1', suggestionId: 'sug-doc' });
        expect(guardDocumentApi.getDocument).toHaveBeenCalled();
        expect(guardComponent.editDocumentDate()).toEqual(new Date(2025, 0, 1));
        expect(guardComponent.pendingDocumentDateSuggestion()).toBeUndefined();
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

// --- Backup section (admin only) --------------------------------------------------------------------
//
// The template gates the section on `auth.isAdmin()`, and the fetch is gated on it in TypeScript too,
// so a non-admin issues zero requests against an admin-only endpoint. The overridden template keeps
// these focused on the fetch keying — what the section itself renders is covered by
// document-backup-state.component.spec.ts.
describe('DocumentDetailPageComponent — backup state', () => {
  let backupParamMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let backupApi: { getDocumentBackupState: ReturnType<typeof vi.fn> };
  let backupFixture: ComponentFixture<DocumentDetailPageComponent>;
  let backupComponent: DocumentDetailPageComponent;

  function backupDocument(): GetDocumentResponse {
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
      versions: [
        {
          id: 'ver-1',
          contentType: 'application/pdf',
          fileExtension: '.pdf',
          fileSize: 100,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
          versionNumber: 1,
        },
      ],
    };
  }

  function configure(isAdmin: boolean, options: { realTemplate?: boolean } = {}) {
    backupParamMap = new BehaviorSubject(convertToParamMap({}));
    backupApi = {
      getDocumentBackupState: vi.fn(() =>
        of({
          documentId: 'doc-1',
          liveVersionCount: 1,
          sources: [
            {
              backupSourceId: 'src-1',
              targetKey: 'local',
              targetKind: 'Local' as const,
              isAvailable: true,
              isEnabled: true,
              targetEnabled: true,
              copiedVersionCount: 1,
              pendingRemovalCount: 0,
              versions: [],
            },
          ],
        }),
      ),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'documents', component: DummyComponent }]),
        PageHeaderService,
        { provide: AuthService, useValue: { canWrite: signal(true), isAdmin: signal(isAdmin) } },
        { provide: AdminBackupApiService, useValue: backupApi },
        {
          provide: OpenDocumentsService,
          useValue: {
            addDocument: vi.fn(),
            removeDocument: vi.fn(),
            getPrevDocumentId: vi.fn(() => null),
            getNextDocumentId: vi.fn(() => null),
            openDocuments: signal([]) as unknown as OpenDocumentsService['openDocuments'],
          },
        },
        {
          provide: DocumentEditStateService,
          useValue: {
            saveState: vi.fn(),
            getState: vi.fn(() => null),
            clearState: vi.fn(),
            clearAll: vi.fn(),
            discardChanges: vi.fn(),
            setActiveHasChanges: vi.fn(),
          },
        },
        {
          provide: DocumentApiService,
          useValue: {
            getDocument: vi.fn(() => of(options.realTemplate ? backupDocument() : null)),
            getDocumentFile: vi.fn(() => EMPTY),
            patchDocument: vi.fn(() => of(null)),
          },
        },
        {
          provide: DocumentVersionApiService,
          useValue: {
            getDocumentVersion: vi.fn(() => EMPTY),
            getDocumentActivity: vi.fn(() => of({ items: [] })),
          },
        },
        {
          provide: ProcessingHubService,
          useValue: {
            jobCompleted$: new Subject(),
            waitForJob: vi.fn(() => new Promise(() => {})),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: backupParamMap.asObservable(),
            snapshot: { paramMap: convertToParamMap({}) },
          },
        },
      ],
    });

    // Overridden by default: this component owns heavy children (NgScrollbar, resizable panels,
    // dialogs) that jsdom cannot render, and most of these specs are about the fetch, not the markup.
    // One case opts back into the real template — the only place an `auth.isAdmin()` template gate
    // could actually throw.
    if (!options.realTemplate) {
      TestBed.overrideComponent(DocumentDetailPageComponent, { set: { template: '<div></div>' } });
    }

    backupFixture = TestBed.createComponent(DocumentDetailPageComponent);
    backupComponent = backupFixture.componentInstance;
    backupFixture.detectChanges();

    backupParamMap.next(convertToParamMap({ id: 'doc-1' }));
    backupFixture.detectChanges();
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
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  // The latch keeps an admin-only request off the wire for a page nobody opened that deeply.
  it('fetches nothing until the Metadata tab has been opened', () => {
    configure(true);

    expect(backupApi.getDocumentBackupState).not.toHaveBeenCalled();

    backupComponent.onTabActivated('metadata');
    backupFixture.detectChanges();

    expect(backupApi.getDocumentBackupState).toHaveBeenCalledWith('doc-1');
    expect(backupComponent.backupState()?.sources?.length).toBe(1);
  });

  it('issues no request at all for a non-admin', () => {
    configure(false);

    backupComponent.onTabActivated('metadata');
    backupFixture.detectChanges();

    expect(backupApi.getDocumentBackupState).not.toHaveBeenCalled();
    expect(backupComponent.backupState()).toBeNull();
  });

  // The latch lives on the COMPONENT, not inside <hlm-tabs>: there are two tab instances under
  // @if (narrow()), so a resize across 768px destroys whichever one holds the state.
  it('keeps the latch set once opened', () => {
    configure(true);

    backupComponent.onTabActivated('metadata');
    backupComponent.onTabActivated('info');
    backupFixture.detectChanges();

    expect(backupComponent.metadataOpened()).toBe(true);
  });

  // Load-bearing keying: the component is REUSED across prev/next navigation and the active tab does
  // not change during it, so a fetch driven only by tabActivated would leave the previous document's
  // backup state on screen under the new document's name.
  it('refetches when the document changes with the tab already open', () => {
    configure(true);

    backupComponent.onTabActivated('metadata');
    backupFixture.detectChanges();
    expect(backupApi.getDocumentBackupState).toHaveBeenCalledTimes(1);

    backupParamMap.next(convertToParamMap({ id: 'doc-2' }));
    backupFixture.detectChanges();

    expect(backupApi.getDocumentBackupState).toHaveBeenCalledWith('doc-2');
  });

  it('refetches on the explicit refresh affordance', () => {
    configure(true);

    backupComponent.onTabActivated('metadata');
    backupFixture.detectChanges();
    expect(backupApi.getDocumentBackupState).toHaveBeenCalledTimes(1);

    backupComponent.refreshBackupState();
    backupFixture.detectChanges();

    expect(backupApi.getDocumentBackupState).toHaveBeenCalledTimes(2);
  });

  // The one case that renders the real template. Without `isAdmin` on the AuthService mock this throws
  // rather than failing an assertion, which is why every mock in this file now carries it.
  it('renders the section for an admin once the Metadata tab is open', async () => {
    configure(true, { realTemplate: true });

    backupComponent.onTabActivated('metadata');
    backupFixture.detectChanges();
    await backupFixture.whenStable();
    backupFixture.detectChanges();

    const element = backupFixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="document-backup-state"]')).toBeTruthy();
    expect(element.querySelector('[data-testid="refresh-backup-state"]')).toBeTruthy();
  });
});

// --- AI date suggestion controls (real template) ----------------------------------------------------
//
// The date fields' approve/decline wiring lives in the page template, so these render it for real: a
// copy-paste slip between the two date blocks (wrong suggestion, wrong field) only shows up here.
describe('DocumentDetailPageComponent — AI date suggestion controls', () => {
  let dateFixture: ComponentFixture<DocumentDetailPageComponent>;
  let dateDocumentApi: {
    getDocument: ReturnType<typeof vi.fn>;
    getDocumentFile: ReturnType<typeof vi.fn>;
    patchDocument: ReturnType<typeof vi.fn>;
    approveSuggestion: ReturnType<typeof vi.fn>;
    declineSuggestion: ReturnType<typeof vi.fn>;
  };

  function documentWithPendingExpiry(): GetDocumentResponse {
    return {
      id: 'doc-1',
      name: 'Doc 1',
      contentType: 'application/pdf',
      fileExtension: '.pdf',
      fileHash: 'hash',
      fileSize: 100,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      expiryDate: '2030-06-30',
      parties: [],
      tags: [],
      suggestions: [
        {
          id: 'sug-expiry',
          field: 'ExpiryDate',
          status: 'Pending',
          suggestedValue: '2030-06-30',
          originalValue: null,
          createdAt: '2026-01-01T00:00:00Z',
        },
      ],
      versions: [
        {
          id: 'ver-1',
          contentType: 'application/pdf',
          fileExtension: '.pdf',
          fileSize: 100,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
          versionNumber: 1,
        },
      ],
    };
  }

  beforeEach(async () => {
    vi.stubGlobal('localStorage', { getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn() });
    vi.stubGlobal('sessionStorage', { getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn() });

    const paramMap = new BehaviorSubject(convertToParamMap({}));
    dateDocumentApi = {
      getDocument: vi.fn(() => of(documentWithPendingExpiry())),
      getDocumentFile: vi.fn(() => EMPTY),
      patchDocument: vi.fn(() => of(null)),
      approveSuggestion: vi.fn(() => of(null)),
      declineSuggestion: vi.fn(() => of(null)),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'documents', component: DummyComponent }]),
        PageHeaderService,
        { provide: AuthService, useValue: { canWrite: signal(true), isAdmin: signal(false) } },
        { provide: AdminBackupApiService, useValue: { getDocumentBackupState: vi.fn(() => EMPTY) } },
        {
          provide: OpenDocumentsService,
          useValue: {
            addDocument: vi.fn(),
            removeDocument: vi.fn(),
            getPrevDocumentId: vi.fn(() => null),
            getNextDocumentId: vi.fn(() => null),
            openDocuments: signal([]) as unknown as OpenDocumentsService['openDocuments'],
          },
        },
        {
          provide: DocumentEditStateService,
          useValue: {
            saveState: vi.fn(),
            getState: vi.fn(() => null),
            clearState: vi.fn(),
            clearAll: vi.fn(),
            discardChanges: vi.fn(),
            setActiveHasChanges: vi.fn(),
          },
        },
        { provide: DocumentApiService, useValue: dateDocumentApi },
        {
          provide: DocumentVersionApiService,
          useValue: {
            getDocumentVersion: vi.fn(() => EMPTY),
            getDocumentActivity: vi.fn(() => of({ items: [] })),
          },
        },
        {
          provide: ProcessingHubService,
          useValue: { jobCompleted$: new Subject(), waitForJob: vi.fn(() => new Promise(() => {})) },
        },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: paramMap.asObservable(), snapshot: { paramMap: convertToParamMap({}) } },
        },
      ],
    });

    dateFixture = TestBed.createComponent(DocumentDetailPageComponent);
    dateFixture.detectChanges();
    paramMap.next(convertToParamMap({ id: 'doc-1' }));
    dateFixture.detectChanges();
    await dateFixture.whenStable();
    dateFixture.detectChanges();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  function button(label: string): HTMLButtonElement | null {
    return (dateFixture.nativeElement as HTMLElement).querySelector(`button[aria-label="${label}"]`);
  }

  it('shows approve/decline only on the field whose suggestion is pending', () => {
    expect(button('Approve expiry date suggestion')).not.toBeNull();
    expect(button('Decline expiry date suggestion')).not.toBeNull();
    expect(button('Approve document date suggestion')).toBeNull();
    expect(button('Decline document date suggestion')).toBeNull();
  });

  it('decline on the expiry date declines the expiry suggestion', () => {
    button('Decline expiry date suggestion')!.click();

    expect(dateDocumentApi.declineSuggestion).toHaveBeenCalledWith({ documentId: 'doc-1', suggestionId: 'sug-expiry' });
    expect(dateDocumentApi.approveSuggestion).not.toHaveBeenCalled();
  });

  it('approve on the expiry date approves the expiry suggestion', () => {
    button('Approve expiry date suggestion')!.click();

    expect(dateDocumentApi.approveSuggestion).toHaveBeenCalledWith({ documentId: 'doc-1', suggestionId: 'sug-expiry' });
    expect(dateDocumentApi.declineSuggestion).not.toHaveBeenCalled();
  });
});

// The rendered gate: the textarea carries the readonly attribute until unlocked, and only writers get the
// button that unlocks it. (Tab panels stay in the DOM when inactive, so the Content tab's controls are
// reachable without activating it.)
describe('DocumentDetailPageComponent — extracted-text lock in the template', () => {
  let lockFixture: ComponentFixture<DocumentDetailPageComponent>;

  function documentWithEngineText(): GetDocumentResponse {
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
      extractedText: 'engine text',
      textExtraction: { engine: 'tesseract', engineKey: 'tesseract', extractedAt: '2026-01-01T00:00:00Z' },
      versions: [
        {
          id: 'ver-1',
          contentType: 'application/pdf',
          fileExtension: '.pdf',
          fileSize: 100,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
          versionNumber: 1,
        },
      ],
    };
  }

  async function configure(canWrite: boolean) {
    vi.stubGlobal('localStorage', { getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn() });
    vi.stubGlobal('sessionStorage', { getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn() });

    const paramMap = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'documents', component: DummyComponent }]),
        PageHeaderService,
        { provide: AuthService, useValue: { canWrite: signal(canWrite), isAdmin: signal(false) } },
        { provide: AdminBackupApiService, useValue: { getDocumentBackupState: vi.fn(() => EMPTY) } },
        {
          provide: OpenDocumentsService,
          useValue: {
            addDocument: vi.fn(),
            removeDocument: vi.fn(),
            getPrevDocumentId: vi.fn(() => null),
            getNextDocumentId: vi.fn(() => null),
            openDocuments: signal([]) as unknown as OpenDocumentsService['openDocuments'],
          },
        },
        {
          provide: DocumentEditStateService,
          useValue: {
            saveState: vi.fn(),
            getState: vi.fn(() => null),
            clearState: vi.fn(),
            clearAll: vi.fn(),
            discardChanges: vi.fn(),
            setActiveHasChanges: vi.fn(),
          },
        },
        {
          provide: DocumentApiService,
          useValue: {
            getDocument: vi.fn(() => of(documentWithEngineText())),
            getDocumentFile: vi.fn(() => EMPTY),
            patchDocument: vi.fn(() => of(null)),
          },
        },
        {
          provide: DocumentVersionApiService,
          useValue: {
            getDocumentVersion: vi.fn(() => EMPTY),
            getDocumentActivity: vi.fn(() => of({ items: [] })),
            patchDocumentVersion: vi.fn(() => of(null)),
          },
        },
        {
          provide: ProcessingHubService,
          useValue: { jobCompleted$: new Subject(), waitForJob: vi.fn(() => new Promise(() => {})) },
        },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: paramMap.asObservable(), snapshot: { paramMap: convertToParamMap({}) } },
        },
      ],
    });

    lockFixture = TestBed.createComponent(DocumentDetailPageComponent);
    lockFixture.detectChanges();
    paramMap.next(convertToParamMap({ id: 'doc-1' }));
    lockFixture.detectChanges();
    await lockFixture.whenStable();
    lockFixture.detectChanges();
  }

  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  function textarea(): HTMLTextAreaElement {
    return (lockFixture.nativeElement as HTMLElement).querySelector('#extracted-text') as HTMLTextAreaElement;
  }

  function buttonLabelled(label: string): HTMLButtonElement | undefined {
    return Array.from((lockFixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    );
  }

  it('renders the text read-only with an Edit text button for a writer', async () => {
    await configure(true);

    expect(textarea().readOnly).toBe(true);
    expect(buttonLabelled('Edit text')).toBeDefined();
    expect(buttonLabelled('Cancel edit')).toBeUndefined();
  });

  it('drops the readonly attribute and offers Cancel edit once unlocked', async () => {
    await configure(true);

    lockFixture.componentInstance.confirmTextEdit();
    lockFixture.detectChanges();

    expect(textarea().readOnly).toBe(false);
    expect(buttonLabelled('Cancel edit')).toBeDefined();
    expect(buttonLabelled('Edit text')).toBeUndefined();
    expect((lockFixture.nativeElement as HTMLElement).textContent).toContain(
      'Saving will mark this text as manually edited.',
    );
  });

  // A viewer could never save; the read-only field now says so instead of inviting typing.
  it('gives a viewer the read-only text and no Edit text button', async () => {
    await configure(false);

    expect(textarea().readOnly).toBe(true);
    expect(buttonLabelled('Edit text')).toBeUndefined();
  });
});
