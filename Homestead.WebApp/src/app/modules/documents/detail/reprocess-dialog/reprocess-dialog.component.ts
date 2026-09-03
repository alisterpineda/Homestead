import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { persistedSignal } from '@/shared/utils/persisted-signal';
import { EngineSelectComponent } from '@/modules/documents/shared/engine-select/engine-select.component';

@Component({
  selector: 'app-reprocess-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmLabel,
    EngineSelectComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Reprocess Text Extraction</h2>
          <p hlmDialogDescription>
            This will re-extract text from the current version's file. The existing extracted text will be replaced.
          </p>
        </div>

        <div class="flex flex-col gap-2 py-4">
          <label hlmLabel>Text extraction engine</label>
          <app-engine-select [value]="engineKey()" (valueChange)="engineKey.set($event)" />
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" hlmDialogClose>Cancel</button>
          <button hlmBtn (click)="handleReprocess(ctx)" [disabled]="reprocessing()">
            {{ reprocessing() ? 'Reprocessing...' : 'Reprocess' }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ReprocessDialogComponent {
  documentId = input.required<string>();
  versionId = input.required<string>();
  reprocessStarted = output<string>();

  reprocessing = signal(false);

  // Sticky across surfaces and sessions, like the classify field selection: comparing engines means
  // reprocessing the same way several times in a row.
  engineKey = persistedSignal<string>({
    key: 'homestead-reprocess-engine-key',
    defaultValue: '',
    validate: (v): v is string => typeof v === 'string',
  });

  private dialog = viewChild(BrnDialog);
  private versionApi = inject(DocumentVersionApiService);
  private errorHandler = inject(ErrorHandlerService);

  open(): void {
    this.dialog()?.open();
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.reprocessing.set(false);
    }
  }

  handleReprocess(ctx: { close: () => void }): void {
    const docId = this.documentId();
    const verId = this.versionId();
    if (!docId || !verId) return;

    this.reprocessing.set(true);

    this.versionApi.reprocessDocumentVersion({
      documentId: docId,
      versionId: verId,
      // Empty means no explicit pick, which the backend resolves to the configured default.
      body: { engineKey: this.engineKey() || null },
    }).pipe(
      handleMutationError(this.errorHandler, this.reprocessing, 'Failed to start text extraction reprocessing.'),
    ).subscribe({
      next: (r) => {
        this.reprocessing.set(false);
        ctx.close();
        this.reprocessStarted.emit(r.jobId);
      },
    });
  }
}
