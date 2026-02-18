using System.Security.Cryptography;
using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Persistence;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocument;

public class CreateDocumentHandler(AppDbContext db, IConfiguration configuration)
    : ICommandHandler<CreateDocumentRequest, CreateDocumentResponse>
{
    public async Task<CreateDocumentResponse> HandleAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        var file = request.File;
        var extension = Path.GetExtension(file.FileName);
        var storedFileName = $"{Guid.NewGuid()}{extension}";

        var basePath = configuration["FileStorage:BasePath"]
            ?? throw new InvalidOperationException("FileStorage:BasePath is not configured.");
        var uploadsDir = Path.Combine(basePath, "documents");
        Directory.CreateDirectory(uploadsDir);

        var filePath = Path.Combine(uploadsDir, storedFileName);

        string fileHash;
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);

            stream.Position = 0;
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = request.Name ?? file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length,
            FilePath = Path.Combine("documents", storedFileName),
            FileHash = fileHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        return new CreateDocumentResponse { DocumentId = document.Id };
    }
}
