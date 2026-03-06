import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUpload } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { AuthService } from '@/shared/services/auth.service';
import { UploadManagerService } from '@/shared/services/upload-manager.service';

@Component({
  selector: 'app-documents-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideUpload })],
  host: {
    class: 'flex min-h-0 flex-1 flex-col relative',
    '(dragenter)': 'onDragEnter($event)',
    '(dragleave)': 'onDragLeave($event)',
    '(dragover)': 'onDragOver($event)',
    '(drop)': 'onDrop($event)',
  },
  template: `
    <router-outlet />
    @if (dragActive() && auth.canWrite()) {
      <div class="absolute inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm border-2 border-dashed border-primary rounded-lg">
        <div class="flex flex-col items-center gap-2 text-primary">
          <ng-icon hlm name="lucideUpload" size="xl" />
          <p class="text-lg font-medium">Drop files to upload</p>
          <p class="text-sm text-muted-foreground">Files will be uploaded and processed automatically</p>
        </div>
      </div>
    }
  `,
})
export class DocumentsLayoutComponent {
  protected auth = inject(AuthService);
  private uploadManager = inject(UploadManagerService);

  protected dragActive = signal(false);
  private dragCounter = 0;

  onDragEnter(event: DragEvent): void {
    event.preventDefault();
    if (!this.auth.canWrite()) return;
    if (!event.dataTransfer?.types.includes('Files')) return;
    this.dragCounter++;
    this.dragActive.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragCounter--;
    if (this.dragCounter <= 0) {
      this.dragCounter = 0;
      this.dragActive.set(false);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragCounter = 0;
    this.dragActive.set(false);

    if (!this.auth.canWrite()) return;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.uploadManager.addFiles(Array.from(files));
    }
  }
}
