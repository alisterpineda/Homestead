using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public class GetDownloadFileHandler(
    AppDbContext db,
    IConfiguration configuration,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetDownloadFileQuery, IResult>
{
    public async Task<IResult> Handle(GetDownloadFileQuery request, CancellationToken ct)
    {
        var job = await db.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct);

        if (job is null || job.Type != ProcessingJobType.DownloadDocuments)
            return Results.NotFound();

        if (job.CreatedByUserId != currentUser.UserId)
            return Results.NotFound();

        if (job.Status != ProcessingJobStatus.Completed)
            return Results.Problem(
                detail: "Download is not ready yet.",
                statusCode: StatusCodes.Status409Conflict);

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var zipPath = DownloadCleanupHelper.GetDownloadPath(mediaPath, job.Id);

        if (!File.Exists(zipPath))
            return Results.Problem(
                detail: "Download has expired. Please create a new download.",
                statusCode: StatusCodes.Status410Gone);

        var fileName = $"Homestead-Download-{job.CompletedAt?.UtcDateTime:yyyy-MM-dd}.zip";
        return Results.File(zipPath, "application/zip", fileName);
    }
}
