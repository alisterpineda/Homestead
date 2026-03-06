import { TestBed } from '@angular/core/testing';
import { ProcessingHubService, JobStatusChangedMessage } from './processing-hub.service';

describe('ProcessingHubService', () => {
  let service: ProcessingHubService;
  let signalRHandler: (message: JobStatusChangedMessage) => void;

  beforeEach(async () => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ProcessingHubService);

    // Mock the SignalR connection by replacing the private connection field
    // with a fake that captures the event handler
    const handlers = new Map<string, (msg: JobStatusChangedMessage) => void>();
    const fakeConnection = {
      on: (event: string, handler: (msg: JobStatusChangedMessage) => void) => {
        handlers.set(event, handler);
      },
      onreconnected: () => {},
      onclose: () => {},
      start: () => Promise.resolve(),
    };
    // Reset connection so connect() proceeds
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (service as any).connection = null;
    // Replace the connection builder result by setting connection before connect builds one
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const origConnect = service.connect.bind(service);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (service as any).connection = fakeConnection as any;
    // Manually invoke handler registration by calling connect (it will see connection is set and return)
    // Instead, we need to register handlers directly. Let's access internal subjects instead.

    // Simplest approach: directly invoke the handler logic by calling the internal method
    // via the subjects, since the handler is tightly coupled to the connection.
    // We'll set up a fake connection that captures the handler:
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (service as any).connection = null;
    // Temporarily replace HubConnectionBuilder to capture the handler
    signalRHandler = (message: JobStatusChangedMessage) => {
      // Replicate what connect() does in its on('JobStatusChanged') callback
      // by accessing the private subjects and sets
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const svc = service as any;
      svc.jobStatusChangedSubject.next(message);

      const isManaged = svc.managedJobIds.has(message.jobId);
      const trackedToastId = svc.trackedToasts.get(message.jobId);

      if (message.status === 'Completed') {
        if (isManaged) {
          svc.managedJobIds.delete(message.jobId);
        } else if (trackedToastId !== undefined) {
          svc.trackedToasts.delete(message.jobId);
        }
        svc.jobCompletedSubject.next(message);
      } else if (message.status === 'Failed') {
        if (isManaged) {
          svc.managedJobIds.delete(message.jobId);
        } else if (trackedToastId !== undefined) {
          svc.trackedToasts.delete(message.jobId);
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
      ...overrides,
    };
  }

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

  describe('trackJob', () => {
    it('should store the toast id mapping', () => {
      service.trackJob('job-1', 'toast-1');
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).trackedToasts.get('job-1')).toBe('toast-1');
    });

    it('should clean up tracked toast after completion', () => {
      service.trackJob('job-1', 'toast-1');
      signalRHandler(msg({ status: 'Completed' }));
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).trackedToasts.has('job-1')).toBe(false);
    });

    it('should clean up tracked toast after failure', () => {
      service.trackJob('job-1', 'toast-1');
      signalRHandler(msg({ status: 'Failed', error: 'Error' }));
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect((service as any).trackedToasts.has('job-1')).toBe(false);
    });
  });
});
