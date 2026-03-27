import { Injectable, computed, signal, inject, DestroyRef } from '@angular/core';
import { HttpClient, HttpRequest, HttpEventType, HttpResponse, HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { toast } from 'ngx-sonner';
import { extractApiError } from '@/shared/utils/api-error.utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';

export type UploadItemStatus = 'queued' | 'uploading' | 'processing' | 'completed' | 'failed' | 'duplicate';

export interface UploadItem {
  id: string;
  file: File;
  fileName: string;
  fileSize: number;
  status: UploadItemStatus;
  progress: number;
  jobId: string | null;
  documentName: string | null;
  error: string | null;
  duplicateDocumentId: string | null;
  duplicateDocumentName: string | null;
  duplicateIsTrashed: boolean;
}

const MAX_CONCURRENT = 3;

@Injectable({ providedIn: 'root' })
export class UploadManagerService {
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private processingHub = inject(ProcessingHubService);
  private destroyRef = inject(DestroyRef);

  readonly items = signal<UploadItem[]>([]);
  private activeUploads = new Map<string, Subscription>();
  private idCounter = 0;
  private batchCounter = 0;
  private batches = new Map<number, Set<string>>();

  readonly hasItems = computed(() => this.items().length > 0);
  readonly hasActiveUploads = computed(() =>
    this.items().some(i => i.status === 'uploading' || i.status === 'queued' || i.status === 'processing'),
  );
  readonly completedCount = computed(() => this.items().filter(i => i.status === 'completed').length);
  readonly failedCount = computed(() => this.items().filter(i => i.status === 'failed').length);
  readonly duplicateCount = computed(() => this.items().filter(i => i.status === 'duplicate').length);

  private hubSub: Subscription;

  constructor() {
    this.hubSub = this.processingHub.jobStatusChanged$.subscribe(msg => {
      this.handleJobStatusChanged(msg.jobId, msg.status, msg.documentName, msg.error, msg.errorCode, msg.errorData);
    });
    this.destroyRef.onDestroy(() => this.hubSub.unsubscribe());

    if (typeof window !== 'undefined') {
      const handler = (e: BeforeUnloadEvent) => {
        if (this.hasActiveUploads()) {
          e.preventDefault();
        }
      };
      window.addEventListener('beforeunload', handler);
      this.destroyRef.onDestroy(() => window.removeEventListener('beforeunload', handler));
    }
  }

  addFiles(files: File[]): void {
    const newItems: UploadItem[] = files.map(file => ({
      id: `upload-${++this.idCounter}`,
      file,
      fileName: file.name,
      fileSize: file.size,
      status: 'queued' as const,
      progress: 0,
      jobId: null,
      documentName: null,
      error: null,
      duplicateDocumentId: null,
      duplicateDocumentName: null,
      duplicateIsTrashed: false,
    }));

    this.items.update(prev => [...prev, ...newItems]);

    const count = files.length;
    toast.message(count === 1 ? `Uploading "${files[0].name}"...` : `Uploading ${count} files...`);

    const batchId = ++this.batchCounter;
    this.batches.set(batchId, new Set(newItems.map(i => i.id)));

    this.processQueue();
  }

  retryItem(id: string): void {
    this.items.update(prev =>
      prev.map(item =>
        item.id === id && item.status === 'failed'
          ? { ...item, status: 'queued' as const, progress: 0, error: null, jobId: null }
          : item,
      ),
    );
    this.processQueue();
  }

  removeItem(id: string): void {
    const item = this.items().find(i => i.id === id);
    if (item && this.activeUploads.has(id)) {
      this.activeUploads.get(id)!.unsubscribe();
      this.activeUploads.delete(id);
    }
    this.items.update(prev => prev.filter(i => i.id !== id));
    this.checkBatchesComplete();
  }

  /** Clear completed, failed, and duplicate items, keeping in-progress and queued ones. */
  clearFinished(): void {
    this.items.update(prev => prev.filter(i => i.status !== 'completed' && i.status !== 'failed' && i.status !== 'duplicate'));
  }

  clearAll(): void {
    for (const [id, sub] of this.activeUploads) {
      sub.unsubscribe();
    }
    this.activeUploads.clear();
    this.batches.clear();
    this.items.set([]);
  }

  private processQueue(): void {
    const currentItems = this.items();
    const activeCount = currentItems.filter(i => i.status === 'uploading').length;
    const slotsAvailable = MAX_CONCURRENT - activeCount;

    if (slotsAvailable <= 0) return;

    const queued = currentItems.filter(i => i.status === 'queued');
    const toStart = queued.slice(0, slotsAvailable);

    for (const item of toStart) {
      this.uploadFile(item);
    }
  }

  private uploadFile(item: UploadItem): void {
    this.updateItem(item.id, { status: 'uploading', progress: 0 });

    const formData = new FormData();
    formData.append('file', item.file);

    const req = new HttpRequest(
      'POST',
      `${this.apiConfig.rootUrl}/api/v1/documents`,
      formData,
      { reportProgress: true },
    );

    const sub = this.http.request(req).subscribe({
      next: event => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          const progress = Math.round((100 * event.loaded) / event.total);
          this.updateItem(item.id, { progress });
        } else if (event instanceof HttpResponse) {
          const body = event.body as { jobId: string } | null;
          if (body?.jobId) {
            this.processingHub.registerManagedJob(body.jobId);
            this.updateItem(item.id, {
              status: 'processing',
              progress: 100,
              jobId: body.jobId,
            });
          } else {
            this.updateItem(item.id, { status: 'completed', progress: 100 });
          }
          this.activeUploads.delete(item.id);
          this.processQueue();
          this.checkBatchesComplete();
        }
      },
      error: (err: HttpErrorResponse) => {
        const errorMsg = err.status === 0
          ? 'Network error'
          : extractApiError(err, err.statusText ?? 'Upload failed');
        this.updateItem(item.id, { status: 'failed', error: errorMsg });
        toast.error(`Upload failed for "${item.fileName}": ${errorMsg}`);
        this.activeUploads.delete(item.id);
        this.processQueue();
        this.checkBatchesComplete();
      },
    });

    this.activeUploads.set(item.id, sub);
  }

  private handleJobStatusChanged(
    jobId: string,
    status: ProcessingJobStatus,
    documentName: string | null,
    error: string | null,
    errorCode: string | null,
    errorData: Record<string, string> | null,
  ): void {
    const currentItems = this.items();
    const item = currentItems.find(i => i.jobId === jobId);
    if (!item) return;

    if (status === 'Completed') {
      this.updateItem(item.id, {
        status: 'completed',
        documentName: documentName ?? item.fileName,
      });
    } else if (status === 'Failed') {
      if (errorCode === 'duplicate_content' && errorData) {
        const isTrashed = errorData['isTrashed'] === 'true';
        this.updateItem(item.id, {
          status: 'duplicate',
          duplicateDocumentId: errorData['documentId'] ?? null,
          duplicateDocumentName: errorData['documentName'] ?? null,
          duplicateIsTrashed: isTrashed,
        });
        const name = errorData['documentName'] ?? 'unknown';
        const suffix = isTrashed ? ' (in trash)' : '';
        toast.info(`"${item.fileName}" already exists as "${name}"${suffix}.`);
      } else {
        const errorMsg = error ?? 'Processing failed';
        this.updateItem(item.id, { status: 'failed', error: errorMsg });
        toast.error(`Processing failed for "${item.fileName}": ${errorMsg}`);
      }
    }

    this.checkBatchesComplete();
  }

  private checkBatchesComplete(): void {
    const currentItems = this.items();
    for (const [batchId, itemIds] of this.batches) {
      const batchItems = currentItems.filter(i => itemIds.has(i.id));
      let completed = 0;
      let failed = 0;
      let duplicates = 0;
      for (const i of batchItems) {
        if (i.status === 'completed') completed++;
        else if (i.status === 'failed') failed++;
        else if (i.status === 'duplicate') duplicates++;
      }
      if (completed + failed + duplicates < batchItems.length) continue;

      this.batches.delete(batchId);

      // Duplicates already have their own info toast — only summarize actual successes and failures
      if (completed > 0 && failed === 0) {
        toast.success(completed === 1 ? '1 file uploaded successfully.' : `${completed} files uploaded successfully.`);
      } else if (completed > 0 && failed > 0) {
        toast.warning(`${completed} file${completed > 1 ? 's' : ''} uploaded. ${failed} failed.`);
      } else if (failed > 0) {
        toast.error(failed === 1 ? 'Upload failed.' : `${failed} uploads failed.`);
      }
    }
  }

  private updateItem(id: string, changes: Partial<UploadItem>): void {
    this.items.update(prev =>
      prev.map(item => (item.id === id ? { ...item, ...changes } : item)),
    );
  }
}
