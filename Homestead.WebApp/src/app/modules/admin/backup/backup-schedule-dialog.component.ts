import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  viewChild,
} from '@angular/core';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BackupScheduleSummary } from '@/api/generated/models/backup-schedule-summary';

/**
 * Cron editor for one backup pairing's recurring run. Modelled on the admin create-schedule dialog;
 * upserts through the source-scoped endpoint rather than the generic schedules API, which deliberately
 * accepts only `NoOp`. The upsert updates in place, so editing a cron keeps the schedule's id.
 */
@Component({
  selector: 'app-backup-schedule-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmInput, HlmLabel, HlmCheckboxImports],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" size="sm" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>{{ schedule() ? 'Edit schedule' : 'Add schedule' }}</h2>
          <p hlmDialogDescription>
            Run <span class="font-medium">{{ sourceLabel() }}</span> &rarr;
            <span class="font-mono">{{ targetKey() }}</span> on a recurring cron.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label hlmLabel for="backup-cron">Cron expression</label>
            <input
              hlmInput
              id="backup-cron"
              placeholder="0 3 * * *"
              [value]="cron()"
              (input)="cron.set(asInputValue($event))"
              required
            />
            <p class="text-xs text-muted-foreground">
              Standard 5-field cron (minute hour day month weekday), in UTC.
              <code>0 3 * * *</code> runs daily at 03:00 UTC.
            </p>
          </div>

          <div class="flex items-center gap-2">
            <hlm-checkbox
              inputId="backup-schedule-enabled"
              [checked]="enabled()"
              (checkedChange)="enabled.set($event)"
              aria-label="Enabled"
            />
            <label hlmLabel for="backup-schedule-enabled" class="cursor-pointer">Enabled</label>
          </div>

          <p class="text-xs text-muted-foreground">
            Scheduled runs never bypass the bulk-delete guard — an unattended run can't prune the mirror
            wholesale.
          </p>

          @if (saveError()) {
            <p class="text-sm text-destructive">{{ saveError() }}</p>
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
export class BackupScheduleDialogComponent {
  sourceId = input.required<string>();
  sourceLabel = input.required<string>();
  targetKey = input.required<string>();
  schedule = input<BackupScheduleSummary | null>(null);
  saved = output();

  private api = inject(AdminBackupApiService);
  private errorHandler = inject(ErrorHandlerService);
  private dialog = viewChild(BrnDialog);

  cron = signal('');
  enabled = signal(true);
  saving = signal(false);
  saveError = signal('');

  canSubmit = computed(() => this.cron().trim().length > 0);

  onStateChanged(state: 'open' | 'closed'): void {
    if (state !== 'open') return;

    // Seed from the current schedule each time the dialog opens, so a cancelled edit never leaves stale
    // text behind and a refetch between opens is picked up.
    const existing = this.schedule();
    this.cron.set(existing?.cronExpression ?? '');
    this.enabled.set(existing?.isEnabled ?? true);
    this.saving.set(false);
    this.saveError.set('');
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.saving.set(true);
    this.saveError.set('');

    this.api
      .upsertSchedule(this.sourceId(), {
        cronExpression: this.cron().trim(),
        isEnabled: this.enabled(),
        priority: 0,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          toast.success('Backup schedule saved.');
          this.dialog()?.close();
          this.saved.emit();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.saveError.set(this.errorHandler.getErrorMessage(err, 'Failed to save schedule.'));
        },
      });
  }
}
