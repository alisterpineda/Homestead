import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideDatabaseBackup,
  lucideTriangleAlert,
  lucideCalendarClock,
  lucidePlay,
  lucideEye,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmCheckbox } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { formatBytes } from '@/shared/utils';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';
import type { BackupTargetResponse } from '@/api/generated/models/backup-target-response';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';
import { BackupScheduleDialogComponent } from './backup-schedule-dialog.component';

@Component({
  selector: 'app-backup-target-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    DecimalPipe,
    NgIcon,
    HlmIconImports,
    HlmCardImports,
    HlmButton,
    HlmBadge,
    HlmCheckbox,
    HlmLabel,
    HlmSpinner,
    HlmEmptyImports,
    BackupScheduleDialogComponent,
  ],
  providers: [
    provideIcons({
      lucideDatabaseBackup,
      lucideTriangleAlert,
      lucideCalendarClock,
      lucidePlay,
      lucideEye,
    }),
  ],
  template: `
    <div class="mx-auto flex w-full max-w-3xl flex-col gap-4">
      @if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideDatabaseBackup" />
            </div>
            <div hlmEmptyTitle>Failed to load backup targets</div>
            <div hlmEmptyDescription>There was an error reading the backup configuration.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" (click)="load()">Retry</button>
          </div>
        </div>
      } @else if (targets(); as list) {
        @if (list.length === 0) {
          <div hlmEmpty data-testid="no-targets">
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideDatabaseBackup" />
              </div>
              <div hlmEmptyTitle>No backup targets configured</div>
              <div hlmEmptyDescription>
                Document backup is off. Define a target with
                <span class="font-mono">Backup:Targets:&#123;name&#125;:Kind</span> and
                <span class="font-mono">:Path</span> — in AppHost user-secrets, or as
                <span class="font-mono">Backup__Targets__&#8230;</span> environment variables in Docker.
              </div>
            </div>
          </div>
        } @else {
          <p class="text-sm text-muted-foreground">
            A one-way mirror of every document version onto a separate filesystem, laid out by year with
            human-readable names. Files are copied out; nothing is ever read back in.
          </p>

          @for (t of list; track t.key) {
            <div hlmCard [attr.data-testid]="'backup-target-' + t.key">
              <div hlmCardHeader>
                <div class="flex flex-wrap items-center gap-2">
                  <h2 hlmCardTitle class="font-mono text-base">{{ t.key }}</h2>
                  <span hlmBadge variant="secondary">{{ kindLabel(t.kind) }}</span>
                  @if (t.isAvailable) {
                    <span hlmBadge variant="default" data-testid="availability-badge">Available</span>
                  } @else {
                    <span
                      hlmBadge
                      variant="outline"
                      class="!text-muted-foreground"
                      data-testid="availability-badge"
                    >
                      Unavailable
                    </span>
                  }
                </div>
                @if (t.location) {
                  <p hlmCardDescription class="font-mono break-all">{{ t.location }}</p>
                }
              </div>

              <div hlmCardContent class="flex flex-col gap-4">
                @if (!t.isAvailable) {
                  <p class="text-sm text-destructive" data-testid="configuration-error">
                    {{ t.configurationError ?? 'This target is unavailable.' }}
                  </p>
                }

                @if (t.pruneBlockedAt) {
                  <div
                    class="flex flex-col gap-2 rounded-md border border-destructive/50 p-3 text-sm text-destructive"
                    data-testid="prune-blocked-banner"
                  >
                    <p class="flex items-start gap-2">
                      <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
                      <span>
                        <strong>{{ t.pruneBlockedCount }} deletion(s) were blocked</strong> on
                        {{ t.pruneBlockedAt | date: 'medium' }}. The run copied and moved everything else,
                        but withheld the deletions because they exceeded the bulk-delete guard. Prune only
                        if you expect the mirror to shrink this much.
                      </span>
                    </p>
                    <div class="flex flex-wrap items-center gap-2">
                      <button
                        hlmBtn
                        variant="destructive"
                        size="sm"
                        data-testid="prune-anyway"
                        [disabled]="!canRun(t)"
                        (click)="run(t, { allowLargePrune: true })"
                      >
                        Prune anyway
                      </button>
                      <button
                        hlmBtn
                        variant="outline"
                        size="sm"
                        data-testid="dismiss-prune"
                        [disabled]="busy()"
                        (click)="dismissPruneLatch(t)"
                      >
                        Dismiss
                      </button>
                    </div>
                  </div>
                }

                <div class="grid grid-cols-2 gap-3 text-sm">
                  <div>
                    <div class="text-xs uppercase tracking-wide text-muted-foreground">Files mirrored</div>
                    <div class="tabular-nums" data-testid="backed-up-count">
                      {{ t.backedUpFileCount | number }}
                    </div>
                  </div>
                  <div>
                    <div class="text-xs uppercase tracking-wide text-muted-foreground">Size</div>
                    <div class="tabular-nums">{{ formatBytes(t.backedUpBytes) }}</div>
                  </div>
                  <div>
                    <div class="text-xs uppercase tracking-wide text-muted-foreground">Last run</div>
                    <div class="flex items-center gap-2">
                      @if (t.lastRun; as run) {
                        <span hlmBadge [variant]="statusVariant(run.status)" data-testid="last-run-badge">
                          {{ statusLabel(run) }}
                        </span>
                        <span class="text-xs text-muted-foreground">
                          {{ (run.completedAt ?? run.createdAt) | date: 'short' }}
                        </span>
                      } @else {
                        <span hlmBadge variant="outline" data-testid="last-run-badge">Never run</span>
                      }
                    </div>
                  </div>
                  <div>
                    <div class="text-xs uppercase tracking-wide text-muted-foreground">Schedule</div>
                    <div>
                      @if (t.schedule; as s) {
                        <span class="font-mono text-xs">{{ s.cronExpression }}</span>
                        <span class="text-xs text-muted-foreground" data-testid="schedule-note">
                          @if (!s.isEnabled) {
                            · schedule disabled
                          } @else if (!t.isEnabled) {
                            <!-- The cron still fires, but the run skips — don't advertise a next run
                                 that will copy nothing. -->
                            · paused (target disabled)
                          } @else if (!t.isAvailable) {
                            · paused (target unavailable)
                          } @else {
                            · next {{ s.nextRunAt | date: 'short' }}
                          }
                        </span>
                      } @else {
                        <span class="text-muted-foreground">None</span>
                      }
                    </div>
                  </div>
                </div>

                @if (t.lastRun?.summary; as summary) {
                  @if (summary.skipped) {
                    <p class="text-sm text-muted-foreground" data-testid="last-run-summary">
                      Skipped — {{ summary.skipReason }}
                    </p>
                  } @else {
                    <!--
                      The summary is the PLAN, staged by the planner before any file moves. It equals the
                      outcome only when every action succeeded, so it may claim "applied" only for a run
                      that completed cleanly — otherwise a failed run would report copies that never
                      happened. "Files mirrored" above is always the outcome, read from the state table.
                    -->
                    <p class="text-sm text-muted-foreground" data-testid="last-run-summary">
                      {{ summaryVerb(t.lastRun!) }}: {{ summary.putCount ?? 0 }} to copy,
                      {{ summary.moveCount ?? 0 }} to move, {{ summary.deleteCount ?? 0 }} to remove,
                      {{ summary.unchangedCount ?? 0 }} unchanged.
                    </p>
                  }
                }

                @if (runFailures(t); as failures) {
                  @if (failures.length > 0) {
                    <div class="flex flex-col gap-1" data-testid="last-run-error">
                      @for (failure of failures; track $index) {
                        <p class="text-sm break-words text-destructive">{{ failure }}</p>
                      }
                    </div>
                  }
                }

                <div class="flex items-center gap-2">
                  <hlm-checkbox
                    [inputId]="'enabled-' + t.key"
                    [checked]="t.isEnabled"
                    [disabled]="busy()"
                    (checkedChange)="setEnabled(t, $event)"
                    [attr.data-testid]="'enabled-toggle-' + t.key"
                    aria-label="Enabled"
                  />
                  <label hlmLabel [for]="'enabled-' + t.key" class="cursor-pointer">Enabled</label>
                </div>

                <div class="flex flex-wrap items-center gap-2">
                  <button
                    hlmBtn
                    size="sm"
                    data-testid="back-up-now"
                    [disabled]="!canRun(t)"
                    (click)="run(t, {})"
                  >
                    <ng-icon hlm name="lucidePlay" size="sm" />
                    Back up now
                  </button>
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    data-testid="dry-run"
                    [disabled]="!canRun(t)"
                    (click)="run(t, { dryRun: true })"
                  >
                    <ng-icon hlm name="lucideEye" size="sm" />
                    Dry run
                  </button>

                  <app-backup-schedule-dialog
                    [targetKey]="t.key"
                    [schedule]="t.schedule ?? null"
                    (saved)="load()"
                  >
                    <ng-icon hlm name="lucideCalendarClock" size="sm" />
                    {{ t.schedule ? 'Edit schedule' : 'Add schedule' }}
                  </app-backup-schedule-dialog>

                  @if (t.schedule) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      data-testid="remove-schedule"
                      [disabled]="busy()"
                      (click)="removeSchedule(t)"
                    >
                      Remove schedule
                    </button>
                  }

                  @if (busy() && busyKey() === t.key) {
                    <hlm-spinner class="size-4" />
                  }
                </div>
              </div>
            </div>
          }
        }
      }
    </div>
  `,
})
export class BackupTargetListComponent implements OnInit {
  private api = inject(AdminBackupApiService);
  private errorHandler = inject(ErrorHandlerService);
  private hub = inject(ProcessingHubService);

  loading = signal(true);
  error = signal(false);
  targets = signal<BackupTargetResponse[] | null>(null);

  /** True while any mutation is in flight — gates every action so only one runs at a time. */
  busy = signal(false);

  /** Which target that mutation belongs to, so the spinner renders on the right card. */
  busyKey = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    this.api
      .listTargets()
      .pipe(handleQueryError(this.loading, this.error, null))
      .subscribe(response => {
        this.loading.set(false);
        if (response) this.targets.set(response.items);
      });
  }

  /** A run needs a destination that works and an operator who hasn't switched it off. */
  canRun(target: BackupTargetResponse): boolean {
    return target.isAvailable && target.isEnabled && !this.busy();
  }

  kindLabel(kind: BackupTargetResponse['kind']): string {
    switch (kind) {
      case 'Local':
        return 'Local directory';
      default:
        return 'Unknown kind';
    }
  }

  statusVariant(status: ProcessingJobStatus): 'default' | 'secondary' | 'destructive' | 'outline' {
    switch (status) {
      case 'Completed':
        return 'default';
      case 'Failed':
        return 'destructive';
      case 'PartiallyCompleted':
        return 'secondary';
      default:
        return 'outline';
    }
  }

  /**
   * How to introduce the staged change set. The planner stages it BEFORE any file moves, so it is a
   * plan; it is only also an outcome when the run completed with nothing failing.
   */
  summaryVerb(run: NonNullable<BackupTargetResponse['lastRun']>): string {
    if (run.summary?.dryRun) return 'Planned';
    return run.status === 'Completed' ? 'Applied' : 'Planned';
  }

  /**
   * Everything that went wrong in the last run: the parent's own error when the planner itself threw,
   * plus each failed batch's message. The batch messages are the only place per-file failures are
   * named — a parent whose children failed carries no error string of its own.
   */
  runFailures(target: BackupTargetResponse): string[] {
    const run = target.lastRun;
    if (!run) return [];

    return [run.error, ...(run.batchErrors ?? [])].filter((m): m is string => !!m);
  }

  statusLabel(run: NonNullable<BackupTargetResponse['lastRun']>): string {
    switch (run.status) {
      case 'Completed':
        return run.summary?.skipped ? 'Skipped' : 'Completed';
      case 'PartiallyCompleted':
        return `Partial (${run.failedChildCount} failed)`;
      case 'InProgress':
        return run.totalChildCount > 0
          ? `Running ${run.completedChildCount + run.failedChildCount}/${run.totalChildCount}`
          : 'Running';
      default:
        return run.status;
    }
  }

  // Delegates to the shared humanizer so byte formatting stays consistent with the rest of the app
  // (upload manager, document table, trash list). Guard the negative case the shared helper doesn't.
  formatBytes(bytes: number): string {
    return bytes <= 0 ? '0 B' : formatBytes(bytes);
  }

  setEnabled(target: BackupTargetResponse, isEnabled: boolean): void {
    this.patch(target, { isEnabled }, isEnabled ? 'Target enabled.' : 'Target disabled.');
  }

  dismissPruneLatch(target: BackupTargetResponse): void {
    // Any PATCH acknowledges the latch; sending the unchanged IsEnabled keeps this to a pure
    // acknowledgement rather than an accidental toggle.
    this.patch(target, { isEnabled: target.isEnabled }, 'Blocked-deletion warning dismissed.');
  }

  run(target: BackupTargetResponse, options: { dryRun?: boolean; allowLargePrune?: boolean }): void {
    const dryRun = options.dryRun ?? false;
    this.begin(target.key);

    this.api
      .runNow(target.key, { dryRun, allowLargePrune: options.allowLargePrune ?? false })
      .pipe(handleMutationError(this.errorHandler, this.busy, 'Failed to start the backup run.'))
      .subscribe(({ jobId }) => {
        this.end();
        toast.success(dryRun ? 'Dry run queued.' : 'Backup queued.');
        this.followRun(jobId, dryRun);
        this.load();
      });
  }

  /**
   * Claims the run's job on the processing hub and reports its outcome here.
   *
   * Claiming it is what suppresses the hub's generic completion toast, which is worded for document
   * uploads (`"<name>" has been processed successfully.`) and reads as `"Document" …` for a backup,
   * since a backup job carries no document name. Owning the message also lets the card refresh itself
   * when the run lands, instead of leaving the admin to reload the page to find out what happened.
   */
  private followRun(jobId: string, dryRun: boolean): void {
    this.hub.waitForJob(jobId).then(
      message => {
        if (message.status === 'PartiallyCompleted') {
          const failed = message.failedChildCount ?? 0;
          toast.warning(`Backup finished with ${failed} failed batch${failed === 1 ? '' : 'es'}.`);
        } else {
          toast.success(dryRun ? 'Dry run finished.' : 'Backup finished.');
        }
        this.load();
      },
      () => {
        // Deliberately not surfacing the rejection's own message: a parent whose children failed has no
        // error string, so it rejects with the hub's "An unknown error occurred." placeholder. The card
        // below carries the real per-file reasons, so point at that instead of echoing the placeholder.
        toast.error('The backup run failed — see the run details on the target below.');
        this.load();
      },
    );
  }

  removeSchedule(target: BackupTargetResponse): void {
    this.begin(target.key);

    this.api
      .deleteSchedule(target.key)
      .pipe(handleMutationError(this.errorHandler, this.busy, 'Failed to remove the schedule.'))
      .subscribe(() => {
        this.end();
        toast.success('Backup schedule removed.');
        this.load();
      });
  }

  private patch(
    target: BackupTargetResponse,
    body: { isEnabled?: boolean },
    successMessage: string,
  ): void {
    this.begin(target.key);

    this.api
      .patchTarget(target.key, body)
      .pipe(handleMutationError(this.errorHandler, this.busy, 'Failed to update the target.'))
      .subscribe(() => {
        this.end();
        toast.success(successMessage);
        this.load();
      });
  }

  private begin(key: string): void {
    this.busy.set(true);
    this.busyKey.set(key);
  }

  private end(): void {
    this.busy.set(false);
    this.busyKey.set(null);
  }
}
