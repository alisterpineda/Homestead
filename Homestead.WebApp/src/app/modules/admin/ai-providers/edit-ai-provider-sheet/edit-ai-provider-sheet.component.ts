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
              </div>

              <div class="flex flex-col gap-2">
                <label hlmLabel for="edit-azure-model">Deployment Name</label>
                <input hlmInput id="edit-azure-model" placeholder="e.g. gpt-4o"
                  [value]="editModel()" (input)="editModel.set(asInputValue($event))" />
                <p class="text-xs text-muted-foreground">The deployment name in Azure AI Foundry, not the model name.</p>
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

  private seedKey = computed(() =>
    `${this.provider()?.id ?? ''}|${this.state() === 'open'}`,
  );

  private seed = computed((): { endpoint: string; apiKey: string; model: string } | null => {
    const p = this.provider();
    if (!p || this.state() !== 'open') return null;

    let endpoint = '';
    let apiKey = '';
    let model = '';
    if (p.configuration) {
      try {
        const parsed = JSON.parse(p.configuration);
        endpoint = parsed.endpoint ?? '';
        apiKey = parsed.apiKey ?? '';
        model = parsed.model ?? '';
      } catch { /* empty */ }
    }
    return { endpoint, apiKey, model };
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
  editEnabled = seededLinkedSignal(this.seedKey, () => {
    const p = this.provider();
    if (!p || this.state() !== 'open') return true;
    return p.isEnabled;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);

  editConfiguration = computed(() => {
    const p = this.provider();
    if (!p) return null;
    if (p.providerType === 'AzureAIFoundry') {
      return JSON.stringify({
        endpoint: this.editEndpoint(),
        apiKey: this.editApiKey(),
        model: this.editModel(),
      });
    }
    return p.configuration ?? null;
  });

  hasChanges = computed(() => {
    const p = this.provider();
    if (!p) return false;
    if (this.editName() !== p.name) return true;
    if (this.editEnabled() !== p.isEnabled) return true;
    if (this.editConfiguration() !== (p.configuration ?? null)) return true;
    return false;
  });

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  getProviderTypeLabel(providerType: string): string {
    return PROVIDER_TYPE_LABELS[providerType] ?? providerType;
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
