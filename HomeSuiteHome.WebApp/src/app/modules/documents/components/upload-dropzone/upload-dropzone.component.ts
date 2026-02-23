import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  viewChild,
  ElementRef,
} from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { Upload } from 'lucide-angular';
import { cn, formatBytes } from '@/shared/utils';

@Component({
  selector: 'app-upload-dropzone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule],
  template: `
    <div
      (click)="onClick()"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
      [class]="dropzoneClasses()"
    >
      <input #fileInput type="file" class="hidden" (change)="onFileChange($event)" />
      <lucide-icon
        [img]="uploadIcon"
        [size]="32"
        [class]="'mb-3 ' + (file() ? 'text-primary' : 'text-muted-foreground')"
      />
      @if (file()) {
        <div class="text-center">
          <p class="text-sm font-medium text-foreground">{{ file()!.name }}</p>
          <p class="mt-1 text-xs text-muted-foreground">{{ formatBytes(file()!.size) }}</p>
        </div>
      } @else {
        <div class="text-center">
          <p class="text-sm font-medium text-foreground">Drop a file here, or click to browse</p>
          <p class="mt-1 text-xs text-muted-foreground">Any file type accepted</p>
        </div>
      }
    </div>
  `,
})
export class UploadDropzoneComponent {
  file = input<File | null>(null);
  fileSelect = output<File>();

  uploadIcon = Upload;
  formatBytes = formatBytes;

  dragOver = signal(false);
  fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  dropzoneClasses(): string {
    const f = this.file();
    const drag = this.dragOver();
    return cn(
      'flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed p-6 transition-colors md:p-8',
      drag
        ? 'border-primary bg-accent'
        : f
          ? 'border-primary bg-accent'
          : 'border-border hover:border-primary hover:bg-muted',
    );
  }

  onClick(): void {
    this.fileInput()?.nativeElement.click();
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    const dropped = event.dataTransfer?.files[0];
    if (dropped) this.fileSelect.emit(dropped);
  }

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const selected = input.files?.[0];
    if (selected) this.fileSelect.emit(selected);
  }
}
