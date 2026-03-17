import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output, effect } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { TESSERACT_LANGUAGES } from '../tesseract-config-fields/tesseract-languages';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { updateOcrEngine } from '@/api/generated/fn/admin/update-ocr-engine';
import { deleteOcrEngine } from '@/api/generated/fn/admin/delete-ocr-engine';
import type { OcrEngineResponse } from '@/api/generated/models/ocr-engine-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-edit-ocr-engine-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, HlmSheetImports, HlmButton, HlmInput, HlmLabel, HlmBadge,
    BrnSelectImports, HlmSelectImports,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>{{ engine()?.isBuiltIn ? 'View' : 'Edit' }} OCR Engine</h3>
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
              <p><span hlmBadge variant="secondary">{{ e.engineType }}</span></p>
            </div>

            @if (e.engineType === 'Tesseract') {
              <div class="flex flex-col gap-2">
                <label hlmLabel>Language</label>
                @if (e.isBuiltIn) {
                  <p class="text-sm text-muted-foreground">{{ getLanguageLabel(e.configuration) }}</p>
                } @else {
                  <brn-select [ngModel]="editLanguage()" (ngModelChange)="onLanguageChange($event)">
                    <hlm-select-trigger class="w-full">
                      <hlm-select-value />
                    </hlm-select-trigger>
                    <hlm-select-content>
                      @for (lang of languages; track lang.value) {
                        <hlm-option [value]="lang.value">{{ lang.label }}</hlm-option>
                      }
                    </hlm-select-content>
                  </brn-select>
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
              @if (confirmingDelete()) {
                <p class="text-sm text-destructive text-center">This action cannot be undone.</p>
                <div class="flex gap-2">
                  <button hlmBtn variant="secondary" class="flex-1" (click)="confirmingDelete.set(false)">Cancel</button>
                  <button hlmBtn variant="destructive" class="flex-1" (click)="handleDelete()" [disabled]="deleting()">
                    {{ deleting() ? 'Deleting...' : 'Confirm Delete' }}
                  </button>
                </div>
              } @else {
                <button hlmBtn variant="destructive" class="w-full" (click)="confirmingDelete.set(true)">Delete</button>
                <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
                  {{ saving() ? 'Saving...' : 'Save' }}
                </button>
              }
            }
          }
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditOcrEngineSheetComponent {
  engine = input<OcrEngineResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  deleted = output<void>();
  closed = output<void>();

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  languages = TESSERACT_LANGUAGES;
  editName = signal('');
  editLanguage = signal('Eng');
  editConfiguration = signal<string | null>(null);
  saving = signal(false);
  deleting = signal(false);
  confirmingDelete = signal(false);

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
        // Parse language from configuration
        if (e.configuration) {
          try {
            const parsed = JSON.parse(e.configuration);
            if (parsed.language) {
              this.editLanguage.set(parsed.language);
            }
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

  getLanguageLabel(configuration: string | null | undefined): string {
    if (!configuration) return 'English';
    try {
      const parsed = JSON.parse(configuration);
      const lang = this.languages.find(l => l.value === parsed.language);
      return lang?.label ?? parsed.language ?? 'English';
    } catch {
      return 'Unknown';
    }
  }

  onLanguageChange(value: string): void {
    this.editLanguage.set(value);
    this.editConfiguration.set(JSON.stringify({ language: value }));
  }

  handleSave(): void {
    const e = this.engine();
    if (!e || e.isBuiltIn) return;
    this.saving.set(true);

    updateOcrEngine(this.http, this.apiConfig.rootUrl, {
      id: e.id,
      body: {
        name: this.editName().trim(),
        configuration: this.editConfiguration(),
      },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('OCR engine updated.');
        this.saved.emit();
      },
      error: (err: { error?: { error?: string } }) => {
        this.saving.set(false);
        toast.error(err?.error?.error ?? 'Failed to save changes.');
      },
    });
  }

  handleDelete(): void {
    const e = this.engine();
    if (!e || e.isBuiltIn) return;
    this.deleting.set(true);

    deleteOcrEngine(this.http, this.apiConfig.rootUrl, { id: e.id }).subscribe({
      next: () => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        toast.success('OCR engine deleted.');
        this.deleted.emit();
      },
      error: (err: { error?: { error?: string } }) => {
        this.deleting.set(false);
        toast.error(err?.error?.error ?? 'Failed to delete OCR engine.');
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.confirmingDelete.set(false);
      this.closed.emit();
    }
  }
}
