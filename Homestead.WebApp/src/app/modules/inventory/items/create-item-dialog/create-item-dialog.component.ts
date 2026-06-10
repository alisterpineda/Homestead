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
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { AreaPickerComponent } from '../../shared/area-picker/area-picker.component';
import { tomorrowIso } from '../../shared/date';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-item-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel, AreaPickerComponent],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[560px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New item</h2>
          <p hlmDialogDescription>Add a tracked item. You can set value and currency after creating it.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-item-name">Name</label>
            <input
              hlmInput
              id="create-item-name"
              maxlength="256"
              placeholder="e.g. Cordless drill"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Area</label>
            <app-area-picker
              [value]="areaId()"
              (valueChange)="areaId.set($event)"
              ariaLabel="Area"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-item-serial">Serial (optional)</label>
            <input
              hlmInput
              id="create-item-serial"
              maxlength="128"
              placeholder="e.g. SN-12345"
              [value]="serial()"
              (input)="serial.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-item-acquired">Acquired on (optional)</label>
            <input
              hlmInput
              id="create-item-acquired"
              type="date"
              [max]="maxAcquiredDate"
              [value]="acquiredOn()"
              (input)="acquiredOn.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-item-notes">Notes (optional)</label>
            <textarea
              hlmInput
              id="create-item-notes"
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
              {{ creating() ? 'Creating...' : 'Create item' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateItemDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  name = signal('');
  areaId = signal<string | null>(null);
  serial = signal('');
  acquiredOn = signal('');
  notes = signal('');
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private itemApi = inject(InventoryItemApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly maxAcquiredDate = tomorrowIso();

  canSubmit = computed(() => this.name().trim().length > 0);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.areaId.set(null);
      this.serial.set('');
      this.acquiredOn.set('');
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

    this.itemApi.createItem({
      body: {
        name: this.name().trim(),
        areaId: this.areaId(),
        serial: this.serial().trim() ? this.serial().trim() : null,
        acquiredOn: this.acquiredOn() ? this.acquiredOn() : null,
        notes: this.notes().trim() ? this.notes().trim() : null,
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Item created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create item.'));
      },
    });
  }
}
