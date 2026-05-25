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
            <hlm-select [ngModel]="selectedProviderType()" (ngModelChange)="selectedProviderType.set($event)" [itemToString]="providerTypeToString">
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

  providerTypeOptions = PROVIDER_TYPE_OPTIONS;
  providerTypeToString = (value: string): string => PROVIDER_TYPE_LABELS[value] ?? value;

  name = signal('');
  selectedProviderType = signal<AiProviderType>('AzureAIFoundry');
  endpoint = signal('');
  apiKey = signal('');
  model = signal('');
  creating = signal(false);
  createError = signal('');

  canSubmit = computed(() => {
    if (this.name().trim().length === 0) return false;
    if (this.selectedProviderType() === 'AzureAIFoundry') {
      return this.endpoint().trim().length > 0
        && this.apiKey().trim().length > 0
        && this.model().trim().length > 0;
    }
    return false;
  });

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.selectedProviderType.set('AzureAIFoundry');
      this.endpoint.set('');
      this.apiKey.set('');
      this.model.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSubmit(ctx: { close: () => void }): void {
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    const configuration = JSON.stringify({
      endpoint: this.endpoint().trim(),
      apiKey: this.apiKey().trim(),
      model: this.model().trim(),
    });

    this.adminApi.createAiProvider({
      body: {
        name: this.name().trim(),
        providerType: this.selectedProviderType(),
        configuration,
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
