import type { BadgeVariants } from '@/shared/components/spartan/badge';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';

export function statusBadgeVariant(status: ProcessingJobStatus): BadgeVariants['variant'] {
  switch (status) {
    case 'Pending': return 'outline';
    case 'InProgress': return 'default';
    case 'Completed': return 'secondary';
    case 'Failed': return 'destructive';
  }
}

export function statusLabel(status: ProcessingJobStatus): string {
  return status === 'InProgress' ? 'In Progress' : status;
}
