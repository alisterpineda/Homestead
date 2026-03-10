import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { toast } from 'ngx-sonner';
import { UploadManagerService } from './upload-manager.service';
import { ProcessingHubService } from './processing-hub.service';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { Subject } from 'rxjs';
import type { JobStatusChangedMessage } from './processing-hub.service';

function createFile(name: string, size = 1024): File {
  return new File([new ArrayBuffer(size)], name, { type: 'application/pdf' });
}

describe('UploadManagerService', () => {
  let service: UploadManagerService;
  let httpTesting: HttpTestingController;
  let jobStatusChanged$: Subject<JobStatusChangedMessage>;

  beforeEach(() => {
    vi.spyOn(toast, 'message').mockImplementation(() => undefined as never);
    vi.spyOn(toast, 'success').mockImplementation(() => undefined as never);
    vi.spyOn(toast, 'warning').mockImplementation(() => undefined as never);
    vi.spyOn(toast, 'error').mockImplementation(() => undefined as never);
    jobStatusChanged$ = new Subject<JobStatusChangedMessage>();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ProcessingHubService,
          useValue: {
            jobStatusChanged$: jobStatusChanged$.asObservable(),
            registerManagedJob: vi.fn(),
          },
        },
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });

    service = TestBed.inject(UploadManagerService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    service.clearAll();
    // Discard any remaining HTTP requests left by the test
    httpTesting.match(() => true);
    vi.restoreAllMocks();
  });

  /** Flush all pending upload requests with a jobId response */
  function flushAll(): void {
    httpTesting.match('/api/v1/documents').forEach((r, i) => r.flush({ jobId: `job-${i}` }));
  }

  describe('addFiles', () => {
    it('should add files as queued items and start uploading', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);

      expect(service.items().length).toBe(2);
      expect(service.items()[0].fileName).toBe('a.pdf');
      expect(service.items()[1].fileName).toBe('b.pdf');
      expect(service.hasItems()).toBe(true);
    });

    it('should assign unique ids to each item', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);

      const ids = service.items().map(i => i.id);
      expect(new Set(ids).size).toBe(2);
    });

    it('should start uploading up to 3 files immediately', () => {
      service.addFiles([
        createFile('a.pdf'),
        createFile('b.pdf'),
        createFile('c.pdf'),
        createFile('d.pdf'),
      ]);

      const uploading = service.items().filter(i => i.status === 'uploading');
      const queued = service.items().filter(i => i.status === 'queued');
      expect(uploading.length).toBe(3);
      expect(queued.length).toBe(1);
      expect(queued[0].fileName).toBe('d.pdf');
    });
  });

  describe('concurrency', () => {
    it('should start queued items when a slot opens', () => {
      service.addFiles([
        createFile('a.pdf'),
        createFile('b.pdf'),
        createFile('c.pdf'),
        createFile('d.pdf'),
      ]);

      const requests = httpTesting.match('/api/v1/documents');
      expect(requests.length).toBe(3);

      // Complete first upload — should start the queued item
      requests[0].flush({ jobId: 'job-a' });

      const newRequests = httpTesting.match('/api/v1/documents');
      expect(newRequests.length).toBe(1);

      const dItem = service.items().find(i => i.fileName === 'd.pdf');
      expect(dItem?.status).toBe('uploading');

      // Clean up remaining
      requests[1].flush({ jobId: 'job-b' });
      requests[2].flush({ jobId: 'job-c' });
      newRequests[0].flush({ jobId: 'job-d' });
    });
  });

  describe('upload success with jobId', () => {
    it('should transition to processing and register managed job', () => {
      const hub = TestBed.inject(ProcessingHubService);
      service.addFiles([createFile('test.pdf')]);

      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'job-123' });

      const item = service.items()[0];
      expect(item.status).toBe('processing');
      expect(item.progress).toBe(100);
      expect(item.jobId).toBe('job-123');
      expect(hub.registerManagedJob).toHaveBeenCalledWith('job-123');
    });
  });

  describe('upload success without jobId', () => {
    it('should transition directly to completed', () => {
      service.addFiles([createFile('test.pdf')]);

      httpTesting.expectOne('/api/v1/documents').flush({});

      const item = service.items()[0];
      expect(item.status).toBe('completed');
      expect(item.progress).toBe(100);
    });
  });

  describe('upload failure', () => {
    it('should transition to failed with error message', () => {
      service.addFiles([createFile('test.pdf')]);

      httpTesting.expectOne('/api/v1/documents').flush(null, { status: 500, statusText: 'Internal Server Error' });

      const item = service.items()[0];
      expect(item.status).toBe('failed');
      expect(item.error).toBeTruthy();
    });

    it('should show network error for status 0', () => {
      service.addFiles([createFile('test.pdf')]);

      httpTesting.expectOne('/api/v1/documents').error(new ProgressEvent('error'));

      const item = service.items()[0];
      expect(item.status).toBe('failed');
      expect(item.error).toBe('Network error');
    });

    it('should start next queued item after failure', () => {
      service.addFiles([
        createFile('a.pdf'),
        createFile('b.pdf'),
        createFile('c.pdf'),
        createFile('d.pdf'),
      ]);

      const requests = httpTesting.match('/api/v1/documents');
      requests[0].flush(null, { status: 500, statusText: 'Error' });

      // d.pdf should now be uploading
      const dItem = service.items().find(i => i.fileName === 'd.pdf');
      expect(dItem?.status).toBe('uploading');
    });
  });

  describe('job status changes via hub', () => {
    it('should mark item as completed when job completes', () => {
      service.addFiles([createFile('test.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'job-1' });

      expect(service.items()[0].status).toBe('processing');

      jobStatusChanged$.next({
        jobId: 'job-1',
        status: 'Completed',
        entityType: null,
        entityId: null,
        documentName: 'My Document',
        error: null,
      });

      const item = service.items()[0];
      expect(item.status).toBe('completed');
      expect(item.documentName).toBe('My Document');
    });

    it('should mark item as failed when job fails', () => {
      service.addFiles([createFile('test.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'job-1' });

      jobStatusChanged$.next({
        jobId: 'job-1',
        status: 'Failed',
        entityType: null,
        entityId: null,
        documentName: null,
        error: 'OCR failed',
      });

      const item = service.items()[0];
      expect(item.status).toBe('failed');
      expect(item.error).toBe('OCR failed');
    });

    it('should ignore status changes for unknown job ids', () => {
      service.addFiles([createFile('test.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'job-1' });

      jobStatusChanged$.next({
        jobId: 'unknown-job',
        status: 'Completed',
        entityType: null,
        entityId: null,
        documentName: null,
        error: null,
      });

      expect(service.items()[0].status).toBe('processing');
    });
  });

  describe('retryItem', () => {
    it('should requeue a failed item', () => {
      service.addFiles([createFile('test.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush(null, { status: 500, statusText: 'Error' });

      expect(service.items()[0].status).toBe('failed');

      service.retryItem(service.items()[0].id);

      expect(service.items()[0].status).toBe('uploading');
      expect(service.items()[0].error).toBeNull();
    });

    it('should not retry non-failed items', () => {
      service.addFiles([createFile('test.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'j1' });

      const id = service.items()[0].id;
      expect(service.items()[0].status).toBe('processing');

      service.retryItem(id);
      expect(service.items()[0].status).toBe('processing');
    });
  });

  describe('removeItem', () => {
    it('should remove an item from the list', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);
      flushAll();

      const id = service.items()[0].id;
      service.removeItem(id);
      expect(service.items().length).toBe(1);
    });
  });

  describe('clearFinished', () => {
    it('should remove completed and failed items but keep active ones', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf'), createFile('c.pdf')]);
      const requests = httpTesting.match('/api/v1/documents');

      // a -> completed, b -> failed, c -> processing (active)
      requests[0].flush({});
      requests[1].flush(null, { status: 500, statusText: 'Error' });
      requests[2].flush({ jobId: 'job-c' });

      expect(service.items().length).toBe(3);

      service.clearFinished();

      expect(service.items().length).toBe(1);
      expect(service.items()[0].fileName).toBe('c.pdf');
      expect(service.items()[0].status).toBe('processing');
    });

    it('should do nothing when there are no finished items', () => {
      service.addFiles([createFile('a.pdf')]);
      // a is uploading (active)
      expect(service.items().length).toBe(1);

      service.clearFinished();

      expect(service.items().length).toBe(1);
    });
  });

  describe('clearAll', () => {
    it('should remove all items', () => {
      service.addFiles([createFile('a.pdf')]);
      service.clearAll();

      expect(service.items().length).toBe(0);
      expect(service.hasItems()).toBe(false);
    });
  });

  describe('toast notifications', () => {
    it('should show info toast when files are added (single file)', () => {
      service.addFiles([createFile('a.pdf')]);

      expect(toast.message).toHaveBeenCalledWith('Uploading "a.pdf"...');
    });

    it('should show info toast when files are added (multiple files)', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);

      expect(toast.message).toHaveBeenCalledWith('Uploading 2 files...');
    });

    it('should show success toast when batch completes', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);

      const requests = httpTesting.match('/api/v1/documents');
      requests[0].flush({});
      requests[1].flush({});

      expect(toast.success).toHaveBeenCalledWith('2 files uploaded successfully.');
    });

    it('should show success toast for single file completion', () => {
      service.addFiles([createFile('a.pdf')]);

      httpTesting.expectOne('/api/v1/documents').flush({});

      expect(toast.success).toHaveBeenCalledWith('1 file uploaded successfully.');
    });

    it('should show error toast for each failed upload', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);

      const requests = httpTesting.match('/api/v1/documents');
      requests[0].flush(null, { status: 500, statusText: 'Internal Server Error' });
      requests[1].flush({});

      expect(toast.error).toHaveBeenCalledOnce();
      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('a.pdf'));
      expect(toast.warning).toHaveBeenCalledWith('1 file uploaded. 1 failed.');
    });

    it('should show separate completion toasts for separate batches', () => {
      service.addFiles([createFile('a.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({});

      expect(toast.success).toHaveBeenCalledOnce();
      vi.mocked(toast.message).mockClear();
      vi.mocked(toast.success).mockClear();
      vi.mocked(toast.error).mockClear();

      service.addFiles([createFile('b.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({});

      expect(toast.success).toHaveBeenCalledOnce();
    });

    it('should show error toast when processing job fails', () => {
      service.addFiles([createFile('a.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'job-1' });

      jobStatusChanged$.next({
        jobId: 'job-1',
        status: 'Failed',
        entityType: null,
        entityId: null,
        documentName: null,
        error: 'OCR failed',
      });

      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('a.pdf'));
      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('OCR failed'));
    });

    it('should show success toast when processing job completes (batch done)', () => {
      service.addFiles([createFile('a.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({ jobId: 'job-1' });

      jobStatusChanged$.next({
        jobId: 'job-1',
        status: 'Completed',
        entityType: null,
        entityId: null,
        documentName: 'My Document',
        error: null,
      });

      expect(toast.success).toHaveBeenCalledWith('1 file uploaded successfully.');
    });

    it('should show error batch toast if all items failed', () => {
      service.addFiles([createFile('a.pdf')]);

      httpTesting.expectOne('/api/v1/documents').flush(null, { status: 500, statusText: 'Error' });

      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('a.pdf'));
      expect(toast.error).toHaveBeenCalledWith('Upload failed.');
      expect(toast.success).not.toHaveBeenCalled();
    });

    it('should show error batch toast with count for multiple failures', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf')]);

      const requests = httpTesting.match('/api/v1/documents');
      requests[0].flush(null, { status: 500, statusText: 'Error' });
      requests[1].flush(null, { status: 500, statusText: 'Error' });

      expect(toast.error).toHaveBeenCalledWith('2 uploads failed.');
      expect(toast.success).not.toHaveBeenCalled();
    });
  });

  describe('computed signals', () => {
    it('should reflect correct counts', () => {
      service.addFiles([createFile('a.pdf'), createFile('b.pdf'), createFile('c.pdf')]);
      const requests = httpTesting.match('/api/v1/documents');

      requests[0].flush({});
      requests[1].flush(null, { status: 500, statusText: 'Error' });
      requests[2].flush({ jobId: 'j3' });

      expect(service.completedCount()).toBe(1);
      expect(service.failedCount()).toBe(1);
      expect(service.hasActiveUploads()).toBe(true); // j3 is processing
      expect(service.hasItems()).toBe(true);
    });

    it('should report no active uploads when all done', () => {
      service.addFiles([createFile('a.pdf')]);
      httpTesting.expectOne('/api/v1/documents').flush({});

      expect(service.hasActiveUploads()).toBe(false);
    });
  });
});
