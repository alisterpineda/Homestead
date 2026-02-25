import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { UploadDropzoneComponent } from '../../components/upload-dropzone/upload-dropzone.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-document-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButton,
    HlmCardImports,
    HlmInput,
    HlmLabel,
    UploadDropzoneComponent,
  ],
  template: `
    <div>
      <!-- Page Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-bold tracking-tight text-foreground">Upload Document</h1>
        <p class="text-sm text-muted-foreground">Upload a new document to the system</p>
      </div>

      <hlm-card class="md:max-w-xl">
        <div hlmCardContent class="pt-6">
          <form (submit)="handleSubmit($event)" class="flex flex-col gap-5">
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="doc-name">Document Name (optional)</label>
              <input
                hlmInput
                id="doc-name"
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
            <div class="flex gap-3">
              <button hlmBtn type="submit" [disabled]="!file() || uploading()">
                {{ uploading() ? 'Uploading...' : 'Upload' }}
              </button>
              <button hlmBtn variant="secondary" type="button" (click)="router.navigate(['/documents'])">
                Cancel
              </button>
            </div>
          </form>
        </div>
      </hlm-card>
    </div>
  `,
})
export class DocumentUploadComponent {
  name = signal('');
  file = signal<File | null>(null);
  uploading = signal(false);
  uploadError = signal(false);

  router = inject(Router);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private processingHub = inject(ProcessingHubService);

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
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
          this.router.navigate(['/documents']);
        },
        error: () => {
          this.uploading.set(false);
          this.uploadError.set(true);
          toast.error('Upload failed. Please try again.');
        },
      });
  }
}
