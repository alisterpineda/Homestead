using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AppUserSnapshot : IAuditSnapshot<AppUserSnapshot, AppUser>
{
    public string? Email { get; init; }
    public required string DisplayName { get; init; }
    public bool IsActive { get; init; }
    public string? Role { get; init; }
    public string? ExternalSubject { get; init; }

    public static AppUserSnapshot From(AppUser source) => new()
    {
        Email = source.Email,
        DisplayName = source.DisplayName,
        IsActive = source.IsActive,
        ExternalSubject = source.ExternalSubject
    };

    public static AppUserSnapshot From(AppUser source, string role) => new()
    {
        Email = source.Email,
        DisplayName = source.DisplayName,
        IsActive = source.IsActive,
        Role = role,
        ExternalSubject = source.ExternalSubject
    };

    public List<FieldChange> Diff(AppUserSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Email != other.Email)
            changes.Add(new ScalarFieldChange("Email", Email, other.Email));
        if (DisplayName != other.DisplayName)
            changes.Add(new ScalarFieldChange("DisplayName", DisplayName, other.DisplayName));
        if (IsActive != other.IsActive)
            changes.Add(new ScalarFieldChange("IsActive", IsActive.ToString(), other.IsActive.ToString()));
        if (Role != other.Role)
            changes.Add(new ScalarFieldChange("Role", Role, other.Role));
        if (ExternalSubject != other.ExternalSubject)
            changes.Add(new ScalarFieldChange("ExternalSubject", ExternalSubject, other.ExternalSubject));

        return changes;
    }
}
