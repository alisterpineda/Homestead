namespace Homestead.WebApi.Infrastructure.Thumbnails;

public interface IThumbnailGenerator
{
    bool CanGenerate(string contentType);
    Task<bool> GenerateAsync(string sourcePath, string outputPath, CancellationToken ct);
}
