import { Component, ChangeDetectionStrategy, computed, inject, signal, OnInit } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideDatabaseBackup,
  lucideTriangleAlert,
  lucideCalendarClock,
  lucidePlay,
  lucideEye,
  lucideTrash2,
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
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { formatBytes } from '@/shared/utils';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';
import type { BackupTargetResponse } from '@/api/generated/models/backup-target-response';
import type { BackupSourceItemResponse } from '@/api/generated/models/backup-source-item-response';
import type { BackupSourceKindResponse } from '@/api/generated/models/backup-source-kind-response';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';
import { BackupScheduleDialogComponent } from './backup-schedule-dialog.component';
import { AddBackupDialogComponent } from './add-backup-dialog.component';

/** What the delete confirmation is currently offering, which escalates as the server pushes back. */
type DeleteStage = 'confirm' | 'cancelRun' | 'retry';

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
    HlmAlertDialogImports,
    BackupScheduleDialogComponent,
    AddBackupDialogComponent,
  ],
  providers: [
    provideIcons({
      lucideDatabaseBackup,
      lucideTriangleAlert,
      lucideCalendarClock,
      lucidePlay,
      lucideEye,
      lucideTrash2,
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
                Backup is off. Define a target with
                <span class="font-mono">Backup:Targets:&#123;name&#125;:Kind</span> and
                <span class="font-mono">:Path</span> — in AppHost user-secrets, or as
                <span class="font-mono">Backup__Targets__&#8230;</span> environment variables in Docker.
              </div>
            </div>
          </div>
        } @else {
          <p class="text-sm text-muted-foreground">
            A one-way mirror onto a separate filesystem. Each dataset you add gets its own folder at the
            destination root and is laid out with human-readable names. Files are copied out; nothing is
            ever read back in.
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

                <div class="flex items-center gap-2">
                  <hlm-checkbox
                    [inputId]="'enabled-' + t.key"
                    [checked]="t.isEnabled"
                    [disabled]="busy()"
                    (checkedChange)="setTargetEnabled(t, $event)"
                    [attr.data-testid]="'enabled-toggle-' + t.key"
                    aria-label="Destination enabled"
                  />
                  <label hlmLabel [for]="'enabled-' + t.key" class="cursor-pointer">
                    Destination enabled
                  </label>
                </div>

                <!-- Per-source rows. Everything operational — enablement, schedule, the breaker latch —
                     belongs to the pairing, not to the destination it writes to. -->
                @if (t.sources.length === 0) {
                  <p class="text-sm text-muted-foreground" [attr.data-testid]="'no-sources-' + t.key">
                    Nothing is backed up here yet.
                  </p>
                } @else {
                  <div class="flex flex-col divide-y rounded-md border">
                    @for (s of t.sources; track s.id) {
                      <div class="flex flex-col gap-3 p-3" [attr.data-testid]="'backup-source-' + s.id">
                        <div class="flex flex-wrap items-center gap-2">
                          <span class="font-medium">{{ s.sourceDisplayName ?? s.sourceKind }}</span>
                          @if (!s.isAvailable) {
                            <span hlmBadge variant="outline" class="!text-muted-foreground">
                              Unavailable
                            </span>
                          } @else if (!s.isEnabled) {
                            <span
                              hlmBadge
                              variant="outline"
                              class="!text-muted-foreground"
                              [attr.data-testid]="'paused-badge-' + s.id"
                            >
                              Paused
                            </span>
                          } @else if (!t.isEnabled) {
                            <!-- Its own toggle is still on, so without saying which switch turned it off
                                 the row just reads as inexplicably dead. -->
                            <span
                              hlmBadge
                              variant="outline"
                              class="!text-muted-foreground"
                              [attr.data-testid]="'paused-badge-' + s.id"
                            >
                              Paused — destination disabled
                            </span>
                          }
                        </div>

                        @if (s.unavailableReason) {
                          <p
                            class="text-sm text-destructive"
                            [attr.data-testid]="'source-unavailable-' + s.id"
                          >
                            {{ s.unavailableReason }}
                          </p>
                        }

                        <!-- Only a kind that can actually delete can have withheld a deletion. -->
                        @if (s.supportsPrune && s.pruneBlockedAt) {
                          <div
                            class="flex flex-col gap-2 rounded-md border border-destructive/50 p-3 text-sm text-destructive"
                            [attr.data-testid]="'prune-blocked-banner-' + s.id"
                          >
                            <p class="flex items-start gap-2">
                              <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
                              <span>
                                <strong>{{ s.pruneBlockedCount }} deletion(s) were blocked</strong> on
                                {{ s.pruneBlockedAt | date: 'medium' }}. The run copied and moved
                                everything else, but withheld the deletions because they exceeded the
                                bulk-delete guard. Prune only if you expect the mirror to shrink this much.
                              </span>
                            </p>
                            <div class="flex flex-wrap items-center gap-2">
                              <button
                                hlmBtn
                                variant="destructive"
                                size="sm"
                                [attr.data-testid]="'prune-anyway-' + s.id"
                                [disabled]="!canRun(t, s)"
                                (click)="run(t, s, { allowLargePrune: true })"
                              >
                                Prune anyway
                              </button>
                              <button
                                hlmBtn
                                variant="outline"
                                size="sm"
                                [attr.data-testid]="'dismiss-prune-' + s.id"
                                [disabled]="busy()"
                                (click)="acknowledgePruneBlock(s)"
                              >
                                Dismiss
                              </button>
                            </div>
                          </div>
                        }

                        <div class="grid grid-cols-2 gap-3 text-sm">
                          <div>
                            <div class="text-xs uppercase tracking-wide text-muted-foreground">
                              Files mirrored
                            </div>
                            <div class="tabular-nums" [attr.data-testid]="'backed-up-count-' + s.id">
                              {{ s.backedUpFileCount | number }}
                            </div>
                          </div>
                          <div>
                            <div class="text-xs uppercase tracking-wide text-muted-foreground">Size</div>
                            <div class="tabular-nums">{{ formatBytes(s.backedUpBytes) }}</div>
                          </div>
                          <div>
                            <div class="text-xs uppercase tracking-wide text-muted-foreground">
                              Last run
                            </div>
                            <div class="flex items-center gap-2">
                              @if (s.lastRun; as run) {
                                <span
                                  hlmBadge
                                  [variant]="statusVariant(run.status)"
                                  [attr.data-testid]="'last-run-badge-' + s.id"
                                >
                                  {{ statusLabel(run) }}
                                </span>
                                <span class="text-xs text-muted-foreground">
                                  {{ run.completedAt ?? run.createdAt | date: 'short' }}
                                </span>
                              } @else {
                                <span hlmBadge variant="outline" [attr.data-testid]="'last-run-badge-' + s.id">
                                  Never run
                                </span>
                              }
                            </div>
                          </div>
                          <div>
                            <div class="text-xs uppercase tracking-wide text-muted-foreground">Schedule</div>
                            <div>
                              @if (s.schedule; as sched) {
                                <span class="font-mono text-xs">{{ sched.cronExpression }}</span>
                                <span
                                  class="text-xs text-muted-foreground"
                                  [attr.data-testid]="'schedule-note-' + s.id"
                                >
                                  @if (!sched.isEnabled) {
                                    · schedule disabled
                                  } @else if (!s.isEnabled) {
                                    <!-- The cron still fires, but the run skips — don't advertise a next
                                         run that will copy nothing. -->
                                    · paused (backup disabled)
                                  } @else if (!t.isEnabled) {
                                    · paused (destination disabled)
                                  } @else if (!s.isAvailable) {
                                    · paused (unavailable)
                                  } @else {
                                    · next {{ sched.nextRunAt | date: 'short' }}
                                  }
                                </span>
                              } @else {
                                <span class="text-muted-foreground">None</span>
                              }
                            </div>
                          </div>
                        </div>

                        @if (s.lastRun?.summary; as summary) {
                          @if (summary.skipped) {
                            <p
                              class="text-sm text-muted-foreground"
                              [attr.data-testid]="'last-run-summary-' + s.id"
                            >
                              Skipped — {{ summary.skipReason }}
                            </p>
                          } @else {
                            <!--
                              The summary is the PLAN, staged by the planner before any file moves. It
                              equals the outcome only when every action succeeded, so it may claim
                              "applied" only for a run that completed cleanly — otherwise a failed run
                              would report copies that never happened. "Files mirrored" above is always
                              the outcome, read from the state table.
                            -->
                            <p
                              class="text-sm text-muted-foreground"
                              [attr.data-testid]="'last-run-summary-' + s.id"
                            >
                              {{ summaryVerb(s.lastRun!) }}: {{ summary.puts ?? 0 }} to copy,
                              {{ summary.moves ?? 0 }} to move, {{ summary.deletes ?? 0 }} to remove,
                              {{ summary.unchanged ?? 0 }} unchanged ({{ summaryBytes(summary.totalBytes) }}).
                            </p>
                          }
                        }

                        @if (runFailures(s); as failures) {
                          @if (failures.length > 0) {
                            <div class="flex flex-col gap-1" [attr.data-testid]="'last-run-error-' + s.id">
                              @for (failure of failures; track $index) {
                                <p class="text-sm break-words text-destructive">{{ failure }}</p>
                              }
                            </div>
                          }
                        }

                        <div class="flex items-center gap-2">
                          <hlm-checkbox
                            [inputId]="'source-enabled-' + s.id"
                            [checked]="s.isEnabled"
                            [disabled]="busy()"
                            (checkedChange)="setSourceEnabled(s, $event)"
                            [attr.data-testid]="'source-enabled-toggle-' + s.id"
                            aria-label="Enabled"
                          />
                          <label hlmLabel [for]="'source-enabled-' + s.id" class="cursor-pointer">
                            Enabled
                          </label>
                        </div>

                        <div class="flex flex-wrap items-center gap-2">
                          <button
                            hlmBtn
                            size="sm"
                            [attr.data-testid]="'back-up-now-' + s.id"
                            [disabled]="!canRun(t, s)"
                            (click)="run(t, s, {})"
                          >
                            <ng-icon hlm name="lucidePlay" size="sm" />
                            Back up now
                          </button>
                          <button
                            hlmBtn
                            variant="outline"
                            size="sm"
                            [attr.data-testid]="'dry-run-' + s.id"
                            [disabled]="!canRun(t, s)"
                            (click)="run(t, s, { dryRun: true })"
                          >
                            <ng-icon hlm name="lucideEye" size="sm" />
                            Dry run
                          </button>

                          <app-backup-schedule-dialog
                            [sourceId]="s.id"
                            [sourceLabel]="s.sourceDisplayName ?? s.sourceKind"
                            [targetKey]="t.key"
                            [schedule]="s.schedule ?? null"
                            (saved)="load()"
                          >
                            <ng-icon hlm name="lucideCalendarClock" size="sm" />
                            {{ s.schedule ? 'Edit schedule' : 'Add schedule' }}
                          </app-backup-schedule-dialog>

                          @if (s.schedule) {
                            <button
                              hlmBtn
                              variant="ghost"
                              size="sm"
                              [attr.data-testid]="'remove-schedule-' + s.id"
                              [disabled]="busy()"
                              (click)="removeSchedule(s)"
                            >
                              Remove schedule
                            </button>
                          }

                          <hlm-alert-dialog (stateChanged)="onDeleteDialogState(s, $event)">
                            <button
                              hlmBtn
                              variant="ghost"
                              size="icon-sm"
                              hlmAlertDialogTrigger
                              [attr.data-testid]="'delete-source-' + s.id"
                              [attr.aria-label]="
                                'Remove ' + (s.sourceDisplayName ?? s.sourceKind) + ' backup'
                              "
                            >
                              <ng-icon hlm name="lucideTrash2" size="sm" />
                            </button>
                            <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                              <div hlmAlertDialogHeader>
                                <h2 hlmAlertDialogTitle>Remove this backup?</h2>
                                <p hlmAlertDialogDescription>
                                  @switch (deleteStage()) {
                                    @case ('cancelRun') {
                                      A run is in progress. Cancelling it stops the mirror where it is —
                                      whatever has already been copied stays on the destination, and
                                      re-adding this backup later re-copies from scratch.
                                    }
                                    @case ('retry') {
                                      A backup batch is still finishing — retry shortly.
                                    }
                                    @default {
                                      Homestead will forget what it has mirrored for
                                      <span class="font-medium">{{
                                        s.sourceDisplayName ?? s.sourceKind
                                      }}</span>
                                      and drop its schedule. Files already on the destination are left
                                      untouched; re-adding this backup later re-copies everything.
                                    }
                                  }
                                </p>
                              </div>
                              <div hlmAlertDialogFooter>
                                <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                                <button
                                  hlmAlertDialogAction
                                  variant="destructive"
                                  [attr.data-testid]="'confirm-delete-source-' + s.id"
                                  [disabled]="deleting()"
                                  (click)="deleteSource(s, ctx)"
                                >
                                  @if (deleting()) {
                                    Removing...
                                  } @else if (deleteStage() === 'cancelRun') {
                                    Cancel run and remove
                                  } @else if (deleteStage() === 'retry') {
                                    Retry
                                  } @else {
                                    Remove
                                  }
                                </button>
                              </div>
                            </hlm-alert-dialog-content>
                          </hlm-alert-dialog>

                          @if (busy() && busyKey() === s.id) {
                            <hlm-spinner class="size-4" />
                          }
                        </div>
                      </div>
                    }
                  </div>
                }

                <div>
                  <app-add-backup-dialog
                    [targetKey]="t.key"
                    [sourceKinds]="sourceKinds()"
                    [existingKinds]="existingKinds(t)"
                    (added)="load()"
                  />
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
  sourceKinds = signal<BackupSourceKindResponse[]>([]);

  /** True while any mutation is in flight — gates every action so only one runs at a time. */
  busy = signal(false);

  /** Which source that mutation belongs to, so the spinner renders on the right row. */
  busyKey = signal<string | null>(null);

  deleting = signal(false);

  /**
   * What the open delete dialog is offering. Starts as a plain confirmation and escalates when the
   * server refuses: a live run turns it into "cancel run and remove", and a batch we cannot interrupt
   * turns it into a retry.
   */
  deleteStage = signal<DeleteStage>('confirm');

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
        if (!response) return;
        this.targets.set(response.items);
        this.sourceKinds.set(response.sourceKinds);
      });
  }

  /**
   * A run needs a destination that works, a pairing that resolves, and an operator who hasn't switched
   * either of them off.
   */
  canRun(target: BackupTargetResponse, source: BackupSourceItemResponse): boolean {
    return (
      target.isAvailable && target.isEnabled && source.isAvailable && source.isEnabled && !this.busy()
    );
  }

  /**
   * Computed once per load rather than mapped per call: a template-invoked method returning a fresh
   * array hands the Add-backup dialog a new input identity on every change-detection pass, re-dirtying
   * an OnPush child (and re-running its `available()` computed) for data that never changed.
   */
  private existingKindsByTarget = computed(() => {
    const byKey = new Map<string, string[]>();
    for (const target of this.targets() ?? []) {
      byKey.set(
        target.key,
        target.sources.map(s => s.sourceKind),
      );
    }
    return byKey;
  });

  existingKinds(target: BackupTargetResponse): string[] {
    return this.existingKindsByTarget().get(target.key) ?? [];
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
  summaryVerb(run: NonNullable<BackupSourceItemResponse['lastRun']>): string {
    if (run.summary?.dryRun) return 'Planned';
    return run.status === 'Completed' ? 'Applied' : 'Planned';
  }

  /**
   * Null means the planner could not know the size at plan time (a kind that generates its content
   * rather than copying stored blobs). Render an em dash — "0 B" would read as "nothing to copy".
   */
  summaryBytes(totalBytes: number | null | undefined): string {
    return totalBytes == null ? '—' : this.formatBytes(totalBytes);
  }

  /**
   * Everything that went wrong in the last run: the parent's own error when the planner itself threw,
   * plus each failed batch's message. The batch messages are the only place per-file failures are
   * named — a parent whose children failed carries no error string of its own.
   */
  runFailures(source: BackupSourceItemResponse): string[] {
    const run = source.lastRun;
    if (!run) return [];

    return [run.error, ...(run.batchErrors ?? [])].filter((m): m is string => !!m);
  }

  statusLabel(run: NonNullable<BackupSourceItemResponse['lastRun']>): string {
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

  setTargetEnabled(target: BackupTargetResponse, isEnabled: boolean): void {
    this.begin(target.key);

    this.api
      .patchTarget(target.key, { isEnabled })
      .pipe(handleMutationError(this.errorHandler, this.busy, 'Failed to update the destination.'))
      .subscribe(() => {
        this.end();
        toast.success(isEnabled ? 'Destination enabled.' : 'Destination disabled.');
        this.load();
      });
  }

  setSourceEnabled(source: BackupSourceItemResponse, isEnabled: boolean): void {
    this.patchSource(source, { isEnabled }, isEnabled ? 'Backup enabled.' : 'Backup paused.');
  }

  acknowledgePruneBlock(source: BackupSourceItemResponse): void {
    // An explicit flag, not a side effect of any PATCH: the banner is the only place a blocked prune is
    // reported, so toggling the backup off and on again must not silently dismiss it.
    this.patchSource(
      source,
      { acknowledgePruneBlock: true },
      'Blocked-deletion warning dismissed.',
    );
  }

  run(
    target: BackupTargetResponse,
    source: BackupSourceItemResponse,
    options: { dryRun?: boolean; allowLargePrune?: boolean },
  ): void {
    const dryRun = options.dryRun ?? false;
    this.begin(source.id);

    this.api
      .runNow(source.id, { dryRun, allowLargePrune: options.allowLargePrune ?? false })
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
   * since a backup job carries no document name. Owning the message also lets the row refresh itself
   * when the run lands, instead of leaving the admin to reload the page to find out what happened.
   */
  private followRun(jobId: string, dryRun: boolean): void {
    this.hub.waitForJob(jobId).then(
      message => {
        if (message.status === 'Cancelled') {
          toast.warning('Backup run cancelled.');
        } else if (message.status === 'PartiallyCompleted') {
          const failed = message.failedChildCount ?? 0;
          toast.warning(`Backup finished with ${failed} failed batch${failed === 1 ? '' : 'es'}.`);
        } else {
          toast.success(dryRun ? 'Dry run finished.' : 'Backup finished.');
        }
        this.load();
      },
      () => {
        // Deliberately not surfacing the rejection's own message: a parent whose children failed has no
        // error string, so it rejects with the hub's "An unknown error occurred." placeholder. The row
        // below carries the real per-file reasons, so point at that instead of echoing the placeholder.
        toast.error('The backup run failed — see the run details below.');
        this.load();
      },
    );
  }

  removeSchedule(source: BackupSourceItemResponse): void {
    this.begin(source.id);

    this.api
      .deleteSchedule(source.id)
      .pipe(handleMutationError(this.errorHandler, this.busy, 'Failed to remove the schedule.'))
      .subscribe(() => {
        this.end();
        toast.success('Backup schedule removed.');
        this.load();
      });
  }

  onDeleteDialogState(source: BackupSourceItemResponse, state: 'open' | 'closed'): void {
    if (state !== 'open') return;

    // Always reopen at the plain confirmation: whether a run is live is the server's call at the moment
    // of the request, not something a stale escalation from a previous attempt should presume.
    this.deleteStage.set('confirm');
    this.deleting.set(false);
  }

  /**
   * Deletes the pairing, escalating rather than guessing. The first attempt never cancels anything; a
   * 409 is the server telling us a run is live, and only then does the dialog offer to cancel it.
   */
  deleteSource(source: BackupSourceItemResponse, ctx: { close: () => void }): void {
    const cancelRun = this.deleteStage() !== 'confirm';

    this.deleting.set(true);

    this.api.deleteSource(source.id, cancelRun || undefined).subscribe({
      next: () => {
        this.deleting.set(false);
        toast.success('Backup removed.');
        ctx.close();
        this.load();
      },
      error: (err: unknown) => {
        this.deleting.set(false);

        if (err instanceof HttpErrorResponse && err.status === 409) {
          // Two different 409s: "a run is queued, opt in to cancelling it" and "a batch is mid-flight,
          // nothing can cancel that — come back in a moment". The second only follows an attempt that
          // already opted in, so the stage we are in tells them apart.
          this.deleteStage.set(cancelRun ? 'retry' : 'cancelRun');
          return;
        }

        toast.error(this.errorHandler.getErrorMessage(err, 'Failed to remove the backup.'));
        ctx.close();
      },
    });
  }

  private patchSource(
    source: BackupSourceItemResponse,
    body: { isEnabled?: boolean; acknowledgePruneBlock?: boolean },
    successMessage: string,
  ): void {
    this.begin(source.id);

    this.api
      .patchSource(source.id, body)
      .pipe(handleMutationError(this.errorHandler, this.busy, 'Failed to update the backup.'))
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
