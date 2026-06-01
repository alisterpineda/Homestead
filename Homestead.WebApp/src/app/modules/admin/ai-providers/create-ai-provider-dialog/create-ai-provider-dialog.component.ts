import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiProviderType } from '@/api/generated/models/ai-provider-type';
import { PROVIDER_TYPE_LABELS, PROVIDER_TYPE_OPTIONS } from '../provider-type-labels';
import { aiProviderConfigComplete, buildAiProviderConfiguration, type AiProviderTestResult } from '../ai-provider-config';
import { AiProviderProbeService } from '../ai-provider-probe.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-create-ai-provider-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, HlmDialogImports, HlmButton, HlmInput, HlmLabel,
    HlmSelectImports,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create AI Provider</h2>
          <p hlmDialogDescription>Add a new AI provider configuration.</p>
        </div>

        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="provider-name">Name</label>
            <input hlmInput id="provider-name" placeholder="Provider name" [value]="name()"
              (input)="name.set(asInputValue($event))" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Provider Type</label>
            <hlm-select [ngModel]="selectedProviderType()" (ngModelChange)="onProviderTypeChange($event)" [itemToString]="providerTypeToString">
              <hlm-select-trigger class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
<hlm-select-group>
                @for (type of providerTypeOptions; track type.value) {
                  <hlm-select-item [value]="type.value">{{ type.label }}</hlm-select-item>
                }
              </hlm-select-group>
</hlm-select-content>
            </hlm-select>
          </div>

          @if (selectedProviderType() === 'AzureAIFoundry') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="azure-endpoint">Endpoint</label>
              <input hlmInput id="azure-endpoint" placeholder="https://your-resource.openai.azure.com"
                [value]="endpoint()" (input)="endpoint.set(asInputValue($event))" />
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="azure-api-key">API Key</label>
              <input hlmInput id="azure-api-key" type="password" placeholder="API Key"
                [value]="apiKey()" (input)="apiKey.set(asInputValue($event))" />
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="azure-model">Deployment Name</label>
              <input hlmInput id="azure-model" placeholder="e.g. gpt-4o"
                [value]="model()" (input)="model.set(asInputValue($event))" />
              <p class="text-xs text-muted-foreground">The deployment name in Azure AI Foundry, not the model name.</p>
            </div>
          }

          @if (selectedProviderType() === 'OpenAI') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="openai-api-key">API Key</label>
              <input hlmInput id="openai-api-key" type="password" placeholder="API Key"
                [value]="apiKey()" (input)="apiKey.set(asInputValue($event))" />
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="openai-model">Default model</label>
              <div class="flex gap-2">
                <input hlmInput id="openai-model" class="flex-1" placeholder="e.g. gpt-4o"
                  list="create-openai-model-options"
                  [value]="model()" (input)="model.set(asInputValue($event))" />
                <datalist id="create-openai-model-options">
                  @for (m of models(); track m) {
                    <option [value]="m"></option>
                  }
                </datalist>
                <button hlmBtn variant="outline" type="button"
                  [disabled]="apiKey().trim().length === 0 || fetchingModels()"
                  (click)="fetchModels()">
                  {{ fetchingModels() ? 'Fetching...' : 'Fetch models' }}
                </button>
              </div>
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="openai-endpoint">Endpoint (optional)</label>
              <input hlmInput id="openai-endpoint" placeholder="https://api.openai.com/v1"
                [value]="endpoint()" (input)="endpoint.set(asInputValue($event))" />
              <p class="text-xs text-muted-foreground">Leave blank to use the default OpenAI endpoint.</p>
            </div>
          }

          @if (selectedProviderType() === 'AzureAIFoundry' || selectedProviderType() === 'OpenAI') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="create-max-input-chars">Max input characters (optional)</label>
              <input hlmInput id="create-max-input-chars" type="number" min="1000" step="1000"
                placeholder="Default (12000)"
                [value]="maxInputChars()" (input)="maxInputChars.set(asInputValue($event))" />
              <p class="text-xs text-muted-foreground">How much document text to send. Raise it for large-context models; leave blank for the default.</p>
            </div>
          }

          @if (canTest()) {
            <div class="flex flex-col gap-1.5">
              <button hlmBtn variant="outline" type="button" [disabled]="testing()" (click)="testConnection()">
                {{ testing() ? 'Testing...' : 'Test connection' }}
              </button>
              @if (testResult(); as r) {
                <p [class]="'text-sm ' + (r.success ? 'text-green-600 dark:text-green-400' : 'text-destructive')">{{ r.message }}</p>
              }
            </div>
          }

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
          <button hlmBtn [disabled]="!canSubmit() || creating()" (click)="handleSubmit(ctx)">
            {{ creating() ? 'Creating...' : 'Create' }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateAiProviderDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon'>('default');
  created = output();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);
  private probe = inject(AiProviderProbeService);

  providerTypeOptions = PROVIDER_TYPE_OPTIONS;
  providerTypeToString = (value: string): string => PROVIDER_TYPE_LABELS[value] ?? value;

  name = signal('');
  selectedProviderType = signal<AiProviderType>('AzureAIFoundry');
  endpoint = signal('');
  apiKey = signal('');
  model = signal('');
  maxInputChars = signal('');
  creating = signal(false);
  createError = signal('');

  models = signal<string[]>([]);
  fetchingModels = signal(false);
  testing = signal(false);
  testResult = signal<AiProviderTestResult | null>(null);

  // Required fields per provider, excluding the name — gates the Test connection button.
  canTest = computed(() => aiProviderConfigComplete(this.selectedProviderType(), {
    endpoint: this.endpoint(),
    apiKey: this.apiKey(),
    model: this.model(),
  }));

  canSubmit = computed(() => this.name().trim().length > 0 && this.canTest());

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.selectedProviderType.set('AzureAIFoundry');
      this.resetProviderFields();
      this.creating.set(false);
      this.createError.set('');
    }
  }

  onProviderTypeChange(type: AiProviderType): void {
    this.selectedProviderType.set(type);
    this.resetProviderFields();
  }

  private resetProviderFields(): void {
    this.endpoint.set('');
    this.apiKey.set('');
    this.model.set('');
    this.maxInputChars.set('');
    this.models.set([]);
    this.fetchingModels.set(false);
    this.testing.set(false);
    this.testResult.set(null);
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private buildConfiguration(): string {
    return buildAiProviderConfiguration({
      endpoint: this.endpoint(),
      apiKey: this.apiKey(),
      model: this.model(),
      maxInputChars: this.maxInputChars(),
    });
  }

  fetchModels(): void {
    this.fetchingModels.set(true);
    this.probe.fetchModels(this.adminApi.listAiProviderModels({
      body: {
        providerType: this.selectedProviderType(),
        configuration: this.buildConfiguration(),
      },
    })).subscribe({
      next: (models) => {
        this.fetchingModels.set(false);
        this.models.set(models);
      },
      error: () => this.fetchingModels.set(false),
    });
  }

  testConnection(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.probe.testConnection(this.adminApi.testAiProviderConnection({
      body: {
        providerType: this.selectedProviderType(),
        configuration: this.buildConfiguration(),
      },
    })).subscribe((result) => {
      this.testing.set(false);
      this.testResult.set(result);
    });
  }

  handleSubmit(ctx: { close: () => void }): void {
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    this.adminApi.createAiProvider({
      body: {
        name: this.name().trim(),
        providerType: this.selectedProviderType(),
        configuration: this.buildConfiguration(),
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('AI provider created.');
        ctx.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create AI provider.'));
      },
    });
  }
}
