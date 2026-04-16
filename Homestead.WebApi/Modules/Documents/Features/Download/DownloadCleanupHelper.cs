namespace Homestead.WebApi.Modules.Documents.Features.Download;

public static class DownloadCleanupHelper
{
    public const string DownloadsSubroot = "documents/downloads";
    public static readonly TimeSpan ExpirationPeriod = TimeSpan.FromHours(1);

    public static string GetDownloadPath(string mediaPath, Guid jobId)
        => Path.Combine(mediaPath, DownloadsSubroot, $"{jobId}.zip");

    public static void CleanupExpired(string mediaPath, ILogger logger)
    {
        var downloadsDir = Path.Combine(mediaPath, DownloadsSubroot);
        if (!Directory.Exists(downloadsDir))
            return;

        var cutoff = DateTimeOffset.UtcNow - ExpirationPeriod;

        foreach (var file in Directory.EnumerateFiles(downloadsDir, "*.zip"))
        {
            try
            {
                var created = File.GetCreationTimeUtc(file);
                if (created < cutoff.UtcDateTime)
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up expired download {Path}", file);
            }
        }
    }
}
