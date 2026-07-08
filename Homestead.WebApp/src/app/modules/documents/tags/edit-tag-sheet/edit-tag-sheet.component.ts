import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFieldImports } from '@/shared/components/spartan/field';

import { HlmSelectImports } from '@/shared/components/spartan/select';
import { TagApiService } from '@/api/services/tag-api.service';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { replace } from '@/shared/json-patch';
import { ColorPickerComponent } from '@/shared/components/color-picker/color-picker.component';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';

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
    HlmFieldImports,
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
            <hlm-field>
              <label hlmFieldLabel for="tag-name">Name</label>
              <input
                hlmInput
                id="tag-name"
                class="w-full"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
              <hlm-field-description>Cannot contain '/' characters.</hlm-field-description>
              <hlm-field-error>
                @if (nameRequiredError()) {
                  Tag name is required.
                } @else if (namePatternError()) {
                  Tag name cannot contain '/' characters.
                } @else if (nameMaxLenError()) {
                  Tag name must be 256 characters or fewer.
                }
              </hlm-field-error>
            </hlm-field>

            <div class="space-y-2">
              <label hlmLabel>Parent</label>
              <hlm-select [ngModel]="parentSelectValue()" (ngModelChange)="onParentChange($event)" [itemToString]="parentToString">
                <hlm-select-trigger class="w-full">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content *hlmSelectPortal>
<hlm-select-group>
                  <hlm-select-item value="__none__">None (root)</hlm-select-item>
                  @for (parent of availableParents(); track parent.id) {
                    <hlm-select-item [value]="parent.id">{{ parent.path }}</hlm-select-item>
                  }
                </hlm-select-group>
</hlm-select-content>
              </hlm-select>
            </div>

            <hlm-field>
              <label hlmFieldLabel for="tag-description">Description</label>
              <textarea
                hlmInput
                id="tag-description"
                class="w-full"
                [value]="editDescription()"
                (input)="editDescription.set(asValue($event))"
                rows="3"
              ></textarea>
              <hlm-field-error>Description must be 512 characters or fewer.</hlm-field-error>
            </hlm-field>

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
          <button hlmBtn (click)="save()" [disabled]="!hasChanges() || nameInvalid() || descriptionInvalid() || saving()">
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

  private seedKey = computed(() =>
    `${this.tag()?.id ?? ''}|${this.state() === 'open'}`,
  );

  editName = seededLinkedSignal(this.seedKey, () => {
    const t = this.tag();
    if (!t || this.state() !== 'open') return '';
    return t.name;
  });
  editDescription = seededLinkedSignal(this.seedKey, () => {
    const t = this.tag();
    if (!t || this.state() !== 'open') return '';
    return t.description ?? '';
  });
  editParentId = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const t = this.tag();
    if (!t || this.state() !== 'open') return null;
    return t.parentId ?? null;
  });
  editColor = seededLinkedSignal<string, string | null>(this.seedKey, () => {
    const t = this.tag();
    if (!t || this.state() !== 'open') return null;
    return t.color ?? null;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);

  nameRequiredError = computed(() => this.editName().length === 0);
  namePatternError = computed(() => /\//.test(this.editName()));
  nameMaxLenError = computed(() => this.editName().length > 256);
  nameInvalid = computed(
    () => this.nameRequiredError() || this.namePatternError() || this.nameMaxLenError(),
  );
  descriptionInvalid = computed(() => this.editDescription().length > 512);

  parentSelectValue = computed(() => this.editParentId() ?? NONE_VALUE);

  parentToString = (value: string): string => {
    if (value === NONE_VALUE) return 'None (root)';
    return this.allTags().find(t => t.id === value)?.path ?? value;
  };

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
    if (this.editDescription() !== (t.description ?? '')) return true;
    if (this.editParentId() !== (t.parentId ?? null)) return true;
    if (this.editColor() !== (t.color ?? null)) return true;
    return false;
  });

  private tagApi = inject(TagApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
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
    if (this.editDescription() !== (t.description ?? '')) ops.push(replace('/description', this.editDescription() || null));
    if (this.editParentId() !== (t.parentId ?? null)) ops.push(replace('/parentId', this.editParentId()));
    if (this.editColor() !== (t.color ?? null)) ops.push(replace('/color', this.editColor()));

    this.tagApi.patchTag({
      id: t.id,
      body: ops,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to update tag.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Tag updated.');
        this.saved.emit();
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
