namespace Homestead.WebApi.Features.Documents.Export;

public static class ExportCleanupHelper
{
    public const string ExportsSubroot = "documents/exports";
    public static readonly TimeSpan ExpirationPeriod = TimeSpan.FromHours(1);

    public static string GetExportPath(string mediaPath, Guid jobId)
        => Path.Combine(mediaPath, ExportsSubroot, $"{jobId}.zip");

    public static void CleanupExpired(string mediaPath, ILogger logger)
    {
        var exportsDir = Path.Combine(mediaPath, ExportsSubroot);
        if (!Directory.Exists(exportsDir))
            return;

        var cutoff = DateTimeOffset.UtcNow - ExpirationPeriod;

        foreach (var file in Directory.EnumerateFiles(exportsDir, "*.zip"))
        {
            try
            {
                var created = File.GetCreationTimeUtc(file);
                if (created < cutoff.UtcDateTime)
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up expired export {Path}", file);
            }
        }
    }
}
