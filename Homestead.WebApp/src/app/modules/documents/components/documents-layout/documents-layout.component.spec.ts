import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Component, signal } from '@angular/core';
import { DocumentsLayoutComponent } from './documents-layout.component';
import { AuthService } from '@/shared/services/auth.service';
import { UploadManagerService } from '@/shared/services/upload-manager.service';
import { PageHeaderService } from '@/shared/services/page-header.service';

@Component({ template: '' })
class DummyComponent {}

describe('DocumentsLayoutComponent', () => {
  let fixture: ComponentFixture<DocumentsLayoutComponent>;
  let component: DocumentsLayoutComponent;
  let pageHeaderService: PageHeaderService;
  let canWrite: ReturnType<typeof signal<boolean>>;
  let mockUploadManager: Partial<UploadManagerService>;

  beforeEach(() => {
    canWrite = signal(true);
    mockUploadManager = { addFiles: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'documents', component: DummyComponent },
          { path: 'documents/:id', component: DummyComponent },
        ]),
        PageHeaderService,
        { provide: AuthService, useValue: { canWrite } },
        { provide: UploadManagerService, useValue: mockUploadManager },
      ],
    });

    pageHeaderService = TestBed.inject(PageHeaderService);
    fixture = TestBed.createComponent(DocumentsLayoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should clear header actions on destroy', () => {
    const clearSpy = vi.spyOn(pageHeaderService, 'clearActions');
    fixture.destroy();
    expect(clearSpy).toHaveBeenCalled();
  });

  describe('drag and drop', () => {
    function createDragEvent(type: string, hasFiles = true): DragEvent {
      const event = new Event(type, { bubbles: true }) as DragEvent;
      Object.defineProperty(event, 'dataTransfer', {
        value: { types: hasFiles ? ['Files'] : [], files: [] },
      });
      Object.defineProperty(event, 'preventDefault', { value: vi.fn() });
      return event;
    }

    it('should activate drag overlay on dragenter with files', () => {
      component.onDragEnter(createDragEvent('dragenter'));
      expect(component['dragActive']()).toBe(true);
    });

    it('should not activate drag for read-only users', () => {
      canWrite.set(false);
      component.onDragEnter(createDragEvent('dragenter'));
      expect(component['dragActive']()).toBe(false);
    });

    it('should not activate drag when no files in transfer', () => {
      component.onDragEnter(createDragEvent('dragenter', false));
      expect(component['dragActive']()).toBe(false);
    });

    it('should deactivate drag on dragleave when counter reaches zero', () => {
      component.onDragEnter(createDragEvent('dragenter'));
      component.onDragEnter(createDragEvent('dragenter'));
      expect(component['dragActive']()).toBe(true);

      component.onDragLeave(createDragEvent('dragleave'));
      expect(component['dragActive']()).toBe(true);

      component.onDragLeave(createDragEvent('dragleave'));
      expect(component['dragActive']()).toBe(false);
    });

    it('should upload files on drop', () => {
      const file = new File(['content'], 'test.pdf');
      const event = new Event('drop', { bubbles: true }) as DragEvent;
      const fileList = Object.assign([file], { item: (i: number) => [file][i] ?? null });
      Object.defineProperty(event, 'dataTransfer', {
        value: { types: ['Files'], files: fileList },
      });
      Object.defineProperty(event, 'preventDefault', { value: vi.fn() });

      component.onDrop(event);

      expect(component['dragActive']()).toBe(false);
      expect(mockUploadManager.addFiles).toHaveBeenCalledWith([file]);
    });

    it('should not upload files on drop for read-only users', () => {
      canWrite.set(false);
      const event = createDragEvent('drop');

      component.onDrop(event);

      expect(mockUploadManager.addFiles).not.toHaveBeenCalled();
    });
  });

  describe('header actions registration', () => {
    it('should re-register actions on NavigationEnd', async () => {
      const setSpy = vi.spyOn(pageHeaderService, 'setActions');
      setSpy.mockClear();

      const router = TestBed.inject(Router);
      await router.navigateByUrl('/documents');

      expect(setSpy).toHaveBeenCalled();
    });
  });
});
