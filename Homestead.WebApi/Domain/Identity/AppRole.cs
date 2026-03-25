namespace Homestead.WebApi.Domain.Identity;

public enum AppRole { Admin, Contributor, Viewer, Guest }

public static class AppRoles
{
    /// <summary>All app role names in priority order (highest first).</summary>
    public static readonly string[] All = Enum.GetNames<AppRole>();

    public const string Default = nameof(AppRole.Contributor);

    /// <summary>Resolves the highest-priority app role from a list of role names.</summary>
    public static AppRole Resolve(IEnumerable<string> roleNames)
    {
        var primaryRole = All.FirstOrDefault(roleNames.Contains);
        return primaryRole is not null
            ? Enum.Parse<AppRole>(primaryRole)
            : AppRole.Guest;
    }
}
