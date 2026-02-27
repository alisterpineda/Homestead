using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocumentVersion;

public class UpdateDocumentVersionHandler(AppDbContext db)
    : ICommandHandler<UpdateDocumentVersionCommand, IResult>
{
    public async Task<IResult> HandleAsync(UpdateDocumentVersionCommand request, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return Results.NotFound();

        version.Label = request.Label;
        version.Notes = request.Notes;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new UpdateDocumentVersionResponse
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            Label = version.Label,
            Notes = version.Notes,
            ContentType = version.ContentType,
            FileSize = version.FileSize,
            FileExtension = version.FileExtension,
            CreatedAt = version.CreatedAt
        });
    }
}
