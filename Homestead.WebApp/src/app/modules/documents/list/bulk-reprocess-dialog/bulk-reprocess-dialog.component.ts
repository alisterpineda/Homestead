import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild, computed } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideTriangleAlert } from '@ng-icons/lucide';
import { DocumentApiService } from '@/api/services/document-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { persistedSignal } from '@/shared/utils/persisted-signal';
import { EngineSelectComponent } from '@/modules/documents/shared/engine-select/engine-select.component';
import { TextExtractionEnginesService } from '@/modules/documents/shared/text-extraction-engines.service';

@Component({
  selector: 'app-bulk-reprocess-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmLabel,
    NgIcon,
    EngineSelectComponent,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideTriangleAlert })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" hlmDialogTrigger [disabled]="disabled()">
        <ng-icon name="lucideRefreshCw" class="text-[length:--spacing(4)]" />
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

        <div class="flex flex-col gap-2 py-4">
          <label hlmLabel>Text extraction engine</label>
          <app-engine-select [value]="engineKey()" (valueChange)="engineKey.set($event)" />

          @if (showCostWarning()) {
            <div class="mt-2 flex items-start gap-2 rounded-md border border-border bg-muted p-3 text-sm">
              <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(4)] mt-0.5 shrink-0 text-muted-foreground" />
              <span>
                <strong>{{ engineKey() }}</strong> is billed per page. This run sends
                {{ documentCount() }} document{{ documentCount() > 1 ? 's' : '' }} — every page of each — to
                the provider.
              </span>
            </div>
          }
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

  // Shares the single-document dialog's key on purpose: the engine you are evaluating is the same one
  // either way.
  engineKey = persistedSignal<string>({
    key: 'homestead-reprocess-engine-key',
    defaultValue: '',
    validate: (v): v is string => typeof v === 'string',
  });

  private enginesService = inject(TextExtractionEnginesService);

  // There is no page or document cap on a bulk reprocess, so a metered engine over a large selection can
  // spend real money on one click. Naming the count next to the engine is the cheap half of the mitigation.
  showCostWarning = computed(() => this.enginesService.isMetered(this.engineKey()));

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
      // Empty means no explicit pick, which the backend resolves to the configured default.
      body: { ids, engineKey: this.engineKey() || null },
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
