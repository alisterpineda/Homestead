import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';

import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { EngineSelectComponent } from '@/modules/documents/shared/engine-select/engine-select.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { DocumentSettingsResponse } from '@/api/generated/models/document-settings-response';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-document-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmCardImports,
    HlmButton,
    HlmLabel,
    HlmSpinner,
    HlmEmptyImports,
    HlmCheckboxImports,
    EngineSelectComponent,
  ],
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
              <h3 hlmCardTitle>AI Classification</h3>
              <p hlmCardDescription>Configure AI-powered document classification settings.</p>
            </div>
            <div hlmCardContent>
              <div class="flex items-center gap-3">
                <hlm-checkbox
                  [checked]="selectedAutoClassify() ?? false"
                  (checkedChange)="onAutoClassifyChange($event)"
                  [aria-label]="'Auto-classify on upload'"
                />
                <label hlmLabel class="cursor-pointer" (click)="onAutoClassifyChange(!selectedAutoClassify())">Auto-classify on upload</label>
              </div>
            </div>
            <div hlmCardFooter>
              <button hlmBtn (click)="saveAi()" [disabled]="saving() || !hasAiChanges()">
                {{ saving() ? 'Saving...' : 'Save' }}
              </button>
            </div>
          </hlm-card>

          <hlm-card>
            <div hlmCardHeader>
              <h3 hlmCardTitle>Text Extraction</h3>
              <p hlmCardDescription>
                Choose the engine used to extract text from uploads. Individual reprocess runs can override it.
              </p>
            </div>
            <div hlmCardContent>
              <div class="flex flex-col gap-2">
                <label hlmLabel>Default engine</label>
                <app-engine-select
                  [value]="selectedEngineKey() ?? ''"
                  defaultLabel="No preference (built-in Tesseract)"
                  triggerClass="w-full max-w-sm"
                  (valueChange)="onEngineKeyChange($event)"
                />
              </div>
            </div>
            <div hlmCardFooter>
              <button hlmBtn (click)="saveEngine()" [disabled]="saving() || !hasEngineChanges()">
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
  private errorHandler = inject(ErrorHandlerService);

  loading = signal(true);
  saving = signal(false);
  loadError = signal(false);
  selectedAutoClassify = signal<boolean | null>(null);
  selectedEngineKey = signal<string | null>(null);

  private savedAutoClassify = signal<boolean | null>(null);
  private savedEngineKey = signal<string | null>(null);

  // Dirty state is per card, not per page. Each card has its own Save button, which implies the two
  // settings are independent — so a pending engine change must not light up the AI card's Save, and
  // saving one card must not quietly persist the other's edits.
  hasAiChanges = computed(() => this.selectedAutoClassify() !== this.savedAutoClassify());
  hasEngineChanges = computed(() => this.selectedEngineKey() !== this.savedEngineKey());

  constructor() {
    this.loadData();
  }

  onAutoClassifyChange(checked: boolean): void {
    this.selectedAutoClassify.set(checked);
  }

  // Empty means "no preference", which the backend resolves to the built-in tesseract engine.
  onEngineKeyChange(key: string): void {
    this.selectedEngineKey.set(key || null);
  }

  saveAi(): void {
    this.save(
      [{ op: 'replace', path: '/autoClassifyOnUpload', value: this.selectedAutoClassify() }],
      r => {
        const saved = r.autoClassifyOnUpload ?? null;
        this.savedAutoClassify.set(saved);
        this.selectedAutoClassify.set(saved);
      });
  }

  saveEngine(): void {
    this.save(
      [{ op: 'replace', path: '/defaultEngineKey', value: this.selectedEngineKey() }],
      r => {
        const saved = r.defaultEngineKey ?? null;
        this.savedEngineKey.set(saved);
        this.selectedEngineKey.set(saved);
      });
  }

  // Only the saved card is re-seeded from the response. The response carries both fields (one
  // AppSettings row backs both cards), but re-seeding the other card would throw away an edit the user
  // has pending there and has not saved yet.
  private save(patch: JsonPatchDocument, applySaved: (r: DocumentSettingsResponse) => void): void {
    this.saving.set(true);

    this.documentApi.patchDocumentSettings({ body: patch }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save document settings. Please try again.'),
    ).subscribe({
      next: (r) => {
        this.saving.set(false);
        applySaved(r);
        toast.success('Document settings saved.');
      },
    });
  }

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.documentApi.getDocumentSettings().subscribe({
      next: (settings) => {
        const savedAutoClassify = settings.autoClassifyOnUpload ?? null;
        this.savedAutoClassify.set(savedAutoClassify);
        this.selectedAutoClassify.set(savedAutoClassify);
        const savedEngineKey = settings.defaultEngineKey ?? null;
        this.savedEngineKey.set(savedEngineKey);
        this.selectedEngineKey.set(savedEngineKey);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }
}
