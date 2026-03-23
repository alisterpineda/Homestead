import { Component, ChangeDetectionStrategy, inject, input, output, signal, viewChild } from '@angular/core';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { PartyApiService } from '@/api/services/party-api.service';
import { toast } from 'ngx-sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-party-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    HlmDialogImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmFormFieldImports,
  ],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create Party</h2>
          <p hlmDialogDescription>Add a new party to organize your documents.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <hlm-form-field>
            <label hlmLabel for="create-party-name">Name</label>
            <input
              hlmInput
              id="create-party-name"
              placeholder="Enter a name for this party"
              [formControl]="nameControl"
            />
            <hlm-error>
              @if (nameControl.hasError('required')) {
                Party name is required.
              } @else if (nameControl.hasError('maxlength')) {
                Party name must be 256 characters or fewer.
              }
            </hlm-error>
          </hlm-form-field>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-party-desc">Description (optional)</label>
            <input
              hlmInput
              id="create-party-desc"
              placeholder="Enter a description"
              [value]="description()"
              (input)="description.set(asInputValue($event))"
            />
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
export class CreatePartyDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  nameControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(256)],
  });
  name = toSignal(this.nameControl.valueChanges, { initialValue: '' });
  description = signal('');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private partyApi = inject(PartyApiService);
  private errorHandler = inject(ErrorHandlerService);

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.nameControl.setValue('');
      this.nameControl.markAsPristine();
      this.nameControl.markAsUntouched();
      this.description.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const nameVal = this.name().trim();
    if (!nameVal) return;

    this.creating.set(true);
    this.createError.set('');

    this.partyApi.createParty({
      body: { name: nameVal, description: this.description().trim() || null },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Party created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create party.'));
      },
    });
  }
}
