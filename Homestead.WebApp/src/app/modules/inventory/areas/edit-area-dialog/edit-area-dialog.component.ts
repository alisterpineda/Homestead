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
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { replace, type JsonPatchOperation } from '@/shared/json-patch';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { AreaParentPickerComponent } from '../../shared/area-parent-picker/area-parent-picker.component';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-edit-area-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel, AreaParentPickerComponent],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Edit area</h2>
          <p hlmDialogDescription>Rename this area or move it under a different parent.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="edit-area-name">Name</label>
            <input
              hlmInput
              id="edit-area-name"
              maxlength="128"
              [value]="editName()"
              (input)="editName.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Parent area</label>
            <app-area-parent-picker
              [value]="editParentId()"
              [excludeSubtreeOf]="area()?.id ?? null"
              [selectedLabel]="area()?.parentAreaName ?? null"
              ariaLabel="Parent area"
              (valueChange)="editParentId.set($event)"
            />
            <p class="text-xs text-muted-foreground">Moving an area takes its contents with it.</p>
          </div>

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
export class EditAreaDialogComponent {
  area = input<AreaResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<AreaResponse>();
  closed = output<void>();

  private seedKey = computed(() => `${this.area()?.id ?? ''}|${this.state() === 'open'}`);

  editName = seededLinkedSignal(this.seedKey, () => {
    const a = this.area();
    if (!a || this.state() !== 'open') return '';
    return a.name;
  });
  editParentId = seededLinkedSignal(this.seedKey, (): string | null => {
    const a = this.area();
    if (!a || this.state() !== 'open') return null;
    return a.parentAreaId ?? null;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);

  private areaApi = inject(InventoryAreaApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  canSubmit = computed(() => {
    const a = this.area();
    if (!a) return false;
    const name = this.editName().trim();
    if (name.length === 0) return false;
    const nameChanged = name !== a.name;
    const parentChanged = (this.editParentId() ?? null) !== (a.parentAreaId ?? null);
    return nameChanged || parentChanged;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const a = this.area();
    if (!a || !this.canSubmit()) return;

    const name = this.editName().trim();
    const parentId = this.editParentId() ?? null;
    const ops: JsonPatchOperation[] = [];
    if (name !== a.name) ops.push(replace('/name', name));
    if (parentId !== (a.parentAreaId ?? null)) ops.push(replace('/parentAreaId', parentId));
    if (ops.length === 0) return;

    this.saving.set(true);
    this.areaApi.patchArea({ id: a.id, body: ops }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.saving, 'Failed to save area.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Area saved.');
        this.saved.emit(updated);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
