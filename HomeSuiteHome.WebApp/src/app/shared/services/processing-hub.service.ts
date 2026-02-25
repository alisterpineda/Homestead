import { Injectable, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { toast } from 'ngx-sonner';

export interface JobStatusChangedMessage {
  jobId: string;
  status: string;
  entityType: string | null;
  entityId: string | null;
  documentName: string | null;
  error: string | null;
}

@Injectable({ providedIn: 'root' })
export class ProcessingHubService {
  private connection: HubConnection | null = null;
  private trackedToasts = new Map<string, string | number>();

  private jobCompletedSubject = new Subject<JobStatusChangedMessage>();
  readonly jobCompleted$ = this.jobCompletedSubject.asObservable();

  readonly connected = signal(false);

  /** Register a loading toast to be updated when the job completes or fails. */
  trackJob(jobId: string, toastId: string | number): void {
    this.trackedToasts.set(jobId, toastId);
  }

  async connect(): Promise<void> {
    if (this.connection) return;

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/processing')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('JobStatusChanged', (message: JobStatusChangedMessage) => {
      const trackedToastId = this.trackedToasts.get(message.jobId);

      if (message.status === 'Completed') {
        const name = message.documentName ?? 'Document';
        if (trackedToastId !== undefined) {
          toast.success(`"${name}" has been processed successfully.`, { id: trackedToastId });
          this.trackedToasts.delete(message.jobId);
        } else {
          toast.success(`"${name}" has been processed successfully.`);
        }
        this.jobCompletedSubject.next(message);
      } else if (message.status === 'Failed') {
        const errorMsg = message.error ?? 'An unknown error occurred.';
        if (trackedToastId !== undefined) {
          toast.error(`Processing failed: ${errorMsg}`, { id: trackedToastId });
          this.trackedToasts.delete(message.jobId);
        } else {
          toast.error(`Processing failed: ${errorMsg}`);
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
