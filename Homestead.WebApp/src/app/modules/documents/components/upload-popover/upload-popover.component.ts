import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUpload, lucideCheck, lucideAlertCircle, lucideLoader, lucideX, lucideRotateCw, lucideListX, lucideInfo } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmButton } from '@/shared/components/spartan/button';
import { RouterLink } from '@angular/router';
import { UploadManagerService } from '@/shared/services/upload-manager.service';
import { formatBytes, handleFileInputChange } from '@/shared/utils';

@Component({
  selector: 'app-upload-popover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmButton, RouterLink],
  providers: [provideIcons({ lucideUpload, lucideCheck, lucideAlertCircle, lucideLoader, lucideX, lucideRotateCw, lucideListX, lucideInfo })],
  template: `
    <div hlmPopover align="end">
      <button hlmPopoverTrigger hlmBtn variant="ghost" size="sm" class="relative">
        <ng-icon hlm name="lucideUpload" size="sm" />
        <span class="hidden sm:inline">Upload</span>
        @switch (triggerBadge()) {
          @case ('active') {
            <span class="absolute -right-0.5 -top-0.5 flex h-2.5 w-2.5">
              <span class="absolute inline-flex h-full w-full animate-ping rounded-full bg-primary opacity-75"></span>
              <span class="relative inline-flex h-2.5 w-2.5 rounded-full bg-primary"></span>
            </span>
            <span class="sr-only">Uploads in progress</span>
          }
          @case ('completed') {
            <span class="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full bg-green-600 dark:bg-green-400"></span>
            <span class="sr-only">All uploads completed</span>
          }
          @case ('mixed') {
            <span class="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full bg-amber-500 dark:bg-amber-400"></span>
            <span class="sr-only">Some uploads failed</span>
          }
          @case ('failed') {
            <span class="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full bg-destructive"></span>
            <span class="sr-only">All uploads failed</span>
          }
        }
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-80 p-0">
        <input #fileInput type="file" multiple class="hidden" (change)="onFilesSelected($event)" />
        <div class="px-3 py-3" [class.border-b]="uploadManager.hasItems()" [class.border-border]="uploadManager.hasItems()">
          <button hlmBtn variant="outline" size="sm" class="w-full" (click)="fileInput.click()">
            <ng-icon hlm name="lucideUpload" size="sm" />
            Choose files
          </button>
          <p class="mt-1.5 text-center text-xs text-muted-foreground">Or drag and drop files anywhere on the page</p>
        </div>

        @if (uploadManager.hasItems()) {
          <!-- Header -->
          <div class="flex items-center justify-between border-b border-border px-3 py-2">
            <span class="text-sm font-medium">{{ statusText() }}</span>
            <button hlmBtn variant="ghost" size="icon-sm" (click)="uploadManager.clearFinished()" aria-label="Clear finished">
              <ng-icon hlm name="lucideListX" size="xs" />
            </button>
          </div>

          <!-- Item list -->
          <div class="max-h-60 overflow-y-auto">
            @for (item of uploadManager.items(); track item.id) {
              <div class="flex items-start gap-2 border-b border-border px-3 py-2 last:border-b-0">
                <!-- Status icon -->
                <div class="mt-0.5 shrink-0">
                  @switch (item.status) {
                    @case ('queued') {
                      <ng-icon hlm name="lucideLoader" size="sm" class="text-muted-foreground" />
                    }
                    @case ('uploading') {
                      <ng-icon hlm name="lucideLoader" size="sm" class="animate-spin text-primary" />
                    }
                    @case ('processing') {
                      <ng-icon hlm name="lucideLoader" size="sm" class="animate-spin text-primary" />
                    }
                    @case ('completed') {
                      <ng-icon hlm name="lucideCheck" size="sm" class="text-green-600 dark:text-green-400" />
                    }
                    @case ('failed') {
                      <ng-icon hlm name="lucideAlertCircle" size="sm" class="text-destructive" />
                    }
                    @case ('duplicate') {
                      <ng-icon hlm name="lucideInfo" size="sm" class="text-blue-600 dark:text-blue-400" />
                    }
                  }
                </div>

                <!-- Content -->
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm">{{ item.fileName }}</p>
                  @if (item.status === 'uploading') {
                    <div class="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-muted">
                      <div class="h-full rounded-full bg-primary transition-all" [style.width.%]="item.progress"></div>
                    </div>
                    <p class="mt-0.5 text-xs text-muted-foreground">{{ item.progress }}% &middot; {{ formatBytes(item.fileSize) }}</p>
                  } @else if (item.status === 'processing') {
                    <p class="text-xs text-muted-foreground">Processing...</p>
                  } @else if (item.status === 'queued') {
                    <p class="text-xs text-muted-foreground">Queued &middot; {{ formatBytes(item.fileSize) }}</p>
                  } @else if (item.status === 'completed') {
                    <p class="text-xs text-muted-foreground">{{ item.documentName ?? item.fileName }}</p>
                  } @else if (item.status === 'failed') {
                    <p class="text-xs text-destructive">{{ item.error }}</p>
                  } @else if (item.status === 'duplicate') {
                    <p class="text-xs text-muted-foreground">
                      Already exists as
                      @if (item.duplicateDocumentId) {
                        <a [routerLink]="['/documents', item.duplicateDocumentId]" class="font-medium text-blue-600 hover:underline dark:text-blue-400">{{ item.duplicateDocumentName ?? 'unknown' }}</a>
                      } @else {
                        <span class="font-medium">{{ item.duplicateDocumentName ?? 'unknown' }}</span>
                      }
                    </p>
                  }
                </div>

                <!-- Actions -->
                <div class="mt-0.5 shrink-0">
                  @if (item.status === 'failed') {
                    <button hlmBtn variant="ghost" size="icon-sm" (click)="uploadManager.retryItem(item.id)" aria-label="Retry upload">
                      <ng-icon hlm name="lucideRotateCw" size="xs" />
                    </button>
                  }
                  @if (item.status === 'completed' || item.status === 'failed' || item.status === 'duplicate') {
                    <button hlmBtn variant="ghost" size="icon-sm" (click)="uploadManager.removeItem(item.id)" aria-label="Remove">
                      <ng-icon hlm name="lucideX" size="xs" />
                    </button>
                  }
                </div>
              </div>
            }
          </div>

        }
      </div>
    </div>
  `,
})
export class UploadPopoverComponent {
  protected uploadManager = inject(UploadManagerService);
  protected formatBytes = formatBytes;

  private doneCount = computed(() => this.uploadManager.completedCount() + this.uploadManager.duplicateCount());

  protected triggerBadge = computed(() => {
    if (this.uploadManager.hasActiveUploads()) return 'active' as const;
    const completed = this.doneCount();
    const failed = this.uploadManager.failedCount();
    if (completed > 0 && failed === 0) return 'completed' as const;
    if (completed > 0 && failed > 0) return 'mixed' as const;
    if (failed > 0) return 'failed' as const;
    return null;
  });

  protected statusText = computed(() => {
    const items = this.uploadManager.items();
    const active = items.filter(i => i.status === 'uploading' || i.status === 'queued' || i.status === 'processing');
    const failed = this.uploadManager.failedCount();
    const completed = this.doneCount();

    if (active.length > 0) {
      return `Uploading ${active.length} file${active.length > 1 ? 's' : ''}...`;
    }
    if (failed > 0 && completed > 0) {
      return `${completed} complete, ${failed} failed`;
    }
    if (failed > 0) {
      return `${failed} upload${failed > 1 ? 's' : ''} failed`;
    }
    return `${completed} upload${completed > 1 ? 's' : ''} complete`;
  });

  protected onFilesSelected(event: Event): void {
    handleFileInputChange(event, files => this.uploadManager.addFiles(files));
  }
}
