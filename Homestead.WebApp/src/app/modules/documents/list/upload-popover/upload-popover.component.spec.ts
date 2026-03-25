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
    duplicateDocumentId: null,
    duplicateDocumentName: null,
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
      duplicateCount: computed(() => mockItems().filter(i => i.status === 'duplicate').length),
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

  describe('triggerBadge', () => {
    it('should return null when no items', () => {
      expect(component['triggerBadge']()).toBeNull();
    });

    it('should return "active" when uploads are in progress', () => {
      mockItems.set([createItem({ status: 'uploading' })]);
      fixture.detectChanges();

      expect(component['triggerBadge']()).toBe('active');
    });

    it('should return "active" when queued items exist', () => {
      mockItems.set([createItem({ status: 'queued' })]);
      fixture.detectChanges();

      expect(component['triggerBadge']()).toBe('active');
    });

    it('should return "active" over other states when uploads are active', () => {
      mockItems.set([
        createItem({ id: '1', status: 'uploading' }),
        createItem({ id: '2', status: 'completed' }),
        createItem({ id: '3', status: 'failed', error: 'err' }),
      ]);
      fixture.detectChanges();

      expect(component['triggerBadge']()).toBe('active');
    });

    it('should return "completed" when all finished uploads succeeded', () => {
      mockItems.set([
        createItem({ id: '1', status: 'completed' }),
        createItem({ id: '2', status: 'completed' }),
      ]);
      fixture.detectChanges();

      expect(component['triggerBadge']()).toBe('completed');
    });

    it('should return "mixed" when there are both completed and failed', () => {
      mockItems.set([
        createItem({ id: '1', status: 'completed' }),
        createItem({ id: '2', status: 'failed', error: 'err' }),
      ]);
      fixture.detectChanges();

      expect(component['triggerBadge']()).toBe('mixed');
    });

    it('should return "failed" when all finished uploads failed', () => {
      mockItems.set([
        createItem({ id: '1', status: 'failed', error: 'err' }),
        createItem({ id: '2', status: 'failed', error: 'err' }),
      ]);
      fixture.detectChanges();

      expect(component['triggerBadge']()).toBe('failed');
    });

    it('should return null after clearing finished items', () => {
      mockItems.set([createItem({ status: 'completed' })]);
      fixture.detectChanges();
      expect(component['triggerBadge']()).toBe('completed');

      mockItems.set([]);
      fixture.detectChanges();
      expect(component['triggerBadge']()).toBeNull();
    });
  });

  describe('trigger badge DOM', () => {
    function getTriggerButton(): HTMLElement {
      return fixture.nativeElement.querySelector('button');
    }

    function getBadgeDot(): HTMLElement | null {
      return getTriggerButton().querySelector('span.rounded-full');
    }

    function getScreenReaderText(): string | null {
      const sr = getTriggerButton().querySelector('.sr-only');
      return sr?.textContent?.trim() ?? null;
    }

    it('should show no badge dot when no items', () => {
      expect(getBadgeDot()).toBeNull();
      expect(getScreenReaderText()).toBeNull();
    });

    it('should show pinging primary dot when active', () => {
      mockItems.set([createItem({ status: 'uploading' })]);
      fixture.detectChanges();

      const container = getTriggerButton().querySelector('.animate-ping');
      expect(container).toBeTruthy();
      expect(getScreenReaderText()).toBe('Uploads in progress');
    });

    it('should show green dot when all completed', () => {
      mockItems.set([createItem({ status: 'completed' })]);
      fixture.detectChanges();

      const dot = getBadgeDot();
      expect(dot).toBeTruthy();
      expect(dot!.classList.contains('bg-green-600')).toBe(true);
      expect(getScreenReaderText()).toBe('All uploads completed');
    });

    it('should show amber dot when mixed', () => {
      mockItems.set([
        createItem({ id: '1', status: 'completed' }),
        createItem({ id: '2', status: 'failed', error: 'err' }),
      ]);
      fixture.detectChanges();

      const dot = getBadgeDot();
      expect(dot).toBeTruthy();
      expect(dot!.classList.contains('bg-amber-500')).toBe(true);
      expect(getScreenReaderText()).toBe('Some uploads failed');
    });

    it('should show destructive dot when all failed', () => {
      mockItems.set([createItem({ status: 'failed', error: 'err' })]);
      fixture.detectChanges();

      const dot = getBadgeDot();
      expect(dot).toBeTruthy();
      expect(dot!.classList.contains('bg-destructive')).toBe(true);
      expect(getScreenReaderText()).toBe('All uploads failed');
    });

    it('should remove badge dot after items are cleared', () => {
      mockItems.set([createItem({ status: 'completed' })]);
      fixture.detectChanges();
      expect(getBadgeDot()).toBeTruthy();

      mockItems.set([]);
      fixture.detectChanges();
      expect(getBadgeDot()).toBeNull();
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
