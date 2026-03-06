import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal, computed } from '@angular/core';
import { UploadPopoverComponent } from './upload-popover.component';
import { UploadManagerService } from '@/shared/services/upload-manager.service';
import type { UploadItem } from '@/shared/services/upload-manager.service';

function createItem(overrides: Partial<UploadItem> = {}): UploadItem {
  return {
    id: 'upload-1',
    file: new File([], 'test.pdf'),
    fileName: 'test.pdf',
    fileSize: 1024,
    status: 'queued',
    progress: 0,
    jobId: null,
    documentName: null,
    error: null,
    ...overrides,
  };
}

describe('UploadPopoverComponent', () => {
  let fixture: ComponentFixture<UploadPopoverComponent>;
  let component: UploadPopoverComponent;
  let mockItems: ReturnType<typeof signal<UploadItem[]>>;
  let mockUploadManager: Partial<UploadManagerService>;

  beforeEach(() => {
    mockItems = signal<UploadItem[]>([]);

    mockUploadManager = {
      items: mockItems,
      hasItems: computed(() => mockItems().length > 0),
      hasActiveUploads: computed(() => mockItems().some(i => i.status === 'uploading' || i.status === 'queued' || i.status === 'processing')),
      completedCount: computed(() => mockItems().filter(i => i.status === 'completed').length),
      failedCount: computed(() => mockItems().filter(i => i.status === 'failed').length),
      addFiles: vi.fn(),
      clearFinished: vi.fn(),
      retryItem: vi.fn(),
      removeItem: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: UploadManagerService, useValue: mockUploadManager },
      ],
    });

    fixture = TestBed.createComponent(UploadPopoverComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render Upload trigger button', () => {
    const button = fixture.nativeElement.querySelector('button');
    expect(button.textContent).toContain('Upload');
  });

  describe('statusText', () => {
    it('should show uploading count when files are active', () => {
      mockItems.set([
        createItem({ id: '1', status: 'uploading' }),
        createItem({ id: '2', status: 'queued' }),
      ]);
      fixture.detectChanges();

      expect(component['statusText']()).toBe('Uploading 2 files...');
    });

    it('should show singular when one file active', () => {
      mockItems.set([createItem({ status: 'uploading' })]);
      fixture.detectChanges();

      expect(component['statusText']()).toBe('Uploading 1 file...');
    });

    it('should show completed and failed counts', () => {
      mockItems.set([
        createItem({ id: '1', status: 'completed' }),
        createItem({ id: '2', status: 'failed', error: 'err' }),
      ]);
      fixture.detectChanges();

      expect(component['statusText']()).toBe('1 complete, 1 failed');
    });

    it('should show only failed count', () => {
      mockItems.set([createItem({ status: 'failed', error: 'err' })]);
      fixture.detectChanges();

      expect(component['statusText']()).toBe('1 upload failed');
    });

    it('should show only completed count', () => {
      mockItems.set([createItem({ status: 'completed' })]);
      fixture.detectChanges();

      expect(component['statusText']()).toBe('1 upload complete');
    });

    it('should pluralize completed count', () => {
      mockItems.set([
        createItem({ id: '1', status: 'completed' }),
        createItem({ id: '2', status: 'completed' }),
      ]);
      fixture.detectChanges();

      expect(component['statusText']()).toBe('2 uploads complete');
    });
  });

  describe('onFilesSelected', () => {
    it('should call addFiles and reset the input', () => {
      const file = new File(['content'], 'doc.pdf', { type: 'application/pdf' });
      const input = document.createElement('input');
      Object.defineProperty(input, 'files', { value: [file] });

      component['onFilesSelected']({ target: input } as unknown as Event);

      expect(mockUploadManager.addFiles).toHaveBeenCalledWith([file]);
      expect(input.value).toBe('');
    });

    it('should not call addFiles when no files selected', () => {
      const input = document.createElement('input');
      Object.defineProperty(input, 'files', { value: [] });

      component['onFilesSelected']({ target: input } as unknown as Event);

      expect(mockUploadManager.addFiles).not.toHaveBeenCalled();
    });
  });
});
