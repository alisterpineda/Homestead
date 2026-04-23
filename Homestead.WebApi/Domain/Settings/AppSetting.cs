namespace Homestead.WebApi.Domain.Settings;

public class AppSetting
{
    public const string SystemModule = "system";
    public const string DocumentsModule = "documents";
    public const string FinancesModule = "finances";

    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
