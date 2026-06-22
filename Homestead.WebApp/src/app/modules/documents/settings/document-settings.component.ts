import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';

import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
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
  private errorHandler = inject(ErrorHandlerService);

  loading = signal(true);
  saving = signal(false);
  loadError = signal(false);
  selectedAutoClassify = signal<boolean | null>(null);

  private savedAutoClassify = signal<boolean | null>(null);

  hasChanges = computed(() => this.selectedAutoClassify() !== this.savedAutoClassify());

  constructor() {
    this.loadData();
  }

  onAutoClassifyChange(checked: boolean): void {
    this.selectedAutoClassify.set(checked);
  }

  save(): void {
    this.saving.set(true);

    const patch: JsonPatchDocument = [
      { op: 'replace', path: '/autoClassifyOnUpload', value: this.selectedAutoClassify() },
    ];

    this.documentApi.patchDocumentSettings({ body: patch }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save document settings. Please try again.'),
    ).subscribe({
      next: (r) => {
        this.saving.set(false);
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
