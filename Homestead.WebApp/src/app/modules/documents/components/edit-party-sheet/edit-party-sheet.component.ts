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
import { HttpClient } from '@angular/common/http';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { updateParty } from '@/api/generated/fn/documents/update-party';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-edit-party-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmTextareaImports,
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
            <div class="space-y-2">
              <label hlmLabel for="party-name">Name</label>
              <input
                hlmInput
                id="party-name"
                class="w-full"
                [value]="editName()"
                (input)="editName.set(asInputValue($event))"
              />
            </div>

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
          <button hlmBtn (click)="handleSave()" [disabled]="!hasChanges() || saving()">
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

  editName = signal('');
  editDescription = signal('');
  saving = signal(false);

  hasChanges = computed(() => {
    const p = this.party();
    if (!p) return false;
    if (this.editName() !== p.name) return true;
    if (this.editDescription() !== (p.description ?? '')) return true;
    return false;
  });

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  constructor() {
    effect(() => {
      const p = this.party();
      if (p && this.state() === 'open') {
        this.editName.set(p.name);
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
    updateParty(this.http, this.apiConfig.rootUrl, {
      id: p.id,
      body: { name: this.editName(), description: this.editDescription() || null },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        toast.success('Party updated.');
        this.saved.emit();
      },
      error: () => {
        this.saving.set(false);
        toast.error('Failed to save changes.');
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
