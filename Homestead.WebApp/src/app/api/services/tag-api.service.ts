import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import { getTagsByIds } from '@/api/generated/fn/documents/get-tags-by-ids';
import { createTag } from '@/api/generated/fn/documents/create-tag';
import { patchTag } from '@/api/generated/fn/documents/patch-tag';
import { bulkDeleteTags } from '@/api/generated/fn/documents/bulk-delete-tags';
import type { PaginatedResponseOfGetTagsResponse } from '@/api/generated/models/paginated-response-of-get-tags-response';
import type { GetTagsByIdsResponse } from '@/api/generated/models/get-tags-by-ids-response';
import type { CreateTagResponse } from '@/api/generated/models/create-tag-response';
import type { PatchTagResponse } from '@/api/generated/models/patch-tag-response';
import type { BulkDeleteTagsResponse } from '@/api/generated/models/bulk-delete-tags-response';

export type { GetTags$Params } from '@/api/generated/fn/documents/get-tags';
export type { GetTagsByIds$Params } from '@/api/generated/fn/documents/get-tags-by-ids';
export type { CreateTag$Params } from '@/api/generated/fn/documents/create-tag';
export type { PatchTag$Params } from '@/api/generated/fn/documents/patch-tag';
export type { BulkDeleteTags$Params } from '@/api/generated/fn/documents/bulk-delete-tags';

@Injectable({ providedIn: 'root' })
export class TagApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getTags(params: Parameters<typeof getTags>[2]): Observable<PaginatedResponseOfGetTagsResponse> {
    return getTags(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getTagsByIds(params: Parameters<typeof getTagsByIds>[2]): Observable<Array<GetTagsByIdsResponse>> {
    return getTagsByIds(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createTag(params: Parameters<typeof createTag>[2]): Observable<CreateTagResponse> {
    return createTag(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchTag(params: Parameters<typeof patchTag>[2]): Observable<PatchTagResponse> {
    return patchTag(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteTags(params: Parameters<typeof bulkDeleteTags>[2]): Observable<BulkDeleteTagsResponse> {
    return bulkDeleteTags(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
