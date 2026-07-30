import { formatRelativeAge } from '@/shared/utils/relative-age';
import type { BackupRunStatus } from '@/api/generated/models/backup-run-status';
import type { BackupScheduleSummary } from '@/api/generated/models/backup-schedule-summary';
import type { BackupTargetKind } from '@/api/generated/models/backup-target-kind';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';

/**
 * The vocabulary every backup surface renders through.
 *
 * Shared rather than copied because two surfaces describing one mechanism in two vocabularies is the
 * bug this file exists to prevent: `/admin/backup` answers "is this backup working" for a pairing, the
 * document detail page answers it for one file, and a reader who saw "Partial (3 failed)" on one and
 * "Some files failed" on the other would reasonably conclude they were looking at different runs.
 */

export function backupKindLabel(kind: BackupTargetKind | null | undefined): string {
  switch (kind) {
    case 'Local':
      return 'Local directory';
    case 'GoogleDrive':
      return 'Google Drive';
    default:
      return 'Unknown kind';
  }
}

export function backupStatusVariant(
  status: ProcessingJobStatus,
): 'default' | 'secondary' | 'destructive' | 'outline' {
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

export function backupStatusLabel(run: BackupRunStatus): string {
  switch (run.status) {
    case 'Completed':
      // A skipped run and a dry run both complete NORMALLY while mirroring nothing, and the server
      // already refuses to count either as a successful run. Labelling them plain "Completed" would put
      // a green badge directly beside "last successful run: 38 days ago" with nothing explaining the
      // contradiction — the reader's only clue that the backup is paused.
      if (run.summary?.skipped) return 'Skipped';
      if (run.summary?.dryRun) return 'Dry run';
      return 'Completed';
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

/**
 * The subset of a pairing both surfaces carry. Structural on purpose — the admin list and the
 * per-document panel receive different DTOs that agree on exactly these three fields.
 */
export interface BackupStalenessSource {
  isEnabled: boolean;
  lastSuccessfulRunAt?: string | null;
  schedule?: BackupScheduleSummary | null;

  /**
   * The toggle on the whole DESTINATION, where the caller knows it. Optional because the admin list's
   * per-source DTO does not carry it — the card renders target enablement one level up — while the
   * per-document panel does, and dropping it there would paint a deliberately switched-off destination
   * red for being exactly as stale as its operator intended.
   */
  targetEnabled?: boolean;
}

export function backupLastSuccessLabel(source: BackupStalenessSource): string {
  return source.lastSuccessfulRunAt ? formatRelativeAge(source.lastSuccessfulRunAt) : 'never';
}

const DEFAULT_STALENESS_MS = 7 * 24 * 60 * 60 * 1000;

function stalenessThresholdMs(schedule: BackupScheduleSummary): number {
  if (!schedule.lastRunAt) return DEFAULT_STALENESS_MS;

  const interval = new Date(schedule.nextRunAt).getTime() - new Date(schedule.lastRunAt).getTime();
  if (!Number.isFinite(interval) || interval <= 0) return DEFAULT_STALENESS_MS;

  return interval * 2;
}

/**
 * Whether the last success is old enough to be worth shouting about.
 *
 * Scaled to the schedule where one exists — roughly two missed runs — because "3 days ago" is alarming
 * for an hourly backup and unremarkable for a weekly one. Only flagged when a schedule is armed: a
 * manual-only backup is as stale as its owner intends.
 */
export function isBackupStale(source: BackupStalenessSource): boolean {
  const schedule = source.schedule;
  if (!schedule?.isEnabled || !source.isEnabled) return false;

  // `=== false`, not falsy: a caller that cannot see the destination toggle leaves it undefined, and
  // treating that as "switched off" would silence the staleness warning on the admin page entirely.
  if (source.targetEnabled === false) return false;

  const threshold = stalenessThresholdMs(schedule);

  if (!source.lastSuccessfulRunAt) {
    // Never succeeded, but only stale once a scheduled run has actually had time to happen.
    const armed = new Date(schedule.nextRunAt).getTime() - threshold;
    return Number.isFinite(armed) && Date.now() > armed;
  }

  const age = Date.now() - new Date(source.lastSuccessfulRunAt).getTime();
  return Number.isFinite(age) && age > threshold;
}
