import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
  signal,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, catchError, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { CategoryPickerComponent, type CategoryDirection } from '../../shared/category-picker/category-picker.component';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { FinancesPayeeMappingApiService } from '@/api/services/finances-payee-mapping-api.service';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { openGeneration } from '@/shared/utils/open-generation';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { PayeeResponse } from '@/api/generated/models/payee-response';
import type { PayeeMappingResponse } from '@/api/generated/models/payee-mapping-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-edit-payee-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmDialogImports, HlmButton, HlmInput, HlmLabel, HlmCheckboxImports,
    CategoryPickerComponent,
  ],
  providers: [provideIcons({ lucideX })],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Edit payee</h2>
          <p hlmDialogDescription>Update this payee's name, default category, and notes.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="edit-payee-name">Name</label>
            <input
              hlmInput
              id="edit-payee-name"
              maxlength="128"
              [value]="editName()"
              (input)="onNameInput($event)"
            />
          </div>

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
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="edit-payee-notes">Notes (optional)</label>
            <textarea
              hlmInput
              id="edit-payee-notes"
              rows="3"
              maxlength="1024"
              [value]="notes()"
              (input)="notes.set(asValue($event))"
            ></textarea>
          </div>

          <label class="flex items-center gap-2 text-sm">
            <hlm-checkbox
              [checked]="isArchived()"
              (checkedChange)="isArchived.set($event === true)"
              [aria-label]="'Archived'"
            />
            <span>Archived</span>
          </label>
          <p class="-mt-2 text-xs text-muted-foreground">Archived payees stay on old transactions but are hidden from pickers.</p>

          <!-- Learned raw-description → payee mappings (Phase 7). Prune any wrong/stale fingerprint. -->
          @if (mappings().length > 0) {
            <div class="flex flex-col gap-1.5">
              <label hlmLabel>Learned from these descriptions</label>
              <p class="text-xs text-muted-foreground">
                Statement lines that auto-fill this payee on import. Remove any that are wrong.
              </p>
              <ul class="flex max-h-40 flex-col gap-0.5 overflow-y-auto rounded-md border p-1">
                @for (m of mappings(); track m.id) {
                  <li class="flex items-center gap-2 rounded-sm px-2 py-1 text-sm">
                    <span class="min-w-0 flex-1 truncate" [title]="m.sampleRawDescription">{{ m.sampleRawDescription }}</span>
                    <span class="shrink-0 text-xs tabular-nums text-muted-foreground" title="Times confirmed">×{{ m.timesConfirmed }}</span>
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-xs"
                      type="button"
                      aria-label="Remove learned description"
                      [disabled]="removingId() === m.id"
                      (click)="removeMapping(m.id)"
                    >
                      <ng-icon name="lucideX" class="text-[length:--spacing(3)]" />
                    </button>
                  </li>
                }
              </ul>
            </div>
          }

          @if (duplicateError()) {
            <div class="flex flex-col items-start gap-1.5">
              <p class="text-sm text-destructive">A payee named "{{ editName().trim() }}" already exists.</p>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                [disabled]="merging()"
                (click)="mergeIntoExisting()"
              >
                {{ merging() ? 'Merging…' : 'Merge this payee into it' }}
              </button>
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
export class EditPayeeDialogComponent {
  payee = input<PayeeResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<PayeeResponse>();
  closed = output<void>();

  private openGen = openGeneration(this.state);
  private seedKey = computed(() => `${this.payee()?.id ?? ''}|${this.state() === 'open'}|${this.openGen()}`);

  private seedActive = computed(() => !!this.payee() && this.state() === 'open');

  editName = seededLinkedSignal(this.seedKey, () => (this.seedActive() ? this.payee()!.name : ''));
  direction = seededLinkedSignal<string, CategoryDirection>(this.seedKey, () =>
    this.seedActive() && this.payee()!.defaultCategoryType === 'Income' ? 'Income' : 'Expense');
  categoryId = seededLinkedSignal<string, string | null>(this.seedKey, () =>
    this.seedActive() ? (this.payee()!.defaultCategoryId ?? null) : null);
  notes = seededLinkedSignal(this.seedKey, () => (this.seedActive() ? (this.payee()!.notes ?? '') : ''));
  isArchived = seededLinkedSignal(this.seedKey, () => (this.seedActive() ? this.payee()!.isArchived : false));
  saving = seededLinkedSignal(this.seedKey, () => false);
  /** Set to true on a 409; cleared on reseed (reopen) or when the name is edited. */
  duplicateError = seededLinkedSignal<string, boolean>(this.seedKey, () => false);
  /** In-flight merge (rename→409→merge handoff). */
  merging = seededLinkedSignal(this.seedKey, () => false);

  private payeeApi = inject(FinancesPayeeApiService);
  private mappingApi = inject(FinancesPayeeMappingApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  canSubmit = computed(() => !!this.payee() && this.editName().trim().length > 0);

  // ─── Learned mappings (Phase 7) ─────────────────────────────────────────────
  private payeeIdWhenOpen = computed(() => (this.seedActive() ? this.payee()!.id : null));
  private mappingsRefresh = signal(0);
  removingId = signal<string | null>(null);

  // Fetched when the dialog is open for a payee; empty otherwise. Re-fetched after a delete.
  mappings = toSignal(
    combineLatest([toObservable(this.payeeIdWhenOpen), toObservable(this.mappingsRefresh)]).pipe(
      switchMap(([id]) =>
        id
          ? this.mappingApi.getPayeeMappings({ id }).pipe(catchError(() => of([] as PayeeMappingResponse[])))
          : of([] as PayeeMappingResponse[])),
    ),
    { initialValue: [] as PayeeMappingResponse[] },
  );

  removeMapping(id: string): void {
    this.removingId.set(id);
    this.mappingApi.deletePayeeMapping({ id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.removingId.set(null);
        this.mappingsRefresh.update((v) => v + 1);
        toast.success('Learned description removed.');
      },
      error: (err: unknown) => {
        this.removingId.set(null);
        this.errorHandler.handleError(err, 'Failed to remove learned description.');
      },
    });
  }

  // ─── Rename→409→merge handoff (Phase 6) ─────────────────────────────────────
  // The typed name collides with an existing payee; offer to merge THIS payee into that one. We look the
  // target up by its (now-known) name to get its id, then POST the merge.
  mergeIntoExisting(): void {
    const source = this.payee();
    const name = this.editName().trim();
    if (!source || !name) return;

    this.merging.set(true);
    this.payeeApi.getPayees({ Search: name, IncludeArchived: true, Page: 1, PageSize: 50 }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (res) => {
        const target = (res.items ?? []).find(
          (x) => x.id !== source.id && x.name.toLowerCase() === name.toLowerCase());
        if (!target) {
          this.merging.set(false);
          this.errorHandler.handleError(new Error('not found'), 'Could not find the payee to merge into.');
          return;
        }
        this.payeeApi.mergePayee({ id: source.id, body: { targetId: target.id } }).pipe(
          takeUntilDestroyed(this.destroyRef),
        ).subscribe({
          next: (merged) => {
            this.merging.set(false);
            toast.success('Payees merged.');
            this.saved.emit(merged);
          },
          error: (err: unknown) => {
            this.merging.set(false);
            this.errorHandler.handleError(err, 'Failed to merge payees.');
          },
        });
      },
      error: (err: unknown) => {
        this.merging.set(false);
        this.errorHandler.handleError(err, 'Failed to merge payees.');
      },
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  onNameInput(event: Event): void {
    this.editName.set((event.target as HTMLInputElement).value);
    if (this.duplicateError()) this.duplicateError.set(false);
  }

  // Category is direction-specific — switching drops the picked category so a wrong-direction id can't submit.
  setDirection(direction: CategoryDirection): void {
    if (this.direction() === direction) return;
    this.direction.set(direction);
    this.categoryId.set(null);
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const p = this.payee();
    if (!p || !this.canSubmit()) return;

    const name = this.editName().trim();

    this.saving.set(true);
    this.duplicateError.set(false);
    // Full-replace body (PatchCategory precedent — the whole editable state is sent, not a JSON patch).
    this.payeeApi.patchPayee({
      id: p.id,
      body: {
        name,
        defaultCategoryId: this.categoryId(),
        notes: this.notes().trim() ? this.notes().trim() : null,
        isArchived: this.isArchived(),
      },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Payee saved.');
        this.saved.emit(updated);
      },
      // A 409 means the name collides with an existing payee — surface it inline (no merge handoff). Others toast.
      error: (err: unknown) => {
        this.saving.set(false);
        if ((err as { status?: number }).status === 409) {
          this.duplicateError.set(true);
          return;
        }
        this.errorHandler.handleError(err, 'Failed to save payee.');
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
