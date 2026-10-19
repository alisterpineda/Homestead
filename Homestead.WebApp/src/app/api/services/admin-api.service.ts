import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getUsers } from '@/api/generated/fn/admin/get-users';
import { createUser } from '@/api/generated/fn/admin/create-user';
import { updateUser } from '@/api/generated/fn/admin/update-user';
import { bulkDeleteUsers } from '@/api/generated/fn/admin/bulk-delete-users';
import { getGroups } from '@/api/generated/fn/admin/get-groups';
import { getGroup } from '@/api/generated/fn/admin/get-group';
import { createGroup } from '@/api/generated/fn/admin/create-group';
import { updateGroup } from '@/api/generated/fn/admin/update-group';
import { deleteGroup } from '@/api/generated/fn/admin/delete-group';
import { addGroupMember } from '@/api/generated/fn/admin/add-group-member';
import { updateGroupMember } from '@/api/generated/fn/admin/update-group-member';
import { removeGroupMember } from '@/api/generated/fn/admin/remove-group-member';
import { getProcessingJobs } from '@/api/generated/fn/processing/get-processing-jobs';
import { getProcessingSchedules } from '@/api/generated/fn/admin/get-processing-schedules';
import { createProcessingSchedule } from '@/api/generated/fn/admin/create-processing-schedule';
import { deleteProcessingSchedule } from '@/api/generated/fn/admin/delete-processing-schedule';
import { getSystemSettings } from '@/api/generated/fn/admin/get-system-settings';
import { patchSystemSettings } from '@/api/generated/fn/admin/patch-system-settings';
import { getLogLevels } from '@/api/generated/fn/admin/get-log-levels';
import { getAiStatus } from '@/api/generated/fn/admin/get-ai-status';
import { testAiConnection } from '@/api/generated/fn/admin/test-ai-connection';
import type { PaginatedResponseOfUserResponse } from '@/api/generated/models/paginated-response-of-user-response';
import type { UserResponse } from '@/api/generated/models/user-response';
import type { BulkDeleteUsersResponse } from '@/api/generated/models/bulk-delete-users-response';
import type { PaginatedResponseOfGroupResponse } from '@/api/generated/models/paginated-response-of-group-response';
import type { GroupDetailResponse } from '@/api/generated/models/group-detail-response';
import type { GroupResponse } from '@/api/generated/models/group-response';
import type { GroupMemberResponse } from '@/api/generated/models/group-member-response';
import type { PaginatedResponseOfGetProcessingJobsResponse } from '@/api/generated/models/paginated-response-of-get-processing-jobs-response';
import type { PaginatedResponseOfGetProcessingSchedulesResponse } from '@/api/generated/models/paginated-response-of-get-processing-schedules-response';
import type { CreateProcessingScheduleResponse } from '@/api/generated/models/create-processing-schedule-response';
import type { SystemSettingsResponse } from '@/api/generated/models/system-settings-response';
import type { LogLevelsResponse } from '@/api/generated/models/log-levels-response';
import type { AiStatusResponse } from '@/api/generated/models/ai-status-response';
import type { TestAiConnectionResponse } from '@/api/generated/models/test-ai-connection-response';

export type { GetUsers$Params } from '@/api/generated/fn/admin/get-users';
export type { CreateUser$Params } from '@/api/generated/fn/admin/create-user';
export type { UpdateUser$Params } from '@/api/generated/fn/admin/update-user';
export type { BulkDeleteUsers$Params } from '@/api/generated/fn/admin/bulk-delete-users';
export type { GetGroups$Params } from '@/api/generated/fn/admin/get-groups';
export type { GetGroup$Params } from '@/api/generated/fn/admin/get-group';
export type { CreateGroup$Params } from '@/api/generated/fn/admin/create-group';
export type { UpdateGroup$Params } from '@/api/generated/fn/admin/update-group';
export type { DeleteGroup$Params } from '@/api/generated/fn/admin/delete-group';
export type { AddGroupMember$Params } from '@/api/generated/fn/admin/add-group-member';
export type { UpdateGroupMember$Params } from '@/api/generated/fn/admin/update-group-member';
export type { RemoveGroupMember$Params } from '@/api/generated/fn/admin/remove-group-member';
export type { GetProcessingJobs$Params } from '@/api/generated/fn/processing/get-processing-jobs';
export type { GetProcessingSchedules$Params } from '@/api/generated/fn/admin/get-processing-schedules';
export type { CreateProcessingSchedule$Params } from '@/api/generated/fn/admin/create-processing-schedule';
export type { DeleteProcessingSchedule$Params } from '@/api/generated/fn/admin/delete-processing-schedule';
export type { GetSystemSettings$Params } from '@/api/generated/fn/admin/get-system-settings';
export type { PatchSystemSettings$Params } from '@/api/generated/fn/admin/patch-system-settings';
export type { GetLogLevels$Params } from '@/api/generated/fn/admin/get-log-levels';
export type { GetAiStatus$Params } from '@/api/generated/fn/admin/get-ai-status';
export type { TestAiConnection$Params } from '@/api/generated/fn/admin/test-ai-connection';

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getUsers(params: Parameters<typeof getUsers>[2]): Observable<PaginatedResponseOfUserResponse> {
    return getUsers(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createUser(params: Parameters<typeof createUser>[2]): Observable<UserResponse> {
    return createUser(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  updateUser(params: Parameters<typeof updateUser>[2]): Observable<UserResponse> {
    return updateUser(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteUsers(params: Parameters<typeof bulkDeleteUsers>[2]): Observable<BulkDeleteUsersResponse> {
    return bulkDeleteUsers(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getGroups(params: Parameters<typeof getGroups>[2]): Observable<PaginatedResponseOfGroupResponse> {
    return getGroups(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getGroup(params: Parameters<typeof getGroup>[2]): Observable<GroupDetailResponse> {
    return getGroup(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createGroup(params: Parameters<typeof createGroup>[2]): Observable<GroupResponse> {
    return createGroup(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  updateGroup(params: Parameters<typeof updateGroup>[2]): Observable<GroupResponse> {
    return updateGroup(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteGroup(params: Parameters<typeof deleteGroup>[2]): Observable<void> {
    return deleteGroup(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  addGroupMember(params: Parameters<typeof addGroupMember>[2]): Observable<GroupMemberResponse> {
    return addGroupMember(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  updateGroupMember(params: Parameters<typeof updateGroupMember>[2]): Observable<GroupMemberResponse> {
    return updateGroupMember(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  removeGroupMember(params: Parameters<typeof removeGroupMember>[2]): Observable<void> {
    return removeGroupMember(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getProcessingJobs(params: Parameters<typeof getProcessingJobs>[2]): Observable<PaginatedResponseOfGetProcessingJobsResponse> {
    return getProcessingJobs(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getProcessingSchedules(params: Parameters<typeof getProcessingSchedules>[2]): Observable<PaginatedResponseOfGetProcessingSchedulesResponse> {
    return getProcessingSchedules(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createProcessingSchedule(params: Parameters<typeof createProcessingSchedule>[2]): Observable<CreateProcessingScheduleResponse> {
    return createProcessingSchedule(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteProcessingSchedule(params: Parameters<typeof deleteProcessingSchedule>[2]): Observable<void> {
    return deleteProcessingSchedule(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getSystemSettings(params?: Parameters<typeof getSystemSettings>[2]): Observable<SystemSettingsResponse> {
    return getSystemSettings(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchSystemSettings(params: Parameters<typeof patchSystemSettings>[2]): Observable<SystemSettingsResponse> {
    return patchSystemSettings(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getLogLevels(category?: string): Observable<LogLevelsResponse> {
    return getLogLevels(this.http, this.config.rootUrl, category ? { category } : undefined).pipe(map(r => r.body));
  }

  getAiStatus(): Observable<AiStatusResponse> {
    return getAiStatus(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  testAiConnection(): Observable<TestAiConnectionResponse> {
    return testAiConnection(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }
}
