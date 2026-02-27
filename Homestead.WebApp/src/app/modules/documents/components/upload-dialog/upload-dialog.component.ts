import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { UploadDropzoneComponent } from '../upload-dropzone/upload-dropzone.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-upload-dialog',
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
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Upload Document</h2>
          <p hlmDialogDescription>Select a file to upload and optionally give it a name.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="upload-doc-name">Document Name (optional)</label>
            <input
              hlmInput
              id="upload-doc-name"
              placeholder="Enter a name for this document"
              [value]="name()"
              (input)="name.set(asInputValue($event))"
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
export class UploadDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  uploaded = output();

  name = signal('');
  file = signal<File | null>(null);
  uploading = signal(false);
  uploadError = signal(false);

  private dialog = viewChild(BrnDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private processingHub = inject(ProcessingHubService);

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
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
    const nameVal = this.name();
    if (nameVal) {
      formData.append('name', nameVal);
    }

    this.http
      .post<{ jobId: string }>(`${this.apiConfig.rootUrl}/api/v1/documents`, formData)
      .subscribe({
        next: (response) => {
          this.uploading.set(false);
          const toastId = toast.loading('Document uploaded. Processing...');
          this.processingHub.trackJob(response.jobId, toastId);
          this.dialog()?.close();
          this.uploaded.emit();
        },
        error: () => {
          this.uploading.set(false);
          this.uploadError.set(true);
          toast.error('Upload failed. Please try again.');
        },
      });
  }
}
