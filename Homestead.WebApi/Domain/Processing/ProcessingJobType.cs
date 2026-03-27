namespace Homestead.WebApi.Domain.Processing;

public enum ProcessingJobType
{
    UploadDocument,
    UploadDocumentVersion,
    ReprocessDocumentVersion,
    DownloadDocuments,
    BulkReprocessDocuments
}
