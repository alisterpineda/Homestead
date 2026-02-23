import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { PageHeaderComponent } from '@/shared/components/ui/page-header/page-header.component';
import { CardComponent } from '@/shared/components/ui/card/card.component';
import { InputComponent } from '@/shared/components/ui/input/input.component';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';
import { UploadDropzoneComponent } from '../../components/upload-dropzone/upload-dropzone.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { createDocument } from '@/api/generated/fn/documents/create-document';

@Component({
  selector: 'app-document-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageHeaderComponent,
    CardComponent,
    InputComponent,
    ButtonComponent,
    UploadDropzoneComponent,
  ],
  template: `
    <div>
      <app-page-header title="Upload Document" description="Upload a new document to the system" />
      <app-card className="md:max-w-xl">
        <form (submit)="handleSubmit($event)" class="flex flex-col gap-5">
          <app-input
            inputId="name"
            label="Document Name (optional)"
            placeholder="Enter a name for this document"
            [value]="name()"
            (valueChange)="name.set($event)"
          />
          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium text-foreground">File</label>
            <app-upload-dropzone [file]="file()" (fileSelect)="file.set($event)" />
          </div>
          @if (uploadError()) {
            <p class="text-sm text-destructive">Upload failed. Please try again.</p>
          }
          <div class="flex gap-3">
            <app-button type="submit" [disabled]="!file() || uploading()">
              {{ uploading() ? 'Uploading...' : 'Upload' }}
            </app-button>
            <app-button type="button" variant="secondary" (click)="router.navigate(['/documents'])">
              Cancel
            </app-button>
          </div>
        </form>
      </app-card>
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

    // Use HttpClient directly for FormData upload since the generated function
    // expects an IFormFile type which doesn't map well for browser FormData
    this.http.post(`${this.apiConfig.rootUrl}/api/v1/documents`, formData).subscribe({
      next: () => {
        this.uploading.set(false);
        this.router.navigate(['/documents']);
      },
      error: () => {
        this.uploading.set(false);
        this.uploadError.set(true);
      },
    });
  }
}
