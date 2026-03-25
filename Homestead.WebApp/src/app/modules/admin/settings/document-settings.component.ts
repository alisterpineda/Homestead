import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { AdminApiService } from '@/api/services/admin-api.service';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import type { TextExtractionEngineSummary } from '@/api/generated/models/text-extraction-engine-summary';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import { toast } from 'ngx-sonner';
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
    BrnSelectImports,
    HlmSelectImports,
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
        <section hlmEmpty>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load settings</h3>
            <p hlmEmptyDescription>There was an error loading document settings. Please try again.</p>
          </div>
        </section>
      } @else {
        <div class="max-w-2xl">
          <hlm-card>
            <div hlmCardHeader>
              <h3 hlmCardTitle>Text Extraction</h3>
              <p hlmCardDescription>Configure the default engine used for text extraction when documents are uploaded.</p>
            </div>
            <div hlmCardContent>
              <div class="flex flex-col gap-1.5">
                <label hlmLabel>Default Engine</label>
                <brn-select [value]="selectedEngineId() ?? ''" (valueChange)="onEngineChange($any($event))">
                  <hlm-select-trigger class="w-full">
                    <hlm-select-value />
                  </hlm-select-trigger>
                  <hlm-select-content class="max-h-60">
                    <hlm-select-scroll-up />
                    <hlm-option value="">Built-in Tesseract (default)</hlm-option>
                    @for (engine of engines(); track engine.id) {
                      <hlm-option [value]="engine.id">{{ engine.name }}</hlm-option>
                    }
                    <hlm-select-scroll-down />
                  </hlm-select-content>
                </brn-select>
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
        </div>
      }
    </div>
  `,
})
export class DocumentSettingsComponent {
  private adminApi = inject(AdminApiService);
  private docVersionApi = inject(DocumentVersionApiService);
  private errorHandler = inject(ErrorHandlerService);

  loading = signal(true);
  saving = signal(false);
  loadError = signal(false);
  engines = signal<TextExtractionEngineSummary[]>([]);
  selectedEngineId = signal<string | null>(null);

  private savedEngineId = signal<string | null>(null);

  hasChanges = computed(() => this.selectedEngineId() !== this.savedEngineId());

  staleEngineId = computed(() => {
    const id = this.savedEngineId();
    if (!id || id === BUILT_IN_TESSERACT_ID) return false;
    return !this.engines().some(e => e.id === id);
  });

  constructor() {
    this.loadData();
  }

  onEngineChange(value: string): void {
    this.selectedEngineId.set(value === '' ? null : value);
  }

  save(): void {
    this.saving.set(true);
    const value = this.selectedEngineId();

    const patch: JsonPatchDocument = [
      { op: 'replace', path: '/defaultTextExtractionEngineId', value: value },
    ];

    this.adminApi.patchDocumentSettings({ body: patch }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save document settings. Please try again.'),
    ).subscribe({
      next: (r) => {
        this.saving.set(false);
        const newId = r.defaultTextExtractionEngineId ?? null;
        this.savedEngineId.set(newId);
        this.selectedEngineId.set(newId);
        toast.success('Document settings saved.');
      },
    });
  }

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    forkJoin({
      settings: this.adminApi.getDocumentSettings(),
      engines: this.docVersionApi.getTextExtractionEngineSummaries(),
    }).subscribe({
      next: ({ settings, engines }) => {
        const savedId = settings.defaultTextExtractionEngineId ?? null;
        this.engines.set(engines);
        this.savedEngineId.set(savedId);
        // staleEngineId computed derives from savedEngineId + engines
        this.selectedEngineId.set(this.staleEngineId() ? null : savedId);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }
}
