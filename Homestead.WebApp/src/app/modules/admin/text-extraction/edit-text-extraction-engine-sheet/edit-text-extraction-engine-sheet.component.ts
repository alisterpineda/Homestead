import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmBadge } from '@/shared/components/spartan/badge';

import { HlmSelectImports } from '@/shared/components/spartan/select';
import { DEFAULT_TESSERACT_LANGUAGES, TESSERACT_LANGUAGES } from '../tesseract-config-fields/tesseract-languages';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { ENGINE_TYPE_LABELS } from '../engine-type-labels';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-edit-text-extraction-engine-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, HlmSheetImports, HlmButton, HlmInput, HlmLabel, HlmBadge,
    HlmSelectImports,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ engine()?.isBuiltIn ? 'View' : 'Edit' }} Engine</h3>
          <p hlmSheetDescription>{{ engine()?.isBuiltIn ? 'Built-in engine details.' : 'Update engine configuration.' }}</p>
        </hlm-sheet-header>

        @if (engine(); as e) {
          <div class="flex-1 space-y-4 overflow-y-auto px-4 py-4">
            <div class="flex flex-col gap-2">
              <label hlmLabel for="edit-engine-name">Name</label>
              @if (e.isBuiltIn) {
                <p class="text-sm text-muted-foreground">{{ e.name }}</p>
              } @else {
                <input hlmInput id="edit-engine-name" [value]="editName()"
                  (input)="editName.set(asInputValue($event))" />
              }
            </div>

            <div class="flex flex-col gap-2">
              <label hlmLabel>Engine Type</label>
              <p><span hlmBadge variant="secondary">{{ getEngineTypeLabel(e.engineType) }}</span></p>
            </div>

            @if (e.engineType === 'Tesseract') {
              <div class="flex flex-col gap-2">
                <label hlmLabel>Languages</label>
                @if (e.isBuiltIn) {
                  <p class="text-sm text-muted-foreground">{{ getLanguagesLabel(e.configuration) }}</p>
                } @else {
                  <hlm-select-multiple [ngModel]="editLanguages()" (ngModelChange)="onLanguagesChange($event)">
                    <hlm-select-trigger class="w-full">
                      <hlm-select-value />
                    </hlm-select-trigger>
                    <hlm-select-content *hlmSelectPortal class="max-h-60">
<hlm-select-group>
                      
                      @for (lang of languages; track lang.value) {
                        <hlm-select-item [value]="lang.value">{{ lang.label }}</hlm-select-item>
                      }
                      
                    </hlm-select-group>
</hlm-select-content>
                  </hlm-select-multiple>
                }
              </div>
            }

            @if (e.engineType === 'AzureDocumentIntelligence') {
              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-azure-endpoint">Endpoint</label>
                @if (e.isBuiltIn) {
                  <p class="text-sm text-muted-foreground">{{ editEndpoint() }}</p>
                } @else {
                  <input hlmInput id="edit-azure-endpoint"
                    placeholder="https://your-resource.cognitiveservices.azure.com"
                    [value]="editEndpoint()" (input)="onEndpointChange(asInputValue($event))" />
                }
              </div>

              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-azure-api-key">API Key</label>
                @if (e.isBuiltIn) {
                  <p class="text-sm text-muted-foreground">********</p>
                } @else {
                  <input hlmInput id="edit-azure-api-key" type="password" placeholder="API Key"
                    [value]="editApiKey()" (input)="onApiKeyChange(asInputValue($event))" />
                }
              </div>
            }

            @if (e.createdByUserName) {
              <div class="flex flex-col gap-2">
                <label hlmLabel>Created By</label>
                <p class="text-sm text-muted-foreground">{{ e.createdByUserName }}</p>
              </div>
            }
          </div>
        }

        <hlm-sheet-footer>
          @if (engine(); as e) {
            @if (!e.isBuiltIn) {
              <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
                {{ saving() ? 'Saving...' : 'Save' }}
              </button>
            }
          }
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditTextExtractionEngineSheetComponent {
  engine = input<TextExtractionEngineResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);

  languages = TESSERACT_LANGUAGES;
  editName = signal('');
  editLanguages = signal<string[]>([...DEFAULT_TESSERACT_LANGUAGES]);
  editEndpoint = signal('');
  editApiKey = signal('');
  editConfiguration = signal<string | null>(null);
  saving = signal(false);

  hasChanges = computed(() => {
    const e = this.engine();
    if (!e || e.isBuiltIn) return false;
    if (this.editName() !== e.name) return true;
    if (this.editConfiguration() !== (e.configuration ?? null)) return true;
    return false;
  });

  constructor() {
    effect(() => {
      const e = this.engine();
      if (e && this.state() === 'open') {
        this.editName.set(e.name);
        this.editConfiguration.set(e.configuration ?? null);
        if (e.engineType === 'Tesseract' && e.configuration) {
          try {
            const parsed = JSON.parse(e.configuration);
            if (Array.isArray(parsed.languages)) {
              this.editLanguages.set(parsed.languages);
            }
          } catch {
            // ignore
          }
        }
        if (e.engineType === 'AzureDocumentIntelligence' && e.configuration) {
          try {
            const parsed = JSON.parse(e.configuration);
            this.editEndpoint.set(parsed.endpoint ?? '');
            this.editApiKey.set(parsed.apiKey ?? '');
          } catch {
            // ignore
          }
        }
      }
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  getEngineTypeLabel(engineType: string): string {
    return ENGINE_TYPE_LABELS[engineType] ?? engineType;
  }

  getLanguagesLabel(configuration: string | null | undefined): string {
    if (!configuration) return 'English';
    try {
      const parsed = JSON.parse(configuration);
      if (!Array.isArray(parsed.languages) || parsed.languages.length === 0) return 'English';
      return parsed.languages
        .map((v: string) => this.languages.find(l => l.value === v)?.label ?? v)
        .join(', ');
    } catch {
      return 'Unknown';
    }
  }

  onLanguagesChange(values: string[]): void {
    this.editLanguages.set(values);
    this.editConfiguration.set(JSON.stringify({ languages: values }));
  }

  onEndpointChange(value: string): void {
    this.editEndpoint.set(value);
    this.rebuildAzureConfig();
  }

  onApiKeyChange(value: string): void {
    this.editApiKey.set(value);
    this.rebuildAzureConfig();
  }

  private rebuildAzureConfig(): void {
    this.editConfiguration.set(JSON.stringify({
      endpoint: this.editEndpoint(),
      apiKey: this.editApiKey(),
    }));
  }

  handleSave(): void {
    const e = this.engine();
    if (!e || e.isBuiltIn) return;
    this.saving.set(true);

    this.adminApi.updateTextExtractionEngine({
      id: e.id,
      body: {
        name: this.editName().trim(),
        configuration: this.editConfiguration(),
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Engine updated.');
        this.saved.emit();
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
