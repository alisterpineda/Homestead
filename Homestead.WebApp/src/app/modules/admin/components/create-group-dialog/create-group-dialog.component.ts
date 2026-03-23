import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output, viewChild } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { AdminApiService } from '@/api/services/admin-api.service';
import { toast } from 'ngx-sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-group-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmIconImports, HlmDialogImports, HlmButton, HlmInput, HlmLabel],
  providers: [provideIcons({ lucidePlus })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create Group</h2>
          <p hlmDialogDescription>Add a new group for document scoping.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label hlmLabel for="group-name">Name</label>
            <input hlmInput id="group-name" placeholder="Group name" [value]="name()"
              (input)="name.set(asInputValue($event))" required />
          </div>

          <div class="flex flex-col gap-2">
            <label hlmLabel for="group-desc">Description</label>
            <input hlmInput id="group-desc" placeholder="Optional description" [value]="description()"
              (input)="description.set(asInputValue($event))" />
          </div>

          @if (createError()) {
            <p class="text-sm text-destructive">{{ createError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || creating()">
              {{ creating() ? 'Creating...' : 'Create' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreateGroupDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon'>('default');
  created = output();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);
  private dialog = viewChild(BrnDialog);

  name = signal('');
  description = signal('');
  creating = signal(false);
  createError = signal('');

  canSubmit = computed(() => this.name().trim().length > 0);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.description.set('');
      this.creating.set(false);
      this.createError.set('');
    }
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.creating.set(true);
    this.createError.set('');

    this.adminApi.createGroup({
      body: {
        name: this.name().trim(),
        description: this.description().trim() || null,
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Group created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create group.'));
      },
    });
  }
}
