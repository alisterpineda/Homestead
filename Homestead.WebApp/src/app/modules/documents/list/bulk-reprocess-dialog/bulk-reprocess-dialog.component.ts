import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild, computed } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { DocumentApiService } from '@/api/services/document-api.service';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import type { TextExtractionEngineSummary } from '@/api/generated/models/text-extraction-engine-summary';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-bulk-reprocess-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmLabel,
    HlmSpinner,
    HlmIconImports,
    NgIcon,
        HlmSelectImports,
  ],
  providers: [provideIcons({ lucideRefreshCw })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" size="sm" hlmDialogTrigger [disabled]="disabled()">
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

        <div class="flex flex-col gap-4 py-4">
          @if (loadingEngines()) {
            <div class="flex justify-center py-2">
              <hlm-spinner />
            </div>
          } @else if (loadError()) {
            <p class="text-sm text-muted-foreground">
              Could not load engines. You can still reprocess with the system default.
            </p>
          } @else {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel>Text Extraction Engine</label>
              <hlm-select [value]="selectedEngineId() ?? ''" (valueChange)="onEngineChange($any($event))">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content *hlmSelectPortal class="max-h-60">
<hlm-select-group>
                  
                  <hlm-select-item value="">Default (system setting)</hlm-select-item>
                  @for (engine of engines(); track engine.id) {
                    <hlm-select-item [value]="engine.id">{{ engine.name }}</hlm-select-item>
                  }
                  
                </hlm-select-group>
</hlm-select-content>
              </hlm-select>
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

  engines = signal<TextExtractionEngineSummary[]>([]);
  selectedEngineId = signal<string | null>(null);
  loadingEngines = signal(false);
  loadError = signal(false);
  reprocessing = signal(false);

  documentCount = computed(() => this.documentIds().size);

  private dialog = viewChild(BrnDialog);
  private documentApi = inject(DocumentApiService);
  private versionApi = inject(DocumentVersionApiService);
  private errorHandler = inject(ErrorHandlerService);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.selectedEngineId.set(null);
      this.reprocessing.set(false);
      this.loadError.set(false);
      this.loadEngines();
    }
  }

  onEngineChange(value: string): void {
    this.selectedEngineId.set(value === '' ? null : value);
  }

  handleReprocess(ctx: { close: () => void }): void {
    const ids = Array.from(this.documentIds());
    if (ids.length === 0) return;

    this.reprocessing.set(true);
    const engineId = this.selectedEngineId();

    this.documentApi.bulkReprocessDocuments({
      body: { ids, textExtractionEngineId: engineId },
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

  private loadEngines(): void {
    this.loadingEngines.set(true);
    this.loadError.set(false);

    this.versionApi.getTextExtractionEngineSummaries().subscribe({
      next: (r) => {
        this.engines.set(r);
        this.loadingEngines.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loadingEngines.set(false);
      },
    });
  }
}
