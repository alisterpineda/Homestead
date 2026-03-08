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
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { patchTag } from '@/api/generated/fn/documents/patch-tag';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { replace } from '@/shared/json-patch';
import { ColorPickerComponent } from '../color-picker/color-picker.component';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { toast } from 'ngx-sonner';

const NONE_VALUE = '__none__';

@Component({
  selector: 'app-edit-tag-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    BrnSelectImports,
    HlmSelectImports,
    ColorPickerComponent,
    TagBadgeComponent,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)" (closed)="onClosed()">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit Tag</h3>
          <p hlmSheetDescription>Update tag details.</p>
        </hlm-sheet-header>

        @if (tag(); as t) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="tag-name">Name</label>
              <input
                hlmInput
                id="tag-name"
                class="w-full"
                [value]="editName()"
                (input)="editName.set(asInputValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel>Parent</label>
              <brn-select [ngModel]="parentSelectValue()" (ngModelChange)="onParentChange($event)">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content>
                  <hlm-option value="__none__">None (root)</hlm-option>
                  @for (parent of availableParents(); track parent.id) {
                    <hlm-option [value]="parent.id">{{ parent.path }}</hlm-option>
                  }
                </hlm-select-content>
              </brn-select>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Color</label>
              <app-color-picker [color]="editColor()" (colorChange)="editColor.set($event)" />
            </div>

            <div>
              <span class="text-xs text-muted-foreground mr-2">Preview:</span>
              <app-tag-badge [path]="previewPath()" [color]="previewEffectiveColor()" />
            </div>
          </div>
        }

        <hlm-sheet-footer>
          <button hlmBtn (click)="save()" [disabled]="!hasChanges() || !isValid() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditTagSheetComponent {
  tag = input<GetTagsResponse | null>(null);
  allTags = input<GetTagsResponse[]>([]);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  editName = signal('');
  editParentId = signal<string | null>(null);
  editColor = signal<string | null>(null);
  saving = signal(false);

  parentSelectValue = computed(() => this.editParentId() ?? NONE_VALUE);

  availableParents = computed(() => {
    const tags = this.allTags();
    const current = this.tag();
    if (!current) return tags;
    const currentPath = current.path;
    return tags.filter(t =>
      t.id !== current.id && !t.path.startsWith(currentPath + '/')
    );
  });

  private selectedParent = computed(() => {
    const parentId = this.editParentId();
    if (!parentId) return null;
    return this.allTags().find(t => t.id === parentId) ?? null;
  });

  previewEffectiveColor = computed(() => {
    const ownColor = this.editColor();
    if (ownColor) return ownColor;
    return this.selectedParent()?.effectiveColor ?? null;
  });

  previewPath = computed(() => {
    const name = this.editName();
    if (!name.trim()) return '';
    const parent = this.selectedParent();
    return parent ? parent.path + '/' + name : name;
  });

  hasChanges = computed(() => {
    const t = this.tag();
    if (!t) return false;
    if (this.editName() !== t.name) return true;
    if (this.editParentId() !== (t.parentId ?? null)) return true;
    if (this.editColor() !== (t.color ?? null)) return true;
    return false;
  });

  isValid = computed(() => {
    const name = this.editName();
    return name.trim().length > 0 && !name.includes('/');
  });

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  constructor() {
    effect(() => {
      const t = this.tag();
      if (t && this.state() === 'open') {
        this.editName.set(t.name);
        this.editParentId.set(t.parentId ?? null);
        this.editColor.set(t.color ?? null);
      }
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onParentChange(value: string): void {
    this.editParentId.set(value === NONE_VALUE ? null : value);
  }

  save(): void {
    const t = this.tag();
    if (!t) return;
    this.saving.set(true);

    const ops: JsonPatchDocument = [];
    if (this.editName() !== t.name) ops.push(replace('/name', this.editName()));
    if (this.editParentId() !== (t.parentId ?? null)) ops.push(replace('/parentId', this.editParentId()));
    if (this.editColor() !== (t.color ?? null)) ops.push(replace('/color', this.editColor()));

    patchTag(this.http, this.apiConfig.rootUrl, {
      id: t.id,
      body: ops,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Tag updated.');
        this.saved.emit();
      },
      error: (err) => {
        this.saving.set(false);
        if (err.status === 409) {
          toast.error('A tag with that path already exists.');
        } else {
          toast.error('Failed to update tag.');
        }
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
