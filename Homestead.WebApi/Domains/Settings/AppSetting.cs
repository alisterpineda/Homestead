namespace Homestead.WebApi.Domains.Settings;

public class AppSetting
{
    public const string SystemModule = "System";
    public const string DocumentsModule = "Documents";

    public required string Module { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
