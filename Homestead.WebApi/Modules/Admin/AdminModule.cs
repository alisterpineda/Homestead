using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;
using Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;
using Homestead.WebApi.Modules.Admin.Features.Backup.CreateBackupSource;
using Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSchedule;
using Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSource;
using Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;
using Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupSource;
using Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;
using Homestead.WebApi.Modules.Admin.Features.Backup.RunBackupNow;
using Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;
using Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;
using Homestead.WebApi.Modules.Admin.Features.Users.BulkDeleteUsers;
using Homestead.WebApi.Modules.Admin.Features.Groups.CreateGroup;
using Homestead.WebApi.Modules.Admin.Features.Users.CreateUser;
using Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;
using Homestead.WebApi.Modules.Admin.Features.Users.DeleteUser;
using Homestead.WebApi.Modules.Admin.Features.Groups.GetGroup;
using Homestead.WebApi.Modules.Admin.Features.Groups.GetGroups;
using Homestead.WebApi.Modules.Admin.Features.Users.GetUsers;
using Homestead.WebApi.Modules.Admin.Features.Groups.RemoveGroupMember;
using Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;
using Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;
using Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;
using Homestead.WebApi.Modules.Admin.Features.Schedules.DeleteProcessingSchedule;
using Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;
using Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;
using Homestead.WebApi.Modules.Admin.Features.Settings.GetSystemSettings;
using Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;
using Homestead.WebApi.Modules.Admin.Features.Users.UpdateUser;

namespace Homestead.WebApi.Modules.Admin;

public static class AdminModule
{
    public static IVersionedEndpointRouteBuilder MapAdminEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/admin/users")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetUsersEndpoint.Map(v1);
        CreateUserEndpoint.Map(v1);
        UpdateUserEndpoint.Map(v1);
        DeleteUserEndpoint.Map(v1);
        BulkDeleteUsersEndpoint.Map(v1);

        var groups = app.MapGroup("/api/v{version:apiVersion}/admin/groups")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        CreateGroupEndpoint.Map(groups);
        GetGroupsEndpoint.Map(groups);
        GetGroupEndpoint.Map(groups);
        UpdateGroupEndpoint.Map(groups);
        DeleteGroupEndpoint.Map(groups);
        AddGroupMemberEndpoint.Map(groups);
        UpdateGroupMemberEndpoint.Map(groups);
        RemoveGroupMemberEndpoint.Map(groups);

        var settings = app.MapGroup("/api/v{version:apiVersion}/admin/settings")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetSystemSettingsEndpoint.Map(settings);
        PatchSystemSettingsEndpoint.Map(settings);
        GetLogLevelsEndpoint.Map(settings);

        var schedules = app.MapGroup("/api/v{version:apiVersion}/admin/schedules")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetProcessingSchedulesEndpoint.Map(schedules);
        CreateProcessingScheduleEndpoint.Map(schedules);
        DeleteProcessingScheduleEndpoint.Map(schedules);

        var ai = app.MapGroup("/api/v{version:apiVersion}/admin/ai")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetAiStatusEndpoint.Map(ai);
        TestAiConnectionEndpoint.Map(ai);

        var backup = app.MapGroup("/api/v{version:apiVersion}/admin/backup")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetBackupTargetsEndpoint.Map(backup);
        PatchBackupTargetEndpoint.Map(backup);
        CreateBackupSourceEndpoint.Map(backup);
        PatchBackupSourceEndpoint.Map(backup);
        DeleteBackupSourceEndpoint.Map(backup);
        RunBackupNowEndpoint.Map(backup);
        UpsertBackupScheduleEndpoint.Map(backup);
        DeleteBackupScheduleEndpoint.Map(backup);

        return app;
    }
}
