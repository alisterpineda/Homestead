import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  viewChild,
} from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-supply-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[480px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New supply</h2>
          <p hlmDialogDescription>Define a consumable type. Add stock to it afterwards.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-supply-name">Name</label>
            <input
              hlmInput
              id="create-supply-name"
              maxlength="256"
              placeholder="e.g. Paper towels"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-supply-unit">Unit</label>
            <input
              hlmInput
              id="create-supply-unit"
              maxlength="32"
              placeholder="e.g. rolls"
              [value]="unit()"
              (input)="unit.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-supply-threshold">Restock threshold (optional)</label>
            <input
              hlmInput
              id="create-supply-threshold"
              type="number"
              step="0.001"
              min="0"
              placeholder="Flag as low at or below this total"
              [value]="threshold()"
              (input)="threshold.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-supply-notes">Notes (optional)</label>
            <textarea
              hlmInput
              id="create-supply-notes"
              rows="2"
              placeholder="Anything worth remembering"
              [value]="notes()"
              (input)="notes.set(asValue($event))"
            ></textarea>
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create supply' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateSupplyDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  name = signal('');
  unit = signal('');
  threshold = signal('');
  notes = signal('');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private supplyApi = inject(InventorySupplyApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() => {
    if (!this.name().trim() || !this.unit().trim()) return false;
    const t = this.threshold().trim();
    if (t !== '' && !(Number.isFinite(Number(t)) && Number(t) >= 0)) return false;
    return true;
  });

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.unit.set('');
      this.threshold.set('');
      this.notes.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    const t = this.threshold().trim();
    this.supplyApi.createSupply({
      body: {
        name: this.name().trim(),
        unit: this.unit().trim(),
        restockThreshold: t === '' ? null : Number(t),
        notes: this.notes().trim() ? this.notes().trim() : null,
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Supply created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create supply.'));
      },
    });
  }
}
