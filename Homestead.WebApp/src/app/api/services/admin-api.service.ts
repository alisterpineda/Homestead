import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getUsers } from '@/api/generated/fn/admin/get-users';
import { createUser } from '@/api/generated/fn/admin/create-user';
import { updateUser } from '@/api/generated/fn/admin/update-user';
import { bulkDeleteUsers } from '@/api/generated/fn/admin/bulk-delete-users';
import { changeUserPassword } from '@/api/generated/fn/admin/change-user-password';
import { getGroups } from '@/api/generated/fn/admin/get-groups';
import { getGroup } from '@/api/generated/fn/admin/get-group';
import { createGroup } from '@/api/generated/fn/admin/create-group';
import { updateGroup } from '@/api/generated/fn/admin/update-group';
import { deleteGroup } from '@/api/generated/fn/admin/delete-group';
import { addGroupMember } from '@/api/generated/fn/admin/add-group-member';
import { updateGroupMember } from '@/api/generated/fn/admin/update-group-member';
import { removeGroupMember } from '@/api/generated/fn/admin/remove-group-member';
import { getTextExtractionEngines } from '@/api/generated/fn/admin/get-text-extraction-engines';
import { createTextExtractionEngine } from '@/api/generated/fn/admin/create-text-extraction-engine';
import { updateTextExtractionEngine } from '@/api/generated/fn/admin/update-text-extraction-engine';
import { bulkDeleteTextExtractionEngines } from '@/api/generated/fn/admin/bulk-delete-text-extraction-engines';
import { getDocumentSettings } from '@/api/generated/fn/admin/get-document-settings';
import { patchDocumentSettings } from '@/api/generated/fn/admin/patch-document-settings';
import { getProcessingJobs } from '@/api/generated/fn/processing/get-processing-jobs';
import type { PaginatedResponseOfUserResponse } from '@/api/generated/models/paginated-response-of-user-response';
import type { UserResponse } from '@/api/generated/models/user-response';
import type { BulkDeleteUsersResponse } from '@/api/generated/models/bulk-delete-users-response';
import type { PaginatedResponseOfGroupResponse } from '@/api/generated/models/paginated-response-of-group-response';
import type { GroupDetailResponse } from '@/api/generated/models/group-detail-response';
import type { GroupResponse } from '@/api/generated/models/group-response';
import type { GroupMemberResponse } from '@/api/generated/models/group-member-response';
import type { PaginatedResponseOfTextExtractionEngineResponse } from '@/api/generated/models/paginated-response-of-text-extraction-engine-response';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';
import type { BulkDeleteTextExtractionEnginesResponse } from '@/api/generated/models/bulk-delete-text-extraction-engines-response';
import type { DocumentSettingsResponse } from '@/api/generated/models/document-settings-response';
import type { PaginatedResponseOfGetProcessingJobsResponse } from '@/api/generated/models/paginated-response-of-get-processing-jobs-response';

export type { GetUsers$Params } from '@/api/generated/fn/admin/get-users';
export type { CreateUser$Params } from '@/api/generated/fn/admin/create-user';
export type { UpdateUser$Params } from '@/api/generated/fn/admin/update-user';
export type { BulkDeleteUsers$Params } from '@/api/generated/fn/admin/bulk-delete-users';
export type { ChangeUserPassword$Params } from '@/api/generated/fn/admin/change-user-password';
export type { GetGroups$Params } from '@/api/generated/fn/admin/get-groups';
export type { GetGroup$Params } from '@/api/generated/fn/admin/get-group';
export type { CreateGroup$Params } from '@/api/generated/fn/admin/create-group';
export type { UpdateGroup$Params } from '@/api/generated/fn/admin/update-group';
export type { DeleteGroup$Params } from '@/api/generated/fn/admin/delete-group';
export type { AddGroupMember$Params } from '@/api/generated/fn/admin/add-group-member';
export type { UpdateGroupMember$Params } from '@/api/generated/fn/admin/update-group-member';
export type { RemoveGroupMember$Params } from '@/api/generated/fn/admin/remove-group-member';
export type { GetTextExtractionEngines$Params } from '@/api/generated/fn/admin/get-text-extraction-engines';
export type { CreateTextExtractionEngine$Params } from '@/api/generated/fn/admin/create-text-extraction-engine';
export type { UpdateTextExtractionEngine$Params } from '@/api/generated/fn/admin/update-text-extraction-engine';
export type { BulkDeleteTextExtractionEngines$Params } from '@/api/generated/fn/admin/bulk-delete-text-extraction-engines';
export type { GetDocumentSettings$Params } from '@/api/generated/fn/admin/get-document-settings';
export type { PatchDocumentSettings$Params } from '@/api/generated/fn/admin/patch-document-settings';
export type { GetProcessingJobs$Params } from '@/api/generated/fn/processing/get-processing-jobs';

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

  changeUserPassword(params: Parameters<typeof changeUserPassword>[2]): Observable<void> {
    return changeUserPassword(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
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

  getTextExtractionEngines(params: Parameters<typeof getTextExtractionEngines>[2]): Observable<PaginatedResponseOfTextExtractionEngineResponse> {
    return getTextExtractionEngines(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createTextExtractionEngine(params: Parameters<typeof createTextExtractionEngine>[2]): Observable<TextExtractionEngineResponse> {
    return createTextExtractionEngine(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  updateTextExtractionEngine(params: Parameters<typeof updateTextExtractionEngine>[2]): Observable<TextExtractionEngineResponse> {
    return updateTextExtractionEngine(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteTextExtractionEngines(params: Parameters<typeof bulkDeleteTextExtractionEngines>[2]): Observable<BulkDeleteTextExtractionEnginesResponse> {
    return bulkDeleteTextExtractionEngines(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDocumentSettings(): Observable<DocumentSettingsResponse> {
    return getDocumentSettings(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  patchDocumentSettings(params: Parameters<typeof patchDocumentSettings>[2]): Observable<DocumentSettingsResponse> {
    return patchDocumentSettings(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getProcessingJobs(params: Parameters<typeof getProcessingJobs>[2]): Observable<PaginatedResponseOfGetProcessingJobsResponse> {
    return getProcessingJobs(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
