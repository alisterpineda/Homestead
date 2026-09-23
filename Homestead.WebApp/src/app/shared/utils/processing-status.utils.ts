import type { BadgeVariants } from '@/shared/components/spartan/badge';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';
import type { GetProcessingJobsResponse } from '@/api/generated/models/get-processing-jobs-response';

export function statusBadgeVariant(status: ProcessingJobStatus): BadgeVariants['variant'] {
  switch (status) {
    case 'Pending': return 'outline';
    case 'InProgress': return 'default';
    case 'Completed': return 'secondary';
    case 'Failed': return 'destructive';
    case 'PartiallyCompleted': return 'outline';
    // Terminal but not a failure — the operator cancelled it. Neutral, like Pending.
    case 'Cancelled': return 'outline';
  }
}

export function statusLabel(status: ProcessingJobStatus): string {
  switch (status) {
    case 'InProgress': return 'In Progress';
    case 'PartiallyCompleted': return 'Partially Completed';
    default: return status;
  }
}

type AttemptFields = Pick<GetProcessingJobsResponse, 'status' | 'attempts' | 'maxAttempts'>;

/**
 * The retry story of a non-parent job, or null when there is none to tell — any job type that has not
 * opted into retries, or one that succeeded first time. A Pending row with failed attempts is waiting to
 * retry; its due time (`runAfter`) is rendered beside this by the caller, so it can use the date pipe.
 */
export function attemptSummary(job: AttemptFields): string | null {
  const attempts = job.attempts ?? 0;
  const max = job.maxAttempts ?? 1;

  switch (job.status) {
    case 'Pending':
      return attempts > 0 ? `Retry ${attempts + 1}/${max}` : null;
    case 'Failed':
      return max > 1 ? `${attempts}/${max} attempts` : null;
    case 'Completed':
      return attempts > 0 ? `Succeeded on attempt ${attempts + 1}` : null;
    default:
      return null;
  }
}
