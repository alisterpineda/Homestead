import type { BadgeVariants } from '@/shared/components/spartan/badge';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';

export function statusBadgeVariant(status: ProcessingJobStatus): BadgeVariants['variant'] {
  switch (status) {
    case 'Pending': return 'outline';
    case 'InProgress': return 'default';
    case 'Completed': return 'secondary';
    case 'Failed': return 'destructive';
    case 'PartiallyCompleted': return 'outline';
  }
}

export function statusLabel(status: ProcessingJobStatus): string {
  switch (status) {
    case 'InProgress': return 'In Progress';
    case 'PartiallyCompleted': return 'Partially Completed';
    default: return status;
  }
}
