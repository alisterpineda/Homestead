import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  inject,
  effect,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { updateTag } from '@/api/generated/fn/documents/update-tag';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { ColorPickerComponent } from '../color-picker/color-picker.component';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-edit-tag-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    ColorPickerComponent,
    TagBadgeComponent,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)" (closed)="onClosed()">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit Tag</h3>
          <p hlmSheetDescription>Update the tag color.</p>
        </hlm-sheet-header>

        @if (tag(); as t) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel>Name</label>
              <p class="text-sm text-foreground">{{ t.name }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Path</label>
              <p class="text-sm text-foreground">{{ t.path }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Color</label>
              <app-color-picker [color]="editColor()" (colorChange)="editColor.set($event)" />
            </div>

            <div>
              <span class="text-xs text-muted-foreground mr-2">Preview:</span>
              <app-tag-badge [path]="t.path" [color]="editColor()" />
            </div>
          </div>
        }

        <hlm-sheet-footer>
          <button hlmBtn (click)="save()" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditTagSheetComponent {
  tag = input<GetTagsResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  editColor = signal<string | null>(null);
  saving = signal(false);
  hasChanges = computed(() => this.editColor() !== (this.tag()?.color ?? null));

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  constructor() {
    effect(() => {
      const t = this.tag();
      if (t && this.state() === 'open') {
        this.editColor.set(t.color ?? null);
      }
    });
  }

  save(): void {
    const t = this.tag();
    if (!t) return;
    this.saving.set(true);
    updateTag(this.http, this.apiConfig.rootUrl, {
      id: t.id,
      body: { color: this.editColor() },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Tag color updated.');
        this.saved.emit();
      },
      error: () => {
        this.saving.set(false);
        toast.error('Failed to update tag color.');
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }

  onClosed(): void {
    this.closed.emit();
  }
}
