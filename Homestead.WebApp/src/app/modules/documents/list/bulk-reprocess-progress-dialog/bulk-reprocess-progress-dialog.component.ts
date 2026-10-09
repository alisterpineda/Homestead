import { Component, ChangeDetectionStrategy, signal, viewChild, computed } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleAlert, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmProgressImports } from '@/shared/components/spartan/progress';
import { HlmSpinner } from '@/shared/components/spartan/spinner';

type ReprocessStatus = 'idle' | 'in-progress' | 'completed' | 'partially-completed' | 'failed';

@Component({
  selector: 'app-bulk-reprocess-progress-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmDialogImports,
    HlmProgressImports,
    HlmSpinner,
  ],
  providers: [provideIcons({ lucideCircleCheck, lucideCircleAlert, lucideTriangleAlert })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)" [disableClose]="status() === 'in-progress'">
      <button hlmBtn class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal [showCloseButton]="status() !== 'in-progress'">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Reprocess Text Extraction</h2>
        </div>

        <div class="flex w-72 flex-col items-center gap-4 self-center py-6">
          @switch (status()) {
            @case ('in-progress') {
              <hlm-spinner />
              <p class="text-muted-foreground text-center text-sm">
                Reprocessing text extraction&hellip;
              </p>
              <hlm-progress class="w-full" [value]="progressPercent()" [max]="100">
                <hlm-progress-indicator />
              </hlm-progress>
              <p class="text-muted-foreground text-center text-xs">
                {{ progressText() }}
              </p>
            }
            @case ('completed') {
              <ng-icon name="lucideCircleCheck" class="text-[length:--spacing(12)] text-primary" />
              <p class="text-foreground text-center text-sm">{{ resultMessage() }}</p>
            }
            @case ('partially-completed') {
              <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(12)] text-yellow-500" />
              <p class="text-foreground text-center text-sm">{{ resultMessage() }}</p>
            }
            @case ('failed') {
              <ng-icon name="lucideCircleAlert" class="text-[length:--spacing(12)] text-destructive" />
              <p class="text-destructive text-center text-sm">{{ resultMessage() }}</p>
            }
          }
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class BulkReprocessProgressDialogComponent {
  private dialog = viewChild(BrnDialog);

  readonly status = signal<ReprocessStatus>('idle');
  readonly total = signal(0);
  readonly completed = signal(0);
  readonly failed = signal(0);

  readonly progressPercent = computed(() => {
    const t = this.total();
    if (t === 0) return 0;
    return Math.round(((this.completed() + this.failed()) / t) * 100);
  });

  readonly progressText = computed(() => {
    const t = this.total();
    const done = this.completed() + this.failed();
    return `${done} of ${t} processed`;
  });

  readonly resultMessage = computed(() => {
    const t = this.total();
    const f = this.failed();
    const s = t - f;

    if (f === 0) {
      return `Text extraction completed for ${s} document${s !== 1 ? 's' : ''}.`;
    }
    if (s === 0) {
      return `Text extraction failed for all ${f} document${f !== 1 ? 's' : ''}.`;
    }
    return `Text extraction completed for ${s} of ${t} document${t !== 1 ? 's' : ''} (${f} failed).`;
  });

  start(documentCount: number): void {
    this.status.set('in-progress');
    this.total.set(documentCount);
    this.completed.set(0);
    this.failed.set(0);
    this.dialog()?.open();
  }

  updateProgress(completedCount: number, failedCount: number, totalCount: number): void {
    this.total.set(totalCount);
    this.completed.set(completedCount);
    this.failed.set(failedCount);
  }

  markCompleted(): void {
    this.status.set('completed');
  }

  markPartiallyCompleted(): void {
    this.status.set('partially-completed');
  }

  markFailed(): void {
    this.status.set('failed');
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'closed') {
      this.status.set('idle');
    }
  }
}
