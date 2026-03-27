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
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { PartyApiService } from '@/api/services/party-api.service';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-edit-party-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmTextareaImports,
    HlmFormFieldImports,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)" (closed)="onClosed()">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit Party</h3>
          <p hlmSheetDescription>Update the party name and description.</p>
        </hlm-sheet-header>

        @if (party(); as p) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <hlm-form-field>
              <label hlmLabel for="party-name">Name</label>
              <input
                hlmInput
                id="party-name"
                class="w-full"
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

            <div class="space-y-2">
              <label hlmLabel for="party-desc">Description</label>
              <textarea
                hlmTextarea
                id="party-desc"
                rows="3"
                placeholder="No description"
                [value]="editDescription()"
                (input)="editDescription.set(asInputValue($event))"
              ></textarea>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Created</label>
              <p class="text-sm text-muted-foreground">
                {{ formatDate(p.createdAt) }}
              </p>
            </div>
          </div>
        }

        <hlm-sheet-footer>
          <button hlmBtn (click)="handleSave()" [disabled]="!hasChanges() || nameControl.invalid || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditPartySheetComponent {
  party = input<GetPartiesResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<void>();
  closed = output<void>();

  nameControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(256)],
  });
  editName = toSignal(this.nameControl.valueChanges, { initialValue: '' });
  editDescription = signal('');
  saving = signal(false);

  hasChanges = computed(() => {
    const p = this.party();
    if (!p) return false;
    if (this.editName() !== p.name) return true;
    if (this.editDescription() !== (p.description ?? '')) return true;
    return false;
  });

  private partyApi = inject(PartyApiService);
  private errorHandler = inject(ErrorHandlerService);

  constructor() {
    effect(() => {
      const p = this.party();
      if (p && this.state() === 'open') {
        this.nameControl.setValue(p.name);
        this.nameControl.markAsPristine();
        this.nameControl.markAsUntouched();
        this.editDescription.set(p.description ?? '');
      }
    });
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const p = this.party();
    if (!p) return;
    this.saving.set(true);
    this.partyApi.updateParty({
      id: p.id,
      body: { name: this.editName(), description: this.editDescription() || null },
    }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Party updated.');
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
