import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmFieldImports } from '@/shared/components/spartan/field';
import { TagApiService } from '@/api/services/tag-api.service';
import { ColorPickerComponent } from '@/shared/components/color-picker/color-picker.component';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-tag-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmFieldImports,
    ColorPickerComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" [class.hidden]="hideTrigger()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create Tag</h2>
          <p hlmDialogDescription>Use <code>/</code> to create nested tags (e.g., <code>finance/invoices</code>).</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <hlm-field>
            <label hlmFieldLabel for="create-tag-path">Path</label>
            <input
              hlmInput
              id="create-tag-path"
              placeholder="e.g., finance/invoices"
              [formControl]="pathControl"
            />
            <hlm-field-error>Tag path is required.</hlm-field-error>
          </hlm-field>
          <hlm-field>
            <label hlmFieldLabel for="create-tag-description">Description</label>
            <textarea
              hlmInput
              id="create-tag-description"
              placeholder="Optional description"
              [formControl]="descriptionControl"
              rows="2"
            ></textarea>
            <hlm-field-error>Description must be 512 characters or fewer.</hlm-field-error>
          </hlm-field>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Color</label>
            <app-color-picker [color]="selectedColor()" (colorChange)="selectedColor.set($event)" />
          </div>
          @if (createError()) {
            <p class="text-sm text-destructive">Failed to create tag. Please try again.</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="pathControl.invalid || descriptionControl.invalid || creating()">
              {{ creating() ? 'Creating...' : 'Create' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateTagDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  initialPath = input('');
  hideTrigger = input(false);
  created = output<{ path: string; effectiveColor: string | null }>();
  closed = output();

  pathControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  });
  descriptionControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(512)],
  });
  path = toSignal(this.pathControl.valueChanges, { initialValue: '' });
  selectedColor = signal<string | null>(null);
  creating = signal(false);
  createError = signal(false);

  private dialog = viewChild(BrnDialog);
  private tagApi = inject(TagApiService);
  private errorHandler = inject(ErrorHandlerService);

  open(): void {
    this.dialog()?.open();
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.pathControl.setValue(this.initialPath());
      this.pathControl.markAsPristine();
      this.pathControl.markAsUntouched();
      this.descriptionControl.setValue('');
      this.descriptionControl.markAsPristine();
      this.descriptionControl.markAsUntouched();
      this.selectedColor.set(null);
      this.creating.set(false);
      this.createError.set(false);
    } else {
      this.closed.emit();
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const pathVal = this.path().trim();
    if (!pathVal) return;

    this.creating.set(true);
    this.createError.set(false);

    const descVal = this.descriptionControl.value.trim() || null;

    this.tagApi.createTag({
      body: { path: pathVal, color: this.selectedColor(), description: descVal },
    }).subscribe({
      next: (response) => {
        this.creating.set(false);
        toast.success('Tag created.');
        this.dialog()?.close();
        this.created.emit({ path: response.path, effectiveColor: response.effectiveColor ?? null });
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(true);
      },
    });
  }
}
