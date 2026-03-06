import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideActivity, lucideCheck, lucideAlertCircle, lucideLoader, lucideX, lucideRotateCw } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmButton } from '@/shared/components/spartan/button';
import { UploadManagerService } from '@/shared/services/upload-manager.service';
import { formatBytes } from '@/shared/utils';

@Component({
  selector: 'app-activity-popover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmButton],
  providers: [provideIcons({ lucideActivity, lucideCheck, lucideAlertCircle, lucideLoader, lucideX, lucideRotateCw })],
  template: `
      <div hlmPopover align="end">
        <button hlmPopoverTrigger hlmBtn variant="ghost" size="icon-sm" class="relative" aria-label="Upload activity">
          <ng-icon hlm name="lucideActivity" size="sm" />
          @if (uploadManager.hasActiveUploads()) {
            <span class="absolute -right-0.5 -top-0.5 flex h-2.5 w-2.5">
              <span class="absolute inline-flex h-full w-full animate-ping rounded-full bg-primary opacity-75"></span>
              <span class="relative inline-flex h-2.5 w-2.5 rounded-full bg-primary"></span>
            </span>
          } @else if (uploadManager.failedCount() > 0) {
            <span class="absolute -right-0.5 -top-0.5 flex h-2.5 w-2.5">
              <span class="relative inline-flex h-2.5 w-2.5 rounded-full bg-destructive"></span>
            </span>
          }
        </button>
        <div hlmPopoverContent *hlmPopoverPortal class="w-80 p-0">
          <!-- Header -->
          <div class="flex items-center justify-between border-b border-border px-3 py-2">
            <span class="text-sm font-medium">{{ statusText() }}</span>
            <button hlmBtn variant="ghost" size="icon-sm" (click)="uploadManager.clearAll()" aria-label="Clear all">
              <ng-icon hlm name="lucideX" size="xs" />
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
                  }
                </div>

                <!-- Actions -->
                <div class="mt-0.5 shrink-0">
                  @if (item.status === 'failed') {
                    <button hlmBtn variant="ghost" size="icon-sm" (click)="uploadManager.retryItem(item.id)" aria-label="Retry upload">
                      <ng-icon hlm name="lucideRotateCw" size="xs" />
                    </button>
                  }
                  @if (item.status === 'completed' || item.status === 'failed') {
                    <button hlmBtn variant="ghost" size="icon-sm" (click)="uploadManager.removeItem(item.id)" aria-label="Remove">
                      <ng-icon hlm name="lucideX" size="xs" />
                    </button>
                  }
                </div>
              </div>
            }
          </div>

          <!-- Footer -->
          @if (uploadManager.completedCount() > 0) {
            <div class="border-t border-border px-3 py-2">
              <button hlmBtn variant="ghost" size="sm" class="w-full" (click)="uploadManager.clearCompleted()">
                Clear completed
              </button>
            </div>
          }
        </div>
      </div>
  `,
})
export class ActivityPopoverComponent {
  protected uploadManager = inject(UploadManagerService);
  protected formatBytes = formatBytes;

  protected statusText = computed(() => {
    const items = this.uploadManager.items();
    const active = items.filter(i => i.status === 'uploading' || i.status === 'queued' || i.status === 'processing');
    const failed = this.uploadManager.failedCount();
    const completed = this.uploadManager.completedCount();

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
}
