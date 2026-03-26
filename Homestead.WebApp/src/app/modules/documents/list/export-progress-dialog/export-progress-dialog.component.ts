import { Component, ChangeDetectionStrategy, signal, viewChild } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleAlert, lucideDownload } from '@ng-icons/lucide';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';

type ExportStatus = 'idle' | 'in-progress' | 'completed' | 'failed';

const DEFAULT_ERROR = 'Export failed. Please try again.';

@Component({
  selector: 'app-export-progress-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmDialogImports,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
  ],
  providers: [provideIcons({ lucideCircleCheck, lucideCircleAlert, lucideDownload })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)" [disableClose]="status() === 'in-progress'">
      <button hlmBtn class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal [showCloseButton]="status() !== 'in-progress'">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Export Documents</h2>
        </div>

        <div class="flex w-72 flex-col items-center gap-4 self-center py-6">
          @switch (status()) {
            @case ('in-progress') {
              <hlm-spinner />
              <p class="text-center text-sm text-muted-foreground">
                Preparing your export&hellip; Please do not refresh the page.
              </p>
            }
            @case ('completed') {
              <ng-icon hlm name="lucideCircleCheck" size="xl" class="text-primary" />
              <p class="text-center text-sm text-foreground">Your export is ready.</p>
              <a hlmBtn [href]="downloadUrl()" aria-label="Download export">
                <ng-icon hlm name="lucideDownload" size="sm" />
                Download Export
              </a>
            }
            @case ('failed') {
              <ng-icon hlm name="lucideCircleAlert" size="xl" class="text-destructive" />
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
export class ExportProgressDialogComponent {
  private dialog = viewChild(BrnDialog);

  status = signal<ExportStatus>('idle');
  errorMessage = signal<string>(DEFAULT_ERROR);
  downloadUrl = signal<string>('');

  startExport(downloadUrl: string): void {
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
