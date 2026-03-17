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
import { createOcrEngine } from '@/api/generated/fn/admin/create-ocr-engine';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-create-ocr-engine-dialog',
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
          <h2 hlmDialogTitle>Create OCR Engine</h2>
          <p hlmDialogDescription>Add a new OCR engine configuration.</p>
        </div>

        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="engine-name">Name</label>
            <input hlmInput id="engine-name" placeholder="Engine name" [value]="name()"
              (input)="name.set(asInputValue($event))" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Languages</label>
            <brn-select [multiple]="true" [ngModel]="selectedLanguages()" (ngModelChange)="selectedLanguages.set($event)">
              <hlm-select-trigger class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content>
                @for (lang of languages; track lang.value) {
                  <hlm-option [value]="lang.value">{{ lang.label }}</hlm-option>
                }
              </hlm-select-content>
            </brn-select>
          </div>

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
export class CreateOcrEngineDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon'>('default');
  created = output();

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  languages = TESSERACT_LANGUAGES;
  name = signal('');
  selectedLanguages = signal<string[]>([...DEFAULT_TESSERACT_LANGUAGES]);
  creating = signal(false);
  createError = signal('');

  canSubmit = computed(() => this.name().trim().length > 0 && this.selectedLanguages().length > 0);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.selectedLanguages.set([...DEFAULT_TESSERACT_LANGUAGES]);
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

    createOcrEngine(this.http, this.apiConfig.rootUrl, {
      body: {
        name: this.name().trim(),
        engineType: 'Tesseract',
        configuration: JSON.stringify({ languages: this.selectedLanguages() }),
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('OCR engine created.');
        ctx.close();
        this.created.emit();
      },
      error: (err: { error?: { error?: string } }) => {
        this.creating.set(false);
        this.createError.set(err?.error?.error ?? 'Failed to create OCR engine.');
      },
    });
  }
}
