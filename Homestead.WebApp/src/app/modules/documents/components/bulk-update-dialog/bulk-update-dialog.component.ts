import { Component, ChangeDetectionStrategy, inject, input, output, signal, computed, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTags } from '@ng-icons/lucide';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmLabel } from '@/shared/components/spartan/label';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { bulkUpdateDocuments } from '@/api/generated/fn/documents/bulk-update-documents';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { PartyInputComponent } from '../party-input/party-input.component';
import type { PartyOption } from '../party-input/party-input.component';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-bulk-update-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmDialogImports,
    HlmButton,
    HlmIconImports,
    HlmLabel,
    TagInputComponent,
    PartyInputComponent,
  ],
  providers: [provideIcons({ lucideTags })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" size="sm" hlmDialogTrigger>
        <ng-icon hlm name="lucideTags" size="sm" />
        Apply Tags / Parties
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Apply Tags &amp; Parties</h2>
          <p hlmDialogDescription>
            Add tags and parties to {{ selectedIds().size }} selected document{{ selectedIds().size > 1 ? 's' : '' }}.
            Existing tags and parties will not be removed.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Tags</label>
            <app-tag-input [value]="tagPaths()" (valueChange)="tagPaths.set($event)" />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Parties</label>
            <app-party-input [value]="parties()" (valueChange)="parties.set($event)" />
          </div>
          @if (applyError()) {
            <p class="text-sm text-destructive">Failed to update documents. Please try again.</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canApply() || applying()">
              {{ applying() ? 'Applying...' : 'Apply' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class BulkUpdateDialogComponent {
  selectedIds = input.required<Set<string>>();
  applied = output();

  tagPaths = signal<string[]>([]);
  parties = signal<PartyOption[]>([]);
  applying = signal(false);
  applyError = signal(false);

  canApply = computed(() => this.tagPaths().length > 0 || this.parties().length > 0);

  private dialog = viewChild(BrnDialog);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.tagPaths.set([]);
      this.parties.set([]);
      this.applying.set(false);
      this.applyError.set(false);
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canApply()) return;

    const ids = Array.from(this.selectedIds());
    this.applying.set(true);
    this.applyError.set(false);

    bulkUpdateDocuments(this.http, this.apiConfig.rootUrl, {
      body: {
        ids,
        tagPaths: this.tagPaths().length > 0 ? this.tagPaths() : null,
        partyIds: this.parties().length > 0 ? this.parties().map(p => p.id) : null,
      },
    }).subscribe({
      next: (response) => {
        this.applying.set(false);
        const count = +response.body.updatedCount;
        toast.success(`Updated ${count} document${count > 1 ? 's' : ''}.`);
        this.dialog()?.close();
        this.applied.emit();
      },
      error: () => {
        this.applying.set(false);
        this.applyError.set(true);
        toast.error('Failed to update documents. Please try again.');
      },
    });
  }
}
