import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Component, signal } from '@angular/core';
import { DocumentsSidebarComponent } from './documents-sidebar.component';
import { OpenDocumentsService } from '../../services/open-documents.service';

@Component({ template: '' })
class DummyComponent {}

describe('DocumentsSidebarComponent', () => {
  let fixture: ComponentFixture<DocumentsSidebarComponent>;
  let component: DocumentsSidebarComponent;
  let router: Router;
  let mockOpenDocumentsService: Partial<OpenDocumentsService>;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    mockOpenDocumentsService = {
      openDocuments: signal([]),
      removeDocument: vi.fn(),
      clearAll: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'documents', component: DummyComponent },
          { path: 'documents/parties', component: DummyComponent },
          { path: 'documents/tags', component: DummyComponent },
          { path: 'documents/trash', component: DummyComponent },
          { path: 'documents/:id', component: DummyComponent },
          { path: 'documents/:id/:tab', component: DummyComponent },
        ]),
        { provide: OpenDocumentsService, useValue: mockOpenDocumentsService },
      ],
    });

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(DocumentsSidebarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('isActive', () => {
    it('should return true for exact match', async () => {
      await router.navigateByUrl('/documents');
      expect(component.isActive('/documents')).toBe(true);
    });

    it('should return true for /documents when on a document detail page', async () => {
      await router.navigateByUrl('/documents/abc-123');
      expect(component.isActive('/documents')).toBe(true);
    });

    it('should return false for /documents when on /documents/parties', async () => {
      await router.navigateByUrl('/documents/parties');
      expect(component.isActive('/documents')).toBe(false);
    });

    it('should return false for /documents when on /documents/tags', async () => {
      await router.navigateByUrl('/documents/tags');
      expect(component.isActive('/documents')).toBe(false);
    });

    it('should return false for /documents when on /documents/trash', async () => {
      await router.navigateByUrl('/documents/trash');
      expect(component.isActive('/documents')).toBe(false);
    });

    it('should return true for /documents/parties on exact match', async () => {
      await router.navigateByUrl('/documents/parties');
      expect(component.isActive('/documents/parties')).toBe(true);
    });

    it('should return true for /documents/tags on exact match', async () => {
      await router.navigateByUrl('/documents/tags');
      expect(component.isActive('/documents/tags')).toBe(true);
    });

    it('should return true for /documents/trash on exact match', async () => {
      await router.navigateByUrl('/documents/trash');
      expect(component.isActive('/documents/trash')).toBe(true);
    });

    it('should return false for non-matching paths', async () => {
      await router.navigateByUrl('/documents/parties');
      expect(component.isActive('/documents/tags')).toBe(false);
    });
  });

  describe('isOpenDocActive', () => {
    it('should return true for exact document path', async () => {
      await router.navigateByUrl('/documents/doc-1');
      expect(component.isOpenDocActive('doc-1')).toBe(true);
    });

    it('should return true for document sub-path', async () => {
      await router.navigateByUrl('/documents/doc-1/versions');
      expect(component.isOpenDocActive('doc-1')).toBe(true);
    });

    it('should return false for different document', async () => {
      await router.navigateByUrl('/documents/doc-2');
      expect(component.isOpenDocActive('doc-1')).toBe(false);
    });

    it('should return false for documents list', async () => {
      await router.navigateByUrl('/documents');
      expect(component.isOpenDocActive('doc-1')).toBe(false);
    });
  });

  describe('showOpenDocuments', () => {
    it('should be false when no open documents', () => {
      expect(component['showOpenDocuments']()).toBe(false);
    });

    it('should be true when documents are open', () => {
      (mockOpenDocumentsService.openDocuments as ReturnType<typeof signal>).set([
        { id: '1', name: 'Doc A' },
      ]);
      expect(component['showOpenDocuments']()).toBe(true);
    });
  });

  describe('closeOpenDocument', () => {
    it('should remove the document', async () => {
      await router.navigateByUrl('/documents');
      const event = new Event('click');
      vi.spyOn(event, 'stopPropagation');

      component.closeOpenDocument(event, 'doc-1');

      expect(event.stopPropagation).toHaveBeenCalled();
      expect(mockOpenDocumentsService.removeDocument).toHaveBeenCalledWith('doc-1');
    });

    it('should navigate to /documents when closing the active document', async () => {
      await router.navigateByUrl('/documents/doc-1');
      const navSpy = vi.spyOn(router, 'navigate');

      component.closeOpenDocument(new Event('click'), 'doc-1');

      expect(mockOpenDocumentsService.removeDocument).toHaveBeenCalledWith('doc-1');
      expect(navSpy).toHaveBeenCalledWith(['/documents']);
    });

    it('should not navigate when closing a non-active document', async () => {
      await router.navigateByUrl('/documents/doc-2');
      const navSpy = vi.spyOn(router, 'navigate');

      component.closeOpenDocument(new Event('click'), 'doc-1');

      expect(mockOpenDocumentsService.removeDocument).toHaveBeenCalledWith('doc-1');
      expect(navSpy).not.toHaveBeenCalled();
    });
  });

  describe('clearAllOpenDocuments', () => {
    it('should clear all documents and navigate to /documents when viewing an open document', async () => {
      (mockOpenDocumentsService.openDocuments as ReturnType<typeof signal>).set([
        { id: 'doc-1', name: 'Doc 1' },
      ]);
      await router.navigateByUrl('/documents/doc-1');
      const navSpy = vi.spyOn(router, 'navigate');

      component.clearAllOpenDocuments();

      expect(mockOpenDocumentsService.clearAll).toHaveBeenCalled();
      expect(navSpy).toHaveBeenCalledWith(['/documents']);
    });

    it('should clear all documents without navigating when on the documents list', async () => {
      (mockOpenDocumentsService.openDocuments as ReturnType<typeof signal>).set([
        { id: 'doc-1', name: 'Doc 1' },
      ]);
      await router.navigateByUrl('/documents');
      const navSpy = vi.spyOn(router, 'navigate');

      component.clearAllOpenDocuments();

      expect(mockOpenDocumentsService.clearAll).toHaveBeenCalled();
      expect(navSpy).not.toHaveBeenCalled();
    });

    it('should not navigate when on a section page like /documents/parties', async () => {
      (mockOpenDocumentsService.openDocuments as ReturnType<typeof signal>).set([
        { id: 'doc-1', name: 'Doc 1' },
      ]);
      await router.navigateByUrl('/documents/parties');
      const navSpy = vi.spyOn(router, 'navigate');

      component.clearAllOpenDocuments();

      expect(mockOpenDocumentsService.clearAll).toHaveBeenCalled();
      expect(navSpy).not.toHaveBeenCalled();
    });
  });
});
