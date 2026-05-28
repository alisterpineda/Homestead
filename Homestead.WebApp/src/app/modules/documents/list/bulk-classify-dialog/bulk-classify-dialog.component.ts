import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild, computed } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles } from '@ng-icons/lucide';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { AiProviderSummary } from '@/api/generated/models/ai-provider-summary';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-bulk-classify-dialog',
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
  providers: [provideIcons({ lucideSparkles })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" size="sm" hlmDialogTrigger [disabled]="disabled()">
        <ng-icon hlm name="lucideSparkles" size="sm" />
        Classify
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>AI Classification</h2>
          <p hlmDialogDescription>
            This will classify {{ documentCount() }}
            selected document{{ documentCount() > 1 ? 's' : '' }} using AI. Suggestions for name, tags, and parties will be generated.
          </p>
        </div>

        <div class="flex flex-col gap-4 py-4">
          @if (loadingProviders()) {
            <div class="flex justify-center py-2">
              <hlm-spinner />
            </div>
          } @else if (loadError()) {
            <p class="text-sm text-muted-foreground">
              Could not load AI providers. You can still classify with the system default.
            </p>
          } @else {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel>AI Provider</label>
              <hlm-select [value]="selectedProviderId() ?? ''" (valueChange)="onProviderChange($any($event))" [itemToString]="providerToString">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content *hlmSelectPortal class="max-h-60">
<hlm-select-group>

                  <hlm-select-item value="">Default (system setting)</hlm-select-item>
                  @for (provider of enabledProviders(); track provider.id) {
                    <hlm-select-item [value]="provider.id">{{ provider.name }}</hlm-select-item>
                  }

                </hlm-select-group>
</hlm-select-content>
              </hlm-select>
            </div>
          }
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" hlmDialogClose>Cancel</button>
          <button hlmBtn (click)="handleClassify(ctx)" [disabled]="classifying()">
            {{ classifying() ? 'Classifying...' : 'Classify ' + documentCount() + ' document' + (documentCount() > 1 ? 's' : '') }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class BulkClassifyDialogComponent {
  documentIds = input.required<Set<string>>();
  disabled = input(false);
  classifyStarted = output<string[]>();

  providers = signal<AiProviderSummary[]>([]);
  selectedProviderId = signal<string | null>(null);
  loadingProviders = signal(false);
  loadError = signal(false);
  classifying = signal(false);

  documentCount = computed(() => this.documentIds().size);
  enabledProviders = computed(() => this.providers().filter(p => p.isEnabled));

  private dialog = viewChild(BrnDialog);
  private documentApi = inject(DocumentApiService);
  private errorHandler = inject(ErrorHandlerService);

  providerToString = (id: string): string => {
    if (id === '') return 'Default (system setting)';
    return this.providers().find(p => p.id === id)?.name ?? id;
  };

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.selectedProviderId.set(null);
      this.classifying.set(false);
      this.loadError.set(false);
      this.loadProviders();
    }
  }

  onProviderChange(value: string): void {
    this.selectedProviderId.set(value === '' ? null : value);
  }

  handleClassify(ctx: { close: () => void }): void {
    const ids = Array.from(this.documentIds());
    if (ids.length === 0) return;

    this.classifying.set(true);
    const aiProviderId = this.selectedProviderId();

    this.documentApi.bulkClassifyDocuments({
      body: { ids, aiProviderId },
    }).pipe(
      handleMutationError(this.errorHandler, this.classifying, 'Failed to start AI classification.'),
    ).subscribe({
      next: (r) => {
        this.classifying.set(false);
        ctx.close();
        this.classifyStarted.emit(r.jobIds);
      },
    });
  }

  private loadProviders(): void {
    this.loadingProviders.set(true);
    this.loadError.set(false);

    this.documentApi.getAiProviderSummaries().subscribe({
      next: (r) => {
        this.providers.set(r);
        this.loadingProviders.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loadingProviders.set(false);
      },
    });
  }
}
