using System.Security.Cryptography;
using System.Text;
using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Domains.Auditing.Snapshots;

public record AppSettingSnapshot : IAuditSnapshot<AppSettingSnapshot, AppSetting>
{
    public required string Value { get; init; }

    public static AppSettingSnapshot From(AppSetting source) => new()
    {
        Value = source.Value
    };

    public List<FieldChange> Diff(AppSettingSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Value != other.Value)
            changes.Add(new ScalarFieldChange("Value", Value, other.Value));

        return changes;
    }

    public static Guid ModuleToGuid(string module) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(module)));
}
