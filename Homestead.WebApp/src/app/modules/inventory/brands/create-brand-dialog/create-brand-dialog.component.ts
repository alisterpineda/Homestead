import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFieldImports } from '@/shared/components/spartan/field';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-brand-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmFieldImports,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" [class.hidden]="hideTrigger()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create brand</h2>
          <p hlmDialogDescription>Add a canonical brand you can attribute items to.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <hlm-field>
            <label hlmFieldLabel for="create-brand-name">Name</label>
            <input
              hlmInput
              id="create-brand-name"
              maxlength="128"
              placeholder="e.g., Apple"
              [formControl]="nameControl"
            />
            <hlm-field-error>Brand name is required.</hlm-field-error>
          </hlm-field>
          @if (createError()) {
            <p class="text-sm text-destructive">Failed to create brand. Please try again.</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="nameControl.invalid || creating()">
              {{ creating() ? 'Creating...' : 'Create' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateBrandDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  hideTrigger = input(false);
  created = output<void>();
  closed = output();

  nameControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(128)],
  });
  creating = signal(false);
  createError = signal(false);

  private dialog = viewChild(BrnDialog);
  private brandApi = inject(InventoryBrandApiService);
  private errorHandler = inject(ErrorHandlerService);

  open(): void {
    this.dialog()?.open();
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.nameControl.setValue('');
      this.nameControl.markAsPristine();
      this.nameControl.markAsUntouched();
      this.creating.set(false);
      this.createError.set(false);
    } else {
      this.closed.emit();
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const name = this.nameControl.value.trim();
    if (!name) return;

    this.creating.set(true);
    this.createError.set(false);

    this.brandApi.createBrand({ body: { name } }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Brand created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: () => {
        this.creating.set(false);
        this.createError.set(true);
      },
    });
  }
}
