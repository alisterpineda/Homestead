namespace Homestead.WebApi.Features.Documents.Thumbnails;

public interface IThumbnailGenerator
{
    bool CanGenerate(string contentType);
    Task<bool> GenerateAsync(string sourcePath, string outputPath, CancellationToken ct);
}
