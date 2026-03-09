import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router, ActivatedRoute, convertToParamMap } from '@angular/router';
import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { BehaviorSubject, Subject } from 'rxjs';
import { DocumentDetailPageComponent } from './document-detail-page.component';
import { AuthService } from '@/shared/services/auth.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { OpenDocumentsService } from '../../services/open-documents.service';
import { PageHeaderService } from '@/shared/services/page-header.service';

@Component({ template: '' })
class DummyComponent {}

describe('DocumentDetailPageComponent', () => {
  let fixture: ComponentFixture<DocumentDetailPageComponent>;
  let component: DocumentDetailPageComponent;
  let router: Router;
  let mockOpenDocumentsService: Partial<OpenDocumentsService>;
  let paramMapSubject: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

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

    paramMapSubject = new BehaviorSubject(convertToParamMap({}));

    mockOpenDocumentsService = {
      addDocument: vi.fn(),
      removeDocument: vi.fn(),
      getPrevDocumentId: vi.fn(() => null),
      getNextDocumentId: vi.fn(() => null),
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
        {
          provide: ProcessingHubService,
          useValue: { jobCompleted$: new Subject() },
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
});
