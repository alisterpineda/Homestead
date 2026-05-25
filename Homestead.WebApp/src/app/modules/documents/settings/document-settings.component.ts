import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';

import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { DocumentApiService } from '@/api/services/document-api.service';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import type { TextExtractionEngineSummary } from '@/api/generated/models/text-extraction-engine-summary';
import type { AiProviderSummary } from '@/api/generated/models/ai-provider-summary';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

const BUILT_IN_TESSERACT_ID = '00000000-0000-0000-0000-000000000001';

@Component({
  selector: 'app-document-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmCardImports,
    HlmButton,
    HlmLabel,
    HlmSpinner,
    HlmEmptyImports,
        HlmSelectImports,
    HlmCheckboxImports,
  ],
  providers: [provideIcons({ lucideTriangleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col">
      @if (loading()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      } @else if (loadError()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyTitle>Failed to load settings</div>
            <div hlmEmptyDescription>There was an error loading document settings. Please try again.</div>
          </div>
        </div>
      } @else {
        <div class="mx-auto w-full max-w-2xl space-y-6">
          <hlm-card>
            <div hlmCardHeader>
              <h3 hlmCardTitle>Text Extraction</h3>
              <p hlmCardDescription>Configure the default engine used for text extraction when documents are uploaded.</p>
            </div>
            <div hlmCardContent>
              <div class="flex flex-col gap-1.5">
                <label hlmLabel>Default Engine</label>
                <hlm-select [value]="selectedEngineId() ?? ''" (valueChange)="onEngineChange($any($event))" [itemToString]="engineToString">
                  <hlm-select-trigger class="w-full">
                    <hlm-select-value />
                  </hlm-select-trigger>
                  <hlm-select-content *hlmSelectPortal class="max-h-60">
<hlm-select-group>

                    <hlm-select-item value="">Built-in Tesseract (default)</hlm-select-item>
                    @for (engine of engines(); track engine.id) {
                      <hlm-select-item [value]="engine.id">{{ engine.name }}</hlm-select-item>
                    }

                  </hlm-select-group>
</hlm-select-content>
                </hlm-select>
              </div>

              @if (staleEngineId()) {
                <div class="mt-4 flex items-start gap-2 rounded-md border border-yellow-500/50 bg-yellow-500/10 p-3 text-sm text-yellow-700 dark:text-yellow-400">
                  <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
                  <span>The previously configured engine no longer exists. The system is using the built-in Tesseract engine as a fallback. Save to clear the stale reference.</span>
                </div>
              }
            </div>
            <div hlmCardFooter>
              <button hlmBtn (click)="save()" [disabled]="saving() || !hasChanges()">
                {{ saving() ? 'Saving...' : 'Save' }}
              </button>
            </div>
          </hlm-card>

          <hlm-card>
            <div hlmCardHeader>
              <h3 hlmCardTitle>AI Classification</h3>
              <p hlmCardDescription>Configure AI-powered document classification settings.</p>
            </div>
            <div hlmCardContent>
              <div class="flex flex-col gap-4">
                <div class="flex items-center gap-3">
                  <hlm-checkbox
                    [checked]="selectedAutoClassify() ?? false"
                    (checkedChange)="onAutoClassifyChange($event)"
                    [aria-label]="'Auto-classify on upload'"
                  />
                  <label hlmLabel class="cursor-pointer" (click)="onAutoClassifyChange(!selectedAutoClassify())">Auto-classify on upload</label>
                </div>

                <div class="flex flex-col gap-1.5">
                  <label hlmLabel>Default AI Provider</label>
                  <hlm-select [value]="selectedAiProviderId() ?? ''" (valueChange)="onAiProviderChange($any($event))" [itemToString]="aiProviderToString">
                    <hlm-select-trigger class="w-full">
                      <hlm-select-value />
                    </hlm-select-trigger>
                    <hlm-select-content *hlmSelectPortal class="max-h-60">
<hlm-select-group>

                      <hlm-select-item value="">None</hlm-select-item>
                      @for (provider of enabledAiProviders(); track provider.id) {
                        <hlm-select-item [value]="provider.id">{{ provider.name }}</hlm-select-item>
                      }

                    </hlm-select-group>
</hlm-select-content>
                  </hlm-select>
                </div>

                @if (staleAiProviderId()) {
                  <div class="flex items-start gap-2 rounded-md border border-yellow-500/50 bg-yellow-500/10 p-3 text-sm text-yellow-700 dark:text-yellow-400">
                    <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
                    <span>The previously configured AI provider no longer exists or is disabled. Save to clear the stale reference.</span>
                  </div>
                }
              </div>
            </div>
            <div hlmCardFooter>
              <button hlmBtn (click)="save()" [disabled]="saving() || !hasChanges()">
                {{ saving() ? 'Saving...' : 'Save' }}
              </button>
            </div>
          </hlm-card>
        </div>
      }
    </div>
  `,
})
export class DocumentSettingsComponent {
  private documentApi = inject(DocumentApiService);
  private docVersionApi = inject(DocumentVersionApiService);
  private errorHandler = inject(ErrorHandlerService);

  loading = signal(true);
  saving = signal(false);
  loadError = signal(false);
  engines = signal<TextExtractionEngineSummary[]>([]);
  selectedEngineId = signal<string | null>(null);
  aiProviders = signal<AiProviderSummary[]>([]);
  selectedAiProviderId = signal<string | null>(null);
  selectedAutoClassify = signal<boolean | null>(null);

  private savedEngineId = signal<string | null>(null);
  private savedAiProviderId = signal<string | null>(null);
  private savedAutoClassify = signal<boolean | null>(null);

  enabledAiProviders = computed(() => this.aiProviders().filter(p => p.isEnabled));

  hasChanges = computed(() =>
    this.selectedEngineId() !== this.savedEngineId() ||
    this.selectedAiProviderId() !== this.savedAiProviderId() ||
    this.selectedAutoClassify() !== this.savedAutoClassify(),
  );

  engineToString = (id: string): string => {
    if (id === '') return 'Built-in Tesseract (default)';
    return this.engines().find(e => e.id === id)?.name ?? id;
  };

  aiProviderToString = (id: string): string => {
    if (id === '') return 'None';
    return this.aiProviders().find(p => p.id === id)?.name ?? id;
  };

  staleEngineId = computed(() => {
    const id = this.savedEngineId();
    if (!id || id === BUILT_IN_TESSERACT_ID) return false;
    return !this.engines().some(e => e.id === id);
  });

  staleAiProviderId = computed(() => {
    const id = this.savedAiProviderId();
    if (!id) return false;
    return !this.enabledAiProviders().some(p => p.id === id);
  });

  constructor() {
    this.loadData();
  }

  onEngineChange(value: string): void {
    this.selectedEngineId.set(value === '' ? null : value);
  }

  onAiProviderChange(value: string): void {
    this.selectedAiProviderId.set(value === '' ? null : value);
  }

  onAutoClassifyChange(checked: boolean): void {
    this.selectedAutoClassify.set(checked);
  }

  save(): void {
    this.saving.set(true);

    const patch: JsonPatchDocument = [];

    if (this.selectedEngineId() !== this.savedEngineId()) {
      patch.push({ op: 'replace', path: '/defaultTextExtractionEngineId', value: this.selectedEngineId() });
    }
    if (this.selectedAiProviderId() !== this.savedAiProviderId()) {
      patch.push({ op: 'replace', path: '/defaultAiProviderId', value: this.selectedAiProviderId() });
    }
    if (this.selectedAutoClassify() !== this.savedAutoClassify()) {
      patch.push({ op: 'replace', path: '/autoClassifyOnUpload', value: this.selectedAutoClassify() });
    }

    this.documentApi.patchDocumentSettings({ body: patch }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save document settings. Please try again.'),
    ).subscribe({
      next: (r) => {
        this.saving.set(false);
        const newEngineId = r.defaultTextExtractionEngineId ?? null;
        this.savedEngineId.set(newEngineId);
        this.selectedEngineId.set(newEngineId);
        const newAiProviderId = r.defaultAiProviderId ?? null;
        this.savedAiProviderId.set(newAiProviderId);
        this.selectedAiProviderId.set(newAiProviderId);
        const newAutoClassify = r.autoClassifyOnUpload ?? null;
        this.savedAutoClassify.set(newAutoClassify);
        this.selectedAutoClassify.set(newAutoClassify);
        toast.success('Document settings saved.');
      },
    });
  }

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    forkJoin({
      settings: this.documentApi.getDocumentSettings(),
      engines: this.docVersionApi.getTextExtractionEngineSummaries(),
      aiProviders: this.documentApi.getAiProviderSummaries(),
    }).subscribe({
      next: ({ settings, engines, aiProviders }) => {
        const savedId = settings.defaultTextExtractionEngineId ?? null;
        this.engines.set(engines);
        this.savedEngineId.set(savedId);
        this.selectedEngineId.set(this.staleEngineId() ? null : savedId);

        this.aiProviders.set(aiProviders);
        const savedAiProviderId = settings.defaultAiProviderId ?? null;
        this.savedAiProviderId.set(savedAiProviderId);
        this.selectedAiProviderId.set(this.staleAiProviderId() ? null : savedAiProviderId);
        const savedAutoClassify = settings.autoClassifyOnUpload ?? null;
        this.savedAutoClassify.set(savedAutoClassify);
        this.selectedAutoClassify.set(savedAutoClassify);

        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }
}
