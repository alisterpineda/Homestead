import { Injectable, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { toast } from 'ngx-sonner';
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
}

@Injectable({ providedIn: 'root' })
export class ProcessingHubService {
  private connection: HubConnection | null = null;
  private trackedToasts = new Map<string, string | number>();
  private managedJobIds = new Set<string>();

  private jobCompletedSubject = new Subject<JobStatusChangedMessage>();
  readonly jobCompleted$ = this.jobCompletedSubject.asObservable();

  private jobStatusChangedSubject = new Subject<JobStatusChangedMessage>();
  readonly jobStatusChanged$ = this.jobStatusChangedSubject.asObservable();

  readonly connected = signal(false);

  trackJob(jobId: string, toastId: string | number): void {
    this.trackedToasts.set(jobId, toastId);
  }

  registerManagedJob(jobId: string): void {
    this.managedJobIds.add(jobId);
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

      const isManaged = this.managedJobIds.has(message.jobId);
      const trackedToastId = this.trackedToasts.get(message.jobId);

      if (message.status === 'Completed') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const name = message.documentName ?? 'Document';
          if (trackedToastId !== undefined) {
            toast.success(`"${name}" has been processed successfully.`, { id: trackedToastId });
            this.trackedToasts.delete(message.jobId);
          } else {
            toast.success(`"${name}" has been processed successfully.`);
          }
        }
        this.jobCompletedSubject.next(message);
      } else if (message.status === 'Failed') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const errorMsg = message.error ?? 'An unknown error occurred.';
          if (trackedToastId !== undefined) {
            toast.error(`Processing failed: ${errorMsg}`, { id: trackedToastId });
            this.trackedToasts.delete(message.jobId);
          } else {
            toast.error(`Processing failed: ${errorMsg}`);
          }
        }
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
