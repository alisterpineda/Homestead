import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmFieldImports } from '@/shared/components/spartan/field';
import { CategoryPickerComponent, type CategoryDirection } from '../../shared/category-picker/category-picker.component';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-payee-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmFieldImports,
    CategoryPickerComponent,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" [class.hidden]="hideTrigger()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create payee</h2>
          <p hlmDialogDescription>Add a counterparty you can attach to expenses and income.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <hlm-field>
            <label hlmFieldLabel for="create-payee-name">Name</label>
            <input
              hlmInput
              id="create-payee-name"
              maxlength="128"
              placeholder="e.g., Costco"
              [formControl]="nameControl"
            />
            <hlm-field-error>Payee name is required.</hlm-field-error>
          </hlm-field>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Default category (optional)</label>
            <div role="radiogroup" class="grid grid-cols-2 gap-2">
              <button
                type="button"
                role="radio"
                [attr.aria-checked]="direction() === 'Expense'"
                class="rounded-md border px-3 py-1.5 text-sm text-center transition-colors"
                [class]="direction() === 'Expense' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                (click)="setDirection('Expense')"
              >Expense</button>
              <button
                type="button"
                role="radio"
                [attr.aria-checked]="direction() === 'Income'"
                class="rounded-md border px-3 py-1.5 text-sm text-center transition-colors"
                [class]="direction() === 'Income' ? 'border-ring bg-accent text-accent-foreground' : 'border-input hover:bg-accent'"
                (click)="setDirection('Income')"
              >Income</button>
            </div>
            <app-category-picker
              [value]="categoryId()"
              [direction]="direction()"
              placeholder="No default category"
              clearLabel="No default category"
              ariaLabel="Select default category"
              (valueChange)="categoryId.set($event)"
            />
            <p class="text-xs text-muted-foreground">Autofills the category when you record a flow with this payee.</p>
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-payee-notes">Notes (optional)</label>
            <textarea
              hlmInput
              id="create-payee-notes"
              rows="3"
              maxlength="1024"
              placeholder="Anything worth remembering about this payee"
              [value]="notes()"
              (input)="notes.set(asValue($event))"
            ></textarea>
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
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
export class CreatePayeeDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  hideTrigger = input(false);
  created = output<void>();
  closed = output();

  nameControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(128)],
  });
  direction = signal<CategoryDirection>('Expense');
  categoryId = signal<string | null>(null);
  notes = signal('');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private payeeApi = inject(FinancesPayeeApiService);
  private errorHandler = inject(ErrorHandlerService);

  open(): void {
    this.dialog()?.open();
  }

  asValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  // Category is direction-specific — switching drops the picked category so a wrong-direction id can't submit.
  setDirection(direction: CategoryDirection): void {
    if (this.direction() === direction) return;
    this.direction.set(direction);
    this.categoryId.set(null);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.nameControl.setValue('');
      this.nameControl.markAsPristine();
      this.nameControl.markAsUntouched();
      this.direction.set('Expense');
      this.categoryId.set(null);
      this.notes.set('');
      this.creating.set(false);
      this.createError.set('');
    } else {
      this.closed.emit();
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const name = this.nameControl.value.trim();
    if (!name) return;

    this.creating.set(true);
    this.createError.set('');

    this.payeeApi.createPayee({
      body: {
        name,
        defaultCategoryId: this.categoryId(),
        notes: this.notes().trim() ? this.notes().trim() : null,
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Payee created.');
        this.dialog()?.close();
        this.created.emit();
      },
      // A 409 means the name collides — surface it inline (the open dialog would obscure a toast). Others toast.
      error: (err: unknown) => {
        this.creating.set(false);
        if ((err as { status?: number }).status === 409) {
          this.createError.set('A payee with this name already exists.');
          return;
        }
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create payee.'));
      },
    });
  }
}
