using System.IO.Compression;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Features.Download;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class DownloadDocumentsJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<DownloadDocumentsJobHandler> logger,
    IProcessingNotifier notifier,
    IDownloadPathBuilder pathBuilder) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.DownloadDocuments;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<DownloadDocumentsPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        DownloadCleanupHelper.CleanupExpired(mediaPath, logger);

        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => payload.DocumentIds.Contains(d.Id))
            .Select(d => new
            {
                d.Id,
                d.Name,
                TagPaths = d.DocumentTags.Select(dt => dt.Tag.Path).ToList(),
                PartyNames = d.DocumentParties.Select(dp => dp.Party.Name).ToList(),
                LatestVersion = d.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new { v.FilePath, v.FileExtension })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var zipPath = DownloadCleanupHelper.GetDownloadPath(mediaPath, job.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        var usedPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var documentCount = 0;

        await using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            foreach (var doc in documents)
            {
                if (doc.LatestVersion is null)
                {
                    logger.LogWarning("Document {DocumentId} has no versions, skipping", doc.Id);
                    continue;
                }

                var physicalPath = Path.Combine(mediaPath, doc.LatestVersion.FilePath);
                if (!File.Exists(physicalPath))
                {
                    logger.LogWarning("File not found for document {DocumentId}: {Path}, skipping", doc.Id, physicalPath);
                    continue;
                }

                var metadata = new DownloadDocumentMetadata(
                    doc.Name,
                    doc.LatestVersion.FileExtension,
                    doc.TagPaths,
                    doc.PartyNames);

                var archivePaths = pathBuilder.BuildPaths(metadata);

                foreach (var archivePath in archivePaths)
                {
                    var resolvedPath = ResolveCollision(archivePath, usedPaths);
                    var entry = archive.CreateEntry(resolvedPath, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var fileStream = File.OpenRead(physicalPath);
                    await fileStream.CopyToAsync(entryStream, ct);
                }

                documentCount++;
            }
        }

        if (documentCount == 0)
        {
            try { File.Delete(zipPath); } catch { }
            throw new InvalidOperationException("No documents could be downloaded — all files were missing or had no versions.");
        }

        var zipFileSize = new FileInfo(zipPath).Length;
        var expiresAt = DateTimeOffset.UtcNow + DownloadCleanupHelper.ExpirationPeriod;

        job.SetPayload(new DownloadResultPayload(zipFileSize, documentCount, expiresAt));

        await notifier.NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Download,
            entityId: null,
            documentName: $"Download ({documentCount} document{(documentCount != 1 ? "s" : "")})",
            error: null,
            job.CreatedByUserId,
            ct);
    }

    private static string ResolveCollision(string path, Dictionary<string, int> usedPaths)
    {
        if (!usedPaths.TryGetValue(path, out var count))
        {
            usedPaths[path] = 1;
            return path;
        }

        usedPaths[path] = count + 1;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, $"{name} ({count + 1}){ext}");
    }
}
