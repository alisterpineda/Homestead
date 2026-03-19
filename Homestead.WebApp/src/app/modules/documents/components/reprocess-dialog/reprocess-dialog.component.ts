import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTextExtractionEngineSummaries } from '@/api/generated/fn/documents/get-text-extraction-engine-summaries';
import { reprocessDocumentVersion } from '@/api/generated/fn/documents/reprocess-document-version';
import type { TextExtractionEngineSummary } from '@/api/generated/models/text-extraction-engine-summary';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-reprocess-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmLabel,
    HlmSpinner,
    BrnSelectImports,
    HlmSelectImports,
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
              <brn-select [value]="selectedEngineId() ?? ''" (valueChange)="onEngineChange($any($event))">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content class="max-h-60">
                  <hlm-select-scroll-up />
                  <hlm-option value="">Default (system setting)</hlm-option>
                  @for (engine of engines(); track engine.id) {
                    <hlm-option [value]="engine.id">{{ engine.name }}</hlm-option>
                  }
                  <hlm-select-scroll-down />
                </hlm-select-content>
              </brn-select>
            </div>
          }
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

  engines = signal<TextExtractionEngineSummary[]>([]);
  selectedEngineId = signal<string | null>(null);
  loadingEngines = signal(false);
  loadError = signal(false);
  reprocessing = signal(false);

  private dialog = viewChild(BrnDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  open(): void {
    this.dialog()?.open();
  }

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
    const docId = this.documentId();
    const verId = this.versionId();
    if (!docId || !verId) return;

    this.reprocessing.set(true);
    const engineId = this.selectedEngineId();

    reprocessDocumentVersion(this.http, this.apiConfig.rootUrl, {
      documentId: docId,
      versionId: verId,
      body: { textExtractionEngineId: engineId },
    }).subscribe({
      next: (r) => {
        this.reprocessing.set(false);
        ctx.close();
        this.reprocessStarted.emit(r.body.jobId);
      },
      error: () => {
        this.reprocessing.set(false);
        toast.error('Failed to start text extraction reprocessing.');
      },
    });
  }

  private loadEngines(): void {
    this.loadingEngines.set(true);
    this.loadError.set(false);

    getTextExtractionEngineSummaries(this.http, this.apiConfig.rootUrl).subscribe({
      next: (r) => {
        this.engines.set(r.body);
        this.loadingEngines.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loadingEngines.set(false);
      },
    });
  }
}
