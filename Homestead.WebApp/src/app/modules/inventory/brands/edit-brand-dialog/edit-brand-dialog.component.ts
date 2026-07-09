import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { replace, type JsonPatchOperation } from '@/shared/json-patch';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { openGeneration } from '@/shared/utils/open-generation';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

/** Payload for the rename-collision handoff: pre-seed the merge dialog targeting the existing brand. */
export interface BrandMergeRequest {
  readonly source: BrandResponse;
  readonly targetName: string;
}

@Component({
  selector: 'app-edit-brand-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Rename brand</h2>
          <p hlmDialogDescription>Give this brand a new canonical name.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="edit-brand-name">Name</label>
            <input
              hlmInput
              id="edit-brand-name"
              maxlength="128"
              [value]="editName()"
              (input)="onNameInput($event)"
            />
          </div>

          @if (duplicateConflict(); as existingName) {
            <div class="flex flex-col gap-2 rounded-md border border-border bg-muted/40 p-3">
              <p class="text-sm">
                A brand named "{{ existingName }}" already exists — merge into it?
              </p>
              <div>
                <button hlmBtn variant="outline" size="sm" type="button" (click)="onMergeInstead()">
                  Merge into "{{ existingName }}"
                </button>
              </div>
            </div>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || saving()">
              {{ saving() ? 'Saving...' : 'Save' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class EditBrandDialogComponent {
  brand = input<BrandResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<BrandResponse>();
  closed = output<void>();
  mergeRequested = output<BrandMergeRequest>();

  private openGen = openGeneration(this.state);
  private seedKey = computed(() => `${this.brand()?.id ?? ''}|${this.state() === 'open'}|${this.openGen()}`);

  editName = seededLinkedSignal(this.seedKey, () => {
    const b = this.brand();
    if (!b || this.state() !== 'open') return '';
    return b.name;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  /** Set to the colliding name on a 409; cleared on reseed (reopen) or when the name is edited. */
  duplicateConflict = seededLinkedSignal<string, string | null>(this.seedKey, () => null);

  private brandApi = inject(InventoryBrandApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  canSubmit = computed(() => {
    const b = this.brand();
    if (!b) return false;
    const name = this.editName().trim();
    return name.length > 0 && name !== b.name;
  });

  onNameInput(event: Event): void {
    this.editName.set((event.target as HTMLInputElement).value);
    // Editing the name invalidates a prior collision — clear it so the merge offer doesn't linger.
    if (this.duplicateConflict() !== null) this.duplicateConflict.set(null);
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const b = this.brand();
    if (!b || !this.canSubmit()) return;

    const name = this.editName().trim();
    const ops: JsonPatchOperation[] = [replace('/name', name)];

    this.saving.set(true);
    this.duplicateConflict.set(null);
    this.brandApi.patchBrand({ id: b.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Brand saved.');
        this.saved.emit(updated);
      },
      // A 409 means the name collides with an existing brand — rename can't consolidate, so offer the
      // merge handoff inline rather than a toast the open dialog would obscure. Other errors toast.
      error: (err: unknown) => {
        this.saving.set(false);
        if ((err as { status?: number }).status === 409) {
          this.duplicateConflict.set(name);
          return;
        }
        this.errorHandler.handleError(err, 'Failed to save brand.');
      },
    });
  }

  onMergeInstead(): void {
    const b = this.brand();
    const targetName = this.duplicateConflict();
    if (!b || !targetName) return;
    this.mergeRequested.emit({ source: b, targetName });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
