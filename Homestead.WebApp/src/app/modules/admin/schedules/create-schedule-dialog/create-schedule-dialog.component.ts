import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output, viewChild } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { AdminApiService } from '@/api/services/admin-api.service';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

@Component({
  selector: 'app-create-schedule-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmIconImports, HlmDialogImports, HlmButton, HlmInput, HlmLabel, HlmCheckboxImports],
  providers: [provideIcons({ lucidePlus })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Create Schedule</h2>
          <p hlmDialogDescription>Schedule a recurring No-op job to smoke-test the scheduler.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label hlmLabel for="schedule-name">Name</label>
            <input hlmInput id="schedule-name" placeholder="e.g. Hourly smoke test" [value]="name()"
              (input)="name.set(asInputValue($event))" required />
          </div>

          <div class="flex flex-col gap-2">
            <label hlmLabel for="schedule-cron">Cron Expression</label>
            <input hlmInput id="schedule-cron" placeholder="0 * * * *" [value]="cron()"
              (input)="cron.set(asInputValue($event))" required />
            <p class="text-xs text-muted-foreground">Standard 5-field cron (minute hour day month weekday). Example: <code>0 * * * *</code> runs every hour.</p>
          </div>

          <div class="flex flex-col gap-2">
            <label hlmLabel for="schedule-desc">Description</label>
            <input hlmInput id="schedule-desc" placeholder="Optional description" [value]="description()"
              (input)="description.set(asInputValue($event))" />
          </div>

          <div class="flex items-center gap-2">
            <hlm-checkbox id="schedule-enabled" [checked]="enabled()" (checkedChange)="enabled.set($event)"
              aria-label="Enabled" />
            <label hlmLabel for="schedule-enabled" class="cursor-pointer">Enabled</label>
          </div>

          <p class="text-xs text-muted-foreground">Job type: <span class="font-medium">No-op</span> (smoke test only)</p>

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
export class CreateScheduleDialogComponent {
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon'>('default');
  created = output();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);
  private dialog = viewChild(BrnDialog);

  name = signal('');
  cron = signal('');
  description = signal('');
  enabled = signal(true);
  creating = signal(false);
  createError = signal('');

  canSubmit = computed(() => this.name().trim().length > 0 && this.cron().trim().length > 0);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.name.set('');
      this.cron.set('');
      this.description.set('');
      this.enabled.set(true);
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

    const description = this.description().trim();
    this.adminApi.createProcessingSchedule({
      body: {
        name: this.name().trim(),
        cronExpression: this.cron().trim(),
        description: description || null,
        isEnabled: this.enabled(),
        jobType: 'NoOp',
      },
    }).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success('Schedule created.');
        this.dialog()?.close();
        this.created.emit();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.createError.set(this.errorHandler.getErrorMessage(err, 'Failed to create schedule.'));
      },
    });
  }
}
