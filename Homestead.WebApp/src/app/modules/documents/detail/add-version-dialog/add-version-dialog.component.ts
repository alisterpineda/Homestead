import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { UploadDropzoneComponent } from '../../list/upload-dropzone/upload-dropzone.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { toast } from 'ngx-sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-add-version-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    UploadDropzoneComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Add Version</h2>
          <p hlmDialogDescription>Upload a new version of this document.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="version-label">Label (optional)</label>
            <input
              hlmInput
              id="version-label"
              placeholder="e.g. Signed copy, Updated terms"
              [value]="label()"
              (input)="label.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>File</label>
            <app-upload-dropzone [file]="file()" (fileSelect)="file.set($event)" />
          </div>
          @if (uploadError()) {
            <p class="text-sm text-destructive">Upload failed. Please try again.</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!file() || uploading()">
              {{ uploading() ? 'Uploading...' : 'Upload' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class AddVersionDialogComponent {
  documentId = input.required<string>();
  versionCreated = output();

  label = signal('');
  file = signal<File | null>(null);
  uploading = signal(false);
  uploadError = signal(false);

  private dialog = viewChild(BrnDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private processingHub = inject(ProcessingHubService);
  private errorHandler = inject(ErrorHandlerService);

  open(): void {
    this.dialog()?.open();
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.label.set('');
      this.file.set(null);
      this.uploading.set(false);
      this.uploadError.set(false);
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const f = this.file();
    if (!f) return;

    this.uploading.set(true);
    this.uploadError.set(false);

    const formData = new FormData();
    formData.append('file', f);
    const labelVal = this.label();
    if (labelVal) {
      formData.append('label', labelVal);
    }
    const docId = this.documentId();
    this.http
      .post<{ jobId: string }>(
        `${this.apiConfig.rootUrl}/api/v1/documents/${docId}/versions`,
        formData,
      )
      .subscribe({
        next: (response) => {
          this.uploading.set(false);
          toast.promise(this.processingHub.waitForJob(response.jobId), {
            loading: 'Version uploaded. Processing...',
            success: 'Version has been processed successfully.',
            error: (err: unknown) => `Processing failed: ${err instanceof Error ? err.message : 'An unknown error occurred.'}`,
          });
          this.dialog()?.close();
          this.versionCreated.emit();
        },
        error: () => {
          this.uploading.set(false);
          this.uploadError.set(true);
        },
      });
  }
}
