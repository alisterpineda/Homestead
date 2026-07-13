import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild, computed } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { DocumentApiService } from '@/api/services/document-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-bulk-reprocess-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmIconImports,
    NgIcon,
  ],
  providers: [provideIcons({ lucideRefreshCw })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" hlmDialogTrigger [disabled]="disabled()">
        <ng-icon hlm name="lucideRefreshCw" size="sm" />
        Reprocess
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Reprocess Text Extraction</h2>
          <p hlmDialogDescription>
            This will re-extract text from the latest version of {{ documentCount() }}
            selected document{{ documentCount() > 1 ? 's' : '' }}. The existing extracted text will be replaced.
          </p>
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" hlmDialogClose>Cancel</button>
          <button hlmBtn (click)="handleReprocess(ctx)" [disabled]="reprocessing()">
            {{ reprocessing() ? 'Reprocessing...' : 'Reprocess ' + documentCount() + ' document' + (documentCount() > 1 ? 's' : '') }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class BulkReprocessDialogComponent {
  documentIds = input.required<Set<string>>();
  disabled = input(false);
  reprocessStarted = output<string[]>();

  reprocessing = signal(false);

  documentCount = computed(() => this.documentIds().size);

  private dialog = viewChild(BrnDialog);
  private documentApi = inject(DocumentApiService);
  private errorHandler = inject(ErrorHandlerService);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.reprocessing.set(false);
    }
  }

  handleReprocess(ctx: { close: () => void }): void {
    const ids = Array.from(this.documentIds());
    if (ids.length === 0) return;

    this.reprocessing.set(true);

    this.documentApi.bulkReprocessDocuments({
      body: { ids },
    }).pipe(
      handleMutationError(this.errorHandler, this.reprocessing, 'Failed to start text extraction reprocessing.'),
    ).subscribe({
      next: (r) => {
        this.reprocessing.set(false);
        ctx.close();
        this.reprocessStarted.emit(r.jobIds);
      },
    });
  }
}
