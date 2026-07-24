import { Injectable, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { firstValueFrom, Subject } from 'rxjs';
import { filter, map, take } from 'rxjs/operators';
import { toast } from '@spartan-ng/brain/sonner';
import { JobFailedError } from '@/shared/types/job-failed-error';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';

export interface JobStatusChangedMessage {
  jobId: string;
  status: ProcessingJobStatus;
  entityType: string | null;
  entityId: string | null;
  documentName: string | null;
  error: string | null;
  errorCode: string | null;
  errorData: Record<string, string> | null;
  totalChildCount: number | null;
  completedChildCount: number | null;
  failedChildCount: number | null;
}

/**
 * Whether a job opts out of the generic toasts below.
 *
 * Those toasts are document-shaped by construction — they name a `documentName` and fall back to the
 * literal "Document". A backup run has no document: its parent would read as
 * `"Document" has been processed successfully.`, and its batch children would dump a multi-line
 * per-file failure report into a toast. The admin backup page reports its own runs (it claims the
 * parent job and renders batch errors on the source row), so the whole entity type opts out.
 * Claiming them individually is not possible — the client never learns the children's job ids.
 */
export function ownsItsOwnReporting(message: JobStatusChangedMessage): boolean {
  return message.entityType === 'BackupSource';
}

@Injectable({ providedIn: 'root' })
export class ProcessingHubService {
  private connection: HubConnection | null = null;
  private managedJobIds = new Set<string>();

  private jobCompletedSubject = new Subject<JobStatusChangedMessage>();
  readonly jobCompleted$ = this.jobCompletedSubject.asObservable();

  private jobStatusChangedSubject = new Subject<JobStatusChangedMessage>();
  readonly jobStatusChanged$ = this.jobStatusChangedSubject.asObservable();

  readonly connected = signal(false);

  registerManagedJob(jobId: string): void {
    this.managedJobIds.add(jobId);
  }

  waitForJob(jobId: string): Promise<JobStatusChangedMessage> {
    this.registerManagedJob(jobId);
    return firstValueFrom(
      this.jobStatusChanged$.pipe(
        filter(msg => msg.jobId === jobId),
        // Every TERMINAL status, Cancelled included: a caller awaiting a job that was cancelled out from
        // under it (the backup-source delete flow) must be told, not left with a promise that never
        // settles and a subscription that never unsubscribes.
        filter(msg =>
          msg.status === 'Completed' ||
          msg.status === 'Failed' ||
          msg.status === 'PartiallyCompleted' ||
          msg.status === 'Cancelled'),
        take(1),
        map(msg => {
          if (msg.status === 'Failed') {
            throw new JobFailedError(
              msg.error ?? 'An unknown error occurred.',
              msg.errorCode,
              msg.errorData,
            );
          }
          return msg;
        }),
      ),
    );
  }

  async connect(): Promise<void> {
    if (this.connection) return;

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/processing')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('JobStatusChanged', (message: JobStatusChangedMessage) => {
      this.jobStatusChangedSubject.next(message);

      const isManaged = ownsItsOwnReporting(message) || this.managedJobIds.has(message.jobId);

      if (message.status === 'Completed') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const name = message.documentName ?? 'Document';
          toast.success(`"${name}" has been processed successfully.`);
        }
        this.jobCompletedSubject.next(message);
      } else if (message.status === 'PartiallyCompleted') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const failed = message.failedChildCount ?? 0;
          toast.warning(`Processing completed with ${failed} failed item${failed !== 1 ? 's' : ''}.`);
        }
        this.jobCompletedSubject.next(message);
      } else if (message.status === 'Failed') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const errorMsg = message.error ?? 'An unknown error occurred.';
          toast.error(`Processing failed: ${errorMsg}`);
        }
      } else if (message.status === 'Cancelled') {
        // Terminal, but never worth announcing: cancellation is always something the operator just did,
        // so they already know. Release the claim so managedJobIds doesn't grow for the session.
        this.managedJobIds.delete(message.jobId);
      }
    });

    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));

    try {
      await this.connection.start();
      this.connected.set(true);
    } catch (err) {
      console.error('SignalR connection failed:', err);
    }
  }
}
