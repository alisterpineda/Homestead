using System.Security.Cryptography;
using HomeSuiteHome.WebApi.Authentication;
using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Persistence;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentHandler(AppDbContext db, IConfiguration configuration, ICurrentUserAccessor currentUser)
    : ICommandHandler<CreateDocumentRequest, CreateDocumentResponse>
{
    public async Task<CreateDocumentResponse> HandleAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        var file = request.File;
        var extension = Path.GetExtension(file.FileName);
        var documentId = Guid.NewGuid();

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");
        var uploadsDir = Path.Combine(mediaPath, Document.FilesSubroot);
        Directory.CreateDirectory(uploadsDir);

        var physicalPath = Path.Combine(uploadsDir, $"{documentId}{extension}");

        string fileHash;
        await using (var stream = new FileStream(physicalPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);

            stream.Position = 0;
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var userId = currentUser.User?.Id;

        var document = new Document
        {
            Id = documentId,
            Name = request.Name ?? file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length,
            FilePath = Document.GetFilePath(documentId, extension),
            FileHash = fileHash,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            OwnerUserId = userId
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        return new CreateDocumentResponse { DocumentId = document.Id };
    }
}
