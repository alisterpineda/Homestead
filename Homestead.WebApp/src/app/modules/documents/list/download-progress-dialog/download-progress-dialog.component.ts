import { Component, ChangeDetectionStrategy, signal, viewChild } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleAlert, lucideDownload } from '@ng-icons/lucide';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';

type DownloadStatus = 'idle' | 'in-progress' | 'completed' | 'failed';

const DEFAULT_ERROR = 'Download failed. Please try again.';

@Component({
  selector: 'app-download-progress-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmDialogImports,
    HlmButton,
    HlmSpinner,
  ],
  providers: [provideIcons({ lucideCircleCheck, lucideCircleAlert, lucideDownload })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)" [disableClose]="status() === 'in-progress'">
      <button hlmBtn class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal [showCloseButton]="status() !== 'in-progress'">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Download Documents</h2>
        </div>

        <div class="flex w-72 flex-col items-center gap-4 self-center py-6">
          @switch (status()) {
            @case ('in-progress') {
              <hlm-spinner />
              <p class="text-center text-sm text-muted-foreground">
                Preparing your download&hellip; Please do not refresh the page.
              </p>
            }
            @case ('completed') {
              <ng-icon name="lucideCircleCheck" class="text-[length:--spacing(12)] text-primary" />
              <p class="text-center text-sm text-foreground">Your download is ready.</p>
              <a hlmBtn [href]="downloadUrl()" aria-label="Download documents">
                <ng-icon name="lucideDownload" class="text-[length:--spacing(4)]" />
                Download
              </a>
            }
            @case ('failed') {
              <ng-icon name="lucideCircleAlert" class="text-[length:--spacing(12)] text-destructive" />
              <p class="text-center text-sm text-destructive">
                {{ errorMessage() }}
              </p>
            }
          }
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class DownloadProgressDialogComponent {
  private dialog = viewChild(BrnDialog);

  status = signal<DownloadStatus>('idle');
  errorMessage = signal<string>(DEFAULT_ERROR);
  downloadUrl = signal<string>('');

  startDownload(downloadUrl: string): void {
    this.status.set('in-progress');
    this.downloadUrl.set(downloadUrl);
    this.dialog()?.open();
  }

  markCompleted(): void {
    this.status.set('completed');
  }

  markFailed(error: string | null): void {
    this.status.set('failed');
    this.errorMessage.set(error ?? DEFAULT_ERROR);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'closed') {
      this.status.set('idle');
      this.errorMessage.set(DEFAULT_ERROR);
    }
  }
}
