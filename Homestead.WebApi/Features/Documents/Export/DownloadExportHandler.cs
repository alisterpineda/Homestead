using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Export;

public class DownloadExportHandler(
    AppDbContext db,
    IConfiguration configuration,
    ICurrentUserAccessor currentUser) : IRequestHandler<DownloadExportQuery, IResult>
{
    public async Task<IResult> Handle(DownloadExportQuery request, CancellationToken ct)
    {
        var job = await db.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct);

        if (job is null || job.Type != ProcessingJobType.ExportDocuments)
            return Results.NotFound();

        if (job.CreatedByUserId != currentUser.UserId)
            return Results.NotFound();

        if (job.Status != ProcessingJobStatus.Completed)
            return Results.Problem(
                detail: "Export is not ready for download.",
                statusCode: StatusCodes.Status409Conflict);

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var zipPath = ExportCleanupHelper.GetExportPath(mediaPath, job.Id);

        if (!File.Exists(zipPath))
            return Results.Problem(
                detail: "Export file has expired. Please create a new export.",
                statusCode: StatusCodes.Status410Gone);

        var fileName = $"Homestead-Export-{job.CompletedAt?.UtcDateTime:yyyy-MM-dd}.zip";
        return Results.File(zipPath, "application/zip", fileName);
    }
}
