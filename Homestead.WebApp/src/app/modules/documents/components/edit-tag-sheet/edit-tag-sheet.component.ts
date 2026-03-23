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
import { ReactiveFormsModule, FormsModule, FormControl, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { TagApiService } from '@/api/services/tag-api.service';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { replace } from '@/shared/json-patch';
import { ColorPickerComponent } from '../color-picker/color-picker.component';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from 'ngx-sonner';

const NONE_VALUE = '__none__';

@Component({
  selector: 'app-edit-tag-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    FormsModule,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmFormFieldImports,
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
            <hlm-form-field>
              <label hlmLabel for="tag-name">Name</label>
              <input
                hlmInput
                id="tag-name"
                class="w-full"
                [formControl]="nameControl"
              />
              <hlm-hint>Cannot contain '/' characters.</hlm-hint>
              <hlm-error>
                @if (nameControl.hasError('required')) {
                  Tag name is required.
                } @else if (nameControl.hasError('pattern')) {
                  Tag name cannot contain '/' characters.
                } @else if (nameControl.hasError('maxlength')) {
                  Tag name must be 256 characters or fewer.
                }
              </hlm-error>
            </hlm-form-field>

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

            <hlm-form-field>
              <label hlmLabel for="tag-description">Description</label>
              <textarea
                hlmInput
                id="tag-description"
                class="w-full"
                [formControl]="descriptionControl"
                rows="3"
              ></textarea>
              <hlm-error>Description must be 512 characters or fewer.</hlm-error>
            </hlm-form-field>

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
          <button hlmBtn (click)="save()" [disabled]="!hasChanges() || nameControl.invalid || descriptionControl.invalid || saving()">
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

  nameControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/^[^/]*$/), Validators.maxLength(256)],
  });
  descriptionControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(512)],
  });
  editName = toSignal(this.nameControl.valueChanges, { initialValue: '' });
  editDescription = toSignal(this.descriptionControl.valueChanges, { initialValue: '' });
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
    if (this.editDescription() !== (t.description ?? '')) return true;
    if (this.editParentId() !== (t.parentId ?? null)) return true;
    if (this.editColor() !== (t.color ?? null)) return true;
    return false;
  });

  private tagApi = inject(TagApiService);
  private errorHandler = inject(ErrorHandlerService);

  constructor() {
    effect(() => {
      const t = this.tag();
      if (t && this.state() === 'open') {
        this.nameControl.setValue(t.name);
        this.nameControl.markAsPristine();
        this.nameControl.markAsUntouched();
        this.descriptionControl.setValue(t.description ?? '');
        this.descriptionControl.markAsPristine();
        this.descriptionControl.markAsUntouched();
        this.editParentId.set(t.parentId ?? null);
        this.editColor.set(t.color ?? null);
      }
    });
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
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Tag updated.');
        this.saved.emit();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (!this.errorHandler.isServerOrNetworkError(err)) {
          toast.error(this.errorHandler.getErrorMessage(err, 'Failed to update tag.'));
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
