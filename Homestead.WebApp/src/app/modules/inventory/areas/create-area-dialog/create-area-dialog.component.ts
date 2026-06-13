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
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { AreaParentPickerComponent } from '../../shared/area-parent-picker/area-parent-picker.component';

@Component({
  selector: 'app-create-area-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel, AreaParentPickerComponent],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>New area</h2>
          <p hlmDialogDescription>A room or zone where items live, like "Garage" or "Pantry".</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="create-area-name">Name</label>
            <input
              hlmInput
              id="create-area-name"
              maxlength="128"
              placeholder="e.g. Garage"
              [value]="name()"
              (input)="name.set(asValue($event))"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Parent area</label>
            <app-area-parent-picker
              [value]="parentAreaId()"
              ariaLabel="Parent area"
              (valueChange)="parentAreaId.set($event)"
            />
            <p class="text-xs text-muted-foreground">Nest this area inside another, or leave as top-level.</p>
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create area' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateAreaDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  created = output();

  name = signal('');
  parentAreaId = signal<string | null>(null);
  creating = signal(false);
  createError = signal('');

  private dialog = viewChild(BrnDialog);
  private areaApi = inject(InventoryAreaApiService);
  private errorHandler = inject(ErrorHandlerService);

  canSubmit = computed(() => this.name().trim().length > 0);

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.parentAreaId.set(null);
      this.creating.set(false);
      this.createError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    this.areaApi.createArea({ body: { name: this.name().trim(), parentAreaId: this.parentAreaId() } }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Area created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create area.'));
      },
    });
  }
}
