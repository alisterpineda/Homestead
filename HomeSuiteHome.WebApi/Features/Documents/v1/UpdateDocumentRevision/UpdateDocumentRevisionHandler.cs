using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocumentRevision;

public class UpdateDocumentRevisionHandler(AppDbContext db)
    : ICommandHandler<UpdateDocumentRevisionCommand, IResult>
{
    public async Task<IResult> HandleAsync(UpdateDocumentRevisionCommand request, CancellationToken ct)
    {
        var revision = await db.DocumentRevisions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (revision is null)
            return Results.NotFound();

        revision.Label = request.Label;
        revision.Notes = request.Notes;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new UpdateDocumentRevisionResponse
        {
            Id = revision.Id,
            RevisionNumber = revision.RevisionNumber,
            Label = revision.Label,
            Notes = revision.Notes,
            ContentType = revision.ContentType,
            FileSize = revision.FileSize,
            FileExtension = revision.FileExtension,
            CreatedAt = revision.CreatedAt
        });
    }
}
