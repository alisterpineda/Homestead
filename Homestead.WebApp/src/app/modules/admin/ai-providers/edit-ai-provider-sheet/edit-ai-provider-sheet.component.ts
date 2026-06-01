import { Component, ChangeDetectionStrategy, inject, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiProviderResponse } from '@/api/generated/models/ai-provider-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { PROVIDER_TYPE_LABELS } from '../provider-type-labels';
import { aiProviderConfigComplete, buildAiProviderConfiguration, type AiProviderTestResult } from '../ai-provider-config';
import { AiProviderProbeService } from '../ai-provider-probe.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-edit-ai-provider-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, HlmSheetImports, HlmButton, HlmInput, HlmLabel, HlmBadge,
    HlmCheckboxImports,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit Provider</h3>
          <p hlmSheetDescription>Update AI provider configuration.</p>
        </hlm-sheet-header>

        @if (provider(); as p) {
          <div class="flex-1 space-y-4 overflow-y-auto px-4 py-4">
            <div class="flex flex-col gap-2">
              <label hlmLabel for="edit-provider-name">Name</label>
              <input hlmInput id="edit-provider-name" [value]="editName()"
                (input)="editName.set(asInputValue($event))" />
            </div>

            <div class="flex flex-col gap-2">
              <label hlmLabel>Provider Type</label>
              <p><span hlmBadge variant="secondary">{{ getProviderTypeLabel(p.providerType) }}</span></p>
            </div>

            @if (p.providerType === 'AzureAIFoundry') {
              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-azure-endpoint">Endpoint</label>
                <input hlmInput id="edit-azure-endpoint"
                  placeholder="https://your-resource.openai.azure.com"
                  [value]="editEndpoint()" (input)="editEndpoint.set(asInputValue($event))" />
              </div>

              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-azure-api-key">API Key</label>
                <input hlmInput id="edit-azure-api-key" type="password" placeholder="API Key"
                  [value]="editApiKey()" (input)="editApiKey.set(asInputValue($event))" />
                <p class="text-xs text-muted-foreground">Leave unchanged to keep the existing key.</p>
              </div>

              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-azure-model">Deployment Name</label>
                <input hlmInput id="edit-azure-model" placeholder="e.g. gpt-4o"
                  [value]="editModel()" (input)="editModel.set(asInputValue($event))" />
                <p class="text-xs text-muted-foreground">The deployment name in Azure AI Foundry, not the model name.</p>
              </div>
            }

            @if (p.providerType === 'OpenAI') {
              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-openai-api-key">API Key</label>
                <input hlmInput id="edit-openai-api-key" type="password" placeholder="API Key"
                  [value]="editApiKey()" (input)="editApiKey.set(asInputValue($event))" />
                <p class="text-xs text-muted-foreground">Leave unchanged to keep the existing key.</p>
              </div>

              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-openai-model">Default model</label>
                <div class="flex gap-2">
                  <input hlmInput id="edit-openai-model" class="flex-1" placeholder="e.g. gpt-4o"
                    list="edit-openai-model-options"
                    [value]="editModel()" (input)="editModel.set(asInputValue($event))" />
                  <datalist id="edit-openai-model-options">
                    @for (m of models(); track m) {
                      <option [value]="m"></option>
                    }
                  </datalist>
                  <button hlmBtn variant="outline" type="button"
                    [disabled]="editApiKey().trim().length === 0 || fetchingModels()"
                    (click)="fetchModels()">
                    {{ fetchingModels() ? 'Fetching...' : 'Fetch models' }}
                  </button>
                </div>
              </div>

              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-openai-endpoint">Endpoint (optional)</label>
                <input hlmInput id="edit-openai-endpoint" placeholder="https://api.openai.com/v1"
                  [value]="editEndpoint()" (input)="editEndpoint.set(asInputValue($event))" />
                <p class="text-xs text-muted-foreground">Leave blank to use the default OpenAI endpoint.</p>
              </div>
            }

            @if (p.providerType === 'AzureAIFoundry' || p.providerType === 'OpenAI') {
              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-max-input-chars">Max input characters (optional)</label>
                <input hlmInput id="edit-max-input-chars" type="number" min="1000" step="1000"
                  placeholder="Default (12000)"
                  [value]="editMaxInputChars()" (input)="editMaxInputChars.set(asInputValue($event))" />
                <p class="text-xs text-muted-foreground">How much document text to send. Raise it for large-context models; leave blank for the default.</p>
              </div>
            }

            @if (canTest()) {
              <div class="flex flex-col gap-2">
                <button hlmBtn variant="outline" type="button" [disabled]="testing()" (click)="testConnection()">
                  {{ testing() ? 'Testing...' : 'Test connection' }}
                </button>
                @if (testResult(); as r) {
                  <p [class]="'text-sm ' + (r.success ? 'text-green-600 dark:text-green-400' : 'text-destructive')">{{ r.message }}</p>
                }
              </div>
            }

            <div class="flex items-center gap-2">
              <hlm-checkbox [checked]="editEnabled()" (checkedChange)="editEnabled.set($event)"
                aria-label="Enable provider" />
              <label hlmLabel class="cursor-pointer" (click)="editEnabled.set(!editEnabled())">Enabled</label>
            </div>

            @if (p.createdByUserName) {
              <div class="flex flex-col gap-2">
                <label hlmLabel>Created By</label>
                <p class="text-sm text-muted-foreground">{{ p.createdByUserName }}</p>
              </div>
            }
          </div>
        }

        <hlm-sheet-footer>
          @if (provider(); as p) {
            <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
              {{ saving() ? 'Saving...' : 'Save' }}
            </button>
          }
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditAiProviderSheetComponent {
  provider = input<AiProviderResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);
  private probe = inject(AiProviderProbeService);

  private seedKey = computed(() =>
    `${this.provider()?.id ?? ''}|${this.state() === 'open'}`,
  );

  private seed = computed((): { endpoint: string; apiKey: string; model: string; maxInputChars: string } | null => {
    const p = this.provider();
    if (!p || this.state() !== 'open') return null;

    let endpoint = '';
    let apiKey = '';
    let model = '';
    let maxInputChars = '';
    if (p.configuration) {
      try {
        const parsed = JSON.parse(p.configuration);
        // apiKey arrives masked (e.g. ••••XXXX); seeding it unchanged lets the merge on save /
        // the {id} test+fetch routes reuse the stored secret.
        endpoint = parsed.endpoint ?? '';
        apiKey = parsed.apiKey ?? '';
        model = parsed.model ?? '';
        // Stored as a number; the input edits it as a string. Absent -> blank (use the server default).
        maxInputChars = parsed.maxInputChars != null ? String(parsed.maxInputChars) : '';
      } catch { /* empty */ }
    }
    return { endpoint, apiKey, model, maxInputChars };
  });

  editName = seededLinkedSignal(this.seedKey, () => {
    const p = this.provider();
    if (!p || this.state() !== 'open') return '';
    return p.name;
  });
  editEndpoint = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed?.endpoint ?? '';
  });
  editApiKey = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed?.apiKey ?? '';
  });
  editModel = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed?.model ?? '';
  });
  editMaxInputChars = seededLinkedSignal(this.seedKey, () => {
    const seed = this.seed();
    return seed?.maxInputChars ?? '';
  });
  editEnabled = seededLinkedSignal(this.seedKey, () => {
    const p = this.provider();
    if (!p || this.state() !== 'open') return true;
    return p.isEnabled;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);

  models = seededLinkedSignal(this.seedKey, () => [] as string[]);
  fetchingModels = seededLinkedSignal(this.seedKey, () => false);
  testing = seededLinkedSignal(this.seedKey, () => false);
  testResult = seededLinkedSignal(this.seedKey, () => null as AiProviderTestResult | null);

  editConfiguration = computed(() => {
    const p = this.provider();
    if (!p) return null;
    // Azure and OpenAI share the same JSON config keys; other types keep their stored config as-is.
    if (p.providerType === 'AzureAIFoundry' || p.providerType === 'OpenAI') {
      return buildAiProviderConfiguration({
        endpoint: this.editEndpoint(),
        apiKey: this.editApiKey(),
        model: this.editModel(),
        maxInputChars: this.editMaxInputChars(),
      });
    }
    return p.configuration ?? null;
  });

  canTest = computed(() => {
    const p = this.provider();
    if (!p) return false;
    return aiProviderConfigComplete(p.providerType, {
      endpoint: this.editEndpoint(),
      apiKey: this.editApiKey(),
      model: this.editModel(),
    });
  });

  hasChanges = computed(() => {
    const p = this.provider();
    if (!p) return false;
    if (this.editName() !== p.name) return true;
    if (this.editEnabled() !== p.isEnabled) return true;
    // Compare each editable field against its seeded original rather than comparing serialized
    // JSON to the stored config: masking round-trips the key as • escapes (and key order may
    // differ), so a string comparison reports false changes on a pristine form. Leaving the masked
    // apiKey untouched correctly counts as no change (the merge reuses the stored secret on save).
    const seed = this.seed();
    if (seed) {
      if (this.editEndpoint() !== seed.endpoint) return true;
      if (this.editApiKey() !== seed.apiKey) return true;
      if (this.editModel() !== seed.model) return true;
      if (this.editMaxInputChars().trim() !== seed.maxInputChars) return true;
    }
    return false;
  });

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  getProviderTypeLabel(providerType: string): string {
    return PROVIDER_TYPE_LABELS[providerType] ?? providerType;
  }

  fetchModels(): void {
    const p = this.provider();
    if (!p) return;
    this.fetchingModels.set(true);
    // The {id} route reuses the stored key when the masked sentinel is left unchanged.
    this.probe.fetchModels(this.adminApi.listAiProviderModelsById({
      id: p.id,
      body: { configuration: this.editConfiguration() },
    })).subscribe({
      next: (models) => {
        this.fetchingModels.set(false);
        this.models.set(models);
      },
      error: () => this.fetchingModels.set(false),
    });
  }

  testConnection(): void {
    const p = this.provider();
    if (!p) return;
    this.testing.set(true);
    this.testResult.set(null);
    this.probe.testConnection(this.adminApi.testAiProviderConnectionById({
      id: p.id,
      body: { configuration: this.editConfiguration() },
    })).subscribe((result) => {
      this.testing.set(false);
      this.testResult.set(result);
    });
  }

  handleSave(): void {
    const p = this.provider();
    if (!p) return;
    this.saving.set(true);

    this.adminApi.updateAiProvider({
      id: p.id,
      body: {
        name: this.editName().trim(),
        configuration: this.editConfiguration(),
        isEnabled: this.editEnabled(),
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('AI provider updated.');
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
