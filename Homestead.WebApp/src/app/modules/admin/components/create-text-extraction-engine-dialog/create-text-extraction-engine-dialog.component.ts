import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { DEFAULT_TESSERACT_LANGUAGES, TESSERACT_LANGUAGES } from '../tesseract-config-fields/tesseract-languages';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { createTextExtractionEngine } from '@/api/generated/fn/admin/create-text-extraction-engine';
import type { TextExtractionEngineType } from '@/api/generated/models/text-extraction-engine-type';
import { ENGINE_TYPE_OPTIONS } from '../../constants/engine-type-labels';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-create-text-extraction-engine-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, HlmDialogImports, HlmButton, HlmInput, HlmLabel,
    BrnSelectImports, HlmSelectImports,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create Text Extraction Engine</h2>
          <p hlmDialogDescription>Add a new text extraction engine configuration.</p>
        </div>

        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="engine-name">Name</label>
            <input hlmInput id="engine-name" placeholder="Engine name" [value]="name()"
              (input)="name.set(asInputValue($event))" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Engine Type</label>
            <brn-select [ngModel]="selectedEngineType()" (ngModelChange)="selectedEngineType.set($event)">
              <hlm-select-trigger class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content>
                @for (type of engineTypeOptions; track type.value) {
                  <hlm-option [value]="type.value">{{ type.label }}</hlm-option>
                }
              </hlm-select-content>
            </brn-select>
          </div>

          @if (selectedEngineType() === 'Tesseract') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel>Languages</label>
              <brn-select [multiple]="true" [ngModel]="selectedLanguages()" (ngModelChange)="selectedLanguages.set($event)">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content class="max-h-60">
                  <hlm-select-scroll-up />
                  @for (lang of languages; track lang.value) {
                    <hlm-option [value]="lang.value">{{ lang.label }}</hlm-option>
                  }
                  <hlm-select-scroll-down />
                </hlm-select-content>
              </brn-select>
            </div>
          }

          @if (selectedEngineType() === 'AzureDocumentIntelligence') {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="azure-endpoint">Endpoint</label>
              <input hlmInput id="azure-endpoint" placeholder="https://your-resource.cognitiveservices.azure.com"
                [value]="endpoint()" (input)="endpoint.set(asInputValue($event))" />
            </div>

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="azure-api-key">API Key</label>
              <input hlmInput id="azure-api-key" type="password" placeholder="API Key"
                [value]="apiKey()" (input)="apiKey.set(asInputValue($event))" />
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
export class CreateTextExtractionEngineDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon'>('default');
  created = output();

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  engineTypeOptions = ENGINE_TYPE_OPTIONS;
  languages = TESSERACT_LANGUAGES;
  name = signal('');
  selectedEngineType = signal<TextExtractionEngineType>('Tesseract');
  selectedLanguages = signal<string[]>([...DEFAULT_TESSERACT_LANGUAGES]);
  endpoint = signal('');
  apiKey = signal('');
  creating = signal(false);
  createError = signal('');

  canSubmit = computed(() => {
    if (this.name().trim().length === 0) return false;
    if (this.selectedEngineType() === 'Tesseract') {
      return this.selectedLanguages().length > 0;
    }
    if (this.selectedEngineType() === 'AzureDocumentIntelligence') {
      return this.endpoint().trim().length > 0 && this.apiKey().trim().length > 0;
    }
    return false;
  });

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.selectedEngineType.set('Tesseract');
      this.selectedLanguages.set([...DEFAULT_TESSERACT_LANGUAGES]);
      this.endpoint.set('');
      this.apiKey.set('');
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

    const engineType = this.selectedEngineType();
    let configuration: string;
    if (engineType === 'AzureDocumentIntelligence') {
      configuration = JSON.stringify({
        endpoint: this.endpoint().trim(),
        apiKey: this.apiKey().trim(),
      });
    } else {
      configuration = JSON.stringify({ languages: this.selectedLanguages() });
    }

    createTextExtractionEngine(this.http, this.apiConfig.rootUrl, {
      body: {
        name: this.name().trim(),
        engineType,
        configuration,
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Text extraction engine created.');
        ctx.close();
        this.created.emit();
      },
      error: (err: { error?: { error?: string } }) => {
        this.creating.set(false);
        this.createError.set(err?.error?.error ?? 'Failed to create engine.');
      },
    });
  }
}
