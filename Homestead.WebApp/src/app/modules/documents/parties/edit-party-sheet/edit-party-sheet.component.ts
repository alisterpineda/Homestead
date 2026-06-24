import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
} from '@angular/core';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { HlmFieldImports } from '@/shared/components/spartan/field';
import { PartyApiService } from '@/api/services/party-api.service';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-edit-party-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmTextareaImports,
    HlmFieldImports,
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
            <hlm-field>
              <label hlmLabel for="party-name">Name</label>
              <input
                hlmInput
                id="party-name"
                class="w-full"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
              <hlm-field-error>
                @if (nameRequiredError()) {
                  Party name is required.
                } @else if (nameMaxLenError()) {
                  Party name must be 256 characters or fewer.
                }
              </hlm-field-error>
            </hlm-field>

            <div class="space-y-2">
              <label hlmLabel for="party-desc">Description</label>
              <textarea
                hlmTextarea
                id="party-desc"
                rows="3"
                placeholder="No description"
                [value]="editDescription()"
                (input)="editDescription.set(asValue($event))"
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
          <button hlmBtn (click)="handleSave()" [disabled]="!hasChanges() || nameInvalid() || saving()">
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

  private seedKey = computed(() =>
    `${this.party()?.id ?? ''}|${this.state() === 'open'}`,
  );

  editName = seededLinkedSignal(this.seedKey, () => {
    const p = this.party();
    if (!p || this.state() !== 'open') return '';
    return p.name;
  });
  editDescription = seededLinkedSignal(this.seedKey, () => {
    const p = this.party();
    if (!p || this.state() !== 'open') return '';
    return p.description ?? '';
  });
  saving = seededLinkedSignal(this.seedKey, () => false);

  nameRequiredError = computed(() => this.editName().length === 0);
  nameMaxLenError = computed(() => this.editName().length > 256);
  nameInvalid = computed(() => this.nameRequiredError() || this.nameMaxLenError());

  hasChanges = computed(() => {
    const p = this.party();
    if (!p) return false;
    if (this.editName() !== p.name) return true;
    if (this.editDescription() !== (p.description ?? '')) return true;
    return false;
  });

  private partyApi = inject(PartyApiService);
  private errorHandler = inject(ErrorHandlerService);

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
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
