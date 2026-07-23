import { TestBed } from '@angular/core/testing';
import { ProcessingHubService, JobStatusChangedMessage, ownsItsOwnReporting } from './processing-hub.service';

describe('ProcessingHubService', () => {
  let service: ProcessingHubService;
  let signalRHandler: (message: JobStatusChangedMessage) => void;

  beforeEach(async () => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ProcessingHubService);

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (service as any).connection = null;

    signalRHandler = (message: JobStatusChangedMessage) => {
      // Replicate what connect() does in its on('JobStatusChanged') callback
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const svc = service as any;
      svc.jobStatusChangedSubject.next(message);

      const isManaged = svc.managedJobIds.has(message.jobId);

      if (message.status === 'Completed') {
        if (isManaged) {
          svc.managedJobIds.delete(message.jobId);
        }
        svc.jobCompletedSubject.next(message);
      } else if (message.status === 'Failed') {
        if (isManaged) {
          svc.managedJobIds.delete(message.jobId);
        }
      }
    };
  });

  function msg(overrides: Partial<JobStatusChangedMessage> = {}): JobStatusChangedMessage {
    return {
      jobId: 'job-1',
      status: 'Completed',
      entityType: null,
      entityId: null,
      documentName: 'Test Doc',
      error: null,
      errorCode: null,
      errorData: null,
      totalChildCount: null,
      completedChildCount: null,
      failedChildCount: null,
      ...overrides,
    };
  }

  describe('generic-toast opt-out', () => {
    // The generic toasts are worded for document uploads. A backup job carries no document name, so
    // the parent would announce `"Document" has been processed successfully.` and a failed batch would
    // dump its whole per-file report into a toast. /admin/backup reports its own runs instead.
    it('opts a backup job out of the generic toasts', () => {
      expect(ownsItsOwnReporting(msg({ entityType: 'BackupTarget' }))).toBe(true);
    });

    it('opts out backup jobs regardless of status', () => {
      for (const status of ['Completed', 'PartiallyCompleted', 'Failed'] as const) {
        expect(ownsItsOwnReporting(msg({ entityType: 'BackupTarget', status }))).toBe(true);
      }
    });

    it('leaves document and download jobs on the generic path', () => {
      expect(ownsItsOwnReporting(msg({ entityType: 'Document' }))).toBe(false);
      expect(ownsItsOwnReporting(msg({ entityType: 'Download' }))).toBe(false);
      expect(ownsItsOwnReporting(msg({ entityType: null }))).toBe(false);
    });
  });

  describe('managed jobs', () => {
    it('should suppress managed job from being tracked as toast', () => {
      service.registerManagedJob('job-1');
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).managedJobIds.has('job-1')).toBe(true);
    });

    it('should clean up managed job id after completion', () => {
      service.registerManagedJob('job-1');
      signalRHandler(msg({ status: 'Completed' }));
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).managedJobIds.has('job-1')).toBe(false);
    });

    it('should clean up managed job id after failure', () => {
      service.registerManagedJob('job-1');
      signalRHandler(msg({ status: 'Failed', error: 'Bad' }));
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).managedJobIds.has('job-1')).toBe(false);
    });

    it('should still emit jobCompleted$ for managed jobs', () => {
      service.registerManagedJob('job-1');
      const completions: JobStatusChangedMessage[] = [];
      service.jobCompleted$.subscribe(m => completions.push(m));

      signalRHandler(msg({ status: 'Completed' }));
      expect(completions.length).toBe(1);
      expect(completions[0].jobId).toBe('job-1');
    });

    it('should not emit jobCompleted$ for failed jobs', () => {
      const completions: JobStatusChangedMessage[] = [];
      service.jobCompleted$.subscribe(m => completions.push(m));

      signalRHandler(msg({ status: 'Failed', error: 'Error' }));
      expect(completions.length).toBe(0);
    });
  });

  describe('jobStatusChanged$', () => {
    it('should emit for all messages regardless of managed state', () => {
      service.registerManagedJob('job-1');
      const messages: JobStatusChangedMessage[] = [];
      service.jobStatusChanged$.subscribe(m => messages.push(m));

      signalRHandler(msg({ status: 'Completed' }));
      signalRHandler(msg({ jobId: 'job-2', status: 'Failed', error: 'Error' }));

      expect(messages.length).toBe(2);
    });
  });

  describe('waitForJob', () => {
    it('should resolve with the message when the job completes', async () => {
      const promise = service.waitForJob('job-1');
      signalRHandler(msg({ status: 'Completed' }));
      const result = await promise;
      expect(result.jobId).toBe('job-1');
      expect(result.status).toBe('Completed');
    });

    it('should reject with an error when the job fails', async () => {
      const promise = service.waitForJob('job-1');
      signalRHandler(msg({ status: 'Failed', error: 'OCR engine crashed' }));
      await expect(promise).rejects.toThrow('OCR engine crashed');
    });

    it('should reject with a default message when error is null', async () => {
      const promise = service.waitForJob('job-1');
      signalRHandler(msg({ status: 'Failed', error: null }));
      await expect(promise).rejects.toThrow('An unknown error occurred.');
    });

    it('should register the job as managed', () => {
      service.waitForJob('job-1');
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).managedJobIds.has('job-1')).toBe(true);
    });

    it('should ignore status messages for other jobs', async () => {
      const promise = service.waitForJob('job-1');
      signalRHandler(msg({ jobId: 'job-2', status: 'Completed' }));

      const settled = await Promise.race([
        promise.then(() => 'resolved'),
        new Promise<string>(r => setTimeout(() => r('pending'), 50)),
      ]);
      expect(settled).toBe('pending');
    });
  });
});
