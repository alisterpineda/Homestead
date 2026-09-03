import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';

import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { TextExtractionEnginesService } from '@/modules/documents/shared/text-extraction-engines.service';
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
    HlmBadge,
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

          <!-- Read-only: engines and the default are deployment config (Ocr:*), mirroring the admin AI
               status page. -->
          <hlm-card data-testid="text-extraction-card">
            <div hlmCardHeader>
              <h3 hlmCardTitle>Text Extraction</h3>
              <p hlmCardDescription>
                Engines defined under <span class="font-mono">Ocr:Engines</span>. The default is what uploads
                and un-pinned reprocess runs use.
              </p>
            </div>
            <div hlmCardContent class="flex flex-col">
              @for (e of engines(); track e.key) {
                <div class="flex flex-col gap-1 border-b border-border/50 py-3 first:pt-0 last:border-b-0 last:pb-0"
                  data-testid="engine-row">
                  <div class="flex items-center gap-2">
                    <span class="font-mono text-sm font-medium">{{ e.key }}</span>
                    <span class="text-xs text-muted-foreground">{{ e.name }}</span>
                    @if (e.isDefault) {
                      <span hlmBadge variant="secondary">Default</span>
                    }
                    <span hlmBadge [variant]="e.isConfigured ? 'default' : 'outline'"
                      [class]="e.isConfigured ? '' : '!text-muted-foreground'">
                      {{ e.isConfigured ? 'Configured' : 'Not configured' }}
                    </span>
                  </div>
                  @if (!e.isConfigured) {
                    <p class="text-sm text-destructive">{{ e.configurationError ?? 'Unavailable.' }}</p>
                  }
                </div>
              }
              <p class="mt-4 text-xs text-muted-foreground">
                The default engine is set with <span class="font-mono">Ocr:DefaultEngine</span> in AppHost
                user-secrets, or the <span class="font-mono">Ocr__DefaultEngine</span> environment variable in
                Docker — unset means the built-in <span class="font-mono">tesseract</span>. See the project README.
              </p>
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
  private enginesService = inject(TextExtractionEnginesService);

  loading = signal(true);
  saving = signal(false);
  loadError = signal(false);
  selectedAutoClassify = signal<boolean | null>(null);

  private savedAutoClassify = signal<boolean | null>(null);

  hasAiChanges = computed(() => this.selectedAutoClassify() !== this.savedAutoClassify());

  // Read-only: the engine list (and which one is the default) is deployment config.
  engines = this.enginesService.engines;

  constructor() {
    this.loadData();
  }

  onAutoClassifyChange(checked: boolean): void {
    this.selectedAutoClassify.set(checked);
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
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }
}
