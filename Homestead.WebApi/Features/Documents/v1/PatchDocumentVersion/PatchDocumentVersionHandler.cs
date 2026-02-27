using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocumentVersion;

public class PatchDocumentVersionHandler(AppDbContext db)
    : ICommandHandler<PatchDocumentVersionCommand, IResult>
{
    public async Task<IResult> HandleAsync(PatchDocumentVersionCommand request, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return Results.NotFound();

        // Populate model from current DB state
        var model = new PatchDocumentVersionModel
        {
            Label = version.Label,
            Notes = version.Notes
        };

        // Apply patch operations
        var errors = new List<JsonPatchError>();
        request.PatchDocument.ApplyTo(model, error => errors.Add(error));

        if (errors.Count > 0)
            return Results.BadRequest(new
            {
                Errors = errors.Select(e => new
                {
                    Operation = e.Operation?.ToString(),
                    e.ErrorMessage
                })
            });

        // Update entity from patched model
        version.Label = model.Label;
        version.Notes = model.Notes;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new PatchDocumentVersionResponse
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
