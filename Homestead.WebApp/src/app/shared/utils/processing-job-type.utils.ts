import type { ProcessingJobType } from '@/api/generated/models/processing-job-type';

export function processingJobTypeLabel(type: ProcessingJobType): string {
  switch (type) {
    case 'UploadDocument': return 'Upload Document';
    case 'UploadDocumentVersion': return 'Upload Version';
    case 'ReprocessDocumentVersion': return 'Reprocess Version';
    case 'BulkReprocessDocuments': return 'Bulk Reprocess';
    case 'DownloadDocuments': return 'Download Documents';
    case 'NoOp': return 'No-op';
    case 'ImportCommodityPrices': return 'Import commodity prices';
    default: return type;
  }
}
