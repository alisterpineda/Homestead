import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { createTag } from '@/api/generated/fn/documents/create-tag';
import { ColorPickerComponent } from '../color-picker/color-picker.component';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-create-tag-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    ColorPickerComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create Tag</h2>
          <p hlmDialogDescription>Use <code>/</code> to create nested tags (e.g., <code>finance/invoices</code>).</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-tag-path">Path</label>
            <input
              hlmInput
              id="create-tag-path"
              placeholder="e.g., finance/invoices"
              [value]="path()"
              (input)="path.set(asInputValue($event))"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Color</label>
            <app-color-picker [color]="selectedColor()" (colorChange)="selectedColor.set($event)" />
          </div>
          @if (createError()) {
            <p class="text-sm text-destructive">Failed to create tag. Please try again.</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!path().trim() || creating()">
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
  created = output();

  path = signal('');
  selectedColor = signal<string | null>(null);
  creating = signal(false);
  createError = signal(false);

  private dialog = viewChild(BrnDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.path.set('');
      this.selectedColor.set(null);
      this.creating.set(false);
      this.createError.set(false);
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const pathVal = this.path().trim();
    if (!pathVal) return;

    this.creating.set(true);
    this.createError.set(false);

    createTag(this.http, this.apiConfig.rootUrl, {
      body: { path: pathVal, color: this.selectedColor() },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Tag created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: () => {
        this.creating.set(false);
        this.createError.set(true);
        toast.error('Failed to create tag. Please try again.');
      },
    });
  }
}
