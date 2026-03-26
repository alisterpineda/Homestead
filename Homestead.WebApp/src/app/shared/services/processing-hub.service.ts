import { Injectable, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { firstValueFrom, Subject } from 'rxjs';
import { filter, map, take } from 'rxjs/operators';
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
        filter(msg => msg.status === 'Completed' || msg.status === 'Failed'),
        take(1),
        map(msg => {
          if (msg.status === 'Failed') {
            throw new Error(msg.error ?? 'An unknown error occurred.');
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

      const isManaged = this.managedJobIds.has(message.jobId);

      if (message.status === 'Completed') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const name = message.documentName ?? 'Document';
          toast.success(`"${name}" has been processed successfully.`);
        }
        this.jobCompletedSubject.next(message);
      } else if (message.status === 'Failed') {
        if (isManaged) {
          this.managedJobIds.delete(message.jobId);
        } else {
          const errorMsg = message.error ?? 'An unknown error occurred.';
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
